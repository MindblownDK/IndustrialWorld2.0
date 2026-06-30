// Assets/Scripts/VoxelEngine/WaterSim/VolumetricWaterData.cs
using System.Runtime.InteropServices;
using UnityEngine;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// GPU-aligned struct representing a single cell in the Spherical 3D Density Field.
    /// Matches the HLSL FluidCell layout in FluidSim.compute.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FluidCellData
    {
        public float density;     // 0.0 = air, 1.0 = full water/oil, 0.5 = surface
        public float material;    // 1.0 = Water, 2.0 = Crude Oil
        public Vector2 flow;      // Surface flow velocity (x, z)
        public int suctionFlag;   // 0 = none, 1 = suction active (pumping)

        public static readonly FluidCellData Empty = new FluidCellData { density = 0f, material = 0f, flow = Vector2.zero, suctionFlag = 0 };
        public static readonly FluidCellData FullWater = new FluidCellData { density = 1f, material = 1f, flow = Vector2.zero, suctionFlag = 0 };
    }

    /// <summary>
    /// LOD tiers for volumetric fluid simulation based on distance from camera.
    /// </summary>
    public enum WaterLodTier
    {
        FullVolumetric_60Hz = 0,  // 0 - 50m
        SWE_Gerstner_30Hz   = 1,  // 50 - 200m
        SimplifiedSWE_10Hz  = 2,  // 200 - 1000m
        StaticHeightmap_1Hz = 3   // 1000m+
    }

    /// <summary>
    /// Adaptive Sparse Storage metadata for a planet chunk.
    /// Only chunks within 1 chunk of the surface allocate active GPU buffers.
    /// Deep ocean interiors are represented as a constant "Full Water" value to conserve RAM.
    /// </summary>
    public class SparseWaterChunk
    {
        public Vector3Int chunkCoord;
        public bool isDeepInteriorConstant;
        public int bufferOffsetIndex = -1; // -1 if constant or inactive
        public WaterLodTier currentLod = WaterLodTier.StaticHeightmap_1Hz;
        public float lastUpdateTimer;
    }
}
