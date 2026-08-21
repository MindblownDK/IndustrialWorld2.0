// Assets/Scripts/VoxelEngine/WaterSim/LiquidPhysics.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║               LIQUID PHYSICS — every liquid flows differently        ║
// ║                                                                      ║
// ║  Per-liquid flow parameters consumed by FluidSimJob (Burst-safe      ║
// ║  constants):
// ║                                                                      ║
// ║   • MaxFall        — gravity throughput per tick (water pours,       ║
// ║                      heavy fuel oil oozes)                           ║
// ║   • HorizontalStep — spread rate (thin fuels run, tar stays put)     ║
// ║   • DensityRank    — 0 lightest … 6 heaviest. Full cells swap        ║
// ║                      vertically until heavier liquids sit BELOW      ║
// ║                      lighter ones — real layering: fuel floats on    ║
// ║                      water, water floats on crude.                   ║
// ╚══════════════════════════════════════════════════════════════════════╝
using VoxelEngine.Items;

namespace VoxelEngine.WaterSim
{
    public static class LiquidPhysics
    {
        // Burst-friendly constants (kept in sync with the switch in FluidSimJob).
        public const int FuelMaxFall      = 150;
        public const int RefinedMaxFall   = 120;
        public const int MgoMaxFall       = 110;
        public const int WaterMaxFall     = 255;
        public const int CoolantMaxFall   = 200;
        public const int HfoMaxFall       = 24;
        public const int CrudeMaxFall     = 20;

        // 9.16.0 flow remake — spread steps raised so water runs like water: the flow
        // front advances a full cell per tick and pools re-level within a second of an edit.
        public const int FuelHorizontalStep      = 80;
        public const int RefinedHorizontalStep   = 48;
        public const int MgoHorizontalStep       = 40;
        public const int WaterHorizontalStep     = 96;
        public const int CoolantHorizontalStep   = 84;
        public const int HfoHorizontalStep       = 4;
        public const int CrudeHorizontalStep     = 2;

        // Density layering ranks (0 = lightest, 6 = heaviest).
        public const byte RankLiquidFuel  = 0;
        public const byte RankRefinedOil  = 1;
        public const byte RankMgo         = 2;
        public const byte RankWater       = 3;
        public const byte RankCoolant     = 4;
        public const byte RankHfo         = 5;
        public const byte RankCrudeOil    = 6;

        /// <summary>Density rank for a liquid (same order as FluidSimJob's lookup).</summary>
        public static byte DensityRank(LiquidType t) => t switch
        {
            LiquidType.LiquidFuel          => RankLiquidFuel,
            LiquidType.RefinedOil          => RankRefinedOil,
            LiquidType.MarineGasOil        => RankMgo,
            LiquidType.Water               => RankWater,
            LiquidType.MarineEngineCoolant => RankCoolant,
            LiquidType.HeavyFuelOil        => RankHfo,
            LiquidType.CrudeOil            => RankCrudeOil,
            _                              => RankWater,
        };
    }
}
