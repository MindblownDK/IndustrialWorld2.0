// Assets/Scripts/VoxelEngine/Networks/DataPipeSelection.cs

using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Networks
{
    /// <summary>
    /// Global selection state for the Data Pipe shape variant and straight
    /// length (14.41.0). Mirrors EnergyPipeSelection - the data pipe shares
    /// the energy pipe's nine fitting shapes but keeps its OWN selection, so
    /// swapping between the two pipe items never forgets either choice.
    /// </summary>
    public static class DataPipeSelection
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
                case EnergyPipeVariant.Straight:      return $"STRAIGHT [{StraightLength}m]";
                case EnergyPipeVariant.BendRight:     return "90° ELBOW (RIGHT)";
                case EnergyPipeVariant.BendUp:        return "90° RISER (UP)";
                case EnergyPipeVariant.StepUp:        return "VERTICAL STEP (UP)";
                case EnergyPipeVariant.StepRight:     return "HORIZONTAL S-CURVE";
                case EnergyPipeVariant.BendLeftToUp:  return "LEFT → UP BEND";
                case EnergyPipeVariant.BendRightToUp: return "RIGHT → UP BEND";
                case EnergyPipeVariant.Junction4Way:  return "4-WAY JUNCTION";
                case EnergyPipeVariant.Junction6Way:  return "6-WAY 3D HUB";
                default:                              return v.ToString().ToUpperInvariant();
            }
        }
    }
}
