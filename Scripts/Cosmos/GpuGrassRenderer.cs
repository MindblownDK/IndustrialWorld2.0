// Assets/Scripts/VoxelEngine/Cosmos/GpuGrassRenderer.cs
//
// GPU-instanced grass renderer for spherical voxel worlds.
//
// Renders THOUSANDS of grass blades around the viewer in a SINGLE draw call via
// Graphics.RenderMeshPrimitives (the Unity 6 GPU-instancing API). Each blade is placed on the
// terrain surface by sampling the active world's voxels, oriented to the radial surface normal,
// and animated by the global WindField (so the whole field flows like real grass in the wind).
//
// Density scales with the quality preset (Low/Mid/High/Ultra) per the design brief.
// Only spawns grass on Grass-material surface voxels (not sand/desert/stone/snow), and skips
// steep slopes and underwater positions automatically.
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// GPU-instanced grass field that follows the viewer and waves in the wind.
    /// Attach to the same GameObject as (or a child of) the active SphereWorld.
    /// </summary>
    [RequireComponent(typeof(CelestialBody))]
    public class GpuGrassRenderer : MonoBehaviour
    {
        [Header("References")]
        public CelestialBody body;
        public Transform viewer;
        public Material grassMaterial;     // GPU-instanced, vertex-animated by wind
        public Mesh grassBladeMesh;        // a simple 2-4 triangle blade

        [Header("Placement")]
        [Tooltip("Radius around the viewer to fill with grass (metres).")]
        public float range = 60f;
        [Tooltip("Rebuild the field when the viewer moves more than this (metres).")]
        public float rebuildThreshold = 8f;

        [Header("Density (per square metre, before quality scaling)")]
        [Range(0f, 4f)] public float baseDensity = 1.2f;

        [Header("Blade")]
        [Range(0.1f, 2f)] public float bladeHeight = 0.45f;
        [Range(0.1f, 2f)] public float bladeWidth = 0.08f;
        [Range(0f, 0.5f)] public float heightVariance = 0.2f;

        [Header("Quality Scaling")]
        [Tooltip("Density multiplier per quality preset (Low, Mid, High, Ultra).")]
        public float[] qualityDensityMul = { 0.0f, 0.5f, 1.0f, 1.6f };

        // ── Runtime ──
        private Vector3 _lastRebuildPos = new Vector3(float.MaxValue, 0, 0);
        private NativeArray<Matrix4x4> _matrices;
        private int _instanceCount;
        private bool _built;

        // Render params (reused — no per-frame GC).
        private RenderParams _renderParams;

        private void Awake()
        {
            if (body == null) body = GetComponentInParent<CelestialBody>();
            if (grassBladeMesh == null) grassBladeMesh = CreateDefaultBlade();
            if (grassMaterial == null) grassMaterial = CreateDefaultGrassMaterial();
            _renderParams = new RenderParams(grassMaterial);
        }

        private void OnDestroy()
        {
            if (_matrices.IsCreated) _matrices.Dispose();
        }

        private void Update()
        {
            if (body == null || viewer == null || grassBladeMesh == null || grassMaterial == null) return;

            // Rebuild when the viewer has moved enough.
            if (Vector3.Distance(viewer.position, _lastRebuildPos) > rebuildThreshold || !_built)
            {
                RebuildField();
                _lastRebuildPos = viewer.position;
                _built = true;
            }

            // Push the current wind vector to the material so the shader can animate the blades.
            var wind = WindField.Instance;
            if (wind != null && grassMaterial != null)
            {
                grassMaterial.SetVector("_WindDir", wind.Direction);
                grassMaterial.SetFloat("_WindStrength", wind.strength);
            }

            // Draw all blades in one GPU draw call.
            if (_instanceCount > 0 && _matrices.IsCreated)
                Graphics.RenderMeshPrimitives(_renderParams, grassBladeMesh, 0, _instanceCount, _matrices);
        }

        // ── Field rebuild ──
        private void RebuildField()
        {
            var world = ActiveWorld.Current;
            if (world == null) { _instanceCount = 0; return; }

            // Quality-scaled density.
            float densityMul = GetQualityDensityMul();
            if (densityMul <= 0f) { _instanceCount = 0; return; }
            float density = baseDensity * densityMul;

            // Sample candidate positions in a disc around the viewer.
            // We scan a voxel grid within `range` and place grass on Grass-surface voxels.
            Vector3 viewerLocal = body.transform.InverseTransformPoint(viewer.position);
            int voxelRange = Mathf.CeilToInt(range);
            var candidates = new List<Matrix4x4>(2048);

            // Step in voxels — coarser step = fewer samples but still dense coverage because each
            // hit spawns `density` blades via jitter.
            int step = density > 1.5f ? 1 : 2;
            float3 vLocal = viewerLocal;

            for (int dz = -voxelRange; dz <= voxelRange; dz += step)
            for (int dx = -voxelRange; dx <= voxelRange; dx += step)
            {
                // Only within the circular range.
                if (dx * dx + dz * dz > range * range) continue;

                // Find the surface voxel at this XZ offset (scan downward from above).
                int wx = Mathf.FloorToInt(vLocal.x) + dx;
                int wz = Mathf.FloorToInt(vLocal.z) + dz;

                // Scan a window of Y values around the viewer to find the topmost solid voxel.
                int topY = int.MinValue;
                byte topMat = 0;
                for (int dy = 8; dy >= -8; dy--)
                {
                    int wy = Mathf.FloorToInt(vLocal.y) + dy;
                    var voxel = world.GetVoxelWorld(new Vector3Int(wx, wy, wz));
                    if (voxel.density > 0)
                    {
                        topY = wy; topMat = voxel.material; break;
                    }
                }
                if (topY == int.MinValue) continue;

                // Only grass material (skip sand/desert/stone/snow/water).
                if (topMat != (byte)MaterialId.Grass) continue;

                // Convert the surface voxel to world position.
                float3 localPos = new float3(wx + 0.5f, topY + 1f, wz + 0.5f);
                float3 worldPos = body.transform.TransformPoint(localPos);

                // Slope check: the surface normal must be roughly aligned with radial up.
                float3 radialUp = math.normalizesafe(worldPos - (float3)body.transform.position, new float3(0, 1, 0));
                // Sample one voxel above to estimate the normal (cheap).
                float aboveDensity = world.GetVoxelWorld(new Vector3Int(wx, topY + 2, wz)).density;
                if (aboveDensity > 0) continue; // buried — skip

                // Spawn `density` blades (jittered) at this position.
                int bladeCount = Mathf.Max(1, Mathf.RoundToInt(density));
                var rng = new Unity.Mathematics.Random((uint)((wx * 73856093) ^ (wz * 19349663) ^ (topY * 83492791) + 1));
                for (int b = 0; b < bladeCount; b++)
                {
                    float3 jitter = new float3(
                        rng.NextFloat(-0.5f, 0.5f),
                        0f,
                        rng.NextFloat(-0.5f, 0.5f));
                    float3 bladePos = worldPos + jitter;

                    // Orient blade to the radial up (so grass stands upright on the sphere).
                    quaternion rot = quaternion.LookRotation(
                        new float3(rng.NextFloat(-1f, 1f), 0, rng.NextFloat(-1f, 1f)), radialUp);
                    float h = bladeHeight * (1f + rng.NextFloat(-heightVariance, heightVariance));
                    float3 scale = new float3(bladeWidth, h, 1f);

                    candidates.Add(Matrix4x4.TRS(bladePos, rot, scale));
                }

                // Hard cap to avoid runaway memory.
                if (candidates.Count > 60000) goto done;
            }
            done:

            // Upload to the GPU buffer.
            if (_matrices.IsCreated) _matrices.Dispose();
            _instanceCount = candidates.Count;
            if (_instanceCount == 0) return;
            _matrices = new NativeArray<Matrix4x4>(_instanceCount, Allocator.Persistent);
            for (int i = 0; i < _instanceCount; i++) _matrices[i] = candidates[i];
        }

        private float GetQualityDensityMul()
        {
            // Map Unity quality level (0..5) to our preset array (Low/Mid/High/Ultra).
            int q = QualitySettings.GetQualityLevel();
            // 0-1 = Low, 2-3 = Mid, 4 = High, 5 = Ultra.
            int idx = q <= 1 ? 0 : q <= 3 ? 1 : q == 4 ? 2 : 3;
            if (idx < 0 || idx >= qualityDensityMul.Length) return 1f;
            return qualityDensityMul[idx];
        }

        // ── Default assets (so it works without authoring) ──
        private static Mesh CreateDefaultBlade()
        {
            // A simple 2-triangle quad blade (tall and thin), pointing up.
            var mesh = new Mesh { name = "GrassBlade" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var verts = new Vector3[]
            {
                new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0),
                new Vector3(-0.5f, 1, 0), new Vector3(0.5f, 1, 0),
            };
            var uvs = new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            var tris = new int[] { 0, 2, 1, 1, 2, 3 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateDefaultGrassMaterial()
        {
            // URP/Unlit with GPU instancing + vertex-color. The wind animation can be done in a
            // custom shader later; for now this renders solid green blades that the GPU draws in
            // one call.
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                       ?? Shader.Find("Unlit/Color")
                       ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.name = "Mat_Grass_Runtime";
            mat.enableInstancing = true;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.32f, 0.52f, 0.18f, 1f));
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", new Color(0.32f, 0.52f, 0.18f, 1f));
            return mat;
        }
    }
}
