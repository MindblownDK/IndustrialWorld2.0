// Assets/Scripts/VoxelEngine/GpuVoxel/GpuOceanEngine.cs
//
// QUADTREE OCEAN SPHERE (9.0.0) — the water counterpart of GpuPlanetEngine.
//
// The same spherified quadtree drives curved water patches at the body's sea
// radius. Tiles whose terrain sits entirely above sea level are skipped, so
// no water sphere ever wraps through dry land or caves. Patches share the
// chunk-water material (VoxelEngine/VoxelWaterURP), so boat wakes
// (NativeWaterWakeSystem), Gerstner waves, foam and the flow-map UV2 channel
// all work on the open ocean exactly as they do in the gameplay bubble —
// UV2 is reserved per-vertex for the Phase-3 flow simulation.
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Cosmos;
using VoxelEngine.WaterSim;

namespace VoxelEngine.GpuVoxel
{
    [DisallowMultipleComponent]
    public sealed class GpuOceanEngine : MonoBehaviour
    {
        [Header("Body & References")]
        public CelestialBody body;
        public Transform viewer;

        [Header("Streaming")]
        [Tooltip("A patch splits while the viewer is closer than splitFactor × its footprint size.")]
        [Range(1.2f, 4f)] public float splitFactor = 2f;
        [Tooltip("Finest water grid spacing (m) near the viewer — vertex resolution available to Gerstner waves and wakes.")]
        [Range(2f, 32f)] public float finestCellMeters = 6f;
        [Tooltip("Water surface sits this far (m) below the mathematical sea radius so the bubble's chunk water renders on top without z-fighting.")]
        [Range(0f, 1f)] public float surfaceInset = 0.3f;
        [Range(1, 8)] public int maxBuildsPerFrame = 4;
        [Range(0.1f, 2f)] public float desiredRefreshInterval = 0.4f;

        private const int PATCH_CELLS = 32;
        private const int PATCH_VERTS = PATCH_CELLS + 1;
        private const float SKIRT_OVERLAP = 0.015f;   // uv overshoot hides LOD T-junction pinholes

        private sealed class PatchRec
        {
            public QuadNodeDesc desc;
            public bool built;
            public bool hasMesh;
            public GameObject go;
            public MeshFilter filter;
            public MeshRenderer renderer;
            public Mesh mesh;
            public int desiredStamp;
        }

        private readonly Dictionary<QuadNodeId, PatchRec> _patches = new();
        private readonly List<PatchRec> _queue = new();
        private readonly Stack<GameObject> _goPool = new();

        private NativeList<QuadNodeDesc> _desired;
        private JobHandle _desiredHandle;
        private bool _desiredRunning;
        private bool _hasDesiredSet;
        private float _desiredTimer;
        private int _desiredStamp;
        private int _maxDepth = 5;
        private CelestialBody _activeBody;
        private Material _waterMaterial;

        // The descent job REQUIRES a constructed split-set container (9.4.0 fix for
        // "splitSet has not been assigned or constructed" — the exception aborted every
        // ocean rebuild, leaving seas rendered as giant holes). The ocean uses an empty
        // set: water patches are flat, so split flip-flop is invisible here.
        private NativeParallelHashSet<QuadNodeId> _emptySplitSet;

        // reusable mesh-build buffers
        private Vector3[] _verts;
        private Vector3[] _normals;
        private Vector2[] _uv0;
        private Vector2[] _uv2;
        private Color32[] _colors;
        private int[] _tris;

        private void Awake()
        {
            _desired = new NativeList<QuadNodeDesc>(512, Allocator.Persistent);
            _emptySplitSet = new NativeParallelHashSet<QuadNodeId>(1, Allocator.Persistent);

            int vcount = PATCH_VERTS * PATCH_VERTS;
            _verts = new Vector3[vcount];
            _normals = new Vector3[vcount];
            _uv0 = new Vector2[vcount];
            _uv2 = new Vector2[vcount];
            _colors = new Color32[vcount];
            _tris = new int[PATCH_CELLS * PATCH_CELLS * 6];
            int t = 0;
            for (int j = 0; j < PATCH_CELLS; j++)
            for (int i = 0; i < PATCH_CELLS; i++)
            {
                int v0 = i + j * PATCH_VERTS;
                _tris[t++] = v0;
                _tris[t++] = v0 + PATCH_VERTS;
                _tris[t++] = v0 + 1;
                _tris[t++] = v0 + 1;
                _tris[t++] = v0 + PATCH_VERTS;
                _tris[t++] = v0 + PATCH_VERTS + 1;
            }
        }

        private void OnDestroy()
        {
            if (_desiredRunning) { _desiredHandle.Complete(); _desiredRunning = false; }
            foreach (var kv in _patches) ReleasePatch(kv.Value);
            _patches.Clear();
            _queue.Clear();
            while (_goPool.Count > 0) Destroy(_goPool.Pop());
            if (_desired.IsCreated) _desired.Dispose();
            if (_emptySplitSet.IsCreated) _emptySplitSet.Dispose();
        }

        /// <summary>Quality-tier hook (QualityPresetApplier).</summary>
        public void ApplyQualityBudget(int lodResolution)
        {
            // Higher LOD tiers buy a finer near-shore water grid.
            finestCellMeters = lodResolution >= 2562 ? 4f : (lodResolution >= 1282 ? 6f : 9f);
        }

        private void Update()
        {
            if (!ResolveContext()) return;
            PumpDesiredJob();
            PumpBuildQueue();
            UpdateVisibilityAndEviction();
        }

        private bool ResolveContext()
        {
            if (body == null) return false;
            if (body.genParams.isAsteroidBelt == 1) return false;
            if (body.genParams.seaRadius - body.genParams.radiusWorld <= 0.5f) return false; // dry world
            if (viewer == null)
            {
                var cam = Camera.main;
                if (cam != null) viewer = cam.transform;
                if (viewer == null) return false;
            }
            if (_activeBody != body)
            {
                _activeBody = body;
                ResetAllPatches();
                float faceArc = (Mathf.PI * 0.5f) * body.genParams.radiusWorld;
                _maxDepth = Mathf.Clamp(
                    Mathf.CeilToInt(Mathf.Log(faceArc / (PATCH_CELLS * Mathf.Max(2f, finestCellMeters)), 2f)),
                    1, 11);
            }
            if (_waterMaterial == null)
                _waterMaterial = WaterMeshBuilder.GetWaterMaterial();
            return true;
        }

        public void ResetAllPatches()
        {
            if (_desiredRunning) { _desiredHandle.Complete(); _desiredRunning = false; }
            foreach (var kv in _patches) ReleasePatch(kv.Value);
            _patches.Clear();
            _queue.Clear();
            _hasDesiredSet = false;
            _desiredTimer = 999f;
        }

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
            var job = new BuildDesiredLeavesJob
            {
                seed = prm.seed,
                radiusWorld = prm.radiusWorld,
                baseHeight = prm.baseHeight,
                seaRadius = prm.seaRadius,
                continentScale = prm.continentScaleDir,
                mountainScale = prm.mountainScale,
                viewerLocal = (float3)(Vector3)body.transform.InverseTransformPoint(viewer.position),
                maxDepth = _maxDepth,
                splitFactor = splitFactor,
                maxLeaves = 3072,
                splitSet = _emptySplitSet,
                results = _desired
            };
            _desiredHandle = job.Schedule();
            _desiredRunning = true;
        }

        private void ReconcileDesired()
        {
            _hasDesiredSet = true;
            _desiredStamp++;
            for (int i = 0; i < _desired.Length; i++)
            {
                QuadNodeDesc desc = _desired[i];
                if (_patches.TryGetValue(desc.id, out PatchRec rec))
                {
                    rec.desiredStamp = _desiredStamp;
                }
                else
                {
                    rec = new PatchRec { desc = desc, desiredStamp = _desiredStamp };
                    _patches.Add(desc.id, rec);
                    _queue.Add(rec);
                }
            }
        }

        private void PumpBuildQueue()
        {
            int budget = maxBuildsPerFrame;
            while (budget > 0 && _queue.Count > 0)
            {
                // coarse-first, then nearest — same top-down policy as the terrain.
                int bestIdx = -1;
                float bestScore = float.MaxValue;
                for (int i = 0; i < _queue.Count; i++)
                {
                    PatchRec r = _queue[i];
                    if (r.desiredStamp != _desiredStamp && _hasDesiredSet)
                    {
                        _patches.Remove(r.desc.id);
                        _queue.RemoveAt(i);
                        i--;
                        continue;
                    }
                    float score = r.desc.id.depth * 1e9f + r.desc.distance;
                    if (score < bestScore) { bestScore = score; bestIdx = i; }
                }
                if (bestIdx < 0) return;
                PatchRec rec = _queue[bestIdx];
                _queue.RemoveAt(bestIdx);
                BuildPatch(rec);
                budget--;
            }
        }

        private void BuildPatch(PatchRec rec)
        {
            rec.built = true;
            var prm = body.genParams;

            // Skip tiles whose terrain is entirely above the waterline (dry land).
            if (rec.desc.minSurface > prm.seaRadius + 1f)
            {
                rec.hasMesh = false;
                return;
            }

            float seaR = prm.seaRadius - surfaceInset;
            float3 anchor = rec.desc.centerDir * seaR;

            // uv window with a small overshoot so neighbouring depths overlap
            // instead of leaving sub-pixel T-junction pinholes.
            float2 uvMin = rec.desc.uvMin - rec.desc.uvSize * SKIRT_OVERLAP;
            float2 uvSize = rec.desc.uvSize * (1f + 2f * SKIRT_OVERLAP);

            for (int j = 0; j < PATCH_VERTS; j++)
            for (int i = 0; i < PATCH_VERTS; i++)
            {
                float u = uvMin.x + uvSize.x * (i / (float)PATCH_CELLS);
                float v = uvMin.y + uvSize.y * (j / (float)PATCH_CELLS);
                float3 dir = CubeSphere.FaceDirection(rec.desc.id.face, u, v);
                float3 pos = dir * seaR - anchor;

                int vi = i + j * PATCH_VERTS;
                _verts[vi] = (Vector3)pos;
                _normals[vi] = (Vector3)dir;
                _uv0[vi] = new Vector2(u * 64f, v * 64f);
                _uv2[vi] = Vector2.zero;                    // flow-map channel (Phase 3)
                _colors[vi] = new Color32(255, 255, 255, 255);
            }

            if (rec.mesh == null)
                rec.mesh = new Mesh { name = $"GpuOcean {rec.desc.id}", indexFormat = IndexFormat.UInt16 };
            else
                rec.mesh.Clear();

            rec.mesh.vertices = _verts;
            rec.mesh.normals = _normals;
            rec.mesh.uv = _uv0;
            rec.mesh.uv2 = _uv2;
            rec.mesh.colors32 = _colors;
            rec.mesh.triangles = _tris;
            rec.mesh.RecalculateBounds();

            if (rec.go == null) AcquirePatchObjects(rec);
            rec.go.transform.localPosition = (Vector3)anchor;
            rec.filter.sharedMesh = rec.mesh;
            rec.renderer.sharedMaterial = _waterMaterial;
            rec.hasMesh = true;
        }

        private void AcquirePatchObjects(PatchRec rec)
        {
            GameObject go;
            if (_goPool.Count > 0)
            {
                go = _goPool.Pop();
                go.SetActive(true);
            }
            else
            {
                go = new GameObject("GpuOceanPatch");
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            go.name = $"GpuOcean {rec.desc.id}";
            go.transform.SetParent(body.transform, false);
            go.transform.localRotation = Quaternion.identity;
            rec.go = go;
            rec.filter = go.GetComponent<MeshFilter>();
            rec.renderer = go.GetComponent<MeshRenderer>();
        }

        private void ReleasePatch(PatchRec rec)
        {
            if (rec.go != null)
            {
                rec.filter.sharedMesh = null;
                rec.go.SetActive(false);
                _goPool.Push(rec.go);
                rec.go = null;
                rec.filter = null;
                rec.renderer = null;
            }
            if (rec.mesh != null)
            {
                Destroy(rec.mesh);
                rec.mesh = null;
            }
            rec.hasMesh = false;
        }

        private void UpdateVisibilityAndEviction()
        {
            if (!_hasDesiredSet) return;

            // Hide a built patch once all four children are built (top-down swap).
            foreach (var kv in _patches)
            {
                PatchRec rec = kv.Value;
                if (!rec.built || rec.renderer == null) continue;
                rec.renderer.enabled = rec.hasMesh && !ChildrenCover(kv.Key, 0);
            }

            List<QuadNodeId> toRemove = null;
            foreach (var kv in _patches)
            {
                PatchRec rec = kv.Value;
                if (rec.desiredStamp == _desiredStamp) continue;
                bool covered = ChildrenCover(kv.Key, 0) || AncestorBuilt(kv.Key);
                if (!rec.built || covered || !rec.hasMesh)
                {
                    toRemove ??= new List<QuadNodeId>(16);
                    toRemove.Add(kv.Key);
                }
            }
            if (toRemove != null)
            {
                foreach (var id in toRemove)
                {
                    if (!_patches.TryGetValue(id, out PatchRec rec)) continue;
                    ReleasePatch(rec);
                    _patches.Remove(id);
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
                if (_patches.TryGetValue(child, out PatchRec rec))
                {
                    if (rec.built) continue;
                    if (ChildrenCover(child, recursion + 1)) continue;
                }
                else if (ChildrenCover(child, recursion + 1)) continue;
                return false;
            }
            return true;
        }

        private bool AncestorBuilt(QuadNodeId id)
        {
            QuadNodeId cur = id;
            for (int i = 0; i < 4 && cur.depth > 0; i++)
            {
                cur = cur.Parent;
                if (_patches.TryGetValue(cur, out PatchRec rec) &&
                    rec.built && rec.desiredStamp == _desiredStamp)
                    return true;
            }
            return false;
        }
    }
}
