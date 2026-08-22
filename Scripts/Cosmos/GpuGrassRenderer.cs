// Assets/Scripts/VoxelEngine/Cosmos/GpuGrassRenderer.cs
//
// GPU-instanced grass renderer for spherical voxel worlds.
//
// Renders THOUSANDS of grass blades around the viewer in batched GPU-instanced draw
// calls via Graphics.RenderMeshInstanced. Each blade is placed on the terrain surface
// by sampling the active world's voxels, oriented to the radial surface normal, and
// animated by the global WindField (so the whole field flows like real grass in wind).
//
// 9.18.0 REAL BLADES:
//   - The blade is a real tapered 4-level blade mesh (3 segments, pointed tip, baked
//     lean curve) instead of the flat 2-triangle quad.
//   - Rendering is BATCHED in slices of 1000 instances - Graphics.RenderMeshInstanced
//     refuses more than 1023 per call, which used to throw every frame and kill the
//     whole field once the budget grew.
//   - Matrices are kept BODY-LOCAL and re-projected to world space whenever the body
//     local-to-world matrix changes, so floating-origin rebases can no longer leave
//     the field floating in stale world coordinates.
//   - Quality floor: Low no longer turns grass OFF (0.35x) - a sparse but visible
//     field - so the ground never reads as bald plastic by accident.
//   - One console diagnostic per rebuild: [Grass] blades=N density=x.xx tier=Xxx.
//
// Only spawns grass on Grass-material surface voxels (not sand/desert/stone/snow), and
// skips steep slopes and underwater positions automatically.
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
    public class GpuGrassRenderer : MonoBehaviour
    {
        [Header("References")]
        public CelestialBody body;
        public Transform viewer;
        public Material grassMaterial;     // GPU-instanced, vertex-animated by wind
        public Mesh grassBladeMesh;        // a real tapered blade (3 segments)

        [Header("Placement")]
        [Tooltip("Radius around the viewer to fill with grass (metres).")]
        public float range = 70f;
        [Tooltip("Rebuild the field when the viewer moves more than this (metres).")]
        public float rebuildThreshold = 12f;

        [Header("Density (per square metre, before quality scaling)")]
        [Range(0f, 6f)] public float baseDensity = 2.2f;
        [Tooltip("Maximum radial terrain samples used when rebuilding one grass field.")]
        [Range(256, 8192)] public int maxSurfaceSamples = 3600;

        [Header("Blade")]
        [Range(0.1f, 2f)] public float bladeHeight = 0.65f;
        [Range(0.02f, 0.5f)] public float bladeWidth = 0.1f;
        [Range(0f, 0.5f)] public float heightVariance = 0.35f;

        [Header("Quality Scaling")]
        [Tooltip("Density multiplier per quality preset (Low, Mid, High, Ultra).")]
        public float[] qualityDensityMul = { 0.35f, 0.6f, 1.0f, 1.5f };

        // Graphics.RenderMeshInstanced refuses more than 1023 instances per call.
        private const int InstanceBatchSize = 1000;

        // -- Runtime --
        private Vector3 _lastRebuildPos = new Vector3(float.MaxValue, 0, 0);
        private Matrix4x4 _lastBodyLocalToWorld;
        private NativeArray<Matrix4x4> _localMatrices;   // body-local anchors (stable)
        private NativeArray<Matrix4x4> _matrices;        // world-space projection
        private int _instanceCount;
        private bool _built;

        // Render params (reused - no per-frame GC).
        private RenderParams _renderParams;

        private void Awake()
        {
            if (body == null) body = GetComponentInParent<CelestialBody>();
            if (grassBladeMesh == null) grassBladeMesh = CreateDefaultBlade();
            if (grassMaterial == null) grassMaterial = CreateDefaultGrassMaterial();
            _renderParams = new RenderParams(grassMaterial);
            _renderParams.worldBounds = new Bounds(Vector3.zero, new Vector3(100000f, 100000f, 100000f)); // Prevent frustum culling from hiding the grass field
            if (grassMaterial != null && grassMaterial.HasProperty("_FadeRange"))
                grassMaterial.SetFloat("_FadeRange", range);
        }

        private void OnDestroy()
        {
            DisposeField();
        }

        private void DisposeField()
        {
            if (_matrices.IsCreated) _matrices.Dispose();
            if (_localMatrices.IsCreated) _localMatrices.Dispose();
            _matrices = default;
            _localMatrices = default;
            _instanceCount = 0;
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

            // Floating-origin safety: if the body moved in the world (rebase/frame switch),
            // re-project the stored body-local anchors to the new world space.
            Matrix4x4 bodyLW = body.transform.localToWorldMatrix;
            if (_instanceCount > 0 && _matrices.IsCreated && bodyLW != _lastBodyLocalToWorld)
            {
                for (int i = 0; i < _instanceCount; i++)
                    _matrices[i] = bodyLW * _localMatrices[i];
            }
            _lastBodyLocalToWorld = bodyLW;

            // Push the current wind vector to the grass shader (VoxelGrass.shader uses _WindDir).
            // GUSTS: the strength breathes with layered time-noise (slow swells +
            // quick flutters) and the direction meanders a few degrees - the whole field
            // moves like living grass instead of a metronome.
            var wind = WindField.Instance;
            if (wind != null && grassMaterial != null)
            {
                float t = Time.time;
                float gust = 0.65f
                           + 0.45f * (Mathf.PerlinNoise(t * 0.11f, 13.7f) - 0.5f) * 2f
                           + 0.18f * (Mathf.PerlinNoise(t * 0.9f, 71.3f) - 0.5f) * 2f;
                float meander = (Mathf.PerlinNoise(t * 0.05f, 3.1f) - 0.5f) * 0.6f;   // +/- ~17 deg
                Vector3 dir = Quaternion.AngleAxis(meander * Mathf.Rad2Deg,
                    body != null ? (transform.position - body.transform.position).normalized : Vector3.up)
                    * wind.Direction;
                grassMaterial.SetVector("_WindDir", dir);
                grassMaterial.SetFloat("_WindStrength", Mathf.Clamp01(wind.strength * 0.4f * Mathf.Max(0.15f, gust)));
            }

            // Draw every blade in batched GPU-instanced draw calls (1000 per call).
            if (_instanceCount > 0 && _matrices.IsCreated)
            {
                int drawn = 0;
                while (drawn < _instanceCount)
                {
                    int slice = Mathf.Min(InstanceBatchSize, _instanceCount - drawn);
                    Graphics.RenderMeshInstanced(_renderParams, grassBladeMesh, 0,
                        _matrices.GetSubArray(drawn, slice));
                    drawn += slice;
                }
            }
        }

        // -- Field rebuild --
        private void RebuildField()
        {
            var world = ActiveWorld.Current;
            if (world == null || body == null) { _instanceCount = 0; return; }

            float densityMul = GetQualityDensityMul();
            float density = baseDensity * densityMul;
            if (density <= 0.01f)
            {
                Debug.Log($"[Grass] field empty (quality density {densityMul:0.00}).");
                DisposeField();
                return;
            }
            Vector3 viewerLocal = body.transform.InverseTransformPoint(viewer.position);
            Vector3 localUp = viewerLocal.sqrMagnitude > 0.0001f ? viewerLocal.normalized : Vector3.up;
            GetTangentBasis(localUp, out Vector3 tangentA, out Vector3 tangentB);

            int voxelRange = Mathf.CeilToInt(range);
            int densityStep = density > 1.5f ? 1 : 2;
            int sampleBudget = Mathf.Max(256, maxSurfaceSamples);
            int budgetStep = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(
                Mathf.PI * range * range / sampleBudget)));
            int step = Mathf.Max(densityStep, budgetStep);
            var candidates = new List<Matrix4x4>(sampleBudget);

            // Every candidate begins on a tangent plane around the viewer, then is projected
            // along the radial direction onto the true spherical voxel surface. This avoids
            // the old top-of-planet XZ scan that made grass vanish or lie incorrectly elsewhere.
            for (int v = -voxelRange; v <= voxelRange; v += step)
            for (int u = -voxelRange; u <= voxelRange; u += step)
            {
                if (u * u + v * v > range * range) continue;

                Vector3 probeLocal = viewerLocal + tangentA * u + tangentB * v;
                Vector3 radial = probeLocal.sqrMagnitude > 0.0001f ? probeLocal.normalized : localUp;
                if (!TryFindRadialGrassSurface(world, radial, out Vector3Int surfaceVoxel,
                        out Vector3 surfaceLocal, out Vector3 radialUpLocal, out byte surfaceMaterial))
                    continue;
                if (surfaceMaterial != (byte)MaterialId.Grass) continue;
                Vector3Int outward = surfaceVoxel + Vector3Int.RoundToInt(radialUpLocal);
                var above = world.GetVoxelWorld(outward);
                if (above.IsSolid || above.waterLevel > 0) continue;

                // Keep blades perpendicular to the planet's radial frame. Surface-net gradient
                // estimates can tilt wildly across a Cartesian chunk seam, making grass look
                // flat relative to world Y instead of wrapped around the sphere.
                Vector3 terrainNormalLocal = radialUpLocal;
                GetTangentBasis(radialUpLocal, out Vector3 localTangentA, out Vector3 localTangentB);

                int bladeCount = Mathf.Max(1, Mathf.RoundToInt(density * step * step));
                uint hash = (uint)(surfaceVoxel.x * 73856093 ^ surfaceVoxel.y * 19349663 ^ surfaceVoxel.z * 83492791);
                var rng = new Unity.Mathematics.Random(math.max(1u, hash));

                // Anchor the placement to the voxel center to prevent grass from sliding when the player moves
                Vector3 voxelCenter = new Vector3(surfaceVoxel.x + 0.5f, surfaceVoxel.y + 0.5f, surfaceVoxel.z + 0.5f) * VoxelConstants.VOXEL_SIZE;
                Vector3 stableSurfaceLocal = voxelCenter.sqrMagnitude > 0.0001f ? voxelCenter.normalized * surfaceLocal.magnitude : surfaceLocal;

                for (int blade = 0; blade < bladeCount; blade++)
                {
                    Vector3 localJitter = localTangentA * rng.NextFloat(-step * 0.5f, step * 0.5f)
                        + localTangentB * rng.NextFloat(-step * 0.5f, step * 0.5f);
                    // BODY-LOCAL anchor (9.18.0): world projection happens in Update, so a
                    // floating-origin rebase can never strand blades in stale coordinates.
                    Vector3 anchorLocal = stableSurfaceLocal + localJitter + radialUpLocal * 0.35f;
                    Quaternion rotationLocal = Quaternion.FromToRotation(Vector3.up, terrainNormalLocal);
                    rotationLocal = Quaternion.AngleAxis(rng.NextFloat(0f, 360f), terrainNormalLocal) * rotationLocal;
                    float height = bladeHeight * (1f + rng.NextFloat(-heightVariance, heightVariance));
                    candidates.Add(Matrix4x4.TRS(anchorLocal, rotationLocal, new Vector3(bladeWidth, height, 1f)));
                }

                if (candidates.Count >= sampleBudget * 3) goto done;
            }

        done:
            DisposeField();
            _instanceCount = candidates.Count;
            Debug.Log($"[Grass] blades={_instanceCount} density={density:0.00}/m2 tier={GraphicsPreset.Current} range={range:0}m");
            if (_instanceCount == 0) return;
            _localMatrices = new NativeArray<Matrix4x4>(_instanceCount, Allocator.Persistent);
            _matrices = new NativeArray<Matrix4x4>(_instanceCount, Allocator.Persistent);
            Matrix4x4 bodyLW = body.transform.localToWorldMatrix;
            _lastBodyLocalToWorld = bodyLW;
            for (int i = 0; i < _instanceCount; i++)
            {
                _localMatrices[i] = candidates[i];
                _matrices[i] = bodyLW * candidates[i];
            }
        }

        private bool TryFindRadialGrassSurface(IVoxelWorld world, Vector3 radial,
            out Vector3Int surfaceVoxel, out Vector3 surfaceLocal, out Vector3 radialUp, out byte surfaceMaterial)
        {
            surfaceVoxel = default;
            surfaceLocal = Vector3.zero;
            radialUp = radial.sqrMagnitude > 0.0001f ? radial.normalized : Vector3.up;
            surfaceMaterial = 0;

            // SphereWorld resolves the generated column directly.
            if (world is SphereWorld sphere)
                return sphere.TrySampleExteriorSurface(radial, out surfaceVoxel, out surfaceLocal, out radialUp, out surfaceMaterial);

            // Legacy compatibility only; new planet generation never takes this path.
            float estimate = body.SurfaceRadius / VoxelConstants.VOXEL_SIZE;
            for (int offset = 48; offset >= -96; offset--)
            {
                Vector3Int voxel = Vector3Int.RoundToInt(radialUp * (estimate + offset));
                Voxel value = world.GetVoxelWorld(voxel);
                if (!value.IsSolid) continue;
                Vector3Int outward = voxel + Vector3Int.RoundToInt(radialUp);
                Voxel above = world.GetVoxelWorld(outward);
                if (above.IsSolid || above.waterLevel > 0) continue;
                surfaceVoxel = voxel;
                surfaceMaterial = value.material;
                surfaceLocal = ((Vector3)voxel + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE;
                radialUp = surfaceLocal.sqrMagnitude > 0.0001f ? surfaceLocal.normalized : radialUp;
                return true;
            }
            return false;
        }

        private static void GetTangentBasis(Vector3 up, out Vector3 tangentA, out Vector3 tangentB)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(up, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
            tangentA = Vector3.Cross(reference, up).normalized;
            tangentB = Vector3.Cross(up, tangentA).normalized;
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

        // -- Default assets (so it works without authoring) --
        private static Mesh CreateDefaultBlade()
        {
            // 9.18.0 - a REAL tapered blade: four levels, narrowing width, pointed tip,
            // and a slight baked forward lean so blades curve instead of standing as
            // flat cards. 7 vertices / 5 triangles - still virtually free to rasterize.
            var mesh = new Mesh { name = "GrassBlade" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            float w = 0.5f;                       // half-width at the root (mesh units)
            float lean = 0.16f;                   // baked curvature (units of height)
            Vector3[] verts =
            {
                new Vector3(-w,     0.00f, 0f),
                new Vector3( w,     0.00f, 0f),
                new Vector3(-w*0.72f, 0.38f, lean * 0.14f),
                new Vector3( w*0.72f, 0.38f, lean * 0.14f),
                new Vector3(-w*0.44f, 0.72f, lean * 0.52f),
                new Vector3( w*0.44f, 0.72f, lean * 0.52f),
                new Vector3( 0f,    1.00f, lean * 1.00f),   // pointed tip
            };
            Vector2[] uvs =
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 0.38f), new Vector2(1f, 0.38f),
                new Vector2(0f, 0.72f), new Vector2(1f, 0.72f),
                new Vector2(0.5f, 1f),
            };
            int[] tris =
            {
                0, 2, 1,  1, 2, 3,       // root segment
                2, 4, 3,  3, 4, 5,       // mid segment
                4, 6, 5,                 // tip triangle
            };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateDefaultGrassMaterial()
        {
            // Use the custom wind-animated grass shader (procedural, GPU-instanced).
            var shader = Shader.Find("VoxelEngine/VoxelGrass")
                       ?? Shader.Find("Universal Render Pipeline/Unlit")
                       ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.name = "Mat_Grass_Runtime";
            mat.enableInstancing = true;
            return mat;
        }
    }
}
