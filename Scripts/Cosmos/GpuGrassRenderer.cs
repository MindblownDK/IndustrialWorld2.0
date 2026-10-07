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
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Environment;
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
        public float range = 48f;
        [Tooltip("Rebuild the field when the viewer moves more than this (metres).")]
        public float rebuildThreshold = 12f;

        [Header("Density (per square metre, before quality scaling)")]
        [Range(0f, 6f)] public float baseDensity = 3.6f;
        [Tooltip("Maximum radial terrain samples used when rebuilding one grass field.")]
        [Range(256, 8192)] public int maxSurfaceSamples = 3600;

        [Header("Blade")]
        [Range(0.1f, 2f)] public float bladeHeight = 0.48f;
        [Range(0.02f, 0.5f)] public float bladeWidth = 0.045f;
        [Range(0f, 0.5f)] public float heightVariance = 0.35f;

        [Header("Quality Scaling")]
        [Tooltip("Density multiplier per quality preset (Low, Mid, High, Ultra).")]
        public float[] qualityDensityMul = { 0.45f, 0.6f, 1.2f, 1.8f };

        // Graphics.RenderMeshInstanced refuses more than 1023 instances per call.
        private const int InstanceBatchSize = 1000;

        // -- Runtime --
        private Vector3 _lastRebuildPos = new Vector3(float.MaxValue, 0, 0);
        private Matrix4x4 _lastBodyLocalToWorld;
        private NativeArray<Matrix4x4> _localMatrices;   // body-local anchors (stable)
        private NativeArray<Matrix4x4> _matrices;        // world-space projection
        private int _instanceCount;
        private bool _built;
        private int _lastEditVersion = -1;
        private float _lastRebuildTime;
        private float _nextEcologySample;
        private float _lastEcologyMultiplier = -1f;
        private float _lastQualityMultiplier = -1f;
        private bool _ownsBladeMesh;
        private float _nextStreamingRefresh;
        private Coroutine _fieldBuild;
        private Color _healthyBaseColor;
        private Color _healthyTipColor;

        // Render params (reused - no per-frame GC).
        private RenderParams _renderParams;

        private void Awake()
        {
            // 14.46.2: no shaders on a dedicated server - purely visual,
            // the component retires itself headless instead of re-trying
            // Shader.Find every frame (THE console spam).
            if (VoxelEngine.Networking.NetworkSession.IsDedicated) { enabled = false; return; }
            if (body == null) body = GetComponentInParent<CelestialBody>();
            if (grassBladeMesh == null) { grassBladeMesh = CreateDefaultBlade(); _ownsBladeMesh = true; }
            if (grassMaterial == null) grassMaterial = CreateDefaultGrassMaterial();
            else grassMaterial = new Material(grassMaterial);
            if (grassMaterial == null) { enabled = false; return; }
            _renderParams = new RenderParams(grassMaterial);
            _renderParams.worldBounds = new Bounds(Vector3.zero, new Vector3(100000f, 100000f, 100000f)); // Prevent frustum culling from hiding the grass field
            if (grassMaterial != null)
            {
                if (grassMaterial.HasProperty("_FadeRange")) grassMaterial.SetFloat("_FadeRange", range);
                _healthyBaseColor = grassMaterial.HasProperty("_BaseColor")
                    ? grassMaterial.GetColor("_BaseColor") : new Color(0.22f, 0.40f, 0.12f, 1f);
                _healthyTipColor = grassMaterial.HasProperty("_TipColor")
                    ? grassMaterial.GetColor("_TipColor") : new Color(0.45f, 0.65f, 0.22f, 1f);
            }
        }

        private void OnDisable()
        {
            if (_fieldBuild != null) StopCoroutine(_fieldBuild);
            _fieldBuild = null;
            _built = false;
            DisposeField();
        }

        private void OnDestroy()
        {
            DisposeField();
            if (grassMaterial != null) Destroy(grassMaterial);
            if (_ownsBladeMesh && grassBladeMesh != null) Destroy(grassBladeMesh);
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
            if (VoxelEngine.GridSystem.AtmosphereManager.IsInSpace(viewer.position)
                || GravityProvider.ActiveBody == null)
            {
                if (_fieldBuild != null) StopCoroutine(_fieldBuild);
                _fieldBuild = null;
                _built = false;
                if (_instanceCount > 0) DisposeField();
                return;
            }

            // Pollution pressure is slow-moving. Sample it on the same low cadence as
            // other environmental visuals, recolour immediately, and rebuild only after
            // a meaningful density change so the GPU field does not churn every frame.
            if (Time.unscaledTime >= _nextEcologySample)
            {
                _nextEcologySample = Time.unscaledTime + 2.5f;
                EcologyReading ecology = EcologyPressure.Sample(viewer.position);
                ApplyEcologyColour(ecology);
                float multiplier = ecology.FloraSpawnMultiplier;
                bool densityChanged = _lastEcologyMultiplier >= 0f
                    && Mathf.Abs(multiplier - _lastEcologyMultiplier) >= 0.08f;
                _lastEcologyMultiplier = multiplier;
                if (densityChanged && _built && Time.unscaledTime - _lastRebuildTime >= 0.6f)
                {
                    RebuildField();
                    _lastRebuildPos = viewer.position;
                    _lastRebuildTime = Time.unscaledTime;
                }
            }

            // 9.18.2 - PLAYER EDITS rebuild the field: mining grass (or the ground under
            // it) must remove its blades immediately, not when the player next walks 12 m.
            // Bumped once per edit batch by VoxelEditor; rate-limited so a drilling spree
            // cannot rebuild more than ~1.6 times per second.
            var sphere = SphereWorld.Instance;
            if (sphere != null && sphere.PlayerEditVersion != _lastEditVersion
                && Time.unscaledTime - _lastRebuildTime >= 0.6f)
            {
                _lastEditVersion = sphere.PlayerEditVersion;
                RebuildField();
                _lastRebuildPos = viewer.position;
                _lastRebuildTime = Time.unscaledTime;
                _built = true;
            }

            // Density preset changes must also rebuild an already populated field.
            float qualityMultiplier = GetQualityDensityMul();
            if (!Mathf.Approximately(_lastQualityMultiplier, qualityMultiplier))
            {
                _lastQualityMultiplier = qualityMultiplier;
                _built = false;
            }

            // Rebuild when the viewer has moved enough.
            if (Vector3.Distance(viewer.position, _lastRebuildPos) > rebuildThreshold || !_built
                || Time.unscaledTime >= _nextStreamingRefresh)
            {
                RebuildField();
                _lastRebuildPos = viewer.position;
                _lastRebuildTime = Time.unscaledTime;
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

        private void ApplyEcologyColour(EcologyReading ecology)
        {
            if (grassMaterial == null) return;
            float stress = 1f - ecology.Vitality01;
            Color stressedBase = new Color(0.31f, 0.25f, 0.13f, _healthyBaseColor.a);
            Color stressedTip = new Color(0.43f, 0.37f, 0.18f, _healthyTipColor.a);
            if (grassMaterial.HasProperty("_BaseColor"))
                grassMaterial.SetColor("_BaseColor", Color.Lerp(_healthyBaseColor, stressedBase, stress * 0.86f));
            if (grassMaterial.HasProperty("_TipColor"))
                grassMaterial.SetColor("_TipColor", Color.Lerp(_healthyTipColor, stressedTip, stress * 0.86f));
        }

        // -- Field rebuild --
        private void RebuildField()
        {
            if (_fieldBuild != null) return;
            _fieldBuild = StartCoroutine(BuildFieldProgressively());
        }

        private IEnumerator BuildFieldProgressively()
        {
            // Start on the next frame so the coroutine handle is assigned even on early exit.
            yield return null;
            _nextStreamingRefresh = Time.unscaledTime + 4f;
            var world = ActiveWorld.Current;
            if (world == null || body == null || viewer == null) { _instanceCount = 0; _fieldBuild = null; yield break; }

            float densityMul = GetQualityDensityMul();
            EcologyReading ecology = EcologyPressure.Sample(viewer.position);
            _lastEcologyMultiplier = ecology.FloraSpawnMultiplier;
            ApplyEcologyColour(ecology);
            float density = baseDensity * densityMul * ecology.FloraSpawnMultiplier;
            if (density <= 0.01f)
            {
                Debug.Log($"[Grass] field empty (quality density {densityMul:0.00}).");
                DisposeField();
                _fieldBuild = null; yield break;
            }
            Vector3 viewerLocal = body.transform.InverseTransformPoint(viewer.position);
            Vector3 localUp = viewerLocal.sqrMagnitude > 0.0001f ? viewerLocal.normalized : Vector3.up;
            GetTangentBasis(localUp, out Vector3 tangentA, out Vector3 tangentB);

            int voxelRange = Mathf.CeilToInt(range);
            int densityStep = density > 1.5f ? 1 : 2;
            int sampleBudget = Mathf.Max(256, maxSurfaceSamples);
            int budgetStep = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(
                Mathf.PI * range * range / sampleBudget)));
            // At least 3 m cells keep the default 48 m field below 850 base probes.
            // A uniform per-cell cap fills every quadrant instead of truncating one side.
            int step = Mathf.Max(3, Mathf.Max(densityStep, budgetStep));
            var candidates = new List<Matrix4x4>(Mathf.CeilToInt(Mathf.PI * range * range / (step * step)) * 24);

            int processedCells = 0;
            // Every candidate begins on a tangent plane around the viewer, then is projected
            // along the radial direction onto the true spherical voxel surface. This avoids
            // the old top-of-planet XZ scan that made grass vanish or lie incorrectly elsewhere.
            for (int v = -voxelRange; v <= voxelRange; v += step)
            for (int u = -voxelRange; u <= voxelRange; u += step)
            {
                if (++processedCells % 24 == 0)
                {
                    yield return null;
                    if (body == null || viewer == null || ActiveWorld.Current != world
                        || (body.transform.InverseTransformPoint(viewer.position) - viewerLocal).sqrMagnitude > range * range * 0.25f)
                    { _fieldBuild = null; yield break; }
                }
                if (u * u + v * v > range * range) continue;

                Vector3 probeLocal = viewerLocal + tangentA * u + tangentB * v;
                Vector3 radial = probeLocal.sqrMagnitude > 0.0001f ? probeLocal.normalized : localUp;
                if (!TryFindRadialGrassSurface(world, radial, out Vector3Int surfaceVoxel,
                        out Vector3 surfaceLocal, out Vector3 radialUpLocal, out byte surfaceMaterial))
                    continue;
                if (surfaceMaterial != (byte)MaterialId.Grass
                    && !(surfaceMaterial == (byte)MaterialId.Clay && ecology.SupportsLivestock)) continue;
                Vector3Int outward = surfaceVoxel + Vector3Int.RoundToInt(radialUpLocal);
                var above = world.GetVoxelWorld(outward);
                if (above.waterLevel > 0) continue;

                // Keep blades perpendicular to the planet's radial frame. Surface-net gradient
                // estimates can tilt wildly across a Cartesian chunk seam, making grass look
                // flat relative to world Y instead of wrapped around the sphere.

                uint hash = (uint)(surfaceVoxel.x * 73856093 ^ surfaceVoxel.y * 19349663 ^ surfaceVoxel.z * 83492791);
                var rng = new Unity.Mathematics.Random(math.max(1u, hash));
                // Broad body-local patches, not identical evenly spaced grass cards.
                float patch = Mathf.PerlinNoise(surfaceLocal.x * 0.045f + surfaceLocal.y * 0.019f,
                    surfaceLocal.z * 0.045f + 41f);
                int bladeCount = Mathf.Clamp(Mathf.RoundToInt(density * step * step
                    * Mathf.Lerp(0.55f, 1.15f, patch)), 1, 24);
                Vector3 tuftSurface = surfaceLocal;
                Vector3 tuftUp = radialUpLocal;
                if (!TryGroundTuft(world, ref tuftSurface, ref tuftUp)) continue;
                GetTangentBasis(tuftUp, out Vector3 tuftA, out Vector3 tuftB);
                // One terrain hit anchors a compact patch. Three leaves per matrix supply
                // coverage without repeated expensive density evaluations or collider probes.
                for (int blade = 0; blade < bladeCount; blade++)
                {
                    Vector3 anchor = tuftSurface - tuftUp * 0.02f
                        + tuftA * rng.NextFloat(-0.65f, 0.65f) + tuftB * rng.NextFloat(-0.65f, 0.65f);
                    Quaternion rotation = Quaternion.AngleAxis(rng.NextFloat(0f, 360f), tuftUp)
                        * Quaternion.FromToRotation(Vector3.up, tuftUp);
                    float height = bladeHeight * Mathf.Lerp(0.72f, 1.18f, patch)
                        * (1f + rng.NextFloat(-heightVariance, heightVariance));
                    candidates.Add(Matrix4x4.TRS(anchor, rotation,
                        new Vector3(bladeWidth * rng.NextFloat(0.7f, 1.25f), height, height)));
                }
            }

            DisposeField();
            _fieldBuild = null;
            _nextStreamingRefresh = Time.unscaledTime + 4f;
            _instanceCount = candidates.Count;
            Debug.Log($"[Grass] blades={_instanceCount} density={density:0.00}/m2 tier={GraphicsPreset.Current} range={range:0}m");
            if (_instanceCount == 0) yield break;
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

        private bool TryGroundTuft(IVoxelWorld world, ref Vector3 localSurface, ref Vector3 localUp)
        {
            if (!(world is SphereWorld sphere)) return true;
            Vector3 root = body.transform.TransformPoint(localSurface);
            Vector3 up = body.transform.TransformDirection(localUp).normalized;
            Ray ray = new Ray(root + up * 5f, -up);
            // Resolve the local chunk across a radial boundary; at most one raycast per cell.
            for (int offset = 2; offset >= -2; offset--)
            {
                Vector3 sample = localSurface + localUp * offset;
                Vector3Int coord = Vector3Int.FloorToInt(sample /
                    (VoxelConstants.CHUNK_SIZE * VoxelConstants.VOXEL_SIZE));
                if (!sphere.TryGetChunk(coord, out Chunk chunk) || chunk == null
                    || chunk.meshCollider == null || !chunk.meshCollider.enabled
                    || chunk.meshCollider.sharedMesh == null) continue;
                if (!chunk.meshCollider.Raycast(ray, out RaycastHit hit, 10f)) return false;
                if (Vector3.Dot(hit.normal, up) < 0.65f) return false;
                localSurface = body.transform.InverseTransformPoint(hit.point);
                localUp = body.transform.InverseTransformDirection(hit.normal).normalized;
                return localSurface.magnitude > sphere.SeaLevel + 0.15f;
            }
            return false;
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
            if (qualityDensityMul == null || idx < 0 || idx >= qualityDensityMul.Length) return 1f;
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
            // Three curved leaves per instance: a readable tuft from every direction,
            // without tripling matrices or draw calls. Root width uses X; lean uses Z.
            var tuftVertices = new List<Vector3>(21);
            var tuftUvs = new List<Vector2>(21);
            var tuftTriangles = new List<int>(45);
            for (int leaf = 0; leaf < 3; leaf++)
            {
                float angle = leaf * Mathf.PI * 2f / 3f;
                float height = leaf == 0 ? 1f : leaf == 1 ? 0.78f : 0.9f;
                for (int vertex = 0; vertex < verts.Length; vertex++)
                {
                    Vector3 v = verts[vertex];
                    // Keep basal spread small; most of the volume comes from leaf curvature.
                    float bend = v.y * v.y * 0.12f;
                    tuftVertices.Add(new Vector3(v.x + Mathf.Cos(angle) * bend * 4f,
                        v.y * height, v.z * Mathf.Sin(angle)));
                    tuftUvs.Add(uvs[vertex]);
                }
                foreach (int index in tris) tuftTriangles.Add(leaf * verts.Length + index);
            }
            mesh.SetVertices(tuftVertices);
            mesh.SetUVs(0, tuftUvs);
            mesh.SetTriangles(tuftTriangles, 0);
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
            if (shader == null) return null;
            var mat = new Material(shader);
            mat.name = "Mat_Grass_Runtime";
            mat.enableInstancing = true;
            return mat;
        }
    }
}
