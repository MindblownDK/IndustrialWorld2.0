// Assets/Scripts/VoxelEngine/Cosmos/SolarGlareRenderer.cs
//
// Camera-space solar glare with restrained lens ghosts. Visibility follows the
// real star direction and is suppressed by the local horizon, celestial-body
// eclipses, and nearby physical occluders. No texture or scene prefab is needed.

using Unity.Mathematics;
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    [DefaultExecutionOrder(120)]
    public sealed class SolarGlareRenderer : MonoBehaviour
    {
        [Header("Presentation")]
        [Range(0.08f, 0.45f)] public float glareScreenHeight = 0.24f;
        [Range(0f, 1f)] public float surfaceIntensity = 0.82f;
        [Range(0f, 1f)] public float vacuumIntensity = 0.62f;
        [Range(0f, 0.4f)] public float ghostIntensity = 0.13f;
        [Range(1f, 12f)] public float fadeSpeed = 5f;

        [Header("Occlusion")]
        public LayerMask localOcclusionMask = ~0;
        [Range(50f, 10000f)] public float localOcclusionDistance = 3500f;
        [Range(0.5f, 250f)] public float visualSunRadiusKm = 80f;

        private Camera _camera;
        private Transform _glare;
        private Transform _ghostNear;
        private Transform _ghostFar;
        private MeshRenderer _glareRenderer;
        private MeshRenderer _ghostNearRenderer;
        private MeshRenderer _ghostFarRenderer;
        private Material _glareMaterial;
        private Material _ghostNearMaterial;
        private Material _ghostFarMaterial;
        private readonly RaycastHit[] _occlusionHits = new RaycastHit[12];
        private float _opacity;

        private static readonly int IdTint = Shader.PropertyToID("_Tint");
        private static readonly int IdOpacity = Shader.PropertyToID("_Opacity");
        private static readonly int IdMode = Shader.PropertyToID("_Mode");

        private void Awake()
        {
            EnsureVisuals();
            SetVisible(false);
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_glareMaterial != null) Destroy(_glareMaterial);
            if (_ghostNearMaterial != null) Destroy(_ghostNearMaterial);
            if (_ghostFarMaterial != null) Destroy(_ghostFarMaterial);
        }

        private void LateUpdate()
        {
            ResolveCamera();
            var registry = CosmicRegistry.Instance;
            var origin = SpaceOrigin.Instance;
            if (_camera == null || registry == null || !registry.IsReady || registry.Sun == null || origin == null)
            {
                FadeTo(0f);
                return;
            }

            var underwater = _camera.GetComponent<VoxelEngine.Player.UnderwaterEffect>();
            if (underwater != null && underwater.IsUnderwater)
            {
                FadeTo(0f);
                return;
            }

            double3 cameraCosmic = origin.GetCosmicKm(_camera.transform.position);
            double3 toSun = registry.Sun.positionKmD - cameraCosmic;
            double distanceKm = math.length(toSun);
            if (distanceKm <= 0.001d || double.IsNaN(distanceKm) || double.IsInfinity(distanceKm))
            {
                FadeTo(0f);
                return;
            }

            Vector3 sunDirection = (Vector3)(float3)(toSun / distanceKm);
            Vector3 viewport = _camera.WorldToViewportPoint(_camera.transform.position + sunDirection * 1000f);
            bool insideView = viewport.z > 0f
                && viewport.x > -0.18f && viewport.x < 1.18f
                && viewport.y > -0.18f && viewport.y < 1.18f;
            if (!insideView)
            {
                FadeTo(0f);
                return;
            }

            float spaceBlend = ResolveSpaceBlend();
            float horizonVisibility = ResolveHorizonVisibility(sunDirection);
            float bodyVisibility = ResolveBodyVisibility(registry, cameraCosmic, sunDirection, distanceKm);
            float localVisibility = IsLocallyOccluded(sunDirection) ? 0f : 1f;
            float facing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.28f,
                Vector3.Dot(_camera.transform.forward, sunDirection)));
            float environmentIntensity = Mathf.Lerp(surfaceIntensity, vacuumIntensity, spaceBlend);
            float target = horizonVisibility * bodyVisibility * localVisibility * facing * environmentIntensity;
            FadeTo(target);
            if (_opacity <= 0.002f) return;

            EnsureVisuals();
            Vector2 sunViewport = new Vector2(viewport.x, viewport.y);
            Vector2 center = new Vector2(0.5f, 0.5f);
            Vector2 axis = sunViewport - center;

            float angularRadius = Mathf.Atan2(visualSunRadiusKm, Mathf.Max(0.01f, (float)distanceKm));
            float angularBoost = Mathf.Clamp(angularRadius * Mathf.Rad2Deg * 0.018f, 0f, 0.12f);
            float atmosphereScale = Mathf.Lerp(1.15f, 0.78f, spaceBlend);
            float mainSize = glareScreenHeight * atmosphereScale + angularBoost;
            PlaceQuad(_glare, sunViewport, mainSize, 2f);
            PlaceQuad(_ghostNear, center - axis * 0.38f, mainSize * 0.23f, 1.8f);
            PlaceQuad(_ghostFar, center - axis * 0.78f, mainSize * 0.12f, 1.7f);

            PlanetSkyPalette palette = PlanetSkyController.Instance != null
                ? PlanetSkyController.Instance.CurrentPalette
                : PlanetSkyCatalog.DeepSpace();
            Color sunColor = PlanetSkyController.Instance != null
                ? PlanetSkyController.Instance.ResolveSunColor()
                : new Color(1f, 0.91f, 0.70f, 1f);
            ApplyMaterial(_glareMaterial, sunColor, _opacity);
            ApplyMaterial(_ghostNearMaterial, palette.AtmosphereRim, _opacity * ghostIntensity);
            ApplyMaterial(_ghostFarMaterial, palette.NebulaSecondary, _opacity * ghostIntensity * 0.72f);
        }

        private void FadeTo(float target)
        {
            _opacity = Mathf.MoveTowards(_opacity, Mathf.Clamp01(target), Time.unscaledDeltaTime * fadeSpeed);
            bool visible = _opacity > 0.002f;
            SetVisible(visible);
            if (!visible) return;
            if (_glareMaterial != null) _glareMaterial.SetFloat(IdOpacity, _opacity);
            if (_ghostNearMaterial != null) _ghostNearMaterial.SetFloat(IdOpacity, _opacity * ghostIntensity);
            if (_ghostFarMaterial != null) _ghostFarMaterial.SetFloat(IdOpacity, _opacity * ghostIntensity * 0.72f);
        }

        private float ResolveSpaceBlend()
        {
            var sky = PlanetSkyController.Instance;
            if (sky != null) return sky.SpaceBlend;
            var sample = VoxelEngine.GridSystem.AtmosphereManager.Sample(_camera.transform.position);
            return sample.HasAtmosphere
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 0.02f, sample.Density01))
                : 1f;
        }

        private float ResolveHorizonVisibility(Vector3 sunDirection)
        {
            var body = GravityProvider.ActiveBody;
            if (body == null) return 1f;
            Vector3 up = body.UpAt(_camera.transform.position);
            float elevation = Vector3.Dot(sunDirection, up);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.015f, 0.055f, elevation));
        }

        private static float ResolveBodyVisibility(
            CosmicRegistry registry,
            double3 cameraCosmic,
            Vector3 sunDirection,
            double sunDistanceKm)
        {
            float visibility = 1f;
            for (int i = 0; i < registry.Bodies.Count; i++)
            {
                var body = registry.Bodies[i];
                if (body == null || body.settings == null) continue;

                double3 toBody = registry.CosmicPositionOf(body) - cameraCosmic;
                double distanceKm = math.length(toBody);
                double radiusKm = math.max(0.001d, body.settings.radiusKm);
                if (distanceKm <= radiusKm || distanceKm >= sunDistanceKm) continue;

                double3 bodyDirection = toBody / distanceKm;
                double alignment = math.clamp(math.dot(bodyDirection, CosmicRegistry.ToDouble3(sunDirection)), -1d, 1d);
                double separation = math.acos(alignment);
                double angularRadius = math.asin(math.clamp(radiusKm / distanceKm, 0d, 1d));
                double feather = math.max(angularRadius * 0.08d, 0.00035d);
                float bodyVisibility = Mathf.Clamp01((float)((separation - angularRadius) / feather));
                visibility = Mathf.Min(visibility, bodyVisibility);
                if (visibility <= 0f) return 0f;
            }
            return visibility;
        }

        private bool IsLocallyOccluded(Vector3 sunDirection)
        {
            Vector3 start = _camera.transform.position + sunDirection * Mathf.Max(0.15f, _camera.nearClipPlane * 1.5f);
            int count = Physics.RaycastNonAlloc(start, sunDirection, _occlusionHits,
                localOcclusionDistance, localOcclusionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var collider = _occlusionHits[i].collider;
                if (collider == null) continue;
                Transform hitTransform = collider.transform;
                if (hitTransform == _camera.transform || hitTransform.IsChildOf(_camera.transform)) continue;
                return true;
            }
            return false;
        }

        private void ResolveCamera()
        {
            Camera candidate = Camera.main;
            if (candidate == null)
            {
                var player = FindAnyObjectByType<VoxelEngine.Player.PlayerController>();
                if (player != null) candidate = player.playerCamera;
            }
            _camera = candidate;
        }

        private void EnsureVisuals()
        {
            if (_glare != null) return;
            _glare = CreateQuad("SolarGlare", out _glareRenderer, out _glareMaterial, 0f);
            _ghostNear = CreateQuad("SolarLensGhostNear", out _ghostNearRenderer, out _ghostNearMaterial, 1f);
            _ghostFar = CreateQuad("SolarLensGhostFar", out _ghostFarRenderer, out _ghostFarMaterial, 1f);
        }

        private Transform CreateQuad(
            string objectName,
            out MeshRenderer meshRenderer,
            out Material material,
            float mode)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = objectName;
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            meshRenderer.allowOcclusionWhenDynamic = false;
            material = CreateMaterial(mode);
            meshRenderer.sharedMaterial = material;
            return go.transform;
        }

        private static Material CreateMaterial(float mode)
        {
            var template = Resources.Load<Material>("VoxelEngineRuntime/SolarGlare");
            Material material;
            if (template != null && template.shader != null)
            {
                material = new Material(template);
            }
            else
            {
                Shader shader = Shader.Find("VoxelEngine/SolarGlareURP");
                if (shader == null)
                {
                    Debug.LogError("[SolarGlareRenderer] Solar glare shader is unavailable. Run Voxel Engine Setup Step 51.");
                    return null;
                }
                material = new Material(shader);
            }
            material.name = mode < 0.5f ? "Mat_SolarGlare_Runtime" : "Mat_SolarLensGhost_Runtime";
            material.renderQueue = 3100;
            if (material.HasProperty(IdMode)) material.SetFloat(IdMode, mode);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            if (material.HasProperty("_Cull")) material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            return material;
        }

        private void PlaceQuad(Transform quad, Vector2 viewport, float screenHeight, float depth)
        {
            if (quad == null || _camera == null) return;
            float safeDepth = Mathf.Max(_camera.nearClipPlane + 0.1f, depth);
            quad.position = _camera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, safeDepth));
            quad.rotation = _camera.transform.rotation;
            float worldHeight = 2f * safeDepth * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            quad.localScale = Vector3.one * Mathf.Max(0.01f, worldHeight * screenHeight);
        }

        private static void ApplyMaterial(Material material, Color tint, float opacity)
        {
            if (material == null) return;
            tint.a = 1f;
            if (material.HasProperty(IdTint)) material.SetColor(IdTint, tint);
            if (material.HasProperty(IdOpacity)) material.SetFloat(IdOpacity, Mathf.Clamp01(opacity));
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
        }

        private void SetVisible(bool visible)
        {
            if (_glareRenderer != null) _glareRenderer.enabled = visible && _glareMaterial != null;
            if (_ghostNearRenderer != null) _ghostNearRenderer.enabled = visible && _ghostNearMaterial != null;
            if (_ghostFarRenderer != null) _ghostFarRenderer.enabled = visible && _ghostFarMaterial != null;
        }
    }
}
