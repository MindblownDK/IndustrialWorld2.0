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
            signedSurface = bestWaterRadius - worldPosition.magnitude;
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
