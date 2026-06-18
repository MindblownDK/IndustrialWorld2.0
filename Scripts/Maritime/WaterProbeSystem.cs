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

            var world = VoxelWorld.Instance;

            for (int i = 0; i < n; i++)
            {
                float3 p = positions[i];
                outHeights[i] = SampleColumn(world, p.x, p.z);
            }
        }

        /// <summary>Single-point world-space surface height (main thread only).</summary>
        public static float GetSurfaceHeight(float worldX, float worldZ)
        {
            int frame = Time.frameCount;
            if (frame != _cachedFrame) { _columnCache.Clear(); _cachedFrame = frame; }
            return SampleColumn(VoxelWorld.Instance, worldX, worldZ);
        }

        private static float SampleColumn(VoxelWorld world, float wx, float wz)
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
        private static float ComputeColumnHeight(VoxelWorld world, int ix, int iz)
        {
            if (world == null) return NoWaterHeight;

            const int ceilingVoxels = 96;
            const int floorVoxels = 96;
            int topY = Mathf.RoundToInt(SeaLevel / VOXEL_SIZE) + ceilingVoxels;

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

        /// <summary>
        /// Estimated local water flow velocity at a world position (m/s).
        /// Samples the chunk flow field (computed by FlowFieldManager); falls back
        /// to a gentle global current so waterwheels always have a usable input.
        /// </summary>
        public static float3 GetWaterFlow(float3 worldPos)
        {
            var world = VoxelWorld.Instance;
            if (world != null)
            {
                int vx = Mathf.RoundToInt(worldPos.x / VOXEL_SIZE);
                int vy = Mathf.RoundToInt(worldPos.y / VOXEL_SIZE);
                int vz = Mathf.RoundToInt(worldPos.z / VOXEL_SIZE);
                var coord = new Vector3Int(
                    Mathf.FloorToInt(vx / (float)CHUNK_SIZE),
                    Mathf.FloorToInt(vy / (float)CHUNK_SIZE),
                    Mathf.FloorToInt(vz / (float)CHUNK_SIZE));

                if (world.TryGetChunk(coord, out var chunk) && chunk != null && chunk.isGenerated)
                {
                    int lx = vx - coord.x * CHUNK_SIZE;
                    int lz = vz - coord.z * CHUNK_SIZE;
                    Vector2 flow = chunk.GetFlow(lx, lz);
                    return new float3(flow.x, 0f, flow.y);
                }
            }
            // Fallback: a mild prevailing current toward +X.
            return new float3(0.4f, 0f, 0f);
        }

        /// <summary>Clear the column cache (call on world load / region change).</summary>
        public static void InvalidateCache()
        {
            _columnCache.Clear();
            _cachedFrame = -1;
        }
    }
}
