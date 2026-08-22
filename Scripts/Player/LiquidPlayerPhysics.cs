// Assets/Scripts/VoxelEngine/Player/LiquidPlayerPhysics.cs
//
// 9.16.0 (Liquids Overhaul, Part 3) — how each of the 7 liquids treats a swimming
// player: speed, buoyancy and contact damage.
//
//   • Water          — swims naturally (the classic swim model).
//   • Coolant        — swims just fine... while it scalds you (armour-escalating burn).
//   • Liquid fuel    — floats you (it is lighter than you) and eats your skin (caustic).
//   • Refined oil    — drags, sinks you slowly.
//   • MGO            — drags a bit less, sinks a bit less.
//   • Crude oil      — thick tar: heavy drag, strong sinking.
//   • Heavy fuel oil — near-molasses: the strongest drag of all.
using VoxelEngine.Items;

namespace VoxelEngine.Player
{
    public static class LiquidPlayerPhysics
    {
        /// <summary>Swim speed multiplier (1 = water-like).</summary>
        public static float SwimSpeedScale(LiquidType t) => t switch
        {
            LiquidType.MarineEngineCoolant => 1.05f,  // water-based — swims fine (while it scalds)
            LiquidType.LiquidFuel          => 0.85f,
            LiquidType.RefinedOil          => 0.65f,
            LiquidType.MarineGasOil        => 0.70f,
            LiquidType.CrudeOil            => 0.40f,
            LiquidType.HeavyFuelOil        => 0.30f,
            _                              => 1f,    // water
        };

        /// <summary>Vertical drift while swimming (m/s). Positive = sinks, negative = floats.</summary>
        public static float BuoyancyBias(LiquidType t) => t switch
        {
            LiquidType.LiquidFuel          => -0.35f, // light fuel — the player FLOATS on it
            LiquidType.RefinedOil          =>  0.45f,
            LiquidType.MarineGasOil        =>  0.35f,
            LiquidType.CrudeOil            =>  1.05f,
            LiquidType.HeavyFuelOil        =>  0.80f,
            _                              =>  0f,    // water + coolant are neutral
        };

        /// <summary>Contact damage per second while touching the liquid (0 = harmless).</summary>
        public static float ContactDps(LiquidType t) => t switch
        {
            LiquidType.MarineEngineCoolant => 10f,  // scald (armour-escalating burn)
            LiquidType.LiquidFuel          =>  8f,  // caustic (armour-mitigated)
            _                              =>  0f,
        };
    }
}
