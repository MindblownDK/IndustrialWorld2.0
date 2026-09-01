// Assets/Scripts/VoxelEngine/Weather/WeatherParticles.cs
//
// Procedurally creates rain and snow particle systems.
// Follows the player camera. Adjusts emission rate based on WeatherManager.Intensity.
// Rain = long thin billboard streaks falling fast. Snow = small round flakes drifting
// down. Splash = brief ground puffs where rain lands.
//
// Rendering follows the project's PROVEN particle path (see SpaceDustRenderer): every
// system is a plain camera-facing Billboard with the shape drawn procedurally in the
// shader from the billboard UV. The rain STREAK direction is not left to Unity: the
// world-space fall vector is published as a global (_WeatherFallDir) and the shader draws
// the streak along its screen projection. Unity's Velocity alignment puts the quad's X
// axis on the velocity while the shader draws along V — a 90° mismatch that rendered rain
// as horizontal slashes. Stretch mode is not used either: it depends on the renderer's own
// velocity read-back and rendered nothing in practice.

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

        // World-space velocity modules — rewritten every frame from the body's radial down.
        private ParticleSystem.VelocityOverLifetimeModule _rainVel;
        private ParticleSystem.VelocityOverLifetimeModule _snowVel;
        private ParticleSystem.VelocityOverLifetimeModule _splashVel;
        private Vector3 _fallDir = Vector3.down;

        // Diagnostic throttle + one-shot "manager missing" warning.
        private float _diagTimer;
        private bool _warnedNoManager;

        private static readonly int IdFallDir = Shader.PropertyToID("_WeatherFallDir");

        private const float RAIN_FALL_SPEED = 23f;   // m/s straight down the radial
        private const float SNOW_FALL_SPEED = 2.2f;
        private const float SPLASH_SPEED = 1.6f;

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

            ApplyFallDirection(wm);

            bool isSnow = wm.IsSnowBiome &&
                (wm.CurrentState == WeatherState.Snow || wm.CurrentState == WeatherState.Blizzard ||
                 wm.TargetState == WeatherState.Snow || wm.TargetState == WeatherState.Blizzard);

            // LocalIntensity is the planet's precipitation scaled by how far the player is
            // under the cloud deck — above the deck it is 0, so it never rains in space.
            float intensity = wm.LocalIntensity;

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

                // Blizzard: stronger horizontal drive, applied along the SURFACE TANGENT so a
                // blizzard blows across the ground instead of tilting the fall off the radial.
                float windStr = wm.CurrentState == WeatherState.Blizzard ? 8f : 2f;
                Vector3 gust = Tangent(_fallDir) * windStr;
                Vector3 snowVelocity = _fallDir * SNOW_FALL_SPEED + gust;
                _snowVel.x = new ParticleSystem.MinMaxCurve(snowVelocity.x);
                _snowVel.y = new ParticleSystem.MinMaxCurve(snowVelocity.y);
                _snowVel.z = new ParticleSystem.MinMaxCurve(snowVelocity.z);
            }
            else
            {
                _snowEmission.rateOverTime = 0;
            }

            // Heavier rain = longer streaks.
            _rainMain.startSize = new ParticleSystem.MinMaxCurve(0.55f, 0.85f + intensity * 0.35f);

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
                          $"planet={wm.Intensity:F2} proximity={wm.SurfaceProximity:F2} " +
                          $"snow={wm.IsSnowBiome} rainPS={(_rainPS != null)} " +
                          $"playing={(_rainPS != null && _rainPS.isPlaying)} " +
                          $"alive={(_rainPS != null ? _rainPS.particleCount : 0)} " +
                          $"shader='{(psr != null && psr.material != null ? psr.material.shader.name : "NULL")}' " +
                          $"fallDir={_fallDir} " +
                          $"emitter={(_rainPS != null ? _rainPS.transform.position : Vector3.zero)}");
            }
        }

        /// <summary>
        /// Writes the world-space fall vector into every weather system. Rain and snow travel
        /// along the active body's radial DOWN at the player's position, splashes along its
        /// radial UP. Reading the direction straight from the body (instead of inheriting the
        /// emitter's rotation) is what guarantees rain falls to the ground under your feet on
        /// every face of a spherical world, at any camera angle.
        /// </summary>
        private void ApplyFallDirection(WeatherManager wm)
        {
            Vector3 anchor = wm.playerCamera != null ? wm.playerCamera.position : transform.position;
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            Vector3 up = body != null ? body.UpAt(anchor) : Vector3.up;
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            up.Normalize();

            // Written every frame (the modules are structs — no allocation, no GC) so the fall
            // direction is always the CURRENT radial down, even while walking around the sphere.
            Vector3 down = -up;
            _fallDir = down;

            // Publish the fall direction for the particle shader's streak orientation.
            Shader.SetGlobalVector(IdFallDir, new Vector4(down.x, down.y, down.z, 0f));

            Vector3 rain = down * RAIN_FALL_SPEED;
            _rainVel.x = new ParticleSystem.MinMaxCurve(rain.x);
            _rainVel.y = new ParticleSystem.MinMaxCurve(rain.y);
            _rainVel.z = new ParticleSystem.MinMaxCurve(rain.z);

            Vector3 snow = down * SNOW_FALL_SPEED;
            _snowVel.x = new ParticleSystem.MinMaxCurve(snow.x);
            _snowVel.y = new ParticleSystem.MinMaxCurve(snow.y);
            _snowVel.z = new ParticleSystem.MinMaxCurve(snow.z);

            Vector3 splash = up * SPLASH_SPEED;
            _splashVel.x = new ParticleSystem.MinMaxCurve(splash.x);
            _splashVel.y = new ParticleSystem.MinMaxCurve(splash.y);
            _splashVel.z = new ParticleSystem.MinMaxCurve(splash.z);
        }

        /// <summary>A stable horizontal direction on the surface tangent plane (for gusts).</summary>
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
            go.transform.localPosition = Vector3.up * 32f;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.0f);
            main.startSpeed = 0f;                       // motion comes from velocityOverLifetime (radial)
            // Square quad: it is the CANVAS the shader draws the streak into, so its size is
            // the streak length (the width is a shader constant). A scalar size keeps the
            // billboard perfectly camera-facing and free of any alignment ambiguity.
            main.startSize3D = false;
            main.startSize = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.80f, 0.85f, 0.95f, 0.75f),
                new Color(0.95f, 0.97f, 1.00f, 0.95f));
            main.maxParticles = 12000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;                  // world gravity is world -Y — wrong on a sphere
            _rainMain = main;

            // Fall direction is written in WORLD space every frame from the body's radial DOWN
            // (see ApplyFallDirection). It is deliberately NOT the emitter's local -Y any more:
            // that depended on the weather frame's rotation, which is rebuilt each frame from
            // the camera forward and degenerates when you look straight up/down — the exact
            // case where rain appeared to fall off to the side toward the core.
            // All three axes are single CONSTANT curves (one shared curve mode, which Unity
            // requires) so every drop falls along the same exact vector.
            _rainVel = ps.velocityOverLifetime;
            _rainVel.enabled = true;
            _rainVel.space = ParticleSystemSimulationSpace.World;
            _rainVel.x = new ParticleSystem.MinMaxCurve(0f);
            _rainVel.y = new ParticleSystem.MinMaxCurve(-RAIN_FALL_SPEED);
            _rainVel.z = new ParticleSystem.MinMaxCurve(0f);

            // Shape: large box above player
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(56f, 1f, 56f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            // Plain camera-facing billboard — the streak inside it is oriented by the shader
            // from the global fall direction, so no renderer alignment mode can rotate it.
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
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

            // Snow falls along the same world-space radial down as rain (written every frame),
            // with the swirl coming from the noise module rather than per-axis randomness —
            // per-axis ranges would tilt the fall direction differently for every flake.
            _snowVel = ps.velocityOverLifetime;
            _snowVel.enabled = true;
            _snowVel.space = ParticleSystemSimulationSpace.World;
            _snowVel.x = new ParticleSystem.MinMaxCurve(0f);
            _snowVel.y = new ParticleSystem.MinMaxCurve(-SNOW_FALL_SPEED);
            _snowVel.z = new ParticleSystem.MinMaxCurve(0f);

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

            // Splash: a short puff along the body's radial UP, written in world space every
            // frame like rain so it kicks away from the ground on every face of the sphere.
            _splashVel = ps.velocityOverLifetime;
            _splashVel.enabled = true;
            _splashVel.space = ParticleSystemSimulationSpace.World;
            _splashVel.x = new ParticleSystem.MinMaxCurve(0f);
            _splashVel.y = new ParticleSystem.MinMaxCurve(SPLASH_SPEED);
            _splashVel.z = new ParticleSystem.MinMaxCurve(0f);

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
