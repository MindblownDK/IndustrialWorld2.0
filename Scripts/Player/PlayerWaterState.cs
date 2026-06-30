// Assets/Scripts/VoxelEngine/Player/PlayerWaterState.cs
//
// Queries fluid state at the player's position for swimming mechanics.

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
        public float WaterSurfaceY { get; private set; } = -9999f;
        public float WaterSurfaceRadius { get; private set; } = -1f;
        public float SubmergenceDepthMeters { get; private set; }

        private const float BodyHeightMeters = 1.8f;
        private const float HeadOffsetMeters = 1.6f;

        private void Update()
        {
            var world = ActiveWorld.Current;
            if (world == null)
            {
                ClearState();
                return;
            }

            if (PlanetWaterUtility.IsPlanetWorld)
            {
                UpdateSphereState(world);
                return;
            }

            UpdateFlatState(world);
        }

        private void UpdateSphereState(IVoxelWorld world)
        {
            Vector3 centerDir = transform.position.sqrMagnitude > 0.0001f ? transform.position.normalized : Vector3.up;
            Vector3 feet = transform.position;
            Vector3 chest = feet + centerDir * 0.9f;
            Vector3 head = feet + centerDir * HeadOffsetMeters;

            WaterSurfaceRadius = PlanetWaterUtility.GetVisualSeaRadius(world);
            WaterSurfaceY = WaterSurfaceRadius;

            float feetSubmerged = WaterSurfaceRadius - feet.magnitude;
            float chestSubmerged = WaterSurfaceRadius - chest.magnitude;
            float headSubmerged = WaterSurfaceRadius - head.magnitude;

            SubmergenceDepthMeters = Mathf.Max(0f, feetSubmerged);

            var feetVoxel = world.GetVoxelWorld(world.WorldToVoxel(feet));
            var chestVoxel = world.GetVoxelWorld(world.WorldToVoxel(chest));
            var headVoxel = world.GetVoxelWorld(world.WorldToVoxel(head));

            bool nearLiquid = IsLiquid(feetVoxel) || IsLiquid(chestVoxel) || IsLiquid(headVoxel);
            bool volumetricSubmerged = feetSubmerged > 0.12f;
            bool bodySubmerged = chestSubmerged > 0.18f;
            bool headInside = headSubmerged > 0.02f;

            IsSwimming = (volumetricSubmerged && bodySubmerged) || (nearLiquid && chestSubmerged > -0.15f);
            IsHeadUnderwater = (volumetricSubmerged && headInside) || (IsLiquid(headVoxel) && headSubmerged > -0.2f);

            float normalizedDepth = Mathf.Max(0f, feetSubmerged + 0.25f) / BodyHeightMeters;
            WaterDepth = IsSwimming ? Mathf.Clamp01(normalizedDepth) : 0f;
        }

        private void UpdateFlatState(IVoxelWorld world)
        {
            WaterSurfaceRadius = -1f;
            SubmergenceDepthMeters = 0f;

            Vector3 feet = transform.position;
            Vector3 head = feet + Vector3.up * HeadOffsetMeters;

            var feetVoxel = world.GetVoxelWorld(world.WorldToVoxel(feet));
            var headVoxel = world.GetVoxelWorld(world.WorldToVoxel(head));

            WaterSurfaceY = SampleWaterSurface(world, feet);
            float flatSubmerged = WaterSurfaceY > -9000f ? (WaterSurfaceY - feet.y) : 0f;
            bool feetInLiquid = feetVoxel.waterLevel > 10 && !feetVoxel.IsSolid;

            const float SwimDepth = 0.85f;
            IsSwimming = feetInLiquid && (WaterSurfaceY <= -9000f || flatSubmerged > SwimDepth);
            IsHeadUnderwater = headVoxel.waterLevel > 10 && !headVoxel.IsSolid;
            WaterDepth = IsSwimming ? Mathf.Clamp01(Mathf.Max(flatSubmerged, 1.8f) / 1.8f) : 0f;
        }

        private void ClearState()
        {
            IsSwimming = false;
            WaterDepth = 0f;
            IsHeadUnderwater = false;
            WaterSurfaceY = -9999f;
            WaterSurfaceRadius = -1f;
            SubmergenceDepthMeters = 0f;
        }

        private static bool IsLiquid(Voxel v)
        {
            return FluidMaterialUtility.IsFluid(v) || (v.waterLevel > 10 && !v.IsSolid);
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

            return -9999f;
        }

        public void MarkInWater() { }
    }
}
