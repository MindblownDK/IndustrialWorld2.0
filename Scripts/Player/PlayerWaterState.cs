// Assets/Scripts/VoxelEngine/Player/PlayerWaterState.cs
//
// Queries the voxel waterLevel at the player's position for swimming detection.
// Uses the new integrated water system (waterLevel in Voxel struct).

using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.Player
{
    public class PlayerWaterState : MonoBehaviour
    {
        public bool IsSwimming { get; set; }
        public bool IsHeadUnderwater { get; set; }
        public float WaterDepth { get; set; }
        public float WaterSurfaceY { get; private set; } = -9999;

        private void Update()
        {
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) { IsSwimming = false; WaterDepth = 0; IsHeadUnderwater = false; return; }

            Vector3 feet = transform.position;
            Vector3 head = feet + Vector3.up * 1.6f;

            // Direct voxel waterLevel check at feet and head.
            var feetVoxel = world.GetVoxelWorld(world.WorldToVoxel(feet));
            var headVoxel = world.GetVoxelWorld(world.WorldToVoxel(head));

            // Find the liquid surface Y so we can decide swim-vs-wade from real
            // depth. Deep oceans can be far more than 10 voxels below the surface,
            // so the search must be generous; otherwise the player "stops swimming"
            // and can only jump at depth.
            WaterSurfaceY = SampleWaterSurface(world, feet);
            float submerged = WaterSurfaceY > -9000 ? (WaterSurfaceY - feet.y) : 0f;
            bool feetInLiquid = feetVoxel.waterLevel > 10 && !feetVoxel.IsSolid;

            // Swim only when genuinely in liquid. If we are in a deep column but the
            // top surface is outside the scan range, keep swimming instead of falling
            // back to jump-only land movement.
            const float SWIM_DEPTH = 0.85f;
            IsSwimming       = feetInLiquid && (WaterSurfaceY <= -9000 || submerged > SWIM_DEPTH);
            IsHeadUnderwater = headVoxel.waterLevel > 10 && !headVoxel.IsSolid;
            WaterDepth       = IsSwimming ? Mathf.Clamp01(Mathf.Max(submerged, 1.8f) / 1.8f) : 0f;
        }

        /// <summary>Find the Y position of the water surface above a world position.</summary>
        private float SampleWaterSurface(VoxelEngine.Core.IVoxelWorld world, Vector3 pos)
        {
            Vector3Int vp = world.WorldToVoxel(pos);

            // Search upward from the voxel position to find the top liquid cell.
            // 96m is enough for our current ocean depths while still cheap (one
            // column sample per frame).
            for (int dy = 0; dy < 96; dy++)
            {
                var checkPos = new Vector3Int(vp.x, vp.y + dy, vp.z);
                var v = world.GetVoxelWorld(checkPos);
                if (v.waterLevel > 0 && !v.IsSolid)
                {
                    // Check if cell above has no water.
                    var above = world.GetVoxelWorld(new Vector3Int(vp.x, vp.y + dy + 1, vp.z));
                    if (above.waterLevel == 0 || above.IsSolid)
                        return (vp.y + dy) * VoxelConstants.VOXEL_SIZE + v.WaterFill;
                }
            }

            // Also check downward (player might be just above water).
            for (int dy = 0; dy > -12; dy--)
            {
                var checkPos = new Vector3Int(vp.x, vp.y + dy, vp.z);
                var v = world.GetVoxelWorld(checkPos);
                if (v.waterLevel > 0 && !v.IsSolid)
                {
                    var above = world.GetVoxelWorld(new Vector3Int(vp.x, vp.y + dy + 1, vp.z));
                    if (above.waterLevel == 0 || above.IsSolid)
                        return (vp.y + dy) * VoxelConstants.VOXEL_SIZE + v.WaterFill;
                }
            }

            return -9999;
        }

        public void MarkInWater() { } // legacy compat
    }
}
