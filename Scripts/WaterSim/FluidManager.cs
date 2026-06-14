// Assets/Scripts/VoxelEngine/WaterSim/FluidManager.cs
//
// Manages simulated voxel liquids across chunks. Only active chunks are processed;
// chunks go to sleep once water/oil settles. The system is save-compatible with the
// old waterLevel byte while using voxel material to distinguish Water vs Crude Oil.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Items;

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

        public void MarkActive(Vector3Int chunkCoord)
        {
            if (_activeChunks.Add(chunkCoord)) _workQueue.Enqueue(chunkCoord);
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            float interval = 1f / Mathf.Max(0.1f, tickRate);
            if (_timer < interval) return;
            _timer -= interval;

            var world = VoxelWorld.Instance;
            if (world == null) return;

            int budget = maxChunksPerTick;
            int processed = 0;
            var toRemove = new List<Vector3Int>();

            int queueSize = _workQueue.Count;
            for (int q = 0; q < queueSize && processed < budget; q++)
            {
                var coord = _workQueue.Dequeue();
                if (!world.TryGetChunk(coord, out var chunk) || !chunk.isGenerated)
                {
                    _activeChunks.Remove(coord);
                    continue;
                }

                world.CompleteMeshJobForChunk(chunk);

                var changed = new NativeArray<int>(1, Allocator.TempJob);
                bool didChange;
                try
                {
                    var job = new FluidSimJob
                    {
                        voxels     = chunk.voxels,
                        chunkSize  = VoxelConstants.CHUNK_SIZE,
                        chunkSizeP = VoxelConstants.CHUNK_SIZE_P,
                        changed    = changed
                    };
                    job.Run();
                    didChange = changed[0] != 0;
                }
                finally
                {
                    if (changed.IsCreated) changed.Dispose();
                }

                if (didChange)
                {
                    _workQueue.Enqueue(coord);
                    chunk.isDirty = true;
                    WakeNeighbour(world, coord + new Vector3Int(1, 0, 0));
                    WakeNeighbour(world, coord + new Vector3Int(-1, 0, 0));
                    WakeNeighbour(world, coord + new Vector3Int(0, 0, 1));
                    WakeNeighbour(world, coord + new Vector3Int(0, 0, -1));
                    WakeNeighbour(world, coord + new Vector3Int(0, -1, 0));
                    WakeNeighbour(world, coord + new Vector3Int(0, 1, 0));
                    WaterMeshBuilder.Schedule(chunk);
                }
                else toRemove.Add(coord);

                processed++;
            }

            foreach (var c in toRemove) _activeChunks.Remove(c);
        }

        private void WakeNeighbour(VoxelWorld world, Vector3Int coord)
        {
            if (world.TryGetChunk(coord, out var ch) && ch.isGenerated) MarkActive(coord);
        }

        public void PlaceWater(Vector3Int worldVoxel, byte level = 255) => PlaceLiquid(worldVoxel, LiquidType.Water, level);
        public void PlaceOil(Vector3Int worldVoxel, byte level = 255) => PlaceLiquid(worldVoxel, LiquidType.CrudeOil, level);

        public void PlaceLiquid(Vector3Int worldVoxel, LiquidType liquid, byte level = 255)
        {
            var world = VoxelWorld.Instance;
            if (world == null || !TryGetChunkAndLocal(world, worldVoxel, out var coord, out var ch, out int lx, out int ly, out int lz)) return;

            var v = ch.GetVoxelLocal(lx, ly, lz);
            if (v.IsSolid) return;

            FluidMaterialUtility.SetLiquid(ref v, liquid, level);
            ch.SetVoxelLocal(lx, ly, lz, v);
            ch.isDirty = true;
            MarkActive(coord);
            WaterMeshBuilder.Schedule(ch);
        }

        public bool DrainWater(Vector3Int worldVoxel) => DrainLiquid(worldVoxel, LiquidType.Water, 255) > 0;
        public bool DrainOil(Vector3Int worldVoxel) => DrainLiquid(worldVoxel, LiquidType.CrudeOil, 255) > 0;

        /// <summary>Drain up to maxLevel fluid units from one voxel. Returns drained byte-volume.</summary>
        public byte DrainLiquid(Vector3Int worldVoxel, LiquidType liquid, byte maxLevel = 255)
        {
            var world = VoxelWorld.Instance;
            if (world == null || !TryGetChunkAndLocal(world, worldVoxel, out var coord, out var ch, out int lx, out int ly, out int lz)) return 0;

            var v = ch.GetVoxelLocal(lx, ly, lz);
            if (!FluidMaterialUtility.Matches(v, liquid)) return 0;

            byte drained = v.waterLevel < maxLevel ? v.waterLevel : maxLevel;
            v.waterLevel = (byte)(v.waterLevel - drained);
            if (v.waterLevel == 0) FluidMaterialUtility.ClearLiquid(ref v);
            ch.SetVoxelLocal(lx, ly, lz, v);
            ch.isDirty = true;
            MarkActive(coord);
            WaterMeshBuilder.Schedule(ch);
            return drained;
        }

        public byte GetWaterLevel(Vector3Int worldVoxel) => GetLiquidLevel(worldVoxel, LiquidType.Water);

        public byte GetLiquidLevel(Vector3Int worldVoxel, LiquidType liquid)
        {
            var world = VoxelWorld.Instance;
            if (world == null) return 0;
            var v = world.GetVoxelWorld(worldVoxel);
            return FluidMaterialUtility.Matches(v, liquid) ? v.waterLevel : (byte)0;
        }

        public LiquidType GetLiquidType(Vector3Int worldVoxel)
        {
            var world = VoxelWorld.Instance;
            if (world == null) return LiquidType.Water;
            return FluidMaterialUtility.LiquidFromVoxel(world.GetVoxelWorld(worldVoxel));
        }

        private static bool TryGetChunkAndLocal(VoxelWorld world, Vector3Int worldVoxel, out Vector3Int coord, out Chunk ch, out int lx, out int ly, out int lz)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            coord = new Vector3Int(
                Mathf.FloorToInt(worldVoxel.x / (float)S),
                Mathf.FloorToInt(worldVoxel.y / (float)S),
                Mathf.FloorToInt(worldVoxel.z / (float)S));
            ch = null;
            lx = worldVoxel.x - coord.x * S;
            ly = worldVoxel.y - coord.y * S;
            lz = worldVoxel.z - coord.z * S;
            return world.TryGetChunk(coord, out ch);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
