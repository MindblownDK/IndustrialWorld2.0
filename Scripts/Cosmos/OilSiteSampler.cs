// Assets/Scripts/VoxelEngine/Cosmos/OilSiteSampler.cs
//
// Deterministic crude-oil site sampling for the LOD levels (PlanetVoxelLod).
//
// The gameplay world (SphereWorld 1 m chunks) builds oil sites with
// OilReservoirDecorator: a liquid puddle on the surface, a tapered solid-oil
// bore down to a solid-oil reservoir. The LOD levels render the SAME geological
// story by sampling a precomputed site map: every 96 m cell that rolls the
// decorator's finite/infinite chance (same hash, same salts, same seed) gets a
// site anchored at the radial surface through the cell, with puddle → bore →
// reservoir geometry scaled up so it reads at coarse voxel sizes.
//
// Sites are visual approximations in the LOD (the exact decorator anchor is the
// first scanned surface voxel in the cell, which can sit up to ~80 m away); the
// real liquid puddle and exact geometry appear when the 1 m world streams in.

using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    /// <summary>Burstable container for one oil site (all floats, no managed refs).</summary>
    public struct OilSiteData
    {
        public float3 anchor;          // surface point — puddle centre
        public float3 funnelTop;       // top of the solid bore (reservoir roof)
        public float3 reservoirCenter; // heart of the reservoir sphere
        public float puddleRadius;     // metres
        public float mouthRadius;      // bore radius at the surface
        public float throatRadius;     // bore radius at the reservoir
        public float reservoirRadius;  // metres
    }

    /// <summary>
    /// Builds and queries the oil-site map used by the real-voxel LOD levels.
    /// </summary>
    public static class OilSiteSampler
    {
        public const int SiteCellSize = 96;   // must match OilReservoirDecorator
        public const uint FiniteSalt  = 0x68E31DA4u;  // must match OilReservoirDecorator
        public const uint InfiniteSalt = 0xB5297A4Du; // must match OilReservoirDecorator

        public static int CellKey(int3 cell)
            => cell.x * 73856093 ^ cell.y * 19349663 ^ cell.z * 83492791;

        public static float Hash01(int key, int seed, uint salt)
        {
            unchecked
            {
                uint h = (uint)key;
                h ^= (uint)seed * 2654435761u;
                h ^= salt;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
        }

        /// <summary>
        /// Burst-compatible per-voxel probe. Returns the crude-oil material id when
        /// the voxel lies inside a site's reservoir, bore, or surface puddle disc.
        /// </summary>
        public static byte Sample(
            in NativeParallelHashMap<int, OilSiteData> sites,
            in float3 worldPos,
            float depth,
            float3 up)
        {
            if (!sites.IsCreated || sites.Length == 0) return 0;

            int3 cell = new int3(
                (int)math.floor(worldPos.x / SiteCellSize),
                (int)math.floor(worldPos.y / SiteCellSize),
                (int)math.floor(worldPos.z / SiteCellSize));
            int key = CellKey(cell);
            if (!sites.TryGetValue(key, out OilSiteData s)) return 0;

            // Reservoir sphere.
            if (math.length(worldPos - s.reservoirCenter) <= s.reservoirRadius)
                return (byte)MaterialId.CrudeOil;

            // Tapered bore from the surface down to the reservoir roof.
            float3 ab = s.funnelTop - s.anchor;
            float lenSq = math.dot(ab, ab);
            if (lenSq > 1e-6f)
            {
                float t = math.clamp(math.dot(worldPos - s.anchor, ab) / lenSq, 0f, 1f);
                float3 closest = s.anchor + ab * t;
                float r = math.lerp(s.mouthRadius, s.throatRadius, t);
                if (math.length(worldPos - closest) <= r)
                    return (byte)MaterialId.CrudeOil;
            }

            // Surface puddle disc — only the topmost solid layer (reads as a dark
            // oil patch from the air; the real liquid puddle appears at 1 m detail).
            if (depth < 25f)
            {
                float3 toAxis = worldPos - s.anchor;
                float axial = math.dot(toAxis, up);
                float radial = math.length(toAxis - up * axial);
                if (axial >= -32f && axial <= 4f && radial <= s.puddleRadius)
                    return (byte)MaterialId.CrudeOil;
            }
            return 0;
        }
    }
}
