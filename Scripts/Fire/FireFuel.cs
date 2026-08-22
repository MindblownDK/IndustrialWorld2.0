// Assets/Scripts/VoxelEngine/Fire/FireFuel.cs
//
// 9.16.0 fire system (Liquids Overhaul, Part 2) — the flammability table for the 7
// liquids. Water and engine coolant never burn (they put fires OUT — a burning cell
// whose liquid is replaced by water/coolant by the fluid sim is extinguished the
// moment it happens). The fuel family burns at different speeds and spreads at
// different rates, so every pool fire reads like its liquid.
using VoxelEngine.Items;

namespace VoxelEngine.Fire
{
    public static class FireFuel
    {
        /// <summary>True when this liquid can burn at all.</summary>
        public static bool IsFlammable(LiquidType t) => Flammability01(t) > 0f;

        /// <summary>0..1 — how easily the liquid ignites and how lively it burns.</summary>
        public static float Flammability01(LiquidType t) => t switch
        {
            LiquidType.LiquidFuel     => 1.00f,
            LiquidType.RefinedOil     => 0.85f,
            LiquidType.MarineGasOil   => 0.70f,
            LiquidType.CrudeOil       => 0.50f,
            LiquidType.HeavyFuelOil   => 0.40f,
            _                         => 0f,    // water + coolant put fires out
        };

        /// <summary>Fuel consumed per second while burning (levels out of 255 per full cell).
        /// Volatile liquid fuel burns through a cell in ~30 s; heavy fuel oil smoulders
        /// for over two minutes per cell.</summary>
        public static float BurnLevelsPerSecond(LiquidType t) => t switch
        {
            LiquidType.LiquidFuel     => 8f,
            LiquidType.RefinedOil     => 6f,
            LiquidType.MarineGasOil   => 5f,
            LiquidType.CrudeOil       => 3f,
            LiquidType.HeavyFuelOil   => 2f,
            _                         => 0f,
        };

        /// <summary>Chance per fire tick (10 Hz) that a hot flame ignites ONE adjacent
        /// cell of the same family of fuel. Liquid fuel races across a lake; HFO crawls.</summary>
        public static float SpreadChancePerTick(LiquidType t) => t switch
        {
            LiquidType.LiquidFuel     => 0.20f,
            LiquidType.RefinedOil     => 0.16f,
            LiquidType.MarineGasOil   => 0.13f,
            LiquidType.CrudeOil       => 0.09f,
            LiquidType.HeavyFuelOil   => 0.05f,
            _                         => 0f,
        };
    }
}
