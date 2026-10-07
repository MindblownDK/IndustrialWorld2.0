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

        private sealed class Patch
        {
            public Chunk chunk;
            public int epoch, meshRevision = -1;
            public readonly List<Matrix4x4> roots = new();
            public NativeArray<Matrix4x4> worldMatrices;
            public Matrix4x4 bodyMatrix;
            public bool dirty = true, hasEditBounds;
            public Bounds editBounds;
            public void Dispose() { if (worldMatrices.IsCreated) worldMatrices.Dispose(); }
        }
        private readonly Dictionary<Vector3Int, Patch> _patches = new();
        private readonly List<Vector3Int> _retire = new();
        private SphereWorld _world;
        private Coroutine _fieldBuild;
        private float _nextScan, _nextEcologySample;
        private float _lastEcologyMultiplier = 1f, _lastQualityMultiplier = -1f;
        private bool _supportsClay, _ownsBladeMesh;
        private Color _healthyBaseColor, _healthyTipColor;

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

        private void OnDisable() => ResetPatches();
        private void OnDestroy()
        {
            ResetPatches();
            if (grassMaterial != null) Destroy(grassMaterial);
            if (_ownsBladeMesh && grassBladeMesh != null) Destroy(grassBladeMesh);
        }

        private void ResetPatches()
        {
            if (_fieldBuild != null) StopCoroutine(_fieldBuild);
            _fieldBuild = null;
            if (_world != null) _world.TerrainEdited -= OnTerrainEdited;
            _world = null;
            foreach (Patch patch in _patches.Values) patch.Dispose();
            _patches.Clear();
        }

        private void OnTerrainEdited(Bounds bounds)
        {
            // Keep every unaffected anchor exactly where it was. Only roots in the edit halo
            // disappear; their patch rebuild waits for the new terrain mesh to be applied.
            foreach (Patch patch in _patches.Values)
            {
                var chunkBounds = new Bounds(patch.chunk.WorldOrigin + Vector3.one * 16f, Vector3.one * 36f);
                if (!chunkBounds.Intersects(bounds)) continue;
                if (patch.hasEditBounds) patch.editBounds.Encapsulate(bounds);
                else { patch.editBounds = bounds; patch.hasEditBounds = true; }
                patch.dirty = true;
                patch.roots.RemoveAll(m => bounds.Contains(m.GetColumn(3)));
                Upload(patch);
            }
        }

        private void Update()
        {
            var current = ActiveWorld.Current as SphereWorld;
            if (current != _world) { ResetPatches(); _world = current; if (_world != null) _world.TerrainEdited += OnTerrainEdited; }
            if (_world == null || body == null || viewer == null || grassBladeMesh == null || grassMaterial == null) return;
            if (body != _world.body) { ResetPatches(); return; }
            if (VoxelEngine.GridSystem.AtmosphereManager.IsInSpace(viewer.position))
            { ResetPatches(); return; }
            if (Time.unscaledTime >= _nextEcologySample)
            {
                _nextEcologySample = Time.unscaledTime + 2.5f;
                EcologyReading ecology = EcologyPressure.Sample(viewer.position);
                ApplyEcologyColour(ecology);
                float quality = GetQualityDensityMul();
                if (Mathf.Abs(_lastEcologyMultiplier - ecology.FloraSpawnMultiplier) > 0.08f
                    || !Mathf.Approximately(_lastQualityMultiplier, quality))
                    foreach (Patch patch in _patches.Values) patch.dirty = true;
                _lastEcologyMultiplier = ecology.FloraSpawnMultiplier;
                _lastQualityMultiplier = quality;
                _supportsClay = ecology.SupportsLivestock;
            }
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                ScanPatches();
            }
            if (_fieldBuild == null)
            {
                Patch nearest = null; float best = float.PositiveInfinity;
                Vector3 viewerLocal = body.transform.InverseTransformPoint(viewer.position);
                foreach (Patch patch in _patches.Values)
                {
                    if (!patch.dirty || !Ready(patch)) continue;
                    float d = (patch.chunk.WorldOrigin + Vector3.one * 16f - viewerLocal).sqrMagnitude;
                    if (d < best) { best = d; nearest = patch; }
                }
                if (nearest != null) _fieldBuild = StartCoroutine(BuildPatch(nearest));
            }
            Matrix4x4 bodyMatrix = body.transform.localToWorldMatrix;
            foreach (Patch patch in _patches.Values)
            {
                if (!patch.worldMatrices.IsCreated) continue;
                if (patch.bodyMatrix != bodyMatrix) Upload(patch);
                for (int i=0;i<patch.worldMatrices.Length;i+=InstanceBatchSize)
                    Graphics.RenderMeshInstanced(_renderParams, grassBladeMesh, 0,
                        patch.worldMatrices.GetSubArray(i, Mathf.Min(InstanceBatchSize, patch.worldMatrices.Length-i)));
            }
            var wind = WindField.Instance;
            if (wind != null)
            {
                grassMaterial.SetVector("_WindDir", wind.Direction);
                grassMaterial.SetFloat("_WindStrength", Mathf.Clamp01(wind.strength * 0.4f));
            }
        }

        private bool Ready(Patch patch) => _world != null && patch.chunk.streamEpoch == patch.epoch
            && patch.chunk.isGenerated && patch.chunk.meshFilter != null
            && patch.chunk.meshFilter.sharedMesh != null && !_world.IsTerrainMeshPending(patch.chunk);

        private void ScanPatches()
        {
            Vector3 local = body.transform.InverseTransformPoint(viewer.position);
            Vector3Int center = _world.WorldToChunk(viewer.position);
            int r = Mathf.CeilToInt(range / 32f) + 1;
            _retire.Clear();
            foreach (var pair in _patches)
                if (!_world.TryGetChunk(pair.Key, out Chunk c) || c != pair.Value.chunk
                    || c.streamEpoch != pair.Value.epoch || (c.WorldOrigin + Vector3.one*16f-local).sqrMagnitude > (range+40f)*(range+40f))
                    _retire.Add(pair.Key);
            foreach (Vector3Int key in _retire) { _patches[key].Dispose(); _patches.Remove(key); }
            for (int z=-r;z<=r;z++) for(int y=-r;y<=r;y++) for(int x=-r;x<=r;x++)
            {
                Vector3Int coord = center + new Vector3Int(x,y,z);
                if (!_world.TryGetChunk(coord, out Chunk c) || !c.isGenerated || c.meshFilter == null
                    || c.meshFilter.sharedMesh == null || c.meshFilter.sharedMesh.vertexCount < 3) continue;
                if ((c.WorldOrigin + Vector3.one*16f-local).sqrMagnitude > (range+28f)*(range+28f)) continue;
                if (!_patches.TryGetValue(coord, out Patch patch))
                    _patches.Add(coord, patch = new Patch { chunk=c, epoch=c.streamEpoch });
                if (patch.meshRevision != c.terrainMeshRevision) patch.dirty = true;
            }
        }

        private void Upload(Patch patch)
        {
            patch.Dispose();
            patch.bodyMatrix = body.transform.localToWorldMatrix;
            if (patch.roots.Count == 0) return;
            patch.worldMatrices = new NativeArray<Matrix4x4>(patch.roots.Count, Allocator.Persistent);
            for(int i=0;i<patch.roots.Count;i++) patch.worldMatrices[i] = patch.bodyMatrix * patch.roots[i];
        }

        private IEnumerator BuildPatch(Patch patch)
        {
            yield return null;
            if (!Ready(patch)) { _fieldBuild = null; yield break; }
            int revision = patch.chunk.terrainMeshRevision, editVersion = patch.chunk.terrainRevision;
            Mesh mesh = patch.chunk.meshFilter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Color32[] colors = mesh.colors32;
            var roots = new List<Matrix4x4>();
            bool partial = patch.hasEditBounds;
            Bounds editBounds = patch.editBounds;
            if (partial) roots.AddRange(patch.roots);
            Vector3 origin = patch.chunk.WorldOrigin;
            double start = Time.realtimeSinceStartupAsDouble;
            float density = Mathf.Max(0, baseDensity * GetQualityDensityMul() * _lastEcologyMultiplier);
            for (int t=0;t<triangles.Length;t+=3)
            {
                if (Time.realtimeSinceStartupAsDouble-start >= 0.002)
                {
                    yield return null;
                    start = Time.realtimeSinceStartupAsDouble;
                    if (!Ready(patch) || revision != patch.chunk.terrainMeshRevision || editVersion != patch.chunk.terrainRevision
                        || !_patches.TryGetValue(patch.chunk.coord, out Patch live) || live != patch)
                    { _fieldBuild = null; yield break; }
                }
                int ia=triangles[t], ib=triangles[t+1], ic=triangles[t+2];
                if (colors.Length != vertices.Length) continue;
                int suitable = (IsSoil(colors[ia].a)?1:0) + (IsSoil(colors[ib].a)?1:0) + (IsSoil(colors[ic].a)?1:0);
                if (suitable < 2) continue;
                Vector3 a=vertices[ia]+origin, b=vertices[ib]+origin, c=vertices[ic]+origin;
                Vector3 cross = Vector3.Cross(b-a,c-a);
                float area = cross.magnitude * 0.5f;
                if (area < 0.0001f) continue;
                Vector3 radial = ((a+b+c)/3f).normalized;
                Vector3 normal = cross.normalized;
                if (Vector3.Dot(normal,radial) < 0.72f) continue;
                uint hash = (uint)(patch.chunk.coord.x*73856093 ^ patch.chunk.coord.y*19349663 ^ patch.chunk.coord.z*83492791 ^ (t+1)*104729);
                var rng = new Unity.Mathematics.Random(math.max(1u,hash));
                float expected = density * area;
                int count = Mathf.FloorToInt(expected) + (rng.NextFloat() < expected%1f ? 1 : 0);
                for (int j=0;j<count;j++)
                {
                    float u=Mathf.Sqrt(rng.NextFloat()), v=rng.NextFloat();
                    Vector3 root = a*(1-u)+b*(u*(1-v))+c*(u*v);
                    if (partial && !editBounds.Contains(root)) continue;
                    // Sample above the actual triangle, not below an analytic sphere. Round
                    // because density is stored at integer lattice points, not cell centres.
                    Vector3Int q = Vector3Int.RoundToInt((root+radial*0.6f)/VoxelConstants.VOXEL_SIZE);
                    if (!_world.TryGetVoxelReady(q,out Voxel above) || above.waterLevel > 0) continue;
                    Quaternion rotation = Quaternion.AngleAxis(rng.NextFloat(0,360),radial) * Quaternion.FromToRotation(Vector3.up,radial);
                    float height = bladeHeight * (1f+rng.NextFloat(-heightVariance,heightVariance));
                    float width = bladeWidth * rng.NextFloat(0.8f,1.2f);
                    roots.Add(Matrix4x4.TRS(root-radial*0.025f,rotation,new Vector3(width,height,width)));
                }
            }
            if (Ready(patch) && revision == patch.chunk.terrainMeshRevision && editVersion == patch.chunk.terrainRevision
                && _patches.TryGetValue(patch.chunk.coord,out Patch current) && current == patch)
            {
                patch.roots.Clear(); patch.roots.AddRange(roots);
                patch.meshRevision = revision; patch.dirty = false; patch.hasEditBounds = false;
                Upload(patch);
            }
            _fieldBuild = null;
        }

        private bool IsSoil(byte material) => material == (byte)MaterialId.Grass
            || (_supportsClay && material == (byte)MaterialId.Clay);

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
                    tuftVertices.Add(new Vector3(v.x * Mathf.Cos(angle) - (v.z + bend) * Mathf.Sin(angle),
                        v.y * height, v.x * Mathf.Sin(angle) + (v.z + bend) * Mathf.Cos(angle)));
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
