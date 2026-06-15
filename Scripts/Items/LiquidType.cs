// Assets/Scripts/VoxelEngine/GridSystem/LiquidType.cs
//
// Liquids that grid liquid tanks and liquid pipes can carry. A tank can be
// reconfigured to any of these from its UI (only while empty).

using UnityEngine;

namespace VoxelEngine.Items
{
    public enum LiquidType
    {
        Water = 0,
        CrudeOil = 1,
        RefinedOil = 2,
        LiquidFuel = 3,
        HeavyFuelOil = 4,
        MarineGasOil = 5,
    }

    public static class LiquidTypeExt
    {
        public static string DisplayName(this LiquidType t) => t switch
        {
            LiquidType.Water         => "Water",
            LiquidType.CrudeOil      => "Crude Oil",
            LiquidType.RefinedOil    => "Refined Oil",
            LiquidType.LiquidFuel    => "Liquid Fuel",
            LiquidType.HeavyFuelOil  => "Heavy Fuel Oil",
            LiquidType.MarineGasOil  => "Marine Gas Oil (MGO)",
            _                        => t.ToString(),
        };

        /// <summary>Tint used for the tank fill gauge.</summary>
        public static Color Color(this LiquidType t) => t switch
        {
            LiquidType.Water         => new Color(0.25f, 0.55f, 0.95f),
            LiquidType.CrudeOil      => new Color(0.12f, 0.10f, 0.08f),
            LiquidType.RefinedOil    => new Color(0.55f, 0.35f, 0.12f),
            LiquidType.LiquidFuel    => new Color(0.95f, 0.65f, 0.15f),
            LiquidType.HeavyFuelOil  => new Color(0.28f, 0.20f, 0.08f),
            LiquidType.MarineGasOil  => new Color(0.85f, 0.80f, 0.30f),
            _                        => new Color(0.5f, 0.5f, 0.5f),
        };

        /// <summary>Density in kg per litre — drives the stored-liquid mass on the ship.</summary>
        public static float DensityKgPerL(this LiquidType t) => t switch
        {
            LiquidType.Water         => 1.0f,
            LiquidType.CrudeOil      => 1.12f, // gameplay: crude oil is heavier and more sluggish than water
            LiquidType.RefinedOil    => 0.82f,
            LiquidType.LiquidFuel    => 0.78f,
            LiquidType.HeavyFuelOil  => 0.96f, // thick, viscous bunker fuel
            LiquidType.MarineGasOil  => 0.86f, // clean distillate — lighter than HFO
            _                        => 1.0f,
        };
    }
}
