// Assets/Scripts/VoxelEngine/Weather/RainFogEffect.cs
//
// Rain fog effect — applies atmospheric fog when it's raining.
// This is separate from UnderwaterEffect (which only activates when
// underwater + raining). RainFogEffect handles the above-water rain atmosphere.
//
// When rain starts: gradually increase fog density and shift fog color
// toward a dark overcast tone.
// When rain stops: gradually restore the original fog settings.
//
// Attach to the same GameObject as the Camera.

using UnityEngine;

namespace VoxelEngine.Weather
{
    [RequireComponent(typeof(Camera))]
    public class RainFogEffect : MonoBehaviour
    {
        [Header("Rain Fog")]
        [Tooltip("Maximum additional fog density during heavy rain.")]
        public float maxRainFogDensity = 0.025f;
        [Tooltip("Fog color during rain (dark overcast).")]
        public Color rainFogColor = new Color(0.28f, 0.30f, 0.34f, 1f);
        [Tooltip("Seconds to transition in/out of rain fog.")]
        public float transitionSpeed = 1.5f;

        private float _currentIntensity; // 0..1 blended
        private bool _saved;
        private Color _sFC;
        private float _sFD;
        private FogMode _sFM;
        private bool _sFog;

        private void LateUpdate()
        {
            var weather = WeatherManager.Instance;
            if (weather == null) return;

            // Only rain (not snow) affects fog
            float targetIntensity = 0f;
            if (weather.IsPrecipitating && !weather.IsSnowBiome)
                targetIntensity = weather.Intensity;

            // Don't apply rain fog if the player is underwater (UnderwaterEffect handles that)
            var waterState = GetComponentInParent<VoxelEngine.Player.PlayerWaterState>();
            if (waterState != null && waterState.IsHeadUnderwater)
                targetIntensity = 0f;

            // Smooth transition
            _currentIntensity = Mathf.MoveTowards(_currentIntensity, targetIntensity, transitionSpeed * Time.deltaTime);

            if (_currentIntensity > 0.01f)
            {
                if (!_saved)
                {
                    _sFC  = RenderSettings.fogColor;
                    _sFD  = RenderSettings.fogDensity;
                    _sFM  = RenderSettings.fogMode;
                    _sFog = RenderSettings.fog;
                    _saved = true;
                }

                float t = _currentIntensity;
                RenderSettings.fog     = true;
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogColor   = Color.Lerp(_sFC, rainFogColor, t);
                RenderSettings.fogDensity = _sFD + maxRainFogDensity * t;
            }
            else if (_saved)
            {
                // Restore
                if (WeatherManager.Instance == null || !WeatherManager.Instance.IsPrecipitating)
                {
                    RenderSettings.fog        = _sFog;
                    RenderSettings.fogColor   = _sFC;
                    RenderSettings.fogDensity = _sFD;
                    RenderSettings.fogMode    = _sFM;
                }
                _saved = false;
            }
        }

        private void OnDisable()
        {
            if (_saved)
            {
                RenderSettings.fog        = _sFog;
                RenderSettings.fogColor   = _sFC;
                RenderSettings.fogDensity = _sFD;
                RenderSettings.fogMode    = _sFM;
                _saved = false;
            }
        }
    }
}
