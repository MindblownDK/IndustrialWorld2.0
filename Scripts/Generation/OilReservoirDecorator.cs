// Assets/Scripts/VoxelEngine/Generation/OilReservoirDecorator.cs
//
// Converts a deep crude-oil marker into one authored geological feature:
// a shallow surface puddle, a narrow vertical bore, and a real underground
// reservoir. It deliberately never scatters ambient oil through ocean water.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Materials;
using VoxelEngine.WaterSim;

namespace VoxelEngine.Generation
{
    public static class OilReservoirDecorator
    {
        private const int MarkerScanStride = 2;
        private const int MaxSurfaceProbeSteps = 192;
        private const int MinimumShaftDepth = 8;
        private const int PendingAttemptLimit = 48;
        private const int PendingRetryBudgetPerChunk = 2;

        private struct PendingReservoir
        {
            public IVoxelWorld World;
            public Vector3Int Marker;
            public int Attempts;
        }

        // A deep chunk can finish before the streamed surface chunks above it. Keep a
        // tiny retry list so the feature waits for a genuine exposed surface instead
        // of treating an unloaded chunk boundary as an underwater oil puddle.
        private static readonly List<PendingReservoir> s_pending = new(32);

        public static void Decorate(Chunk chunk, IVoxelWorld world)
        {
            if (chunk == null || world == null || !chunk.isGenerated) return;

            RetryPending(world);
            if (!FindOilMarker(chunk, out Vector3Int marker)) return;
            if (!ShouldCreateReservoir(marker)) return;

            if (!TryCreateReservoir(world, marker))
                QueuePending(world, marker);
        }

        private static bool FindOilMarker(Chunk chunk, out Vector3Int marker)
        {
            marker = default;
            const int size = VoxelConstants.CHUNK_SIZE;
            int baseX = chunk.coord.x * size;
            int baseY = chunk.coord.y * size;
            int baseZ = chunk.coord.z * size;

            for (int z = 1; z < size - 1; z += MarkerScanStride)
            for (int y = 1; y < size - 1; y += MarkerScanStride)
            for (int x = 1; x < size - 1; x += MarkerScanStride)
            {
                Voxel voxel = chunk.GetVoxelLocal(x, y, z);
                // The marker is an ore voxel inside solid crust. Fluid oil is output
                // from this decorator, never an input candidate.
                if (!voxel.IsSolid || voxel.material != (byte)MaterialId.CrudeOil) continue;
                marker = new Vector3Int(baseX + x, baseY + y, baseZ + z);
                return true;
            }
            return false;
        }

        private static bool ShouldCreateReservoir(Vector3Int marker)
        {
            unchecked
            {
                // Ore markers already occur in clustered veins. One in three anchors
                // becomes a visible extraction site so deposits stay meaningful rather
                // than turning every crude voxel into a surface feature.
                int hash = marker.x * 73856093 ^ marker.y * 19349663 ^ marker.z * 83492791;
                return ((hash & 0x7fffffff) % 3) == 0;
            }
        }

        private static bool TryCreateReservoir(IVoxelWorld world, Vector3Int marker)
        {
            Vector3 up = GetUpDir(world, marker);
            if (!TryFindExposedSurface(world, marker, up, out Vector3Int surface, out int shaftDepth))
                return false;
            if (shaftDepth < MinimumShaftDepth) return true;

            unchecked
            {
                int hash = marker.x * 73856093 ^ marker.y * 19349663 ^ marker.z * 83492791;
                var random = new System.Random(hash);
                int puddleRadius = 3 + random.Next(2);       // 3–4 voxel surface puddle
                int reservoirRadius = 6 + random.Next(3);    // 6–8 voxel underground body

                // The bore joins the puddle to the upper shoulder of the deep pool.
                Vector3 reservoirTop = (Vector3)marker + up * Mathf.Max(1f, reservoirRadius * 0.55f);
                var touched = new HashSet<Chunk>();
                BuildSurfacePuddle(world, surface, up, puddleRadius, touched);
                BuildVerticalBore(world, surface, reservoirTop, up, 1, touched);
                BuildReservoir(world, marker, reservoirRadius, touched);
                FlushTouchedChunks(world, touched);
            }

            return true;
        }

        private static Vector3 GetUpDir(IVoxelWorld world, Vector3Int voxel)
        {
            if (world is VoxelEngine.Cosmos.SphereWorld)
            {
                Vector3 radial = ((Vector3)voxel).normalized;
                return radial.sqrMagnitude > 0.0001f ? radial : Vector3.up;
            }
            return Vector3.up;
        }

        /// <summary>
        /// Finds a true exterior surface. On ocean worlds it deliberately traverses
        /// water from the sea floor to the water/air boundary, preventing an oil bowl
        /// from being carved as a random dark patch below the water surface.
        /// </summary>
        private static bool TryFindExposedSurface(IVoxelWorld world, Vector3Int start, Vector3 up,
            out Vector3Int surface, out int depth)
        {
            surface = default;
            depth = 0;
            Vector3 origin = start;

            for (int step = 0; step < MaxSurfaceProbeSteps; step++)
            {
                Vector3Int currentPos = Vector3Int.RoundToInt(origin + up * step);
                Vector3Int nextPos = Vector3Int.RoundToInt(origin + up * (step + 1));
                if (!TryGetLoadedVoxel(world, currentPos, out Voxel current)
                    || !TryGetLoadedVoxel(world, nextPos, out Voxel next))
                    return false;

                bool currentFluid = FluidMaterialUtility.IsFluid(current);
                bool nextFluid = FluidMaterialUtility.IsFluid(next);

                // Dry land: the top solid voxel is the puddle floor.
                if (current.IsSolid && !next.IsSolid && !nextFluid)
                {
                    surface = currentPos;
                    depth = step;
                    return true;
                }

                // Ocean/lake: use the highest fluid voxel, not the sea floor.
                if (currentFluid && !nextFluid && !next.IsSolid)
                {
                    surface = currentPos;
                    depth = step;
                    return true;
                }
            }
            return false;
        }

        private static void BuildSurfacePuddle(IVoxelWorld world, Vector3Int surface, Vector3 up,
            int radius, HashSet<Chunk> touched)
        {
            GetTangentBasis(up, out Vector3 tangentA, out Vector3 tangentB);
            for (int a = -radius; a <= radius; a++)
            for (int b = -radius; b <= radius; b++)
            {
                if (a * a + b * b > radius * radius) continue;
                Vector3 basePoint = surface + tangentA * a + tangentB * b;
                // Two shallow layers turn a terrain depression or ocean surface into
                // a readable puddle instead of isolated oil speckles.
                WriteOil(world, Vector3Int.RoundToInt(basePoint), touched);
                WriteOil(world, Vector3Int.RoundToInt(basePoint - up), touched);
            }
        }

        private static void BuildVerticalBore(IVoxelWorld world, Vector3Int surface, Vector3 reservoirTop,
            Vector3 up, int radius, HashSet<Chunk> touched)
        {
            Vector3 start = surface - up;
            Vector3 direction = reservoirTop - start;
            int steps = Mathf.Max(1, Mathf.CeilToInt(direction.magnitude));
            GetTangentBasis(up, out Vector3 tangentA, out Vector3 tangentB);

            for (int step = 0; step <= steps; step++)
            {
                Vector3 center = Vector3.Lerp(start, reservoirTop, step / (float)steps);
                for (int a = -radius; a <= radius; a++)
                for (int b = -radius; b <= radius; b++)
                {
                    if (a * a + b * b > radius * radius) continue;
                    WriteOil(world, Vector3Int.RoundToInt(center + tangentA * a + tangentB * b), touched);
                }
            }
        }

        private static void BuildReservoir(IVoxelWorld world, Vector3Int center, int radius, HashSet<Chunk> touched)
        {
            int radiusSquared = radius * radius;
            for (int z = -radius; z <= radius; z++)
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + y * y + z * z > radiusSquared) continue;
                WriteOil(world, center + new Vector3Int(x, y, z), touched);
            }
        }

        private static void GetTangentBasis(Vector3 up, out Vector3 tangentA, out Vector3 tangentB)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(up, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
            tangentA = Vector3.Cross(reference, up).normalized;
            tangentB = Vector3.Cross(up, tangentA).normalized;
        }

        private static bool TryGetLoadedVoxel(IVoxelWorld world, Vector3Int voxel, out Voxel value)
        {
            const int size = VoxelConstants.CHUNK_SIZE;
            Vector3Int coord = new(
                Mathf.FloorToInt(voxel.x / (float)size),
                Mathf.FloorToInt(voxel.y / (float)size),
                Mathf.FloorToInt(voxel.z / (float)size));
            if (!world.TryGetChunk(coord, out Chunk chunk) || chunk == null || !chunk.isGenerated)
            {
                value = default;
                return false;
            }
            value = world.GetVoxelWorld(voxel);
            return true;
        }

        private static void WriteOil(IVoxelWorld world, Vector3Int voxel, HashSet<Chunk> touched)
        {
            const int size = VoxelConstants.CHUNK_SIZE;
            Vector3Int coord = new(
                Mathf.FloorToInt(voxel.x / (float)size),
                Mathf.FloorToInt(voxel.y / (float)size),
                Mathf.FloorToInt(voxel.z / (float)size));
            if (!world.TryGetChunk(coord, out Chunk chunk) || chunk == null || !chunk.isGenerated) return;

            world.SetVoxelWorld(voxel, new Voxel(-1, (byte)MaterialId.CrudeOil, 255), remesh: false);
            touched.Add(chunk);
        }

        private static void FlushTouchedChunks(IVoxelWorld world, HashSet<Chunk> touched)
        {
            foreach (Chunk chunk in touched)
            {
                if (chunk == null || !chunk.isGenerated) continue;
                world.ScheduleMeshJob(chunk);
                FluidManager.Instance?.MarkActive(chunk.coord);
                WaterMeshBuilder.Schedule(chunk);
            }
        }

        private static void QueuePending(IVoxelWorld world, Vector3Int marker)
        {
            for (int i = 0; i < s_pending.Count; i++)
            {
                if (object.ReferenceEquals(s_pending[i].World, world) && s_pending[i].Marker == marker)
                    return;
            }
            s_pending.Add(new PendingReservoir { World = world, Marker = marker, Attempts = 0 });
        }

        private static void RetryPending(IVoxelWorld world)
        {
            int processed = 0;
            for (int i = s_pending.Count - 1; i >= 0 && processed < PendingRetryBudgetPerChunk; i--)
            {
                PendingReservoir pending = s_pending[i];
                if (!object.ReferenceEquals(pending.World, world)) continue;
                processed++;
                if (TryCreateReservoir(world, pending.Marker))
                {
                    s_pending.RemoveAt(i);
                    continue;
                }

                pending.Attempts++;
                if (pending.Attempts >= PendingAttemptLimit) s_pending.RemoveAt(i);
                else s_pending[i] = pending;
            }
        }
    }
}
