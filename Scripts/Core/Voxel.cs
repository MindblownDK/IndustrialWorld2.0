// Assets/Scripts/VoxelEngine/Core/Voxel.cs
using System.Runtime.InteropServices;

namespace VoxelEngine.Core
{
    /// <summary>
    /// 3-byte voxel: density + material + waterLevel.
    ///   density:    signed byte (-128..127). >0 = solid, <=0 = empty.
    ///   material:   byte 0..255 (MaterialId).
    ///   waterLevel: byte 0..255. 0 = dry, 255 = fully saturated.
    /// 3 bytes * 34^3 ≈ 118 KB per padded chunk — still fits in L2.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Voxel
    {
        public sbyte density;
        public byte  material;
        public byte  waterLevel;

        public Voxel(sbyte density, byte material, byte waterLevel = 0)
        {
            this.density    = density;
            this.material   = material;
            this.waterLevel = waterLevel;
        }

        public bool IsSolid => density > VoxelConstants.ISO_LEVEL;
        public bool HasWater => waterLevel > 0;

        /// <summary>Water fill fraction 0..1.</summary>
        public float WaterFill => waterLevel / 255f;

        public static readonly Voxel Empty = new Voxel(-127, 0, 0);
        public static readonly Voxel Solid = new Voxel( 127, 1, 0);
    }
}
