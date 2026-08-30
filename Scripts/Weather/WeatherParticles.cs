// Assets/Scripts/VoxelEngine/Weather/WeatherParticles.cs
//
// Procedurally creates rain and snow particle systems.
// Follows the player camera. Adjusts emission rate based on WeatherManager.Intensity.
// Rain = long billboard streaks falling fast. Snow = small round particles floating down.

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

        // Procedural shape textures (rain streak + soft snow dot). Owned here and
        // destroyed with the component so repeated setup runs never leak them.
        private Texture2D _rainStreakTex;
        private Texture2D _snowDotTex;

        private const int MAX_RAIN_RATE = 4200;
        private const int MAX_SNOW_RATE = 1500;
        private const int MAX_SPLASH_RATE = 500;

        private void Start()
        {
            _rainStreakTex = GenerateStreakTexture();
            _snowDotTex = GenerateSoftDotTexture();
            _rainPS = CreateRainSystem();
            _snowPS = CreateSnowSystem();
            _splashPS = CreateSplashSystem();
        }

        private void OnDestroy()
        {
            if (_rainStreakTex != null) Destroy(_rainStreakTex);
            if (_snowDotTex != null) Destroy(_snowDotTex);
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

            // Adjust rain streak thickness/length by intensity (heavier = thicker, longer).
            _rainMain.startSizeX = new ParticleSystem.MinMaxCurve(0.04f, 0.07f + intensity * 0.03f);
            _rainMain.startSizeY = new ParticleSystem.MinMaxCurve(1.6f, 2.0f + intensity * 0.8f);
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
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.0f);
            main.startSpeed = 0f;                       // motion comes from velocityOverLifetime (radial)
            // 3D start size: thin (X) and LONG (Y). The streak length is baked into the
            // billboard's height via a vertical streak texture, so rain reads as falling
            // lines without relying on Stretch render mode (which is unreliable in URP).
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.72f, 0.78f, 0.90f, 0.55f),
                new Color(0.92f, 0.95f, 1.00f, 0.85f));
            main.maxParticles = 12000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;                  // world gravity is world -Y — wrong on a sphere
            _rainMain = main;

            // Fall along the emitter's LOCAL -Y. WeatherManager rotates this transform every
            // frame so local -Y points at the planet core → rain falls radially on spherical
            // worlds. World simulation space keeps drops pinned in the world (not glued to you).
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.y = new ParticleSystem.MinMaxCurve(-28f, -18f);

            // Shape: large box above player
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 1f, 40f);

            // Renderer: BILLBOARD with a vertical streak texture. Stretch mode proved
            // unreliable (velocityScale tweaks left rain invisible across two rounds), so
            // the streak length is baked into the texture + 3D start size instead — the
            // same bulletproof billboard path the (visible) splash system already uses.
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = CreateParticleMaterial(new Color(0.85f, 0.89f, 0.97f, 0.90f), _rainStreakTex);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _rainEmission = ps.emission;
            _rainEmission.rateOverTime = 0;

            // Color over lifetime: quick fade-in, strong hold, quick fade-out — streaks
            // must stay BRIGHT through their whole fall to read against the sky.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.65f, 0.08f),
                        new GradientAlphaKey(0.90f, 0.75f), new GradientAlphaKey(0f, 1f) });
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
            main.startSpeed = 0f;                       // motion comes from velocityOverLifetime (radial)
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.92f, 0.95f, 1.0f, 0.7f),
                new Color(1.0f, 1.0f, 1.0f, 0.9f));
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
            renderer.material = CreateParticleMaterial(new Color(0.97f, 0.98f, 1.00f, 0.95f), _snowDotTex);
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
            main.startSpeed = 0f;                       // motion comes from velocityOverLifetime (radial)
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.80f, 0.85f, 0.95f, 0.30f),
                new Color(0.90f, 0.92f, 0.97f, 0.50f));
            main.maxParticles = 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;                  // world gravity is world -Y — wrong on a sphere

            // Splash: a short radial puff (local +Y = away from the planet core).
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.y = new ParticleSystem.MinMaxCurve(0.6f, 2.4f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(30f, 0.5f, 30f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = CreateParticleMaterial(new Color(0.90f, 0.93f, 0.98f, 0.55f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _splashEmission = ps.emission;
            _splashEmission.rateOverTime = 0;

            return ps;
        }

        // ── Material & Textures ───────────────────────────────────────

        private static Material CreateParticleMaterial(Color color, Texture2D shape = null)
        {
            // Project-authored transparent particle shader (fog-aware, true alpha blend).
            var shader = Shader.Find("VoxelEngine/WeatherParticlesURP")
                      ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "WeatherParticleMat" };
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", color);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (shape != null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", shape);
            mat.color = color;
            return mat;
        }

        /// <summary>
        /// Vertical rain-streak alpha. Bright down the spine, soft on the sides, fading
        /// out at both tips so the streak has no hard caps. Stretched tall by the 3D
        /// start size, this turns an ordinary billboard into a falling rain line.
        /// </summary>
        private static Texture2D GenerateStreakTexture(int size = 16)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "WeatherRainStreak",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                float endFade = Mathf.Clamp01(Mathf.Min(v, 1f - v) * 3.2f);   // soft tips
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float dx = (u - 0.5f) * 2f;                                 // -1..1
                    float cross = Mathf.Clamp01(1f - dx * dx * 7f);             // bright spine
                    px[y * size + x] = new Color(1f, 1f, 1f, cross * endFade);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>Soft round snowflake alpha — bright centre fading to a clean edge.</summary>
        private static Texture2D GenerateSoftDotTexture(int size = 16)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "WeatherSnowDot",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float dx = (u - 0.5f) * 2f;
                    float dy = (v - 0.5f) * 2f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d * 2.2f));
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }
    }
}
