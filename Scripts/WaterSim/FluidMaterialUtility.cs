// Assets/Scripts/VoxelEngine/WaterSim/FluidMaterialUtility.cs
//
// Tiny shared helpers for identifying which simulated liquid a voxel contains.
// The voxel keeps a single byte of fluid volume (waterLevel) and uses material
// to distinguish water from crude oil while remaining save-compatible.

using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;

namespace VoxelEngine.WaterSim
{
    public static class FluidMaterialUtility
    {
        public const byte WaterMaterial = (byte)MaterialId.WaterLiquid;
        public const byte OilMaterial   = (byte)MaterialId.CrudeOil;
        public const byte AirMaterial   = (byte)MaterialId.Air;

        public static bool IsFluidMaterial(byte material)
            => material == WaterMaterial || material == OilMaterial;

        public static bool IsFluid(Voxel voxel)
            => voxel.waterLevel > 0 && !voxel.IsSolid;

        public static LiquidType LiquidFromVoxel(Voxel voxel)
            => voxel.material == OilMaterial ? LiquidType.CrudeOil : LiquidType.Water;

        public static byte MaterialFor(LiquidType liquid)
            => liquid == LiquidType.CrudeOil ? OilMaterial : WaterMaterial;

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
