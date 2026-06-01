// Assets/Scripts/VoxelEngine/WaterSim/FluidManager.cs
//
// Manages fluid simulation across all chunks. Only processes "active" chunks
// (those with moving water). Chunks go to sleep when water settles.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    public class FluidManager : MonoBehaviour
    {
        public static FluidManager Instance { get; private set; }

        [Header("Simulation")]
        [Tooltip("Ticks per second.")]
        public float tickRate = 15f;
        [Tooltip("Max chunks to simulate per tick.")]
        public int maxChunksPerTick = 4;
        [Tooltip("Chunks within this radius of the player are eligible.")]
        public int activeRadius = 6;

        private readonly HashSet<Vector3Int> _activeChunks = new();
        private readonly Queue<Vector3Int> _workQueue = new();
        private float _timer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("FluidManager");
            Instance = go.AddComponent<FluidManager>();
            DontDestroyOnLoad(go);
        }

        /// <summary>Mark a chunk as having active water — it will be simulated.</summary>
        public void MarkActive(Vector3Int chunkCoord)
        {
            if (_activeChunks.Add(chunkCoord))
                _workQueue.Enqueue(chunkCoord);
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            float interval = 1f / tickRate;
            if (_timer < interval) return;
            _timer -= interval;

            var world = VoxelWorld.Instance;
            if (world == null) return;

            int budget = maxChunksPerTick;
            int processed = 0;
            var toRemove = new List<Vector3Int>();

            // Process active chunks.
            int queueSize = _workQueue.Count;
            for (int q = 0; q < queueSize && processed < budget; q++)
            {
                var coord = _workQueue.Dequeue();
                if (!world.TryGetChunk(coord, out var chunk) || !chunk.isGenerated)
                {
                    _activeChunks.Remove(coord);
                    continue;
                }

                // Run the fluid sim job synchronously (small, fast with Burst).
                var changed = new NativeArray<int>(1, Allocator.TempJob);
                var job = new FluidSimJob
                {
                    voxels = chunk.voxels,
                    chunkSize = VoxelConstants.CHUNK_SIZE,
                    chunkSizeP = VoxelConstants.CHUNK_SIZE_P,
                    changed = changed
                };

                // Complete the job inline (it's fast enough for 1 chunk).
                job.Run();

                bool didChange = changed[0] != 0;
                changed.Dispose();

                if (didChange)
                {
                    // Re-queue for next tick.
                    _workQueue.Enqueue(coord);
                    // Mark chunk dirty for re-meshing.
                    chunk.isDirty = true;
                    // Also wake horizontal neighbours.
                    WakeNeighbour(world, coord + new Vector3Int(1, 0, 0));
                    WakeNeighbour(world, coord + new Vector3Int(-1, 0, 0));
                    WakeNeighbour(world, coord + new Vector3Int(0, 0, 1));
                    WakeNeighbour(world, coord + new Vector3Int(0, 0, -1));
                    WakeNeighbour(world, coord + new Vector3Int(0, -1, 0));
                    // Schedule water mesh rebuild.
                    WaterMeshBuilder.Schedule(chunk);
                }
                else
                {
                    // Chunk settled — put to sleep.
                    toRemove.Add(coord);
                }

                processed++;
            }

            foreach (var c in toRemove) _activeChunks.Remove(c);
        }

        private void WakeNeighbour(VoxelWorld world, Vector3Int coord)
        {
            if (world.TryGetChunk(coord, out var ch) && ch.isGenerated)
                MarkActive(coord);
        }

        /// <summary>Place water at a world voxel position.</summary>
        public void PlaceWater(Vector3Int worldVoxel, byte level = 255)
        {
            var world = VoxelWorld.Instance;
            if (world == null) return;
            const int S = VoxelConstants.CHUNK_SIZE;

            var coord = new Vector3Int(
                Mathf.FloorToInt(worldVoxel.x / (float)S),
                Mathf.FloorToInt(worldVoxel.y / (float)S),
                Mathf.FloorToInt(worldVoxel.z / (float)S));

            if (!world.TryGetChunk(coord, out var ch)) return;

            int lx = worldVoxel.x - coord.x * S;
            int ly = worldVoxel.y - coord.y * S;
            int lz = worldVoxel.z - coord.z * S;

            var v = ch.GetVoxelLocal(lx, ly, lz);
            if (v.IsSolid) return; // can't place water in solid

            v.waterLevel = level;
            ch.SetVoxelLocal(lx, ly, lz, v);
            MarkActive(coord);
        }

        /// <summary>Remove water at a world voxel. Returns true if water was there.</summary>
        public bool DrainWater(Vector3Int worldVoxel)
        {
            var world = VoxelWorld.Instance;
            if (world == null) return false;
            const int S = VoxelConstants.CHUNK_SIZE;

            var coord = new Vector3Int(
                Mathf.FloorToInt(worldVoxel.x / (float)S),
                Mathf.FloorToInt(worldVoxel.y / (float)S),
                Mathf.FloorToInt(worldVoxel.z / (float)S));

            if (!world.TryGetChunk(coord, out var ch)) return false;

            int lx = worldVoxel.x - coord.x * S;
            int ly = worldVoxel.y - coord.y * S;
            int lz = worldVoxel.z - coord.z * S;

            var v = ch.GetVoxelLocal(lx, ly, lz);
            if (v.waterLevel == 0) return false;

            v.waterLevel = 0;
            ch.SetVoxelLocal(lx, ly, lz, v);
            MarkActive(coord);
            return true;
        }

        /// <summary>Get water level at a world position (0..255).</summary>
        public byte GetWaterLevel(Vector3Int worldVoxel)
        {
            var world = VoxelWorld.Instance;
            if (world == null) return 0;
            var v = world.GetVoxelWorld(worldVoxel);
            return v.waterLevel;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
