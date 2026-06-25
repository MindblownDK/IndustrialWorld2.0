// Assets/Scripts/VoxelEngine/WaterSim/FluidManager.cs
//
// Manages simulated voxel liquids across chunks. Only active chunks are processed;
// chunks go to sleep once water/oil settles. The system is save-compatible with the
// old waterLevel byte while using voxel material to distinguish Water vs Crude Oil.
//
// V2: Integrates FlowFieldManager for pressure-gradient surface flow velocity,
//     which is consumed by WaterMeshBuilder for KWS2-quality flow-mapped rendering.

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

            var world = VoxelEngine.Core.ActiveWorld.Current;
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

                world.CompleteGenJobForChunk(chunk);
                world.CompleteMeshJobForChunk(chunk);

                var changed = new NativeArray<int>(1, Allocator.TempJob);
                bool didChange;
                try
                {
                    int downX = 0, downY = -1, downZ = 0;
                    if (world is VoxelEngine.Cosmos.SphereWorld) {
                        Vector3 worldOrigin = chunk.coord * VoxelConstants.CHUNK_SIZE + new Vector3(VoxelConstants.CHUNK_SIZE/2f, VoxelConstants.CHUNK_SIZE/2f, VoxelConstants.CHUNK_SIZE/2f);
                        Vector3 gravity = -worldOrigin.normalized;
                        if (Mathf.Abs(gravity.x) > Mathf.Abs(gravity.y) && Mathf.Abs(gravity.x) > Mathf.Abs(gravity.z)) {
                            downX = (int)Mathf.Sign(gravity.x); downY = 0; downZ = 0;
                        } else if (Mathf.Abs(gravity.z) > Mathf.Abs(gravity.y)) {
                            downZ = (int)Mathf.Sign(gravity.z); downX = 0; downY = 0;
                        } else {
                            downY = (int)Mathf.Sign(gravity.y); downX = 0; downZ = 0;
                        }
                    }

                    var job = new FluidSimJob
                    {
                        voxels     = chunk.voxels,
                        chunkSize  = VoxelConstants.CHUNK_SIZE,
                        chunkSizeP = VoxelConstants.CHUNK_SIZE_P,
                        downX      = downX,
                        downY      = downY,
                        downZ      = downZ,
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
                    FlushPaddingFlowsToNeighbours(world, chunk);
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

        private void WakeNeighbour(VoxelEngine.Core.IVoxelWorld world, Vector3Int coord)
        {
            if (world.TryGetChunk(coord, out var ch) && ch.isGenerated) MarkActive(coord);
        }

        private void FlushPaddingFlowsToNeighbours(VoxelEngine.Core.IVoxelWorld world, Chunk chunk)
        {
            FlushFace(world, chunk, new Vector3Int( 1, 0, 0));
            FlushFace(world, chunk, new Vector3Int(-1, 0, 0));
            FlushFace(world, chunk, new Vector3Int( 0, 1, 0));
            FlushFace(world, chunk, new Vector3Int( 0,-1, 0));
            FlushFace(world, chunk, new Vector3Int( 0, 0, 1));
            FlushFace(world, chunk, new Vector3Int( 0, 0,-1));
        }

        private void FlushFace(VoxelEngine.Core.IVoxelWorld world, Chunk source, Vector3Int dir)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            var nCoord = source.coord + dir;
            if (!world.TryGetChunk(nCoord, out var target) || target == null || !target.isGenerated) return;
            world.CompleteGenJobForChunk(target);
            world.CompleteMeshJobForChunk(target);

            bool changed = false;
            int sx = dir.x > 0 ? S : (dir.x < 0 ? -1 : 0);
            int sy = dir.y > 0 ? S : (dir.y < 0 ? -1 : 0);
            int sz = dir.z > 0 ? S : (dir.z < 0 ? -1 : 0);
            int tx = dir.x > 0 ? 0 : (dir.x < 0 ? S - 1 : 0);
            int ty = dir.y > 0 ? 0 : (dir.y < 0 ? S - 1 : 0);
            int tz = dir.z > 0 ? 0 : (dir.z < 0 ? S - 1 : 0);

            for (int z = 0; z < S; z++)
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int px = dir.x == 0 ? x : sx;
                int py = dir.y == 0 ? y : sy;
                int pz = dir.z == 0 ? z : sz;
                int lx = dir.x == 0 ? x : tx;
                int ly = dir.y == 0 ? y : ty;
                int lz = dir.z == 0 ? z : tz;

                var pad = source.GetVoxelLocal(px, py, pz);
                if (!FluidMaterialUtility.IsFluid(pad)) continue;
                var dst = target.GetVoxelLocal(lx, ly, lz);
                if (dst.IsSolid) continue;

                bool same = dst.waterLevel == 0 || FluidMaterialUtility.LiquidFromVoxel(dst) == FluidMaterialUtility.LiquidFromVoxel(pad);
                if (!same || pad.waterLevel <= dst.waterLevel) continue;

                dst.density = -1;
                dst.material = pad.material;
                dst.waterLevel = pad.waterLevel;
                target.SetVoxelLocal(lx, ly, lz, dst);
                changed = true;
            }

            if (!changed) return;
            target.isModified = true;
            MarkActive(target.coord);
            WaterMeshBuilder.Schedule(target);
        }

        public void PlaceWater(Vector3Int worldVoxel, byte level = 255) => PlaceLiquid(worldVoxel, LiquidType.Water, level);
        public void PlaceOil(Vector3Int worldVoxel, byte level = 255) => PlaceLiquid(worldVoxel, LiquidType.CrudeOil, level);

        public void PlaceLiquid(Vector3Int worldVoxel, LiquidType liquid, byte level = 255)
        {
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null || !TryGetChunkAndLocal(world, worldVoxel, out var coord, out var ch, out int lx, out int ly, out int lz)) return;

            world.CompleteGenJobForChunk(ch);
            world.CompleteMeshJobForChunk(ch);

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
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null || !TryGetChunkAndLocal(world, worldVoxel, out var coord, out var ch, out int lx, out int ly, out int lz)) return 0;

            world.CompleteGenJobForChunk(ch);
            world.CompleteMeshJobForChunk(ch);

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
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) return 0;
            var v = world.GetVoxelWorld(worldVoxel);
            return FluidMaterialUtility.Matches(v, liquid) ? v.waterLevel : (byte)0;
        }

        public LiquidType GetLiquidType(Vector3Int worldVoxel)
        {
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) return LiquidType.Water;
            return FluidMaterialUtility.LiquidFromVoxel(world.GetVoxelWorld(worldVoxel));
        }

        /// <summary>
        /// Count connected fluid voxels of the given liquid type starting from a seed voxel.
        /// Returns (voxelCount, totalLitres, isInfinite) within the given reach radius.
        /// Used by WaterPump for pool status display.
        /// </summary>
        public (int voxels, float litres, bool isInfinite) ScanPool(
            Vector3Int seed, LiquidType liquid, float reachRadius, int infiniteThreshold, int maxScan)
        {
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) return (0, 0, false);
            if (!FluidMaterialUtility.Matches(world.GetVoxelWorld(seed), liquid)) return (0, 0, false);

            var seen = new HashSet<Vector3Int>();
            var q = new Queue<Vector3Int>();
            q.Enqueue(seed);
            seen.Add(seed);
            float litresPerLevel = 1000f / 255f;
            int count = 0;
            float litres = 0f;
            float r2 = reachRadius * reachRadius * 9f;

            while (q.Count > 0 && count < maxScan)
            {
                var p = q.Dequeue();
                var v = world.GetVoxelWorld(p);
                if (!FluidMaterialUtility.Matches(v, liquid)) continue;
                count++;
                litres += v.waterLevel * litresPerLevel;

                var offsets = new[]{ Vector3Int.right, Vector3Int.left, Vector3Int.forward,
                                     Vector3Int.back, Vector3Int.up, Vector3Int.down };
                foreach (var off in offsets)
                {
                    var n = p + off;
                    if (seen.Contains(n)) continue;
                    if ((n - seed).sqrMagnitude > r2) continue;
                    seen.Add(n);
                    q.Enqueue(n);
                }
            }

            bool infinite = count >= infiniteThreshold || count >= maxScan;
            return (count, litres, infinite);
        }

        private static bool TryGetChunkAndLocal(VoxelEngine.Core.IVoxelWorld world, Vector3Int worldVoxel, out Vector3Int coord, out Chunk ch, out int lx, out int ly, out int lz)
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
