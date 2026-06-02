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
            var world = VoxelWorld.Instance;
            if (world == null) { IsSwimming = false; WaterDepth = 0; IsHeadUnderwater = false; return; }

            Vector3 feet = transform.position;
            Vector3 head = feet + Vector3.up * 1.6f;

            // Direct voxel waterLevel check at feet and head.
            var feetVoxel = world.GetVoxelWorld(world.WorldToVoxel(feet));
            var headVoxel = world.GetVoxelWorld(world.WorldToVoxel(head));

            // Find the water surface Y so we can decide swim-vs-wade from real depth,
            // not just from "any water cell touches my feet". Sampling first lets us
            // gate buoyancy on actual submersion depth below.
            WaterSurfaceY = SampleWaterSurface(world, feet);
            float submerged = WaterSurfaceY > -9000 ? (WaterSurfaceY - feet.y) : 0f;

            // Swim only when the player is genuinely in the water (waist deep or
            // more). Knee-deep water is just visual; the player still walks normally.
            // Without this guard buoyancy kicks in on the first puddle and the
            // player floats across the surface as if it were a solid road.
            const float SWIM_DEPTH = 0.85f;       // metres of submersion
            IsSwimming       = feetVoxel.waterLevel > 10 && submerged > SWIM_DEPTH;
            IsHeadUnderwater = headVoxel.waterLevel > 10;
            WaterDepth       = IsSwimming ? Mathf.Clamp01(submerged / 1.8f) : 0f;
        }

        /// <summary>Find the Y position of the water surface above a world position.</summary>
        private float SampleWaterSurface(VoxelWorld world, Vector3 pos)
        {
            Vector3Int vp = world.WorldToVoxel(pos);

            // Search upward from the voxel position to find the top water cell.
            for (int dy = 0; dy < 10; dy++)
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

            // Also check downward (player might be above water).
            for (int dy = 0; dy > -5; dy--)
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
