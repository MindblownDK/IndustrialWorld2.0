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
        /// <summary>Surface-lattice resolution per axis (7³ = 343 samples per chunk).</summary>
        public const int LATTICE = 7;

        /// <summary>Lattice plane spacing (m). Planes sit on the GLOBAL 8 m grid, so
        /// neighbouring chunks sample the surface at IDENTICAL world positions — their
        /// interpolated surfaces agree exactly at shared borders (9.5.1: the per-box
        /// lattice disagreed at chunk faces and opened small seams between chunks).</summary>
        public const float LATTICE_SPACING = 8f;

        public SphereGenParams prm;

        // Body-relative origin (metres) of the chunk box [min corner].
        public float3 originWorld;
        // Voxel edge length (metres) — usually VoxelConstants.VOXEL_SIZE.
        public float voxelSize;

        [ReadOnly] public NativeArray<BiomeData> biomes;
        [ReadOnly] public NativeArray<OreLayer>   ores;

        /// <summary>
        /// Oil-site map for coarse LOD generation. The gameplay world passes the
        /// default (uncreated) map — its oil is authored by OilReservoirDecorator instead.
        /// </summary>
        [ReadOnly] public NativeParallelHashMap<int, OilSiteData> oilSites;

        [WriteOnly] public NativeArray<Voxel> voxels;

        // Box dimensions (voxels per axis). Caller passes them so the job isn't coupled to
        // VoxelConstants (lets the preview use a small 16³ sample grid cheaply).
        public int sizeX;
        public int sizeY;
        public int sizeZ;

        // Precomputed surface column for this chunk (7.20.0): climate/biome/slope
        // flavour is evaluated ONCE per chunk. 9.5.0: the surface SHAPE no longer
        // comes from the column's linear gradient — the ridged 9.x field broke that
        // approximation (hollow/filled chunks, gaps appearing exactly when chunks
        // generated). The exact surface radius is now trilinearly interpolated from
        // a 5³ per-chunk lattice built by BuildSurfaceLatticeJob.
        public SphereDensity.ChunkColumn column;

        /// <summary>Per-chunk 7³ surface-radius lattice (built by BuildSurfaceLatticeJob).</summary>
        [ReadOnly] public NativeArray<float> surfaceLattice;

        /// <summary>World-grid-snapped origin of the lattice (multiple of LATTICE_SPACING).</summary>
        public float3 latticeOrigin;

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

            if (surfaceLattice.IsCreated && surfaceLattice.Length == LATTICE * LATTICE * LATTICE)
            {
                // Exact path (9.5.x): trilinear surface radius from the WORLD-ALIGNED
                // lattice — shared plane positions across chunk borders (seam-free).
                float3 f = (originWorld + new float3(x, y, z) * voxelSize - latticeOrigin)
                           / LATTICE_SPACING;
                f = math.clamp(f, 0f, LATTICE - 1.0001f);
                int3 i0 = math.clamp((int3)math.floor(f), 0, LATTICE - 2);
                float3 t = math.saturate(f - i0);

                float s000 = surfaceLattice[LatIdx(i0.x,     i0.y,     i0.z)];
                float s100 = surfaceLattice[LatIdx(i0.x + 1, i0.y,     i0.z)];
                float s010 = surfaceLattice[LatIdx(i0.x,     i0.y + 1, i0.z)];
                float s110 = surfaceLattice[LatIdx(i0.x + 1, i0.y + 1, i0.z)];
                float s001 = surfaceLattice[LatIdx(i0.x,     i0.y,     i0.z + 1)];
                float s101 = surfaceLattice[LatIdx(i0.x + 1, i0.y,     i0.z + 1)];
                float s011 = surfaceLattice[LatIdx(i0.x,     i0.y + 1, i0.z + 1)];
                float s111 = surfaceLattice[LatIdx(i0.x + 1, i0.y + 1, i0.z + 1)];

                float surfaceRadius = math.lerp(
                    math.lerp(math.lerp(s000, s100, t.x), math.lerp(s010, s110, t.x), t.y),
                    math.lerp(math.lerp(s001, s101, t.x), math.lerp(s011, s111, t.x), t.y),
                    t.z);

                voxels[index] = SphereDensity.EvaluateVoxelWithSurface(
                    prm, biomes, ores, worldPos, surfaceRadius, column, oilSites);
                return;
            }

            voxels[index] = SphereDensity.EvaluateVoxelCached(prm, biomes, ores, worldPos, column, oilSites);
        }

        private static int LatIdx(int x, int y, int z) => x + y * LATTICE + z * LATTICE * LATTICE;
    }

    /// <summary>
    /// Small Burst prepass: evaluates the EXACT surface radius (PlanetField) at a 5³
    /// lattice spanning the chunk box. SphereChunkGenJob trilinearly interpolates it
    /// per voxel — the surface follows the true ridged field across the whole chunk
    /// (max lattice spacing ≈ 8.5 m), where the old chunk-centre linear gradient
    /// produced hollow or overfilled chunks on sharp relief.
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = false)]
    public struct BuildSurfaceLatticeJob : IJobParallelFor
    {
        public SphereGenParams prm;
        public float3 latticeOrigin;   // world-grid-snapped (multiple of LATTICE_SPACING)

        [WriteOnly] public NativeArray<float> lattice;   // LATTICE³

        public void Execute(int index)
        {
            const int L = SphereChunkGenJob.LATTICE;
            int x = index % L;
            int y = (index / L) % L;
            int z = index / (L * L);

            float3 pos = latticeOrigin + new float3(x, y, z) * SphereChunkGenJob.LATTICE_SPACING;
            float3 dir = Unity.Mathematics.math.normalizesafe(pos, new float3(0f, 1f, 0f));
            lattice[index] = VoxelEngine.GpuVoxel.PlanetField.SurfaceRadius(
                prm.seed, dir, prm.radiusWorld, prm.baseHeight, prm.seaRadius,
                prm.continentScaleDir, prm.mountainScale);
        }
    }
}
