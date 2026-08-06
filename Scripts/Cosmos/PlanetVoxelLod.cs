// Assets/Scripts/VoxelEngine/Cosmos/PlanetVoxelLod.cs
//
// REAL voxel LOD surface for a spherical body — Space-Engineers style.
//
// The whole planet is generated as ACTUAL voxel chunks (SphereChunkGenJob +
// SurfaceNetsJob — the exact same pipeline as the gameplay world), at coarser
// voxel sizes for distance. There is no sampled impostor sphere: what you see
// from orbit IS the real terrain density field, just chunked bigger.
//
// Levels (voxel size → chunk size):
//   L3 FAR   — whole planet, adaptive 128–512 m voxels   (visible from space,
//              window 8,000 km — the whole interplanetary crossing)
//   L2 MID   — whole planet, adaptive 32–128 m voxels    (approach + surface,
//              window 150 km — quality-tier driven)
//   L1 NEAR  — 8 m voxel ring around the viewer          (low altitude, ~2 km)
//   L0 PLAY  — the existing SphereWorld 1 m gameplay bubble (unchanged)
//
// Because every level samples the SAME density field, continents, oceans,
// mountains, biomes and ore colours match exactly between levels — the only
// difference is voxel resolution. Levels nest: a finer level hides the coarser
// chunks fully inside its bubble (no gaps, no overlap).
//
// LOD chunks are VISUAL ONLY — no colliders (the PlanetLodImpostor safety
// shell keeps the planet solid), no scatter, no fluids, no persistence.
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

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Streams the real voxel surface of one celestial body at multiple LOD
    /// resolutions. One component per body (spawned by CosmosBootstrap as a
    /// child of the body); each body streams independently by viewer distance.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlanetVoxelLod : MonoBehaviour
    {
        [Header("Body & References")]
        public CelestialBody body;
        public Transform viewer;
        public Material terrainMaterial;
        public MaterialRegistry materialRegistry;
        public BiomeRegistry biomeRegistry;

        [Header("Level Windows")]
        [Tooltip("Distance (m) within which the FAR whole-planet level streams (128–512 m voxels).")]
        public float farWindowMeters = 8000000f;
        [Tooltip("Distance (m) within which the MID whole-planet level streams (32–128 m voxels).")]
        public float midWindowMeters = 150000f;
        [Tooltip("Altitude (m) below which the NEAR 8 m voxel ring streams around the viewer.")]
        public float nearAltitudeMeters = 4000f;
        [Tooltip("Hysteresis added to nearAltitudeMeters before the ring is torn down.")]
        public float nearAltitudeHysteresisMeters = 1500f;
        [Tooltip("Radius (m) of the NEAR 8 m voxel ring around the viewer.")]
        public float nearRadiusMeters = 2000f;

        [Header("Budget")]
        [Range(1, 12)] public int maxJobsPerFrame = 4;

        /// <summary>True once the body's real voxel surface has been built at least once
        /// (the sampled impostor bridge hides after this).</summary>
        public bool SurfaceReady { get; private set; }

        // ── Level descriptors ──────────────────────────────────────────
        private const int VoxelsPerChunkP = VoxelConstants.VOXELS_PER_CHUNK_P; // 34³

        private class LevelState
        {
            public float voxelSize;    // metres per voxel
            public float chunkSize;    // 32 * voxelSize
            public float halfDiag;     // chunkSize * √3 / 2
            public bool wholePlanet;   // true = shell band around the core, false = ring around viewer
            public readonly Dictionary<Vector3Int, LodChunk> active = new();
            public readonly Stack<LodChunk> pool = new();
            public readonly Queue<QueuedChunk> genQueue = new();
            public readonly Queue<QueuedChunk> meshQueue = new();
            public readonly List<PendingGen> pendingGen = new();
            public readonly List<PendingMesh> pendingMesh = new();
            public int targetCount;
            public int meshedCount;
            public bool isActive;
            public bool wasActive;
        }

        private class LodChunk
        {
            public Vector3Int coord;
            public int levelIndex;
            public int epoch;
            public GameObject go;
            public MeshFilter mf;
            public MeshRenderer mr;
            public Mesh mesh;
            public NativeArray<Voxel> voxels;
            public bool voxelsAllocated;
            public bool generated;
            public bool meshed;
            public bool inGenQueue;
            public bool inMeshQueue;
        }

        private readonly struct QueuedChunk
        {
            public readonly LodChunk chunk;
            public readonly int epoch;
            public QueuedChunk(LodChunk chunk) { this.chunk = chunk; epoch = chunk.epoch; }
        }

        private struct PendingGen
        {
            public LodChunk chunk; public int epoch; public JobHandle handle;
        }

        private struct PendingMesh
        {
            public LodChunk chunk; public int epoch; public JobHandle handle;
            public Mesh.MeshDataArray meshDataArray;
            public NativeArray<Bounds> bounds;
            public NativeArray<int> counts;
            public NativeArray<float3> vertScratch, normScratch;
            public NativeArray<Color32> colScratch;
            public NativeArray<int> idxScratch, cellLut;
        }

        private readonly LevelState[] _levels = { new(), new(), new() }; // far, mid, near

        private NativeArray<Color32> _materialColors;
        private NativeArray<OreLayer> _ores;
        private NativeArray<BiomeData> _biomes;
        private NativeArray<VertexAttributeDescriptor> _vertexAttributes;

        private Transform _chunkRoot;
        private bool _ready;
        private bool _nearActive;
        private float _diagTimer;
        private bool _logBuilt;

        private const int LevelFar  = 0;
        private const int LevelMid  = 1;
        private const int LevelNear = 2;

        private void Awake()
        {
            if (body == null) body = GetComponentInParent<CelestialBody>();
            if (body == null) { enabled = false; return; }
            if (materialRegistry == null) materialRegistry = Resources.Load<MaterialRegistry>("MaterialRegistry");
            if (terrainMaterial == null) terrainMaterial = Resources.Load<Material>("Mat_Terrain");
            body.ApplySettings();

            var rootGO = new GameObject("VoxelLodChunks");
            rootGO.transform.SetParent(body.transform, false);
            _chunkRoot = rootGO.transform;

            _materialColors = new NativeArray<Color32>(256, Allocator.Persistent);
            for (int i = 0; i < 256; i++)
                _materialColors[i] = materialRegistry != null ? materialRegistry.GetColor((byte)i) : Color.white;

            var oreArr = body.BuildOreLayers();
            _ores = new NativeArray<OreLayer>(oreArr.Length, Allocator.Persistent);
            for (int i = 0; i < oreArr.Length; i++) _ores[i] = oreArr[i];

            var biomeArr = body.BuildBiomeData(biomeRegistry);
            _biomes = new NativeArray<BiomeData>(biomeArr.Length, Allocator.Persistent);
            for (int i = 0; i < biomeArr.Length; i++) _biomes[i] = biomeArr[i];

            _vertexAttributes = new NativeArray<VertexAttributeDescriptor>(3, Allocator.Persistent);
            _vertexAttributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0);
            _vertexAttributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal,   VertexAttributeFormat.Float32, 3, 0);
            _vertexAttributes[2] = new VertexAttributeDescriptor(VertexAttribute.Color,    VertexAttributeFormat.UNorm8,  4, 0);

            ConfigureLevels();
            _ready = true;
        }

        /// <summary>
        /// Pick voxel sizes per level, adaptive to the body's radius so the whole-planet
        /// chunk count stays ~constant (≈ 700–770 mid chunks) on any planet size.
        /// </summary>
        private void ConfigureLevels()
        {
            float radius = body.SurfaceRadius;
            // Keep whole-planet chunk counts bounded as the radius grows: double the voxel
            // size each time the radius doubles past 12 km.
            float mult = Mathf.Pow(2f, Mathf.Ceil(Mathf.Max(0f, Mathf.Log(Mathf.Max(1f, radius / 12000f), 2f))));

            float farVoxel  = GraphicsPreset.PlanetFarLodVoxelSize * mult;
            float midVoxel  = GraphicsPreset.PlanetMidLodVoxelSize * mult;
            float nearVoxel = 8f;

            ConfigureLevel(LevelFar,  farVoxel,  wholePlanet: true);
            ConfigureLevel(LevelMid,  midVoxel,  wholePlanet: true);
            ConfigureLevel(LevelNear, nearVoxel, wholePlanet: false);

            Debug.Log($"[PlanetVoxelLod] '{body.DisplayName}': far {farVoxel:0}m / mid {midVoxel:0}m / near {nearVoxel:0}m voxels " +
                      $"(planet radius {radius / 1000f:0.#} km).");
        }

        private void ConfigureLevel(int index, float voxelSize, bool wholePlanet)
        {
            var l = _levels[index];
            l.voxelSize = Mathf.Max(2f, voxelSize);
            l.chunkSize = 32f * l.voxelSize;
            l.halfDiag = l.chunkSize * 0.8660254f; // √3/2
            l.wholePlanet = wholePlanet;
        }

        private void OnDestroy()
        {
            foreach (var l in _levels)
            {
                foreach (var p in l.pendingGen) p.handle.Complete();
                foreach (var p in l.pendingMesh) DisposePendingMesh(p, complete: true);
                l.pendingGen.Clear();
                l.pendingMesh.Clear();
                foreach (var kv in l.active) ReturnToPool(l, kv.Value);
                l.active.Clear();
                l.genQueue.Clear();
                l.meshQueue.Clear();
                while (l.pool.Count > 0) DestroyChunk(l.pool.Pop());
            }
            if (_materialColors.IsCreated) _materialColors.Dispose();
            if (_ores.IsCreated) _ores.Dispose();
            if (_biomes.IsCreated) _biomes.Dispose();
            if (_vertexAttributes.IsCreated) _vertexAttributes.Dispose();
        }

        // ── Per-frame streaming ───────────────────────────────────────
        private void Update()
        {
            if (!_ready || body == null) return;
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
            if (viewer == null) return;

            // Asteroid belts are procedural rock fields, not surface worlds.
            if (body.genParams.isAsteroidBelt == 1) return;

            UpdateLevelActivity();
            UpdateStreaming();
            DispatchJobs();
            CompleteJobs();
        }

        private void UpdateLevelActivity()
        {
            float distM = Vector3.Distance(viewer.position, body.transform.position);
            Vector3 viewerLocal = body.transform.InverseTransformPoint(viewer.position);
            float alt = viewerLocal.magnitude - body.SurfaceRadius;

            // FAR: whole planet within the interplanetary window. While MID is building,
            // FAR stays active so the planet is never a hole in the sky; once MID is
            // complete FAR steps aside (its chunks are fully covered by MID).
            bool midActive = distM < midWindowMeters;
            bool midComplete = _levels[LevelMid].isActive &&
                               _levels[LevelMid].targetCount > 0 &&
                               _levels[LevelMid].meshedCount >= _levels[LevelMid].targetCount;
            bool farActive = distM < farWindowMeters && !midComplete;

            // NEAR ring: only around the player on the streaming (active) body, low altitude.
            var sphere = SphereWorld.Instance;
            bool onThisBody = sphere != null && sphere.body == body;
            if (_nearActive)
                _nearActive = onThisBody && alt < nearAltitudeMeters + nearAltitudeHysteresisMeters;
            else
                _nearActive = onThisBody && alt < nearAltitudeMeters;

            _levels[LevelFar].isActive = farActive;
            _levels[LevelMid].isActive = midActive;
            _levels[LevelNear].isActive = _nearActive;

            if (_levels[LevelMid].isActive != _levels[LevelMid].wasActive)
            {
                _levels[LevelMid].wasActive = _levels[LevelMid].isActive;
                _levels[LevelMid].meshedCount = 0;
            }
            if (_levels[LevelFar].isActive != _levels[LevelFar].wasActive)
            {
                _levels[LevelFar].wasActive = _levels[LevelFar].isActive;
                _levels[LevelFar].meshedCount = 0;
            }
        }

        private readonly List<(Vector3Int coord, float distSq)> _candidates = new();
        private readonly List<Vector3Int> _evict = new();

        private void UpdateStreaming()
        {
            Vector3 viewerLocal = body.transform.InverseTransformPoint(viewer.position);
            float radius = body.SurfaceRadius;

            // L0 gameplay bubble (SphereWorld 1 m chunks) — near chunks fully inside it
            // are never streamed (the fine world renders there).
            float l0Radius = 0f;
            var sphere = SphereWorld.Instance;
            if (sphere != null && sphere.body == body)
                l0Radius = Mathf.Max(0f, sphere.viewDistance * VoxelConstants.CHUNK_SIZE * VoxelConstants.VOXEL_SIZE) + 64f;

            // 1) Compute desired coords per active level.
            for (int li = 0; li < _levels.Length; li++)
            {
                var l = _levels[li];
                l.targetCount = 0;

                // Inactive level → evict EVERYTHING (e.g. MID completes → FAR steps aside,
                // or the player climbs above the NEAR ring's altitude) and drop stale queues.
                if (!l.isActive)
                {
                    if (l.active.Count > 0)
                    {
                        _evict.Clear();
                        foreach (var kv in l.active) _evict.Add(kv.Key);
                        foreach (var c in _evict)
                        {
                            if (!l.active.TryGetValue(c, out var chunk)) continue;
                            CancelChunk(l, chunk);
                            l.active.Remove(c);
                            ReturnToPool(l, chunk);
                        }
                    }
                    l.genQueue.Clear();
                    l.meshQueue.Clear();
                    continue;
                }

                _candidates.Clear();
                if (l.wholePlanet)
                {
                    // Chunks whose box intersects the surface shell band [R − halfDiag, R + halfDiag].
                    int range = Mathf.CeilToInt((radius + l.halfDiag + 200f) / l.chunkSize);
                    for (int cz = -range; cz <= range; cz++)
                    for (int cy = -range; cy <= range; cy++)
                    for (int cx = -range; cx <= range; cx++)
                    {
                        var coord = new Vector3Int(cx, cy, cz);
                        float centerDist = coord.magnitude * l.chunkSize;
                        if (Mathf.Abs(centerDist - radius) > l.halfDiag + 200f) continue;
                        // Nested exclusion: chunks fully inside a finer level's bubble are skipped.
                        if (li == LevelFar)
                        {
                            // Covered by the MID whole-planet level once it is complete — but
                            // FAR stays active while MID builds, so no exclusion applies here.
                        }
                        else if (li == LevelMid && _levels[LevelNear].isActive)
                        {
                            if (DistToPlayerSq(coord, l, viewerLocal) + l.halfDiag < nearRadiusMeters) continue;
                        }
                        float d2 = DistToPlayerSq(coord, l, viewerLocal);
                        _candidates.Add((coord, d2));
                    }
                }
                else
                {
                    // NEAR ring: chunks intersecting the sphere of nearRadiusMeters around the viewer,
                    // minus the L0 gameplay bubble.
                    int range = Mathf.CeilToInt((nearRadiusMeters + l.halfDiag) / l.chunkSize);
                    Vector3Int vc = new(
                        Mathf.FloorToInt(viewerLocal.x / l.chunkSize),
                        Mathf.FloorToInt(viewerLocal.y / l.chunkSize),
                        Mathf.FloorToInt(viewerLocal.z / l.chunkSize));
                    for (int cz = vc.z - range; cz <= vc.z + range; cz++)
                    for (int cy = vc.y - range; cy <= vc.y + range; cy++)
                    for (int cx = vc.x - range; cx <= vc.x + range; cx++)
                    {
                        var coord = new Vector3Int(cx, cy, cz);
                        Vector3 center = new Vector3(coord.x + 0.5f, coord.y + 0.5f, coord.z + 0.5f) * l.chunkSize;
                        float d = Vector3.Distance(center, viewerLocal);
                        if (d > nearRadiusMeters + l.halfDiag) continue;
                        // Fully inside the fine gameplay bubble → the 1 m world renders there.
                        if (d + l.halfDiag < l0Radius) continue;
                        _candidates.Add((coord, (center - viewerLocal).sqrMagnitude));
                    }
                }

                _candidates.Sort((a, b) => a.distSq.CompareTo(b.distSq));

                // 2) Evict chunks no longer desired (with a small hysteresis margin).
                _evict.Clear();
                foreach (var kv in l.active)
                {
                    Vector3Int c = kv.Key;
                    float evictMargin = l.wholePlanet ? 0f : l.chunkSize * 2f;
                    bool keep = false;
                    if (l.wholePlanet)
                    {
                        float centerDist = c.magnitude * l.chunkSize;
                        keep = Mathf.Abs(centerDist - radius) <= l.halfDiag + 200f;
                    }
                    else
                    {
                        Vector3 center = new Vector3(c.x + 0.5f, c.y + 0.5f, c.z + 0.5f) * l.chunkSize;
                        float d = Vector3.Distance(center, viewerLocal);
                        keep = d <= nearRadiusMeters + l.halfDiag + evictMargin;
                    }
                    if (!keep) _evict.Add(c);
                }
                foreach (var c in _evict)
                {
                    if (!l.active.TryGetValue(c, out var chunk)) continue;
                    CancelChunk(l, chunk);
                    l.active.Remove(c);
                    ReturnToPool(l, chunk);
                }

                // 3) Rent new chunks, nearest-first, bounded by outstanding work.
                int outstanding = 0;
                foreach (var kv in l.active)
                    if (!kv.Value.generated) outstanding++;
                int budget = Mathf.Max(0, MaxOutstanding - outstanding);
                int spawned = 0;
                for (int i = 0; i < _candidates.Count && spawned < budget; i++)
                {
                    var (coord, _) = _candidates[i];
                    if (l.active.ContainsKey(coord)) continue;
                    var chunk = RentFromPool(l, coord);
                    chunk.go.transform.localPosition = new Vector3(coord.x, coord.y, coord.z) * l.chunkSize;
                    l.active.Add(coord, chunk);
                    QueueGen(l, chunk);
                    spawned++;
                }
                l.targetCount = l.active.Count;
            }
        }

        private static float DistToPlayerSq(Vector3Int coord, LevelState l, Vector3 viewerLocal)
        {
            Vector3 center = new Vector3(coord.x + 0.5f, coord.y + 0.5f, coord.z + 0.5f) * l.chunkSize;
            return (center - viewerLocal).sqrMagnitude;
        }

        private int MaxOutstanding => Mathf.Clamp(maxJobsPerFrame * 4, 8, 24);

        // ── Jobs ──────────────────────────────────────────────────────
        private void QueueGen(LevelState l, LodChunk chunk)
        {
            if (chunk.generated || chunk.inGenQueue) return;
            chunk.inGenQueue = true;
            l.genQueue.Enqueue(new QueuedChunk(chunk));
        }

        private void QueueMesh(LevelState l, LodChunk chunk)
        {
            if (!chunk.generated || chunk.meshed || chunk.inMeshQueue) return;
            chunk.inMeshQueue = true;
            l.meshQueue.Enqueue(new QueuedChunk(chunk));
        }

        private void DispatchJobs()
        {
            int genBudget = Mathf.Clamp((maxJobsPerFrame + 1) / 2, 2, 4);
            int meshBudget = Mathf.Clamp((maxJobsPerFrame + 3) / 4, 1, 3);

            // Priority: near → mid → far (the player's surroundings build first).
            int[] order = { LevelNear, LevelMid, LevelFar };

            foreach (int li in order)
            {
                var l = _levels[li];
                if (!l.isActive) continue;

                while (genBudget > 0 && l.genQueue.Count > 0)
                {
                    var q = l.genQueue.Dequeue();
                    var chunk = q.chunk;
                    if (chunk == null || chunk.epoch != q.epoch || chunk.generated) continue;
                    if (!l.active.TryGetValue(chunk.coord, out var live) || !ReferenceEquals(live, chunk)) continue;
                    chunk.inGenQueue = false;

                    // Recycle the voxel buffer on re-rent.
                    if (!chunk.voxelsAllocated)
                    {
                        chunk.voxels = new NativeArray<Voxel>(VoxelsPerChunkP, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                        chunk.voxelsAllocated = true;
                    }

                    float s = l.chunkSize;
                    var job = new SphereChunkGenJob
                    {
                        prm        = body.genParams,
                        originWorld= new float3(chunk.coord.x, chunk.coord.y, chunk.coord.z) * s - new float3(l.voxelSize),
                        voxelSize  = l.voxelSize,
                        biomes     = _biomes,
                        ores       = _ores,
                        voxels     = chunk.voxels,
                        sizeX      = VoxelConstants.CHUNK_SIZE_P,
                        sizeY      = VoxelConstants.CHUNK_SIZE_P,
                        sizeZ      = VoxelConstants.CHUNK_SIZE_P,
                    };
                    var handle = job.Schedule(VoxelConstants.VOXELS_PER_CHUNK_P, 64);
                    l.pendingGen.Add(new PendingGen { chunk = chunk, epoch = chunk.epoch, handle = handle });
                    genBudget--;
                }

                while (meshBudget > 0 && l.meshQueue.Count > 0)
                {
                    var q = l.meshQueue.Dequeue();
                    var chunk = q.chunk;
                    if (chunk == null || chunk.epoch != q.epoch || !chunk.generated || chunk.meshed) continue;
                    if (!l.active.TryGetValue(chunk.coord, out var live) || !ReferenceEquals(live, chunk)) continue;
                    chunk.inMeshQueue = false;

                    ScheduleMesh(l, chunk);
                    meshBudget--;
                }
            }
        }

        private void ScheduleMesh(LevelState l, LodChunk chunk)
        {
            const int CELLS = (VoxelConstants.CHUNK_SIZE + 1) * (VoxelConstants.CHUNK_SIZE + 1) * (VoxelConstants.CHUNK_SIZE + 1);
            int maxVerts = CELLS, maxIdx = CELLS * 18;

            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            var pending = new PendingMesh
            {
                chunk = chunk,
                epoch = chunk.epoch,
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
                enableVertexAo = false,
                chunkOrigin = new float3(chunk.coord.x, chunk.coord.y, chunk.coord.z) * VoxelConstants.CHUNK_SIZE,
                voxelSize = l.voxelSize,
            };
            pending.handle = job.Schedule();
            l.pendingMesh.Add(pending);
        }

        private void CompleteJobs()
        {
            foreach (var l in _levels)
            {
                for (int i = l.pendingGen.Count - 1; i >= 0; i--)
                {
                    var p = l.pendingGen[i];
                    if (!p.handle.IsCompleted) continue;
                    l.pendingGen.RemoveAt(i);
                    p.handle.Complete();
                    var chunk = p.chunk;
                    if (chunk == null || chunk.epoch != p.epoch || !l.active.TryGetValue(chunk.coord, out var live) ||
                        !ReferenceEquals(live, chunk))
                        continue;
                    chunk.generated = true;
                    QueueMesh(l, chunk);
                }

                for (int i = l.pendingMesh.Count - 1; i >= 0; i--)
                {
                    var p = l.pendingMesh[i];
                    if (!p.handle.IsCompleted) continue;
                    l.pendingMesh.RemoveAt(i);
                    p.handle.Complete();
                    var chunk = p.chunk;
                    if (chunk != null && chunk.epoch == p.epoch &&
                        l.active.TryGetValue(chunk.coord, out var live) && ReferenceEquals(live, chunk))
                    {
                        chunk.mesh.Clear();
                        Mesh.ApplyAndDisposeWritableMeshData(p.meshDataArray, chunk.mesh,
                            MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                        chunk.mesh.bounds = p.bounds[0];
                        chunk.mf.sharedMesh = chunk.mesh;
                        chunk.meshed = true;
                        l.meshedCount++;
                        // Voxels are only needed for meshing — LOD chunks are never edited,
                        // so release the buffer immediately (keeps whole-planet memory low).
                        ReleaseVoxels(chunk);
                    }
                    else
                    {
                        p.meshDataArray.Dispose();
                    }
                    DisposePendingMesh(p, complete: false, meshDataAlreadyDisposed: true);
                }
            }

            UpdateSurfaceReady();
        }

        private void UpdateSurfaceReady()
        {
            if (SurfaceReady) return;
            var far = _levels[LevelFar];
            var mid = _levels[LevelMid];
            bool farComplete = far.isActive && far.targetCount > 0 && far.meshedCount >= far.targetCount;
            bool midComplete = mid.isActive && mid.targetCount > 0 && mid.meshedCount >= mid.targetCount;
            if (farComplete || midComplete)
            {
                SurfaceReady = true;
                Debug.Log($"[PlanetVoxelLod] Real voxel surface ready for '{body.DisplayName}' " +
                          $"(far {far.meshedCount}/{far.targetCount}, mid {mid.meshedCount}/{mid.targetCount}).");
            }

            _diagTimer += Time.deltaTime;
            if (_diagTimer > 5f)
            {
                _diagTimer = 0f;
                if (!_logBuilt)
                {
                    _logBuilt = true;
                    Debug.Log($"[PlanetVoxelLod] '{body.DisplayName}': far {far.active.Count} mid {mid.active.Count} " +
                              $"near {_levels[LevelNear].active.Count} chunks, ready={SurfaceReady}.");
                }
            }
        }

        // ── Chunk lifecycle ───────────────────────────────────────────
        private LodChunk RentFromPool(LevelState l, Vector3Int coord)
        {
            LodChunk chunk;
            if (l.pool.Count > 0)
            {
                chunk = l.pool.Pop();
                chunk.go.SetActive(true);
            }
            else
            {
                chunk = CreateChunk(l);
            }
            chunk.coord = coord;
            chunk.epoch = chunk.epoch == int.MaxValue ? 1 : chunk.epoch + 1;
            chunk.generated = false;
            chunk.meshed = false;
            chunk.inGenQueue = false;
            chunk.inMeshQueue = false;
            chunk.go.name = $"LodChunk_L{chunk.levelIndex}_{coord.x}_{coord.y}_{coord.z}";
            return chunk;
        }

        private LodChunk CreateChunk(LevelState l)
        {
            var go = new GameObject("LodChunk");
            go.transform.SetParent(_chunkRoot, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = terrainMaterial;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;

            var mesh = new Mesh { name = "LodChunkMesh", indexFormat = IndexFormat.UInt32 };

            int li = l == _levels[LevelFar] ? LevelFar : l == _levels[LevelMid] ? LevelMid : LevelNear;
            return new LodChunk
            {
                levelIndex = li,
                go = go,
                mf = mf,
                mr = mr,
                mesh = mesh
            };
        }

        private void ReturnToPool(LevelState l, LodChunk chunk)
        {
            CancelChunk(l, chunk);
            ReleaseVoxels(chunk);
            if (chunk.mesh != null) chunk.mesh.Clear();
            if (chunk.mf != null) chunk.mf.sharedMesh = null;
            chunk.go.SetActive(false);
            l.pool.Push(chunk);
        }

        private void CancelChunk(LevelState l, LodChunk chunk)
        {
            // Complete any in-flight jobs owning this chunk's buffers.
            for (int i = l.pendingGen.Count - 1; i >= 0; i--)
            {
                var p = l.pendingGen[i];
                if (p.chunk != chunk || p.epoch != chunk.epoch) continue;
                l.pendingGen.RemoveAt(i);
                p.handle.Complete();
            }
            for (int i = l.pendingMesh.Count - 1; i >= 0; i--)
            {
                var p = l.pendingMesh[i];
                if (p.chunk != chunk || p.epoch != chunk.epoch) continue;
                l.pendingMesh.RemoveAt(i);
                p.handle.Complete();
                p.meshDataArray.Dispose();
                DisposePendingMesh(p, complete: false, meshDataAlreadyDisposed: true);
            }
            // Stale queue entries are harmless (epoch-guarded).
        }

        private void ReleaseVoxels(LodChunk chunk)
        {
            if (chunk.voxelsAllocated)
            {
                if (chunk.voxels.IsCreated) chunk.voxels.Dispose();
                chunk.voxelsAllocated = false;
            }
        }

        private void DestroyChunk(LodChunk chunk)
        {
            ReleaseVoxels(chunk);
            if (chunk.mesh != null) Destroy(chunk.mesh);
            if (chunk.go != null) Destroy(chunk.go);
        }

        private static void DisposePendingMesh(PendingMesh p, bool complete, bool meshDataAlreadyDisposed = false)
        {
            if (complete) p.handle.Complete();
            if (!meshDataAlreadyDisposed && p.meshDataArray.Length > 0) p.meshDataArray.Dispose();
            if (p.bounds.IsCreated) p.bounds.Dispose();
            if (p.counts.IsCreated) p.counts.Dispose();
            if (p.vertScratch.IsCreated) p.vertScratch.Dispose();
            if (p.normScratch.IsCreated) p.normScratch.Dispose();
            if (p.colScratch.IsCreated) p.colScratch.Dispose();
            if (p.idxScratch.IsCreated) p.idxScratch.Dispose();
            if (p.cellLut.IsCreated) p.cellLut.Dispose();
        }
    }
}
