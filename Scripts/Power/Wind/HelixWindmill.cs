// Assets/Scripts/VoxelEngine/Power/Wind/HelixWindmill.cs
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Power.Wind
{
    public class HelixWindmill : WindmillBase
    {
        public enum GeneratorSize { Small, Large }
        public enum WingSize { Small, Large }

        public GeneratorSize genSize = GeneratorSize.Small;
        public WingSize installedWingSize = WingSize.Small;
        public bool wingsInstalled = false;

        protected override void Update()
        {
            if (!wingsInstalled)
            {
                wattsPerSecond = 0;
                return;
            }

            // Efficiency logic:
            // Large wings on small gen: No extra power.
            // Small wings on large gen: Worse efficiency.
            float efficiency = 1.0f;
            if (genSize == GeneratorSize.Large && installedWingSize == WingSize.Small)
                efficiency = 0.6f;
            
            // Base power based on generator size
            float baseMax = (genSize == GeneratorSize.Large) ? maxPowerWatts : maxPowerWatts * 0.4f;

            float windSpeed = WindSystem.Instance != null ? WindSystem.Instance.GetWindSpeed() : 10f;
            float height = transform.position.y;
            float heightMultiplier = 1f + (Mathf.Min(height, maxEffectiveHeight) * heightCoefficient);
            
            float speedFactor = Mathf.Pow(windSpeed / 10f, 3);
            
            wattsPerSecond = baseMax * speedFactor * heightMultiplier * efficiency;
        }
    }
}
