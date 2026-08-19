// Assets/Scripts/VoxelEngine/Maritime/WaterProbeSystem.cs
//
// High-performance batched water-surface sampling.
//
//   WaterProbeSystem.GetWavesHeights(positions, outWaterHeights)
//
// Mirrors the URP "WaterSim.GetWavesHeights" API the design calls for, but is
// driven by IndustrialWorld's own voxel FluidManager (waterLevel bytes in the
// chunk grid) so it works with the existing ocean simulation — no Unity Water
// package dependency.
//
// Usage contract:
//   1. The main thread calls GetWavesHeights() once per FixedUpdate with the
//      NativeArray of block centres.
//   2. The returned heights are handed (read-only) into the Burst buoyancy job.
//
// All chunk access happens OUTSIDE the job (it touches managed VoxelWorld),
// then the heights are consumed inside the job — fully Burst-safe.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.WaterSim;

namespace VoxelEngine.Maritime
{
    public static class WaterProbeSystem
    {
        /// <summary>World-space Y of the intended ocean datum.</summary>
        public const float SeaLevel = 0f;
        /// <summary>Sentinel height used when a column contains no water, preventing dry-land buoyancy.</summary>
        public const float NoWaterHeight = -1000000f;

        private const int CHUNK_SIZE = VoxelConstants.CHUNK_SIZE;
        private const float VOXEL_SIZE = VoxelConstants.VOXEL_SIZE;

        // ── Per-frame column cache ──────────────────────────────────────
        // Water surface height only changes between fluid ticks (FluidManager ~15 Hz),
        // so caching per integer (x,z) column for one frame collapses thousands of
        // repeated probes into ~one voxel scan per unique column.
        private static readonly Dictionary<int, float> _columnCache = new(512);
        private static int _cachedFrame = -1;

        /// <summary>
        /// Batched water-surface height query. For every world position in
        /// <paramref name="positions"/> writes the world-space Y of the water
        /// surface directly above that XZ column into <paramref name="outHeights"/>.
        /// </summary>
        /// <param name="positions">Block centres (world space).</param>
        /// <param name="outHeights">Pre-allocated array, same length as positions.</param>
        public static void GetWavesHeights(in NativeArray<float3> positions, NativeArray<float> outHeights)
        {
            int n = positions.Length;
            if (n == 0 || !outHeights.IsCreated) return;

            int frame = Time.frameCount;
            if (frame != _cachedFrame) { _columnCache.Clear(); _cachedFrame = frame; }

            var world = VoxelEngine.Core.ActiveWorld.Current;

            for (int i = 0; i < n; i++)
            {
                float3 p = positions[i];
                outHeights[i] = SampleSurface(world, new Vector3(p.x, p.y, p.z));
            }
        }

        /// <summary>Single-point world-space surface height (main thread only).</summary>
        public static float GetSurfaceHeight(float worldX, float worldZ)
        {
            int frame = Time.frameCount;
            if (frame != _cachedFrame) { _columnCache.Clear(); _cachedFrame = frame; }
            return SampleColumn(VoxelEngine.Core.ActiveWorld.Current, worldX, worldZ);
        }

        public static float GetSurfaceHeight(Vector3 worldPosition)
        {
            int frame = Time.frameCount;
            if (frame != _cachedFrame) { _columnCache.Clear(); _cachedFrame = frame; }
            return SampleSurface(VoxelEngine.Core.ActiveWorld.Current, worldPosition);
        }

        public static float GetSubmergence(Vector3 worldPosition, float probeRadius = 0.5f)
        {
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world is VoxelEngine.Cosmos.SphereWorld)
                return SamplePlanetDensity(world, worldPosition, probeRadius);

            float h = SampleColumn(world, worldPosition.x, worldPosition.z);
            if (h <= NoWaterHeight * 0.5f) return 0f;
            return Mathf.Clamp01((h - (worldPosition.y - probeRadius)) / Mathf.Max(probeRadius * 2f, 0.001f));
        }

        private static float SampleSurface(VoxelEngine.Core.IVoxelWorld world, Vector3 worldPosition)
        {
            if (world is VoxelEngine.Cosmos.SphereWorld)
                return SamplePlanetSignedWaterHeight(world, worldPosition);
            return SampleColumn(world, worldPosition.x, worldPosition.z);
        }

        private static float SampleColumn(VoxelEngine.Core.IVoxelWorld world, float wx, float wz)
        {
            // Cache key: quantise to whole voxels in XZ.
            int ix = Mathf.RoundToInt(wx / VOXEL_SIZE);
            int iz = Mathf.RoundToInt(wz / VOXEL_SIZE);
            int key = (ix * 73856093) ^ (iz * 19349663);

            if (_columnCache.TryGetValue(key, out float cached)) return cached;

            float h = ComputeColumnHeight(world, ix, iz);
            _columnCache[key] = h;
            return h;
        }

        /// <summary>
        /// Scan a vertical voxel column for the topmost fluid voxel and return
        /// its world-space surface Y (voxel top + fractional water level).
        /// </summary>
        private static float ComputeColumnHeight(VoxelEngine.Core.IVoxelWorld world, int ix, int iz)
        {
            if (world == null) return NoWaterHeight;

            const int ceilingVoxels = 96;
            const int floorVoxels = 96;
            
            // Use the world's actual sea level (for spheres, this is the radial sea radius).
            float seaLevel = world.SeaLevel * VOXEL_SIZE;
            int topY = Mathf.RoundToInt(seaLevel / VOXEL_SIZE) + ceilingVoxels;

            for (int y = topY; y >= topY - ceilingVoxels - floorVoxels; y--)
            {
                var voxel = world.GetVoxelWorld(new Vector3Int(ix, y, iz));
                if (!voxel.IsSolid && voxel.HasWater)
                {
                    // Fractional fill of this voxel (0..1).
                    float fill = voxel.waterLevel / 255f;
                    float voxelTopY = (y + 1) * VOXEL_SIZE;            // top face of voxel
                    return voxelTopY - VOXEL_SIZE * (1f - fill);        // adjust down by unfilled portion
                }
            }
            return NoWaterHeight;
        }

        private static float SamplePlanetSignedWaterHeight(VoxelEngine.Core.IVoxelWorld world, Vector3 worldPosition)
        {
            if (world == null) return NoWaterHeight;

            Vector3Int center = world.WorldToVoxel(worldPosition);
            Vector3 localProbe = ((Vector3)center + Vector3.one * 0.5f) * VOXEL_SIZE;
            Vector3 radialUp = PlanetWaterUtility.LocalUp(localProbe);
            int scan = 10;
            float bestRadius = -1f;

            for (int i = -scan; i <= scan; i++)
            {
                Vector3Int p = center + Vector3Int.RoundToInt(radialUp * i);
                var voxel = world.GetVoxelWorld(p);
                if (!voxel.IsSolid && voxel.HasWater)
                {
                    Vector3 lp = ((Vector3)p + Vector3.one * 0.5f) * VOXEL_SIZE;
                    float r = lp.magnitude + (voxel.waterLevel / 255f - 0.5f) * VOXEL_SIZE;
                    if (r > bestRadius) bestRadius = r;
                }
            }

            if (bestRadius <= 0f) return NoWaterHeight;
            Vector3 localWorld = PlanetWaterUtility.VoxelToLocalPosition((Vector3)center);
            return bestRadius - Mathf.Max(0.0001f, localWorld.magnitude);
        }

        private static float SamplePlanetDensity(VoxelEngine.Core.IVoxelWorld world, Vector3 worldPosition, float probeRadius)
        {
            if (world == null) return 0f;
            Vector3 halfExtents = new Vector3(probeRadius, probeRadius, probeRadius);
            float total = 0f;
            Vector3[] pts = new Vector3[8]
            {
                worldPosition + new Vector3(-halfExtents.x, -halfExtents.y, -halfExtents.z),
                worldPosition + new Vector3( halfExtents.x, -halfExtents.y, -halfExtents.z),
                worldPosition + new Vector3(-halfExtents.x,  halfExtents.y, -halfExtents.z),
                worldPosition + new Vector3( halfExtents.x,  halfExtents.y, -halfExtents.z),
                worldPosition + new Vector3(-halfExtents.x, -halfExtents.y,  halfExtents.z),
                worldPosition + new Vector3( halfExtents.x, -halfExtents.y,  halfExtents.z),
                worldPosition + new Vector3(-halfExtents.x,  halfExtents.y,  halfExtents.z),
                worldPosition + new Vector3( halfExtents.x,  halfExtents.y,  halfExtents.z)
            };
            for (int i = 0; i < 8; i++) total += PlanetWaterUtility.SampleDensityAtWorldPos(pts[i]);
            return total / 8f;
        }

        /// <summary>
        /// Estimated local water flow velocity at a world position (m/s).
        /// Samples the chunk flow field (computed by FlowFieldManager); falls back
        /// to a gentle global current so waterwheels always have a usable input.
        /// </summary>
        public static float3 GetWaterFlow(float3 worldPos)
        {
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world != null)
            {
                // SphereWorld voxel coordinates are body-local. Never derive them from raw
                // scene X/Y/Z or currents drift to the wrong side of an offset planet.
                Vector3Int voxel = world.WorldToVoxel(new Vector3(worldPos.x, worldPos.y, worldPos.z));
                var coord = new Vector3Int(
                    Mathf.FloorToInt(voxel.x / (float)CHUNK_SIZE),
                    Mathf.FloorToInt(voxel.y / (float)CHUNK_SIZE),
                    Mathf.FloorToInt(voxel.z / (float)CHUNK_SIZE));

                if (world.TryGetChunk(coord, out var chunk) && chunk != null && chunk.isGenerated)
                {
                    int lx = voxel.x - coord.x * CHUNK_SIZE;
                    int lz = voxel.z - coord.z * CHUNK_SIZE;
                    Vector2 flow = chunk.GetFlow(lx, lz);
                    if (world is VoxelEngine.Cosmos.SphereWorld)
                    {
                        Vector3 local = ((Vector3)voxel + Vector3.one * 0.5f) * VOXEL_SIZE;
                        Vector3 up = PlanetWaterUtility.LocalUp(local);
                        Vector3 east = Vector3.Cross(Vector3.up, up);
                        if (east.sqrMagnitude < 0.001f) east = Vector3.Cross(Vector3.forward, up);
                        east.Normalize();
                        Vector3 north = Vector3.Cross(up, east).normalized;
                        Vector3 tangentFlow = east * flow.x + north * flow.y;
                        float tide = PlanetWaterUtility.MoonWaveEnergy(local) - 1f;
                        tangentFlow += north * tide * 0.35f;
                        return new float3(tangentFlow.x, tangentFlow.y, tangentFlow.z);
                    }
                    return new float3(flow.x, 0f, flow.y);
                }
            }
            if (VoxelEngine.Core.ActiveWorld.Current is VoxelEngine.Cosmos.SphereWorld)
            {
                Vector3 up = PlanetWaterUtility.WorldUp(new Vector3(worldPos.x, worldPos.y, worldPos.z));
                Vector3 tangent = Vector3.Cross(up, Vector3.forward);
                if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(up, Vector3.right);
                tangent.Normalize();
                return new float3(tangent.x, tangent.y, tangent.z) * 0.25f;
            }
            return new float3(0.4f, 0f, 0f);
        }

        /// <summary>Clear the column cache (call on world load / region change).</summary>
        public static void InvalidateCache()
        {
            _columnCache.Clear();
            _cachedFrame = -1;
        }

        /// <summary>
        /// Hands a moving submerged grid to the in-house wake registry. The registry projects
        /// each stamp to the actual radial water surface, so wakes wrap correctly around
        /// spherical planets instead of being written into a flat XZ texture.
        /// </summary>
        public static void RegisterShipWake(Vector3 shipPos, Vector3 velocity, float hullSize)
        {
            VoxelEngine.WaterSim.NativeWaterWakeSystem.RegisterWake(shipPos, velocity, hullSize);
        }

    }
}
