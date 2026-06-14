// Assets/Scripts/VoxelEngine/Generation/OilReservoirDecorator.cs
//
// Post-generation decorator that finds CrudeOil voxels in a newly generated chunk
// and carves proper oil reservoirs: a large underground pocket of oil with a vertical
// shaft going up to a surface pool.
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
        /// Scans a chunk for CrudeOil material. When found, carves a pocket-shaped
        /// reservoir underground and a chimney up to the surface with a pool on top.
        /// Oil is placed into the FluidGrid as a fluid (same system as water).
        /// </summary>
        public static void Decorate(Chunk chunk, VoxelWorld world)
        {
            if (chunk == null || world == null) return;
            const int S = VoxelConstants.CHUNK_SIZE;
            int baseX = chunk.coord.x * S;
            int baseY = chunk.coord.y * S;
            int baseZ = chunk.coord.z * S;

            // Scan for CrudeOil voxels — use stride of 4 to find pockets without
            // checking every single voxel (perf). One reservoir per chunk max.
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

            // Use a hash of the position as a deterministic seed for this reservoir.
            int hash = oilCenter.x * 73856093 ^ oilCenter.y * 19349663 ^ oilCenter.z * 83492791;
            System.Random rng = new System.Random(hash);

            int pocketRadius = 5 + rng.Next(4);  // 5-8 voxel radius: real extractable pool
            int shaftRadius  = 1;                 // narrow natural funnel/chimney

            // 1) Carve the underground pocket (sphere of air + fill with oil fluid).
            CarveAndFillPocket(world, oilCenter, pocketRadius);

            // 2) Find the surface Y above the oil pocket.
            int surfaceY = FindSurface(world, oilCenter.x, oilCenter.z, oilCenter.y);
            if (surfaceY <= oilCenter.y) return; // somehow already at surface

            // 3) Carve the vertical shaft from pocket top to surface.
            for (int y = oilCenter.y + pocketRadius; y <= surfaceY; y++)
            {
                for (int dx = -shaftRadius; dx <= shaftRadius; dx++)
                for (int dz = -shaftRadius; dz <= shaftRadius; dz++)
                {
                    Vector3Int pos = new Vector3Int(oilCenter.x + dx, y, oilCenter.z + dz);
                    world.SetVoxelWorld(pos, Voxel.Empty, remesh: false);
                    // Fill shaft with oil fluid
                    PlaceOilFluid(world, pos);
                }
            }

            // 4) Carve a visible surface seep/pool at the top.
            int poolRadius = 2 + rng.Next(2); // 2-3
            for (int dx = -poolRadius; dx <= poolRadius; dx++)
            for (int dz = -poolRadius; dz <= poolRadius; dz++)
            {
                if (dx * dx + dz * dz > poolRadius * poolRadius + 1) continue;
                Vector3Int pos = new Vector3Int(oilCenter.x + dx, surfaceY, oilCenter.z + dz);
                world.SetVoxelWorld(pos, Voxel.Empty, remesh: false);
                PlaceOilFluid(world, pos);
                // Also clear the voxel above to make the pool visible.
                Vector3Int above = new Vector3Int(pos.x, pos.y + 1, pos.z);
                var aboveV = world.GetVoxelWorld(above);
                if (aboveV.density > 0)
                    world.SetVoxelWorld(above, Voxel.Empty, remesh: false);
            }
        }

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
            // Write the liquid voxel directly instead of "place if empty". During
            // generation we are carving rock and filling in the same operation, so
            // direct writes guarantee the deep reservoir is genuinely full and not
            // just a rendered cap layer.
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
            return startY + 20; // fallback
        }
    }
}
