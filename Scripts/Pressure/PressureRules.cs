// Assets/Scripts/VoxelEngine/Pressure/PressureRules.cs
//
// Single place that decides which grid blocks hold air. Keeping the rule here means
// the room solver, the vent, the HUD and the editor tooling can never disagree.

using VoxelEngine.GridSystem;

namespace VoxelEngine.Pressure
{
    public static class PressureRules
    {
        /// <summary>Standard breathable pressure of a fully charged room (atm).</summary>
        public const float NominalPressureAtm = 1.0f;

        /// <summary>Below this a room no longer supports unassisted breathing.</summary>
        public const float BreathablePressureAtm = 0.55f;

        /// <summary>Litres of oxygen required per cubic metre for nominal pressure.</summary>
        public const float LitresPerCubicMetre = 21f;

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
