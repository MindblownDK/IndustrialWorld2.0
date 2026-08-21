// Assets/Scripts/VoxelEngine/WaterSim/FluidMaterialUtility.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║        FLUID MATERIAL UTILITY — all 7 liquids, save-compatible        ║
// ║                                                                      ║
// ║  The voxel keeps a single byte of fluid volume (waterLevel) and uses  ║
// ║  the material byte to distinguish WHICH liquid it is. 9.16.0 lifts   ║
// ║  that from two liquids (WaterLiquid / CrudeOil) to all seven:        ║
// ║                                                                      ║
// ║   Water · CrudeOil · RefinedOil · LiquidFuel · HeavyFuelOil ·        ║
// ║   MarineGasOil · MarineEngineCoolant                                 ║
// ║                                                                      ║
// ║  Old saves keep their exact meaning (their voxels only ever used the ║
// ║  two legacy values) — no format change, no migration.                ║
// ╚══════════════════════════════════════════════════════════════════════╝
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;

namespace VoxelEngine.WaterSim
{
    public static class FluidMaterialUtility
    {
        public const byte WaterVoxelMaterial = (byte)MaterialId.WaterVoxel;
        public const byte WaterMaterial  = (byte)MaterialId.WaterLiquid;
        public const byte OilMaterial    = (byte)MaterialId.CrudeOil;
        public const byte AirMaterial    = (byte)MaterialId.Air;

        public const byte RefinedOilMaterial   = (byte)MaterialId.RefinedOilLiquid;
        public const byte LiquidFuelMaterial   = (byte)MaterialId.LiquidFuelLiquid;
        public const byte HeavyFuelOilMaterial = (byte)MaterialId.HeavyFuelOilLiquid;
        public const byte MarineGasOilMaterial = (byte)MaterialId.MarineGasOilLiquid;
        public const byte CoolantMaterial      = (byte)MaterialId.CoolantLiquid;

        /// <summary>The material byte for each placeable liquid.</summary>
        public static byte MaterialFor(LiquidType liquid) => liquid switch
        {
            LiquidType.CrudeOil            => OilMaterial,
            LiquidType.RefinedOil          => RefinedOilMaterial,
            LiquidType.LiquidFuel          => LiquidFuelMaterial,
            LiquidType.HeavyFuelOil        => HeavyFuelOilMaterial,
            LiquidType.MarineGasOil        => MarineGasOilMaterial,
            LiquidType.MarineEngineCoolant => CoolantMaterial,
            _                              => WaterMaterial,
        };

        /// <summary>The liquid for a material byte (legacy-safe: anything unknown reads as water).</summary>
        public static LiquidType LiquidFromMaterial(byte material) => material switch
        {
            OilMaterial            => LiquidType.CrudeOil,
            RefinedOilMaterial     => LiquidType.RefinedOil,
            LiquidFuelMaterial     => LiquidType.LiquidFuel,
            HeavyFuelOilMaterial   => LiquidType.HeavyFuelOil,
            MarineGasOilMaterial   => LiquidType.MarineGasOil,
            CoolantMaterial        => LiquidType.MarineEngineCoolant,
            _                      => LiquidType.Water,   // WaterLiquid + legacy values
        };

        public static bool IsFluidMaterial(byte material)
            => material == WaterVoxelMaterial || material == WaterMaterial || material == OilMaterial
            || material == RefinedOilMaterial || material == LiquidFuelMaterial
            || material == HeavyFuelOilMaterial || material == MarineGasOilMaterial
            || material == CoolantMaterial;

        /// <summary>True for the 7 simulated liquid materials (excludes the frozen WaterVoxel solid).</summary>
        public static bool IsLiquidMaterial(byte material)
            => material == WaterMaterial || material == OilMaterial
            || material == RefinedOilMaterial || material == LiquidFuelMaterial
            || material == HeavyFuelOilMaterial || material == MarineGasOilMaterial
            || material == CoolantMaterial;

        public static bool IsFluid(Voxel voxel)
            => voxel.waterLevel > 0 && !voxel.IsSolid;

        public static LiquidType LiquidFromVoxel(Voxel voxel)
            => LiquidFromMaterial(voxel.material);

        public static bool Matches(Voxel voxel, LiquidType liquid)
        {
            if (!IsFluid(voxel)) return false;
            return LiquidFromVoxel(voxel) == liquid;
        }

        public static void SetLiquid(ref Voxel voxel, LiquidType liquid, byte level)
        {
            voxel.density = voxel.density > VoxelConstants.ISO_LEVEL ? voxel.density : (sbyte)-1;
            voxel.material = MaterialFor(liquid);
            voxel.waterLevel = level;
        }

        public static void ClearLiquid(ref Voxel voxel)
        {
            voxel.waterLevel = 0;
            if (IsFluidMaterial(voxel.material)) voxel.material = AirMaterial;
        }
    }
}
