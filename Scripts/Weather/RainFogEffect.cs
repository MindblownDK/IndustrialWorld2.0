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
using VoxelEngine.Environment;

namespace VoxelEngine.Weather
{
    [DefaultExecutionOrder(120)]
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
        [Tooltip("Maximum additional fog density from severe local smog while weather owns the fog pass.")]
        public float maxSmogFogDensity = 0.018f;
        public Color smogFogColor = new Color(0.30f, 0.25f, 0.20f, 1f);

        private float _currentIntensity; // rain, 0..1 blended
        private float _currentSmog;
        private bool _saved;
        private Color _sFC;
        private float _sFD;
        private FogMode _sFM;
        private bool _sFog;

        private void LateUpdate()
        {
            // 14.46.1: no shaders on a dedicated server - purely visual,
            // the component retires itself headless.
            if (VoxelEngine.Networking.NetworkSession.IsDedicated) { enabled = false; return; }
            var weather = WeatherManager.Instance;

            // Only rain (not snow) affects the weather contribution. Smog joins this pass
            // only while non-clear weather owns fog; on clear days PlanetSkyController owns it.
            float targetIntensity = 0f;
            float targetSmog = 0f;
            bool weatherOwnsFog = weather != null && weather.TargetState != WeatherState.Clear;
            if (weather != null && weather.IsPrecipitating && !weather.IsSnowBiome)
                targetIntensity = weather.Intensity;
            if (weatherOwnsFog)
                targetSmog = PollutionService.SampleAirborne01(transform.position);

            // Don't apply weather/smog fog if the player is underwater.
            var waterState = GetComponentInParent<VoxelEngine.Player.PlayerWaterState>();
            if (waterState != null && waterState.IsHeadUnderwater)
            {
                targetIntensity = 0f;
                targetSmog = 0f;
            }

            _currentIntensity = Mathf.MoveTowards(_currentIntensity, targetIntensity,
                transitionSpeed * Time.deltaTime);
            _currentSmog = Mathf.MoveTowards(_currentSmog, targetSmog,
                transitionSpeed * Time.deltaTime);

            if (_currentIntensity > 0.01f || _currentSmog > 0.01f)
            {
                if (!_saved)
                {
                    _sFC  = RenderSettings.fogColor;
                    _sFD  = RenderSettings.fogDensity;
                    _sFM  = RenderSettings.fogMode;
                    _sFog = RenderSettings.fog;
                    _saved = true;
                }

                Color polluted = Color.Lerp(_sFC, smogFogColor, _currentSmog * 0.78f);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogColor = Color.Lerp(polluted, rainFogColor, _currentIntensity);
                RenderSettings.fogDensity = _sFD + maxRainFogDensity * _currentIntensity
                    + maxSmogFogDensity * _currentSmog;
            }
            else if (_saved)
            {
                if (!weatherOwnsFog)
                {
                    RenderSettings.fog = _sFog;
                    RenderSettings.fogColor = _sFC;
                    RenderSettings.fogDensity = _sFD;
                    RenderSettings.fogMode = _sFM;
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
