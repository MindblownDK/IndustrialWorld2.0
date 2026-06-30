// Assets/Scripts/VoxelEngine/Core/VoxelWorld.cs
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Biomes;
using VoxelEngine.Generation;
using VoxelEngine.Materials;
using VoxelEngine.Meshing;
using VoxelEngine.Persistence;
using VoxelEngine.Pooling;
using VoxelEngine.Scattering;

namespace VoxelEngine.Core
{
    /// <summary>
    /// Top-level voxel engine controller. Attach to one Manager GameObject in the scene.
    /// Streams chunks around 'viewer', schedules generation + meshing jobs, applies meshes
    /// using the modern Mesh.AllocateWritableMeshData / ApplyAndDisposeWritableMeshData path.
    /// </summary>
    [DisallowMultipleComponent]
    public class VoxelWorld : MonoBehaviour, VoxelEngine.Cosmos.IChunkScatterWorld, IVoxelWorld
    {
        // ---- Inspector ----
        [Header("Assets")]
        // Flat-world config (replaces the deprecated PlanetSettings class).
        // These are set from the inspector or WorldSession on load.
        [Header("Flat World Config (legacy — sphere uses CelestialBody instead)")]
        public int   flatSeed           = 1337;
        public int   flatSeaLevel       = 96;
        public int   flatBaseHeight     = 100;
        public float flatContinentScale = 0.0015f;
        public int   flatCrustDepth     = 40;
        public BiomeRegistry flatBiomeRegistry;

        // Backward-compat property used by IVoxelWorld + scatter.
        public MaterialRegistry  materialRegistry;
        public Material          terrainMaterial;     // URP Lit, vertex-colour driven

        [Header("Streaming")]
        public Transform viewer;
        [Range(1, 16)] public int viewDistance = VoxelConstants.DEFAULT_VIEW_DISTANCE;
        [Range(1, 16)] public int maxJobsPerFrame = 4;
        public bool generateColliders = true;
        [Tooltip("Spawn trees/rocks from biome scatter lists.")]
        public bool enableScatter = true;

        [Header("Persistence")]
        [Tooltip("Folder name under persistentDataPath/VoxelWorlds/. Different names = different save slots.")]
        public string worldName = "DefaultWorld";
        [Tooltip("Persist chunks the player has modified to disk.")]
        public bool enablePersistence = true;

        // ---- Singleton-ish access (handy for modification system) ----
        public static VoxelWorld Instance { get; private set; }

        /// <summary>Voxel sea level (IChunkScatterWorld contract; also used by scatter).</summary>
        public int SeaLevel => flatSeaLevel;

        // ── IVoxelWorld explicit properties ──
        // Delegate to the existing inspector-assigned fields so the flat world satisfies the
        // interface without any behavior change.
        MaterialRegistry IVoxelWorld.MaterialRegistry => materialRegistry;
        Transform IVoxelWorld.Viewer => viewer;
        int IVoxelWorld.SeaLevel => SeaLevel;
        int IVoxelWorld.Seed => flatSeed;

        // ---- Runtime ----
        private readonly Dictionary<Vector3Int, Chunk> _chunks    = new();
        private readonly Queue<Chunk>                  _genQueue  = new();
        private readonly Queue<Chunk>                  _meshQueue = new();

        private ChunkPool _pool;
        private ChunkStorage _storage;
        private NativeArray<Color32>                   _materialColors;
        private NativeArray<OreLayer>                  _oreLayers;
        private NativeArray<BiomeData>                 _biomes;
        private NativeArray<VertexAttributeDescriptor> _vertexAttributes;

        // In-flight job tracking
        private readonly List<PendingGen>  _pendingGen  = new();
        private readonly List<PendingMesh> _pendingMesh = new();

        private struct PendingGen
        {
            public Chunk                chunk;
            public JobHandle            handle;
            public NativeArray<float>   heights;     // disposed after main gen job completes
            public NativeArray<int>     biomeIdx;
        }
        private struct PendingMesh
        {
            public Chunk                chunk;
            public JobHandle            handle;
            public Mesh.MeshDataArray   meshDataArray;
            public NativeArray<Bounds>  bounds;
            public NativeArray<int>     counts;
            public NativeArray<float3>  vertScratch;
            public NativeArray<float3>  normScratch;
            public NativeArray<Color32> colScratch;
            public NativeArray<int>     idxScratch;
            public NativeArray<int>     cellLut;
        }

        // ---- Lifecycle ----
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            // Volumetric water simulation.
            VoxelEngine.WaterSim.FluidManager.EnsureInstance();
            VoxelEngine.Fluids.FluidSimManager.EnsureInstance();

            // Auto-recover missing references (common after branch switches / scene reloads)
            if (materialRegistry == null)
                materialRegistry = Resources.Load<MaterialRegistry>("MaterialRegistry");
            if (flatBiomeRegistry == null)
                flatBiomeRegistry = Resources.Load<BiomeRegistry>("BiomeRegistry");
            if (terrainMaterial == null)
                terrainMaterial = Resources.Load<Material>("Mat_Terrain");

            if (materialRegistry == null || terrainMaterial == null)
            {
                Debug.LogWarning("[VoxelWorld] Missing required asset references. Assign MaterialRegistry and terrainMaterial in the inspector.");
                // Do not disable — allow the world to run with defaults if possible
            }

            // Apply main-menu session overrides (world name, seed, sliders) if present.
            // We DO NOT mutate the planet asset on disk — only the runtime instance.
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session != null)
            {
                worldName       = session.worldName;
                flatSeed           = session.seed;
                flatSeaLevel       = 96;
                flatBaseHeight     = 100;
                flatContinentScale = 0.0015f;
            }

            materialRegistry.Build();
            _pool = new ChunkPool(transform, terrainMaterial);

            _materialColors = new NativeArray<Color32>(256, Allocator.Persistent);
            for (int i = 0; i < 256; i++) _materialColors[i] = materialRegistry.GetColor((byte)i);

            var oreList = new List<OreLayer>
            {
                new OreLayer { material = MaterialId.Iron,     scale = 0.06f, threshold = 0.45f, minDepth = 4,   maxDepth = 80 },
                new OreLayer { material = MaterialId.Copper,   scale = 0.07f, threshold = 0.55f, minDepth = 6,   maxDepth = 70 },
                new OreLayer { material = MaterialId.Coal,     scale = 0.05f, threshold = 0.50f, minDepth = 4,   maxDepth = 60 },
                new OreLayer { material = MaterialId.Nickel,   scale = 0.08f, threshold = 0.60f, minDepth = 20,  maxDepth = 120 },
                new OreLayer { material = MaterialId.Silicon,  scale = 0.06f, threshold = 0.55f, minDepth = 4,   maxDepth = 90 },
                new OreLayer { material = MaterialId.Cobalt,   scale = 0.09f, threshold = 0.65f, minDepth = 30,  maxDepth = 140 },
                new OreLayer { material = MaterialId.Magnesium,scale = 0.08f, threshold = 0.62f, minDepth = 15,  maxDepth = 110 },
                new OreLayer { material = MaterialId.Silver,   scale = 0.10f, threshold = 0.72f, minDepth = 60,  maxDepth = 200 },
                new OreLayer { material = MaterialId.Gold,     scale = 0.11f, threshold = 0.78f, minDepth = 80,  maxDepth = 220 },
                new OreLayer { material = MaterialId.Platinum, scale = 0.12f, threshold = 0.80f, minDepth = 100, maxDepth = 240 },
                new OreLayer { material = MaterialId.Uranium,  scale = 0.13f, threshold = 0.82f, minDepth = 120, maxDepth = 250 },
                new OreLayer { material = MaterialId.CrudeOil, scale = 0.04f, threshold = 0.70f, minDepth = 25,  maxDepth = 90 },
                new OreLayer { material = MaterialId.Ice,      scale = 0.05f, threshold = 0.65f, minDepth = 0,   maxDepth = 12 }
            };
            _oreLayers = new NativeArray<OreLayer>(oreList.Count, Allocator.Persistent);
            for (int i = 0; i < oreList.Count; i++) _oreLayers[i] = oreList[i];

            // Pack biomes into Burst-friendly POD array.
            int biomeCount = (flatBiomeRegistry != null) ? flatBiomeRegistry.biomes.Count : 0;
            if (biomeCount == 0)
            {
                Debug.LogWarning("[VoxelWorld] flatBiomeRegistry is empty — falling back to a single Plains biome.");
                _biomes = new NativeArray<BiomeData>(1, Allocator.Persistent);
                _biomes[0] = new BiomeData
                {
                    tempRange = new Unity.Mathematics.float2(0, 1),
                    humidRange = new Unity.Mathematics.float2(0, 1),
                    priority = 0,
                    heightOffset = 0, heightAmplitude = 12, heightFrequency = 0.02f, ridgedness = 0,
                    surfaceMat = (byte)MaterialId.Clay, surfaceDepth = 1,
                    subsurfaceMat = (byte)MaterialId.Clay, subsurfaceDepth = 4,
                    allowBeach = 1, isOceanic = 0
                };
            }
            else
            {
                _biomes = new NativeArray<BiomeData>(biomeCount, Allocator.Persistent);
                for (int i = 0; i < biomeCount; i++)
                {
                    var def = flatBiomeRegistry.biomes[i];
                    _biomes[i] = def != null ? BiomeData.FromDefinition(def) : default;
                }
            }

            _vertexAttributes = new NativeArray<VertexAttributeDescriptor>(3, Allocator.Persistent);
            _vertexAttributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0);
            _vertexAttributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal,   VertexAttributeFormat.Float32, 3, 0);
            _vertexAttributes[2] = new VertexAttributeDescriptor(VertexAttribute.Color,    VertexAttributeFormat.UNorm8,  4, 0);

            if (enablePersistence)
                _storage = new ChunkStorage(string.IsNullOrEmpty(worldName) ? "DefaultWorld" : worldName);

            // Honour user setting for view distance, then track changes live.
            ApplyUserSettings();
            VoxelEngine.Settings.GameSettings.OnChanged += ApplyUserSettings;
        }

        private void ApplyUserSettings()
        {
            int desired = VoxelEngine.Settings.GameSettings.ViewDistance;
            if (desired > 0) viewDistance = desired;
        }

        private void OnApplicationQuit()
        {
            if (_storage == null) return;
            foreach (var kv in _chunks)
                if (kv.Value.isModified) _storage.EnqueueSave(kv.Value);
            _storage.WaitForIdle();
        }

        private void OnDestroy()
        {
            foreach (var p in _pendingGen)
            {
                p.handle.Complete();
                if (p.heights.IsCreated)  p.heights.Dispose();
                if (p.biomeIdx.IsCreated) p.biomeIdx.Dispose();
            }
            foreach (var p in _pendingMesh) DisposePendingMesh(p, complete:true);
            _pendingGen.Clear();
            _pendingMesh.Clear();

            if (_storage != null)
            {
                // Flush every modified chunk that's still in memory.
                foreach (var kv in _chunks)
                    if (kv.Value.isModified) _storage.EnqueueSave(kv.Value);
                _storage.WaitForIdle();
                _storage.Shutdown();
                _storage = null;
            }

            _pool?.DisposeAll(_chunks.Values);
            _chunks.Clear();
            if (_materialColors.IsCreated)   _materialColors.Dispose();
            if (_oreLayers.IsCreated)        _oreLayers.Dispose();
            if (_biomes.IsCreated)           _biomes.Dispose();
            if (_vertexAttributes.IsCreated) _vertexAttributes.Dispose();
            VoxelEngine.Settings.GameSettings.OnChanged -= ApplyUserSettings;
            if (Instance == this) Instance = null;
        }

        // ---- Streaming ----
        private void Update()
        {
            if (viewer == null) return;
            UpdateStreaming();
            var pt = VoxelEngine.Performance.PerformanceThrottle.Instance;
            int wmBudget = pt != null ? pt.waterMeshBudget : 2;
            VoxelEngine.WaterSim.WaterMeshBuilder.Pump(wmBudget);
            DispatchGenerationJobs();
            DispatchMeshingJobs();
            CompleteFinishedJobs();
            ProcessDeferredScatter();
        }

        // Reusable scratch lists (no per-frame GC).
        private readonly List<(Vector3Int coord, int distSq)> _loadCandidates = new();
        private readonly List<Vector3Int>                     _evictList      = new();

        private void UpdateStreaming()
        {
            Vector3Int center = WorldToChunk(viewer.position);
            int r        = viewDistance;
            int loadR2   = r * r;
            // Hysteresis: don't evict until well outside the load radius. This avoids
            // the "chunk in front of me unloads as I walk into it" flicker.
            int evictR2  = (r + 3) * (r + 3);

            // ---- 1. Find chunks to load, sorted by distance (closest first) ----
            _loadCandidates.Clear();
            for (int z = -r; z <= r; z++)
            for (int x = -r; x <= r; x++)
            {
                int d2 = x * x + z * z;
                if (d2 > loadR2) continue;

                for (int y = 0; y < VoxelConstants.WORLD_HEIGHT_CHUNKS; y++)
                {
                    var c = new Vector3Int(center.x + x, y, center.z + z);
                    if (_chunks.ContainsKey(c)) continue;
                    _loadCandidates.Add((c, d2));
                }
            }
            // Sort by horizontal distance squared, then vertically (low Y first looks more natural).
            _loadCandidates.Sort((a, b) =>
            {
                int cmp = a.distSq.CompareTo(b.distSq);
                if (cmp != 0) return cmp;
                return a.coord.y.CompareTo(b.coord.y);
            });
            for (int i = 0; i < _loadCandidates.Count; i++)
            {
                var c = _loadCandidates[i].coord;
                var chunk = _pool.Rent(c);
                _chunks.Add(c, chunk);
                _genQueue.Enqueue(chunk);
            }

            // ---- 2. Evict chunks that are well outside the view radius ----
            _evictList.Clear();
            foreach (var kv in _chunks)
            {
                int dx = kv.Key.x - center.x;
                int dz = kv.Key.z - center.z;
                if (dx * dx + dz * dz > evictR2)
                    _evictList.Add(kv.Key);
            }
            for (int i = 0; i < _evictList.Count; i++)
            {
                var k  = _evictList[i];
                var ch = _chunks[k];
                // Finish any in-flight jobs touching this chunk's voxel buffer BEFORE the
                // pool reuses/disposes it — otherwise the running job writes to freed memory.
                CompleteGenJobFor(ch);
                CompleteMeshJobFor(ch);
                if (_storage != null && ch.isModified) _storage.EnqueueSave(ch);
                _pool.Return(ch);
                _chunks.Remove(k);
            }
        }

        // ---- Generation jobs ----
        private void DispatchGenerationJobs()
        {
            int budget = maxJobsPerFrame;
            while (budget-- > 0 && _genQueue.Count > 0)
            {
                var chunk = _genQueue.Dequeue();
                if (chunk == null || !chunk.go.activeSelf) continue;

                // The new ChunkGenJob WRITES chunk.voxels. Any prior gen job (write) or mesh
                // job (read) on this same pooled buffer MUST finish first, or the Job System
                // throws a read/write dependency violation. (Happens on pool reuse / requeue.)
                CompleteGenJobFor(chunk);
                CompleteMeshJobFor(chunk);

                // Fast path: load from disk if we've saved this chunk before.
                if (_storage != null && _storage.TryLoadChunk(chunk.coord, chunk))
                {
                    chunk.isGenerated = true;
                    chunk.isModified  = false; // freshly loaded == matches disk
                    chunk.isScattered = false;
                    // Seed water for loaded chunks.
                    {
                        const int CS2 = VoxelConstants.CHUNK_SIZE;
                        bool hw = false;
                        for (int wz2 = 0; wz2 < CS2; wz2++)
                        for (int wy2 = 0; wy2 < CS2; wy2++)
                        for (int wx2 = 0; wx2 < CS2; wx2++)
                        {
                            var wv2 = chunk.GetVoxelLocal(wx2, wy2, wz2);
                            if (wv2.density > 0 && wv2.material == (byte)Materials.MaterialId.WaterVoxel)
                            {
                                chunk.SetVoxelLocal(wx2, wy2, wz2, new Voxel(-1, (byte)Materials.MaterialId.WaterLiquid, 255));
                                hw = true;
                            }
                            if (wv2.waterLevel > 0) hw = true;
                        }
                        if (hw) { WaterSim.FluidManager.Instance?.MarkActive(chunk.coord); WaterSim.WaterMeshBuilder.Schedule(chunk); }
                    }
                    StitchBordersWithNeighbours(chunk);
                    _meshQueue.Enqueue(chunk);
                    continue;
                }

                int origX = chunk.coord.x * VoxelConstants.CHUNK_SIZE;
                int origY = chunk.coord.y * VoxelConstants.CHUNK_SIZE;
                int origZ = chunk.coord.z * VoxelConstants.CHUNK_SIZE;
                int colCount = VoxelConstants.CHUNK_SIZE_P * VoxelConstants.CHUNK_SIZE_P;

                // 1) Compute the heightmap once for this chunk's footprint.
                var heights  = new NativeArray<float>(colCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var biomeIds = new NativeArray<int>  (colCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                var heightJob = new ChunkHeightJob
                {
                    chunkOriginVoxels = new int3(origX, origY, origZ),
                    seed              = flatSeed,
                    seaLevel          = flatSeaLevel,
                    baseHeight        = flatBaseHeight,
                    continentScale    = flatContinentScale,
                    biomes            = _biomes,
                    heights           = heights,
                    biomeIdx          = biomeIds
                };
                var heightHandle = heightJob.Schedule(colCount, 32);

                // 2) Per-voxel job depends on height job.
                var job = new ChunkGenJob
                {
                    chunkOriginVoxels = new int3(origX, origY, origZ),
                    seed              = flatSeed,
                    seaLevel          = flatSeaLevel,
                    baseHeight        = flatBaseHeight,
                    continentScale    = flatContinentScale,
                    crustDepth        = flatCrustDepth,
                    biomes            = _biomes,
                    ores              = _oreLayers,
                    heights           = heights,
                    biomeIdx          = biomeIds,
                    voxels            = chunk.voxels
                };
                var handle = job.Schedule(VoxelConstants.VOXELS_PER_CHUNK_P, 64, heightHandle);

                _pendingGen.Add(new PendingGen
                {
                    chunk    = chunk,
                    handle   = handle,
                    heights  = heights,
                    biomeIdx = biomeIds
                });
            }
        }

        // ---- Mesh jobs ----
        private void DispatchMeshingJobs()
        {
            int budget = maxJobsPerFrame;
            int requeue = 0;
            int queuedAtStart = _meshQueue.Count;
            while (budget > 0 && _meshQueue.Count > 0 && requeue < queuedAtStart)
            {
                var chunk = _meshQueue.Dequeue();
                if (chunk == null || !chunk.go.activeSelf || !chunk.isGenerated) continue;

                // Only mesh once horizontal neighbours have been stitched in.
                // Gate avoids the "biome flicker in the sky" caused by meshing with empty borders.
                bool readyOrTimeout = AreNeighboursReady(chunk) ||
                                      (Time.time - chunk.genCompletedTime) > 0.5f;
                if (!readyOrTimeout)
                {
                    _meshQueue.Enqueue(chunk);
                    requeue++;
                    continue;
                }

                ScheduleMeshJob(chunk);
                budget--;
            }
        }

        private bool AreNeighboursReady(Chunk c)
        {
            // We only require the four horizontal neighbours (most common seam direction).
            return  HasGenerated(c.coord + new Vector3Int(-1, 0,  0)) &&
                    HasGenerated(c.coord + new Vector3Int( 1, 0,  0)) &&
                    HasGenerated(c.coord + new Vector3Int( 0, 0, -1)) &&
                    HasGenerated(c.coord + new Vector3Int( 0, 0,  1));
        }
        private bool HasGenerated(Vector3Int coord) =>
            _chunks.TryGetValue(coord, out var n) && n.isGenerated;

        public void ScheduleMeshJob(Chunk chunk)
        {
            // Skip if this chunk already has a mesh job in flight (avoids race & double-allocation).
            for (int i = 0; i < _pendingMesh.Count; i++)
                if (_pendingMesh[i].chunk == chunk) return;

            // The SurfaceNetsJob READS chunk.voxels — make sure the ChunkGenJob that WRITES
            // them has finished first, otherwise the Job System throws a dependency violation.
            CompleteGenJobFor(chunk);

            chunk.isDirty = false;

            const int CELLS = (VoxelConstants.CHUNK_SIZE + 1) *
                              (VoxelConstants.CHUNK_SIZE + 1) *
                              (VoxelConstants.CHUNK_SIZE + 1);
            int maxVerts = CELLS;
            int maxIdx   = CELLS * 18;

            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            var pending = new PendingMesh
            {
                chunk         = chunk,
                meshDataArray = meshDataArray,
                bounds        = new NativeArray<Bounds>(1, Allocator.TempJob),
                counts        = new NativeArray<int>(2, Allocator.TempJob),
                vertScratch   = new NativeArray<float3>(maxVerts, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
                normScratch   = new NativeArray<float3>(maxVerts, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
                colScratch    = new NativeArray<Color32>(maxVerts, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
                idxScratch    = new NativeArray<int>(maxIdx, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
                cellLut       = new NativeArray<int>(CELLS, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
            };

            var job = new SurfaceNetsJob
            {
                voxels          = chunk.voxels,
                meshData        = meshDataArray[0],
                bounds          = pending.bounds,
                counts          = pending.counts,
                vertexScratch   = pending.vertScratch,
                normalScratch   = pending.normScratch,
                colorScratch    = pending.colScratch,
                indexScratch    = pending.idxScratch,
                cellVertexIndex  = pending.cellLut,
                materialColors   = _materialColors,
                vertexAttributes = _vertexAttributes,
                isSphere         = false,
                chunkOrigin      = new float3(chunk.coord.x, chunk.coord.y, chunk.coord.z) * VoxelConstants.CHUNK_SIZE
            };
            pending.handle = job.Schedule();
            _pendingMesh.Add(pending);
        }

        private void CompleteFinishedJobs()
        {
            for (int i = _pendingGen.Count - 1; i >= 0; i--)
            {
                var p = _pendingGen[i];
                if (!p.handle.IsCompleted) continue;
                _pendingGen.RemoveAt(i);   // remove BEFORE finalize so re-entrant completes can't double-process
                FinalizeGen(p);
            }

            for (int i = _pendingMesh.Count - 1; i >= 0; i--)
            {
                var p = _pendingMesh[i];
                if (!p.handle.IsCompleted) continue;
                p.handle.Complete();

                var mesh = p.chunk.mesh;
                mesh.Clear();
                Mesh.ApplyAndDisposeWritableMeshData(
                    p.meshDataArray, mesh,
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

                mesh.bounds = p.bounds[0];

                p.chunk.meshFilter.sharedMesh = mesh;
                if (generateColliders && p.counts[1] > 0)
                    p.chunk.meshCollider.sharedMesh = mesh;
                else if (p.counts[1] == 0)
                    p.chunk.meshCollider.sharedMesh = null;

                DisposePendingMesh(p, complete:false);
                _pendingMesh.RemoveAt(i);
            }
        }

        private static void DisposePendingMesh(PendingMesh p, bool complete)
        {
            if (complete) p.handle.Complete();
            if (p.bounds.IsCreated)      p.bounds.Dispose();
            if (p.counts.IsCreated)      p.counts.Dispose();
            if (p.vertScratch.IsCreated) p.vertScratch.Dispose();
            if (p.normScratch.IsCreated) p.normScratch.Dispose();
            if (p.colScratch.IsCreated)  p.colScratch.Dispose();
            if (p.idxScratch.IsCreated)  p.idxScratch.Dispose();
            if (p.cellLut.IsCreated)     p.cellLut.Dispose();
        }

        private void ProcessDeferredScatter()
        {
            if (!enableScatter || flatBiomeRegistry == null) return;

            foreach (var kv in _chunks)
            {
                var c = kv.Value;
                if (!c.isGenerated || c.isScattered) continue;
                // Wait until the chunk has a visible mesh (prevents scatter floating in air).
                if (c.meshFilter == null || c.meshFilter.sharedMesh == null) continue;

                // We can scatter once we know the chunk directly above (if any) is generated.
                // Topmost chunks have no chunk above and can scatter immediately.
                bool topmost = c.coord.y >= VoxelConstants.WORLD_HEIGHT_CHUNKS - 1;
                if (!topmost)
                {
                    if (!_chunks.TryGetValue(c.coord + new Vector3Int(0, 1, 0), out var above))
                        continue;             // above-chunk not yet streamed in
                    if (!above.isGenerated)
                        continue;             // above-chunk still generating
                }

                ChunkScatter.Populate(this, c, flatBiomeRegistry, flatSeed);
                c.isScattered = true;
            }
        }

        // ---- Public API ----
        public Vector3Int WorldToChunk(Vector3 worldPos)
        {
            float s = VoxelConstants.CHUNK_SIZE * VoxelConstants.VOXEL_SIZE;
            return new Vector3Int(
                Mathf.FloorToInt(worldPos.x / s),
                Mathf.FloorToInt(worldPos.y / s),
                Mathf.FloorToInt(worldPos.z / s));
        }

        public Vector3Int WorldToVoxel(Vector3 worldPos) =>
            new Vector3Int(
                Mathf.FloorToInt(worldPos.x / VoxelConstants.VOXEL_SIZE),
                Mathf.FloorToInt(worldPos.y / VoxelConstants.VOXEL_SIZE),
                Mathf.FloorToInt(worldPos.z / VoxelConstants.VOXEL_SIZE));

        public bool TryGetChunk(Vector3Int coord, out Chunk chunk) => _chunks.TryGetValue(coord, out chunk);

        public Voxel GetVoxelWorld(Vector3Int worldVoxel)
        {
            var chunkCoord = new Vector3Int(
                Mathf.FloorToInt(worldVoxel.x / (float)VoxelConstants.CHUNK_SIZE),
                Mathf.FloorToInt(worldVoxel.y / (float)VoxelConstants.CHUNK_SIZE),
                Mathf.FloorToInt(worldVoxel.z / (float)VoxelConstants.CHUNK_SIZE));
            if (!_chunks.TryGetValue(chunkCoord, out var c) || !c.isGenerated) return Voxel.Empty;

            // Safety: callers like generation decorators, pumps, swimming, and UI scans
            // can query a chunk during the same frame its ChunkGenJob completed. Complete
            // any outstanding jobs before reading the NativeArray to satisfy Unity's job
            // safety system.
            CompleteGenJobFor(c);
            CompleteMeshJobFor(c);

            int lx = worldVoxel.x - chunkCoord.x * VoxelConstants.CHUNK_SIZE;
            int ly = worldVoxel.y - chunkCoord.y * VoxelConstants.CHUNK_SIZE;
            int lz = worldVoxel.z - chunkCoord.z * VoxelConstants.CHUNK_SIZE;
            return c.GetVoxelLocal(lx, ly, lz);
        }

        public void SetVoxelWorld(Vector3Int worldVoxel, Voxel v, bool remesh = true)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            var chunkCoord = new Vector3Int(
                Mathf.FloorToInt(worldVoxel.x / (float)S),
                Mathf.FloorToInt(worldVoxel.y / (float)S),
                Mathf.FloorToInt(worldVoxel.z / (float)S));
            if (!_chunks.TryGetValue(chunkCoord, out var c) || !c.isGenerated) return;

            int lx = worldVoxel.x - chunkCoord.x * S;
            int ly = worldVoxel.y - chunkCoord.y * S;
            int lz = worldVoxel.z - chunkCoord.z * S;

            // CRITICAL: complete any in-flight GEN job (writes voxels) AND MESH job (reads
            // voxels) for this chunk before mutating its voxels — the Burst/Jobs safety
            // system would otherwise throw because a job still holds a handle on chunk.voxels.
            CompleteGenJobFor(c);
            CompleteMeshJobFor(c);

            c.SetVoxelLocal(lx, ly, lz, v);
            c.isModified = true;

            // Wake fluid sim — but only when the edited voxel or its immediate neighbours
            // are already fluid-adjacent. This prevents every mining action on dry land from
            // instantly turning into a global "water wants to flood here" hole search.
            bool nearFluid = VoxelEngine.WaterSim.FluidMaterialUtility.IsFluid(v)
                             || HasFluidAdjacent(worldVoxel);
            if (nearFluid)
            {
                var fm = WaterSim.FluidManager.Instance;
                if (fm != null)
                {
                    for (int wz = -1; wz <= 1; wz++)
                    for (int wy = -1; wy <= 1; wy++)
                    for (int wx = -1; wx <= 1; wx++)
                        fm.MarkActive(chunkCoord + new Vector3Int(wx, wy, wz));
                }
                WaterSim.WaterMeshBuilder.Schedule(c);
            }

            // Mirror the write into the padded border of any neighbour chunks that share
            // this voxel, otherwise their meshing job would see stale data and the seam
            // between chunks would not update correctly.
            MirrorIntoNeighbour(chunkCoord, lx, ly, lz, v, -1,  0,  0);
            MirrorIntoNeighbour(chunkCoord, lx, ly, lz, v,  1,  0,  0);
            MirrorIntoNeighbour(chunkCoord, lx, ly, lz, v,  0, -1,  0);
            MirrorIntoNeighbour(chunkCoord, lx, ly, lz, v,  0,  1,  0);
            MirrorIntoNeighbour(chunkCoord, lx, ly, lz, v,  0,  0, -1);
            MirrorIntoNeighbour(chunkCoord, lx, ly, lz, v,  0,  0,  1);

            if (!remesh) return;

            EnqueueRemesh(c);
            if (lx == 0)         EnqueueRemeshNeighbour(chunkCoord + new Vector3Int(-1, 0, 0));
            if (lx == S - 1)     EnqueueRemeshNeighbour(chunkCoord + new Vector3Int( 1, 0, 0));
            if (ly == 0)         EnqueueRemeshNeighbour(chunkCoord + new Vector3Int( 0,-1, 0));
            if (ly == S - 1)     EnqueueRemeshNeighbour(chunkCoord + new Vector3Int( 0, 1, 0));
            if (lz == 0)         EnqueueRemeshNeighbour(chunkCoord + new Vector3Int( 0, 0,-1));
            if (lz == S - 1)     EnqueueRemeshNeighbour(chunkCoord + new Vector3Int( 0, 0, 1));
        }

        // Write 'v' into the padded-border slot of the neighbour at (dx,dy,dz) so the
        // neighbour's mesher sees an identical value across the shared face.
        private void MirrorIntoNeighbour(Vector3Int chunkCoord, int lx, int ly, int lz, Voxel v,
                                         int dx, int dy, int dz)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            // Only mirror when the modified voxel actually lies on the corresponding face.
            if (dx == -1 && lx != 0)     return;
            if (dx ==  1 && lx != S - 1) return;
            if (dy == -1 && ly != 0)     return;
            if (dy ==  1 && ly != S - 1) return;
            if (dz == -1 && lz != 0)     return;
            if (dz ==  1 && lz != S - 1) return;

            var nCoord = chunkCoord + new Vector3Int(dx, dy, dz);
            if (!_chunks.TryGetValue(nCoord, out var n) || !n.isGenerated) return;

            CompleteGenJobFor(n);
            CompleteMeshJobFor(n);

            // Compute the neighbour-local coordinates of this same world voxel.
            int nx = lx - dx * S;
            int ny = ly - dy * S;
            int nz = lz - dz * S;
            // Use the padded indexer (it accepts -1..S as valid border slots).
            const int SP = VoxelConstants.CHUNK_SIZE_P;
            int paddedIdx = (nx + 1) + (ny + 1) * SP + (nz + 1) * SP * SP;
            n.voxels[paddedIdx] = v;
            n.isModified = true;
        }

        // Copies the 1-voxel border between this newly-generated chunk and any neighbour
        // that is already generated. Both sides get updated, and any neighbours whose own
        // borders changed are re-queued for meshing. Called immediately after generation
        // completes — fixes "biome flicker" at chunk seams.
        private void StitchBordersWithNeighbours(Chunk c)
        {
            const int S  = VoxelConstants.CHUNK_SIZE;
            const int SP = VoxelConstants.CHUNK_SIZE_P;

            // Helper inlined as local: padded index for (x+1, y+1, z+1)-shifted coords.
            int Pad(int x, int y, int z) => (x + 1) + (y + 1) * SP + (z + 1) * SP * SP;

            // Six axis-aligned neighbours.
            for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3Int off = Vector3Int.zero;
                if (axis == 0) off.x = sign;
                else if (axis == 1) off.y = sign;
                else                off.z = sign;

                if (!_chunks.TryGetValue(c.coord + off, out var n) || !n.isGenerated)
                    continue;

                // Make sure neither side has an in-flight GEN job (writes voxels) or MESH job
                // (reads voxels) that'd race with the border copy below.
                CompleteGenJobFor(c);
                CompleteGenJobFor(n);
                CompleteMeshJobFor(c);
                CompleteMeshJobFor(n);

                // Copy the *real* face of one chunk into the *padded border* of the other.
                if (axis == 0)
                {
                    // X axis: face x = (sign>0 ? S-1 : 0) of c <-> padded x = (sign>0 ? S : -1) of n
                    int faceCx = sign > 0 ? S - 1 : 0;
                    int padCx  = sign > 0 ? S      : -1;
                    int faceNx = sign > 0 ? 0      : S - 1;
                    int padNx  = sign > 0 ? -1     : S;
                    for (int y = 0; y < S; y++)
                    for (int z = 0; z < S; z++)
                    {
                        // c's border <- n's face
                        c.voxels[Pad(padCx, y, z)] = n.voxels[Pad(faceNx, y, z)];
                        // n's border <- c's face
                        n.voxels[Pad(padNx, y, z)] = c.voxels[Pad(faceCx, y, z)];
                    }
                }
                else if (axis == 1)
                {
                    int faceCy = sign > 0 ? S - 1 : 0;
                    int padCy  = sign > 0 ? S      : -1;
                    int faceNy = sign > 0 ? 0      : S - 1;
                    int padNy  = sign > 0 ? -1     : S;
                    for (int x = 0; x < S; x++)
                    for (int z = 0; z < S; z++)
                    {
                        c.voxels[Pad(x, padCy, z)] = n.voxels[Pad(x, faceNy, z)];
                        n.voxels[Pad(x, padNy, z)] = c.voxels[Pad(x, faceCy, z)];
                    }
                }
                else
                {
                    int faceCz = sign > 0 ? S - 1 : 0;
                    int padCz  = sign > 0 ? S      : -1;
                    int faceNz = sign > 0 ? 0      : S - 1;
                    int padNz  = sign > 0 ? -1     : S;
                    for (int x = 0; x < S; x++)
                    for (int y = 0; y < S; y++)
                    {
                        c.voxels[Pad(x, y, padCz)] = n.voxels[Pad(x, y, faceNz)];
                        n.voxels[Pad(x, y, padNz)] = c.voxels[Pad(x, y, faceCz)];
                    }
                }

                // The neighbour's border slots changed -> remesh it too.
                if (!_meshQueue.Contains(n)) _meshQueue.Enqueue(n);
            }
        }

        /// <summary>
        /// Block until any pending mesh job (SurfaceNetsJob) that READS this chunk's
        /// voxel NativeArray completes, then finalize it. Public so other systems
        /// — most importantly the fluid sim — can call it BEFORE scheduling a job
        /// that WRITES to the same voxel buffer, satisfying Unity's Job-System
        /// dependency safety check.
        /// </summary>
        public void CompleteMeshJobForChunk(Chunk chunk) => CompleteMeshJobFor(chunk);

        /// <summary>Block until the in-flight <c>ChunkGenJob</c> that WRITES this chunk's voxel
        /// NativeArray completes, then finalize it. Call this before ANY code reads or writes
        /// <c>chunk.voxels</c> (stitching, meshing, modifying) so we never touch the buffer
        /// while the generation job is mid-write — that was the source of the Job-System
        /// "you must call JobHandle.Complete()" / dependency-safety exceptions.</summary>
        public void CompleteGenJobForChunk(Chunk chunk) => CompleteGenJobFor(chunk);

        private void CompleteGenJobFor(Chunk chunk)
        {
            if (chunk == null) return;
            for (int i = _pendingGen.Count - 1; i >= 0; i--)
            {
                if (_pendingGen[i].chunk != chunk) continue;
                var p = _pendingGen[i];
                _pendingGen.RemoveAt(i);  // remove first so FinalizeGen's stitch can't recurse onto this entry
                FinalizeGen(p);
                return;
            }
        }

        // Force-completes an in-flight generation job and applies all its side-effects
        // (mark generated, stitch borders, wake fluids, dispose temp arrays, queue mesh).
        // Shared by the per-frame CompleteFinishedJobs poll and the on-demand CompleteGenJobFor.
        private void FinalizeGen(PendingGen p)
        {
            p.handle.Complete();
            p.chunk.isGenerated = true;
            p.chunk.genCompletedTime = Time.time;
            p.chunk.isScattered = false; // (re)evaluate scatter once neighbours are ready

            // Natural crude oil reservoirs: crude-oil ore markers become a surface
            // seep + vertical funnel + deep pool, filled by the unified liquid sim.
            VoxelEngine.Generation.OilReservoirDecorator.Decorate(p.chunk, this);

            // Stitch borders for seamless meshing.
            StitchBordersWithNeighbours(p.chunk);

            // Wake fluid sim for chunks that have water (waterLevel > 0).
            {
                bool hw = false;
                const int WS = VoxelConstants.CHUNK_SIZE;
                for (int wz = 0; wz < WS && !hw; wz++)
                for (int wy = 0; wy < WS && !hw; wy++)
                for (int wx = 0; wx < WS && !hw; wx++)
                {
                    if (p.chunk.GetVoxelLocal(wx, wy, wz).waterLevel > 0) hw = true;
                }
                if (hw)
                {
                    WaterSim.FluidManager.Instance?.MarkActive(p.chunk.coord);
                    WaterSim.WaterMeshBuilder.Schedule(p.chunk);
                }
            }

            if (p.heights.IsCreated)  p.heights.Dispose();
            if (p.biomeIdx.IsCreated) p.biomeIdx.Dispose();

            if (!_meshQueue.Contains(p.chunk)) _meshQueue.Enqueue(p.chunk);
        }

        // Block until any pending mesh job for 'chunk' completes (and finalize it).
        private void CompleteMeshJobFor(Chunk chunk)
        {
            for (int i = _pendingMesh.Count - 1; i >= 0; i--)
            {
                var p = _pendingMesh[i];
                if (p.chunk != chunk) continue;

                p.handle.Complete();
                var mesh = p.chunk.mesh;
                mesh.Clear();
                Mesh.ApplyAndDisposeWritableMeshData(
                    p.meshDataArray, mesh,
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                mesh.bounds = p.bounds[0];
                p.chunk.meshFilter.sharedMesh = mesh;
                if (generateColliders && p.counts[1] > 0) p.chunk.meshCollider.sharedMesh = mesh;
                else if (p.counts[1] == 0)                p.chunk.meshCollider.sharedMesh = null;
                DisposePendingMesh(p, complete:false);
                _pendingMesh.RemoveAt(i);
            }
        }

        private void EnqueueRemesh(Chunk c)
        {
            if (!_meshQueue.Contains(c)) _meshQueue.Enqueue(c);
        }

        private void EnqueueRemeshNeighbour(Vector3Int coord)
        {
            if (_chunks.TryGetValue(coord, out var n) && n.isGenerated && !_meshQueue.Contains(n))
                _meshQueue.Enqueue(n);
        }
    }
}
