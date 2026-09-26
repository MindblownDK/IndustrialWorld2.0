// Assets/Scripts/VoxelEngine/Power/EnergyPipeSelection.cs

using UnityEngine;

namespace VoxelEngine.Power
{
    /// <summary>
    /// Global selection state for the Energy Pipe shape variant and straight length.
    /// Remembers the player's active choice across tool and inventory switches.
    /// </summary>
    public static class EnergyPipeSelection
    {
        public static EnergyPipeVariant Variant { get; set; } = EnergyPipeVariant.Straight;
        public static int StraightLength { get; set; } = 1;

        public static void AdjustLength(int delta)
        {
            StraightLength = Mathf.Clamp(StraightLength + delta, 1, 5);
        }

        public static string GetVariantDisplayName(EnergyPipeVariant v)
        {
            switch (v)
            {
                case EnergyPipeVariant.Straight:
                    return $"STRAIGHT [{StraightLength}m]";
                case EnergyPipeVariant.BendRight:
                    return "90° ELBOW (RIGHT)";
                case EnergyPipeVariant.BendUp:
                    return "90° RISER (UP)";
                case EnergyPipeVariant.StepUp:
                    return "VERTICAL STEP (UP)";
                case EnergyPipeVariant.StepRight:
                    return "HORIZONTAL S-CURVE";
                case EnergyPipeVariant.BendLeftToUp:
                    return "LEFT → UP BEND";
                case EnergyPipeVariant.BendRightToUp:
                    return "RIGHT → UP BEND";
                case EnergyPipeVariant.Junction4Way:
                    return "4-WAY JUNCTION";
                case EnergyPipeVariant.Junction6Way:
                    return "6-WAY 3D HUB";
                default:
                    return v.ToString().ToUpperInvariant();
            }
        }
    }
}
