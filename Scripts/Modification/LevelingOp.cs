// Assets/Scripts/VoxelEngine/Modification/LevelingOp.cs
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;

namespace VoxelEngine.Modification
{
    /// <summary>
    /// Flatten terrain within a horizontal brush radius to a target world Y.
    /// Voxels above the target → carved (air). Voxels below → filled with stone.
    /// Voxels at exactly the target Y → made fully solid stone.
    /// </summary>
    public static class LevelingOp
    {
        // Maintained per-call by PlayerInteractionTool — first click sets it, then it persists.
        public static int    TargetWorldY = int.MinValue;
        public static bool   HasTarget => TargetWorldY != int.MinValue;
        public static void   ClearTarget() => TargetWorldY = int.MinValue;

        /// <summary>
        /// Apply the flatten operation centred at `worldPos` within a circular footprint of `radius`.
        /// If no target Y has been set yet, sets the target to the Y of the looked-at voxel and returns false
        /// (so the caller can show feedback "anchored").
        /// </summary>
        public static bool ApplyAt(IVoxelWorld world, MaterialRegistry registry,
                                   Vector3 worldPos, float radius, int verticalReach)
        {
            if (world == null) return false;

            Vector3Int center = world.WorldToVoxel(worldPos);

            // First click sets the target Y.
            if (!HasTarget)
            {
                TargetWorldY = center.y;
                return false;
            }

            int r = Mathf.CeilToInt(radius);
            int targetY = TargetWorldY;
            int yLow  = targetY - verticalReach;
            int yHigh = targetY + verticalReach;

            // Stone material reference.
            byte stoneMat = (byte)MaterialId.Stone;

            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                float d2 = dx * dx + dz * dz;
                if (d2 > radius * radius) continue;

                for (int y = yLow; y <= yHigh; y++)
                {
                    var coord = new Vector3Int(center.x + dx, y, center.z + dz);

                    if (y > targetY)
                    {
                        // Anything above target -> carve (set density to air).
                        world.SetVoxelWorld(coord, new Voxel(-127, (byte)MaterialId.Air), remesh: false);
                    }
                    else
                    {
                        // At or below target -> fully solid stone.
                        world.SetVoxelWorld(coord, new Voxel(127, stoneMat), remesh: false);
                    }
                }
            }

            // Trigger remesh for the affected chunks (radius+1 chunk square).
            int cs = VoxelConstants.CHUNK_SIZE;
            Vector3Int chunkCenter = new Vector3Int(
                Mathf.FloorToInt(center.x / (float)cs),
                Mathf.FloorToInt(targetY / (float)cs),
                Mathf.FloorToInt(center.z / (float)cs));
            int chunkR = Mathf.CeilToInt(radius / cs) + 1;
            for (int cz = -chunkR; cz <= chunkR; cz++)
            for (int cy = -2; cy <= 2; cy++)
            for (int cx = -chunkR; cx <= chunkR; cx++)
            {
                if (world.TryGetChunk(chunkCenter + new Vector3Int(cx, cy, cz), out var ch) && ch.isGenerated)
                    world.ScheduleMeshJob(ch);
            }
            return true;
        }
    }
}
