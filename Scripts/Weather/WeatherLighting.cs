// Assets/Scripts/VoxelEngine/Weather/WeatherLighting.cs
//
// Makes the scene FEEL like the weather. It owns three things, all non-destructive:
//
//   1. SUN DARKENING — publishes a sun-intensity scale + ambient tint that
//      SunLightController multiplies into its day/night light. Weather never writes the
//      sun directly, so it can never fight the star/day-night controller. Clear weather
//      (and airless bodies) publish neutral 1.0 / white → zero effect.
//   2. RAIN & SNOW FOG — owns RenderSettings fog while weather is active and non-clear.
//      PlanetSkyController yields fog to weather whenever the target state isn't Clear,
//      so the two never collide; the moment weather clears, the sky retakes the fog.
//   3. LIGHTNING — on every WeatherManager.OnThunder strike it flashes the sun scale +
//      ambient for a fraction of a second. Because the flash rides the same multiplier
//      the sun already uses, it lights the whole scene correctly with no extra lights.

using UnityEngine;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// Publishes weather-driven lighting modifiers and owns weather fog. Attach alongside
    /// <see cref="WeatherManager"/> (it is created automatically by the manager).
    /// </summary>
    [RequireComponent(typeof(WeatherManager))]
    public class WeatherLighting : MonoBehaviour
    {
        /// <summary>Global shader float that dims the sky dome during storms (0 = clear sky).</summary>
        private static readonly int IdWeatherDarken = Shader.PropertyToID("_VoxelWeatherDarken");

        // ── Live modifiers read by SunLightController (neutral when weather is clear/absent) ──
        /// <summary>Multiplier applied to the sun directional light intensity. 1 = unchanged.</summary>
        public static float SunIntensityScale { get; private set; } = 1f;
        /// <summary>Component-wise multiplier applied to RenderSettings.ambientLight. White = unchanged.</summary>
        public static Color AmbientScale { get; private set; } = Color.white;
        /// <summary>True while weather is actively reshaping lighting (used for diagnostics).</summary>
        public static bool Modulating { get; private set; }

        [Header("Storm Fog Colours")]
        public Color rainFogColor     = new Color(0.45f, 0.50f, 0.58f);
        public Color heavyRainFogColor = new Color(0.35f, 0.40f, 0.48f);
        public Color snowFogColor     = new Color(0.75f, 0.78f, 0.82f);
        public Color blizzardFogColor = new Color(0.80f, 0.82f, 0.85f);
        public Color overcastFogColor = new Color(0.55f, 0.58f, 0.64f);

        [Header("Storm Fog Density (authored peaks, scaled by the profile)")]
        [Range(0f, 0.05f)] public float overcastFogDensity = 0.0045f;
        [Range(0f, 0.05f)] public float rainFogDensity     = 0.007f;
        [Range(0f, 0.05f)] public float heavyFogDensity     = 0.012f;
        [Range(0f, 0.05f)] public float snowFogDensity     = 0.006f;
        [Range(0f, 0.05f)] public float blizzardFogDensity = 0.028f;

        [Header("Lightning Flash")]
        [Tooltip("Seconds the sun flare holds on a thunder strike.")]
        public float flashDuration = 0.18f;
        [Tooltip("Peak sun-intensity scale during the flash (stacks on the storm-darkened base).")]
        public float flashIntensityScale = 4.0f;

        private WeatherManager _wm;
        private float _flashTimer;

        // Fog ownership bookkeeping (mirrors PlanetSkyController's own/restore pattern).
        private bool _ownsFog;
        private bool _savedFog;
        private float _savedFogDensity;
        private Color _savedFogColor;
        private FogMode _savedFogMode;

        private void OnEnable()
        {
            _wm = GetComponent<WeatherManager>();
            if (_wm != null) _wm.OnThunder += HandleThunder;
        }

        private void OnDisable()
        {
            if (_wm != null) _wm.OnThunder -= HandleThunder;
            ReleaseFog();
            SunIntensityScale = 1f;
            AmbientScale = Color.white;
            Modulating = false;
            Shader.SetGlobalFloat(IdWeatherDarken, 0f);   // never leave a stale storm sky
        }

        private void Update()
        {
            if (_wm == null) _wm = WeatherManager.Instance;
            if (_wm == null)
            {
                ReleaseFog();
                SunIntensityScale = Mathf.MoveTowards(SunIntensityScale, 1f, Time.deltaTime * 2f);
                AmbientScale = Color.Lerp(AmbientScale, Color.white, Time.deltaTime * 2f);
                Modulating = false;
                Shader.SetGlobalFloat(IdWeatherDarken, 0f);
                return;
            }

            var profile = _wm.Profile ?? WeatherClimateProfile.Default();
            WeatherState state = _wm.TargetState;
            // Above the cloud deck the storm is below you: no darkening, and above all no fog.
            float proximity = Mathf.Clamp01(_wm.SurfaceProximity);
            bool nonClear = _wm.IsWeatherActive && state != WeatherState.Clear && proximity > 0.02f;

            float sunScaleTarget = 1f;
            Color ambientTintTarget = Color.white;
            bool wantFog = false;
            Color fogColorTarget = rainFogColor;
            float fogDensityTarget = 0f;
            float darken = 0f;

            if (nonClear)
            {
                // Storm darkening scales smoothly with precipitation intensity.
                darken = Mathf.Clamp01(profile.stormDarkening) * _wm.Intensity * proximity;
                sunScaleTarget = Mathf.Lerp(1f, Mathf.Clamp01(profile.stormLightFloor), darken);
                Color stormAmbientTint = new Color(0.55f, 0.60f, 0.68f, 1f);
                ambientTintTarget = Color.Lerp(Color.white, stormAmbientTint, darken);

                switch (state)
                {
                    case WeatherState.HeavyRain:
                        fogColorTarget = heavyRainFogColor; fogDensityTarget = heavyFogDensity; wantFog = true; break;
                    case WeatherState.LightRain:
                        fogColorTarget = rainFogColor; fogDensityTarget = rainFogDensity; wantFog = true; break;
                    case WeatherState.Overcast:
                        fogColorTarget = overcastFogColor; fogDensityTarget = overcastFogDensity; wantFog = true; break;
                    case WeatherState.Snow:
                        fogColorTarget = snowFogColor; fogDensityTarget = snowFogDensity; wantFog = true; break;
                    case WeatherState.Blizzard:
                        fogColorTarget = blizzardFogColor; fogDensityTarget = blizzardFogDensity; wantFog = true; break;
                }
                fogDensityTarget *= Mathf.Max(0f, profile.stormFogScale) * proximity;
            }

            // Lightning flash rides the same multiplier the sun already applies.
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                float f = Mathf.Clamp01(_flashTimer / Mathf.Max(0.01f, flashDuration));
                sunScaleTarget = Mathf.Max(sunScaleTarget, Mathf.Lerp(1f, flashIntensityScale, f));
                ambientTintTarget = Color.Lerp(ambientTintTarget, new Color(2.2f, 2.2f, 2.6f, 1f), f);
            }

            // Ease the published modifiers toward their targets (slow, organic blend).
            float blend = Time.deltaTime * 1.5f;
            SunIntensityScale = Mathf.Lerp(SunIntensityScale, sunScaleTarget, blend);
            AmbientScale = Color.Lerp(AmbientScale, ambientTintTarget, blend);
            Modulating = nonClear || _flashTimer > 0f || SunIntensityScale < 0.999f;

            // Sky darkening: publish the same storm factor so the sky dome dims in step
            // with the sun. The sky is the brightest thing on screen, so without this the
            // scene never reads as "it got darker" even though the terrain light dropped.
            Shader.SetGlobalFloat(IdWeatherDarken, darken);

            // Fog ownership: only write while weather is actively non-clear; otherwise hand
            // fog back to PlanetSkyController (which owns it whenever the state is Clear).
            if (wantFog)
            {
                if (!_ownsFog) CaptureFog();
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, fogColorTarget, Time.deltaTime * 0.5f);
                RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, fogDensityTarget, Time.deltaTime * 0.5f);
            }
            else
            {
                ReleaseFog();
            }
        }

        /// <summary>Called by the manager's thunder event — fires a synced sky flash
        /// (the rumble itself is delayed by the audio to honour the speed of sound).</summary>
        private void HandleThunder(Vector3 strikePosition) => _flashTimer = flashDuration;

        private void CaptureFog()
        {
            if (_ownsFog) return;
            _savedFog = RenderSettings.fog;
            _savedFogDensity = RenderSettings.fogDensity;
            _savedFogColor = RenderSettings.fogColor;
            _savedFogMode = RenderSettings.fogMode;
            _ownsFog = true;
        }

        private void ReleaseFog()
        {
            if (!_ownsFog) return;
            RenderSettings.fog = _savedFog;
            RenderSettings.fogDensity = _savedFogDensity;
            RenderSettings.fogColor = _savedFogColor;
            RenderSettings.fogMode = _savedFogMode;
            _ownsFog = false;
        }
    }
}
