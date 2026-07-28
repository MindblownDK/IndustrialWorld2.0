// Assets/Scripts/VoxelEngine/Cosmos/SphereChunkGenJob.cs
//
// Burst job that fills a NativeArray<Voxel> for a box of space expressed in BODY-RELATIVE
// cartesian coordinates (origin at the planet core). It is the spherical twin of the flat
// ChunkGenJob: same responsibilities (terrain density, caves, surface materials, ores) but the
// density comes from SphereDensity (radial) instead of a heightmap.
//
// Phase 2's face-streamer will allocate one of these per spherical chunk; Phase 1 keeps it
// isolated and exercised by the authoring preview so we can validate the math + performance.
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Biomes;
using VoxelEngine.Core;
using VoxelEngine.Generation;

namespace VoxelEngine.Cosmos
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = false)]
    public struct SphereChunkGenJob : IJobParallelFor
    {
        public SphereGenParams prm;

        // Body-relative origin (metres) of the chunk box [min corner].
        public float3 originWorld;
        // Voxel edge length (metres) — usually VoxelConstants.VOXEL_SIZE.
        public float voxelSize;

        [ReadOnly] public NativeArray<BiomeData> biomes;
        [ReadOnly] public NativeArray<OreLayer>   ores;

        [WriteOnly] public NativeArray<Voxel> voxels;

        // Box dimensions (voxels per axis). Caller passes them so the job isn't coupled to
        // VoxelConstants (lets the preview use a small 16³ sample grid cheaply).
        public int sizeX;
        public int sizeY;
        public int sizeZ;

        public void Execute(int index)
        {
            // index → (x,y,z) within the box.
            int x = index % sizeX;
            int y = (index / sizeX) % sizeY;
            int z = index / (sizeX * sizeY);

            float3 worldPos = originWorld + new float3(x, y, z) * voxelSize;
            voxels[index] = SphereDensity.EvaluateVoxel(prm, biomes, ores, worldPos);
        }
    }
}
