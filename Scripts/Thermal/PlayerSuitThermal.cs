// Assets/Scripts/VoxelEngine/Thermal/PlayerSuitThermal.cs
//
// The player's suit temperature. Roadmap item 12 (Player Heat UI) and the 9.30.0 fix
// for "cockpit heat drops too fast": the suit is now a real stat with thermal inertia
// instead of an instantaneous damage-per-second lookup.
//
//   • Environment target is assembled from: planetary/altitude ambient, the hottest
//     nearby hull plate of the grid the player is riding, any thruster plume the
//     player is standing in, and the climate-controlled cabin if one is sealed and
//     powered around them.
//   • The suit slews toward that target SLOWLY (heating ~0.045/s, cooling ~0.018/s).
//     Cooling is deliberately slower than heating, boosted by a live cabin or water.
//   • Above the (armor-raised) tolerance the crew takes ramping heat damage; far below
//     it, cold damage. Heat Tolerance modules add +4 °C headroom per tier on top of the
//     existing damage multiplier.
//
// One component per PlayerStats. It publishes read-only telemetry for the HUD.

using UnityEngine;
using VoxelEngine.Combat;
using VoxelEngine.Cosmos;
using VoxelEngine.Player;

namespace VoxelEngine.Thermal
{
    [DisallowMultipleComponent]
    public class PlayerSuitThermal : MonoBehaviour
    {
        /// <summary>Radius around the player within which hull plate radiates onto the suit.</summary>
        private const float HullCouplingRadius = 4.5f;

        private PlayerStats _stats;
        private PlayerEquipment _equipment;
        private PlayerWaterState _water;
        private float _refreshTimer;

        /// <summary>Current suit temperature in °C.</summary>
        public float SuitTemperatureC { get; private set; } = ThermalRules.SuitRestingTemperatureC;

        /// <summary>Environment temperature the suit is currently being pulled toward.</summary>
        public float EnvironmentTemperatureC { get; private set; } = ThermalRules.FallbackAmbientC;

        /// <summary>Largest single contributor, for HUD copy.</summary>
        public string DominantSource { get; private set; } = "AMBIENT";

        /// <summary>Installed Heat Tolerance tier (0..5).</summary>
        public int HeatToleranceTier { get; private set; }

        /// <summary>Suit temperature above which damage begins, including armor headroom.</summary>
        public float DamageThresholdC =>
            ThermalRules.SuitDamageThresholdC + ThermalRules.SuitToleranceHeadroomPerTierC * HeatToleranceTier;

        public ThermalBand Band => ThermalRules.SuitBand(SuitTemperatureC, HeatToleranceTier);

        /// <summary>True while the suit is cold enough to hurt.</summary>
        public bool IsCold => SuitTemperatureC < ThermalRules.SuitColdThresholdC;

        /// <summary>Heat damage per second currently applied (after armor), for HUD/debug.</summary>
        public float CurrentDamagePerSecond { get; private set; }

        public static PlayerSuitThermal For(PlayerStats stats)
        {
            if (stats == null) return null;
            var suit = stats.GetComponent<PlayerSuitThermal>();
            if (suit == null) suit = stats.gameObject.AddComponent<PlayerSuitThermal>();
            return suit;
        }

        private void Awake()
        {
            _stats = GetComponent<PlayerStats>();
            _equipment = GetComponent<PlayerEquipment>();
            _water = GetComponent<PlayerWaterState>();
        }

        /// <summary>
        /// Advance the suit model. Called by PlayerStats each frame so ordering with
        /// the rest of the survival tick is deterministic. Returns HP/s to apply.
        /// </summary>
        public float Tick(float dt)
        {
            if (_stats == null) _stats = GetComponent<PlayerStats>();
            if (_equipment == null) _equipment = GetComponent<PlayerEquipment>();
            if (_water == null) _water = GetComponent<PlayerWaterState>();

            // The environment sample is comparatively expensive (atmosphere, rooms,
            // grid lookups) and changes slowly, so refresh it at 5 Hz.
            _refreshTimer -= dt;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 0.2f;
                RefreshEnvironment();
            }

            HeatToleranceTier = _equipment != null ? _equipment.GetArmorUpgradeTier(ArmorUpgradeKind.HeatTolerance) : 0;

            // The body regulates toward 37 °C in a comfortable environment; a hot or cold
            // environment drags the suit away from it. Blend so a 21 °C cabin reads as a
            // comfortable 36-37 °C suit rather than a chilly 21 °C one.
            float comfortLow = 10f, comfortHigh = 32f;
            float target;
            if (EnvironmentTemperatureC >= comfortLow && EnvironmentTemperatureC <= comfortHigh)
                target = ThermalRules.SuitRestingTemperatureC;
            else if (EnvironmentTemperatureC > comfortHigh)
                target = ThermalRules.SuitRestingTemperatureC + (EnvironmentTemperatureC - comfortHigh) * 0.55f;
            else
                target = ThermalRules.SuitRestingTemperatureC - (comfortLow - EnvironmentTemperatureC) * 0.18f;

            // A sealed suit is an insulated vessel in both directions.
            bool sealedSuit = _equipment != null && _equipment.HasBreathingKit;
            float insulation = sealedSuit ? 0.7f : 1f;

            float rate;
            if (target > SuitTemperatureC)
            {
                rate = ThermalRules.SuitHeatingRatePerSecond * insulation;
            }
            else
            {
                rate = ThermalRules.SuitCoolingRatePerSecond;
                if (_inCabin) rate *= ThermalRules.SuitCabinCoolingBoost;
                if (_water != null && (_water.IsSwimming || _water.IsHeadUnderwater)) rate *= ThermalRules.SuitSubmergedCoolingBoost;
                // Warming back up from cold is body heat, not radiation: a bit quicker.
                if (target > ThermalRules.SuitRestingTemperatureC - 0.5f && SuitTemperatureC < target) rate *= 1.6f;
            }

            SuitTemperatureC = Mathf.Lerp(SuitTemperatureC, target, 1f - Mathf.Exp(-rate * dt));

            // Damage
            float dps = ThermalRules.SuitHeatDamagePerSecond(SuitTemperatureC, HeatToleranceTier);
            if (dps > 0f && _equipment != null) dps *= _equipment.HeatDamageMultiplier;
            dps += ThermalRules.SuitColdDamagePerSecond(SuitTemperatureC);
            CurrentDamagePerSecond = dps;
            return dps;
        }

        private bool _inCabin;

        private void RefreshEnvironment()
        {
            Vector3 pos = transform.position;
            float ambient = ThermalRules.AmbientTemperatureC(pos);
            float env = ambient;
            string source = "AMBIENT";

            // Sealed, charged room: climate control wins over the outside.
            var room = VoxelEngine.Pressure.RoomAtmosphereService.RoomAt(pos);
            _inCabin = room != null && room.IsBreathable;
            if (_inCabin)
            {
                env = ThermalRules.CabinTemperatureC;
                source = "CABIN";
            }

            // Hull plate around the player.
            var thermal = ThermalService.NearestTo(pos, 40f);
            if (thermal != null)
            {
                float hull = thermal.HottestTemperatureNear(pos, HullCouplingRadius);
                float excess = hull - thermal.AmbientC;
                if (excess > 40f)
                {
                    float coupling = ThermalRules.SuitHullCoupling * (_inCabin ? 0.5f : 1f);
                    float felt = env + excess * coupling;
                    if (felt > env) { env = felt; source = thermal.EntryHeatingC > 1f ? "RE-ENTRY" : "HULL"; }
                }
            }

            // Standing in an exhaust plume. Cabins do not help — the plume is outside.
            float plume = ThrusterPlumeHazard.PlayerPlumeTemperatureC;
            if (plume > 20f && !_inCabin)
            {
                float felt = ambient + plume * ThermalRules.SuitPlumeCoupling;
                if (felt > env) { env = felt; source = "PLUME"; }
            }

            // Direct planetary heat hazards (volcanic worlds) keep working through ambient.
            EnvironmentTemperatureC = env;
            DominantSource = source;
        }
    }
}
