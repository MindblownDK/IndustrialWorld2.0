// Assets/Scripts/VoxelEngine/Generation/ChunkGenJob.cs
//
// Per-voxel job. Reads the precomputed heightmap (from ChunkHeightJob) so we don't
// re-evaluate the (now expensive) blurred biome heights for every Y voxel.

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Biomes;
using VoxelEngine.Core;
using VoxelEngine.Materials;

namespace VoxelEngine.Generation
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = false)]
    public struct ChunkGenJob : IJobParallelFor
    {
        public int3 chunkOriginVoxels;
        public int  seed;

        public int   seaLevel;
        public int   baseHeight;
        public float continentScale;
        public int   crustDepth;

        [ReadOnly] public NativeArray<BiomeData> biomes;
        [ReadOnly] public NativeArray<OreLayer> ores;

        // Precomputed (XZ) heightmap and biome index — same size CHUNK_SIZE_P^2.
        [ReadOnly] public NativeArray<float> heights;
        [ReadOnly] public NativeArray<int>   biomeIdx;

        [WriteOnly] public NativeArray<Voxel> voxels;

        public void Execute(int index)
        {
            const int S = VoxelConstants.CHUNK_SIZE_P;
            int x = index % S;
            int y = (index / S) % S;
            int z = index / (S * S);

            int wx = chunkOriginVoxels.x + x - 1;
            int wy = chunkOriginVoxels.y + y - 1;
            int wz = chunkOriginVoxels.z + z - 1;

            // Look up the precomputed surface height for this column.
            int columnIdx = x + z * S;
            float surface = heights[columnIdx];
            int   biomeI  = biomeIdx[columnIdx];

            float density = surface - wy;

            // Caves: only carve where reasonably deep, not too close to bedrock,
            // and NOT below sea level (prevents holes in the ocean floor).
            if (wy < surface - 6 && wy > 4 && wy > seaLevel - 5)
            {
                float cave = NoiseUtility.FBM(new float3(wx, wy * 2f, wz) * 0.045f, 3, 2.1f, 0.55f);
                if (cave > 0.55f)
                    density -= (cave - 0.55f) * 50f;
            }


            byte material;
            sbyte densityByte;

            // Bedrock floor (unbreakable bottom layer).
            if (wy <= 2) { density = 127f; material = (byte)MaterialId.Bedrock; densityByte = 127; voxels[index] = new Voxel(densityByte, material, 0); return; }

            // Force solid below sea level to prevent ocean floor holes at biome boundaries.
            if (density <= 0f && wy < seaLevel - 2 && wy < surface - 1)
                density = math.max(1f, (seaLevel - wy) * 0.5f);

            if (density > 0f)
            {
                int depth = (int)math.round(surface) - wy;
                var biome = biomes[biomeI];

                material = (byte)MaterialId.Stone;

                if (biome.allowBeach == 1 && wy >= seaLevel - 1 && wy <= seaLevel + 2 && depth < 4)
                {
                    material = (byte)MaterialId.Sand;
                }
                else if (depth < biome.surfaceDepth)
                {
                    material = biome.surfaceMat;
                }
                else if (depth < biome.surfaceDepth + biome.subsurfaceDepth)
                {
                    material = biome.subsurfaceMat;
                }
                else if (depth < crustDepth)
                {
                    material = (byte)MaterialId.Stone;
                }

                if (wy < seaLevel && depth < 2 && biome.isOceanic == 1)
                    material = (byte)MaterialId.Sand;

                if (depth >= biome.surfaceDepth + 1)
                {
                    for (int i = 0; i < ores.Length; i++)
                    {
                        var ore = ores[i];
                        if (depth < ore.minDepth || depth > ore.maxDepth) continue;
                        float n = noise.snoise(new float3(wx, wy, wz) * ore.scale + i * 17.31f);
                        float t = ore.threshold * 2f - 1f;
                        if (n > t)
                        {
                            material = (byte)ore.material;
                            break;
                        }
                    }
                }

                densityByte = (sbyte)math.clamp(density, 1f, 127f);
            }
            else
            {
                if (wy <= seaLevel)
                {
                    // Water: negative density (invisible to SurfaceNets).
                    // waterLevel=255 so our fluid system renders + simulates it.
                    material = (byte)MaterialId.WaterLiquid;
                    densityByte = -1;
                    voxels[index] = new Voxel(densityByte, material, 255);
                    return;
                }
                else
                {
                    material = (byte)MaterialId.Air;
                    densityByte = (sbyte)math.clamp(density, -127f, -1f);
                }
            }

            voxels[index] = new Voxel(densityByte, material, 0);
        }
    }
}
