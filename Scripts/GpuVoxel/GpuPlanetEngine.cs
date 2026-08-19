// Assets/Scripts/VoxelEngine/GpuVoxel/GpuPlanetEngine.cs
//
// GPU-DRIVEN PLANET SURFACE ENGINE (9.0.0) — replaces the legacy
// PlanetLodImpostor / PlanetVoxelLod ladder with ONE unified pipeline:
//
//   SpherifiedQuadtree (async Burst job)      → desired leaf set
//   PlanetFieldGpu.compute (GPU density)      → 67³ corner grid, milliseconds
//   AsyncGPUReadback                          → no pipeline stall, no hitch
//   GpuDualContourJob (Burst, worker threads) → watertight low-poly mesh
//   Mesh.ApplyAndDisposeWritableMeshData      → zero-copy upload
//
// One component per celestial body (spawned by CosmosBootstrap). The whole
// planet is real geometry sampled from the SAME field the gameplay bubble
// uses (PlanetField), so continents, oceans, mountains and caves match
// exactly at every distance — no gaps, no stacked slabs, no ghost surfaces.
//
// TOP-DOWN REFINEMENT, HOLE-FREE BY RULE:
//   • a parent stays visible until all four children are ready;
//   • a child is never destroyed until its parent (or its children) cover it;
//   • equal-depth neighbours stitch watertight (ghost cells, see the DC job);
//   • depth transitions are masked by radial skirts.
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Biomes;
using VoxelEngine.Cosmos;
using VoxelEngine.Materials;

namespace VoxelEngine.GpuVoxel
{
    [DisallowMultipleComponent]
    public class GpuPlanetEngine : MonoBehaviour
    {
        [Header("Body & References")]
        public CelestialBody body;
        public Transform viewer;
        public Material terrainMaterial;
        public MaterialRegistry materialRegistry;
        public BiomeRegistry biomeRegistry;

        [Header("Quadtree Streaming")]
        [Tooltip("A node splits while the viewer is closer than splitFactor × its footprint size. Higher = more detail, more nodes.")]
        [Range(1.2f, 4f)] public float splitFactor = 2.2f;
        [Tooltip("Finest cell size (m) the quadtree refines to. The 1 m gameplay bubble covers the last step.")]
        [Range(0.5f, 8f)] public float finestCellMeters = 2f;
        [Tooltip("GPU density builds allowed in flight simultaneously.")]
        [Range(1, 6)] public int maxConcurrentBuilds = 3;
        [Tooltip("Finished meshes applied per frame.")]
        [Range(1, 8)] public int maxAppliesPerFrame = 4;
        [Tooltip("Seconds between desired-leaf-set refreshes (the async quadtree job).")]
        [Range(0.1f, 2f)] public float desiredRefreshInterval = 0.3f;

        [Header("Colliders")]
        public bool generateColliders = true;
        [Tooltip("Nodes whose cells are at least this fine receive mesh colliders when near the viewer.")]
        public float colliderMaxCellMeters = 4.5f;
        public float colliderRange = 500f;
        [Range(1, 4)] public int maxColliderBakesPerFrame = 2;

        // ─────────────────────────── runtime ───────────────────────────
        private enum NodeState { Queued, Building, Ready }

        private sealed class NodeRec
        {
            public QuadNodeDesc desc;
            public NodeState state;
            public GameObject go;
            public MeshFilter filter;
            public MeshRenderer renderer;
            public MeshCollider collider;
            public Mesh mesh;
            public bool hasMesh;
            public bool colliderOn;
            public bool hiddenByBubble;
            public int desiredStamp;
        }

        private enum SlotState { Free, AwaitGpu, Meshing }

        private sealed class BuildSlot
        {
            public SlotState state;
            public NodeRec node;
            public ComputeBuffer columnsBuf;
            public ComputeBuffer densityBuf;
            public ComputeBuffer materialBuf;
            public AsyncGPUReadbackRequest reqDensity;
            public AsyncGPUReadbackRequest reqMaterial;
            public NativeArray<float> density;
            public NativeArray<uint> material;
            public NativeArray<int> cellVertexIndex;
            public NativeArray<float3> vertScratch;
            public NativeArray<float3> normScratch;
            public NativeArray<Color32> colScratch;
            public NativeArray<int> idxScratch;
            public NativeArray<Bounds> bounds;
            public NativeArray<int> counts;
            public Mesh.MeshDataArray meshDataArray;
            public JobHandle handle;
        }

        private readonly Dictionary<QuadNodeId, NodeRec> _nodes = new();
        private readonly List<NodeRec> _queue = new();
        private readonly List<BuildSlot> _slots = new();
        private readonly Stack<GameObject> _goPool = new();

        private ComputeShader _cs;
        private int _kColumns = -1, _kField = -1;
        private ComputeBuffer _climateLut;

        private NativeArray<Color32> _materialColors;
        private NativeArray<VertexAttributeDescriptor> _vertexAttributes;

        private NativeList<QuadNodeDesc> _desired;
        private JobHandle _desiredHandle;
        private bool _desiredRunning;
        private float _desiredTimer;
        private int _desiredStamp;
        private bool _hasDesiredSet;
        private readonly HashSet<QuadNodeId> _splitIds = new();
        private NativeParallelHashSet<QuadNodeId> _splitSetNative;

        private CelestialBody _activeBody;
        private int _maxDepth = 6;
        private SphereCollider _safetyCore;
        private float _visibilityTimer;
        private Material _lodSkinMaterial;   // terrainMaterial clone with _BubbleCutout = 1

        private static readonly int PropFace = Shader.PropertyToID("_Face");
        private static readonly int PropSeed = Shader.PropertyToID("_Seed");
        private static readonly int PropGridP = Shader.PropertyToID("_GridP");
        private static readonly int PropUvMin = Shader.PropertyToID("_UvMin");
        private static readonly int PropCellUv = Shader.PropertyToID("_CellUv");
        private static readonly int PropRLo = Shader.PropertyToID("_RLo");
        private static readonly int PropDr = Shader.PropertyToID("_Dr");
        private static readonly int PropRadius = Shader.PropertyToID("_RadiusWorld");
        private static readonly int PropBaseH = Shader.PropertyToID("_BaseHeight");
        private static readonly int PropSeaR = Shader.PropertyToID("_SeaRadius");
        private static readonly int PropCont = Shader.PropertyToID("_ContinentScale");
        private static readonly int PropMount = Shader.PropertyToID("_MountainScale");
        private static readonly int PropCrust = Shader.PropertyToID("_CrustDepth");
        private static readonly int PropSlopeSpan = Shader.PropertyToID("_SlopeSpan");

        // ─────────────────────────── lifecycle ───────────────────────────

        private void Awake()
        {
            _cs = Resources.Load<ComputeShader>("PlanetFieldGpu");
            if (_cs == null)
            {
                Debug.LogError("[GpuPlanetEngine] Missing Resources/PlanetFieldGpu.compute — engine disabled.");
                enabled = false;
                return;
            }
            _kColumns = _cs.FindKernel("CSColumns");
            _kField = _cs.FindKernel("CSField");

            _vertexAttributes = new NativeArray<VertexAttributeDescriptor>(3, Allocator.Persistent);
            _vertexAttributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0);
            _vertexAttributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0);
            _vertexAttributes[2] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 0);

            _materialColors = new NativeArray<Color32>(256, Allocator.Persistent);
            _desired = new NativeList<QuadNodeDesc>(1024, Allocator.Persistent);

            while (_slots.Count < maxConcurrentBuilds) _slots.Add(CreateSlot());
        }

        private void OnDestroy()
        {
            if (_desiredRunning) { _desiredHandle.Complete(); _desiredRunning = false; }
            foreach (var slot in _slots) DisposeSlot(slot);
            _slots.Clear();

            foreach (var kv in _nodes) ReleaseNodeObjects(kv.Value, destroyImmediateMesh: true);
            _nodes.Clear();
            _queue.Clear();
            while (_goPool.Count > 0) Destroy(_goPool.Pop());

            if (_desired.IsCreated) _desired.Dispose();
            if (_splitSetNative.IsCreated) _splitSetNative.Dispose();
            if (_materialColors.IsCreated) _materialColors.Dispose();
            if (_vertexAttributes.IsCreated) _vertexAttributes.Dispose();
            _climateLut?.Release();
            _climateLut = null;
            if (_lodSkinMaterial != null) { Destroy(_lodSkinMaterial); _lodSkinMaterial = null; }
        }

        private BuildSlot CreateSlot()
        {
            int corners = GpuVoxelConstants.CORNERS_PER_NODE;
            return new BuildSlot
            {
                state = SlotState.Free,
                columnsBuf = new ComputeBuffer(GpuVoxelConstants.COLUMNS_PER_NODE, 16),
                densityBuf = new ComputeBuffer(corners, 4),
                materialBuf = new ComputeBuffer(corners, 4),
                density = new NativeArray<float>(corners, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                material = new NativeArray<uint>(corners, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                cellVertexIndex = new NativeArray<int>(GpuVoxelConstants.MESH_CELLS * GpuVoxelConstants.MESH_CELLS * GpuVoxelConstants.MESH_CELLS, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                vertScratch = new NativeArray<float3>(GpuVoxelConstants.MAX_VERTICES, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                normScratch = new NativeArray<float3>(GpuVoxelConstants.MAX_VERTICES, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                colScratch = new NativeArray<Color32>(GpuVoxelConstants.MAX_VERTICES, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                idxScratch = new NativeArray<int>(GpuVoxelConstants.MAX_INDICES, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                bounds = new NativeArray<Bounds>(1, Allocator.Persistent),
                counts = new NativeArray<int>(2, Allocator.Persistent)
            };
        }

        private void DisposeSlot(BuildSlot slot)
        {
            if (slot == null) return;
            if (slot.state == SlotState.AwaitGpu)
            {
                slot.reqDensity.WaitForCompletion();
                slot.reqMaterial.WaitForCompletion();
            }
            if (slot.state == SlotState.Meshing)
            {
                slot.handle.Complete();
                slot.meshDataArray.Dispose();
            }
            slot.columnsBuf?.Release();
            slot.densityBuf?.Release();
            slot.materialBuf?.Release();
            if (slot.density.IsCreated) slot.density.Dispose();
            if (slot.material.IsCreated) slot.material.Dispose();
            if (slot.cellVertexIndex.IsCreated) slot.cellVertexIndex.Dispose();
            if (slot.vertScratch.IsCreated) slot.vertScratch.Dispose();
            if (slot.normScratch.IsCreated) slot.normScratch.Dispose();
            if (slot.colScratch.IsCreated) slot.colScratch.Dispose();
            if (slot.idxScratch.IsCreated) slot.idxScratch.Dispose();
            if (slot.bounds.IsCreated) slot.bounds.Dispose();
            if (slot.counts.IsCreated) slot.counts.Dispose();
        }

        // ─────────────────────────── public API ───────────────────────────

        /// <summary>Quality-tier hook (QualityPresetApplier).</summary>
        public void ApplyQualityBudget(int jobsPerFrame)
        {
            maxConcurrentBuilds = Mathf.Clamp(jobsPerFrame / 2, 2, 6);
            maxAppliesPerFrame = Mathf.Clamp(jobsPerFrame, 2, 8);
            while (_slots.Count < maxConcurrentBuilds) _slots.Add(CreateSlot());
        }

        /// <summary>Force a full rebuild (body changed, new seed, quality jump).</summary>
        public void ResetAllNodes()
        {
            if (_desiredRunning) { _desiredHandle.Complete(); _desiredRunning = false; }
            foreach (var slot in _slots)
            {
                if (slot.state == SlotState.AwaitGpu)
                {
                    slot.reqDensity.WaitForCompletion();
                    slot.reqMaterial.WaitForCompletion();
                }
                else if (slot.state == SlotState.Meshing)
                {
                    slot.handle.Complete();
                    slot.meshDataArray.Dispose();
                }
                slot.node = null;
                slot.state = SlotState.Free;
            }
            foreach (var kv in _nodes) ReleaseNodeObjects(kv.Value, destroyImmediateMesh: false);
            _nodes.Clear();
            _queue.Clear();
            _hasDesiredSet = false;
            _desiredTimer = 999f;
        }

        // ─────────────────────────── main loop ───────────────────────────

        private void Update()
        {
            if (!ResolveContext()) return;

            PumpDesiredJob();
            PumpBuildQueue();
            PollSlots();
            UpdateVisibilityAndEviction();
            UpdateColliders();
            DepenetrationGuard();
            PumpDiagnostics();
        }

        private float _engineDiagTimer = 12f;
        private void PumpDiagnostics()
        {
            _engineDiagTimer -= Time.deltaTime;
            if (_engineDiagTimer > 0f) return;
            _engineDiagTimer = 15f;

            int ready = 0, building = 0, finest = 0;
            foreach (var kv in _nodes)
            {
                if (kv.Value.state == NodeState.Ready) ready++;
                else if (kv.Value.state == NodeState.Building) building++;
                if (kv.Key.depth > finest) finest = kv.Key.depth;
            }
            float viewerDist = viewer != null
                ? Vector3.Distance(viewer.position, body.transform.position) - body.genParams.radiusWorld
                : -1f;
            bool hasBubble = TryGetBubble(out _, out float meshedR, out float colR);
            Debug.Log($"[GpuPlanetEngine:{body.DisplayName}] nodes={_nodes.Count} ready={ready} " +
                      $"building={building} queued={_queue.Count} depth={finest}/{_maxDepth} " +
                      $"altitude={viewerDist:0}m bubble={(hasBubble ? $"{meshedR:0}/{colR:0}m" : "none")}");
        }

        // ── Depenetration guard (9.3.0) ─────────────────────────────────────
        // Extreme approach speeds can tunnel through baked mesh colliders (discrete
        // physics). If the viewer crosses from OUTSIDE the analytic surface to well
        // INSIDE it at high radial speed, snap it back onto the surface and kill the
        // velocity. Cave/tunnel players never trigger this: they are already inside
        // slowly, and the outside→inside transition at ≥60 m/s only happens when
        // physics genuinely missed the ground.
        private float _guardTimer;
        private float _guardLastDepth = -1000f;
        private float _guardLastTime = -1f;

        private Vector3 _guardLastPos;
        private bool _guardHasLastPos;

        private void DepenetrationGuard()
        {
            _guardTimer += Time.deltaTime;
            if (_guardTimer < 0.2f) return;
            float dt = Mathf.Max(0.05f, Time.time - (_guardLastTime < 0f ? Time.time - 0.2f : _guardLastTime));
            _guardTimer = 0f;

            // Teleport immunity (9.5.5): respawns/loads/warps move the viewer hundreds of
            // metres in one sample — the guard read that as a "10,000 m/s dive" and
            // snapped players MID-SPAWN, fighting the spawner (and poisoning saves).
            // Displacement faster than any craft = teleport: rebaseline silently.
            Vector3 viewerPos = viewer.position;
            float moved = _guardHasLastPos ? Vector3.Distance(viewerPos, _guardLastPos) : 0f;
            _guardLastPos = viewerPos;
            _guardHasLastPos = true;
            bool teleported = moved > 900f * dt + 50f;

            var prm = body.genParams;
            Vector3 local = body.transform.InverseTransformPoint(viewerPos);
            float r = local.magnitude;
            if (r < 1f || r > prm.radiusWorld * 2f || teleported)
            {
                _guardLastDepth = -1000f;
                _guardLastTime = Time.time;
                return;
            }

            Unity.Mathematics.float3 dir = Unity.Mathematics.math.normalizesafe(
                (Unity.Mathematics.float3)local, new Unity.Mathematics.float3(0f, 1f, 0f));
            float surf = PlanetField.SurfaceRadius(prm.seed, dir, prm.radiusWorld, prm.baseHeight,
                                                   prm.seaRadius, prm.continentScaleDir, prm.mountainScale);
            float depth = surf - r;                    // >0 = below the analytic surface
            bool wasOutside = _guardLastDepth < -2f && _guardLastDepth > -900f;
            float sinkSpeed = (depth - _guardLastDepth) / dt;   // m/s downward through the crust

            // 60–1500 m/s: genuine physics tunnelling. Anything faster is a teleport.
            if (depth > 3f && wasOutside && sinkSpeed > 60f && sinkSpeed < 1500f)
            {
                Vector3 up = (viewer.position - body.transform.position).normalized;
                Vector3 target = body.transform.position + up * (surf + 4f);
                MoveViewerRootTo(target);
                Debug.LogWarning($"[GpuPlanetEngine] Depenetration guard: viewer tunnelled " +
                                 $"{depth:0.0} m into '{body.DisplayName}' at ~{sinkSpeed:0} m/s — snapped to surface.");
                depth = -4f;
            }

            _guardLastDepth = depth;
            _guardLastTime = Time.time;
        }

        private void MoveViewerRootTo(Vector3 target)
        {
            var rb = viewer.GetComponentInParent<Rigidbody>();
            if (rb != null)
            {
                rb.position = target;
                rb.linearVelocity = Vector3.zero;
                return;
            }
            var cc = viewer.GetComponentInParent<CharacterController>();
            if (cc != null)
            {
                bool wasEnabled = cc.enabled;
                cc.enabled = false;
                cc.transform.position = target;
                cc.enabled = wasEnabled;
                return;
            }
            viewer.position = target;
        }

        private bool ResolveContext()
        {
            if (body == null || _cs == null) return false;
            if (body.genParams.isAsteroidBelt == 1) return false;
            if (viewer == null)
            {
                var cam = Camera.main;
                if (cam != null) viewer = cam.transform;
                if (viewer == null) return false;
            }

            if (_activeBody != body)
            {
                _activeBody = body;
                OnBodyAssigned();
            }
            return true;
        }

        private void OnBodyAssigned()
        {
            ResetAllNodes();
            BuildMaterialColorLut();
            BuildClimateLut();

            float r = body.genParams.radiusWorld;
            float faceArc = (Mathf.PI * 0.5f) * r;
            _maxDepth = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Log(faceArc / (GpuVoxelConstants.NODE_CELLS * Mathf.Max(0.5f, finestCellMeters)), 2f)),
                1, 12);

            // Safety core — physics floor below the deepest possible terrain so
            // nothing ever falls through a planet while nodes stream in.
            if (_safetyCore == null)
            {
                var coreGO = new GameObject("GpuSafetyCore");
                coreGO.transform.SetParent(transform, false);
                coreGO.AddComponent<PlanetSafetyCollider>();
                _safetyCore = coreGO.AddComponent<SphereCollider>();
            }
            _safetyCore.center = Vector3.zero;
            _safetyCore.radius = Mathf.Max(10f,
                r + body.genParams.baseHeight + PlanetField.MinElevation(r) - 24f);
        }

        private void BuildMaterialColorLut()
        {
            for (int i = 0; i < 256; i++)
                _materialColors[i] = new Color32(128, 128, 128, 255);
            if (materialRegistry == null) return;
            for (int i = 0; i < 256; i++)
            {
                var def = materialRegistry.Get((byte)i);
                if (def != null) _materialColors[i] = def.color;
            }
        }

        private void BuildClimateLut()
        {
            var packed = new uint[GpuVoxelConstants.CLIMATE_LUT_ENTRIES];
            BiomeData[] biomes = body != null ? body.BuildBiomeData(biomeRegistry) : null;

            for (int h = 0; h < GpuVoxelConstants.CLIMATE_LUT_SIZE; h++)
            for (int t = 0; t < GpuVoxelConstants.CLIMATE_LUT_SIZE; t++)
            {
                // Defaults: grass over stone, beaches allowed.
                uint surfMat = (uint)MaterialId.Grass;
                uint subMat = (uint)MaterialId.Stone;
                uint surfD = 3, subD = 4, beach = 1;

                if (biomes != null && biomes.Length > 0)
                {
                    float2 climate = new float2(
                        (t + 0.5f) / GpuVoxelConstants.CLIMATE_LUT_SIZE,
                        (h + 0.5f) / GpuVoxelConstants.CLIMATE_LUT_SIZE);
                    float best = float.MinValue;
                    int bi = 0;
                    for (int i = 0; i < biomes.Length; i++)
                    {
                        float s = SphereDensity.Score(biomes[i], climate);
                        if (s > best) { best = s; bi = i; }
                    }
                    var b = biomes[bi];
                    surfMat = b.surfaceMat;
                    subMat = b.subsurfaceMat;
                    surfD = (uint)Mathf.Clamp(b.surfaceDepth, 1, 15);
                    subD = (uint)Mathf.Clamp(b.subsurfaceDepth, 1, 15);
                    beach = b.allowBeach;
                }

                packed[t + h * GpuVoxelConstants.CLIMATE_LUT_SIZE] =
                    (surfMat & 0xFF) | ((subMat & 0xFF) << 8) |
                    ((surfD & 0xF) << 16) | ((subD & 0xF) << 20) | ((beach & 0x1) << 24);
            }

            _climateLut ??= new ComputeBuffer(GpuVoxelConstants.CLIMATE_LUT_ENTRIES, 4);
            _climateLut.SetData(packed);
        }

        // ─────────────────────────── desired set (async quadtree) ───────────────────────────

        private void PumpDesiredJob()
        {
            if (_desiredRunning)
            {
                if (!_desiredHandle.IsCompleted) return;
                _desiredHandle.Complete();
                _desiredRunning = false;
                ReconcileDesired();
                return;
            }

            _desiredTimer += Time.deltaTime;
            if (_desiredTimer < desiredRefreshInterval && _hasDesiredSet) return;
            _desiredTimer = 0f;

            var prm = body.genParams;
            float3 viewerLocal = body.transform.InverseTransformPoint(viewer.position);

            // Hysteresis input: node ids that were split last pass hold their split
            // until the viewer clearly leaves range (kills LOD flip-flop flashing).
            if (_splitSetNative.IsCreated) _splitSetNative.Dispose();
            _splitSetNative = new NativeParallelHashSet<QuadNodeId>(
                math.max(64, _splitIds.Count + 16), Allocator.Persistent);
            foreach (var id in _splitIds) _splitSetNative.Add(id);

            var job = new BuildDesiredLeavesJob
            {
                seed = prm.seed,
                radiusWorld = prm.radiusWorld,
                baseHeight = prm.baseHeight,
                seaRadius = prm.seaRadius,
                continentScale = prm.continentScaleDir,
                mountainScale = prm.mountainScale,
                viewerLocal = viewerLocal,
                maxDepth = _maxDepth,
                splitFactor = splitFactor,
                maxLeaves = 6144,
                splitSet = _splitSetNative,
                results = _desired
            };
            _desiredHandle = job.Schedule();
            _desiredRunning = true;
        }

        private void ReconcileDesired()
        {
            _hasDesiredSet = true;
            _desiredStamp++;
            _splitIds.Clear();
            for (int i = 0; i < _desired.Length; i++)
            {
                QuadNodeDesc desc = _desired[i];

                // Every ancestor of a desired leaf is (by definition) split.
                QuadNodeId cur = desc.id;
                while (cur.depth > 0)
                {
                    cur = cur.Parent;
                    if (!_splitIds.Add(cur)) break;   // ancestors above are already added
                }

                if (_nodes.TryGetValue(desc.id, out NodeRec rec))
                {
                    rec.desiredStamp = _desiredStamp;
                    rec.desc.distance = desc.distance;   // refresh priority
                }
                else
                {
                    rec = new NodeRec { desc = desc, state = NodeState.Queued, desiredStamp = _desiredStamp };
                    _nodes.Add(desc.id, rec);
                    _queue.Add(rec);
                }
            }
        }

        // ─────────────────────────── build pipeline ───────────────────────────

        private void PumpBuildQueue()
        {
            if (_queue.Count == 0) return;
            foreach (var slot in _slots)
            {
                if (slot.state != SlotState.Free) continue;
                NodeRec rec = PopBestQueued();
                if (rec == null) return;
                DispatchDensity(slot, rec);
            }
        }

        private NodeRec PopBestQueued()
        {
            int bestIdx = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _queue.Count; i++)
            {
                NodeRec r = _queue[i];
                if (r.state != NodeState.Queued) continue;
                // Stale queued nodes that are no longer desired are dropped cheaply.
                if (r.desiredStamp != _desiredStamp && _hasDesiredSet)
                {
                    _nodes.Remove(r.desc.id);
                    _queue.RemoveAt(i);
                    i--;
                    continue;
                }
                // Coarse levels first (top-down refinement), then nearest.
                float score = r.desc.id.depth * 1e9f + r.desc.distance;
                if (score < bestScore) { bestScore = score; bestIdx = i; }
            }
            if (bestIdx < 0) return null;
            NodeRec best = _queue[bestIdx];
            _queue.RemoveAt(bestIdx);
            return best;
        }

        private void DispatchDensity(BuildSlot slot, NodeRec rec)
        {
            var prm = body.genParams;
            QuadNodeDesc d = rec.desc;

            _cs.SetInt(PropFace, d.id.face);
            _cs.SetInt(PropSeed, prm.seed);
            _cs.SetInt(PropGridP, GpuVoxelConstants.GRID_P);
            _cs.SetVector(PropUvMin, new Vector4(d.uvMin.x, d.uvMin.y, 0f, 0f));
            _cs.SetVector(PropCellUv, new Vector4(
                d.uvSize.x / GpuVoxelConstants.NODE_CELLS,
                d.uvSize.y / GpuVoxelConstants.NODE_CELLS, 0f, 0f));
            _cs.SetFloat(PropRLo, d.rLo);
            _cs.SetFloat(PropDr, d.Dr);
            _cs.SetFloat(PropRadius, prm.radiusWorld);
            _cs.SetFloat(PropBaseH, prm.baseHeight);
            _cs.SetFloat(PropSeaR, prm.seaRadius);
            _cs.SetFloat(PropCont, prm.continentScaleDir);
            _cs.SetFloat(PropMount, prm.mountainScale);
            _cs.SetFloat(PropCrust, 10f);
            _cs.SetFloat(PropSlopeSpan, 2f * d.CellArc);

            _cs.SetBuffer(_kColumns, "_Columns", slot.columnsBuf);
            _cs.SetBuffer(_kField, "_Columns", slot.columnsBuf);
            _cs.SetBuffer(_kField, "_Density", slot.densityBuf);
            _cs.SetBuffer(_kField, "_Material", slot.materialBuf);
            _cs.SetBuffer(_kField, "_ClimateLut", _climateLut);

            int gCol = Mathf.CeilToInt(GpuVoxelConstants.GRID_P / 8f);
            int gFld = Mathf.CeilToInt(GpuVoxelConstants.GRID_P / 4f);
            _cs.Dispatch(_kColumns, gCol, gCol, 1);
            _cs.Dispatch(_kField, gFld, gFld, gFld);

            slot.reqDensity = AsyncGPUReadback.Request(slot.densityBuf);
            slot.reqMaterial = AsyncGPUReadback.Request(slot.materialBuf);
            slot.node = rec;
            slot.state = SlotState.AwaitGpu;
            rec.state = NodeState.Building;
        }

        private void PollSlots()
        {
            int appliesLeft = maxAppliesPerFrame;
            foreach (var slot in _slots)
            {
                if (slot.state == SlotState.AwaitGpu)
                {
                    if (!slot.reqDensity.done || !slot.reqMaterial.done) continue;
                    if (slot.reqDensity.hasError || slot.reqMaterial.hasError)
                    {
                        // GPU hiccup — requeue and free the slot.
                        if (slot.node != null) { slot.node.state = NodeState.Queued; _queue.Add(slot.node); }
                        slot.node = null;
                        slot.state = SlotState.Free;
                        continue;
                    }

                    slot.density.CopyFrom(slot.reqDensity.GetData<float>());
                    slot.material.CopyFrom(slot.reqMaterial.GetData<uint>());
                    ScheduleMeshing(slot);
                }
                else if (slot.state == SlotState.Meshing)
                {
                    if (!slot.handle.IsCompleted || appliesLeft <= 0) continue;
                    slot.handle.Complete();
                    ApplyMesh(slot);
                    appliesLeft--;
                }
            }
        }

        private void ScheduleMeshing(BuildSlot slot)
        {
            QuadNodeDesc d = slot.node.desc;
            slot.meshDataArray = Mesh.AllocateWritableMeshData(1);
            var job = new GpuDualContourJob
            {
                density = slot.density,
                material = slot.material,
                materialColors = _materialColors,
                vertexAttributes = _vertexAttributes,
                face = d.id.face,
                uvMin = d.uvMin,
                cellUv = d.uvSize / GpuVoxelConstants.NODE_CELLS,
                rLo = d.rLo,
                dr = d.Dr,
                anchor = d.Anchor,
                skirtDepth = math.max(3f * d.CellArc, 3f * d.Dr),
                meshData = slot.meshDataArray[0],
                boundsOut = slot.bounds,
                counts = slot.counts,
                cellVertexIndex = slot.cellVertexIndex,
                vertScratch = slot.vertScratch,
                normScratch = slot.normScratch,
                colScratch = slot.colScratch,
                idxScratch = slot.idxScratch
            };
            slot.handle = job.Schedule();
            slot.state = SlotState.Meshing;
        }

        private void ApplyMesh(BuildSlot slot)
        {
            NodeRec rec = slot.node;
            slot.node = null;
            slot.state = SlotState.Free;

            int verts = slot.counts[0];
            int indices = slot.counts[1];

            if (rec == null || !_nodes.ContainsKey(rec.desc.id))
            {
                slot.meshDataArray.Dispose();
                return;
            }

            if (verts <= 0 || indices <= 0)
            {
                slot.meshDataArray.Dispose();
                rec.hasMesh = false;
                rec.state = NodeState.Ready;
                return;
            }

            if (rec.mesh == null) rec.mesh = new Mesh { name = $"GpuNode {rec.desc.id}" };
            Mesh.ApplyAndDisposeWritableMeshData(slot.meshDataArray, rec.mesh,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            rec.mesh.bounds = slot.bounds[0];

            if (rec.go == null) AcquireNodeObjects(rec);
            rec.filter.sharedMesh = rec.mesh;
            rec.hasMesh = true;
            rec.state = NodeState.Ready;
        }

        // ─────────────────────────── node objects ───────────────────────────

        /// <summary>
        /// The LOD-skin material: a clone of the shared terrain material with
        /// _BubbleCutout enabled, so this engine's fragments clip inside the gameplay
        /// bubble's meshed ball (mined holes never show phantom terrain behind them).
        /// Bubble chunks keep the original material (cutout off).
        /// </summary>
        private Material LodSkinMaterial
        {
            get
            {
                if (_lodSkinMaterial == null && terrainMaterial != null)
                {
                    _lodSkinMaterial = new Material(terrainMaterial)
                    {
                        name = terrainMaterial.name + " (GpuLodSkin)"
                    };
                    _lodSkinMaterial.SetFloat("_BubbleCutout", 1f);
                    // Sink the whole LOD skin slightly toward the core so the bubble's
                    // surface always renders on top — no coincident-surface z-fighting.
                    _lodSkinMaterial.SetFloat("_LodRadialBias", 0.45f);
                }
                return _lodSkinMaterial != null ? _lodSkinMaterial : terrainMaterial;
            }
        }

        /// <summary>
        /// Gameplay-bubble coverage on THIS body (single-surface handshake, 9.1.0).
        /// </summary>
        private bool TryGetBubble(out Vector3 centerLocal, out float meshedRadius, out float colliderRadius)
        {
            var sw = SphereWorld.Instance;
            if (sw != null && sw.body == body &&
                sw.TryGetMeshedBubble(out centerLocal, out meshedRadius, out colliderRadius))
                return true;
            centerLocal = default;
            meshedRadius = 0f;
            colliderRadius = 0f;
            return false;
        }

        /// <summary>Conservative bounding-ball radius of a node's shell volume.</summary>
        private static float NodeBallRadius(in QuadNodeDesc desc)
            => 0.75f * desc.arc + 0.5f * (desc.rHi - desc.rLo);

        private void AcquireNodeObjects(NodeRec rec)
        {
            GameObject go;
            if (_goPool.Count > 0)
            {
                go = _goPool.Pop();
                go.SetActive(true);
            }
            else
            {
                go = new GameObject("GpuNode");
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.On;
                var mc = go.AddComponent<MeshCollider>();
                mc.enabled = false;
                go.AddComponent<PlanetSafetyCollider>(); // interaction raycasts skip LOD colliders
            }
            go.name = $"GpuNode {rec.desc.id}";
            go.transform.SetParent(body.transform, false);
            go.transform.localPosition = (Vector3)rec.desc.Anchor;
            go.transform.localRotation = Quaternion.identity;

            rec.go = go;
            rec.filter = go.GetComponent<MeshFilter>();
            rec.renderer = go.GetComponent<MeshRenderer>();
            rec.collider = go.GetComponent<MeshCollider>();
            rec.renderer.sharedMaterial = LodSkinMaterial;
            rec.renderer.enabled = true;
            rec.collider.sharedMesh = null;
            rec.collider.enabled = false;
            rec.colliderOn = false;
        }

        private void ReleaseNodeObjects(NodeRec rec, bool destroyImmediateMesh)
        {
            if (rec.go != null)
            {
                rec.collider.sharedMesh = null;
                rec.collider.enabled = false;
                rec.filter.sharedMesh = null;
                rec.go.SetActive(false);
                _goPool.Push(rec.go);
                rec.go = null;
                rec.filter = null;
                rec.renderer = null;
                rec.collider = null;
            }
            if (rec.mesh != null)
            {
                if (destroyImmediateMesh && !Application.isPlaying) DestroyImmediate(rec.mesh);
                else Destroy(rec.mesh);
                rec.mesh = null;
            }
            rec.hasMesh = false;
            rec.colliderOn = false;
        }

        // ─────────────────────────── visibility & eviction ───────────────────────────

        private void UpdateVisibilityAndEviction()
        {
            _visibilityTimer += Time.deltaTime;
            if (_visibilityTimer < 0.15f) return;
            _visibilityTimer = 0f;

            bool hasBubble = TryGetBubble(out Vector3 bubbleCenter, out float bubbleRadius, out _);

            // 1. A ready node hides once all four children fully cover it. Nodes whose
            //    entire shell sits inside the bubble's meshed ball also hide — the
            //    gameplay bubble IS the surface there (hysteresis avoids flicker).
            foreach (var kv in _nodes)
            {
                NodeRec rec = kv.Value;
                if (rec.state != NodeState.Ready || rec.renderer == null) continue;

                if (hasBubble)
                {
                    float ball = NodeBallRadius(rec.desc);
                    float dist = Vector3.Distance((Vector3)rec.desc.Anchor, bubbleCenter);
                    if (rec.hiddenByBubble)
                    {
                        if (dist + ball > bubbleRadius - 4f) rec.hiddenByBubble = false;
                    }
                    else if (dist + ball < bubbleRadius - 12f)
                    {
                        rec.hiddenByBubble = true;
                    }
                }
                else
                {
                    rec.hiddenByBubble = false;
                }

                bool covered = ChildrenCover(kv.Key, 0);
                rec.renderer.enabled = rec.hasMesh && !covered && !rec.hiddenByBubble;
            }

            // 2. Evict stale nodes only when something else covers their footprint.
            if (!_hasDesiredSet) return;
            List<QuadNodeId> toRemove = null;
            foreach (var kv in _nodes)
            {
                NodeRec rec = kv.Value;
                if (rec.desiredStamp == _desiredStamp) continue;
                if (rec.state == NodeState.Building) continue;   // let the pipeline finish

                bool covered = ChildrenCover(kv.Key, 0) || AncestorReady(kv.Key);
                if (rec.state == NodeState.Queued || covered || !rec.hasMesh)
                {
                    toRemove ??= new List<QuadNodeId>(16);
                    toRemove.Add(kv.Key);
                }
            }
            if (toRemove != null)
            {
                foreach (var id in toRemove)
                {
                    if (!_nodes.TryGetValue(id, out NodeRec rec)) continue;
                    ReleaseNodeObjects(rec, destroyImmediateMesh: false);
                    _nodes.Remove(id);
                    _queue.Remove(rec);
                }
            }
        }

        private bool ChildrenCover(QuadNodeId id, int recursion)
        {
            if (recursion > 3) return false;
            for (int cy = 0; cy < 2; cy++)
            for (int cx = 0; cx < 2; cx++)
            {
                QuadNodeId child = id.Child(cx, cy);
                if (_nodes.TryGetValue(child, out NodeRec rec))
                {
                    if (rec.state == NodeState.Ready) continue;
                    if (ChildrenCover(child, recursion + 1)) continue;
                }
                else if (ChildrenCover(child, recursion + 1)) continue;
                return false;
            }
            return true;
        }

        private bool AncestorReady(QuadNodeId id)
        {
            QuadNodeId cur = id;
            for (int i = 0; i < 4 && cur.depth > 0; i++)
            {
                cur = cur.Parent;
                if (_nodes.TryGetValue(cur, out NodeRec rec) &&
                    rec.state == NodeState.Ready && rec.desiredStamp == _desiredStamp)
                    return true;
            }
            return false;
        }

        // ─────────────────────────── colliders ───────────────────────────

        private void UpdateColliders()
        {
            if (!generateColliders || viewer == null) return;
            int bakesLeft = maxColliderBakesPerFrame;
            Vector3 viewerLocal = body.transform.InverseTransformPoint(viewer.position);
            bool hasBubble = TryGetBubble(out Vector3 bubbleCenter, out _, out float bubbleColliderRadius);

            // UNCONDITIONAL near-viewer yield (9.5.2): whenever the gameplay bubble is
            // actively streaming THIS body, the LOD skin must NEVER collide near the
            // player — the bubble's own mesh colliders are the ground there. This is
            // deliberately independent of every scan/footing heuristic: the heuristics
            // repeatedly failed in the field and left an invisible collider sheet just
            // under the surface (the unmineable "top layer").
            var sw = SphereWorld.Instance;
            bool bubbleStreamingHere = sw != null && sw.body == body && sw.ActiveChunkCount > 24;

            foreach (var kv in _nodes)
            {
                NodeRec rec = kv.Value;
                if (rec.state != NodeState.Ready || !rec.hasMesh || rec.collider == null) continue;

                bool fine = rec.desc.CellArc <= colliderMaxCellMeters;
                float dist = Vector3.Distance(viewerLocal, (Vector3)rec.desc.Anchor);

                // Fine nodes collide within the collider range. ADDITIONALLY the node
                // chain directly under the viewer collides at EVERY depth — a fast
                // approach from orbit always finds solid ground even before the fine
                // levels have streamed (no more flying INTO the planet).
                bool underViewer = dist < 1.35f * rec.desc.arc + 250f;
                bool wantCollider = (fine && dist < colliderRange + rec.desc.arc) || underViewer;

                float ball = NodeBallRadius(rec.desc);

                if (bubbleStreamingHere && wantCollider && dist - ball < 300f)
                    wantCollider = false;

                // Handshake yield (kept as the wider, window-sized rule).
                if (hasBubble && wantCollider)
                {
                    float bubbleDist = Vector3.Distance((Vector3)rec.desc.Anchor, bubbleCenter);
                    if (bubbleDist - ball < bubbleColliderRadius) wantCollider = false;
                }

                if (wantCollider && !rec.colliderOn)
                {
                    if (bakesLeft <= 0) continue;
                    rec.collider.sharedMesh = rec.mesh;   // bakes on assignment
                    rec.collider.enabled = true;
                    rec.colliderOn = true;
                    bakesLeft--;
                }
                else if (!wantCollider && rec.colliderOn)
                {
                    rec.collider.sharedMesh = null;
                    rec.collider.enabled = false;
                    rec.colliderOn = false;
                }
            }
        }
    }
}
