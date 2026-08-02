// Assets/Scripts/VoxelEngine/Player/PlayerHazardService.cs
//
// Lightweight environmental hazard source used by the armor upgrade hooks. It
// deliberately reads existing celestial-body settings so heat/radiation protection
// is useful now without coupling the armor system to future reactor or room systems.

using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Player
{
    public static class PlayerHazardService
    {
        public const float HeatDamageThresholdC = 42f;
        public const float HeatRampSpanC = 30f;
        public const float MaxHeatDamagePerSecond = 3f;

        public static float HeatDamagePerSecond()
        {
            var body = GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return 0f;

            float temperature = body.settings.temperature;
            if (temperature <= HeatDamageThresholdC) return 0f;
            float severity = Mathf.Clamp01((temperature - HeatDamageThresholdC) / HeatRampSpanC);
            return MaxHeatDamagePerSecond * severity;
        }

        public static float RadiationDamagePerSecond()
        {
            var body = GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return 0f;
            return Mathf.Max(0f, body.settings.radiationLevel);
        }
    }
}
