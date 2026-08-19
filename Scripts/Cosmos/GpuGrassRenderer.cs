// Assets/Scripts/VoxelEngine/Cosmos/GpuGrassRenderer.cs
//
// GPU-instanced grass renderer for spherical voxel worlds.
//
// Renders THOUSANDS of grass blades around the viewer in a SINGLE draw call via
// Graphics.RenderMeshInstanced (the Unity 6 GPU-instancing API that accepts per-instance matrices). Each blade is placed on the
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
    public class GpuGrassRenderer : MonoBehaviour
    {
        [Header("References")]
        public CelestialBody body;
        public Transform viewer;
        public Material grassMaterial;     // GPU-instanced, vertex-animated by wind
        public Mesh grassBladeMesh;        // a simple 2-4 triangle blade

        [Header("Placement")]
        [Tooltip("Radius around the viewer to fill with grass (metres).")]
        public float range = 70f;
        [Tooltip("Rebuild the field when the viewer moves more than this (metres).")]
        public float rebuildThreshold = 12f;

        [Header("Density (per square metre, before quality scaling)")]
        [Range(0f, 4f)] public float baseDensity = 1.2f;
        [Tooltip("Maximum radial terrain samples used when rebuilding one grass field.")]
        [Range(256, 4096)] public int maxSurfaceSamples = 2600;

        [Header("Blade")]
        [Range(0.1f, 2f)] public float bladeHeight = 0.55f;
        [Range(0.1f, 2f)] public float bladeWidth = 0.08f;
        [Range(0f, 0.5f)] public float heightVariance = 0.3f;

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
            _renderParams.worldBounds = new Bounds(Vector3.zero, new Vector3(100000f, 100000f, 100000f)); // Prevent frustum culling from hiding the grass field
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

            // Push the current wind vector to the grass shader (VoxelGrass.shader uses _WindDir).
            // 9.9.0 GUSTS: the strength breathes with layered time-noise (slow swells +
            // quick flutters) and the direction meanders a few degrees — the whole field
            // moves like living grass instead of a metronome.
            var wind = WindField.Instance;
            if (wind != null && grassMaterial != null)
            {
                float t = Time.time;
                float gust = 0.65f
                           + 0.45f * (Mathf.PerlinNoise(t * 0.11f, 13.7f) - 0.5f) * 2f
                           + 0.18f * (Mathf.PerlinNoise(t * 0.9f, 71.3f) - 0.5f) * 2f;
                float meander = (Mathf.PerlinNoise(t * 0.05f, 3.1f) - 0.5f) * 0.6f;   // ±~17°
                Vector3 dir = Quaternion.AngleAxis(meander * Mathf.Rad2Deg,
                    body != null ? (transform.position - body.transform.position).normalized : Vector3.up)
                    * wind.Direction;
                grassMaterial.SetVector("_WindDir", dir);
                grassMaterial.SetFloat("_WindStrength", Mathf.Clamp01(wind.strength * 0.4f * Mathf.Max(0.15f, gust)));
            }

            // Draw all blades in one GPU draw call.
            if (_instanceCount > 0 && _matrices.IsCreated)
                Graphics.RenderMeshInstanced(_renderParams, grassBladeMesh, 0, _matrices);
        }

        // ── Field rebuild ──
        private void RebuildField()
        {
            var world = ActiveWorld.Current;
            if (world == null || body == null) { _instanceCount = 0; return; }

            float densityMul = GetQualityDensityMul();
            if (densityMul <= 0f) { _instanceCount = 0; return; }
            float density = baseDensity * densityMul;
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
                Vector3 terrainNormalWorld = body.transform.TransformDirection(terrainNormalLocal).normalized;
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
                    Vector3 worldPosition = body.transform.TransformPoint(stableSurfaceLocal + localJitter + radialUpLocal * 0.35f);
                    Quaternion rotation = Quaternion.FromToRotation(Vector3.up, terrainNormalWorld);
                    rotation = Quaternion.AngleAxis(rng.NextFloat(0f, 360f), terrainNormalWorld) * rotation;
                    float height = bladeHeight * (1f + rng.NextFloat(-heightVariance, heightVariance));
                    candidates.Add(Matrix4x4.TRS(worldPosition, rotation, new Vector3(bladeWidth, height, 1f)));
                }

                if (candidates.Count >= sampleBudget * 3) goto done;
            }

        done:
            if (_matrices.IsCreated) _matrices.Dispose();
            _instanceCount = candidates.Count;
            if (_instanceCount == 0) return;
            _matrices = new NativeArray<Matrix4x4>(_instanceCount, Allocator.Persistent);
            for (int i = 0; i < _instanceCount; i++) _matrices[i] = candidates[i];
        }

        private bool TryFindRadialGrassSurface(IVoxelWorld world, Vector3 radial,
            out Vector3Int surfaceVoxel, out Vector3 surfaceLocal, out Vector3 radialUp, out byte surfaceMaterial)
        {
            surfaceVoxel = default;
            surfaceLocal = Vector3.zero;
            radialUp = radial.sqrMagnitude > 0.0001f ? radial.normalized : Vector3.up;
            surfaceMaterial = 0;

            // SphereWorld resolves the generated column directly. This replaces the old
            // 145-voxel outward-to-inward scan for every grass sample.
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

        private static Vector3 EstimateSurfaceNormal(IVoxelWorld world, Vector3Int voxel, Vector3 fallback)
        {
            float gx = world.GetVoxelWorld(voxel + Vector3Int.left).density - world.GetVoxelWorld(voxel + Vector3Int.right).density;
            float gy = world.GetVoxelWorld(voxel + Vector3Int.down).density - world.GetVoxelWorld(voxel + Vector3Int.up).density;
            float gz = world.GetVoxelWorld(voxel + Vector3Int.back).density - world.GetVoxelWorld(voxel + Vector3Int.forward).density;
            Vector3 normal = new Vector3(gx, gy, gz);
            return normal.sqrMagnitude > 0.0001f ? normal.normalized : fallback;
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
