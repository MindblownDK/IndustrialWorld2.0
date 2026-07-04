using System.Reflection;
using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Crest Voxel Ocean Controller v3.20
    /// Binds Crest's URP OceanRenderer to procedural voxel water with seamless
    /// planet-surface alignment, shallow water depth feeding, and block foam.
    /// 
    /// Replaces the old voxel mesh water surface to eliminate gaps and draw call spam.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class CrestVoxelWaterBinder : MonoBehaviour
    {
        [Header("Viewpoint / Tracking")]
        public Transform viewpoint;
        public float waterHeightOffset = 0.08f;
        public bool alignToPlanetSurface = true;
        public bool followNearestProceduralWater = true;

        [Header("Search")]
        public float waterSearchRadius = 768f;
        public float waterSearchSpacing = 24f;
        [Tooltip("How often to re-scan for water (seconds). Reduces CPU spikes.")]
        [Range(0.05f, 1.0f)] public float scanInterval = 0.15f;

        [Header("Planet Patch")]
        [Tooltip("Smoothly lerp ocean transform to avoid popping.")]
        public bool smoothFollow = true;
        [Range(1f, 20f)] public float followSmoothing = 8f;
        [Tooltip("Keep Crest active even if no voxel water found – prevents flicker over oceans.")]
        public bool forceOceanAlwaysOn = false;

        [Header("Crest Mode v3.20.2")]
        [Tooltip("Disable Crest OceanRenderer plane – we use Crest material on voxel water mesh instead.")]
        public bool disableCrestOceanPlane = true;
        [Tooltip("Bridge Crest material to voxel water mesh renderer.")]
        public bool bridgeCrestMaterialToVoxelMesh = true;

        [Header("Crest Material Tuning")]
        public bool autoConfigureCrestMaterial = true;
        public Color shallowColor = new Color(0.18f, 0.72f, 0.88f, 0.92f);
        public Color deepColor = new Color(0.008f, 0.14f, 0.34f, 1f);
        public Color scatterColor = new Color(0.12f, 0.58f, 0.52f, 1f);

        [Header("Depth Debug (read-only)")]
        [SerializeField] private bool proceduralWaterFound;
        [SerializeField] private float sampledWaterDepth;
        [SerializeField] private float sampledSurfaceOffset;
        [SerializeField] private Vector3 currentOceanUp = Vector3.up;

        // Crest reflection caches
        private Component _oceanRenderer;
        private Behaviour _oceanBehaviour;
        private object _oceanRendererInstance;
        private FieldInfo _viewpointField;
        private PropertyInfo _seaLevelProperty;
        private FieldInfo _seaLevelField;
        private Transform _cachedViewpoint;
        private Renderer[] _renderers;

        // Smoothing
        private Vector3 _targetPos;
        private Quaternion _targetRot = Quaternion.identity;
        private float _nextScanTime;
        private Vector3 _lastWaterPosition;
        private bool _hasLastWaterPos;

        // Performance
        private int _frameSkip = 0;

        private void Awake()
        {
            CacheOcean();
            ApplyCrestMaterialTuning();
            // v3.20.2 – Bridge Crest material to voxel mesh – voxel mesh IS visual
            WaterMeshBuilder.RenderingEnabled = true;
            TryBridgeMaterialToVoxel();
        }

        private void OnEnable()
        {
            CacheOcean();
            ApplyCrestMaterialTuning();
            WaterMeshBuilder.RenderingEnabled = true;
            TryBridgeMaterialToVoxel();
            _nextScanTime = 0f;
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
                ApplyCrestMaterialTuning();
        }

        private void LateUpdate()
        {
            // Throttle to every other frame for CPU savings
            _frameSkip = (_frameSkip + 1) & 1;
            if (_frameSkip == 1 && smoothFollow) { ApplySmoothFollow(); return; }

            var world = ActiveWorld.Current;
            var view = ResolveViewpoint();
            if (world == null || view == null) return;

            CacheOcean();
            PushViewpointToCrest(view);

            bool doScan = Time.unscaledTime >= _nextScanTime;
            if (doScan)
            {
                _nextScanTime = Time.unscaledTime + scanInterval;

                proceduralWaterFound = VoxelWaterDepthSampler.TryFindNearbyWater(
                    view.position,
                    waterSearchRadius,
                    waterSearchSpacing,
                    out Vector3 waterPosition,
                    out float depth,
                    out float surface);

                sampledWaterDepth = proceduralWaterFound ? depth : 32f;
                sampledSurfaceOffset = proceduralWaterFound ? surface : 0f;

                if (proceduralWaterFound)
                {
                    _lastWaterPosition = waterPosition;
                    _hasLastWaterPos = true;
                }
            }

            bool shouldRender = forceOceanAlwaysOn || proceduralWaterFound || _hasLastWaterPos;
            SetCrestVisualActive(shouldRender);

            if (!shouldRender) return;

            Vector3 waterPos = _hasLastWaterPos ? _lastWaterPosition : view.position;

            if (PlanetWaterUtility.IsPlanetWorld && alignToPlanetSurface)
                UpdatePlanetPatchTarget(world, waterPos);
            else
                UpdateFlatPatchTarget(waterPos, sampledSurfaceOffset);

            ApplySmoothFollow();

            // Feed depth globals for Crest shallow water
            Shader.SetGlobalFloat("_VoxelWaterDepth", sampledWaterDepth);
            Shader.SetGlobalVector("_VoxelOceanUp", currentOceanUp);
            Shader.SetGlobalFloat("_VoxelCrestSeaLevel", world.SeaLevel * VoxelConstants.VOXEL_SIZE);
        }

        private void ApplySmoothFollow()
        {
            if (!smoothFollow)
            {
                transform.SetPositionAndRotation(_targetPos, _targetRot);
                return;
            }

            transform.position = Vector3.Lerp(transform.position, _targetPos, Time.deltaTime * followSmoothing);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, Time.deltaTime * followSmoothing);
        }

        private void PushViewpointToCrest(Transform view)
        {
            if (_oceanRenderer == null) return;
            try
            {
                if (_viewpointField != null)
                    _viewpointField.SetValue(_oceanRendererInstance ?? _oceanRenderer, view);

                // Also try sea level override if present
                if (_seaLevelProperty != null && _seaLevelProperty.CanWrite)
                    _seaLevelProperty.SetValue(_oceanRendererInstance ?? _oceanRenderer, transform.position.y);
                else if (_seaLevelField != null)
                    _seaLevelField.SetValue(_oceanRendererInstance ?? _oceanRenderer, transform.position.y);
            }
            catch { /* reflection safe */ }
        }

        private void CacheOcean()
        {
            if (_oceanRenderer != null) return;

            var oceanType = System.Type.GetType("Crest.OceanRenderer, Crest");
            if (oceanType != null)
            {
                _oceanRenderer = GetComponent(oceanType) ?? FindObjectOfType(oceanType) as Component;
                if (_oceanRenderer != null)
                {
                    _oceanBehaviour = _oceanRenderer as Behaviour;
                    _oceanRendererInstance = _oceanRenderer;

                    _viewpointField = oceanType.GetField("Viewpoint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                   ?? oceanType.GetField("_viewpoint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    _seaLevelProperty = oceanType.GetProperty("SeaLevel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _seaLevelField = oceanType.GetField("_seaLevel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                   ?? oceanType.GetField("SeaLevel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
            }

            if (_renderers == null || _renderers.Length == 0)
                _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        }

        private Transform ResolveViewpoint()
        {
            if (viewpoint != null) return viewpoint;
            if (_cachedViewpoint != null && _cachedViewpoint) return _cachedViewpoint;

            var world = ActiveWorld.Current;
            if (world != null && world.Viewer != null)
                return _cachedViewpoint = world.Viewer;

            var cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            _cachedViewpoint = cam != null ? cam.transform : null;
            return _cachedViewpoint;
        }

        private void SetCrestVisualActive(bool active)
        {
            // v3.20.2 – no ocean plane – we bridge Crest material to voxel mesh
            if (disableCrestOceanPlane) active = false;

            if (_oceanBehaviour != null && _oceanBehaviour.enabled != active)
                _oceanBehaviour.enabled = active;

            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r != null && r.enabled != active)
                    r.enabled = active;
            }

            // Bridge Crest material to voxel water mesh
            if (bridgeCrestMaterialToVoxelMesh && !active)
            {
                TryBridgeMaterialToVoxel();
            }
        }

        private void TryBridgeMaterialToVoxel()
        {
            if (_oceanRenderer == null) return;
            try
            {
                var oceanType = _oceanRenderer.GetType();
                var matProp = oceanType.GetProperty("OceanMaterial", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                Material crestMat = matProp != null ? matProp.GetValue(_oceanRenderer) as Material : null;
                if (crestMat == null)
                {
                    var matField = oceanType.GetField("_material", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    crestMat = matField != null ? matField.GetValue(_oceanRenderer) as Material : null;
                }
                if (crestMat != null)
                {
                    WaterMeshBuilder.SetMaterialOverrides(crestMat, null);
                    WaterMeshBuilder.RenderingEnabled = true;
                }
            }
            catch { }
        }

        private void UpdateFlatPatchTarget(Vector3 waterPosition, float surfaceHeight)
        {
            float seaY = surfaceHeight + waterHeightOffset;
            if (ActiveWorld.Current != null && !followNearestProceduralWater)
                seaY = ActiveWorld.Current.SeaLevel * VoxelConstants.VOXEL_SIZE + waterHeightOffset;

            _targetPos = new Vector3(waterPosition.x, seaY, waterPosition.z);
            _targetRot = Quaternion.identity;
            currentOceanUp = Vector3.up;
        }

        private void UpdatePlanetPatchTarget(IVoxelWorld world, Vector3 waterPosition)
        {
            Vector3 up = PlanetWaterUtility.WorldUp(waterPosition);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();
            currentOceanUp = up;

            float seaRadius = world.SeaLevel * VoxelConstants.VOXEL_SIZE + waterHeightOffset;
            Vector3 bodyCenter = Vector3.zero;
            if (world is VoxelEngine.Cosmos.SphereWorld sphere && sphere.body != null)
                bodyCenter = sphere.body.transform.position;

            _targetPos = bodyCenter + up * seaRadius;
            _targetRot = Quaternion.FromToRotation(Vector3.up, up);
        }

        private void ApplyCrestMaterialTuning()
        {
            if (!autoConfigureCrestMaterial) return;
            if (_oceanRenderer == null) CacheOcean();
            if (_oceanRenderer == null) return;

            try
            {
                var oceanType = _oceanRenderer.GetType();
                var matField = oceanType.GetField("_material", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var matProp = oceanType.GetProperty("OceanMaterial", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Material mat = null;
                if (matProp != null && matProp.CanRead) mat = matProp.GetValue(_oceanRenderer) as Material;
                if (mat == null && matField != null) mat = matField.GetValue(_oceanRenderer) as Material;
                if (mat == null) return;

                // Crest URP ocean material properties – safe checks
                if (mat.HasProperty("_SubSurfaceBase")) mat.SetColor("_SubSurfaceBase", scatterColor);
                if (mat.HasProperty("_SubSurfaceColour")) mat.SetColor("_SubSurfaceColour", scatterColor);
                if (mat.HasProperty("_SubSurfaceShallowCol")) mat.SetColor("_SubSurfaceShallowCol", shallowColor);
                if (mat.HasProperty("_Diffuse")) mat.SetColor("_Diffuse", deepColor);
                if (mat.HasProperty("_DiffuseGrazing")) mat.SetColor("_DiffuseGrazing", shallowColor);
                if (mat.HasProperty("_SubSurfaceDepthMax")) mat.SetFloat("_SubSurfaceDepthMax", 8f);
                if (mat.HasProperty("_SubSurfaceDepthPower")) mat.SetFloat("_SubSurfaceDepthPower", 2.2f);
                if (mat.HasProperty("_DepthFogDensity")) mat.SetVector("_DepthFogDensity", new Vector4(0.12f, 0.08f, 0.06f, 1f));
                if (mat.HasProperty("_NormalsStrength")) mat.SetFloat("_NormalsStrength", 0.65f);
                if (mat.HasProperty("_NormalsScale")) mat.SetFloat("_NormalsScale", 28f);
                if (mat.HasProperty("_FoamWhiteFoamCover")) mat.SetFloat("_FoamWhiteFoamCover", 0.55f);
                if (mat.HasProperty("_ShorelineFoamMinDepth")) mat.SetFloat("_ShorelineFoamMinDepth", 1.8f);
                if (mat.HasProperty("_Transparency")) mat.SetFloat("_Transparency", 0.92f);
            }
            catch { }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = proceduralWaterFound ? new Color(0.1f, 0.7f, 1f, 0.45f) : new Color(1f, 0.3f, 0.2f, 0.25f);
            Gizmos.DrawWireSphere(_targetPos, 4f);
            Gizmos.DrawRay(_targetPos, currentOceanUp * 8f);
        }
#endif
    }
}
