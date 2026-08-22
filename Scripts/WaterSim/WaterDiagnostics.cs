// Assets/Scripts/VoxelEngine/WaterSim/WaterDiagnostics.cs
//
// 9.16.0 field round — world-load water diagnostics, delivered BOTH to the console
// and as an on-screen toast so a field report can pin down a water failure from a
// single screenshot. Two independent probes:
//
//   1) FIELD ANALYSIS — evaluates the planet's own noise field over 1024 directions
//      (no chunk streaming involved). oceanCols counts directions whose surface sits
//      below the waterline — "does this planet even HAVE ocean basins by design?"
//
//   2) VOXEL SCAN — a wide ±20-chunk box around the viewer counts generated water /
//      other-liquid voxels plus near-viewer rendered water meshes.
//
//   Interpretation:
//     oceanCols = 0            → the planet's field has NO ocean basins → template /
//                                sea-level tuning (water was never generated ANYWHERE)
//     oceanCols > 0, boxWater=0→ basins exist but no generated water near the player
//     boxWater > 0, nearMeshes=0 → water exists but the surface never meshes/renders
//
// Pure diagnostics — no gameplay effect; heavy work happens once per world load.
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;

namespace VoxelEngine.WaterSim
{
    public static class WaterDiagnostics
    {
        public static void LogWorldState(IVoxelWorld world, Transform viewer, out string summary)
        {
            summary = string.Empty;
            if (world == null || viewer == null) return;

            // ── 1) Field analysis: how much of the planet is ocean by design? ──
            int oceanCols = 0;
            int waterCellsEstimate = 0;
            const int N = 1024;
            if (world is SphereWorld sphere && sphere.body != null)
            {
                var prm = sphere.body.genParams;
                for (int i = 0; i < N; i++)
                {
                    // Golden-angle spiral — uniform directions on the sphere.
                    float y = 1f - 2f * (i + 0.5f) / N;
                    float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                    float phi = i * 2.399963f;
                    var dir = new float3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);

                    float surf = VoxelEngine.GpuVoxel.PlanetField.SurfaceRadius(
                        prm.seed, dir, prm.radiusWorld, prm.baseHeight, prm.seaRadius,
                        prm.continentScaleDir, prm.mountainScale);
                    if (surf < prm.seaRadius - 1f)
                    {
                        oceanCols++;
                        waterCellsEstimate += Mathf.Max(1, (int)Mathf.Min(64f,
                            Mathf.Floor(prm.seaRadius - surf)));
                    }
                }
            }

            // ── 2) Voxel scan: wide generated-chunk box around the viewer. ──
            Vector3Int center = world.WorldToChunk(viewer.position);
            int nearWater = 0, nearOil = 0, nearMeshes = 0, boxWater = 0, boxOil = 0;
            const int S = VoxelConstants.CHUNK_SIZE;

            for (int z = -20; z <= 20; z++)
            for (int y = -20; y <= 20; y++)
            for (int x = -20; x <= 20; x++)
            {
                var coord = center + new Vector3Int(x, y, z);
                if (!world.TryGetChunk(coord, out var chunk) || chunk == null || !chunk.isGenerated) continue;

                int water = 0, other = 0;
                for (int cz = 0; cz < S; cz += 4)
                for (int cy = 0; cy < S; cy += 4)
                for (int cx = 0; cx < S; cx += 4)
                {
                    var v = chunk.GetVoxelLocal(cx, cy, cz);
                    if (!FluidMaterialUtility.IsFluid(v)) continue;
                    if (FluidMaterialUtility.LiquidFromVoxel(v) == VoxelEngine.Items.LiquidType.Water) water++;
                    else other++;
                }
                if (water <= 0 && other <= 0) continue;

                boxWater += water; boxOil += other;
                bool isNear = Mathf.Abs(x) + Mathf.Abs(y) + Mathf.Abs(z) <= 4;
                if (isNear)
                {
                    nearWater += water; nearOil += other;
                    if (water > 0 && chunk.waterMeshGO != null && chunk.waterMeshGO.activeSelf
                        && chunk.waterMesh != null && chunk.waterMesh.vertexCount > 0)
                        nearMeshes++;
                }
            }

            summary = $"oceanCols={oceanCols}/{N} nearWater={nearWater} nearOil={nearOil} " +
                      $"boxWater={boxWater} boxOil={boxOil} nearMeshes={nearMeshes} " +
                      $"seaLevel={world.SeaLevel} rendering={(WaterMeshBuilder.RenderingEnabled ? 1 : 0)}";
            Debug.Log("[WaterDiagnostics] " + summary
                      + "  (oceanCols=0 → field has NO ocean basins | oceanCols>0+boxWater=0 → basins exist but no generated water nearby"
                      + " | boxWater>0+nearMeshes=0 → mesh/render)");
        }
    }
}
