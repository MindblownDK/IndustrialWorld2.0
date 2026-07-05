// Assets/Scripts/VoxelEngine/Weather/WeatherLighting.cs
//
// Adjusts the scene's directional light and fog based on weather state.
// Darkens during rain, adds fog during heavy weather, lightning flash during thunder.

using UnityEngine;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// Modifies scene lighting to match the current weather.
    /// Automatically finds the directional light in the scene.
    /// </summary>
    [RequireComponent(typeof(WeatherManager))]
    public class WeatherLighting : MonoBehaviour
    {
        [Header("Light")]
        [Tooltip("If null, auto-finds the first directional light.")]
        public Light directionalLight;

        [Header("Clear Sky")]
        public float clearIntensity = 1.0f;
        public Color clearAmbient = new Color(0.55f, 0.62f, 0.72f);
        public Color clearFogColor = new Color(0.70f, 0.80f, 0.90f);

        [Header("Rain")]
        public float rainIntensity = 0.35f;
        public Color rainAmbient = new Color(0.30f, 0.33f, 0.40f);
        public Color rainFogColor = new Color(0.45f, 0.50f, 0.58f);
        public float rainFogDensity = 0.015f;

        [Header("Heavy Rain")]
        public float heavyRainIntensity = 0.20f;
        public Color heavyRainFogColor = new Color(0.35f, 0.40f, 0.48f);
        public float heavyFogDensity = 0.030f;

        [Header("Snow")]
        public float snowIntensity = 0.50f;
        public Color snowAmbient = new Color(0.55f, 0.58f, 0.65f);
        public Color snowFogColor = new Color(0.75f, 0.78f, 0.82f);
        public float snowFogDensity = 0.008f;

        [Header("Blizzard")]
        public float blizzardIntensity = 0.25f;
        public Color blizzardFogColor = new Color(0.80f, 0.82f, 0.85f);
        public float blizzardFogDensity = 0.045f;

        [Header("Lightning Flash")]
        public float flashDuration = 0.15f;
        public float flashIntensity = 3.0f;

        private float _baseFogDensity;
        private bool _fogWasEnabled;
        private Color _originalAmbient;
        private float _flashTimer;

        private void Start()
        {
            if (directionalLight == null)
            {
                var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
                foreach (var l in lights)
                    if (l.type == LightType.Directional) { directionalLight = l; break; }
            }

            _baseFogDensity = RenderSettings.fogDensity;
            _fogWasEnabled = RenderSettings.fog;
            _originalAmbient = RenderSettings.ambientLight;
        }

        private void Update()
        {
            var wm = WeatherManager.Instance;
            if (wm == null || directionalLight == null) return;

            float intensity = wm.Intensity;
            var state = wm.TargetState;
            bool isSnow = wm.IsSnowBiome;

            // Target values based on weather state.
            float targetLightIntensity;
            Color targetAmbient;
            Color targetFog;
            float targetFogDensity;
            bool enableFog;

            switch (state)
            {
                case WeatherState.HeavyRain:
                    targetLightIntensity = heavyRainIntensity;
                    targetAmbient = rainAmbient;
                    targetFog = heavyRainFogColor;
                    targetFogDensity = heavyFogDensity;
                    enableFog = true;
                    break;
                case WeatherState.LightRain:
                    targetLightIntensity = rainIntensity;
                    targetAmbient = rainAmbient;
                    targetFog = rainFogColor;
                    targetFogDensity = rainFogDensity;
                    enableFog = true;
                    break;
                case WeatherState.Overcast:
                    targetLightIntensity = Mathf.Lerp(clearIntensity, rainIntensity, 0.5f);
                    targetAmbient = Color.Lerp(clearAmbient, rainAmbient, 0.4f);
                    targetFog = rainFogColor;
                    targetFogDensity = 0.005f;
                    enableFog = true;
                    break;
                case WeatherState.Snow:
                    targetLightIntensity = snowIntensity;
                    targetAmbient = snowAmbient;
                    targetFog = snowFogColor;
                    targetFogDensity = snowFogDensity;
                    enableFog = true;
                    break;
                case WeatherState.Blizzard:
                    targetLightIntensity = blizzardIntensity;
                    targetAmbient = snowAmbient;
                    targetFog = blizzardFogColor;
                    targetFogDensity = blizzardFogDensity;
                    enableFog = true;
                    break;
                default: // Clear
                    targetLightIntensity = clearIntensity;
                    targetAmbient = clearAmbient;
                    targetFog = clearFogColor;
                    targetFogDensity = 0f;
                    enableFog = false;
                    break;
            }

            float blend = Time.deltaTime * 0.5f; // slow blend

            // Lightning flash
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                float flashT = Mathf.Clamp01(_flashTimer / flashDuration);
                directionalLight.intensity = Mathf.Lerp(targetLightIntensity, flashIntensity, flashT);
            }
            else
            {
                directionalLight.intensity = Mathf.Lerp(directionalLight.intensity, targetLightIntensity, blend);
            }

            RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, targetAmbient, blend);
            RenderSettings.fog = enableFog || intensity > 0.1f;
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, targetFog, blend);
            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, targetFogDensity, blend);
            RenderSettings.fogMode = FogMode.Exponential;
        }

        /// <summary>Trigger a lightning flash (called from WeatherManager on thunder).</summary>
        public void TriggerFlash()
        {
            _flashTimer = flashDuration;
        }

        private void OnDisable()
        {
            // Restore original settings.
            if (directionalLight != null)
                directionalLight.intensity = clearIntensity;
            RenderSettings.fog = _fogWasEnabled;
            RenderSettings.fogDensity = _baseFogDensity;
            RenderSettings.ambientLight = _originalAmbient;
        }
    }
}
