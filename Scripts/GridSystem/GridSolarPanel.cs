// Assets/Scripts/VoxelEngine/GridSystem/GridSolarPanel.cs
//
// Solar panel that generates power based on sunlight exposure.
// Works on both ground placements and ships/vehicles.
//
// Phase 5: now requires LINE-OF-SIGHT to the sun via the CosmicRegistry. On a spherical
// planet, the sun is occluded by the planet itself on the night side → solar panels produce
// ZERO power at night. This is checked via a ray-vs-sphere occlusion test (cheap, no physics).

using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.GridSystem
{
    public class GridSolarPanel : GridBlock
    {
        [Header("Solar")]
        [Tooltip("Maximum power output in full sunlight (W).")]
        public float maxOutput = 400f;

        [Tooltip("Panel normal direction check interval (seconds).")]
        public float checkInterval = 1f;

        private float _currentOutput;
        private float _timer;

        public override float PowerOutput => Enabled ? _currentOutput : 0f;

        /// <summary>Current wattage being generated right now (0 when disabled).</summary>
        public float CurrentOutput => PowerOutput;

        /// <summary>0..1 efficiency = current output ÷ rated max (sunlight × weather).</summary>
        public float Efficiency01 => maxOutput > 0f ? Mathf.Clamp01(PowerOutput / maxOutput) : 0f;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < checkInterval) return;
            _timer = 0;

            float sunDot = 0f;
            Vector3 sunDir = GetSunDirection();

            if (sunDir.sqrMagnitude > 0.01f)
            {
                Vector3 panelNormal = transform.up;
                sunDot = Mathf.Clamp01(Vector3.Dot(panelNormal, sunDir));

                // LINE-OF-SIGHT to the sun: on a sphere, the planet itself blocks the sun on the
                // night side. Check if the sun is below the local horizon (occluded by the planet).
                if (!CanSeeSun(sunDir))
                    sunDot = 0f;

                // Also check for local obstructions (buildings, terrain).
                if (sunDot > 0f && Physics.Raycast(transform.position + transform.up * 0.3f, sunDir, 100f))
                    sunDot *= 0.1f; // shadowed by a nearby object
            }

            // Weather affects output.
            var wm = Weather.WeatherManager.Instance;
            float weatherMult = 1f;
            if (wm != null)
            {
                weatherMult = wm.CurrentState switch
                {
                    Weather.WeatherState.Overcast   => 0.5f,
                    Weather.WeatherState.LightRain  => 0.3f,
                    Weather.WeatherState.HeavyRain  => 0.1f,
                    Weather.WeatherState.Snow       => 0.4f,
                    Weather.WeatherState.Blizzard   => 0.05f,
                    _ => 1f
                };
            }

            _currentOutput = maxOutput * sunDot * weatherMult;
        }

        /// <summary>
        /// Get the current sun direction from the CosmicRegistry (the actual star position
        /// relative to the active body). Falls back to the scene's directional light.
        /// </summary>
        private Vector3 GetSunDirection()
        {
            var registry = CosmicRegistry.Instance;
            var body = GravityProvider.ActiveBody;
            if (registry != null && registry.IsReady && body != null && registry.Sun != null)
            {
                // Find the active body's cosmic position.
                Vector3 bodyKmPos = Vector3.zero;
                for (int i = 0; i < registry.Bodies.Count; i++)
                {
                    if (registry.Bodies[i].settings == body.settings)
                    {
                        bodyKmPos = registry.Bodies[i].positionKm;
                        break;
                    }
                }
                Vector3 dir = registry.Sun.positionKm - bodyKmPos;
                if (dir.sqrMagnitude > 1f) return dir.normalized;
            }
            // Fallback: scene directional light.
            var mainLight = RenderSettings.sun;
            if (mainLight != null) return -mainLight.transform.forward;
            return Vector3.zero;
        }

        /// <summary>
        /// Check if the sun is visible from this position (not occluded by the planet itself).
        /// On a sphere, the planet blocks the sun when it's below the local horizon. We test
        /// this by checking if the sun direction has a positive component along the radial up.
        /// </summary>
        private bool CanSeeSun(Vector3 sunDir)
        {
            var body = GravityProvider.ActiveBody;
            if (body == null) return true; // flat world: no planet occlusion

            // Radial up at the panel's position.
            Vector3 radialUp = body.UpAt(transform.position);
            // If the sun is below the horizon (dot < 0), the planet is in the way.
            float horizonDot = Vector3.Dot(sunDir, radialUp);
            return horizonDot > -0.05f; // small tolerance for grazing angles
        }
    }
}
