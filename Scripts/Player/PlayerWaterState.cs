// Assets/Scripts/VoxelEngine/Player/PlayerWaterState.cs
//
// Queries fluid state at the player's position for swimming mechanics.
// Fully supports radial planet gravity orientations and flat worlds.

using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.WaterSim;

namespace VoxelEngine.Player
{
    public class PlayerWaterState : MonoBehaviour
    {
        public bool IsSwimming { get; set; }
        public bool IsHeadUnderwater { get; set; }
        public float WaterDepth { get; set; }
        public float WaterSurfaceY { get; private set; } = -9999;

        // 9.16.0 Part 3 — per-liquid swimming/contact state.
        /// <summary>The liquid the player's body is currently touching (Water when dry).</summary>
        public LiquidType Liquid { get; private set; } = LiquidType.Water;
        /// <summary>True while any part of the body touches a real liquid cell.</summary>
        public bool IsContactingLiquid { get; private set; }
        /// <summary>Swim speed multiplier of the current liquid (1 = water-like).</summary>
        public float SwimSpeedScale { get; private set; } = 1f;
        /// <summary>Vertical drift of the current liquid in m/s (+ sinks, - floats).</summary>
        public float BuoyancyBias { get; private set; }

        private void Update()
        {
            var world = ActiveWorld.Current;
            if (world == null)
            {
                IsSwimming = false; WaterDepth = 0; IsHeadUnderwater = false;
                Liquid = LiquidType.Water; IsContactingLiquid = false;
                SwimSpeedScale = 1f; BuoyancyBias = 0f;
                return;
            }

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

                // Any real liquid at the feet is enough to enter swim locomotion. This keeps
                // a player from being wedged in a mined oil/water pocket on dry land, while the
                // actual voxel checks prevent the mathematical sea shell from causing false swim.
                IsSwimming = actuallyInWater;
                IsHeadUnderwater = pHeadInLiquid;
                float localDepth = pHeadInLiquid ? 1.8f : (pWaistInLiquid ? 1.0f : (pFeetInLiquid ? 0.45f : 0f));
                WaterDepth = IsSwimming ? Mathf.Clamp01(Mathf.Max(submerged, localDepth) / 1.8f) : 0f;

                // 9.16.0 Part 3 — which liquid is the body in? The dominant one by level wins.
                var liquidVote = LiquidType.Water;
                byte bestLevel = 0;
                ConsiderLiquid(pFeetVoxel, ref liquidVote, ref bestLevel);
                ConsiderLiquid(pWaistVoxel, ref liquidVote, ref bestLevel);
                ConsiderLiquid(pHeadVoxel, ref liquidVote, ref bestLevel);
                Liquid = liquidVote;
                IsContactingLiquid = IsSwimming;
                SwimSpeedScale = IsSwimming ? LiquidPlayerPhysics.SwimSpeedScale(Liquid) : 1f;
                BuoyancyBias = IsSwimming ? LiquidPlayerPhysics.BuoyancyBias(Liquid) : 0f;
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

            // 9.16.0 Part 3 — per-liquid state (flat fallback; planets use the radial branch).
            var liquidVoteFlat = LiquidType.Water;
            byte bestLevelFlat = 0;
            ConsiderLiquid(feetVoxel, ref liquidVoteFlat, ref bestLevelFlat);
            ConsiderLiquid(headVoxel, ref liquidVoteFlat, ref bestLevelFlat);
            Liquid = liquidVoteFlat;
            IsContactingLiquid = IsSwimming;
            SwimSpeedScale = IsSwimming ? LiquidPlayerPhysics.SwimSpeedScale(Liquid) : 1f;
            BuoyancyBias = IsSwimming ? LiquidPlayerPhysics.BuoyancyBias(Liquid) : 0f;
        }

        /// <summary>Folds a voxel into the dominant-liquid vote (9.16.0 Part 3).</summary>
        private static void ConsiderLiquid(Voxel v, ref LiquidType liquid, ref byte bestLevel)
        {
            if (v.IsSolid) return;
            byte lv = v.waterLevel;
            if (lv <= 0) return;
            var l = FluidMaterialUtility.LiquidFromMaterial(v.material);
            if (lv > bestLevel) { bestLevel = lv; liquid = l; }
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
