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
        MarineEngineCoolant = 6,
        // 9.38.0-dev: fractionating-column cuts. Appended (never reordered) so the
        // int values of existing liquids — and every save that stores them — stay put.
        Lpg = 7,
        Naphtha = 8,
        Kerosene = 9,
        Diesel = 10,
        Gasoline = 11,
    }

    public static class LiquidTypeExt
    {
        public static string DisplayName(this LiquidType t) => t switch
        {
            LiquidType.Water               => "Water",
            LiquidType.CrudeOil            => "Crude Oil",
            LiquidType.RefinedOil          => "Refined Oil",
            LiquidType.LiquidFuel          => "Liquid Fuel",
            LiquidType.HeavyFuelOil        => "Heavy Fuel Oil",
            LiquidType.MarineGasOil        => "Marine Gas Oil (MGO)",
            LiquidType.MarineEngineCoolant => "Marine Engine Coolant",
            LiquidType.Lpg                 => "LPG",
            LiquidType.Naphtha             => "Naphtha",
            LiquidType.Kerosene            => "Kerosene",
            LiquidType.Diesel              => "Diesel",
            LiquidType.Gasoline            => "Gasoline",
            _                              => t.ToString(),
        };

        /// <summary>Tint used for the tank fill gauge.</summary>
        public static Color Color(this LiquidType t) => t switch
        {
            LiquidType.Water               => new Color(0.25f, 0.55f, 0.95f),
            LiquidType.CrudeOil            => new Color(0.12f, 0.10f, 0.08f),
            LiquidType.RefinedOil          => new Color(0.55f, 0.35f, 0.12f),
            LiquidType.LiquidFuel          => new Color(0.95f, 0.65f, 0.15f),
            LiquidType.HeavyFuelOil        => new Color(0.28f, 0.20f, 0.08f),
            LiquidType.MarineGasOil        => new Color(0.85f, 0.80f, 0.30f),
            LiquidType.MarineEngineCoolant => new Color(0.20f, 0.85f, 0.75f),
            LiquidType.Lpg                 => new Color(0.72f, 0.78f, 0.88f),
            LiquidType.Naphtha             => new Color(0.96f, 0.82f, 0.42f),
            LiquidType.Kerosene            => new Color(0.96f, 0.70f, 0.30f),
            LiquidType.Diesel              => new Color(0.80f, 0.48f, 0.14f),
            LiquidType.Gasoline            => new Color(0.98f, 0.74f, 0.16f),
            _                              => new Color(0.5f, 0.5f, 0.5f),
        };

        /// <summary>Density in kg per litre — drives the stored-liquid mass on the ship.</summary>
        public static float DensityKgPerL(this LiquidType t) => t switch
        {
            LiquidType.Water               => 1.0f,
            LiquidType.CrudeOil            => 1.12f,
            LiquidType.RefinedOil          => 0.82f,
            LiquidType.LiquidFuel          => 0.78f,
            LiquidType.HeavyFuelOil        => 0.96f,
            LiquidType.MarineGasOil        => 0.86f,
            LiquidType.MarineEngineCoolant => 1.05f,
            LiquidType.Lpg                 => 0.51f,
            LiquidType.Naphtha             => 0.70f,
            LiquidType.Kerosene            => 0.80f,
            LiquidType.Diesel              => 0.85f,
            LiquidType.Gasoline            => 0.74f,
            _                              => 1.0f,
        };
    }
}
