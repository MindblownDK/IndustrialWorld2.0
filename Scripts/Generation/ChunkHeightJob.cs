// Assets/Scripts/VoxelEngine/Generation/ChunkHeightJob.cs
//
// Pre-computes the surface heightmap for one chunk's footprint (CHUNK_SIZE_P × CHUNK_SIZE_P
// = 1156 columns). The 3D ChunkGenJob then samples this 2D map per voxel — orders of
// magnitude faster than calling BiomePicker.EvaluateColumn per voxel.

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Biomes;
using VoxelEngine.Core;

namespace VoxelEngine.Generation
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = false)]
    public struct ChunkHeightJob : IJobParallelFor
    {
        public int3 chunkOriginVoxels;
        public int  seed;
        public int  seaLevel;
        public int  baseHeight;
        public float continentScale;

        [ReadOnly] public NativeArray<BiomeData> biomes;

        [WriteOnly] public NativeArray<float> heights;     // CHUNK_SIZE_P²
        [WriteOnly] public NativeArray<int>   biomeIdx;    // CHUNK_SIZE_P²

        public void Execute(int idx)
        {
            const int S = VoxelConstants.CHUNK_SIZE_P;
            int x = idx % S;
            int z = idx / S;
            int wx = chunkOriginVoxels.x + x - 1;
            int wz = chunkOriginVoxels.z + z - 1;

            BiomePicker.EvaluateColumn(seed, wx, wz, baseHeight, seaLevel, continentScale,
                                       biomes, out int bi, out float h);
            heights[idx] = h;
            biomeIdx[idx] = bi;
        }
    }
}
