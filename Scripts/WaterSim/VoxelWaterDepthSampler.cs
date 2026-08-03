using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Materials;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Main-thread helper for deriving real water depth from procedural voxel data.
    /// Crest handles visuals, but this keeps shallow/deep decisions grounded in the
    /// generated world: beaches, lakes, pumps and maritime systems all read the same data.
    /// </summary>
    public static class VoxelWaterDepthSampler
    {
        public static bool TrySampleDepth(Vector3 worldPosition, out float depth, out float surfaceHeight)
        {
            depth = 0f;
            surfaceHeight = 0f;

            var world = ActiveWorld.Current;
            if (world == null) return false;

            if (PlanetWaterUtility.IsPlanetWorld)
                return TrySamplePlanetDepth(world, worldPosition, out depth, out surfaceHeight);

            return TrySampleFlatDepth(world, worldPosition, out depth, out surfaceHeight);
        }


        public static bool TryFindNearbyWater(Vector3 center, float radius, float spacing, out Vector3 waterPosition, out float depth, out float surfaceHeight)
        {
            waterPosition = center;
            depth = 0f;
            surfaceHeight = 0f;

            var world = ActiveWorld.Current;
            if (world == null) return false;

            spacing = Mathf.Max(2f, spacing);
            radius = Mathf.Max(spacing, radius);

            if (TrySampleDepth(center, out depth, out surfaceHeight) || TrySampleSeaSurface(center, out depth, out surfaceHeight))
            {
                waterPosition = PlanetWaterUtility.IsPlanetWorld
                    ? center + PlanetWaterUtility.WorldUp(center) * surfaceHeight
                    : new Vector3(center.x, surfaceHeight, center.z);
                return true;
            }

            Vector3 up = PlanetWaterUtility.IsPlanetWorld ? PlanetWaterUtility.WorldUp(center) : Vector3.up;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();
            Vector3 tangentA = Vector3.Cross(up, Vector3.forward);
            if (tangentA.sqrMagnitude < 0.0001f) tangentA = Vector3.Cross(up, Vector3.right);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;

            float bestDistSq = float.MaxValue;
            bool found = false;
            int steps = Mathf.CeilToInt(radius / spacing);
            for (int z = -steps; z <= steps; z++)
            for (int x = -steps; x <= steps; x++)
            {
                Vector2 offset2 = new Vector2(x * spacing, z * spacing);
                if (offset2.sqrMagnitude > radius * radius) continue;

                Vector3 sample = center + tangentA * offset2.x + tangentB * offset2.y;
                if (!TrySampleDepth(sample, out float d, out float surf) && !TrySampleSeaSurface(sample, out d, out surf)) continue;

                float distSq = offset2.sqrMagnitude;
                if (distSq >= bestDistSq) continue;

                bestDistSq = distSq;
                depth = d;
                surfaceHeight = surf;
                waterPosition = PlanetWaterUtility.IsPlanetWorld
                    ? sample + PlanetWaterUtility.WorldUp(sample) * surf
                    : new Vector3(sample.x, surf, sample.z);
                found = true;
            }

            return found;
        }

        public static bool TrySampleSeaSurface(Vector3 worldPosition, out float depth, out float surfaceHeight)
        {
            depth = 0f;
            surfaceHeight = 0f;

            var world = ActiveWorld.Current;
            if (world == null) return false;

            if (PlanetWaterUtility.IsPlanetWorld)
                return TrySamplePlanetSeaSurface(world, worldPosition, out depth, out surfaceHeight);

            return TrySampleFlatSeaSurface(world, worldPosition, out depth, out surfaceHeight);
        }

        private static bool TrySampleFlatSeaSurface(IVoxelWorld world, Vector3 worldPosition, out float depth, out float surfaceHeight)
        {
            depth = 0f;
            surfaceHeight = world.SeaLevel * VoxelConstants.VOXEL_SIZE;

            int ix = Mathf.RoundToInt(worldPosition.x / VoxelConstants.VOXEL_SIZE);
            int iz = Mathf.RoundToInt(worldPosition.z / VoxelConstants.VOXEL_SIZE);
            int seaY = Mathf.RoundToInt(world.SeaLevel);
            int bottom = seaY - 192;

            int floorY = int.MinValue;
            for (int y = seaY; y >= bottom; y--)
            {
                var v = world.GetVoxelWorld(new Vector3Int(ix, y, iz));
                if (IsTerrain(v)) { floorY = y; break; }
            }

            if (floorY == int.MinValue || floorY >= seaY) return false;
            float floorHeight = (floorY + 1) * VoxelConstants.VOXEL_SIZE;
            depth = Mathf.Max(0f, surfaceHeight - floorHeight);
            return depth > 0.05f;
        }

        private static bool TrySamplePlanetSeaSurface(IVoxelWorld world, Vector3 worldPosition, out float depth, out float signedSurface)
        {
            depth = 0f;
            signedSurface = 0f;

            Vector3 up = PlanetWaterUtility.WorldUp(worldPosition);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            Vector3 bodyCenter = Vector3.zero;
            if (world is VoxelEngine.Cosmos.SphereWorld sphere && sphere.body != null)
                bodyCenter = sphere.body.transform.position;

            float seaRadius = world.SeaLevel * VoxelConstants.VOXEL_SIZE;
            Vector3 seaPoint = bodyCenter + up * seaRadius;
            Vector3Int seaVoxel = world.WorldToVoxel(seaPoint);
            const int scanIn = 256;

            float terrainRadius = 0f;
            for (int i = 0; i >= -scanIn; i--)
            {
                Vector3Int p = seaVoxel + Vector3Int.RoundToInt(up * i);
                var v = world.GetVoxelWorld(p);
                if (!IsTerrain(v)) continue;

                terrainRadius = (((Vector3)p + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE).magnitude + 0.5f * VoxelConstants.VOXEL_SIZE;
                break;
            }

            signedSurface = seaRadius - (worldPosition - bodyCenter).magnitude;

            if (terrainRadius <= 0f)
            {
                // Some sampled points are outside currently generated chunks during
                // streaming. If the point is at/near the sea shell, assume open ocean
                // instead of dropping the whole visual patch for a frame.
                if (signedSurface < -8f) return false;
                depth = 32f;
                return true;
            }

            if (terrainRadius >= seaRadius - 0.05f) return false;

            depth = Mathf.Max(0f, seaRadius - terrainRadius);
            return depth > 0.05f;
        }

        private static bool TrySampleFlatDepth(IVoxelWorld world, Vector3 worldPosition, out float depth, out float surfaceHeight)
        {
            depth = 0f;
            surfaceHeight = 0f;

            Vector3Int vp = world.WorldToVoxel(worldPosition);
            int seaVoxel = Mathf.RoundToInt(world.SeaLevel);
            int top = Mathf.Max(vp.y + 64, seaVoxel + 64);
            int bottom = Mathf.Min(vp.y - 128, seaVoxel - 192);

            int waterY = int.MinValue;
            byte waterLevel = 0;
            for (int y = top; y >= bottom; y--)
            {
                var v = world.GetVoxelWorld(new Vector3Int(vp.x, y, vp.z));
                if (FluidMaterialUtility.IsFluid(v) && FluidMaterialUtility.LiquidFromVoxel(v) == VoxelEngine.Items.LiquidType.Water)
                {
                    waterY = y;
                    waterLevel = v.waterLevel;
                    break;
                }
            }

            if (waterY == int.MinValue) return false;

            int floorY = waterY - 1;
            for (; floorY >= bottom; floorY--)
            {
                var v = world.GetVoxelWorld(new Vector3Int(vp.x, floorY, vp.z));
                if (IsTerrain(v)) break;
            }

            surfaceHeight = (waterY + Mathf.Clamp01(waterLevel / 255f)) * VoxelConstants.VOXEL_SIZE;
            float floorHeight = (floorY + 1) * VoxelConstants.VOXEL_SIZE;
            depth = Mathf.Max(0f, surfaceHeight - floorHeight);
            return depth > 0.01f;
        }

        private static bool TrySamplePlanetDepth(IVoxelWorld world, Vector3 worldPosition, out float depth, out float signedSurface)
        {
            depth = 0f;
            signedSurface = 0f;

            Vector3 up = PlanetWaterUtility.WorldUp(worldPosition);
            Vector3Int center = world.WorldToVoxel(worldPosition);
            const int scanOut = 48;
            const int scanIn = 192;

            float bestWaterRadius = -1f;
            Vector3Int bestWater = center;

            for (int i = scanOut; i >= -scanIn; i--)
            {
                Vector3Int p = center + Vector3Int.RoundToInt(up * i);
                var v = world.GetVoxelWorld(p);
                if (FluidMaterialUtility.IsFluid(v) && FluidMaterialUtility.LiquidFromVoxel(v) == VoxelEngine.Items.LiquidType.Water)
                {
                    Vector3 local = ((Vector3)p + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE;
                    float r = local.magnitude + (v.waterLevel / 255f - 0.5f) * VoxelConstants.VOXEL_SIZE;
                    if (r > bestWaterRadius)
                    {
                        bestWaterRadius = r;
                        bestWater = p;
                    }
                }
            }

            if (bestWaterRadius <= 0f) return false;

            float terrainRadius = 0f;
            for (int i = 0; i >= -scanIn; i--)
            {
                Vector3Int p = bestWater + Vector3Int.RoundToInt(up * i);
                var v = world.GetVoxelWorld(p);
                if (IsTerrain(v))
                {
                    terrainRadius = (((Vector3)p + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE).magnitude + 0.5f * VoxelConstants.VOXEL_SIZE;
                    break;
                }
            }

            if (terrainRadius <= 0f) return false;

            depth = Mathf.Max(0f, bestWaterRadius - terrainRadius);
            signedSurface = bestWaterRadius - (worldPosition - bodyCenter).magnitude;
            return depth > 0.01f;
        }

        private static bool IsTerrain(Voxel v)
        {
            if (!v.IsSolid) return false;
            byte mat = v.material;
            return mat != (byte)MaterialId.WaterLiquid && mat != (byte)MaterialId.WaterVoxel && mat != (byte)MaterialId.CrudeOil;
        }
    }
}
