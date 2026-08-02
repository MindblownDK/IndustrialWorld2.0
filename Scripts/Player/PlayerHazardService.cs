// Assets/Scripts/VoxelEngine/Player/PlayerHazardService.cs
//
// Lightweight environmental hazard source for heat and radiation. Reads the
// active celestial body's climate so the armour upgrades (Heat Tolerance /
// Radiation Shielding / Hazmat) have a real, testable effect. This is the first,
// slim slice of the roadmap's full Radiation / Heat systems (which will later add
// reactor fallout, re-entry heat, heated rooms, etc.) — it is kept additive and
// self-contained so nothing else depends on it yet.

using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Player
{
    public static class PlayerHazardService
    {
        /// <summary>Mean surface temperature (°C) at/above which environmental heat damage begins.</summary>
        public const float HeatDamageThresholdC = 42f;
        /// <summary>Temperature span (°C) over which heat ramps from 0 to full severity.</summary>
        public const float HeatRampSpanC = 30f;
        /// <summary>Maximum heat damage per second at extreme temperature.</summary>
        public const float MaxHeatDps = 3f;

        /// <summary>Environment heat damage per second at the current body position (0 when comfortable).</summary>
        public static float HeatDamagePerSecond()
        {
            var body = GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return 0f;
            float t = body.settings.temperature;
            if (t <= HeatDamageThresholdC) return 0f;
            float severity = Mathf.Clamp01((t - HeatDamageThresholdC) / HeatRampSpanC);
            return MaxHeatDps * severity;
        }

        /// <summary>Environment radiation damage per second (driven by the body's radiationLevel).</summary>
        public static float RadiationDamagePerSecond()
        {
            var body = GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return 0f;
            float r = body.settings.radiationLevel;
            return r > 0f ? r : 0f;
        }
    }
}
