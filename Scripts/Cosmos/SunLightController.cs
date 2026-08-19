// Assets/Scripts/VoxelEngine/Cosmos/SunLightController.cs
//
// Drives the scene's directional light (the SUN) from the CosmicRegistry so it correctly
// illuminates the planet from the star's actual direction. As the planet orbits / rotates,
// the sun moves across the sky → a natural DAY / NIGHT CYCLE with no extra work.
//
// The light direction is computed from the active body's position relative to the star in
// cosmic space, so sunrise/sunset happen at the right angle on every face of the sphere.
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Positions + colours the sun directional light based on the CosmicRegistry. Attach
    /// anywhere; it finds or creates the sun light automatically.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class SunLightController : MonoBehaviour
    {
        [Tooltip("The directional light to use as the sun. Auto-found if null.")]
        public Light sunLight;

        [Tooltip("Base light intensity multiplier.")]
        public float baseIntensity = 1.3f;

        [Tooltip("Sun colour at midday.")]
        public Color dayColor = new Color(1f, 0.97f, 0.88f, 1f);

        [Tooltip("Sun colour at sunrise/sunset (warm).")]
        public Color sunsetColor = new Color(1f, 0.55f, 0.25f, 1f);

        [Tooltip("Ambient light colour (sky bounce).")]
        public Color ambientColor = new Color(0.35f, 0.42f, 0.55f, 1f);

        [Tooltip("Update the light direction every N frames (0 = every frame).")]
        public int updateEveryNFrames = 2;

        private int _frame;

        private void Awake()
        {
            if (sunLight == null) sunLight = RenderSettings.sun;
            if (sunLight == null)
            {
                // Find any directional light.
                sunLight = FindAnyObjectByType<Light>();
                if (sunLight == null || sunLight.type != LightType.Directional)
                {
                    // Create one.
                    var go = new GameObject("Sun_DirectionalLight");
                    sunLight = go.AddComponent<Light>();
                    sunLight.type = LightType.Directional;
                    sunLight.shadows = LightShadows.Soft;
                }
            }
            RenderSettings.sun = sunLight;
        }

        private Transform _player;

        /// <summary>Colour of the system's star (authored glow), cached per refresh.</summary>
        private Color StarGlow(CosmicRegistry registry)
        {
            if (registry?.Sun?.settings != null) return registry.Sun.settings.glowColor;
            return new Color(1f, 0.92f, 0.78f, 1f);
        }

        private void Update()
        {
            _frame++;
            if (updateEveryNFrames > 0 && _frame % updateEveryNFrames != 0) return;

            var registry = CosmicRegistry.Instance;
            if (registry == null || !registry.IsReady || registry.Sun == null || sunLight == null) return;

            if (_player == null)
            {
                var pc = FindAnyObjectByType<VoxelEngine.Player.PlayerController>();
                if (pc != null) _player = pc.transform;
            }

            // ── TRUE STAR DIRECTION (9.8.0): from the VIEWER's cosmic position toward
            // the star — correct on the surface, in orbit AND in deep space. ──
            var origin = SpaceOrigin.Instance;
            if (origin == null || _player == null) return;
            Unity.Mathematics.double3 viewerCosmic = origin.GetCosmicKm(_player.position);

            Unity.Mathematics.double3 toSun = registry.Sun.positionKmD - viewerCosmic;
            double lenKm = Unity.Mathematics.math.length(toSun);
            if (lenKm < 0.001) return;
            Vector3 sunDir = (Vector3)(Unity.Mathematics.float3)(toSun / lenKm);

            // Unity lights emit along -forward: look FROM the sun TOWARD the scene.
            sunLight.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);

            // The star's authored glow drives every lighting colour — different stars
            // genuinely light their worlds differently.
            Color glow = StarGlow(registry);
            var body = GravityProvider.ActiveBody;

            if (body == null)
            {
                // ── DEEP SPACE: harsh unfiltered starlight, near-black ambient. ──
                sunLight.intensity = baseIntensity * 1.2f;
                sunLight.color = Color.Lerp(glow, Color.white, 0.8f);
                sunLight.shadowStrength = 1f;
                RenderSettings.ambientLight = new Color(0.012f, 0.014f, 0.022f, 1f);
                RenderSettings.ambientIntensity = 0.12f;
                return;
            }

            // ── ON / NEAR A BODY: day-night cycle from the sun's true elevation. ──
            Vector3 playerPos = _player != null ? _player.position : body.transform.position;
            Vector3 radialUp = (playerPos - body.transform.position).normalized;
            float elevation = Vector3.Dot(sunDir, radialUp); // 1 = noon, -1 = midnight
            float dayFactor = Mathf.Clamp01(elevation * 1.5f + 0.3f);

            // Above the atmosphere the day/night curve fades out — orbit gets the same
            // harsh space light as deep space, blended smoothly on ascent.
            float altitude = Vector3.Distance(playerPos, body.transform.position) - body.SurfaceRadius;
            float atmoTop = Mathf.Max(500f, body.AtmosphereHeight);
            float spaceBlend = Mathf.Clamp01((altitude - atmoTop * 0.6f) / Mathf.Max(1f, atmoTop * 0.8f));

            sunLight.intensity = Mathf.Lerp(baseIntensity * dayFactor, baseIntensity * 1.2f, spaceBlend);
            sunLight.shadowStrength = Mathf.Lerp(Mathf.Lerp(0.6f, 0.95f, dayFactor), 1f, spaceBlend);

            // Planet-specific palette when the sky controller is live; the star glow
            // tints everything either way.
            Color localDay = dayColor;
            Color localSunset = sunsetColor;
            Color localAmbient = ambientColor;
            Color localNight = new Color(0.02f, 0.03f, 0.08f, 1f);
            var sky = PlanetSkyController.Instance;
            if (sky != null)
            {
                localDay = sky.CurrentPalette.SunDay;
                localSunset = sky.CurrentPalette.SunSunset;
                localAmbient = sky.CurrentPalette.AmbientDay;
                localNight = sky.CurrentPalette.AmbientNight;
            }
            localDay = Color.Lerp(localDay, glow, 0.25f);

            // Warm colour near sunrise/sunset (low elevation), stronger ramp.
            float sunsetFactor = Mathf.Clamp01(1f - Mathf.Abs(elevation) * 2.4f);
            Color surfaceSun = Color.Lerp(localDay, localSunset, sunsetFactor * sunsetFactor * 1.15f);
            sunLight.color = Color.Lerp(surfaceSun, Color.Lerp(glow, Color.white, 0.8f), spaceBlend);

            // Ambient follows the day cycle, dimming toward space with altitude.
            Color surfaceAmbient = Color.Lerp(localNight, localAmbient, dayFactor);
            if (dayFactor < 0.2f)
                surfaceAmbient += new Color(0.01f, 0.02f, 0.05f, 1f) * (1f - dayFactor);
            RenderSettings.ambientLight = Color.Lerp(surfaceAmbient, new Color(0.012f, 0.014f, 0.022f, 1f), spaceBlend);
            RenderSettings.ambientIntensity = Mathf.Lerp(Mathf.Lerp(0.25f, 1f, dayFactor), 0.12f, spaceBlend);
        }
    }
}
