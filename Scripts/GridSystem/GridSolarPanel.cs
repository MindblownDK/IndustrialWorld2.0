// Assets/Scripts/VoxelEngine/GridSystem/GridSolarPanel.cs
//
// Solar panel that generates power based on sunlight exposure.
// Works on both ground placements and ships/vehicles.

using UnityEngine;

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

            // Check sunlight — raycast upward, check angle to sun.
            float sunDot = 0;
            var mainLight = RenderSettings.sun;
            if (mainLight != null)
            {
                Vector3 sunDir = -mainLight.transform.forward;
                Vector3 panelNormal = transform.up;
                sunDot = Mathf.Clamp01(Vector3.Dot(panelNormal, sunDir));

                // Check if anything blocks the sun.
                if (Physics.Raycast(transform.position + transform.up * 0.3f, sunDir, 100f))
                    sunDot *= 0.1f; // shadowed
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
    }
}
