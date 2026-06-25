// Assets/Scripts/VoxelEngine/Weather/WeatherParticles.cs
//
// Procedurally creates rain and snow particle systems.
// Follows the player camera. Adjusts emission rate based on WeatherManager.Intensity.
// Rain = stretched particles falling fast. Snow = small round particles floating down.

using UnityEngine;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// Creates and manages rain/snow particle systems at runtime.
    /// No prefabs needed — everything is built in code.
    /// </summary>
    [RequireComponent(typeof(WeatherManager))]
    public class WeatherParticles : MonoBehaviour
    {
        // Rain system
        private ParticleSystem _rainPS;
        private ParticleSystem.EmissionModule _rainEmission;
        private ParticleSystem.MainModule _rainMain;

        // Snow system
        private ParticleSystem _snowPS;
        private ParticleSystem.EmissionModule _snowEmission;
        private ParticleSystem.MainModule _snowMain;

        // Splash system (rain hitting ground)
        private ParticleSystem _splashPS;
        private ParticleSystem.EmissionModule _splashEmission;

        private const int MAX_RAIN_RATE = 3000;
        private const int MAX_SNOW_RATE = 1500;
        private const int MAX_SPLASH_RATE = 500;

        private void Start()
        {
            _rainPS = CreateRainSystem();
            _snowPS = CreateSnowSystem();
            _splashPS = CreateSplashSystem();
        }

        private void Update()
        {
            var wm = WeatherManager.Instance;
            if (wm == null) return;

            bool isSnow = wm.IsSnowBiome &&
                (wm.CurrentState == WeatherState.Snow || wm.CurrentState == WeatherState.Blizzard ||
                 wm.TargetState == WeatherState.Snow || wm.TargetState == WeatherState.Blizzard);

            float intensity = wm.Intensity;

            // Rain
            if (!isSnow && intensity > 0.01f)
            {
                _rainEmission.rateOverTime = intensity * MAX_RAIN_RATE;
                if (!_rainPS.isPlaying) _rainPS.Play();

                _splashEmission.rateOverTime = intensity * MAX_SPLASH_RATE;
                if (!_splashPS.isPlaying) _splashPS.Play();
            }
            else
            {
                _rainEmission.rateOverTime = 0;
                _splashEmission.rateOverTime = 0;
            }

            // Snow
            if (isSnow && intensity > 0.01f)
            {
                _snowEmission.rateOverTime = intensity * MAX_SNOW_RATE;
                if (!_snowPS.isPlaying) _snowPS.Play();

                // Blizzard: stronger wind
                var vel = _snowPS.velocityOverLifetime;
                vel.enabled = true;
                float windStr = wm.CurrentState == WeatherState.Blizzard ? 8f : 2f;
                vel.x = new ParticleSystem.MinMaxCurve(-windStr, windStr * 0.5f);
                vel.z = new ParticleSystem.MinMaxCurve(-windStr * 0.5f, windStr);
            }
            else
            {
                _snowEmission.rateOverTime = 0;
            }

            // Adjust rain stretch by intensity (heavier = longer streaks).
            _rainMain.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f + intensity * 0.03f);
        }

        // ── Particle System Builders ─────────────────────────────────

        private ParticleSystem CreateRainSystem()
        {
            var go = new GameObject("RainParticles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 25f;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(18f, 28f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.70f, 0.75f, 0.85f, 0.30f),
                new Color(0.80f, 0.85f, 0.92f, 0.45f));
            main.maxParticles = 8000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1.5f;
            _rainMain = main;

            // Shape: large box above player
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 1f, 40f);

            // Renderer: stretch
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.08f;
            renderer.lengthScale = 3f;
            renderer.material = CreateParticleMaterial(new Color(0.75f, 0.80f, 0.90f, 0.35f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _rainEmission = ps.emission;
            _rainEmission.rateOverTime = 0;

            // Color over lifetime: fade in then out.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.1f),
                        new GradientAlphaKey(0.5f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;

            return ps;
        }

        private ParticleSystem CreateSnowSystem()
        {
            var go = new GameObject("SnowParticles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 20f;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.92f, 0.95f, 1.0f, 0.7f),
                new Color(1.0f, 1.0f, 1.0f, 0.9f));
            main.maxParticles = 5000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.15f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            _snowMain = main;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(35f, 1f, 35f);

            // Velocity over lifetime for wind drift.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(-2f, 2f);
            vel.z = new ParticleSystem.MinMaxCurve(-1f, 1f);

            // Noise for gentle swirl.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.5f;
            noise.scrollSpeed = 0.3f;
            noise.octaveCount = 2;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = CreateParticleMaterial(new Color(0.95f, 0.97f, 1.0f, 0.80f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _snowEmission = ps.emission;
            _snowEmission.rateOverTime = 0;

            // Fade over lifetime.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.15f),
                        new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            // Size over lifetime — grow slightly then shrink (snowflake tumble).
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.3f)));

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
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.80f, 0.85f, 0.95f, 0.30f),
                new Color(0.90f, 0.92f, 0.97f, 0.50f));
            main.maxParticles = 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.8f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(30f, 0.5f, 30f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = CreateParticleMaterial(new Color(0.85f, 0.88f, 0.95f, 0.25f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _splashEmission = ps.emission;
            _splashEmission.rateOverTime = 0;

            return ps;
        }

        // ── Material ─────────────────────────────────────────────────

        private static Material CreateParticleMaterial(Color color)
        {
            // Try URP particle shaders first, then fallback.
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);

            // Set blend mode to additive for soft rain look.
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetFloat("_Blend", 0f);   // Alpha
            }
            mat.renderQueue = 3000;
            return mat;
        }
    }
}
