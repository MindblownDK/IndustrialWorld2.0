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

        /// <summary>
        /// Converts a local radial direction into spherical coordinates (Latitude -PI/2..PI/2, Longitude -PI..PI).
        /// Used for sampling cascaded global ocean displacement heightmaps.
        /// </summary>
        public static Vector2 ToSphericalLatLon(Vector3 localDir)
        {
            Vector3 n = localDir.normalized;
            float lat = Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f));
            float lon = Mathf.Atan2(n.z, n.x);
            return new Vector2(lat, lon);
        }

        /// <summary>
        /// Samples the 3D Spherical Density Field at 8 bounding points under a ship hull
        /// to compute total displaced fluid volume for realistic buoyancy mechanics.
        /// </summary>
        public static float SampleHullDisplacedVolume(Vector3 worldCenter, Vector3 halfExtents, Quaternion rotation)
        {
            var world = ActiveWorld.Current;
            if (world == null) return 0f;

            Vector3[] offsets = new Vector3[8]
            {
                new Vector3(-halfExtents.x, -halfExtents.y, -halfExtents.z),
                new Vector3( halfExtents.x, -halfExtents.y, -halfExtents.z),
                new Vector3(-halfExtents.x,  halfExtents.y, -halfExtents.z),
                new Vector3( halfExtents.x,  halfExtents.y, -halfExtents.z),
                new Vector3(-halfExtents.x, -halfExtents.y,  halfExtents.z),
                new Vector3( halfExtents.x, -halfExtents.y,  halfExtents.z),
                new Vector3(-halfExtents.x,  halfExtents.y,  halfExtents.z),
                new Vector3( halfExtents.x,  halfExtents.y,  halfExtents.z)
            };

            float totalSubmergedDensity = 0f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 pt = worldCenter + rotation * offsets[i];
                totalSubmergedDensity += SampleDensityAtWorldPos(pt);
            }

            float avgDensity = totalSubmergedDensity / 8f;
            float hullVolume = halfExtents.x * halfExtents.y * halfExtents.z * 8f;
            return hullVolume * avgDensity;
        }

        /// <summary>
        /// Sample fluid density (0..1) at any world coordinate.
        /// Interfaces with FluidManager adaptive sparse storage or fallback voxel waterLevel.
        /// </summary>
        public static float SampleDensityAtWorldPos(Vector3 worldPos)
        {
            var world = ActiveWorld.Current;
            if (world == null) return 0f;

            Vector3Int voxelPos = world.WorldToVoxel(worldPos);
            if (FluidManager.Instance != null && FluidManager.Instance.TryGetVolumetricDensity(voxelPos, out float gpuDensity))
            {
                return gpuDensity;
            }

            var v = world.GetVoxelWorld(voxelPos);
            return v.waterLevel / 255f;
        }

        public static Unity.Mathematics.float3 ToFloat3(Vector3 v) => new(v.x, v.y, v.z);
    }
}
