// Assets/Scripts/VoxelEngine/WaterSim/FlowFieldManager.cs
//
// Computes per-chunk surface flow velocity from voxel pressure gradients.
// The resulting Vector2[] is stored on each Chunk and consumed by WaterMeshBuilder
// to encode flow direction into UV2, which the VoxelWaterURP shader uses for
// flow-mapped normals and dynamic foam.
//
// Flow is derived from the pressure gradient at each surface cell:
//   • Pressure = waterLevel (byte) + height contribution
//   • Gradient points from high to low pressure
//   • Magnitude proportional to pressure difference
//   • Inertia blending: new flow is blended with previous (60% old, 40% new)
//     so still water calms gradually and flowing water has momentum
//   • Decay: when no pressure gradient exists, flow decays exponentially

using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;

namespace VoxelEngine.WaterSim
{
    public static class FlowFieldManager
    {
        /// <summary>
        /// Recompute the flow field for a chunk from its current voxel state.
        /// Call this after the FluidSimJob has completed and written results.
        /// </summary>
        public static void UpdateFlowField(Chunk chunk)
        {
            if (chunk == null || !chunk.isGenerated) return;
            const int S = VoxelConstants.CHUNK_SIZE;

            chunk.EnsureFlowField();

            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                // Find the topmost fluid cell in this column
                int topY = -1;
                byte topLevel = 0;
                byte topMat = 0;

                for (int y = S - 1; y >= 0; y--)
                {
                    var v = chunk.GetVoxelLocal(x, y, z);
                    if (v.waterLevel > 0 && !v.IsSolid)
                    {
                        topY = y;
                        topLevel = v.waterLevel;
                        topMat = v.material;
                        break;
                    }
                }

                // No fluid in this column — decay existing flow
                if (topY < 0)
                {
                    chunk.flowField[x + z * S] *= 0.5f;
                    continue;
                }

                // Compute pressure gradient from 4 horizontal neighbors at same Y
                Vector2 flow = Vector2.zero;
                bool isOil = topMat == (byte)MaterialId.CrudeOil;

                // Pressure = level + small height factor (higher cells push harder)
                float centerPressure = topLevel + topY * 2f;

                AccumulateGradient(chunk, x + 1, topY, z, centerPressure, ref flow, 1, 0, isOil);
                AccumulateGradient(chunk, x - 1, topY, z, centerPressure, ref flow, -1, 0, isOil);
                AccumulateGradient(chunk, x, topY, z + 1, centerPressure, ref flow, 0, 1, isOil);
                AccumulateGradient(chunk, x, topY, z - 1, centerPressure, ref flow, 0, -1, isOil);

                // Also check one cell above and below for vertical influence on flow
                // (e.g., waterfall edge where water is falling = strong downward flow)
                if (topLevel < 240)
                {
                    // Not full — check if there's a drop below
                    if (topY > 0)
                    {
                        var below = chunk.GetVoxelLocal(x, topY - 1, z);
                        if (!below.IsSolid && below.waterLevel < 200)
                        {
                            // Water is falling — this creates flow spreading at the edge
                            // (waterfall edges fan out slightly)
                            flow *= 1.3f;
                        }
                    }
                }

                // Normalize and scale the flow vector
                float magnitude = flow.magnitude;
                if (magnitude > 0.01f)
                {
                    // Scale: raw pressure differences can be large; normalize to 0..1 range
                    float normalizedMag = Mathf.Min(magnitude / 200f, 1f);

                    // Oil flows slower — reduce the visual flow speed
                    if (isOil) normalizedMag *= 0.35f;

                    flow = flow.normalized * normalizedMag;
                }

                // Inertia blending: 40% new flow, 60% previous flow
                Vector2 prev = chunk.flowField[x + z * S];
                Vector2 blended = Vector2.Lerp(prev, flow, 0.4f);

                // Clamp maximum flow speed
                if (blended.magnitude > 1f) blended = blended.normalized;

                chunk.flowField[x + z * S] = blended;
            }
        }

        private static void AccumulateGradient(Chunk chunk, int nx, int ny, int nz,
            float centerPressure, ref Vector2 flow, int dx, int dz, bool isOil)
        {
            const int S = VoxelConstants.CHUNK_SIZE;

            // Out of chunk bounds — estimate pressure as zero (open edge = low pressure)
            if (nx < 0 || nx >= S || nz < 0 || nz >= S)
            {
                // Edge of chunk: assume neighbour has lower pressure
                float edgePressure = 0f;
                float diff = centerPressure - edgePressure;
                if (diff > 0)
                {
                    flow.x += diff * dx;
                    flow.y += diff * dz;
                }
                return;
            }

            if (ny < 0 || ny >= S) return;

            var v = chunk.GetVoxelLocal(nx, ny, nz);

            // Solid terrain = wall, no flow toward it
            if (v.IsSolid) return;

            float neighborPressure;

            if (v.waterLevel > 0 && !v.IsSolid)
            {
                // Same or different liquid neighbour
                bool neighborOil = v.material == (byte)MaterialId.CrudeOil;

                // Oil and water don't mix in flow gradient — treat as wall
                if (isOil != neighborOil)
                {
                    return;
                }

                neighborPressure = v.waterLevel + ny * 2f;
            }
            else
            {
                // Air / empty = low pressure
                neighborPressure = 0f;
            }

            float pressureDiff = centerPressure - neighborPressure;
            if (pressureDiff > 0)
            {
                flow.x += pressureDiff * dx;
                flow.y += pressureDiff * dz;
            }
        }
    }
}
