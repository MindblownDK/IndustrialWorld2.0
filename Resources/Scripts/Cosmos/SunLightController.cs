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

        private void Update()
        {
            _frame++;
            if (updateEveryNFrames > 0 && _frame % updateEveryNFrames != 0) return;

            var registry = CosmicRegistry.Instance;
            var body = GravityProvider.ActiveBody;
            if (registry == null || !registry.IsReady || body == null || sunLight == null) return;

            // Find the active body's cosmic position to compute the sun direction.
            Vector3 bodyKmPos = Vector3.zero;
            for (int i = 0; i < registry.Bodies.Count; i++)
            {
                if (registry.Bodies[i].settings == body.settings)
                {
                    bodyKmPos = registry.Bodies[i].positionKm;
                    break;
                }
            }

            if (registry.Sun == null) return;

            // Direction from the BODY toward the SUN (cosmic space) → world-space sun direction.
            Vector3 sunDirKm = registry.Sun.positionKm - bodyKmPos;
            if (sunDirKm.sqrMagnitude < 1f) return;
            Vector3 sunDir = sunDirKm.normalized;

            // Orient the directional light: its forward points AWAY from the sun (Unity lights
            // emit along -forward), so we look FROM the sun TOWARD the planet.
            sunLight.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);

            // Day/night intensity: how high is the sun above the local horizon?
            // Compute the sun's elevation relative to the player's position on the sphere.
            Vector3 playerPos = body.transform.position; // approx; precise player pos below
            var pc = FindAnyObjectByType<VoxelEngine.Player.PlayerController>();
            if (pc != null) playerPos = pc.transform.position;

            Vector3 radialUp = (playerPos - body.transform.position).normalized;
            float elevation = Vector3.Dot(sunDir, radialUp); // 1 = noon, -1 = midnight

            // Smooth day/night curve.
            float dayFactor = Mathf.Clamp01(elevation * 1.5f + 0.3f);
            sunLight.intensity = baseIntensity * dayFactor;

            // Warm colour near sunrise/sunset (low elevation but positive).
            float sunsetFactor = Mathf.Clamp01(1f - Mathf.Abs(elevation) * 3f);
            sunLight.color = Color.Lerp(dayColor, sunsetColor, sunsetFactor);

            // Ambient follows the day cycle too.
            RenderSettings.ambientLight = Color.Lerp(
                new Color(0.02f, 0.03f, 0.08f, 1f),  // deep night ambient
                ambientColor,                          // day ambient
                dayFactor);
            RenderSettings.ambientIntensity = Mathf.Lerp(0.2f, 1f, dayFactor);
            
            // Add a subtle "moonlight" blue tint to shadows during the night
            if (dayFactor < 0.2f)
            {
                RenderSettings.ambientLight += new Color(0.01f, 0.02f, 0.05f, 1f) * (1f - dayFactor);
            }
        }
    }
}
