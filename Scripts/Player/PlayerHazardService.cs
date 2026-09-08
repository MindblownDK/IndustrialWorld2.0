// Assets/Scripts/VoxelEngine/Player/PlayerHazardService.cs
//
// Lightweight environmental hazard source used by the armor upgrade hooks. It
// deliberately reads existing celestial-body settings so heat/radiation protection
// is useful now without coupling the armor system to future reactor or room systems.
//
// 9.30.0: hull and plume heat moved into PlayerSuitThermal, which has real thermal
// inertia. The position-aware overload is kept for callers that want a raw "how hot
// is the hull under me" number, but PlayerStats no longer double-applies it.

using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Thermal;

namespace VoxelEngine.Player
{
    public static class PlayerHazardService
    {
        public const float HeatDamageThresholdC = 42f;
        public const float HeatRampSpanC = 30f;
        public const float MaxHeatDamagePerSecond = 3f;

        /// <summary>Extra damage per second taken while riding a hull that is burning
        /// through an atmospheric entry (raw hull hazard, not suit-mediated).</summary>
        public const float MaxHullHeatDamagePerSecond = 5f;

        /// <summary>Ambient planetary heat only (volcanic worlds), no hull contribution.</summary>
        public static float HeatDamagePerSecond() => HeatDamagePerSecond(Vector3.zero, false);

        /// <summary>
        /// Ambient planetary heat, plus — when <paramref name="usePosition"/> is set — the
        /// raw heat of a burning hull the player is standing on. PlayerStats uses the
        /// ambient-only form and lets PlayerSuitThermal handle the hull with inertia.
        /// </summary>
        public static float HeatDamagePerSecond(Vector3 worldPosition, bool usePosition)
        {
            float dps = 0f;

            var body = GravityProvider.ActiveBody;
            if (body != null && body.settings != null)
            {
                float temperature = ThermalRules.SurfaceTemperatureC(body);
                if (temperature > HeatDamageThresholdC)
                {
                    float severity = Mathf.Clamp01((temperature - HeatDamageThresholdC) / HeatRampSpanC);
                    dps += MaxHeatDamagePerSecond * severity;
                }
            }

            if (usePosition)
            {
                var thermal = ThermalService.NearestTo(worldPosition, 40f);
                if (thermal != null && thermal.IsBurning)
                {
                    float over = thermal.PeakTemperatureC - ThermalRules.BlockDamageThresholdC;
                    float severity = Mathf.Clamp01(over / ThermalRules.BlockDamageSpanC);
                    dps += MaxHullHeatDamagePerSecond * severity;
                }
            }

            return dps;
        }

        public static float RadiationDamagePerSecond()
        {
            var body = GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return 0f;
            return Mathf.Max(0f, body.settings.radiationLevel);
        }
    }
}
