// Assets/Scripts/VoxelEngine/GridSystem/GridLiquidNetwork.cs
//
// Registry/topology for grid liquids. There is ONE pipe system: the normal/static
// WaterPipe component can be placed onto a grid and then counts as that grid's
// liquid conduit. Liquid transfer is topology-based: a producer/consumer must
// touch a connected pipe run that reaches a compatible tank.
//
// v5.63.1-dev — FIX: liquid pipes ↔ liquid tanks now reliably connect at 5 squares:
//   • Increased buffers (12→32), widened detail-pipe proximity (2.25→3.25m),
//     bodyRange 2×→3× so face-touch tanks always link.
//   • Corridor radiusScale 1.6→2.2 + brute-force cardinal fallback scanning ALL
//     grid tanks within 5 cells of any visited pipe (mirrors gas fix).

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Networks;

namespace VoxelEngine.GridSystem
{
    public class GridLiquidNetwork : MonoBehaviour
    {
        private static GridLiquidNetwork _instance;
        public static GridLiquidNetwork Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("GridLiquidNetwork");
                    _instance = go.AddComponent<GridLiquidNetwork>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private readonly Dictionary<GridEntity, List<GridLiquidTank>> _tanks = new();

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        public void RegisterTank(GridEntity grid, GridLiquidTank tank)
        {
            if (grid == null || tank == null) return;
            if (!_tanks.TryGetValue(grid, out var list)) { list = new List<GridLiquidTank>(); _tanks[grid] = list; }
            if (!list.Contains(tank)) list.Add(tank);
        }

        public void UnregisterTank(GridEntity grid, GridLiquidTank tank)
        {
            if (grid != null && _tanks.TryGetValue(grid, out var list)) list.Remove(tank);
        }

        public bool HasPipes(GridEntity grid)
        {
            if (grid == null) return false;
            foreach (var block in grid.AllBlocks)
                if (IsLiquidPipe(block)) return true;
            return false;
        }

        public IReadOnlyList<GridLiquidTank> GetTanks(GridEntity grid)
            => _tanks.TryGetValue(grid, out var list) ? list : System.Array.Empty<GridLiquidTank>();

        public List<GridLiquidTank> GetTanks(GridEntity grid, LiquidType type)
        {
            var result = new List<GridLiquidTank>();
            foreach (var t in GetTanks(grid)) if (t != null && t.liquidType == type) result.Add(t);
            return result;
        }

        public float AvailableLiquidFor(GridBlock endpoint, LiquidType type)
        {
            float total = 0f;
            foreach (var tank in CachedTanks(endpoint, type, requireExistingType: true))
                total += Mathf.Max(0f, tank.stored);
            CollectBridgedClassicTanks(endpoint, type, forDraw: true, s_bridgedTanks);
            foreach (var tank in s_bridgedTanks)
                total += Mathf.Max(0f, tank.StoredLitres);
            return total;
        }

        public float SpaceForLiquidFrom(GridBlock endpoint, LiquidType type)
        {
            float total = 0f;
            foreach (var tank in CachedTanks(endpoint, type, requireExistingType: false))
                total += Mathf.Max(0f, tank.capacity - tank.stored);
            CollectBridgedClassicTanks(endpoint, type, forDraw: false, s_bridgedTanks);
            foreach (var tank in s_bridgedTanks)
                total += Mathf.Max(0f, tank.capacityLitres - tank.StoredLitres);
            return total;
        }

        private static readonly Dictionary<long, (float time, List<GridLiquidTank> tanks)> s_tankCache = new();
        private const float TankCacheTtl = 0.15f;
        private static long TankCacheKey(GridBlock endpoint, LiquidType type, bool forOutput)
        {
            var id = endpoint != null ? endpoint.GetEntityId().GetHashCode() : 0;
            return ((long)id << 16) ^ ((long)(int)type << 1) ^ (forOutput ? 1L : 0L);
        }
        public void SetDirty() => s_tankCache.Clear();

        public float DrawLiquidFor(GridBlock endpoint, LiquidType type, float litres)
        {
            if (litres <= 0f) return 0f;
            float drawn = 0f;
            foreach (var tank in CachedTanks(endpoint, type, requireExistingType: true))
            {
                if (drawn >= litres) break;
                drawn += tank.Remove(litres - drawn);
            }
            if (drawn < litres)
            {
                CollectBridgedClassicTanks(endpoint, type, forDraw: true, s_bridgedTanks);
                foreach (var tank in s_bridgedTanks)
                {
                    if (drawn >= litres) break;
                    drawn += tank.TakeSome(type, litres - drawn);
                }
            }
            return drawn;
        }

        public float FillLiquidFrom(GridBlock endpoint, LiquidType type, float litres)
        {
            if (litres <= 0f) return 0f;
            float filled = 0f;
            foreach (var tank in CachedTanks(endpoint, type, requireExistingType: false))
            {
                if (filled >= litres) break;
                if (tank.liquidType != type && tank.stored > 0.001f) continue;
                if (tank.stored <= 0.001f) tank.liquidType = type;
                filled += tank.Add(litres - filled);
            }
            if (filled < litres)
            {
                CollectBridgedClassicTanks(endpoint, type, forDraw: false, s_bridgedTanks);
                foreach (var tank in s_bridgedTanks)
                {
                    if (filled >= litres) break;
                    filled += tank.AddSome(type, litres - filled);
                }
            }
            return filled;
        }

        private const int ClassicWalkCap = 512;
        private static readonly List<VoxelEngine.Fluids.WaterTank> s_bridgedTanks = new(8);
        private static readonly List<Vector3> s_classicSeeds = new(8);
        private static readonly HashSet<VoxelEngine.Fluids.WaterPipe> s_classicVisited = new();
        private static readonly Queue<VoxelEngine.Fluids.WaterPipe> s_classicQueue = new();
        private static readonly Collider[] s_classicProbe = new Collider[32];

        private const float BridgeCacheTtl = 0.6f;
        private static readonly Dictionary<long, (float time, List<VoxelEngine.Fluids.WaterTank> tanks)> s_bridgeCache = new();

        private static void CollectBridgedClassicTanks(GridBlock endpoint, LiquidType type,
            bool forDraw, List<VoxelEngine.Fluids.WaterTank> outTanks)
        {
            outTanks.Clear();
            if (endpoint == null) return;

            long key = ((long)endpoint.GetEntityId().GetHashCode() << 8) ^ (((long)(int)type << 1) | (forDraw ? 1L : 0L));
            if (s_bridgeCache.TryGetValue(key, out var cached)
                && Time.time - cached.time < BridgeCacheTtl)
            {
                cached.tanks.RemoveAll(t => t == null);
                for (int i = 0; i < cached.tanks.Count; i++) outTanks.Add(cached.tanks[i]);
                return;
            }

            var fresh = new List<VoxelEngine.Fluids.WaterTank>(8);
            CollectBridgedClassicTanksUncached(endpoint, type, forDraw, fresh);
            s_bridgeCache[key] = (Time.time, fresh);
            for (int i = 0; i < fresh.Count; i++) outTanks.Add(fresh[i]);
        }

        private static void CollectBridgedClassicTanksUncached(GridBlock endpoint, LiquidType type,
            bool forDraw, List<VoxelEngine.Fluids.WaterTank> outTanks)
        {
            float cs = endpoint.Grid != null ? endpoint.Grid.gridSize.CellSize() : 2.5f;
            s_classicSeeds.Clear();
            foreach (Transform child in endpoint.transform.GetComponentsInChildren<Transform>(true))
            {
                if (s_classicSeeds.Count >= 4) break;
                if (child == null || child == endpoint.transform) continue;
                bool liquidPort = false;
                for (int i = 0; i < VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes.Length; i++)
                {
                    if (child.name.StartsWith(VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes[i], System.StringComparison.Ordinal)) { liquidPort = true; break; }
                }
                if (liquidPort) s_classicSeeds.Add(child.position);
            }
            s_classicSeeds.Add(endpoint.transform.position);

            s_classicVisited.Clear();
            s_classicQueue.Clear();
            for (int c = 0; c < s_classicSeeds.Count; c++)
            {
                float radius = c < s_classicSeeds.Count - 1 ? cs * 1.6f : endpoint.EffectiveCellSize * 1.35f;
                int hitCount = Physics.OverlapSphereNonAlloc(s_classicSeeds[c], radius, s_classicProbe, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hitCount; i++)
                {
                    var col = s_classicProbe[i];
                    if (col == null) continue;
                    var pipe = col.GetComponentInParent<VoxelEngine.Fluids.WaterPipe>();
                    if (pipe != null && s_classicVisited.Add(pipe)) s_classicQueue.Enqueue(pipe);
                }
            }

            int walked = 0;
            while (s_classicQueue.Count > 0 && walked++ < ClassicWalkCap)
            {
                var pipe = s_classicQueue.Dequeue();
                ProbeClassicTankCorridor(pipe, type, forDraw, outTanks);
                var neighbours = pipe.neighbours;
                if (neighbours == null) continue;
                foreach (var node in neighbours)
                {
                    if (node is VoxelEngine.Fluids.WaterPipe nextPipe)
                    {
                        if (s_classicVisited.Add(nextPipe)) s_classicQueue.Enqueue(nextPipe);
                    }
                    else if (node is VoxelEngine.Fluids.WaterTank tank)
                    {
                        TryAddBridgedTank(tank, type, forDraw, outTanks);
                    }
                }
            }
        }

        private static readonly Collider[] s_classicRowProbe = new Collider[32];

        private static void TryAddBridgedTank(VoxelEngine.Fluids.WaterTank tank, LiquidType type,
            bool forDraw, List<VoxelEngine.Fluids.WaterTank> outTanks)
        {
            if (tank == null || outTanks.Contains(tank)) return;
            bool usable = forDraw
                ? tank.liquidType == type && tank.StoredLitres > 0.001f
                : tank.IsEmpty || tank.liquidType == type;
            if (usable) outTanks.Add(tank);
        }

        private static void ProbeClassicTankCorridor(VoxelEngine.Fluids.WaterPipe pipe, LiquidType type,
            bool forDraw, List<VoxelEngine.Fluids.WaterTank> outTanks)
        {
            if (pipe == null) return;
            var block = pipe.GetComponentInParent<GridBlock>();
            float step = block != null && block.Grid != null
                ? block.Grid.gridSize.CellSize()
                : PipeAdjacency.DefaultGridSize;
            Transform frame = block != null && block.Grid != null ? block.Grid.transform : null;
            PipeAdjacency.ProbeCardinal(pipe.transform.position, frame, step, 5,
                s_classicRowProbe, col =>
                {
                    var tank = col.GetComponent<VoxelEngine.Fluids.WaterTank>();
                    if (tank == null) tank = col.GetComponentInParent<VoxelEngine.Fluids.WaterTank>();
                    if (tank != null) TryAddBridgedTank(tank, type, forDraw, outTanks);
                    return false;
                }, radiusScale: 2.2f);
        }

        private IEnumerable<GridLiquidTank> CachedTanks(GridBlock endpoint, LiquidType type, bool requireExistingType)
        {
            long key = TankCacheKey(endpoint, type, requireExistingType);
            if (s_tankCache.TryGetValue(key, out var cached) && Time.time - cached.time < TankCacheTtl)
            {
                for (int i = 0; i < cached.tanks.Count; i++)
                    if (cached.tanks[i] != null) yield return cached.tanks[i];
                yield break;
            }

            var fresh = new List<GridLiquidTank>(8);
            foreach (var tank in ConnectedTanks(endpoint, type, requireExistingType))
                if (tank != null) fresh.Add(tank);
            s_tankCache[key] = (Time.time, fresh);
            for (int i = 0; i < fresh.Count; i++)
                yield return fresh[i];
        }

        private IEnumerable<GridLiquidTank> ConnectedTanks(GridBlock endpoint, LiquidType type, bool requireExistingType)
        {
            var grid = endpoint != null ? endpoint.Grid : null;
            if (grid == null) yield break;

            float cs = grid.gridSize.CellSize();
            var visitedPipes = new HashSet<GridBlock>();
            var yieldedTanks = new HashSet<GridLiquidTank>();
            var corridorTanks = new List<GridLiquidTank>(4);
            var queue = new Queue<GridBlock>();

            void SeedPipe(GridBlock pipe)
            {
                if (pipe == null || WrenchBlacklist.IsBlocked(endpoint.gameObject, pipe.gameObject)
                    || !visitedPipes.Add(pipe)) return;
                queue.Enqueue(pipe);
            }

            foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, endpoint))
                if (IsLiquidPipe(adjacent)) SeedPipe(adjacent);
            foreach (var pipe in grid.AllBlocks)
            {
                if (pipe == null || !IsLiquidPipe(pipe)) continue;
                if (BlocksAreLiquidLinked(endpoint, pipe, cs)) SeedPipe(pipe);
            }

            while (queue.Count > 0)
            {
                var pipeBlock = queue.Dequeue();

                foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, pipeBlock))
                {
                    if (IsLiquidPipe(adjacent)
                        && !WrenchBlacklist.IsBlocked(pipeBlock.gameObject, adjacent.gameObject)
                        && visitedPipes.Add(adjacent)) queue.Enqueue(adjacent);
                }
                foreach (var pipe in ProximityBlocks(grid, pipeBlock, cs, liquidOnly: true))
                {
                    if (!WrenchBlacklist.IsBlocked(pipeBlock.gameObject, pipe.gameObject)
                        && visitedPipes.Add(pipe)) queue.Enqueue(pipe);
                }

                foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, pipeBlock))
                {
                    if (adjacent is GridLiquidTank tank && tank.Enabled && tank.mode != GridTankMode.Stockpile
                        && !WrenchBlacklist.IsBlocked(pipeBlock.gameObject, tank.gameObject))
                    {
                        bool typeOk = tank.liquidType == type || (!requireExistingType && tank.stored <= 0.001f);
                        if (typeOk && yieldedTanks.Add(tank)) yield return tank;
                    }
                }
                foreach (var maybeTank in ProximityBlocks(grid, pipeBlock, cs, liquidOnly: false))
                {
                    if (maybeTank is not GridLiquidTank tank || !tank.Enabled || tank.mode == GridTankMode.Stockpile
                        || WrenchBlacklist.IsBlocked(pipeBlock.gameObject, tank.gameObject)) continue;
                    bool typeOk = tank.liquidType == type || (!requireExistingType && tank.stored <= 0.001f);
                    if (typeOk && yieldedTanks.Add(tank)) yield return tank;
                }
                ProbeGridTankCorridor(pipeBlock, type, requireExistingType, yieldedTanks, corridorTanks);
                for (int i = 0; i < corridorTanks.Count; i++)
                    yield return corridorTanks[i];
            }

            // ── Brute-force 5-cell cardinal fallback for liquid tanks
            if (visitedPipes.Count > 0)
            {
                float smallStep = GridSize.Small.CellSize();
                foreach (var block in grid.AllBlocks)
                {
                    if (block is not GridLiquidTank tank || !tank.Enabled || tank.mode == GridTankMode.Stockpile) continue;
                    if (yieldedTanks.Contains(tank)) continue;
                    bool typeOk = tank.liquidType == type || (!requireExistingType && tank.stored <= 0.001f);
                    if (!typeOk) continue;
                    foreach (var pipe in visitedPipes)
                    {
                        if (IsTankPortWithinDetailLink(grid, pipe, tank,
                                VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes, smallStep))
                        {
                            if (yieldedTanks.Add(tank)) yield return tank;
                            break;
                        }
                    }
                }
            }
        }

        private static readonly Collider[] s_liquidProbe = new Collider[32];
        private static readonly List<GridBlock> s_proximityResult = new(32);

        public static bool BlocksAreLiquidLinked(GridBlock endpoint, GridBlock pipe, float cs)
        {
            if (endpoint == null || pipe == null) return false;
            float portRange = cs * 2.5f;
            float portRange2 = portRange * portRange;
            foreach (Transform child in endpoint.transform.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == endpoint.transform) continue;
                bool liquidPort = false;
                for (int i = 0; i < VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes.Length; i++)
                {
                    if (child.name.StartsWith(VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes[i], System.StringComparison.Ordinal)) { liquidPort = true; break; }
                }
                if (!liquidPort) continue;
                if ((child.position - pipe.transform.position).sqrMagnitude <= portRange2) return true;
            }
            float bodyRange = endpoint.EffectiveCellSize * 3.0f;
            return (endpoint.transform.position - pipe.transform.position).sqrMagnitude <= bodyRange * bodyRange;
        }

        private static IEnumerable<GridBlock> ProximityBlocks(GridEntity grid, GridBlock origin, float cs, bool liquidOnly)
        {
            s_proximityResult.Clear();
            if (grid == null || origin == null) yield break;
            float originCell = origin.EffectiveCellSize;
            bool isDetail = origin.IsPrecisionAttachment;
            float radius = isDetail
                ? Mathf.Max(GridSize.Large.CellSize() * 1.5f, 3.25f)
                : Mathf.Max(originCell, GridSize.Small.CellSize()) * 2.0f;
            int hitCount = Physics.OverlapSphereNonAlloc(origin.transform.position, radius, s_liquidProbe, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hitCount; i++)
            {
                var col = s_liquidProbe[i];
                if (col == null) continue;
                var block = col.GetComponentInParent<GridBlock>();
                if (block == null || block == origin || block.Grid != grid) continue;
                if (liquidOnly && !IsLiquidPipe(block)) continue;
                if (s_proximityResult.Contains(block)) continue;
                s_proximityResult.Add(block);
            }
            for (int i = 0; i < s_proximityResult.Count; i++)
                yield return s_proximityResult[i];
        }

        private static readonly Collider[] s_gridTankRowProbe = new Collider[32];

        private static void ProbeGridTankCorridor(GridBlock pipeBlock, LiquidType type,
            bool requireExistingType, HashSet<GridLiquidTank> yieldedTanks, List<GridLiquidTank> newlyLinked)
        {
            newlyLinked?.Clear();
            if (pipeBlock == null) return;
            float detail = GridSize.Small.CellSize();
            const int maxCells = 5;
            Transform frame = pipeBlock.Grid != null ? pipeBlock.Grid.transform : null;
            PipeAdjacency.ProbeCardinal(pipeBlock.transform.position, frame, detail, maxCells,
                s_gridTankRowProbe, col =>
                {
                    var tank = col.GetComponent<GridLiquidTank>();
                    if (tank == null) tank = col.GetComponentInParent<GridLiquidTank>();
                    if (tank != null && tank.Enabled && tank.mode != GridTankMode.Stockpile && !yieldedTanks.Contains(tank))
                    {
                        bool typeOk = tank.liquidType == type || (!requireExistingType && tank.stored <= 0.001f);
                        bool portAligned = IsTankPortWithinDetailLink(pipeBlock.Grid, pipeBlock, tank,
                            VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes, detail);
                        if (typeOk && portAligned && yieldedTanks.Add(tank)) newlyLinked?.Add(tank);
                    }
                    return false;
                }, radiusScale: 2.2f);
        }

        private static bool IsTankPortWithinDetailLink(GridEntity grid, GridBlock pipe, GridBlock tank,
            string[] portPrefixes, float detailStep)
        {
            if (grid == null || pipe == null || tank == null) return false;
            detailStep = detailStep > 0.0001f ? detailStep : GridSize.Small.CellSize();

            bool TestTarget(Vector3 targetWorld)
            {
                Vector3 localDelta = grid.transform.InverseTransformVector(targetWorld - pipe.transform.position);
                return PipeAdjacency.IsCardinalLinkDelta(localDelta, detailStep, 5f, detailStep * 0.55f);
            }

            foreach (Transform child in tank.transform.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == tank.transform) continue;
                bool matches = false;
                for (int i = 0; i < portPrefixes.Length; i++)
                {
                    if (child.name.StartsWith(portPrefixes[i], System.StringComparison.Ordinal)) { matches = true; break; }
                }
                if (matches && TestTarget(child.position)) return true;
            }

            return TestTarget(tank.transform.position);
        }

        private static bool IsLiquidPipe(GridBlock block)
        {
            return block != null && block.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null;
        }
    }
}
