// Assets/Scripts/VoxelEngine/Cosmos/PlanetSkyController.cs
//
// Camera-relative sky dome that paints a planet-specific zenith → horizon →
// sunset gradient, plus optional aurora / dust haze. It never mutates the
// scene skybox asset: while active, the authored dome and matching solid camera
// background supersede it behind every celestial object. Fog and ambient are
// driven here when weather is clear so a volcanic world stays orange even at noon.

using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Weather;

namespace VoxelEngine.Cosmos
{
    [DefaultExecutionOrder(110)]
    public class PlanetSkyController : MonoBehaviour
    {
        public static PlanetSkyController Instance { get; private set; }

        [Range(0.2f, 4f)] public float blendSeconds = 1.4f;

        public PlanetSkyPalette CurrentPalette { get; private set; }
        public float SpaceBlend { get; private set; }
        public float DayFactor { get; private set; }
        public Vector3 SunDirection { get; private set; } = Vector3.up;
        public Vector3 RadialUp { get; private set; } = Vector3.up;

        private Transform _domeRoot;
        private MeshRenderer _domeRenderer;
        private Material _domeMaterial;
        private Camera _camera;
        private Camera _cameraBackgroundOwner;
        private CameraClearFlags _savedCameraClearFlags;
        private Color _savedCameraBackground;
        private bool _ownsCameraBackground;
        private PlanetSkyPalette _fromPalette;
        private PlanetSkyPalette _toPalette;
        private float _blend;
        private string _lastBodyName;
        private bool _hasPalette;
        private bool _ownsFog;
        private bool _savedFog;
        private float _savedFogDensity;
        private Color _savedFogColor;
        private FogMode _savedFogMode;

        private static readonly int IdZenith = Shader.PropertyToID("_Zenith");
        private static readonly int IdHorizon = Shader.PropertyToID("_Horizon");
        private static readonly int IdGround = Shader.PropertyToID("_Ground");
        private static readonly int IdNight = Shader.PropertyToID("_Night");
        private static readonly int IdSunset = Shader.PropertyToID("_Sunset");
        private static readonly int IdSunDir = Shader.PropertyToID("_SunDir");
        private static readonly int IdRadialUp = Shader.PropertyToID("_RadialUp");
        private static readonly int IdSpaceBlend = Shader.PropertyToID("_SpaceBlend");
        private static readonly int IdDayFactor = Shader.PropertyToID("_DayFactor");
        private static readonly int IdHaze = Shader.PropertyToID("_Haze");
        private static readonly int IdAurora = Shader.PropertyToID("_Aurora");
        private static readonly int IdAuroraA = Shader.PropertyToID("_AuroraColorA");
        private static readonly int IdAuroraB = Shader.PropertyToID("_AuroraColorB");
        private static readonly int IdDust = Shader.PropertyToID("_Dust");

        private void Awake()
        {
            if (Instance == null) Instance = this;
            CurrentPalette = PlanetSkyCatalog.ForKind(PlanetSkyKind.Temperate);
            _fromPalette = CurrentPalette;
            _toPalette = CurrentPalette;
            _blend = 1f;
            EnsureDome();
        }

        private void OnDisable()
        {
            if (_domeRenderer != null) _domeRenderer.enabled = false;
            RestoreCameraBackground();
            RestoreFog();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RestoreCameraBackground();
            RestoreFog();
            if (_domeMaterial != null) Destroy(_domeMaterial);
        }

        private void LateUpdate()
        {
            ResolveCamera();
            if (_camera == null || _domeRoot == null)
            {
                RestoreCameraBackground();
                return;
            }

            var underwater = _camera.GetComponent<VoxelEngine.Player.UnderwaterEffect>();
            bool hidden = underwater != null && underwater.IsUnderwater;
            if (_domeRenderer != null) _domeRenderer.enabled = !hidden;
            if (hidden)
            {
                RestoreCameraBackground();
                return;
            }

            UpdatePaletteTarget();
            if (_blend < 1f)
            {
                _blend = Mathf.Clamp01(_blend + Time.deltaTime / Mathf.Max(0.15f, blendSeconds));
                CurrentPalette = PlanetSkyCatalog.Lerp(_fromPalette, _toPalette, _blend);
            }

            Vector3 viewer = ResolveViewerPosition();
            var atmosphere = AtmosphereManager.Sample(viewer);
            SpaceBlend = atmosphere.HasAtmosphere
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 0.02f, atmosphere.Density01))
                : 1f;

            var body = GravityProvider.ActiveBody;
            RadialUp = body != null ? body.UpAt(viewer) : Vector3.up;
            SunDirection = ResolveSunDirection(body, viewer);
            DayFactor = Mathf.Clamp01(Vector3.Dot(SunDirection, RadialUp) * 1.5f + 0.3f);

            float radius = Mathf.Clamp(_camera.farClipPlane * 0.72f, 1800f, 14000f);
            _domeRoot.position = _camera.transform.position;
            _domeRoot.rotation = Quaternion.identity;
            _domeRoot.localScale = Vector3.one * radius;

            ApplyDomeMaterial();
            ApplyCameraBackground();
            ApplyFogAndAmbient(atmosphere);
            PushGlobals();
        }

        public Color ResolveBackgroundColor()
        {
            Color localAir = Color.Lerp(CurrentPalette.AmbientNight, CurrentPalette.UpperAir, DayFactor);
            return Color.Lerp(localAir, new Color(0.002f, 0.004f, 0.012f, 1f), SpaceBlend);
        }

        public Color ResolveSunColor()
        {
            float elevation = Vector3.Dot(SunDirection, RadialUp);
            float sunset = Mathf.Clamp01(1f - Mathf.Abs(elevation) * 3f);
            return Color.Lerp(CurrentPalette.SunDay, CurrentPalette.SunSunset, sunset);
        }

        public Color ResolveAmbientColor()
        {
            return Color.Lerp(CurrentPalette.AmbientNight, CurrentPalette.AmbientDay, DayFactor);
        }

        private void UpdatePaletteTarget()
        {
            var body = GravityProvider.ActiveBody;
            PlanetSkyPalette target = body != null && body.settings != null
                ? PlanetSkyCatalog.ForBody(body.settings)
                : PlanetSkyCatalog.DeepSpace();
            string name = body != null ? body.DisplayName : "SOL";
            if (!_hasPalette)
            {
                CurrentPalette = target;
                _fromPalette = target;
                _toPalette = target;
                _lastBodyName = name;
                _hasPalette = true;
                _blend = 1f;
                return;
            }

            if (name == _lastBodyName) return;
            _fromPalette = CurrentPalette;
            _toPalette = target;
            _blend = 0f;
            _lastBodyName = name;
        }

        private void ApplyDomeMaterial()
        {
            if (_domeMaterial == null) return;
            PlanetSkyPalette p = CurrentPalette;
            if (_domeMaterial.HasProperty(IdZenith)) _domeMaterial.SetColor(IdZenith, p.Zenith);
            if (_domeMaterial.HasProperty(IdHorizon)) _domeMaterial.SetColor(IdHorizon, p.Horizon);
            if (_domeMaterial.HasProperty(IdGround)) _domeMaterial.SetColor(IdGround, p.GroundFog);
            if (_domeMaterial.HasProperty(IdNight)) _domeMaterial.SetColor(IdNight, p.AmbientNight);
            if (_domeMaterial.HasProperty(IdSunset)) _domeMaterial.SetColor(IdSunset, p.Sunset);
            if (_domeMaterial.HasProperty(IdSunDir)) _domeMaterial.SetVector(IdSunDir, SunDirection);
            if (_domeMaterial.HasProperty(IdRadialUp)) _domeMaterial.SetVector(IdRadialUp, RadialUp);
            if (_domeMaterial.HasProperty(IdSpaceBlend)) _domeMaterial.SetFloat(IdSpaceBlend, SpaceBlend);
            if (_domeMaterial.HasProperty(IdDayFactor)) _domeMaterial.SetFloat(IdDayFactor, DayFactor);
            if (_domeMaterial.HasProperty(IdHaze)) _domeMaterial.SetFloat(IdHaze, p.HazeStrength);
            if (_domeMaterial.HasProperty(IdAurora)) _domeMaterial.SetFloat(IdAurora, p.AuroraStrength * (1f - SpaceBlend));
            if (_domeMaterial.HasProperty(IdAuroraA)) _domeMaterial.SetColor(IdAuroraA, new Color(0.25f, 0.95f, 0.72f, 1f));
            if (_domeMaterial.HasProperty(IdAuroraB)) _domeMaterial.SetColor(IdAuroraB, new Color(0.78f, 0.28f, 0.92f, 1f));
            if (_domeMaterial.HasProperty(IdDust)) _domeMaterial.SetFloat(IdDust, p.DustHaze ? 1f : 0f);
            if (_domeMaterial.HasProperty("_BaseColor"))
                _domeMaterial.SetColor("_BaseColor", Color.Lerp(p.Zenith, p.Horizon, 0.45f));
            if (_domeMaterial.HasProperty("_Color"))
                _domeMaterial.SetColor("_Color", Color.Lerp(p.Zenith, p.Horizon, 0.45f));
        }

        private void ApplyCameraBackground()
        {
            if (_camera == null) return;
            if (!_ownsCameraBackground || _cameraBackgroundOwner != _camera)
            {
                RestoreCameraBackground();
                _cameraBackgroundOwner = _camera;
                _savedCameraClearFlags = _camera.clearFlags;
                _savedCameraBackground = _camera.backgroundColor;
                _ownsCameraBackground = true;
            }

            // The authored dome is the only surface/deep-space background. Keeping the
            // camera on SolidColor prevents Unity's assigned skybox from leaking through
            // during first-frame setup, far-clip changes, or a temporary dome rebuild.
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = ResolveBackgroundColor();
        }

        private void RestoreCameraBackground()
        {
            if (!_ownsCameraBackground) return;
            if (_cameraBackgroundOwner != null)
            {
                _cameraBackgroundOwner.clearFlags = _savedCameraClearFlags;
                _cameraBackgroundOwner.backgroundColor = _savedCameraBackground;
            }
            _cameraBackgroundOwner = null;
            _ownsCameraBackground = false;
        }

        private void ApplyFogAndAmbient(AtmosphereSample atmosphere)
        {
            bool weatherOwnsFog = WeatherManager.Instance != null
                && WeatherManager.Instance.TargetState != WeatherState.Clear;
            if (weatherOwnsFog)
            {
                if (_ownsFog) RestoreFog();
                return;
            }

            if (!_ownsFog)
            {
                _savedFog = RenderSettings.fog;
                _savedFogDensity = RenderSettings.fogDensity;
                _savedFogColor = RenderSettings.fogColor;
                _savedFogMode = RenderSettings.fogMode;
                _ownsFog = true;
            }

            float fogT = (1f - SpaceBlend) * Mathf.Clamp01(atmosphere.Density01 / 0.55f);
            float density = CurrentPalette.SurfaceFogDensity * fogT;
            bool enable = density > 0.0004f && CurrentPalette.Kind != PlanetSkyKind.Moon
                && CurrentPalette.Kind != PlanetSkyKind.Asteroid;
            RenderSettings.fog = enable;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = Color.Lerp(CurrentPalette.GroundFog, CurrentPalette.Horizon, 0.35f);
            RenderSettings.fogDensity = density;
        }

        private void RestoreFog()
        {
            if (!_ownsFog) return;
            RenderSettings.fog = _savedFog;
            RenderSettings.fogDensity = _savedFogDensity;
            RenderSettings.fogColor = _savedFogColor;
            RenderSettings.fogMode = _savedFogMode;
            _ownsFog = false;
        }

        private void PushGlobals()
        {
            Shader.SetGlobalColor("_VoxelSkyZenith", CurrentPalette.Zenith);
            Shader.SetGlobalColor("_VoxelSkyHorizon", CurrentPalette.Horizon);
            Shader.SetGlobalColor("_VoxelSkyRim", CurrentPalette.AtmosphereRim);
            Shader.SetGlobalFloat("_VoxelSkyHaze", CurrentPalette.HazeStrength);
        }

        private void EnsureDome()
        {
            if (_domeRoot != null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "PlanetSkyDome";
            go.transform.SetParent(transform, false);
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            _domeRoot = go.transform;
            _domeRenderer = go.GetComponent<MeshRenderer>();
            if (_domeRenderer != null)
            {
                _domeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _domeRenderer.receiveShadows = false;
                _domeRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                _domeRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                _domeRenderer.allowOcclusionWhenDynamic = false;
                _domeRenderer.sharedMaterial = CreateDomeMaterial();
            }
        }

        private Material CreateDomeMaterial()
        {
            // Step 51 creates this Resources template so the custom shader remains
            // referenced in standalone builds. Editor/no-setup sessions retain the
            // Shader.Find path and still receive the complete runtime sky.
            var template = Resources.Load<Material>("VoxelEngineRuntime/PlanetSkyDome");
            if (template != null && template.shader != null)
            {
                _domeMaterial = new Material(template) { name = "Mat_PlanetSkyDome_Runtime" };
            }
            else
            {
                Shader shader = Shader.Find("VoxelEngine/PlanetSkyDomeURP")
                             ?? Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Unlit/Color")
                             ?? Shader.Find("Sprites/Default")
                             ?? Shader.Find("Hidden/InternalErrorShader");
                if (shader == null)
                {
                    Debug.LogError("[PlanetSkyController] No compatible sky shader is available.");
                    return null;
                }
                _domeMaterial = new Material(shader) { name = "Mat_PlanetSkyDome_Runtime" };
            }

            _domeMaterial.renderQueue = 1000;
            if (_domeMaterial.HasProperty("_ZWrite")) _domeMaterial.SetInt("_ZWrite", 0);
            if (_domeMaterial.HasProperty("_Cull"))
                _domeMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Front);
            return _domeMaterial;
        }

        private void ResolveCamera()
        {
            Camera candidate = Camera.main;
            if (candidate == null)
            {
                var player = FindAnyObjectByType<VoxelEngine.Player.PlayerController>();
                if (player != null) candidate = player.playerCamera;
            }

            if (candidate == _camera) return;
            RestoreCameraBackground();
            _camera = candidate;
        }

        private Vector3 ResolveViewerPosition()
        {
            if (_camera != null) return _camera.transform.position;
            var origin = SpaceOrigin.Instance;
            if (origin != null && origin.viewer != null) return origin.viewer.position;
            return transform.position;
        }

        private static Vector3 ResolveSunDirection(CelestialBody body, Vector3 viewer)
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null || !registry.IsReady || registry.Sun == null)
                return RenderSettings.sun != null ? -RenderSettings.sun.transform.forward : Vector3.up;

            Vector3 bodyKm = Vector3.zero;
            if (body != null && body.settings != null)
            {
                for (int i = 0; i < registry.Bodies.Count; i++)
                {
                    if (registry.Bodies[i] != null && registry.Bodies[i].settings == body.settings)
                    {
                        bodyKm = registry.Bodies[i].positionKm;
                        break;
                    }
                }
            }
            else
            {
                var origin = SpaceOrigin.Instance;
                if (origin != null)
                    bodyKm = (Vector3)(Unity.Mathematics.float3)origin.GetCosmicKm(viewer);
            }

            Vector3 sunDirKm = registry.Sun.positionKm - bodyKm;
            if (sunDirKm.sqrMagnitude < 1f) return Vector3.up;
            return sunDirKm.normalized;
        }
    }
}
