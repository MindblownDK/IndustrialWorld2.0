// Assets/Scripts/VoxelEngine/Biomes/BiomeData.cs
using Unity.Mathematics;
using VoxelEngine.Materials;

namespace VoxelEngine.Biomes
{
    /// <summary>
    /// Burst-friendly POD copy of BiomeDefinition. Built once on Awake, lives in a NativeArray.
    /// </summary>
    public struct BiomeData
    {
        public float2 tempRange;       // x=min, y=max
        public float2 humidRange;
        public int    priority;

        public float  heightOffset;
        public float  heightAmplitude;
        public float  heightFrequency;
        public float  ridgedness;

        public byte   surfaceMat;
        public int    surfaceDepth;
        public byte   subsurfaceMat;
        public int    subsurfaceDepth;
        public byte   allowBeach;     // 1/0
        public byte   isOceanic;      // 1/0

        public static BiomeData FromDefinition(BiomeDefinition d) => new BiomeData
        {
            tempRange       = new float2(d.minTemperature, d.maxTemperature),
            humidRange      = new float2(d.minHumidity,    d.maxHumidity),
            priority        = d.priority,
            heightOffset    = d.heightOffset,
            heightAmplitude = d.heightAmplitude,
            heightFrequency = math.max(0.0001f, d.heightFrequency),
            ridgedness      = math.saturate(d.ridgedness),
            surfaceMat      = (byte)d.surfaceMaterial,
            surfaceDepth    = math.max(0, d.surfaceDepth),
            subsurfaceMat   = (byte)d.subsurfaceMaterial,
            subsurfaceDepth = math.max(0, d.subsurfaceDepth),
            allowBeach      = (byte)(d.allowBeach ? 1 : 0),
            isOceanic       = (byte)(d.isOceanic ? 1 : 0),
        };
    }
}
