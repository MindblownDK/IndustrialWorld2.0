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
//   L4 FAR   — whole planet, adaptive 128–512 m voxels   (visible from space,
//              window 60,000 km — every planet in the system stays real)
//   L3 MID   — whole planet, adaptive 32–128 m voxels    (approach + surface,
//              window 150 km — quality-tier driven)
//   L2 NEAR  — 8 m voxel ring around the viewer          (low altitude, ~3 km, shell-filtered)
//   L1 DETAIL— 4 m voxel ring around the viewer          (0.9 km — clean near horizon,
//              bridges the 1 m bubble to the 8 m ring; shell-filtered)
//   L0 PLAY  — the existing SphereWorld 1 m gameplay bubble (unchanged)
//
// LEVEL NESTING — one surface only:
//   A level NEVER renders where a finer level covers. The finer coverage is a
//   ball around the viewer (L0 bubble + NEAR ring); coarser chunks whose near
//   face is inside that ball are skipped (admission AND eviction use the same
//   test), so there is exactly ONE rendered surface at every distance — no
//   ghost surface above/below the real terrain. While the MID level builds,
//   the FAR level fills the gaps by skipping only the footprints of meshed
//   MID chunks.
//
// Because every level samples the SAME density field (+ oil site map),
// continents, oceans, mountains, biomes, ore colours and oil fields match
// exactly between levels — the only difference is voxel resolution.
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
        public float farWindowMeters = 60000000f;
        [Tooltip("Distance (m) within which the MID whole-planet level streams (32–128 m voxels).")]
        public float midWindowMeters = 150000f;
        [Tooltip("Altitude (m) below which the NEAR 8 m voxel ring streams around the viewer.")]
        public float nearAltitudeMeters = 4000f;
        [Tooltip("Hysteresis added to nearAltitudeMeters before the ring is torn down.")]
        public float nearAltitudeHysteresisMeters = 1500f;
        [Tooltip("Radius (m) of the NEAR 8 m voxel ring around the viewer (the visible horizon quality).")]
        public float nearRadiusMeters = 3000f;
        [Tooltip("Radius (m) of the DETAIL 4 m voxel ring around the viewer — sits between the 1 m gameplay bubble and the 8 m ring so the near horizon isn't visibly blocky.")]
        public float detailRadiusMeters = 900f;

        [Header("Budget")]
        [Range(1, 12)] public int maxJobsPerFrame = 4;

        /// <summary>True once the body's real voxel surface has been built at least once
        /// (the sampled impostor bridge hides after this).</summary>
        public bool SurfaceReady { get; private set; }

        // ── Level descriptors ──────────────────────────────────────────
        private const int VoxelsPerChunkP = VoxelConstants.VOXELS_PER_CHUNK_P; // 34³

        private class LevelState
        {
            public int index;
            public float voxelSize;    // metres per voxel
            public float chunkSize;    // 32 * voxelSize
            public float halfDiag;     // chunkSize * √3 / 2
            public bool wholePlanet;   // true = shell band around the core, false = ring around viewer
            public float ringRadius;   // ring levels: ball radius around the viewer (m)
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
            public MeshCollider mc;   // NEAR ring only — the player walks on real 8 m voxel terrain
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

        private readonly LevelState[] _levels = { new(), new(), new(), new() }; // far, mid, near(8m), detail(4m)

        private NativeArray<Color32> _materialColors;
        private NativeArray<OreLayer> _ores;
        private NativeArray<BiomeData> _biomes;
        private NativeArray<VertexAttributeDescriptor> _vertexAttributes;

        private NativeParallelHashMap<int, OilSiteData> _oilSites;
        private bool _oilReady;                       // oil map built (or body has no oil)
        private List<int3> _oilCells;
        private int _oilBuildIndex;

        private Transform _chunkRoot;
        private bool _ready;
        private bool _nearActive;
        private float _diagTimer;
        private bool _logBuilt;

        private const int LevelFar    = 0;
        private const int LevelMid    = 1;
        private const int LevelNear   = 2; // 8 m ring
        private const int LevelDetail = 3; // 4 m ring

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
            StartOilBuild();
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
            float detailVoxel = 4f;

            ConfigureLevel(LevelFar,    farVoxel,    wholePlanet: true);
            ConfigureLevel(LevelMid,    midVoxel,    wholePlanet: true);
            ConfigureLevel(LevelNear,   nearVoxel,   wholePlanet: false, ringRadius: nearRadiusMeters);
            ConfigureLevel(LevelDetail, detailVoxel, wholePlanet: false, ringRadius: detailRadiusMeters);

            Debug.Log($"[PlanetVoxelLod] '{body.DisplayName}': far {farVoxel:0}m / mid {midVoxel:0}m / near {nearVoxel:0}m / detail {detailVoxel:0}m voxels " +
                      $"(planet radius {radius / 1000f:0.#} km).");
        }

        private void ConfigureLevel(int index, float voxelSize, bool wholePlanet, float ringRadius = 0f)
        {
            var l = _levels[index];
            l.index = index;
            l.ringRadius = ringRadius;
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
            if (_oilSites.IsCreated) _oilSites.Dispose();
        }

        // ── Oil site map (built in batches; LOD streaming waits for it) ──
        private void StartOilBuild()
        {
            bool hasOil = body.settings != null &&
                          (body.settings.CanGenerateFiniteCrudeOilSeeps ||
                           body.settings.CanGenerateInfiniteJackPumpNodes);
            if (!hasOil)
            {
                // No oil on this body — but the job still needs a CONSTRUCTED map on
                // every schedule (an uncreated container throws at Schedule time).
                _oilSites = new NativeParallelHashMap<int, OilSiteData>(1, Allocator.Persistent);
                _oilReady = true;
                return;
            }

            _oilSites = new NativeParallelHashMap<int, OilSiteData>(16384, Allocator.Persistent);

            // Collect the near-surface shell cells to roll (batched per frame).
            const int Cell = OilSiteSampler.SiteCellSize;
            const int Stride = 3;
            float R = body.SurfaceRadius;
            int range = Mathf.CeilToInt((R + 400f) / (Cell * Stride));
            _oilCells = new List<int3>(range * range * range / 2);
            for (int cz = -range; cz <= range; cz++)
            for (int cy = -range; cy <= range; cy++)
            for (int cx = -range; cx <= range; cx++)
            {
                float3 center = new float3(cx, cy, cz) * (Cell * Stride);
                float cd = math.length(center);
                if (Mathf.Abs(cd - R) > 260f) continue;
                _oilCells.Add(new int3(cx, cy, cz));
            }
            _oilBuildIndex = 0;
            _oilReady = _oilCells.Count == 0;
        }

        private void PumpOilBuild()
        {
            if (_oilReady || _oilCells == null) return;
            if (body.settings == null) { _oilReady = true; return; }

            float finiteChance = body.settings.ResolveCrudeOilSiteChance();
            bool infiniteAllowed = body.settings.CanGenerateInfiniteJackPumpNodes;
            float infiniteChance = body.settings.ResolveInfiniteOilNodeChance();
            float scaleVoxel = _levels[LevelMid].voxelSize;
            var prm = body.genParams;

            const int Batch = 512;
            int end = Mathf.Min(_oilBuildIndex + Batch, _oilCells.Count);
            for (int i = _oilBuildIndex; i < end; i++)
            {
                int3 c = _oilCells[i];
                float3 center = (float3)c * (OilSiteSampler.SiteCellSize * 3f);
                float cd = math.length(center);
                float3 dir = cd > 0.001f ? center / cd : new float3(0f, 1f, 0f);

                SphereDensity.EvaluateColumn(prm, _biomes, dir, out float surfR, out _);
                if (surfR < prm.seaRadius + 3f) continue;

                int3 cell = new int3(
                    (int)math.floor(center.x / OilSiteSampler.SiteCellSize),
                    (int)math.floor(center.y / OilSiteSampler.SiteCellSize),
                    (int)math.floor(center.z / OilSiteSampler.SiteCellSize));
                int key = OilSiteSampler.CellKey(cell);

                bool finite = OilSiteSampler.Hash01(key, prm.seed, OilSiteSampler.FiniteSalt) <= finiteChance;
                bool infinite = infiniteAllowed && OilSiteSampler.Hash01(key, prm.seed, OilSiteSampler.InfiniteSalt) <= infiniteChance;
                if (!finite && !infinite) continue;

                float scale = Mathf.Max(1f, scaleVoxel / 8f);
                float3 anchor = dir * surfR;
                float puddleR = Mathf.Max((infinite ? 5f : 3f) * scale, scaleVoxel * 0.75f);
                float reservoirR = Mathf.Max((infinite ? 9f : 5f) * scale, scaleVoxel * 0.8f);
                float depth = (infinite ? 50f : 30f) * scale;
                float3 resCenter = anchor - dir * depth;
                float3 funnelTop = resCenter + dir * Mathf.Max(1f, reservoirR * 0.55f);

                var site = new OilSiteData
                {
                    anchor = anchor,
                    funnelTop = funnelTop,
                    reservoirCenter = resCenter,
                    puddleRadius = puddleR,
                    mouthRadius = Mathf.Max(2f, puddleR - 1f),
                    throatRadius = Mathf.Max(2f, scaleVoxel * 0.4f),
                    reservoirRadius = reservoirR
                };
                _oilSites.TryAdd(key, site);
            }
            _oilBuildIndex = end;

            if (_oilBuildIndex >= _oilCells.Count)
            {
                _oilReady = true;
                _oilCells = null;
                Debug.Log($"[PlanetVoxelLod] '{body.DisplayName}' oil site map ready ({_oilSites.Count()} sites).");
            }
        }

        // ── Per-frame streaming ───────────────────────────────────────
        private void Update()
        {
            if (!_ready || body == null) return;
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
            if (viewer == null) return;

            // Asteroid belts are procedural rock fields, not surface worlds.
            if (body.genParams.isAsteroidBelt == 1) return;

            PumpOilBuild();
            if (!_oilReady) return; // LOD streaming waits for the oil map (impostor bridge covers)

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
            // FAR stays active and fills the gaps (it skips footprints of meshed MID
            // chunks, so the two never double-render); once MID completes FAR steps aside.
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
            // The 4 m detail ring follows the 8 m ring (same altitude gate).
            _levels[LevelDetail].isActive = _nearActive;

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
            if (_levels[LevelNear].isActive != _levels[LevelNear].wasActive)
            {
                _levels[LevelNear].wasActive = _levels[LevelNear].isActive;
                _levels[LevelNear].meshedCount = 0;
                _levels[LevelDetail].wasActive = _levels[LevelNear].isActive;
                _levels[LevelDetail].meshedCount = 0;
            }
        }

        private readonly List<(Vector3Int coord, float distSq)> _candidates = new();
        private readonly List<Vector3Int> _evict = new();

        /// <summary>
        /// Maximum reach (m) of the L0 gameplay bubble: the outermost 1 m chunk's outer
        /// face. SphereWorld streams chunk centers within viewDistance·32 m, so the last
        /// row's far face sits 16 m beyond that. Used as the exact inner edge for the
        /// NEAR ring — anything closer must be rendered by the 1 m world alone.
        /// </summary>
        private float L0ReachRadius(SphereWorld sphere)
        {
            if (sphere == null || sphere.body != body || sphere.viewer == null) return 0f;
            return Mathf.Max(0f, sphere.viewDistance * VoxelConstants.CHUNK_SIZE * VoxelConstants.VOXEL_SIZE) + 16f;
        }

        /// <summary>
        /// THE nesting rule — decides whether a level should render a chunk at all.
        /// A chunk is desired iff it lies in the level's band AND its NEAR FACE is
        /// OUTSIDE every finer level's exact coverage edge. Coverage edges are the finer
        /// level's maximum reach (outer far face), so adjacent levels abut exactly —
        /// no overlap (which rendered a coarser surface ABOVE the finer terrain) and no
        /// gap. Used identically for admission and eviction.
        ///
        /// `outerMargin` (hysteresis) applies ONLY to the level's OUTER boundary — a
        /// chunk just outside the ring may stay a moment longer to avoid flicker. It is
        /// deliberately NEVER applied to the inner nesting edges: a chunk inside a finer
        /// level's coverage must be evicted immediately, or the coarse surface follows
        /// the player around and floats above the real terrain.
        /// </summary>
        private bool IsChunkDesired(LevelState l, Vector3Int coord, Vector3 viewerLocal,
            Vector3 l0CenterLocal, float radius, float l0Reach, float outerMargin = 0f)
        {
            Vector3 center = new Vector3(coord.x + 0.5f, coord.y + 0.5f, coord.z + 0.5f) * l.chunkSize;

            if (l.wholePlanet)
            {
                // Surface shell band around the core.
                float coreDist = center.magnitude;
                if (Mathf.Abs(coreDist - radius) > l.halfDiag + 200f) return false;

                // Never render where finer levels cover (STRICT — no margin).
                if (l.index == LevelMid || l.index == LevelFar)
                {
                    if (_levels[LevelNear].isActive)
                    {
                        // NEAR ring's max reach = ring radius + 2× ring half-diagonal
                        // (a ring chunk centred at the edge still extends one half-diag out).
                        float faceFromViewer = Vector3.Distance(center, viewerLocal) - l.halfDiag;
                        if (faceFromViewer < nearRadiusMeters + _levels[LevelNear].halfDiag * 2f)
                            return false;
                    }
                    if (_levels[LevelDetail].isActive)
                    {
                        float faceFromDetail = Vector3.Distance(center, viewerLocal) - l.halfDiag;
                        if (faceFromDetail < detailRadiusMeters + _levels[LevelDetail].halfDiag * 2f)
                            return false;
                    }
                    if (l0Reach > 0f)
                    {
                        float faceFromL0 = Vector3.Distance(center, l0CenterLocal) - l.halfDiag;
                        if (faceFromL0 < l0Reach)
                            return false;
                    }
                }
                // While MID builds, FAR additionally steps aside under meshed MID chunks.
                if (l.index == LevelFar && HasMeshedMidChunkUnder(coord, l))
                    return false;

                return true;
            }

            // Ring levels (DETAIL 4 m / NEAR 8 m): ball around the viewer, surface shell
            // only, outside every finer level's coverage.
            float d = Vector3.Distance(center, viewerLocal);
            // OUTER edge — hysteresis margin allowed here.
            if (d > l.ringRadius + l.halfDiag + outerMargin) return false;
            // Surface-shell filter — only chunks near the planet's terrain surface are
            // ever visible; skipping air/interior chunks keeps the bigger rings cheap.
            float centerDist = center.magnitude;
            if (Mathf.Abs(centerDist - radius) > l.halfDiag + 100f) return false;
            // INNER nesting edge — STRICT: the ring must never overlap a finer world.
            if (d - l.halfDiag < l0Reach) return false;
            if (l.index == LevelNear)
            {
                // The 8 m ring must stay outside the 4 m detail ring's reach.
                float detailReach = detailRadiusMeters + _levels[LevelDetail].halfDiag * 2f;
                if (d - l.halfDiag < detailReach) return false;
            }
            return true;
        }

        /// <summary>True when any MESHED MID chunk's footprint overlaps this FAR chunk.</summary>
        private bool HasMeshedMidChunkUnder(Vector3Int farCoord, LevelState farLevel)
        {
            var mid = _levels[LevelMid];
            if (!mid.isActive || mid.active.Count == 0) return false;
            float ratio = farLevel.chunkSize / mid.chunkSize;
            int r = Mathf.CeilToInt(ratio) + 1;
            int baseX = Mathf.FloorToInt(farCoord.x * ratio) - 1;
            int baseY = Mathf.FloorToInt(farCoord.y * ratio) - 1;
            int baseZ = Mathf.FloorToInt(farCoord.z * ratio) - 1;
            for (int dz = 0; dz <= r; dz++)
            for (int dy = 0; dy <= r; dy++)
            for (int dx = 0; dx <= r; dx++)
            {
                if (!mid.active.TryGetValue(new Vector3Int(baseX + dx, baseY + dy, baseZ + dz), out var midChunk))
                    continue;
                if (midChunk.meshed) return true;
            }
            return false;
        }

        private void UpdateStreaming()
        {
            Vector3 viewerLocal = body.transform.InverseTransformPoint(viewer.position);
            float radius = body.SurfaceRadius;
            var sphere = SphereWorld.Instance;
            float l0R = L0ReachRadius(sphere);

            // The L0 gameplay bubble is centred on the viewer on the ground, but during
            // high-altitude flight SphereWorld streams around the RADIAL SURFACE POINT
            // beneath the viewer (orbit-approach streaming) — the LOD exclusion must
            // measure from the same centre, or the 1 m bubble and the coarse LOD would
            // render the same patch twice (ghost surface).
            Vector3 l0CenterLocal = viewerLocal;
            if (sphere != null && sphere.body == body && l0R > 0f)
            {
                float viewerAlt = viewerLocal.magnitude - radius;
                float surfaceFocusAlt = VoxelConstants.CHUNK_SIZE * sphere.viewDistance * 2f;
                if (viewerAlt > surfaceFocusAlt)
                {
                    float3 radial = math.normalizesafe((float3)viewerLocal, new float3(0f, 1f, 0f));
                    l0CenterLocal = (Vector3)radial * radius;
                }
            }

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

                // 1) Build the desired candidate list (nearest first).
                _candidates.Clear();
                if (l.wholePlanet)
                {
                    int range = Mathf.CeilToInt((radius + l.halfDiag + 200f) / l.chunkSize);
                    for (int cz = -range; cz <= range; cz++)
                    for (int cy = -range; cy <= range; cy++)
                    for (int cx = -range; cx <= range; cx++)
                    {
                        var coord = new Vector3Int(cx, cy, cz);
                        if (!IsChunkDesired(l, coord, viewerLocal, l0CenterLocal, radius, l0R)) continue;
                        float d2 = DistToPlayerSq(coord, l, viewerLocal);
                        _candidates.Add((coord, d2));
                    }
                }
                else
                {
                    int range = Mathf.CeilToInt((l.ringRadius + l.halfDiag) / l.chunkSize);
                    Vector3Int vc = new(
                        Mathf.FloorToInt(viewerLocal.x / l.chunkSize),
                        Mathf.FloorToInt(viewerLocal.y / l.chunkSize),
                        Mathf.FloorToInt(viewerLocal.z / l.chunkSize));
                    for (int cz = vc.z - range; cz <= vc.z + range; cz++)
                    for (int cy = vc.y - range; cy <= vc.y + range; cy++)
                    for (int cx = vc.x - range; cx <= vc.x + range; cx++)
                    {
                        var coord = new Vector3Int(cx, cy, cz);
                        if (!IsChunkDesired(l, coord, viewerLocal, l0CenterLocal, radius, l0R)) continue;
                        Vector3 center = new Vector3(coord.x + 0.5f, coord.y + 0.5f, coord.z + 0.5f) * l.chunkSize;
                        _candidates.Add((coord, (center - viewerLocal).sqrMagnitude));
                    }
                }
                _candidates.Sort((a, b) => a.distSq.CompareTo(b.distSq));

                // 2) Evict chunks that are no longer desired (same nesting rule).
                _evict.Clear();
                foreach (var kv in l.active)
                {
                    float margin = l.wholePlanet ? 0f : l.chunkSize * 2f;
                    if (!IsChunkDesired(l, kv.Key, viewerLocal, l0CenterLocal, radius, l0R, margin))
                        _evict.Add(kv.Key);
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

            // Priority: detail → near → mid → far (the player's surroundings build first).
            int[] order = { LevelDetail, LevelNear, LevelMid, LevelFar };

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
                    // The job requires a CONSTRUCTED oil map on every schedule — an
                    // uncreated container throws at Schedule time.
                    if (!_oilSites.IsCreated) continue;
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
                        oilSites   = _oilSites,
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
                        if (chunk.mc != null)
                        {
                            // Air/interior chunks produce empty meshes (0 real vertices) —
                            // enabling a MeshCollider on those throws
                            // "mesh must have at least three distinct vertices".
                            bool valid = chunk.mesh.vertexCount >= 3 &&
                                         chunk.mesh.bounds.size.sqrMagnitude > 0.0000001f;
                            chunk.mc.sharedMesh = valid ? chunk.mesh : null;
                            chunk.mc.enabled = valid;
                        }
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
                              $"near {_levels[LevelNear].active.Count} detail {_levels[LevelDetail].active.Count} chunks, ready={SurfaceReady}.");
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
            var chunk = new LodChunk
            {
                levelIndex = li,
                go = go,
                mf = mf,
                mr = mr,
                mesh = mesh
            };
            // The ring levels (4 m detail + 8 m near) carry real colliders — the player
            // walks on them beyond the 1 m gameplay bubble (MID/FAR stay visual-only;
            // the LOD safety shell remains the fallback beneath them).
            if (li == LevelNear || li == LevelDetail)
            {
                chunk.mc = go.AddComponent<MeshCollider>();
                chunk.mc.enabled = false; // enabled when a valid mesh is applied
            }
            return chunk;
        }

        private void ReturnToPool(LevelState l, LodChunk chunk)
        {
            CancelChunk(l, chunk);
            ReleaseVoxels(chunk);
            if (chunk.mesh != null) chunk.mesh.Clear();
            if (chunk.mf != null) chunk.mf.sharedMesh = null;
            if (chunk.mc != null)
            {
                chunk.mc.sharedMesh = null;
                chunk.mc.enabled = false;
            }
            chunk.go.SetActive(false);
            l.pool.Push(chunk);
        }

        /// <summary>
        /// True when a ring-level chunk (4 m detail or 8 m near) with a live mesh
        /// collider covers this world position — the LOD safety shell asks this before
        /// stepping aside, so the player always stands on real voxel collision
        /// (1 m, 4 m or 8 m), never on a gap.
        /// </summary>
        public bool HasColliderAt(Vector3 worldPos)
        {
            if (body == null) return false;
            Vector3 local = body.transform.InverseTransformPoint(worldPos);
            // Check the finest ring first (4 m detail, then 8 m near) — allocation-free.
            if (HasRingColliderAt(_levels[LevelDetail], local)) return true;
            return HasRingColliderAt(_levels[LevelNear], local);
        }

        private static bool HasRingColliderAt(LevelState l, Vector3 local)
        {
            if (l == null || !l.isActive) return false;
            Vector3Int coord = new(
                Mathf.FloorToInt(local.x / l.chunkSize),
                Mathf.FloorToInt(local.y / l.chunkSize),
                Mathf.FloorToInt(local.z / l.chunkSize));
            if (!l.active.TryGetValue(coord, out var chunk) || chunk == null || chunk.mc == null)
                return false;
            return chunk.mc.enabled && chunk.mc.sharedMesh != null;
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
