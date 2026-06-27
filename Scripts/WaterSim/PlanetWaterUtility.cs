// Assets/Scripts/VoxelEngine/WaterSim/PlanetWaterUtility.cs
//
// Shared planet-locked water math. Keeps simulation, rendering, probes, and
// maritime buoyancy using the same local radial frame without changing save data.

using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;

namespace VoxelEngine.WaterSim
{
    public static class PlanetWaterUtility
    {
        public static bool IsPlanetWorld => ActiveWorld.Current is SphereWorld;

        public static Vector3 VoxelToLocalPosition(Vector3 voxelPosition)
            => voxelPosition * VoxelConstants.VOXEL_SIZE;

        public static Vector3 VoxelCenterToLocalPosition(Vector3Int voxel)
            => ((Vector3)voxel + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE;

        public static Vector3 LocalUp(Vector3 localPosition)
            => localPosition.sqrMagnitude > 0.0001f ? localPosition.normalized : Vector3.up;

        public static Vector3 LocalGravityDirection(Vector3Int voxel)
            => -LocalUp(VoxelCenterToLocalPosition(voxel));

        public static Vector3 WorldUp(Vector3 worldPosition)
        {
            var body = GravityProvider.ActiveBody;
            if (body != null) return body.UpAt(worldPosition);
            Vector3 gravity = GravityProvider.GetGravity(worldPosition);
            return gravity.sqrMagnitude > 0.0001f ? -gravity.normalized : Vector3.up;
        }

        public static float SignedDistanceToSea(Vector3 localPosition)
        {
            var world = ActiveWorld.Current;
            if (world is not SphereWorld) return localPosition.y - (world != null ? world.SeaLevel : 0);
            float seaRadius = world.SeaLevel * VoxelConstants.VOXEL_SIZE;
            return localPosition.magnitude - seaRadius;
        }

        public static float TidalPhase(Vector3 localPosition)
        {
            Vector3 up = LocalUp(localPosition);
            Vector3 tideDir = CurrentTideDirectionLocal();
            float alignment = Mathf.Abs(Vector3.Dot(up, tideDir));
            return alignment * alignment;
        }

        public static Vector3 CurrentTideDirectionLocal()
        {
            var body = GravityProvider.ActiveBody;
            var registry = CosmicRegistry.Instance;
            if (body != null && registry != null && registry.Bodies != null)
            {
                Vector3 bodyPos = body.transform.position;
                float bestScore = float.NegativeInfinity;
                Vector3 best = Vector3.zero;

                foreach (var instance in registry.Bodies)
                {
                    if (instance == null || instance.settings == null || instance.settings == body.settings) continue;
                    Vector3 scene = instance.positionKm * registry.WorldUnitsPerKm;
                    Vector3 delta = scene - bodyPos;
                    float distance = delta.magnitude;
                    if (distance < 0.001f) continue;

                    float radius = Mathf.Max(1f, instance.settings.radiusKm);
                    float score = radius * radius * Mathf.Max(0.05f, instance.settings.gravity) / Mathf.Max(distance * distance * distance, 0.001f);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = delta / distance;
                    }
                }

                if (best.sqrMagnitude > 0.0001f) return body.transform.InverseTransformDirection(best).normalized;
            }

            float angle = Time.time * 0.015f;
            return new Vector3(Mathf.Cos(angle), 0.21f, Mathf.Sin(angle)).normalized;
        }

        public static float MoonWaveEnergy(Vector3 localPosition)
        {
            if (!IsPlanetWorld) return 1f;
            return Mathf.Lerp(0.75f, 1.35f, TidalPhase(localPosition));
        }

        public static Unity.Mathematics.float3 ToFloat3(Vector3 v) => new(v.x, v.y, v.z);
    }
}
