// Assets/Scripts/VoxelEngine/Power/Wind/WindmillBase.cs
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Power.Wind
{
    public abstract class WindmillBase : PowerGenerator
    {
        [Header("Windmill Specs")]
        public float maxPowerWatts = 1000000f; // Default 1MW
        public float maxEffectiveHeight = 200f;
        public float heightCoefficient = 0.01f; // Power increase per meter
        
        protected PowerConsumer consumer; // To track internal usage if any
        protected float _currentOutput;

        protected virtual void Update()
        {
            CalculatePower();
        }

        protected void CalculatePower()
        {
            if (!isOn) { wattsPerSecond = 0; return; }

            float windSpeed = WindSystem.Instance != null ? WindSystem.Instance.GetWindSpeed() : 10f;
            float height = transform.position.y;
            
            // Height Bonus: higher = more power, capped at maxEffectiveHeight
            float heightMultiplier = 1f + (Mathf.Min(height, maxEffectiveHeight) * heightCoefficient);
            
            // Obstruction check: Cast a ray upwards. If something is directly above, efficiency drops.
            float obstructionMultiplier = 1.0f;
            if (Physics.Raycast(transform.position + Vector3.up * 2f, Vector3.up, out var hit, 50f))
            {
                obstructionMultiplier = 0.5f; // 50% efficiency if blocked
            }

            // Power formula: P = 0.5 * rho * Area * v^3 * efficiency
            // Simplified for gameplay: Base * (windSpeed/10)^3 * height * obstruction
            float speedFactor = Mathf.Pow(windSpeed / 10f, 3);
            _currentOutput = maxPowerWatts * speedFactor * heightMultiplier * obstructionMultiplier;
            
            wattsPerSecond = _currentOutput;
        }
    }
}
