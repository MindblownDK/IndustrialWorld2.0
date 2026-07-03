// Assets/Scripts/VoxelEngine/Generation/OilReservoirDecorator.cs
//
// Scans a chunk for CrudeOil material. When found, carves a funnel-shaped
// reservoir: surface pool → narrowing funnel → deep underground pocket.
// All void spaces are filled with oil fluid voxels (density = -1, material = CrudeOil, level = 255).
// Fully supports both spherical planet worlds (radial up) and flat worlds (Y up).

using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Fluids;
using VoxelEngine.Materials;

namespace VoxelEngine.Generation
{
    public static class OilReservoirDecorator
    {
        public static void Decorate(Chunk chunk, IVoxelWorld world)
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

            Vector3Int surfacePos;
            int shaftDepth = FindSurface(world, oilCenter, out surfacePos);
            if (shaftDepth <= 2) return;

            int minShaftDepth = 6;
            int pocketTopStep = Mathf.Min(pocketRadius / 2, shaftDepth - minShaftDepth);
            if (pocketTopStep <= 0) pocketTopStep = 1;
            
            Vector3 centerVec = (Vector3)oilCenter;
            Vector3 upDir = GetUpDir(world, centerVec);
            Vector3Int pocketTopPos = Vector3Int.RoundToInt(centerVec + upDir * pocketTopStep);

            CarveSurfacePool(world, surfacePos, upDir, funnelTopRadius + 1, rng);
            CarveFunnel(world, surfacePos, pocketTopPos, funnelTopRadius, funnelBottomRadius);
            CarveAndFillPocket(world, oilCenter, pocketRadius);
        }

        private static Vector3 GetUpDir(IVoxelWorld world, Vector3 pos)
        {
            if (world is VoxelEngine.Cosmos.SphereWorld)
            {
                Vector3 up = pos.normalized;
                return up.sqrMagnitude > 0.001f ? up : Vector3.up;
            }
            return Vector3.up;
        }

        private static int FindSurface(IVoxelWorld world, Vector3Int start, out Vector3Int surfacePos)
        {
            Vector3 centerVec = (Vector3)start;
            Vector3 upDir = GetUpDir(world, centerVec);
            surfacePos = start;

            for (int d = 0; d < 150; d++)
            {
                Vector3Int check = Vector3Int.RoundToInt(centerVec + upDir * d);
                var v = world.GetVoxelWorld(check);
                var above = world.GetVoxelWorld(Vector3Int.RoundToInt(centerVec + upDir * (d + 1)));
                if (v.density > 0 && above.density <= 0)
                {
                    surfacePos = check;
                    return d;
                }
            }
            surfacePos = Vector3Int.RoundToInt(centerVec + upDir * 20);
            return 20;
        }

        private static void CarveSurfacePool(IVoxelWorld world, Vector3Int surfacePos, Vector3 upDir, int radius, System.Random rng)
        {
            Vector3 tanA = Vector3.Cross(upDir, Vector3.up);
            if (tanA.sqrMagnitude < 0.001f) tanA = Vector3.Cross(upDir, Vector3.forward);
            tanA.Normalize();
            Vector3 tanB = Vector3.Cross(upDir, tanA).normalized;

            for (int u = -radius; u <= radius; u++)
            for (int v = -radius; v <= radius; v++)
            {
                if (u * u + v * v > radius * radius + 1) continue;

                Vector3 pt = (Vector3)surfacePos + tanA * u + tanB * v;
                Vector3Int pos = Vector3Int.RoundToInt(pt);
                world.SetVoxelWorld(pos, Voxel.Empty, remesh: false);
                PlaceOilFluid(world, pos);

                Vector3Int above = Vector3Int.RoundToInt(pt + upDir);
                var aboveV = world.GetVoxelWorld(above);
                if (aboveV.density > 0)
                    world.SetVoxelWorld(above, Voxel.Empty, remesh: false);

                Vector3Int below = Vector3Int.RoundToInt(pt - upDir);
                var belowV = world.GetVoxelWorld(below);
                if (belowV.density > 0)
                {
                    world.SetVoxelWorld(below, Voxel.Empty, remesh: false);
                    PlaceOilFluid(world, below);
                }
            }
        }

        private static void CarveFunnel(IVoxelWorld world, Vector3Int topPos, Vector3Int bottomPos, int topRadius, int bottomRadius)
        {
            float dist = Vector3.Distance((Vector3)topPos, (Vector3)bottomPos);
            int steps = Mathf.CeilToInt(dist);
            if (steps <= 0) return;

            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                float radius = Mathf.Lerp(topRadius, bottomRadius, t);
                if (radius < 1f) radius = 1f;
                int rInt = Mathf.CeilToInt(radius);
                int r2 = rInt * rInt;

                Vector3 centerPt = Vector3.Lerp((Vector3)topPos, (Vector3)bottomPos, t);
                Vector3Int centerCell = Vector3Int.RoundToInt(centerPt);

                for (int dz = -rInt; dz <= rInt; dz++)
                for (int dy = -rInt; dy <= rInt; dy++)
                for (int dx = -rInt; dx <= rInt; dx++)
                {
                    if (dx * dx + dy * dy + dz * dz > r2 + 1) continue;
                    Vector3Int pos = new Vector3Int(centerCell.x + dx, centerCell.y + dy, centerCell.z + dz);
                    world.SetVoxelWorld(pos, Voxel.Empty, remesh: false);
                    PlaceOilFluid(world, pos);
                }
            }
        }

        private static void CarveAndFillPocket(IVoxelWorld world, Vector3Int center, int radius)
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

        private static void PlaceOilFluid(IVoxelWorld world, Vector3Int worldVoxel)
        {
            world.SetVoxelWorld(worldVoxel, new Voxel(-1, (byte)MaterialId.CrudeOil, 255), remesh: false);
        }
    }
}
