using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Finite procedural water visual bridge. It renders water patches only where
    /// voxel water exists near the viewer, using the same procedural water/depth
    /// truth as pumps and maritime systems. This is the bridge away from the
    /// infinite Crest sample plane and toward generated oceans/lakes on spherical worlds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralWaterPatchRenderer : MonoBehaviour
    {
        [Header("Patch Sampling")]
        public Transform viewpoint;
        [UnityEngine.Range(64f, 1024f)] public float searchRadius = 512f;
        [UnityEngine.Range(4f, 64f)] public float tileSize = 16f;
        [UnityEngine.Range(8, 96)] public int maxTilesPerAxis = 48;
        [UnityEngine.Range(0.1f, 3f)] public float rebuildInterval = 0.35f;
        public float waterHeightOffset = 0.03f;

        [Header("Shallow/Deep")]
        public float shallowDepth = 2.5f;
        public float deepDepth = 24f;

        [Header("Material")]
        public Material waterMaterial;

        private Mesh _mesh;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Transform _cachedViewpoint;
        private float _nextRebuild;

        private readonly List<Vector3> _vertices = new(8192);
        private readonly List<Vector3> _normals = new(8192);
        private readonly List<Vector2> _uvs = new(8192);
        private readonly List<Vector2> _uv2s = new(8192);
        private readonly List<Color> _colors = new(8192);
        private readonly List<int> _triangles = new(12288);

        private struct Sample
        {
            public bool water;
            public Vector3 position;
            public Vector3 normal;
            public float depth;
        }

        private void Awake()
        {
            EnsureRuntimeObjects();
        }

        private void OnEnable()
        {
            EnsureRuntimeObjects();
            _nextRebuild = 0f;
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextRebuild) return;
            _nextRebuild = Time.unscaledTime + rebuildInterval;
            Rebuild();
        }

        private void EnsureRuntimeObjects()
        {
            if (_filter == null)
                _filter = GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
            if (_renderer == null)
                _renderer = GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "ProceduralWaterPatchMesh" };
                _mesh.indexFormat = IndexFormat.UInt32;
                _filter.sharedMesh = _mesh;
            }
            if (waterMaterial == null)
                waterMaterial = CreateDefaultMaterial();
            _renderer.sharedMaterial = waterMaterial;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }

        private Material CreateDefaultMaterial()
        {
            var shader = Shader.Find("VoxelEngine/VoxelWaterURP")
                      ?? Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "ProceduralVoxelWater_Runtime" };
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_Cull", (int)CullMode.Off);
            mat.renderQueue = 3000;
            if (mat.HasProperty("_ShallowColor")) mat.SetColor("_ShallowColor", new Color(0.20f, 0.72f, 0.86f, 0.90f));
            if (mat.HasProperty("_DeepColor")) mat.SetColor("_DeepColor", new Color(0.01f, 0.075f, 0.24f, 0.98f));
            if (mat.HasProperty("_FoamColor")) mat.SetColor("_FoamColor", new Color(0.92f, 0.98f, 1f, 0.85f));
            if (mat.HasProperty("_DeepWaveAmplitude")) mat.SetFloat("_DeepWaveAmplitude", 0.55f);
            if (mat.HasProperty("_SecondaryWaveAmplitude")) mat.SetFloat("_SecondaryWaveAmplitude", 0.22f);
            if (mat.HasProperty("_NormalScale")) mat.SetFloat("_NormalScale", 2.0f);
            if (mat.HasProperty("_RefractionStrength")) mat.SetFloat("_RefractionStrength", 0.018f);
            if (mat.HasProperty("_DepthFade")) mat.SetFloat("_DepthFade", 6.0f);
            return mat;
        }

        private void Rebuild()
        {
            var world = ActiveWorld.Current;
            var view = ResolveViewpoint();
            if (world == null || view == null)
            {
                ClearMesh();
                return;
            }

            if (!VoxelWaterDepthSampler.TryFindNearbyWater(view.position, searchRadius, Mathf.Max(tileSize, 8f), out var waterCenter, out _, out _))
            {
                ClearMesh();
                return;
            }

            Vector3 up = PlanetWaterUtility.IsPlanetWorld ? PlanetWaterUtility.WorldUp(waterCenter) : Vector3.up;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            Vector3 tangentA = Vector3.Cross(up, Vector3.forward);
            if (tangentA.sqrMagnitude < 0.0001f) tangentA = Vector3.Cross(up, Vector3.right);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;

            int tiles = Mathf.Clamp(Mathf.CeilToInt(searchRadius / tileSize), 2, maxTilesPerAxis / 2);
            float halfStep = tileSize * 0.5f;

            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _uv2s.Clear();
            _colors.Clear();
            _triangles.Clear();

            for (int z = -tiles; z < tiles; z++)
            for (int x = -tiles; x < tiles; x++)
            {
                Vector3 c = waterCenter + tangentA * ((x + 0.5f) * tileSize) + tangentB * ((z + 0.5f) * tileSize);
                if (!TrySample(c, out var centerSample)) continue;

                Sample s00, s10, s11, s01;
                bool ok00 = TrySample(c + tangentA * -halfStep + tangentB * -halfStep, out s00);
                bool ok10 = TrySample(c + tangentA *  halfStep + tangentB * -halfStep, out s10);
                bool ok11 = TrySample(c + tangentA *  halfStep + tangentB *  halfStep, out s11);
                bool ok01 = TrySample(c + tangentA * -halfStep + tangentB *  halfStep, out s01);

                if (!ok00) s00 = centerSample;
                if (!ok10) s10 = centerSample;
                if (!ok11) s11 = centerSample;
                if (!ok01) s01 = centerSample;

                AddQuad(s00, s10, s11, s01);
            }

            if (_vertices.Count == 0)
            {
                ClearMesh();
                return;
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetNormals(_normals);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetUVs(1, _uv2s);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
            _filter.sharedMesh = _mesh;
            if (_renderer != null) _renderer.enabled = true;
        }

        private bool TrySample(Vector3 samplePosition, out Sample sample)
        {
            sample = default;
            if (!VoxelWaterDepthSampler.TrySampleDepth(samplePosition, out float depth, out float surface))
                return false;

            Vector3 up = PlanetWaterUtility.IsPlanetWorld ? PlanetWaterUtility.WorldUp(samplePosition) : Vector3.up;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            Vector3 position = PlanetWaterUtility.IsPlanetWorld
                ? up * (samplePosition.magnitude + surface + waterHeightOffset)
                : new Vector3(samplePosition.x, surface + waterHeightOffset, samplePosition.z);

            sample.water = true;
            sample.position = position;
            sample.normal = up;
            sample.depth = depth;
            return true;
        }

        private void AddQuad(Sample a, Sample b, Sample c, Sample d)
        {
            int i = _vertices.Count;
            AddVertex(a);
            AddVertex(b);
            AddVertex(c);
            AddVertex(d);
            _triangles.Add(i); _triangles.Add(i + 2); _triangles.Add(i + 1);
            _triangles.Add(i); _triangles.Add(i + 3); _triangles.Add(i + 2);
        }

        private void AddVertex(Sample s)
        {
            _vertices.Add(transform.InverseTransformPoint(s.position));
            _normals.Add(transform.InverseTransformDirection(s.normal).normalized);
            _uvs.Add(new Vector2(s.position.x * 0.08f + s.position.y * 0.013f, s.position.z * 0.08f));
            _uv2s.Add(Vector2.zero);
            float shallow = Mathf.InverseLerp(deepDepth, shallowDepth, s.depth);
            float depth01 = Mathf.InverseLerp(shallowDepth, deepDepth, s.depth);
            _colors.Add(new Color(Mathf.Lerp(1f, 0.35f, shallow), 1f, depth01, 1f));
        }

        private Transform ResolveViewpoint()
        {
            if (viewpoint != null) return viewpoint;
            if (_cachedViewpoint != null) return _cachedViewpoint;
            if (ActiveWorld.Current?.Viewer != null) return _cachedViewpoint = ActiveWorld.Current.Viewer;
            var cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            return _cachedViewpoint = cam != null ? cam.transform : null;
        }

        private void ClearMesh()
        {
            if (_mesh != null) _mesh.Clear();
            if (_renderer != null) _renderer.enabled = false;
        }
    }
}
