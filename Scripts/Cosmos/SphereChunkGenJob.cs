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

        /// <summary>
        /// Oil-site map for LOD levels (PlanetVoxelLod). The gameplay world passes the
        /// default (uncreated) map — its oil is authored by OilReservoirDecorator instead.
        /// </summary>
        [ReadOnly] public NativeParallelHashMap<int, OilSiteData> oilSites;

        [WriteOnly] public NativeArray<Voxel> voxels;

        // Box dimensions (voxels per axis). Caller passes them so the job isn't coupled to
        // VoxelConstants (lets the preview use a small 16³ sample grid cheaply).
        public int sizeX;
        public int sizeY;
        public int sizeZ;

        // Precomputed surface column for this chunk (7.20.0): the expensive climate/biome/
        // tectonic column evaluation runs ONCE per chunk instead of once per voxel —
        // ~10–30× faster generation with no visible terrain change. See
        // SphereDensity.EvaluateChunkColumn.
        public SphereDensity.ChunkColumn column;

        // A radial deflation offset applied to LOD generation. Setting this >0 pulls the LOD
        // surface slightly inward toward the planet core, ensuring it sinks inside the higher-res
        // overlapping L0 chunks so it never visually pokes out above them.
        public float radiusOffset;

        /// <summary>
        /// Build the shared column for a chunk box. `chunkCenterWorld` is the box's radial
        /// centre in body-relative metres — the direction whose column the whole chunk uses.
        /// </summary>
        public static SphereDensity.ChunkColumn BuildColumn(
            in SphereGenParams prm,
            in NativeArray<BiomeData> biomes,
            in float3 chunkCenterWorld)
        {
            if (prm.isAsteroidBelt == 1) return default;
            float3 dir = math.normalizesafe(chunkCenterWorld, new float3(0f, 1f, 0f));
            return SphereDensity.EvaluateChunkColumn(prm, biomes, dir);
        }

        public void Execute(int index)
        {
            // index → (x,y,z) within the box.
            int x = index % sizeX;
            int y = (index / sizeX) % sizeY;
            int z = index / (sizeX * sizeY);

            float3 worldPos = originWorld + new float3(x, y, z) * voxelSize;
            float3 radial = worldPos;
            if (radiusOffset != 0f)
            {
                float sq = math.lengthsq(radial);
                if (sq > 0.0001f)
                {
                    radial = radial / math.sqrt(sq);
                    worldPos += radial * radiusOffset;
                }
            }
            voxels[index] = SphereDensity.EvaluateVoxelCached(prm, biomes, ores, worldPos, column, oilSites);
        }
    }
}
