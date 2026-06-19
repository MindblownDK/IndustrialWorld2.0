// Assets/Scripts/VoxelEngine/Cosmos/VeinNoise.cs
//
// Worley-style cellular noise for realistic ore vein clustering.
//
// The old generator stamped ores per-voxel from raw snoise — that gives a uniform
// "salt-and-pepper" speckle. Real ore bodies are POCKETS: clustered, bounded blobs that a
// miner can follow. This hashes the point into a 3D grid of cells, jitter each cell's feature
// point, and returns the distance to the nearest feature. Thresholding that distance produces
// smooth, contiguous vein pockets of arbitrary shape.
//
// Pure & Burst-compatible: no managed objects, no allocations.
using Unity.Burst;
using Unity.Mathematics;

namespace VoxelEngine.Cosmos
{
    [BurstCompile]
    public static class VeinNoise
    {
        /// <summary>
        /// Distance (0..~1) to the nearest feature point in a jittered 3D grid of cell size
        /// <paramref name="cellSize"/>. Small = inside a vein pocket, large = in barren rock.
        /// Combine with an abundance threshold to shape pocket size and rarity.
        /// </summary>
        public static float Worley3(in float3 p, float cellSize, uint seed)
        {
            float3 s = p * (1f / math.max(0.0001f, cellSize));
            int3 cell = (int3)math.floor(s);

            float best = 3f; // > sqrt(3) = max possible within 1 cell
            // Scan the 3x3x3 neighbourhood of cells (a feature point in an adjacent cell can win).
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int3 c = cell + new int3(dx, dy, dz);
                float3 feature = c + HashJitter(c, seed);
                float d = math.distancesq(feature, s); // squared — cheaper, monotonic
                if (d < best) best = d;
            }
            return math.sqrt(best); // in cell units (0..~1.73)
        }

        /// <summary>
        /// Richness 0..1 for an ore deposit at <paramref name="p"/>. Combines a low-frequency
        /// "vein presence" gate (is there a pocket here at all?) with the cellular falloff (how
        /// close to the pocket's core?). Scaled by <paramref name="abundance"/> so designers can
        /// make a material rarer without shrinking every pocket.
        /// </summary>
        public static float DepositRichness(in float3 p, float cellSize, float threshold, float abundance, uint seed)
        {
            // Coarse presence gate — large cells decide whether ANY pocket exists regionally.
            float presence = noise.snoise(p * (1f / (cellSize * 6f)) + seed * 0.137f) * 0.5f + 0.5f;
            if (presence < threshold) return 0f;

            // Cellular falloff inside the pocket.
            float d = Worley3(p, cellSize, seed ^ 0x9E3779B9u);
            // d is in cell units; pockets are the inner core of each cell.
            float core = math.saturate(1f - d * 1.15f);
            return math.saturate(core * abundance);
        }

        // Deterministic per-cell jitter in [0.1, 0.9] so feature points stay inside their cell.
        private static float3 HashJitter(int3 c, uint seed)
        {
            uint h = Hash(c, seed);
            // Three independent floats from the 32-bit hash.
            float jx = ((h        & 0xFFFFu) / 65535f) * 0.8f + 0.1f;
            float jy = (((h >> 8) & 0xFFFFu) / 65535f) * 0.8f + 0.1f;
            float jz = (((h >> 16) & 0xFFFFu) / 65535f) * 0.8f + 0.1f;
            return new float3(jx, jy, jz);
        }

        // Cheap, decent-quality integer hash (wang-style mix).
        private static uint Hash(int3 c, uint seed)
        {
            uint x = (uint)c.x * 0x85ebca6bu;
            uint y = (uint)c.y * 0xc2b2ae35u;
            uint z = (uint)c.z * 0x27d4eb2fu;
            uint h = (x ^ y ^ z) + seed * 0x9E3779B1u;
            h ^= h >> 16; h *= 0x7feb352du;
            h ^= h >> 15; h *= 0x846ca68bu;
            h ^= h >> 16;
            return h;
        }
    }
}
