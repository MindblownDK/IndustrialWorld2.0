// Assets/Scripts/VoxelEngine/Generation/OilReservoirDecorator.cs
//
// Post-generation decorator that finds CrudeOil voxels in a newly generated chunk
// and carves proper oil reservoirs with the funnel pattern:
//
//   ┌───────────┐  ← Surface seep/pool (visible above ground)
//   │  surface   │
//   └─────┬─────┘
//         │          ← Narrow funnel/shaft (widens at top, narrows going down)
//         │
//         │
//   ┌─────┴─────┐  ← Deep underground reservoir (large spherical pocket)
//   │   POCKET   │
//   │  full oil  │
//   └───────────┘
//
// Called from VoxelWorld after ChunkGenJob completes, before meshing.

using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Fluids;
using VoxelEngine.Materials;

namespace VoxelEngine.Generation
{
    public static class OilReservoirDecorator
    {
        /// <summary>
        /// Scans a chunk for CrudeOil material. When found, carves a funnel-shaped
        /// reservoir: surface pool → narrowing funnel → deep underground pocket.
        /// All void spaces are filled with oil fluid voxels (density = -1, material = CrudeOil, level = 255).
        /// </summary>
        public static void Decorate(Chunk chunk, VoxelWorld world)
        {
            if (chunk == null || world == null) return;
            const int S = VoxelConstants.CHUNK_SIZE;
            int baseX = chunk.coord.x * S;
            int baseY = chunk.coord.y * S;
            int baseZ = chunk.coord.z * S;

            bool foundOil = false;
            Vector3Int oilCenter = Vector3Int.zero;

            for (int z = 2; z < S - 2 && !foundOil; z += 4)
            for (int y = 2; y < S - 2 && !foundOil; y += 4)
            for (int x = 2; x < S - 2 && !foundOil; x += 4)
            {
                var v = chunk.GetVoxelLocal(x, y, z);
                if (v.density > 0 && v.material == (byte)MaterialId.CrudeOil)
                {
                    oilCenter = new Vector3Int(baseX + x, baseY + y, baseZ + z);
                    foundOil = true;
                }
            }

            if (!foundOil) return;

            int hash = oilCenter.x * 73856093 ^ oilCenter.y * 19349663 ^ oilCenter.z * 83492791;
            int rarity = Mathf.Abs(hash % 14);
            if (rarity != 0) return;
            System.Random rng = new System.Random(hash);

            int pocketRadius = 8 + rng.Next(5);
            int funnelTopRadius = 2 + rng.Next(2);
            int funnelBottomRadius = 1;

            int surfaceY = FindSurface(world, oilCenter.x, oilCenter.z, oilCenter.y);
            if (surfaceY <= oilCenter.y) return;

            int minShaftDepth = 6;
            int pocketTopY = Mathf.Min(oilCenter.y + pocketRadius / 2, surfaceY - minShaftDepth);
            if (pocketTopY >= surfaceY - 1) pocketTopY = surfaceY - minShaftDepth;
            if (pocketTopY <= oilCenter.y - 1) pocketTopY = oilCenter.y + 1;

            CarveSurfacePool(world, oilCenter.x, surfaceY, oilCenter.z, funnelTopRadius + 1, rng);
            CarveFunnel(world, oilCenter.x, oilCenter.z, surfaceY - 1, pocketTopY, funnelTopRadius, funnelBottomRadius);
            CarveAndFillPocket(world, new Vector3Int(oilCenter.x, oilCenter.y, oilCenter.z), pocketRadius);
        }

        /// <summary>Carve a visible surface pool at the seep point.</summary>
        private static void CarveSurfacePool(VoxelWorld world, int cx, int surfaceY, int cz, int radius, System.Random rng)
        {
            for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (dx * dx + dz * dz > radius * radius + 1) continue;

                // Clear terrain at and just above surface level
                Vector3Int pos = new Vector3Int(cx + dx, surfaceY, cz + dz);
                world.SetVoxelWorld(pos, Voxel.Empty, remesh: false);
                PlaceOilFluid(world, pos);

                // Clear one voxel above so the pool is visible
                Vector3Int above = new Vector3Int(cx + dx, surfaceY + 1, cz + dz);
                var aboveV = world.GetVoxelWorld(above);
                if (aboveV.density > 0)
                    world.SetVoxelWorld(above, Voxel.Empty, remesh: false);

                // Clear one voxel below for depth
                Vector3Int below = new Vector3Int(cx + dx, surfaceY - 1, cz + dz);
                var belowV = world.GetVoxelWorld(below);
                if (belowV.density > 0)
                {
                    world.SetVoxelWorld(below, Voxel.Empty, remesh: false);
                    PlaceOilFluid(world, below);
                }
            }
        }

        /// <summary>
        /// Carve a tapered funnel shaft from topY down to bottomY.
        /// Top radius = topRadius, bottom radius = bottomRadius (linear interpolation).
        /// </summary>
        private static void CarveFunnel(VoxelWorld world, int cx, int cz, int topY, int bottomY, int topRadius, int bottomRadius)
        {
            int height = topY - bottomY;
            if (height <= 0) return;

            for (int y = topY; y >= bottomY; y--)
            {
                float t = (float)(topY - y) / height; // 0 at top, 1 at bottom
                int radius = Mathf.CeilToInt(Mathf.Lerp(topRadius, bottomRadius, t));
                // Add slight irregularity for natural look
                if (radius < 1) radius = 1;

                for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (dx * dx + dz * dz > radius * radius + 1) continue;
                    Vector3Int pos = new Vector3Int(cx + dx, y, cz + dz);
                    world.SetVoxelWorld(pos, Voxel.Empty, remesh: false);
                    PlaceOilFluid(world, pos);
                }
            }
        }

        /// <summary>Carve the deep underground pocket and fill it with oil.</summary>
        private static void CarveAndFillPocket(VoxelWorld world, Vector3Int center, int radius)
        {
            int r2 = radius * radius;
            for (int dz = -radius; dz <= radius; dz++)
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy + dz * dz > r2) continue;
                Vector3Int pos = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
                world.SetVoxelWorld(pos, Voxel.Empty, remesh: false);
                PlaceOilFluid(world, pos);
            }
        }

        private static void PlaceOilFluid(VoxelWorld world, Vector3Int worldVoxel)
        {
            world.SetVoxelWorld(worldVoxel, new Voxel(-1, (byte)MaterialId.CrudeOil, 255), remesh: false);
        }

        private static int FindSurface(VoxelWorld world, int wx, int wz, int startY)
        {
            for (int y = startY; y < VoxelConstants.WORLD_HEIGHT_VOXELS - 1; y++)
            {
                var v = world.GetVoxelWorld(new Vector3Int(wx, y, wz));
                var above = world.GetVoxelWorld(new Vector3Int(wx, y + 1, wz));
                if (v.density > 0 && above.density <= 0)
                    return y;
            }
            return startY + 20;
        }
    }
}
