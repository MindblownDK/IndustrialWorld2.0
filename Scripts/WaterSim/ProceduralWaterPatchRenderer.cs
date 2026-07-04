using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Finite procedural water visual bridge. It renders water only where the
    /// voxel world reports generated ocean/lake water near the viewer. The mesh
    /// components live on an internal child object so old scene objects that only
    /// had this script attached cannot throw MissingComponentException on load.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ProceduralWaterPatchRenderer : MonoBehaviour
    {
        [Header("Patch Sampling")]
        public Transform viewpoint;
        [UnityEngine.Range(64f, 2048f)] public float searchRadius = 512f;
        [UnityEngine.Range(4f, 64f)] public float tileSize = 16f;
        [UnityEngine.Range(8, 128)] public int maxTilesPerAxis = 64;
        [UnityEngine.Range(0.1f, 3f)] public float rebuildInterval = 0.35f;
        public float waterHeightOffset = 0.04f;

        [Tooltip("Keep the last successful water mesh visible if a transient chunk-streaming frame cannot sample water.")]
        public bool keepLastValidMesh = true;

        [Header("Shallow/Deep")]
        public float shallowDepth = 2.5f;
        public float deepDepth = 28f;

        [Header("Surface Motion")]
        [Tooltip("How strongly Crest flow splines/current data influence water normals and foam.")]
        [UnityEngine.Range(0f, 2f)] public float flowVisualStrength = 1f;

        [Header("Material")]
        public Material waterMaterial;

        private GameObject _meshObject;
        private Mesh _mesh;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Transform _cachedViewpoint;
        private float _nextRebuild;
        private bool _hasValidMesh;

        private readonly List<Vector3> _vertices = new(16384);
        private readonly List<Vector3> _normals = new(16384);
        private readonly List<Vector2> _uvs = new(16384);
        private readonly List<Vector2> _uv2s = new(16384);
        private readonly List<Color> _colors = new(16384);
        private readonly List<int> _triangles = new(24576);

        private struct Sample
        {
            public Vector3 position;
            public Vector3 normal;
            public float depth;
            public Vector2 flow;
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

        private void Reset()
        {
            EnsureRuntimeObjects();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying) EnsureRuntimeObjects();
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextRebuild) return;
            _nextRebuild = Time.unscaledTime + Mathf.Max(0.05f, rebuildInterval);
            Rebuild();
        }

        private void EnsureRuntimeObjects()
        {
            // Keep the MeshFilter/MeshRenderer on the SAME GameObject. This is
            // more Unity-inspector friendly and prevents old scenes showing a
            // root renderer with a missing mesh.
            _filter = GetComponent<MeshFilter>();
            if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();

            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "ProceduralWaterPatchMesh" };
                _mesh.indexFormat = IndexFormat.UInt32;
            }

            _filter.sharedMesh = _mesh;

            if (waterMaterial == null)
                waterMaterial = CreateDefaultMaterial();

            _renderer.sharedMaterial = waterMaterial;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = true;
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
            if (mat.HasProperty("_ShallowColor")) mat.SetColor("_ShallowColor", new Color(0.28f, 0.78f, 0.95f, 0.82f));
            if (mat.HasProperty("_DeepColor")) mat.SetColor("_DeepColor", new Color(0.015f, 0.16f, 0.42f, 0.94f));
            if (mat.HasProperty("_FoamColor")) mat.SetColor("_FoamColor", new Color(0.92f, 0.98f, 1f, 0.85f));
            if (mat.HasProperty("_DeepWaveAmplitude")) mat.SetFloat("_DeepWaveAmplitude", 0.42f);
            if (mat.HasProperty("_SecondaryWaveAmplitude")) mat.SetFloat("_SecondaryWaveAmplitude", 0.22f);
            if (mat.HasProperty("_NormalScale")) mat.SetFloat("_NormalScale", 2.0f);
            if (mat.HasProperty("_RefractionStrength")) mat.SetFloat("_RefractionStrength", 0.012f);
            if (mat.HasProperty("_DepthFade")) mat.SetFloat("_DepthFade", 6.0f);
            return mat;
        }

        private bool TryGetFallbackSeaCenter(IVoxelWorld world, Vector3 viewerPosition, out Vector3 waterCenter)
        {
            waterCenter = viewerPosition;
            if (world == null) return false;

            if (PlanetWaterUtility.IsPlanetWorld)
            {
                Vector3 bodyCenter = Vector3.zero;
                if (world is VoxelEngine.Cosmos.SphereWorld sphere && sphere.body != null)
                    bodyCenter = sphere.body.transform.position;

                Vector3 up = PlanetWaterUtility.WorldUp(viewerPosition);
                if (up.sqrMagnitude < 0.0001f)
                {
                    Vector3 fromCenter = viewerPosition - bodyCenter;
                    up = fromCenter.sqrMagnitude > 0.0001f ? fromCenter.normalized : Vector3.up;
                }
                up.Normalize();

                float seaRadius = world.SeaLevel * VoxelConstants.VOXEL_SIZE;
                waterCenter = bodyCenter + up * seaRadius;
                return true;
            }

            waterCenter = new Vector3(viewerPosition.x, world.SeaLevel * VoxelConstants.VOXEL_SIZE, viewerPosition.z);
            return true;
        }

        private void Rebuild()
        {
            EnsureRuntimeObjects();

            var world = ActiveWorld.Current;
            var view = ResolveViewpoint();
            if (world == null || view == null)
            {
                ClearMesh();
                return;
            }

            if (!VoxelWaterDepthSampler.TryFindNearbyWater(view.position, searchRadius, Mathf.Max(tileSize, 8f), out var waterCenter, out _, out _))
            {
                if (!TryGetFallbackSeaCenter(world, view.position, out waterCenter))
                {
                    ClearMesh();
                    return;
                }
            }

            Vector3 up = PlanetWaterUtility.IsPlanetWorld ? PlanetWaterUtility.WorldUp(waterCenter) : Vector3.up;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            Vector3 tangentA = Vector3.Cross(up, Vector3.forward);
            if (tangentA.sqrMagnitude < 0.0001f) tangentA = Vector3.Cross(up, Vector3.right);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;

            int halfTiles = Mathf.Clamp(Mathf.CeilToInt(searchRadius / Mathf.Max(tileSize, 1f)), 2, maxTilesPerAxis / 2);
            int grid = halfTiles * 2;
            int vertCountPerAxis = grid + 1;
            int[,] indices = new int[vertCountPerAxis, vertCountPerAxis];
            for (int z = 0; z < vertCountPerAxis; z++)
            for (int x = 0; x < vertCountPerAxis; x++)
                indices[x, z] = -1;

            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _uv2s.Clear();
            _colors.Clear();
            _triangles.Clear();

            for (int z = 0; z < vertCountPerAxis; z++)
            for (int x = 0; x < vertCountPerAxis; x++)
            {
                float ox = (x - halfTiles) * tileSize;
                float oz = (z - halfTiles) * tileSize;
                Vector3 samplePos = waterCenter + tangentA * ox + tangentB * oz;
                if (!TrySample(samplePos, out var sample)) continue;
                indices[x, z] = AddSharedVertex(sample);
            }

            for (int z = 0; z < grid; z++)
            for (int x = 0; x < grid; x++)
            {
                int i00 = indices[x, z];
                int i10 = indices[x + 1, z];
                int i11 = indices[x + 1, z + 1];
                int i01 = indices[x, z + 1];
                if (i00 < 0 || i10 < 0 || i11 < 0 || i01 < 0) continue;
                _triangles.Add(i00); _triangles.Add(i11); _triangles.Add(i10);
                _triangles.Add(i00); _triangles.Add(i01); _triangles.Add(i11);
            }

            if (_vertices.Count == 0 || _triangles.Count == 0)
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
            _renderer.enabled = true;
            _hasValidMesh = true;
        }

        private int AddSharedVertex(Sample s)
        {
            int index = _vertices.Count;
            _vertices.Add(transform.InverseTransformPoint(s.position));
            _normals.Add(transform.InverseTransformDirection(s.normal).normalized);
            _uvs.Add(new Vector2(s.position.x * 0.08f + s.position.y * 0.013f, s.position.z * 0.08f));
            _uv2s.Add(s.flow);
            float shallow = Mathf.InverseLerp(deepDepth, shallowDepth, s.depth);
            float depth01 = Mathf.InverseLerp(shallowDepth, deepDepth, s.depth);
            _colors.Add(new Color(Mathf.Lerp(1f, 0.35f, shallow), 1f, depth01, 1f));
            return index;
        }

        private bool TrySample(Vector3 samplePosition, out Sample sample)
        {
            sample = default;
            bool hasWater = VoxelWaterDepthSampler.TrySampleDepth(samplePosition, out float depth, out float surface) ||
                            VoxelWaterDepthSampler.TrySampleSeaSurface(samplePosition, out depth, out surface);

            Vector3 up = PlanetWaterUtility.IsPlanetWorld ? PlanetWaterUtility.WorldUp(samplePosition) : Vector3.up;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            if (!hasWater)
            {
                var world = ActiveWorld.Current;
                if (world == null) return false;
                surface = PlanetWaterUtility.IsPlanetWorld ? 0f : world.SeaLevel * VoxelConstants.VOXEL_SIZE;
                depth = 32f;
            }

            Vector3 position = PlanetWaterUtility.IsPlanetWorld
                ? samplePosition + up * (surface + waterHeightOffset)
                : new Vector3(samplePosition.x, surface + waterHeightOffset, samplePosition.z);

            sample.position = position;
            sample.normal = up;
            sample.depth = depth;
            sample.flow = SampleFlow(position, up);
            return true;
        }

        private Vector2 SampleFlow(Vector3 position, Vector3 up)
        {
            if (!CrestFlowSampler.TrySampleFlow(position, out var flow3)) return Vector2.zero;
            Vector3 flow = new Vector3(flow3.x, flow3.y, flow3.z);
            if (flow.sqrMagnitude < 0.0001f) return Vector2.zero;

            Vector3 tangentA = Vector3.Cross(up, Vector3.forward);
            if (tangentA.sqrMagnitude < 0.0001f) tangentA = Vector3.Cross(up, Vector3.right);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;
            return new Vector2(Vector3.Dot(flow, tangentA), Vector3.Dot(flow, tangentB)) * flowVisualStrength;
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
            // Never disable the MeshRenderer here. Disabling it made the scene look
            // like water was broken and prevented manual inspection in play mode.
            if (keepLastValidMesh && _hasValidMesh)
            {
                if (_renderer != null) _renderer.enabled = true;
                return;
            }

            if (_mesh != null) _mesh.Clear();
            _hasValidMesh = false;
            if (_renderer != null) _renderer.enabled = true;
        }
    }
}
