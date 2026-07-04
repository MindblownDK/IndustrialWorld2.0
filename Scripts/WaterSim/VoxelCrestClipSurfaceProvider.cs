using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// v3.20.3 – Hybrid Crest clip surface
    /// Generates a procedural include-area mesh that tells Crest OceanRenderer
    /// WHERE voxel water actually exists. Eliminates the "big infinite water plane"
    /// artifact – Crest tiles only render over scanned water bodies (oceans, lakes).
    /// Small lakes/rivers/oil puddles still use voxel LiquidSurface mesh (hybrid mode).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(-30)]
    public class VoxelCrestClipSurfaceProvider : MonoBehaviour
    {
        [Header("Sampling")]
        public Transform viewpoint;
        [Range(128f, 2048f)] public float radius = 768f;
        [Range(8f, 64f)] public float cellSize = 24f;
        [Range(0.2f, 1.5f)] public float rebuildInterval = 0.4f;

        [Header("Clip Tuning")]
        [Tooltip("Minimum water depth to include – filters out tiny puddles, those use voxel mesh instead.")]
        public float minDepthToInclude = 1.5f;
        [Tooltip("Expand clip mesh slightly to avoid hard edges")]
        public float borderExpand = 6f;

        private Mesh _mesh;
        private MeshFilter _mf;
        private MeshRenderer _mr;
        private float _nextRebuild;

        private readonly List<Vector3> _verts = new(4096);
        private readonly List<int> _tris = new(6144);
        private readonly List<Vector2> _uvs = new(4096);

        private void Awake() { EnsureComponents(); }
        private void OnEnable() { EnsureComponents(); _nextRebuild = 0f; }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextRebuild) return;
            _nextRebuild = Time.unscaledTime + rebuildInterval;
            RebuildClipMesh();
        }

        private void EnsureComponents()
        {
            _mf = GetComponent<MeshFilter>();
            if (_mf == null) _mf = gameObject.AddComponent<MeshFilter>();
            _mr = GetComponent<MeshRenderer>();
            if (_mr == null) _mr = gameObject.AddComponent<MeshRenderer>();

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "VoxelCrestClipSurface", indexFormat = IndexFormat.UInt32 };
                _mf.sharedMesh = _mesh;
            }

            // Attach Crest ClipSurface input if not present
            var clipType = System.Type.GetType("Crest.RegisterClipSurfaceInput, Crest");
            if (clipType != null && GetComponent(clipType) == null)
            {
                gameObject.AddComponent(clipType);
            }

            // Assign ClipSurfaceIncludeArea material
            if (_mr.sharedMaterial == null)
            {
                var mat = Resources.Load<Material>("ClipSurfaceIncludeArea");
                if (mat == null)
                {
                    // Try load from Crest assets via AssetDatabase at edit time, else create fallback
#if UNITY_EDITOR
                    mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Liquid/Crest/Crest/Materials/OceanInputs/ClipSurfaceIncludeArea.mat");
#endif
                }
                if (mat != null)
                {
                    _mr.sharedMaterial = new Material(mat) { name = "VoxelClipSurface_Runtime" };
                }
            }

            _mr.shadowCastingMode = ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            _mr.enabled = true;
            // Hide mesh visually – Crest reads it via depth input, we don't want to see the clip mesh itself
            _mr.forceRenderingOff = true;
        }

        private Transform GetView()
        {
            if (viewpoint != null) return viewpoint;
            var world = ActiveWorld.Current;
            if (world != null && world.Viewer != null) return world.Viewer;
            var cam = Camera.main;
            return cam != null ? cam.transform : transform;
        }

        private void RebuildClipMesh()
        {
            EnsureComponents();
            var view = GetView();
            if (view == null) { Clear(); return; }

            var world = ActiveWorld.Current;
            if (world == null) { Clear(); return; }

            Vector3 center = view.position;
            Vector3 up = PlanetWaterUtility.IsPlanetWorld ? PlanetWaterUtility.WorldUp(center) : Vector3.up;
            up.Normalize();
            Vector3 tA = Vector3.Cross(up, Vector3.forward);
            if (tA.sqrMagnitude < 0.001f) tA = Vector3.Cross(up, Vector3.right);
            tA.Normalize();
            Vector3 tB = Vector3.Cross(up, tA).normalized;

            int steps = Mathf.Clamp(Mathf.CeilToInt(radius / cellSize), 4, 48);
            float half = steps * cellSize * 0.5f;

            _verts.Clear();
            _tris.Clear();
            _uvs.Clear();

            // Build a sparse grid – only add quads where voxel water depth > minDepth
            int[,] indexGrid = new int[steps + 1, steps + 1];
            for (int z = 0; z <= steps; z++)
            for (int x = 0; x <= steps; x++)
                indexGrid[x, z] = -1;

            for (int z = 0; z <= steps; z++)
            {
                for (int x = 0; x <= steps; x++)
                {
                    float ox = (x - steps * 0.5f) * cellSize;
                    float oz = (z - steps * 0.5f) * cellSize;
                    Vector3 samplePos = center + tA * ox + tB * oz;

                    // Snap to planet sea shell if spherical
                    if (PlanetWaterUtility.IsPlanetWorld)
                    {
                        Vector3 dir = PlanetWaterUtility.WorldUp(samplePos);
                        float seaR = world.SeaLevel * VoxelConstants.VOXEL_SIZE;
                        samplePos = dir.normalized * seaR;
                    }

                    bool hasWater = VoxelWaterDepthSampler.TrySampleDepth(samplePos, out float depth, out _)
                                 || VoxelWaterDepthSampler.TrySampleSeaSurface(samplePos, out depth, out _);

                    if (!hasWater || depth < minDepthToInclude) continue;

                    int idx = _verts.Count;
                    indexGrid[x, z] = idx;
                    _verts.Add(transform.InverseTransformPoint(samplePos));
                    _uvs.Add(new Vector2(x / (float)steps, z / (float)steps));
                }
            }

            // Stitch quads
            for (int z = 0; z < steps; z++)
            for (int x = 0; x < steps; x++)
            {
                int i00 = indexGrid[x, z];
                int i10 = indexGrid[x + 1, z];
                int i11 = indexGrid[x + 1, z + 1];
                int i01 = indexGrid[x, z + 1];
                if (i00 < 0 || i10 < 0 || i11 < 0 || i01 < 0) continue;
                _tris.Add(i00); _tris.Add(i10); _tris.Add(i11);
                _tris.Add(i00); _tris.Add(i11); _tris.Add(i01);
            }

            if (_verts.Count < 3 || _tris.Count < 3)
            {
                Clear();
                return;
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices: _verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
            _mesh.RecalculateNormals();
            _mf.sharedMesh = _mesh;
        }

        private void Clear()
        {
            if (_mesh != null) _mesh.Clear();
        }

        private void OnDisable()
        {
            Clear();
        }
    }
}
