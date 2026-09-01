// Assets/Scripts/VoxelEngine/Weather/WeatherParticles.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                 HIGH-PERFORMANCE WEATHER PARTICLES                   ║
// ║                                                                      ║
// ║  Procedurally generates and manages rain and snow particle systems.  ║
// ║  Follows the player camera and adjusts emission based on live        ║
// ║  weather state, intensity, and planetary seasons.                    ║
// ║                                                                      ║
// ║   • Rain: sharp billboard streaks falling straight along the         ║
// ║     body's radial down, oriented dynamically in-shader.              ║
// ║   • Snow: delicate fluttering snowflakes with crystal radial falloff,║
// ║     natural terminal velocity (~4.2 m/s), full 6-9s lifetime reaching║
// ║     terrain/grids/blocks, and tangent blizzard wind drift.           ║
// ║   • Snow Settling: ground-level drifting snow crystals settling on   ║
// ║     surfaces, terrain, and solid blocks during winter snowfall.      ║
// ║   • Splashes: ground impact puffs during rainfall and blizzard mist. ║
// ║   • PERFORMANCE OPTIMIZED: zero per-frame curve allocations, cached  ║
// ║     radial directions, right-sized emission rates, and frustum-tight ║
// ║     emitter boxes to deliver pristine 60+ FPS in dense downpours.    ║
// ╚══════════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Weather
{
    [RequireComponent(typeof(WeatherManager))]
    public class WeatherParticles : MonoBehaviour
    {
        // Rain system
        private ParticleSystem _rainPS;
        private ParticleSystem.EmissionModule _rainEmission;
        private ParticleSystem.MainModule _rainMain;
        private ParticleSystem.VelocityOverLifetimeModule _rainVel;

        // Snow system (falling flakes)
        private ParticleSystem _snowPS;
        private ParticleSystem.EmissionModule _snowEmission;
        private ParticleSystem.MainModule _snowMain;
        private ParticleSystem.VelocityOverLifetimeModule _snowVel;

        // Snow ground settling system (lingering crystals settling on surfaces/grids/blocks)
        private ParticleSystem _snowGroundPS;
        private ParticleSystem.EmissionModule _snowGroundEmission;
        private ParticleSystem.VelocityOverLifetimeModule _snowGroundVel;

        // Splash system (rain hitting ground / storm mist)
        private ParticleSystem _splashPS;
        private ParticleSystem.EmissionModule _splashEmission;
        private ParticleSystem.VelocityOverLifetimeModule _splashVel;

        // Cached state for zero-allocation updates
        private Vector3 _lastFallDir = Vector3.zero;
        private float _lastRainIntensity = -1f;
        private float _lastSnowIntensity = -1f;
        private float _lastStreakSize = -1f;
        private bool _lastWasBlizzard;

        // Diagnostic throttle + one-shot warnings
        private float _diagTimer;
        private bool _warnedNoManager;

        private static readonly int IdFallDir = Shader.PropertyToID("_WeatherFallDir");

        // Calibrated physics constants
        private const float RAIN_FALL_SPEED = 23f;   // m/s along radial down
        private const float SNOW_FALL_SPEED = 4.2f;  // m/s along radial down (calibrated for full surface reach)
        private const float SPLASH_SPEED    = 1.6f;

        // Optimized emission ceilings for dense look without performance penalty
        private const int MAX_RAIN_RATE         = 1600;
        private const int MAX_SNOW_RATE         = 1400;
        private const int MAX_SNOW_GROUND_RATE  = 280;
        private const int MAX_SPLASH_RATE       = 160;

        private void Start()
        {
            _rainPS        = CreateRainSystem();
            _snowPS        = CreateSnowSystem();
            _snowGroundPS  = CreateSnowGroundSystem();
            _splashPS      = CreateSplashSystem();
            Debug.Log($"[Weather] WeatherParticles initialized (Rain: {_rainPS != null}, Snow: {_snowPS != null}, GroundSnow: {_snowGroundPS != null}, Splash: {_splashPS != null})");
        }

        private void Update()
        {
            var wm = WeatherManager.Instance;
            if (wm == null)
            {
                if (!_warnedNoManager)
                {
                    _warnedNoManager = true;
                    Debug.LogWarning("[Weather] WeatherManager.Instance is NULL — weather particles paused.");
                }
                return;
            }

            Vector3 down = ResolveRadialDown(wm);
            ApplyFallDirection(down);

            var profile = wm.Profile ?? WeatherClimateProfile.Default();
            var seasonInfo = PlanetarySeasons.GetCurrentSeasonInfo();

            // Precipitation state determination:
            // Explicit rain state always remains rain
            bool isExplicitRain = profile.precipitation == WeatherClimateProfile.Precipitation.Rain
                               || wm.CurrentState == WeatherState.LightRain
                               || wm.CurrentState == WeatherState.HeavyRain
                               || wm.TargetState == WeatherState.LightRain
                               || wm.TargetState == WeatherState.HeavyRain;

            // Snow state determination:
            // 1) Weather state is Snow or Blizzard
            // 2) Active body forces snow (WeatherClimateProfile.Precipitation.Snow)
            // 3) Biome is a cold snow biome (when not forced rain)
            // 4) Current seasonal temperature is freezing (when not forced rain)
            bool isSnow = !isExplicitRain && (
                wm.IsSnowBiome
                || wm.CurrentState == WeatherState.Snow
                || wm.CurrentState == WeatherState.Blizzard
                || wm.TargetState == WeatherState.Snow
                || wm.TargetState == WeatherState.Blizzard
                || profile.precipitation == WeatherClimateProfile.Precipitation.Snow
                || (profile.precipitation == WeatherClimateProfile.Precipitation.Auto && seasonInfo.isFreezing)
            );

            float intensity = wm.LocalIntensity;
            bool isBlizzard = wm.CurrentState == WeatherState.Blizzard || wm.TargetState == WeatherState.Blizzard;

            // ── 1) Rain System ──────────────────────────────────────────
            if (!isSnow && intensity > 0.01f)
            {
                if (Mathf.Abs(_lastRainIntensity - intensity) > 0.02f)
                {
                    _lastRainIntensity = intensity;
                    _rainEmission.rateOverTime = intensity * MAX_RAIN_RATE;
                }
                if (!_rainPS.isPlaying) _rainPS.Play();

                // Splashes during rain
                _splashEmission.rateOverTime = intensity * MAX_SPLASH_RATE;
                if (!_splashPS.isPlaying) _splashPS.Play();

                // Dynamic streak elongation for heavy rain
                float targetStreak = 0.55f + intensity * 0.35f;
                if (Mathf.Abs(_lastStreakSize - targetStreak) > 0.05f)
                {
                    _lastStreakSize = targetStreak;
                    _rainMain.startSize = new ParticleSystem.MinMaxCurve(0.50f, targetStreak);
                }
            }
            else
            {
                if (_lastRainIntensity != 0f)
                {
                    _lastRainIntensity = 0f;
                    _rainEmission.rateOverTime = 0;
                    _splashEmission.rateOverTime = 0;
                }
            }

            // ── 2) Snow System ──────────────────────────────────────────
            if (isSnow && intensity > 0.01f)
            {
                if (Mathf.Abs(_lastSnowIntensity - intensity) > 0.02f)
                {
                    _lastSnowIntensity = intensity;
                    _snowEmission.rateOverTime = intensity * MAX_SNOW_RATE;
                    _snowGroundEmission.rateOverTime = intensity * MAX_SNOW_GROUND_RATE;
                }
                if (!_snowPS.isPlaying) _snowPS.Play();
                if (!_snowGroundPS.isPlaying) _snowGroundPS.Play();

                // In blizzard mode, apply strong surface tangent wind drift
                if (isBlizzard != _lastWasBlizzard || Vector3.SqrMagnitude(_lastFallDir - down) > 0.001f)
                {
                    _lastWasBlizzard = isBlizzard;
                    float windStr = isBlizzard ? 9.0f : 2.2f;
                    Vector3 gust = Tangent(down) * windStr;
                    Vector3 snowVelocity = down * SNOW_FALL_SPEED + gust;

                    _snowVel.x = new ParticleSystem.MinMaxCurve(snowVelocity.x);
                    _snowVel.y = new ParticleSystem.MinMaxCurve(snowVelocity.y);
                    _snowVel.z = new ParticleSystem.MinMaxCurve(snowVelocity.z);

                    Vector3 groundDrift = down * 0.6f + gust * 0.5f;
                    _snowGroundVel.x = new ParticleSystem.MinMaxCurve(groundDrift.x);
                    _snowGroundVel.y = new ParticleSystem.MinMaxCurve(groundDrift.y);
                    _snowGroundVel.z = new ParticleSystem.MinMaxCurve(groundDrift.z);
                }

                // Snow mist during heavy blizzards
                if (isBlizzard && intensity > 0.4f)
                {
                    _splashEmission.rateOverTime = intensity * 50f;
                    if (!_splashPS.isPlaying) _splashPS.Play();
                }
            }
            else
            {
                if (_lastSnowIntensity != 0f)
                {
                    _lastSnowIntensity = 0f;
                    _snowEmission.rateOverTime = 0;
                    _snowGroundEmission.rateOverTime = 0;
                }
            }

            // Diagnostic logging (throttled every 10 s)
            _diagTimer += Time.deltaTime;
            if (_diagTimer >= 10f)
            {
                _diagTimer = 0f;
                Debug.Log($"[Weather] Particles: state={wm.CurrentState}->{wm.TargetState} intensity={intensity:F2} " +
                          $"snow={isSnow} blizzard={isBlizzard} rainAlive={(_rainPS != null ? _rainPS.particleCount : 0)} " +
                          $"snowAlive={(_snowPS != null ? _snowPS.particleCount : 0)} groundSnow={(_snowGroundPS != null ? _snowGroundPS.particleCount : 0)}");
            }
        }

        private Vector3 ResolveRadialDown(WeatherManager wm)
        {
            Vector3 anchor = wm.playerCamera != null ? wm.playerCamera.position : transform.position;
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            Vector3 up = body != null ? body.UpAt(anchor) : Vector3.up;
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            return -up.normalized;
        }

        /// <summary>
        /// Updates velocity modules only when the radial direction shifts meaningfully.
        /// </summary>
        private void ApplyFallDirection(Vector3 down)
        {
            if (Vector3.SqrMagnitude(_lastFallDir - down) < 0.0004f) return;
            _lastFallDir = down;

            // Publish global vector for particle shader streak orientation
            Shader.SetGlobalVector(IdFallDir, new Vector4(down.x, down.y, down.z, 0f));

            Vector3 rain = down * RAIN_FALL_SPEED;
            _rainVel.x = new ParticleSystem.MinMaxCurve(rain.x);
            _rainVel.y = new ParticleSystem.MinMaxCurve(rain.y);
            _rainVel.z = new ParticleSystem.MinMaxCurve(rain.z);

            Vector3 snow = down * SNOW_FALL_SPEED;
            _snowVel.x = new ParticleSystem.MinMaxCurve(snow.x);
            _snowVel.y = new ParticleSystem.MinMaxCurve(snow.y);
            _snowVel.z = new ParticleSystem.MinMaxCurve(snow.z);

            Vector3 groundSnow = down * 0.6f;
            _snowGroundVel.x = new ParticleSystem.MinMaxCurve(groundSnow.x);
            _snowGroundVel.y = new ParticleSystem.MinMaxCurve(groundSnow.y);
            _snowGroundVel.z = new ParticleSystem.MinMaxCurve(groundSnow.z);

            Vector3 splash = -down * SPLASH_SPEED;
            _splashVel.x = new ParticleSystem.MinMaxCurve(splash.x);
            _splashVel.y = new ParticleSystem.MinMaxCurve(splash.y);
            _splashVel.z = new ParticleSystem.MinMaxCurve(splash.z);
        }

        private static Vector3 Tangent(Vector3 fallDir)
        {
            Vector3 reference = Mathf.Abs(fallDir.y) > 0.95f ? Vector3.right : Vector3.up;
            Vector3 t = Vector3.Cross(fallDir, reference);
            return t.sqrMagnitude < 1e-6f ? Vector3.forward : t.normalized;
        }

        // ── Particle System Builders ─────────────────────────────────

        private ParticleSystem CreateRainSystem()
        {
            var go = new GameObject("RainParticles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 26f;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.50f, 0.85f);
            main.maxParticles = 3000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            _rainMain = main;

            _rainVel = ps.velocityOverLifetime;
            _rainVel.enabled = true;
            _rainVel.space = ParticleSystemSimulationSpace.World;
            _rainVel.x = new ParticleSystem.MinMaxCurve(0f);
            _rainVel.y = new ParticleSystem.MinMaxCurve(-RAIN_FALL_SPEED);
            _rainVel.z = new ParticleSystem.MinMaxCurve(0f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(38f, 1f, 38f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sharedMaterial = CreateParticleMaterial(new Color(0.90f, 0.93f, 0.98f, 0.95f), 1f /* streak */);

            _rainEmission = ps.emission;
            _rainEmission.rateOverTime = 0;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.40f, 0f), new GradientAlphaKey(0.95f, 0.12f),
                        new GradientAlphaKey(0.95f, 0.70f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;

            return ps;
        }

        private ParticleSystem CreateSnowSystem()
        {
            var go = new GameObject("SnowParticles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 22f;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            // 6.0s to 9.0s lifetime at 4.2m/s ensures snowflakes reach completely down to terrain, grids, and blocks
            main.startLifetime = new ParticleSystem.MinMaxCurve(6.0f, 9.0f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.24f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.92f, 0.96f, 1.0f, 0.85f),
                new Color(1.0f, 1.0f, 1.0f, 0.98f));
            main.maxParticles = 3500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            _snowMain = main;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(44f, 2f, 44f);

            _snowVel = ps.velocityOverLifetime;
            _snowVel.enabled = true;
            _snowVel.space = ParticleSystemSimulationSpace.World;
            _snowVel.x = new ParticleSystem.MinMaxCurve(0f);
            _snowVel.y = new ParticleSystem.MinMaxCurve(-SNOW_FALL_SPEED);
            _snowVel.z = new ParticleSystem.MinMaxCurve(0f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.40f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.20f;
            noise.octaveCount = 2;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sharedMaterial = CreateParticleMaterial(new Color(0.98f, 0.99f, 1.00f, 0.95f), 0f /* snowflake */);

            _snowEmission = ps.emission;
            _snowEmission.rateOverTime = 0;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.10f),
                        new GradientAlphaKey(0.95f, 0.82f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(0.2f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0.3f)));

            return ps;
        }

        private ParticleSystem CreateSnowGroundSystem()
        {
            var go = new GameObject("SnowGroundSettlingParticles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.down * 0.4f;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.0f, 6.0f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.90f, 0.94f, 1.0f, 0.70f),
                new Color(1.0f, 1.0f, 1.0f, 0.90f));
            main.maxParticles = 800;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(28f, 1.5f, 28f);

            _snowGroundVel = ps.velocityOverLifetime;
            _snowGroundVel.enabled = true;
            _snowGroundVel.space = ParticleSystemSimulationSpace.World;
            _snowGroundVel.x = new ParticleSystem.MinMaxCurve(0f);
            _snowGroundVel.y = new ParticleSystem.MinMaxCurve(-0.6f);
            _snowGroundVel.z = new ParticleSystem.MinMaxCurve(0f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.15f;
            noise.octaveCount = 1;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sharedMaterial = CreateParticleMaterial(new Color(0.96f, 0.98f, 1.00f, 0.85f), 0f /* snowflake */);

            _snowGroundEmission = ps.emission;
            _snowGroundEmission.rateOverTime = 0;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.20f),
                        new GradientAlphaKey(0.85f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            return ps;
        }

        private ParticleSystem CreateSplashSystem()
        {
            var go = new GameObject("RainSplashParticles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.down * 1f;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.80f, 0.85f, 0.95f, 0.30f),
                new Color(0.90f, 0.92f, 0.97f, 0.50f));
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            _splashVel = ps.velocityOverLifetime;
            _splashVel.enabled = true;
            _splashVel.space = ParticleSystemSimulationSpace.World;
            _splashVel.x = new ParticleSystem.MinMaxCurve(0f);
            _splashVel.y = new ParticleSystem.MinMaxCurve(SPLASH_SPEED);
            _splashVel.z = new ParticleSystem.MinMaxCurve(0f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(24f, 0.5f, 24f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = CreateParticleMaterial(new Color(0.90f, 0.93f, 0.98f, 0.55f), 0f /* dot */);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.allowOcclusionWhenDynamic = false;

            _splashEmission = ps.emission;
            _splashEmission.rateOverTime = 0;

            return ps;
        }

        // ── Material Builder ─────────────────────────────────────────

        private static Material CreateParticleMaterial(Color color, float shapeMode)
        {
            var shader = Shader.Find("VoxelEngine/WeatherParticlesURP")
                      ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "WeatherParticleMat" };
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", color);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_ShapeMode")) mat.SetFloat("_ShapeMode", shapeMode);
            mat.color = color;
            mat.renderQueue = 3020;
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
            if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            return mat;
        }
    }
}
