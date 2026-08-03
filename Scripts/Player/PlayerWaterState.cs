// Assets/Scripts/VoxelEngine/Player/PlayerWaterState.cs
//
// Queries fluid state at the player's position for swimming mechanics.
// Fully supports radial planet gravity orientations and flat worlds.

using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.WaterSim;

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
            var world = ActiveWorld.Current;
            if (world == null) { IsSwimming = false; WaterDepth = 0; IsHeadUnderwater = false; return; }

            if (PlanetWaterUtility.IsPlanetWorld)
            {
                float seaRadius = world.SeaLevel * VoxelConstants.VOXEL_SIZE;
                WaterSurfaceY = seaRadius;
                float submerged = Mathf.Max(0f, -PlanetWaterUtility.SignedDistanceToSea(transform.position));

                Vector3 up = PlanetWaterUtility.WorldUp(transform.position);
                var pFeetVoxel  = world.GetVoxelWorld(world.WorldToVoxel(transform.position));
                var pWaistVoxel = world.GetVoxelWorld(world.WorldToVoxel(transform.position + up * 0.8f));
                var pHeadVoxel  = world.GetVoxelWorld(world.WorldToVoxel(transform.position + up * 1.6f));

                bool pFeetInLiquid  = FluidMaterialUtility.IsFluid(pFeetVoxel)  || pFeetVoxel.material  == (byte)Materials.MaterialId.WaterLiquid;
                bool pWaistInLiquid = FluidMaterialUtility.IsFluid(pWaistVoxel) || pWaistVoxel.material == (byte)Materials.MaterialId.WaterLiquid;
                bool pHeadInLiquid  = FluidMaterialUtility.IsFluid(pHeadVoxel)  || pHeadVoxel.material  == (byte)Materials.MaterialId.WaterLiquid;

                bool actuallyInWater = pFeetInLiquid || pWaistInLiquid || pHeadInLiquid;

                IsSwimming = actuallyInWater && (pWaistInLiquid || pHeadInLiquid || submerged > 0.75f);
                IsHeadUnderwater = actuallyInWater && pHeadInLiquid && submerged > 0.2f;
                WaterDepth = IsSwimming ? Mathf.Clamp01(Mathf.Max(submerged, 1.8f) / 1.8f) : 0f;
                return;
            }

            Vector3 feet = transform.position;
            Vector3 head = feet + Vector3.up * 1.6f;

            var feetVoxel = world.GetVoxelWorld(world.WorldToVoxel(feet));
            var headVoxel = world.GetVoxelWorld(world.WorldToVoxel(head));

            WaterSurfaceY = SampleWaterSurface(world, feet);
            float flatSubmerged = WaterSurfaceY > -9000 ? (WaterSurfaceY - feet.y) : 0f;
            bool feetInLiquid = feetVoxel.waterLevel > 10 && !feetVoxel.IsSolid;

            const float SWIM_DEPTH = 0.85f;
            IsSwimming       = feetInLiquid && (WaterSurfaceY <= -9000 || flatSubmerged > SWIM_DEPTH);
            IsHeadUnderwater = headVoxel.waterLevel > 10 && !headVoxel.IsSolid;
            WaterDepth       = IsSwimming ? Mathf.Clamp01(Mathf.Max(flatSubmerged, 1.8f) / 1.8f) : 0f;
        }

        private float SampleWaterSurface(IVoxelWorld world, Vector3 pos)
        {
            Vector3Int vp = world.WorldToVoxel(pos);

            for (int dy = 0; dy < 96; dy++)
            {
                var checkPos = new Vector3Int(vp.x, vp.y + dy, vp.z);
                var v = world.GetVoxelWorld(checkPos);
                if (v.waterLevel > 0 && !v.IsSolid)
                {
                    var above = world.GetVoxelWorld(new Vector3Int(vp.x, vp.y + dy + 1, vp.z));
                    if (above.waterLevel == 0 || above.IsSolid)
                        return (vp.y + dy) * VoxelConstants.VOXEL_SIZE + (v.waterLevel / 255f);
                }
            }

            for (int dy = 0; dy > -12; dy--)
            {
                var checkPos = new Vector3Int(vp.x, vp.y + dy, vp.z);
                var v = world.GetVoxelWorld(checkPos);
                if (v.waterLevel > 0 && !v.IsSolid)
                {
                    var above = world.GetVoxelWorld(new Vector3Int(vp.x, vp.y + dy + 1, vp.z));
                    if (above.waterLevel == 0 || above.IsSolid)
                        return (vp.y + dy) * VoxelConstants.VOXEL_SIZE + (v.waterLevel / 255f);
                }
            }

            return -9999;
        }

        public void MarkInWater() { }
    }
}
