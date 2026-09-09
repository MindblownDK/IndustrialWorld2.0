// Assets/Scripts/VoxelEngine/Pressure/PressureRules.cs
//
// Single place that decides which grid blocks hold air, and what the surrounding
// planet is offering. Keeping the rules here means the room solver, the vent, the
// HUD and the editor tooling can never disagree.

using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Pressure
{
    /// <summary>What the planet outside the hull is offering at a given point.</summary>
    public readonly struct AmbientAir
    {
        /// <summary>Outside pressure in atmospheres (0 = hard vacuum).</summary>
        public readonly float PressureAtm;
        /// <summary>True when that outside pressure is actually breathable oxygen.</summary>
        public readonly bool IsOxygenBearing;

        public AmbientAir(float pressureAtm, bool oxygenBearing)
        {
            PressureAtm = Mathf.Clamp(pressureAtm, 0f, 1.5f);
            IsOxygenBearing = oxygenBearing;
        }
    }

    public static class PressureRules
    {
        /// <summary>Standard breathable pressure of a fully charged room (atm).</summary>
        public const float NominalPressureAtm = 1.0f;

        /// <summary>Below this a room no longer supports unassisted breathing.</summary>
        public const float BreathablePressureAtm = 0.55f;

        /// <summary>Litres of oxygen required per cubic metre for nominal pressure.</summary>
        public const float LitresPerCubicMetre = 21f;

        /// <summary>Oxygen litres one occupant consumes per second at nominal pressure.</summary>
        public const float OxygenLitresPerOccupantPerSecond = 0.35f;

        /// <summary>Room pressure below which there is no longer enough air to burn fuel in.</summary>
        public static float CombustionAirMinAtm => VoxelEngine.Thermal.ThermalRules.CombustionAirMinAtm;

        /// <summary>Air below this stays untouched: an engine never takes a crew's last breath.</summary>
        public static float CombustionAirReserveAtm => VoxelEngine.Thermal.ThermalRules.CombustionAirReserveAtm;

        /// <summary>
        /// True when the air in a volume can support combustion at all. A sealed room
        /// needs its own charge; an open one borrows the planet, exactly like breathing.
        /// </summary>
        public static bool SupportsCombustion(GridRoom room)
            => room != null && room.CombustionAirAtm >= CombustionAirMinAtm;

        /// <summary>
        /// Samples the planet's atmosphere at a world position. A world with real air
        /// pressure equalises open rooms for free — a hull only matters where the sky
        /// is hostile, which is exactly where the tension belongs.
        /// </summary>
        public static AmbientAir SampleAmbient(Vector3 worldPosition)
        {
            var sample = AtmosphereManager.Sample(worldPosition);
            if (sample.IsInSpace || sample.AirDensity <= 0f) return new AmbientAir(0f, false);

            // Density01 is already referenced to Earth-like sea level, so it maps
            // directly onto our atmospheres scale.
            float atm = Mathf.Clamp(sample.Density01, 0f, 1.5f);

            var body = GravityProvider.ActiveBody;
            bool oxygen = body != null && body.settings != null && body.settings.HasOxygen;

            return new AmbientAir(atm, oxygen);
        }

        /// <summary>
        /// True when this block forms an airtight wall. Blocks implementing
        /// <see cref="IAirtightBlock"/> answer for themselves (doors open/close);
        /// everything else uses the structural default.
        /// </summary>
        public static bool Seals(GridBlock block)
        {
            if (block == null) return false;
            if (block is IAirtightBlock airtight) return airtight.SealsAir;
            return DefaultSeals(block);
        }

        /// <summary>
        /// Structural default: solid hull-style blocks seal, open lattice/functional
        /// hardware (thrusters, wheels, pipes, gear) does not.
        /// </summary>
        public static bool DefaultSeals(GridBlock block)
        {
            switch (block)
            {
                case GridThruster:
                case GridWheel:
                case GridLandingGear:
                case GridDrill:
                case GridGrinder:
                case GridDemolisher:
                case GridPiston:
                case GridSolarPanel:
                case GridBeacon:
                case GridWeapon:
                    return false;
            }

            // Conduits (item/gas/liquid pipes) are modelled as open frames.
            string n = block.blockName;
            if (!string.IsNullOrEmpty(n) && n.IndexOf("Pipe", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return true;
        }
    }
}
