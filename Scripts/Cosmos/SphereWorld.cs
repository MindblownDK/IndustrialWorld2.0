// Assets/Scripts/VoxelEngine/Cosmos/SphereWorld.cs
//
// The spherical voxel engine — Phase 2's "open world" runtime.
//
// Architecturally it is the spherical twin of Core.VoxelWorld: same Chunk / ChunkPool /
// SurfaceNetsJob / ChunkStorage / FluidManager / ChunkScatter pipeline. The differences are
// deliberately minimal and are exactly what "planet-shaped" demands:
//
//   1. CHUNKS ARE PARENTED TO THE BODY. Every chunk GameObject is a child of the active
//      CelestialBody, so the whole terrain mass follows the body (and sits body-local).
//      The chunk's local position is its cartesian grid offset from the body core.
//
//   2. GENERATION IS RADIAL. Instead of ChunkHeightJob + ChunkGenJob (a heightmap), we run
//      SphereChunkGenJob, which evaluates SphereDensity at each voxel's body-local cartesian
//      position. Solid = below the terrain surface RADIUS; oceans fill below the sea RADIUS;
//      ores come from clustered VeinNoise pockets. The mesher (SurfaceNetsJob) is unchanged —
//      it just meshes whatever voxels exist, so all the smooth-terrain / fluid visuals port over.
//
//   3. STREAMING IS BODY-RELATIVE. The viewer's position is converted into the body's local
//      space each frame; chunks stream around that point exactly like the flat streamer.
//
// This keeps ~90% of the engine (jobs, meshing, fluids, mining, persistence, scatter) intact
// while turning the world into a true minable sphere. Radial gravity is provided separately by
// GravityProvider (consumed by the player / atmosphere), not by this class.
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Biomes;
using VoxelEngine.Core;
using VoxelEngine.Generation;
using VoxelEngine.Materials;
using VoxelEngine.Meshing;
using VoxelEngine.Persistence;
using VoxelEngine.Pooling;
using VoxelEngine.Scattering;
using VoxelEngine.WaterSim;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Shared contract for anything that owns voxel chunks and can be scattered on.
    /// Lets ChunkScatter work against the flat VoxelWorld AND the spherical SphereWorld
    /// without knowing which one it's talking to.
    /// </summary>
    public interface IChunkScatterWorld
    {
        bool TryGetChunk(Vector3Int coord, out Chunk chunk);
        /// <summary>Voxel-space sea level (water fills below this).</summary>
        int SeaLevel { get; }
    }

    /// <summary>
    /// Streams a spherical voxel body around a viewer. Attach to a manager GameObject and
    /// assign the active <see cref="CelestialBody"/> (typically the body this streamer belongs to).
    /// </summary>
    [DisallowMultipleComponent]
    public class SphereWorld : MonoBehaviour, IChunkScatterWorld, VoxelEngine.Core.IVoxelWorld
    {
        // ---- Inspector ----
        [Header("Body & Assets")]
        public CelestialBody body;
        public MaterialRegistry materialRegistry;
        public Material terrainMaterial;

        [Header("Streaming")]
        public Transform viewer;
        [Range(1, 16)] public int viewDistance = VoxelConstants.DEFAULT_VIEW_DISTANCE;
        [Range(1, 16)] public int maxJobsPerFrame = 4;
        public bool generateColliders = true;
        [Tooltip("Spawn trees/rocks from biome scatter lists.")]
        public bool enableScatter = true;

        [Tooltip("Biome registry for terrain biomes + scatter. If null, a single default biome is used.")]
        public BiomeRegistry biomeRegistry;

        [Header("Persistence")]
        public string worldName = "DefaultSphereWorld";
        public bool enablePersistence = true;

        // ---- Singleton access (parallels VoxelWorld.Instance) ----
        public static SphereWorld Instance { get; private set; }

        /// <summary>Sea level in body-local voxel space (for scatter placement).</summary>
        public int SeaLevel => body != null ? Mathf.RoundToInt(body.genParams.seaRadius / VoxelConstants.VOXEL_SIZE) : 96;

        // ── IVoxelWorld explicit properties ──
        VoxelEngine.Materials.MaterialRegistry VoxelEngine.Core.IVoxelWorld.MaterialRegistry => materialRegistry;
        Transform VoxelEngine.Core.IVoxelWorld.Viewer => viewer;
        int VoxelEngine.Core.IVoxelWorld.SeaLevel => SeaLevel;
        int VoxelEngine.Core.IVoxelWorld.Seed => body != null ? body.genParams.seed : 0;

        // ---- Runtime ----
        private readonly Dictionary<Vector3Int, Chunk> _chunks = new();

        // Chunk GameObjects are pooled. Queue entries therefore carry the rent epoch captured
        // at enqueue time; a stale entry cannot write to a Chunk that has already been recycled
        // for a different coordinate after fast movement or a teleport.
        private readonly Queue<QueuedChunk> _genQueue = new();
        private readonly Queue<QueuedChunk> _meshQueue = new();
        private readonly HashSet<Chunk> _queuedForGeneration = new();
        private readonly HashSet<Chunk> _queuedForMeshing = new();

        private ChunkPool _pool;
        private ChunkStorage _storage;
        private NativeArray<Color32> _materialColors;
        private NativeArray<OreLayer> _ores;
        private NativeArray<BiomeData> _biomes;
        private NativeArray<VertexAttributeDescriptor> _vertexAttributes;

        private readonly List<PendingGen> _pendingGen = new();
        private readonly List<PendingMesh> _pendingMesh = new();

        private BiomeRegistry _biomeRegistry;   // for scatter

        private readonly struct QueuedChunk
        {
            public readonly Chunk chunk;
            public readonly int epoch;

            public QueuedChunk(Chunk chunk)
            {
                this.chunk = chunk;
                epoch = chunk != null ? chunk.streamEpoch : 0;
            }
        }

        private struct PendingGen
        {
            public Chunk chunk; public int epoch; public JobHandle handle;
        }
        private struct PendingMesh
        {
            public Chunk chunk; public int epoch; public JobHandle handle;
            public Mesh.MeshDataArray meshDataArray;
            public NativeArray<Bounds> bounds; public NativeArray<int> counts;
            public NativeArray<float3> vertScratch, normScratch;
            public NativeArray<Color32> colScratch;
            public NativeArray<int> idxScratch, cellLut;
        }

        // ---- Lifecycle ----
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            VoxelEngine.WaterSim.WaterMeshBuilder.ResetForNewWorld();

            // Ensure the fluid sim exists. The flat VoxelWorld is disabled by CosmosBootstrap
            // when the sphere is active, so there's no cross-contamination — the sphere owns the
            // FluidManager exclusively now.
            VoxelEngine.WaterSim.FluidManager.EnsureInstance();
            VoxelEngine.Fluids.FluidSimManager.EnsureInstance();

            if (materialRegistry == null) materialRegistry = Resources.Load<MaterialRegistry>("MaterialRegistry");
            // Robust fallback: if no MaterialRegistry is resolvable, create an empty one so the
            // world still renders (GetColor falls back to MaterialRegistry.DefaultColor).
            if (materialRegistry == null)
            {
                Debug.LogWarning("[SphereWorld] No MaterialRegistry found — creating an empty fallback " +
                                 "(colors use built-in defaults). Assign a MaterialRegistry for full fidelity.");
                materialRegistry = ScriptableObject.CreateInstance<MaterialRegistry>();
            }
            if (terrainMaterial == null)  terrainMaterial  = Resources.Load<Material>("Mat_Terrain");
            if (terrainMaterial == null)
            {
                Debug.LogWarning("[SphereWorld] No terrain material found — creating a URP-Lit fallback.");
                var terrainShader = Shader.Find("VoxelEngine/VoxelTerrainURP")
                                  ?? Shader.Find("VoxelEngine/VoxelTerrainEnhanced")
                                  ?? Shader.Find("Universal Render Pipeline/Lit")
                                  ?? Shader.Find("Standard");
                terrainMaterial = new Material(terrainShader);
                terrainMaterial.name = "Mat_Terrain_Fallback";
                if (terrainMaterial.HasProperty("_BaseColor")) terrainMaterial.SetColor("_BaseColor", new Color(0.72f, 0.72f, 0.72f, 1f));
                if (terrainMaterial.HasProperty("_Color")) terrainMaterial.SetColor("_Color", new Color(0.72f, 0.72f, 0.72f, 1f));
            }
            if (terrainMaterial.HasProperty("_Smoothness"))
            {
                terrainMaterial.SetFloat("_Smoothness", 0f); // Fix glossy land
            }
            if (terrainMaterial.HasProperty("_BaseColor"))
            {
                var col = terrainMaterial.GetColor("_BaseColor");
                if (col == Color.clear || col == Color.black) terrainMaterial.SetColor("_BaseColor", Color.white);
            }

            // Recover the body if not assigned (search this GameObject + siblings).
            if (body == null) body = GetComponent<CelestialBody>();
            if (body == null) body = GetComponentInChildren<CelestialBody>();
            if (body == null)
            {
                Debug.LogError("[SphereWorld] No CelestialBody assigned/found. SphereWorld needs a body to stream.");
                enabled = false;
                return;
            }
            body.ApplySettings();

            // World-name + seed from the main-menu session (if present) — never mutate the asset.
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session != null)
            {
                worldName = session.worldName;
            }

            materialRegistry.Build();

            // Pool parents chunks under the BODY so the whole terrain mass is body-relative.
            _pool = new ChunkPool(body.transform, terrainMaterial);

            _materialColors = new NativeArray<Color32>(256, Allocator.Persistent);
            for (int i = 0; i < 256; i++) _materialColors[i] = materialRegistry.GetColor((byte)i);

            // Ore + biome data come straight from the body (two-tier ores + climate filtering).
            var oreArr = body.BuildOreLayers();
            _ores = new NativeArray<OreLayer>(oreArr.Length, Allocator.Persistent);
            int crudeOilLayers = 0;
            for (int i = 0; i < oreArr.Length; i++)
            {
                _ores[i] = oreArr[i];
                if (oreArr[i].material == MaterialId.CrudeOil) crudeOilLayers++;
            }
            if (body.settings != null && body.settings.CanGenerateFiniteCrudeOilSeeps)
            {
                string infiniteStatus = body.settings.CanGenerateInfiniteJackPumpNodes
                    ? $"rare Jack Pump chance {body.settings.ResolveInfiniteOilNodeChance():P1} per geological cell"
                    : "no infinite Jack Pump nodes";
                Debug.Log($"[SphereWorld] Crude oil ready on '{body.DisplayName}': {crudeOilLayers} crude layer(s), " +
                          $"finite seep chance {body.settings.ResolveCrudeOilSiteChance():P0}, {infiniteStatus}.");
            }

            var biomeArr = body.BuildBiomeData(biomeRegistry);
            _biomes = new NativeArray<BiomeData>(biomeArr.Length, Allocator.Persistent);
            for (int i = 0; i < biomeArr.Length; i++) _biomes[i] = biomeArr[i];

            _vertexAttributes = new NativeArray<VertexAttributeDescriptor>(3, Allocator.Persistent);
            _vertexAttributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0);
            _vertexAttributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal,   VertexAttributeFormat.Float32, 3, 0);
            _vertexAttributes[2] = new VertexAttributeDescriptor(VertexAttribute.Color,    VertexAttributeFormat.UNorm8,  4, 0);

            if (enablePersistence)
                _storage = new ChunkStorage(string.IsNullOrEmpty(worldName) ? "DefaultSphereWorld" : worldName);
        }

        private void OnDestroy()
        {
            VoxelEngine.Generation.OilReservoirDecorator.ForgetWorld(this);
            ChunkScatter.ForgetWorld(this);
            foreach (var p in _pendingGen) p.handle.Complete();
            foreach (var p in _pendingMesh) DisposePendingMesh(p, complete: true);
            _pendingGen.Clear(); _pendingMesh.Clear();
            _genQueue.Clear(); _meshQueue.Clear();
            _queuedForGeneration.Clear(); _queuedForMeshing.Clear();

            if (_storage != null)
            {
                foreach (var kv in _chunks) if (kv.Value.isModified) _storage.EnqueueSave(kv.Value);
                _storage.WaitForIdle(); _storage.Shutdown(); _storage = null;
            }
            _pool?.DisposeAll(_chunks.Values);
            _chunks.Clear();
            if (_materialColors.IsCreated)  _materialColors.Dispose();
            if (_ores.IsCreated)            _ores.Dispose();
            if (_biomes.IsCreated)          _biomes.Dispose();
            if (_vertexAttributes.IsCreated)_vertexAttributes.Dispose();
            // Never leave the static ActiveWorld pointer dangling at this destroyed world —
            // otherwise callers route terrain queries into a dead CelestialBody and throw
            // MissingReferenceException every frame (breaking the whole world on reload).
            VoxelEngine.Core.ActiveWorld.ClearIfCurrent(this);
            if (Instance == this)
            {
                Shader.SetGlobalVector("_VoxelTerrainBodyCenter", Vector4.zero);
                Shader.SetGlobalFloat("_VoxelTerrainIsPlanet", 0f);
                Instance = null;
            }
        }

        private float _diagTimer;
        private int   _diagGenerated;
        private int   _diagMeshed;

        private void Update()
        {
            if (viewer == null || body == null) return;
            PublishTerrainShaderContext();
            UpdateStreaming();
            DispatchGenerationJobs();
            DispatchMeshingJobs();
            CompleteFinishedJobs();
            VoxelEngine.Generation.OilReservoirDecorator.Tick(this);
            
            // Re-enabled WaterMeshBuilder for Spheres
            VoxelEngine.WaterSim.WaterMeshBuilder.Pump(4);
            
            ProcessDeferredScatter();

            // ── Comprehensive diagnostics (every 3 seconds) ──
            _diagTimer += Time.deltaTime;
            if (_diagTimer > 3f)
            {
                _diagTimer = 0f;
                int gen = 0, meshed = 0, waterChunks = 0, scattered = 0, waterVoxels = 0;
                foreach (var kv in _chunks)
                {
                    var ch = kv.Value;
                    if (ch.isGenerated) gen++;
                    if (ch.meshFilter != null && ch.meshFilter.sharedMesh != null &&
                        ch.meshFilter.sharedMesh.vertexCount > 0) meshed++;
                    if (ch.isScattered) scattered++;
                    // Count water voxels + water mesh GOs.
                    if (ch.waterMeshGO != null) waterChunks++;
                    if (ch.fluidGrid != null) waterChunks++;
                }
                // Sample a few chunks for actual water voxel count.
                int sampled = 0;
                foreach (var kv in _chunks)
                {
                    if (sampled >= 5) break;
                    var ch = kv.Value;
                    if (!ch.isGenerated) continue;
                    for (int wz = 0; wz < 8; wz++)
                    for (int wy = 0; wy < 8; wy++)
                    for (int wx = 0; wx < 8; wx++)
                    {
                        var v = ch.GetVoxelLocal(wx * 4, wy * 4, wz * 4);
                        if (v.waterLevel > 0 || v.material == (byte)VoxelEngine.Materials.MaterialId.WaterLiquid)
                            waterVoxels++;
                    }
                    sampled++;
                }
                var fm = WaterSim.FluidManager.Instance;
                Debug.Log($"[SphereWorld] Chunks: {_chunks.Count} active | {gen} gen | {meshed} meshed | " +
                          $"{scattered} scattered | {waterChunks} with water-GO | {waterVoxels} water voxels (5-chunk sample) | " +
                          $"FluidMgr: {(fm == null ? "NULL" : "alive")} | " +
                          $"genQ:{_genQueue.Count} meshQ:{_meshQueue.Count} | " +
                          $"seaRadius:{body?.SeaRadius} meanSurf:{body?.SurfaceRadius}");
            }
        }

        private void PublishTerrainShaderContext()
        {
            if (body == null) return;
            Vector3 center = body.transform.position;
            Shader.SetGlobalVector("_VoxelTerrainBodyCenter", new Vector4(center.x, center.y, center.z, 1f));
            Shader.SetGlobalFloat("_VoxelTerrainIsPlanet", 1f);
        }

        // ---- Streaming (body-relative cartesian) ----
        private readonly List<(Vector3Int coord, int distSq)> _loadCandidates = new();
        private readonly List<Vector3Int> _evictList = new();

        private bool IsCurrentChunk(Chunk chunk, int epoch)
        {
            if (chunk == null || chunk.go == null || !chunk.go.activeSelf || chunk.streamEpoch != epoch)
                return false;
            return _chunks.TryGetValue(chunk.coord, out var live) && object.ReferenceEquals(live, chunk);
        }

        private bool IsCurrentChunk(Chunk chunk) => chunk != null && IsCurrentChunk(chunk, chunk.streamEpoch);

        private void QueueGeneration(Chunk chunk)
        {
            if (!IsCurrentChunk(chunk) || chunk.isGenerated || IsGenJobPending(chunk)) return;
            if (_queuedForGeneration.Add(chunk)) _genQueue.Enqueue(new QueuedChunk(chunk));
        }

        private void QueueMesh(Chunk chunk)
        {
            if (!IsCurrentChunk(chunk) || !chunk.isGenerated) return;
            if (_queuedForMeshing.Add(chunk)) _meshQueue.Enqueue(new QueuedChunk(chunk));
        }

        private void CancelQueuedWork(Chunk chunk)
        {
            if (chunk == null) return;
            // The actual FIFO entries are intentionally left in place: their captured epoch
            // makes them harmless, while removing from the sets permits a fresh rental to queue
            // its own work immediately without an allocation-heavy queue rebuild.
            _queuedForGeneration.Remove(chunk);
            _queuedForMeshing.Remove(chunk);
        }

        private void UpdateStreaming()
        {
            // Viewer position in the BODY's local space (chunks are parented to the body).
            Vector3 localViewer = body.transform.InverseTransformPoint(viewer.position);
            Vector3Int center = LocalToChunk(localViewer);

            int r = viewDistance;
            int loadR2 = r * r;
            int evictR2 = (r + 3) * (r + 3); // hysteresis to avoid load/unload flicker

            // A planet needs a body-relative 3D stream, not a flat-world XZ column. The local
            // editable layer remains a ball around the player; the full sampled planet LOD owns
            // every unstreamed surface region beyond it.
            _loadCandidates.Clear();
            for (int dz = -r; dz <= r; dz++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int d2 = dx * dx + dy * dy + dz * dz;
                if (d2 > loadR2) continue;
                var c = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
                if (_chunks.ContainsKey(c)) continue;
                _loadCandidates.Add((c, d2));
            }
            _loadCandidates.Sort((a, b) => a.distSq.CompareTo(b.distSq));

            int spawned = 0;
            for (int i = 0; i < _loadCandidates.Count && spawned < maxJobsPerFrame * 2; i++)
            {
                var c = _loadCandidates[i].coord;
                var chunk = _pool.Rent(c);
                // Chunks are parented to the BODY, so place them in BODY-LOCAL space.
                chunk.go.transform.localPosition = chunk.WorldOrigin;
                _chunks.Add(c, chunk);
                QueueGeneration(chunk);
                spawned++;
            }

            _evictList.Clear();
            foreach (var kv in _chunks)
            {
                int dx = kv.Key.x - center.x;
                int dy = kv.Key.y - center.y;
                int dz = kv.Key.z - center.z;
                if (dx * dx + dy * dy + dz * dz > evictR2) _evictList.Add(kv.Key);
            }
            for (int i = 0; i < _evictList.Count; i++)
            {
                var k = _evictList[i];
                var ch = _chunks[k];
                // Complete all work that owns this NativeArray before returning it to the pool.
                CompleteGenJobFor(ch, finalizeContent: false);
                CompleteMeshJobFor(ch);
                CancelQueuedWork(ch);
                if (_storage != null && ch.isModified) _storage.EnqueueSave(ch);
                _pool.Return(ch);
                _chunks.Remove(k);
            }
        }

        // ---- Generation jobs (radial density) ----
        private void DispatchGenerationJobs()
        {
            int budget = maxJobsPerFrame;
            while (budget-- > 0 && _genQueue.Count > 0)
            {
                QueuedChunk queued = _genQueue.Dequeue();
                Chunk chunk = queued.chunk;
                if (!IsCurrentChunk(chunk, queued.epoch)) continue;
                _queuedForGeneration.Remove(chunk);
                if (chunk.isGenerated || IsGenJobPending(chunk)) continue;

                // Fast path: load from disk if previously saved.
                if (_storage != null && _storage.TryLoadChunk(chunk.coord, chunk))
                {
                    chunk.isGenerated = true;
                    chunk.isModified = false;
                    chunk.isScattered = false;
                    // Re-evaluate deterministic oil-rich-body seeps after loading old chunks too.
                    VoxelEngine.Generation.OilReservoirDecorator.Decorate(chunk, this);
                    VoxelEngine.WaterSim.WaterMeshBuilder.Schedule(chunk);
                    QueueMesh(chunk);
                    continue;
                }

                float s = VoxelConstants.CHUNK_SIZE * VoxelConstants.VOXEL_SIZE;
                // Chunk voxel storage is padded by one cell on every side. The padded sample at
                // index 0 must represent coord*32 - 1 (matching SurfaceNetsJob's cx - 1 local
                // coordinates); using coord*32 here offset each generated chunk and produced
                // overlapping/slit terrain at chunk boundaries.
                var originWorld = new float3(chunk.coord.x, chunk.coord.y, chunk.coord.z) * s
                                  - new float3(VoxelConstants.VOXEL_SIZE);

                var job = new SphereChunkGenJob
                {
                    prm        = body.genParams,
                    originWorld= originWorld,
                    voxelSize  = VoxelConstants.VOXEL_SIZE,
                    biomes     = _biomes,
                    ores       = _ores,
                    voxels     = chunk.voxels,
                    sizeX      = VoxelConstants.CHUNK_SIZE_P,
                    sizeY      = VoxelConstants.CHUNK_SIZE_P,
                    sizeZ      = VoxelConstants.CHUNK_SIZE_P,
                };
                var handle = job.Schedule(VoxelConstants.VOXELS_PER_CHUNK_P, 64);
                _pendingGen.Add(new PendingGen { chunk = chunk, epoch = queued.epoch, handle = handle });
            }
        }

        // ---- Mesh jobs ----
        private void DispatchMeshingJobs()
        {
            int budget = maxJobsPerFrame;
            while (budget-- > 0 && _meshQueue.Count > 0)
            {
                QueuedChunk queued = _meshQueue.Dequeue();
                Chunk chunk = queued.chunk;
                if (!IsCurrentChunk(chunk, queued.epoch)) continue;
                _queuedForMeshing.Remove(chunk);
                if (!chunk.isGenerated) continue;

                // SphereChunkGenJob writes its padded border directly from the density field.
                // Waiting for cardinal neighbours was a flat-world relic that left visible
                // square-edge stalls whenever the player moved quickly around the globe.
                ScheduleMeshJob(chunk);
            }
        }

        public void ScheduleMeshJob(Chunk chunk)
        {
            if (!IsCurrentChunk(chunk)) return;
            // This method is also used by edits/oil decoration. It may be called just as the
            // generation job completes, so settle that job first and refuse ungenerated data.
            CompleteGenJobFor(chunk);
            if (!chunk.isGenerated) return;
            _queuedForMeshing.Remove(chunk);

            for (int i = 0; i < _pendingMesh.Count; i++)
                if (_pendingMesh[i].chunk == chunk && _pendingMesh[i].epoch == chunk.streamEpoch) return;

            chunk.isDirty = false;

            const int CELLS = (VoxelConstants.CHUNK_SIZE + 1) * (VoxelConstants.CHUNK_SIZE + 1) * (VoxelConstants.CHUNK_SIZE + 1);
            int maxVerts = CELLS, maxIdx = CELLS * 18;

            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            var pending = new PendingMesh
            {
                chunk = chunk,
                epoch = chunk.streamEpoch,
                meshDataArray = meshDataArray,
                bounds = new NativeArray<Bounds>(1, Allocator.TempJob),
                counts = new NativeArray<int>(2, Allocator.TempJob),
                vertScratch = new NativeArray<float3>(maxVerts, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
                normScratch = new NativeArray<float3>(maxVerts, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
                colScratch  = new NativeArray<Color32>(maxVerts, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
                idxScratch  = new NativeArray<int>(maxIdx, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
                cellLut     = new NativeArray<int>(CELLS, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
            };
            var job = new SurfaceNetsJob
            {
                voxels = chunk.voxels, meshData = meshDataArray[0],
                bounds = pending.bounds, counts = pending.counts,
                vertexScratch = pending.vertScratch, normalScratch = pending.normScratch,
                colorScratch = pending.colScratch, indexScratch = pending.idxScratch,
                cellVertexIndex = pending.cellLut, materialColors = _materialColors,
                vertexAttributes = _vertexAttributes,
                isSphere = true,
                chunkOrigin = new float3(chunk.coord.x, chunk.coord.y, chunk.coord.z) * VoxelConstants.CHUNK_SIZE
            };
            pending.handle = job.Schedule();
            _pendingMesh.Add(pending);
        }

        // Reusable buffers so CompleteFinishedJobs can collect-then-process without re-entrant
        // list mutation (FinalizeGen -> StitchBorders -> CompleteGenJobFor re-enters and would
        // otherwise shrink _pendingGen faster than the backward loop's index decrements -> OOR).
        private readonly List<PendingGen>  _completedGen  = new();
        private readonly List<PendingMesh> _completedMesh = new();

        private void CompleteFinishedJobs()
        {
            // Phase 1: collect ALL completed gen jobs into a buffer and remove them BEFORE
            // finalizing any. This guarantees the backward scan never reads an index that a
            // re-entrant FinalizeGen has already removed.
            _completedGen.Clear();
            for (int i = _pendingGen.Count - 1; i >= 0; i--)
            {
                if (_pendingGen[i].handle.IsCompleted)
                {
                    _completedGen.Add(_pendingGen[i]);
                    _pendingGen.RemoveAt(i);
                }
            }
            // Phase 2: finalize each. Re-entrant CompleteGenJobFor on neighbours is now safe.
            foreach (var p in _completedGen) FinalizeGen(p);
            _completedGen.Clear();

            // Same pattern for mesh jobs (no re-entrancy expected, but consistent + safe).
            _completedMesh.Clear();
            for (int i = _pendingMesh.Count - 1; i >= 0; i--)
            {
                if (_pendingMesh[i].handle.IsCompleted)
                {
                    _completedMesh.Add(_pendingMesh[i]);
                    _pendingMesh.RemoveAt(i);
                }
            }
            foreach (var p in _completedMesh)
            {
                p.handle.Complete();
                if (IsCurrentChunk(p.chunk, p.epoch))
                {
                    var mesh = p.chunk.mesh; mesh.Clear();
                    Mesh.ApplyAndDisposeWritableMeshData(p.meshDataArray, mesh,
                        MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                    mesh.bounds = p.bounds[0];
                    p.chunk.meshFilter.sharedMesh = mesh;
                    if (generateColliders && p.counts[1] > 0) p.chunk.meshCollider.sharedMesh = mesh;
                    else if (p.counts[1] == 0) p.chunk.meshCollider.sharedMesh = null;
                }
                else
                {
                    // The mesh data still belongs to Unity and must be disposed even when a
                    // stale pooled request is rejected before it reaches a live chunk.
                    p.meshDataArray.Dispose();
                }
                DisposePendingMesh(p, complete: false, meshDataAlreadyDisposed: true);
            }
            _completedMesh.Clear();
        }

        private void FinalizeGen(PendingGen p)
        {
            p.handle.Complete();
            if (!IsCurrentChunk(p.chunk, p.epoch)) return;

            p.chunk.isGenerated = true;
            p.chunk.genCompletedTime = Time.time;
            p.chunk.isScattered = false;

            VoxelEngine.Generation.OilReservoirDecorator.Decorate(p.chunk, this);

            // Wake fluid sim and schedule real voxel water/oil meshing.
            VoxelEngine.WaterSim.WaterMeshBuilder.Schedule(p.chunk);
            QueueMesh(p.chunk);
        }

        private void ProcessDeferredScatter()
        {
            if (!enableScatter || body == null) return;
            // Resolve a biome registry once (body.allowedBiomes' owning registry, if any).
            if (_biomeRegistry == null) _biomeRegistry = ResolveBiomeRegistry();
            if (_biomeRegistry == null) return;

            foreach (var kv in _chunks)
            {
                var c = kv.Value;
                if (!c.isGenerated || c.isScattered) continue;
                if (c.meshFilter == null || c.meshFilter.sharedMesh == null) continue;
                // Scatter itself validates the true radial exterior surface. A global +Y
                // prerequisite is incorrect around a sphere and used to delay/skip trees on
                // most latitudes while chunks streamed in.
                ChunkScatter.Populate(this, c, _biomeRegistry, body.genParams.seed);
                c.isScattered = true;
            }
        }

        private BiomeRegistry ResolveBiomeRegistry()
        {
            // Priority: the inspector-assigned biomeRegistry field (wired by CosmosBootstrap),
            // then the body's allowedBiomes whitelist, then Resources fallback.
            if (biomeRegistry != null && biomeRegistry.biomes != null && biomeRegistry.biomes.Count > 0)
                return biomeRegistry;
            if (body.settings != null && body.settings.allowedBiomes != null && body.settings.allowedBiomes.Length > 0)
            {
                var reg = ScriptableObject.CreateInstance<BiomeRegistry>();
                reg.biomes = new List<BiomeDefinition>(body.settings.allowedBiomes);
                return reg;
            }
            return Resources.Load<BiomeRegistry>("BiomeRegistry");
        }

        /// <summary>True if this chunk still has an in-flight SphereChunkGenJob writing its voxels.</summary>
        private bool IsGenJobPending(Chunk chunk)
        {
            if (chunk == null) return false;
            for (int i = 0; i < _pendingGen.Count; i++)
                if (_pendingGen[i].chunk == chunk && _pendingGen[i].epoch == chunk.streamEpoch) return true;
            return false;
        }

        // ---- Border stitching (used only for player edits in Phase 2.1; gen fills borders) ----
        private void StitchBordersWithNeighbours(Chunk c)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            const int SP = VoxelConstants.CHUNK_SIZE_P;
            int Pad(int x, int y, int z) => (x + 1) + (y + 1) * SP + (z + 1) * SP * SP;

            for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3Int off = Vector3Int.zero;
                if (axis == 0) off.x = sign; else if (axis == 1) off.y = sign; else off.z = sign;
                if (!_chunks.TryGetValue(c.coord + off, out var n) || !n.isGenerated) continue;

                // NON-REENTRANT border sync (Phase 2.1 — player edits). Skip neighbours whose
                // gen job is still pending; they will sync their own borders when they finalize.
                // Force-completing them here would re-enter FinalizeGen -> StitchBorders -> crash.
                if (IsGenJobPending(n)) continue;
                // Complete any mesh jobs that READ these voxels before we WRITE the border.
                CompleteMeshJobFor(c); CompleteMeshJobFor(n);
                if (axis == 0)
                {
                    int fCx = sign > 0 ? S - 1 : 0, pCx = sign > 0 ? S : -1;
                    int fNx = sign > 0 ? 0 : S - 1, pNx = sign > 0 ? -1 : S;
                    for (int y = 0; y < S; y++) for (int z = 0; z < S; z++)
                    { c.voxels[Pad(pCx, y, z)] = n.voxels[Pad(fNx, y, z)]; n.voxels[Pad(pNx, y, z)] = c.voxels[Pad(fCx, y, z)]; }
                }
                else if (axis == 1)
                {
                    int fCy = sign > 0 ? S - 1 : 0, pCy = sign > 0 ? S : -1;
                    int fNy = sign > 0 ? 0 : S - 1, pNy = sign > 0 ? -1 : S;
                    for (int x = 0; x < S; x++) for (int z = 0; z < S; z++)
                    { c.voxels[Pad(x, pCy, z)] = n.voxels[Pad(x, fNy, z)]; n.voxels[Pad(x, pNy, z)] = c.voxels[Pad(x, fCy, z)]; }
                }
                else
                {
                    int fCz = sign > 0 ? S - 1 : 0, pCz = sign > 0 ? S : -1;
                    int fNz = sign > 0 ? 0 : S - 1, pNz = sign > 0 ? -1 : S;
                    for (int x = 0; x < S; x++) for (int y = 0; y < S; y++)
                    { c.voxels[Pad(x, y, pCz)] = n.voxels[Pad(x, y, fNz)]; n.voxels[Pad(x, y, pNz)] = c.voxels[Pad(x, y, fCz)]; }
                }
                QueueMesh(n);
            }
        }

        // ---- Job completion helpers (satisfy Unity's job-safety system) ----
        public void CompleteGenJobForChunk(Chunk chunk) => CompleteGenJobFor(chunk);
        public void CompleteMeshJobForChunk(Chunk chunk) => CompleteMeshJobFor(chunk);

        private void CompleteGenJobFor(Chunk chunk, bool finalizeContent = true)
        {
            if (chunk == null) return;
            for (int i = _pendingGen.Count - 1; i >= 0; i--)
            {
                if (_pendingGen[i].chunk != chunk || _pendingGen[i].epoch != chunk.streamEpoch) continue;
                var p = _pendingGen[i];
                _pendingGen.RemoveAt(i);
                // Non-recursive: just complete + mark. Do NOT call FinalizeGen (which would
                // re-enter editing/decorator paths); the deterministic padded border is already
                // safe to mesh and this queue path preserves the normal finalisation order.
                p.handle.Complete();
                if (!IsCurrentChunk(p.chunk, p.epoch)) return;
                p.chunk.isGenerated = true;
                p.chunk.genCompletedTime = Time.time;
                p.chunk.isScattered = false;
                if (finalizeContent)
                {
                    VoxelEngine.Generation.OilReservoirDecorator.Decorate(p.chunk, this);
                    VoxelEngine.WaterSim.WaterMeshBuilder.Schedule(p.chunk);
                }
                QueueMesh(p.chunk);
                return;
            }
        }

        private void CompleteMeshJobFor(Chunk chunk)
        {
            for (int i = _pendingMesh.Count - 1; i >= 0; i--)
            {
                var p = _pendingMesh[i];
                if (p.chunk != chunk || p.epoch != chunk.streamEpoch) continue;
                p.handle.Complete();
                if (IsCurrentChunk(p.chunk, p.epoch))
                {
                    var mesh = p.chunk.mesh; mesh.Clear();
                    Mesh.ApplyAndDisposeWritableMeshData(p.meshDataArray, mesh,
                        MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                    mesh.bounds = p.bounds[0];
                    p.chunk.meshFilter.sharedMesh = mesh;
                    if (generateColliders && p.counts[1] > 0) p.chunk.meshCollider.sharedMesh = mesh;
                    else if (p.counts[1] == 0) p.chunk.meshCollider.sharedMesh = null;
                }
                else
                {
                    p.meshDataArray.Dispose();
                }
                DisposePendingMesh(p, complete: false, meshDataAlreadyDisposed: true);
                _pendingMesh.RemoveAt(i);
            }
        }

        private static void DisposePendingMesh(PendingMesh p, bool complete, bool meshDataAlreadyDisposed = false)
        {
            if (complete) p.handle.Complete();
            if (!meshDataAlreadyDisposed) p.meshDataArray.Dispose();
            if (p.bounds.IsCreated) p.bounds.Dispose();
            if (p.counts.IsCreated) p.counts.Dispose();
            if (p.vertScratch.IsCreated) p.vertScratch.Dispose();
            if (p.normScratch.IsCreated) p.normScratch.Dispose();
            if (p.colScratch.IsCreated) p.colScratch.Dispose();
            if (p.idxScratch.IsCreated) p.idxScratch.Dispose();
            if (p.cellLut.IsCreated) p.cellLut.Dispose();
        }

        // ---- Public query API (mirrors VoxelWorld so callers port over) ----
        private static Vector3Int LocalToChunk(Vector3 localPos)
        {
            float s = VoxelConstants.CHUNK_SIZE * VoxelConstants.VOXEL_SIZE;
            return new Vector3Int(Mathf.FloorToInt(localPos.x / s), Mathf.FloorToInt(localPos.y / s), Mathf.FloorToInt(localPos.z / s));
        }

        /// <summary>Convert a WORLD position to a chunk coord (body-relative cartesian grid).</summary>
        public Vector3Int WorldToChunk(Vector3 worldPos)
            => body == null ? Vector3Int.zero : LocalToChunk(body.transform.InverseTransformPoint(worldPos));

        public Vector3Int WorldToVoxel(Vector3 worldPos)
        {
            // Guard against a destroyed CelestialBody — callers (dropped items, fluid sim,
            // tools) query through ActiveWorld.Current and must never hit a torn-down body.
            if (body == null) return Vector3Int.zero;
            Vector3 lp = body.transform.InverseTransformPoint(worldPos) / VoxelConstants.VOXEL_SIZE;
            return new Vector3Int(Mathf.FloorToInt(lp.x), Mathf.FloorToInt(lp.y), Mathf.FloorToInt(lp.z));
        }

        public bool TryGetChunk(Vector3Int coord, out Chunk chunk) => _chunks.TryGetValue(coord, out chunk);

        /// <summary>
        /// True when a body-local voxel sits below the authored terrain surface of a genuine
        /// ocean basin. Used only to clean legacy generated cave water; it never treats the
        /// global sea shell itself as proof that a dry underground cavity should contain water.
        /// </summary>
        public bool IsNaturalOceanBasinAt(Vector3Int localVoxel)
        {
            if (body == null || !_biomes.IsCreated) return false;
            float3 local = new float3(localVoxel.x + 0.5f, localVoxel.y + 0.5f, localVoxel.z + 0.5f)
                * VoxelConstants.VOXEL_SIZE;
            float3 direction = math.normalizesafe(local, new float3(0f, 1f, 0f));
            SphereDensity.EvaluateColumn(body.genParams, _biomes, direction, out float surfaceRadius, out _);
            return surfaceRadius < body.genParams.seaRadius - 1f;
        }

        /// <summary>
        /// Tests whether a body-local voxel lies in the narrow band of the authored radial
        /// terrain surface. This rejects cave walls and stale/deep interior voxels before
        /// scatter or geological decorators treat them as world exterior.
        /// </summary>
        public bool IsNearSampledTerrainSurface(Vector3Int localVoxel, float toleranceMetres = 2.25f)
        {
            if (body == null || !_biomes.IsCreated) return false;
            float3 local = new float3(localVoxel.x + 0.5f, localVoxel.y + 0.5f, localVoxel.z + 0.5f)
                * VoxelConstants.VOXEL_SIZE;
            float radius = math.length(local);
            float3 direction = math.normalizesafe(local, new float3(0f, 1f, 0f));
            SphereDensity.EvaluateColumn(body.genParams, _biomes, direction, out float surfaceRadius, out _);
            return math.abs(radius - surfaceRadius) <= math.max(0.5f, toleranceMetres);
        }

        /// <summary>
        /// Resolves one real, exposed radial terrain surface point for scatter. The calculation
        /// is tied to the same density column that generated the chunk, so trees cannot anchor
        /// to a cave wall or to the old global-Y notion of a top surface.
        /// </summary>
        public bool TryGetExteriorSurface(Vector3Int localVoxel, out Vector3 localSurface, out Vector3 radialUp)
        {
            localSurface = Vector3.zero;
            radialUp = Vector3.up;
            if (body == null || !_biomes.IsCreated) return false;

            Vector3 sample = ((Vector3)localVoxel + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE;
            radialUp = sample.sqrMagnitude > 0.0001f ? sample.normalized : Vector3.up;
            if (!IsNearSampledTerrainSurface(localVoxel)) return false;

            if (!TryGetGeneratedVoxel(localVoxel, out Voxel source) ||
                !source.IsSolid || VoxelEngine.WaterSim.FluidMaterialUtility.IsFluid(source))
                return false;

            SphereDensity.EvaluateColumn(body.genParams, _biomes, (float3)radialUp, out float surfaceRadius, out _);
            localSurface = radialUp * surfaceRadius;

            // Verify clear air immediately above when that voxel is already streamed. If the
            // probe lies outside the current bubble, the sampled surface test above is the
            // authoritative safe fallback; it still cannot identify an internal cave as exterior.
            for (int step = 1; step <= 3; step++)
            {
                Vector3 probePoint = localSurface + radialUp * (step * VoxelConstants.VOXEL_SIZE);
                Vector3Int probe = new Vector3Int(
                    Mathf.FloorToInt(probePoint.x / VoxelConstants.VOXEL_SIZE),
                    Mathf.FloorToInt(probePoint.y / VoxelConstants.VOXEL_SIZE),
                    Mathf.FloorToInt(probePoint.z / VoxelConstants.VOXEL_SIZE));
                if (!TryGetGeneratedVoxel(probe, out Voxel exterior)) continue;
                if (exterior.IsSolid || VoxelEngine.WaterSim.FluidMaterialUtility.IsFluid(exterior)) return false;
            }

            // A tiny outward lift keeps the prefab root above the voxel iso-surface rather than
            // half a voxel inside its collider/mesh.
            localSurface += radialUp * 0.06f;
            return true;
        }

        private bool TryGetGeneratedVoxel(Vector3Int localVoxel, out Voxel voxel)
        {
            Vector3Int coord = new(
                Mathf.FloorToInt(localVoxel.x / (float)VoxelConstants.CHUNK_SIZE),
                Mathf.FloorToInt(localVoxel.y / (float)VoxelConstants.CHUNK_SIZE),
                Mathf.FloorToInt(localVoxel.z / (float)VoxelConstants.CHUNK_SIZE));
            if (!_chunks.TryGetValue(coord, out Chunk chunk) || chunk == null || !chunk.isGenerated)
            {
                voxel = Voxel.Empty;
                return false;
            }

            int lx = localVoxel.x - coord.x * VoxelConstants.CHUNK_SIZE;
            int ly = localVoxel.y - coord.y * VoxelConstants.CHUNK_SIZE;
            int lz = localVoxel.z - coord.z * VoxelConstants.CHUNK_SIZE;
            voxel = chunk.GetVoxelLocal(lx, ly, lz);
            return true;
        }

        public Voxel GetVoxelWorld(Vector3Int worldVoxel)
        {
            Vector3Int chunkCoord = new(
                Mathf.FloorToInt(worldVoxel.x / (float)VoxelConstants.CHUNK_SIZE),
                Mathf.FloorToInt(worldVoxel.y / (float)VoxelConstants.CHUNK_SIZE),
                Mathf.FloorToInt(worldVoxel.z / (float)VoxelConstants.CHUNK_SIZE));
            if (!_chunks.TryGetValue(chunkCoord, out var c) || !c.isGenerated) return Voxel.Empty;
            CompleteGenJobFor(c); CompleteMeshJobFor(c);
            int lx = worldVoxel.x - chunkCoord.x * VoxelConstants.CHUNK_SIZE;
            int ly = worldVoxel.y - chunkCoord.y * VoxelConstants.CHUNK_SIZE;
            int lz = worldVoxel.z - chunkCoord.z * VoxelConstants.CHUNK_SIZE;
            return c.GetVoxelLocal(lx, ly, lz);
        }

        public void SetVoxelWorld(Vector3Int worldVoxel, Voxel v, bool remesh = true)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            Vector3Int chunkCoord = new(
                Mathf.FloorToInt(worldVoxel.x / (float)S),
                Mathf.FloorToInt(worldVoxel.y / (float)S),
                Mathf.FloorToInt(worldVoxel.z / (float)S));
            if (!_chunks.TryGetValue(chunkCoord, out var c) || !c.isGenerated) return;
            CompleteGenJobFor(c); CompleteMeshJobFor(c);
            int lx = worldVoxel.x - chunkCoord.x * S;
            int ly = worldVoxel.y - chunkCoord.y * S;
            int lz = worldVoxel.z - chunkCoord.z * S;
            c.SetVoxelLocal(lx, ly, lz, v);
            c.isModified = true;

            // Schedule real fluid mesh rebuild if the voxel change affects fluids.
            if (VoxelEngine.WaterSim.FluidMaterialUtility.IsFluid(v))
            {
                VoxelEngine.WaterSim.WaterMeshBuilder.Schedule(c);
            }

            if (!remesh) return;
            EnqueueRemesh(c);
            if (lx == 0)     EnqueueRemeshNeighbour(chunkCoord + new Vector3Int(-1, 0, 0));
            if (lx == S - 1) EnqueueRemeshNeighbour(chunkCoord + new Vector3Int( 1, 0, 0));
            if (ly == 0)     EnqueueRemeshNeighbour(chunkCoord + new Vector3Int( 0,-1, 0));
            if (ly == S - 1) EnqueueRemeshNeighbour(chunkCoord + new Vector3Int( 0, 1, 0));
            if (lz == 0)     EnqueueRemeshNeighbour(chunkCoord + new Vector3Int( 0, 0,-1));
            if (lz == S - 1) EnqueueRemeshNeighbour(chunkCoord + new Vector3Int( 0, 0, 1));
        }

        private void EnqueueRemesh(Chunk c) => QueueMesh(c);
        private void EnqueueRemeshNeighbour(Vector3Int coord)
        {
            if (_chunks.TryGetValue(coord, out var n) && n.isGenerated) QueueMesh(n);
        }
    }
}
