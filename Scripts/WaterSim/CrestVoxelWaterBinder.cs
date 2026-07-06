using System.Reflection;
using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Crest Voxel Ocean Controller v3.23.0 — Voxel Water Authoritative.
    ///
    /// Design (revised from v3.22.0 hybrid):
    ///  • Crest's OceanRenderer is ALIVE (kept for future wake-foam hooks)
    ///    but its infinite ocean tiles are HIDDEN — the user does not want
    ///    an infinite Crest plane.
    ///  • Voxel water renders everywhere (including at sea level) with the
    ///    stylized VoxelEngine/VoxelWaterURP shader (waves, foam, flow,
    ///    fresnel, depth fog).
    ///  • OceanRenderer's transform Y is still snapped to sea level so any
    ///    future Crest LOD sampling stays coherent.
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

        [Header("Crest Mode v3.23.0 – Voxel Water Authoritative")]
        [Tooltip("Hide Crest's built-in infinite ocean tiles. Default ON in v3.23.0 – user does not want an infinite Crest plane; voxel water is the visual.")]
        public bool hideCrestOceanTiles = true;
        [Tooltip("LEGACY – ignored since v3.22.0. Kept for scene-serialization compatibility.")]
        public bool bridgeCrestMaterialToVoxelMesh = false;

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
            HideCrestOceanTiles(hideCrestOceanTiles);
            WaterMeshBuilder.RenderingEnabled = true;
            PushWorldUpGlobal(); // v3.23.1 – valid from frame 0
        }

        private void OnEnable()
        {
            CacheOcean();
            ApplyCrestMaterialTuning();
            HideCrestOceanTiles(hideCrestOceanTiles);
            WaterMeshBuilder.RenderingEnabled = true;
            PushWorldUpGlobal();
            _nextScanTime = 0f;
        }

        /// <summary>
        /// v3.23.1 – Push the world-up vector that VoxelWaterURP samples.
        /// Called on Awake/OnEnable so the shader has valid data from the
        /// very first frame (before LateUpdate ever runs).
        /// </summary>
        private void PushWorldUpGlobal()
        {
            bool isPlanet = PlanetWaterUtility.IsPlanetWorld;
            Vector3 up = isPlanet ? currentOceanUp : Vector3.up;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            Shader.SetGlobalVector("_VoxelWaterWorldUp",
                new Vector4(up.x, up.y, up.z, isPlanet ? 1f : 0f));
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

            // v3.23.1 – feed VoxelWaterURP shader:
            //   xyz = world-up direction (planet radial or (0,1,0))
            //   w   = 1.0 for planet, 0.0 for flat world
            float isPlanet = PlanetWaterUtility.IsPlanetWorld ? 1.0f : 0.0f;
            Vector3 upDir = isPlanet > 0.5f ? currentOceanUp : Vector3.up;
            Shader.SetGlobalVector("_VoxelWaterWorldUp",
                new Vector4(upDir.x, upDir.y, upDir.z, isPlanet));
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

                // v3.12.0 – Crest's SeaLevel is derived from OceanRenderer.Root.position.y
                // (read-only property). Instead of trying to overwrite it, move the
                // OceanRenderer's own transform so its Root.y == our sea level. This
                // keeps every LodDataMgr sampler aligned with our voxel water plane.
                var world = ActiveWorld.Current;
                if (world != null && _oceanBehaviour != null)
                {
                    float seaY = world.SeaLevel * VoxelConstants.VOXEL_SIZE + waterHeightOffset;
                    var t = _oceanBehaviour.transform;
                    var p = t.position;
                    if (Mathf.Abs(p.y - seaY) > 0.01f)
                        t.position = new Vector3(p.x, seaY, p.z);
                }
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

            // v3.12.0 – gather renderers from the ACTUAL OceanRenderer subtree,
            // not from this binder's own GameObject (they usually live apart).
            if (_oceanRenderer != null &&
                (_renderers == null || _renderers.Length == 0))
            {
                _renderers = _oceanRenderer.GetComponentsInChildren<Renderer>(includeInactive: true);
            }
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

        private int _tileRescanFrame = -1;
        private void SetCrestVisualActive(bool active)
        {
            // v3.12.0 – OceanRenderer itself stays ENABLED always so the LOD
            // cascades keep populating. We only toggle the visibility of the
            // built-in Crest ocean tiles (which we always want hidden when the
            // user selected the hide-tiles mode).
            if (_oceanBehaviour != null && !_oceanBehaviour.enabled)
                _oceanBehaviour.enabled = true;

            // Crest builds its tile hierarchy at runtime after OceanRenderer.Awake,
            // so rescan every ~1s until we've found some tiles.
            if (_renderers == null || _renderers.Length == 0 ||
                (Time.frameCount - _tileRescanFrame) > 60)
            {
                _tileRescanFrame = Time.frameCount;
                if (_oceanRenderer != null)
                    _renderers = _oceanRenderer.GetComponentsInChildren<Renderer>(includeInactive: true);
            }

            HideCrestOceanTiles(hideCrestOceanTiles);

            if (bridgeCrestMaterialToVoxelMesh)
                TryBridgeMaterialToVoxel();
        }

        /// <summary>
        /// v3.23.2 — actively DESTROYS any Crest ocean that appears in the
        /// scene. The user does not want a Crest ocean, so we don't just
        /// disable — we remove it entirely. This runs on every Update cycle
        /// so late-loaded prefabs or additive scenes with Crest also get
        /// nuked immediately.
        /// </summary>
        private static System.Type _oceanChunkRendererType;
        private static System.Type _oceanRendererType;
        private void HideCrestOceanTiles(bool hide)
        {
            if (!hide) return;

            if (_oceanRendererType == null)
                _oceanRendererType = System.Type.GetType("Crest.OceanRenderer, Crest");
            if (_oceanChunkRendererType == null)
                _oceanChunkRendererType = System.Type.GetType("Crest.OceanChunkRenderer, Crest");

            // Destroy the OceanRenderer GameObject → tears down all tiles + cameras.
            if (_oceanRendererType != null)
            {
                var renderers = Object.FindObjectsByType(_oceanRendererType, FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; renderers != null && i < renderers.Length; i++)
                {
                    var comp = renderers[i] as Component;
                    if (comp == null || comp.gameObject == null) continue;
                    if (Application.isPlaying) Destroy(comp.gameObject);
                    else DestroyImmediate(comp.gameObject);
                }
            }

            // Also destroy any orphan OceanChunkRenderer GOs that might survive.
            if (_oceanChunkRendererType != null)
            {
                var chunks = Object.FindObjectsByType(_oceanChunkRendererType, FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; chunks != null && i < chunks.Length; i++)
                {
                    var comp = chunks[i] as Component;
                    if (comp == null || comp.gameObject == null) continue;
                    if (Application.isPlaying) Destroy(comp.gameObject);
                    else DestroyImmediate(comp.gameObject);
                }
            }
        }

        private void TryBridgeMaterialToVoxel()
        {
            // v3.22.0 – NO-OP. In the hybrid ocean model voxel water uses the
            // stylized voxel shader for inland lakes and Crest owns the open
            // ocean. Painting the Crest shader onto voxel heightfields caused
            // vertex-snap-driven topology collapse (dark patches around the
            // shore in v3.12.0).
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
