using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;

namespace VoxelEngine.WaterSim
{
    public static class PlanetWaterUtility
    {
        public static bool IsPlanetWorld => ActiveWorld.Current is SphereWorld;

        public static Vector3 LocalUp(Vector3 worldPos)
        {
            if (worldPos.sqrMagnitude <= 0.000001f)
                return Vector3.up;

            return worldPos.normalized;
        }

        public static Vector3 WorldUp(Vector3 worldPos)
        {
            return LocalUp(worldPos);
        }

        public static Vector3 LocalGravityDirection(Vector3 worldPos)
        {
            return -LocalUp(worldPos);
        }

        public static float3 ToFloat3(Vector3 value)
        {
            return new float3(value.x, value.y, value.z);
        }

        public static Vector3 VoxelToLocalPosition(Vector3 voxelPosition)
        {
            return voxelPosition * VoxelConstants.VOXEL_SIZE;
        }

        public static float GetVisualSeaRadius(IVoxelWorld world)
        {
            float baseRadius = (world != null ? world.SeaLevel : 96) * VoxelConstants.VOXEL_SIZE;
            float offset = Shader.GetGlobalFloat("_PlanetOceanSeaLevelOffset");
            return baseRadius + offset;
        }

        public static float SignedDistanceToSea(Vector3 worldPos)
        {
            var world = ActiveWorld.Current;
            return worldPos.magnitude - GetVisualSeaRadius(world);
        }

        public static float SampleDensityAtWorldPos(Vector3 worldPos)
        {
            if (!IsPlanetWorld)
            {
                var world = ActiveWorld.Current;
                if (world == null) return 0f;
                var voxel = world.GetVoxelWorld(world.WorldToVoxel(worldPos));
                if (!FluidMaterialUtility.IsFluid(voxel) && voxel.waterLevel <= 0) return 0f;
                return Mathf.Clamp01(voxel.waterLevel / 255f);
            }

            float seaRadius = GetVisualSeaRadius(ActiveWorld.Current);
            float signed = seaRadius - worldPos.magnitude;
            if (signed <= -0.5f) return 0f;
            if (signed >= 0.5f) return 1f;
            return Mathf.Clamp01(signed + 0.5f);
        }

        public static float MoonWaveEnergy(Vector3 worldPos)
        {
            float t = Time.time * 0.05f;
            Vector3 dir = worldPos.sqrMagnitude > 0.000001f ? worldPos.normalized : Vector3.up;
            float phase = dir.x * 0.73f + dir.y * 0.41f + dir.z * 0.58f + t;
            return 0.75f + 0.25f * Mathf.Sin(phase * Mathf.PI * 2f);
        }

        public static Vector3 CurrentTideDirectionLocal()
        {
            float t = Time.time * 0.035f;
            Vector3 dir = new Vector3(Mathf.Cos(t), Mathf.Sin(t * 0.7f), Mathf.Sin(t));
            if (dir.sqrMagnitude <= 0.000001f)
                return Vector3.up;
            return dir.normalized;
        }
    }
}
