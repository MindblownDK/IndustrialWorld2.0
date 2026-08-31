// Assets/Scripts/VoxelEngine/Weather/WeatherParticles.cs
//
// Procedurally creates rain and snow particle systems.
// Follows the player camera. Adjusts emission rate based on WeatherManager.Intensity.
// Rain = long thin billboard streaks falling fast. Snow = small round flakes drifting
// down. Splash = brief ground puffs where rain lands.
//
// Rendering follows the project's PROVEN particle path (see SpaceDustRenderer): every
// system is a Billboard with the shape drawn procedurally in the shader from the
// billboard UV. Rain additionally sets the billboard ALIGNMENT to Velocity, so the quad's
// long axis is the fall direction (radial-down on a sphere) while still being a normal
// camera-facing billboard — vertical streaks at any camera pitch, on the render path we
// know always draws. Stretch mode is deliberately NOT used: it depends on the renderer's
// own velocity read-back and rendered nothing in practice.

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

        // Diagnostic throttle + one-shot "manager missing" warning.
        private float _diagTimer;
        private bool _warnedNoManager;

        private const int MAX_RAIN_RATE = 4200;
        private const int MAX_SNOW_RATE = 1500;
        private const int MAX_SPLASH_RATE = 500;

        private void Start()
        {
            _rainPS = CreateRainSystem();
            _snowPS = CreateSnowSystem();
            _splashPS = CreateSplashSystem();
            Debug.Log($"[Weather] WeatherParticles created: rain={(_rainPS != null)} snow={(_snowPS != null)} splash={(_splashPS != null)}");
        }

        private void Update()
        {
            var wm = WeatherManager.Instance;
            if (wm == null)
            {
                if (!_warnedNoManager)
                {
                    _warnedNoManager = true;
                    Debug.LogWarning("[Weather] Rain: WeatherManager.Instance is NULL — weather particles cannot run.");
                }
                return;
            }

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

            // Heavier rain = slightly thicker, longer streaks.
            _rainMain.startSizeX = new ParticleSystem.MinMaxCurve(0.05f, 0.07f + intensity * 0.03f);
            _rainMain.startSizeZ = new ParticleSystem.MinMaxCurve(0.05f, 0.07f + intensity * 0.03f);
            _rainMain.startSizeY = new ParticleSystem.MinMaxCurve(1.5f, 2.2f + intensity * 0.8f);

            // UNCONDITIONAL heartbeat — logs every 5 s no matter what the weather is doing,
            // so we can see the full state (active? state? intensity? particles alive?)
            // instead of silently printing nothing when it isn't raining.
            _diagTimer += Time.deltaTime;
            if (_diagTimer >= 5f)
            {
                _diagTimer = 0f;
                var psr = _rainPS != null ? _rainPS.GetComponent<ParticleSystemRenderer>() : null;
                Debug.Log($"[Weather] Rain heartbeat: active={wm.IsWeatherActive} " +
                          $"state={wm.CurrentState}->{wm.TargetState} intensity={intensity:F2} " +
                          $"snow={wm.IsSnowBiome} rainPS={(_rainPS != null)} " +
                          $"playing={(_rainPS != null && _rainPS.isPlaying)} " +
                          $"alive={(_rainPS != null ? _rainPS.particleCount : 0)} " +
                          $"shader='{(psr != null && psr.material != null ? psr.material.shader.name : "NULL")}' " +
                          $"emitter={(_rainPS != null ? _rainPS.transform.position : Vector3.zero)}");
            }
        }

        // ── Particle System Builders ─────────────────────────────────

        private ParticleSystem CreateRainSystem()
        {
            var go = new GameObject("RainParticles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 32f;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.0f);
            main.startSpeed = 0f;                       // motion comes from velocityOverLifetime (radial)
            // 3D start size: X/Z = streak WIDTH, Y = streak LENGTH along the fall direction
            // (billboard alignment is Velocity). All three curves share the two-constant mode —
            // Unity rejects mixed curve modes and silently drops the module if they differ.
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.80f, 0.85f, 0.95f, 0.75f),
                new Color(0.95f, 0.97f, 1.00f, 0.95f));
            main.maxParticles = 12000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;                  // world gravity is world -Y — wrong on a sphere
            _rainMain = main;

            // Fall along the emitter's LOCAL -Y. WeatherManager rotates this transform every
            // frame so local -Y points at the planet core → rain falls radially on spherical
            // worlds. World simulation space keeps drops pinned in the world (not glued to you).
            // NOTE: all three axes must share ONE curve mode or Unity throws
            // "Particle Velocity curves must all be in the same mode" and the module fails
            // (which froze every drop at the emitter — the "rain won't fall" bug). X/Z are
            // explicit two-constant zeros so they match Y's two-constant range.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(-28f, -18f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            // Shape: large box above player
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 1f, 40f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            // Billboard (the proven path) aligned to VELOCITY: the quad's up axis follows the
            // fall direction, so streaks stay vertical at any camera pitch and foreshorten to
            // short marks when you look straight up — exactly like real rain from below.
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.Velocity;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.allowOcclusionWhenDynamic = false; // moving system — never let culling hide it
            renderer.sharedMaterial = CreateParticleMaterial(
                new Color(0.90f, 0.93f, 0.98f, 0.95f), 1f /* streak */);

            _rainEmission = ps.emission;
            _rainEmission.rateOverTime = 0;

            // Color over lifetime: visible almost immediately, strong hold, fade at the end.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.45f, 0f), new GradientAlphaKey(0.95f, 0.12f),
                        new GradientAlphaKey(0.95f, 0.70f), new GradientAlphaKey(0f, 1f) });
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
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startSpeed = 0f;                       // motion comes from velocityOverLifetime (radial)
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.92f, 0.95f, 1.0f, 0.75f),
                new Color(1.0f, 1.0f, 1.0f, 0.95f));
            main.maxParticles = 5000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;                  // world gravity is world -Y — wrong on a sphere
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            _snowMain = main;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(35f, 1f, 35f);

            // Velocity over lifetime: local -Y = toward the planet core (radial fall), plus
            // horizontal wind drift. WeatherManager keeps this transform radial-up aligned.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.y = new ParticleSystem.MinMaxCurve(-2.8f, -1.2f);
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
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sharedMaterial = CreateParticleMaterial(
                new Color(0.97f, 0.98f, 1.00f, 0.95f), 0f /* dot */);

            _snowEmission = ps.emission;
            _snowEmission.rateOverTime = 0;

            // Fade over lifetime.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.15f),
                        new GradientAlphaKey(0.85f, 0.7f), new GradientAlphaKey(0f, 1f) });
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
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSpeed = 0f;                       // motion comes from velocityOverLifetime (radial)
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.80f, 0.85f, 0.95f, 0.30f),
                new Color(0.90f, 0.92f, 0.97f, 0.50f));
            main.maxParticles = 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;                  // world gravity is world -Y — wrong on a sphere

            // Splash: a short radial puff (local +Y = away from the planet core).
            // Same-mode velocity curves as rain (explicit zero X/Z) so the module is valid.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(0.6f, 2.4f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(30f, 0.5f, 30f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = CreateParticleMaterial(new Color(0.90f, 0.93f, 0.98f, 0.55f), 0f /* dot */);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.allowOcclusionWhenDynamic = false;

            _splashEmission = ps.emission;
            _splashEmission.rateOverTime = 0;

            return ps;
        }

        // ── Material ─────────────────────────────────────────────────

        /// <summary>
        /// Builds a weather particle material on the project's proven particle shader and
        /// mirrors the SpaceDustRenderer material setup (explicit render queue, ZWrite off,
        /// cull off) so the material can never be hidden by depth or winding.
        /// </summary>
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
