// Assets/Scripts/VoxelEngine/GridSystem/LiquidType.cs
//
// Liquids that grid liquid tanks and liquid pipes can carry. A tank can be
// reconfigured to any of these from its UI (only while empty).

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public enum LiquidType
    {
        Water = 0,
        CrudeOil = 1,
        RefinedOil = 2,
        LiquidFuel = 3,
    }

    public static class LiquidTypeExt
    {
        public static string DisplayName(this LiquidType t) => t switch
        {
            LiquidType.Water      => "Water",
            LiquidType.CrudeOil   => "Crude Oil",
            LiquidType.RefinedOil => "Refined Oil",
            LiquidType.LiquidFuel => "Liquid Fuel",
            _                     => t.ToString(),
        };

        /// <summary>Tint used for the tank fill gauge.</summary>
        public static Color Color(this LiquidType t) => t switch
        {
            LiquidType.Water      => new Color(0.25f, 0.55f, 0.95f),
            LiquidType.CrudeOil   => new Color(0.12f, 0.10f, 0.08f),
            LiquidType.RefinedOil => new Color(0.55f, 0.35f, 0.12f),
            LiquidType.LiquidFuel => new Color(0.95f, 0.65f, 0.15f),
            _                     => new Color(0.5f, 0.5f, 0.5f),
        };

        /// <summary>Density in kg per litre — drives the stored-liquid mass on the ship.</summary>
        public static float DensityKgPerL(this LiquidType t) => t switch
        {
            LiquidType.Water      => 1.0f,
            LiquidType.CrudeOil   => 0.88f,
            LiquidType.RefinedOil => 0.82f,
            LiquidType.LiquidFuel => 0.78f,
            _                     => 1.0f,
        };
    }
}
