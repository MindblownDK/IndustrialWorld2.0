// Assets/Scripts/VoxelEngine/GridSystem/GridGasNetwork.cs
//
// Per-grid gas distribution. There is ONE pipe system: the normal/static GasPipe
// component can be placed onto a grid and then counts as that grid's gas conduit.
// Gas transfer is topology-based: a producer/consumer must touch a connected gas
// pipe run that reaches a compatible tank.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Networks;

namespace VoxelEngine.GridSystem
{
    public class GridGasNetwork : MonoBehaviour
    {
        private static GridGasNetwork _instance;
        public static GridGasNetwork Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("GridGasNetwork");
                    _instance = go.AddComponent<GridGasNetwork>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private static readonly Vector3Int[] Neighbours =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        // ════════════════════════════════════════════════════════════════
        //  CONNECTED-TANKS CACHE — prevents the full BFS on every frame
        // ════════════════════════════════════════════════════════════════
        private const float TankCacheTtl = 0.15f;
        private static readonly System.Collections.Generic.Dictionary<long, (float time, System.Collections.Generic.List<GridGasTank> tanks)> s_tankCache
            = new();
        private static long TankCacheKey(GridBlock endpoint, Gas.GasType type, bool forOutput, bool includeStockpile)
        {
            var id = endpoint != null ? endpoint.GetEntityId().GetHashCode() : 0;
            return ((long)id << 24) ^ ((long)(int)type << 8) ^ ((forOutput ? 1L : 0L) << 4) ^ (includeStockpile ? 1L : 0L);
        }
        public void SetDirty()
        {
            s_tankCache.Clear();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        public bool HasPipes(GridEntity grid)
        {
            if (grid == null) return false;
            foreach (var block in grid.AllBlocks)
                if (IsGasPipe(block)) return true;
            return false;
        }

        public float AvailableGas(GridEntity grid, Gas.GasType type, bool includeStockpile = false)
        {
            if (grid == null || type == Gas.GasType.None) return 0f;
            float total = 0f;
            foreach (var block in grid.AllBlocks)
            {
                if (block is GridGasTank tank && tank.Enabled && tank.gasType == type)
                {
                    if (!includeStockpile && tank.mode == GridTankMode.Stockpile) continue;
                    total += Mathf.Max(0f, tank.stored);
                }
            }
            return total;
        }

        public float AvailableGasFor(GridBlock consumer, Gas.GasType type, bool includeStockpile = false)
        {
            float total = 0f;
            foreach (var tank in CachedTanks(consumer, type, forOutput: true, includeStockpile))
                total += Mathf.Max(0f, tank.stored);
            return total;
        }

        public float DrawGasFor(GridBlock consumer, Gas.GasType type, float litres, bool includeStockpile = false)
        {
            if (consumer == null || type == Gas.GasType.None || litres <= 0f) return 0f;
            float drawn = 0f;
            foreach (var tank in CachedTanks(consumer, type, forOutput: true, includeStockpile))
            {
                if (drawn >= litres) break;
                drawn += tank.Draw(litres - drawn, ignoreStockpile: includeStockpile);
            }
            return drawn;
        }

        public float FillGasFrom(GridBlock producer, Gas.GasType type, float litres)
        {
            if (producer == null || type == Gas.GasType.None || litres <= 0f) return 0f;
            float filled = 0f;
            foreach (var tank in CachedTanks(producer, type, forOutput: false, includeStockpile: true))
            {
                if (filled >= litres) break;
                filled += tank.Add(type, litres - filled);
            }
            return filled;
        }

        // Legacy broad helpers retained for old callers, but new grid machines should use
        // DrawGasFor / FillGasFrom so transfer follows connected pipe topology.
        public float DrawGas(GridEntity grid, Gas.GasType type, float litres, bool includeStockpile = false)
        {
            if (grid == null || type == Gas.GasType.None || litres <= 0f) return 0f;
            float drawn = 0f;
            foreach (var block in grid.AllBlocks)
            {
                if (drawn >= litres) break;
                if (!(block is GridGasTank tank) || !tank.Enabled || tank.gasType != type) continue;
                if (!includeStockpile && tank.mode == GridTankMode.Stockpile) continue;
                drawn += tank.Draw(litres - drawn, ignoreStockpile: includeStockpile);
            }
            return drawn;
        }

        public float FillGas(GridEntity grid, Gas.GasType type, float litres)
        {
            if (grid == null || type == Gas.GasType.None || litres <= 0f) return 0f;
            float filled = 0f;
            foreach (var block in grid.AllBlocks)
            {
                if (filled >= litres) break;
                if (block is GridGasTank tank && tank.Enabled)
                    filled += tank.Add(type, litres - filled);
            }
            return filled;
        }

        /// <summary>Cached wrapper for <see cref="ConnectedTanks"/>. Stores the
        /// tank list per (endpoint, type, direction) for 0.15 s so the full BFS
        /// isn't repeated every frame for every consumer.</summary>
        private IEnumerable<GridGasTank> CachedTanks(GridBlock endpoint, Gas.GasType type, bool forOutput, bool includeStockpile)
        {
            long key = TankCacheKey(endpoint, type, forOutput, includeStockpile);
            if (s_tankCache.TryGetValue(key, out var cached) && Time.time - cached.time < TankCacheTtl)
            {
                for (int i = 0; i < cached.tanks.Count; i++)
                    if (cached.tanks[i] != null) yield return cached.tanks[i];
                yield break;
            }

            var fresh = new System.Collections.Generic.List<GridGasTank>(8);
            foreach (var tank in ConnectedTanks(endpoint, type, forOutput, includeStockpile))
                if (tank != null) fresh.Add(tank);
            s_tankCache[key] = (Time.time, fresh);
            for (int i = 0; i < fresh.Count; i++)
                yield return fresh[i];
        }

        private IEnumerable<GridGasTank> ConnectedTanks(GridBlock endpoint, Gas.GasType type, bool forOutput, bool includeStockpile)
        {
            var grid = endpoint != null ? endpoint.Grid : null;
            if (grid == null || type == Gas.GasType.None) yield break;

            float cs = grid.gridSize.CellSize();
            var visitedPipes = new HashSet<GridBlock>();
            var yieldedTanks = new HashSet<GridGasTank>();
            var queue = new Queue<GridBlock>();

            void SeedPipe(GridBlock pipe)
            {
                if (pipe == null || VoxelEngine.Networks.WrenchBlacklist.IsBlocked(endpoint.gameObject, pipe.gameObject)
                    || !visitedPipes.Add(pipe)) return;
                queue.Enqueue(pipe);
            }

            // Classic face-touch adjacency
            foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, endpoint))
                if (IsGasPipe(adjacent)) SeedPipe(adjacent);
            // PLUS world-space proximity — pipes snapped to a port may sit cells away
            foreach (var pipe in grid.AllBlocks)
            {
                if (pipe == null || !IsGasPipe(pipe)) continue;
                if (BlocksAreGasLinked(endpoint, pipe, cs)) SeedPipe(pipe);
            }

            while (queue.Count > 0)
            {
                var pipeBlock = queue.Dequeue();

                // Pipe → pipe growth (face adjacency + proximity)
                foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, pipeBlock))
                {
                    if (IsGasPipe(adjacent)
                        && !VoxelEngine.Networks.WrenchBlacklist.IsBlocked(pipeBlock.gameObject, adjacent.gameObject)
                        && visitedPipes.Add(adjacent)) queue.Enqueue(adjacent);
                }
                foreach (var pipe in ProximityPipes(grid, pipeBlock, cs))
                {
                    if (!VoxelEngine.Networks.WrenchBlacklist.IsBlocked(pipeBlock.gameObject, pipe.gameObject)
                        && visitedPipes.Add(pipe)) queue.Enqueue(pipe);
                }

                // Pipe → tanks (face + proximity + 5-cell cardinal corridor)
                foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, pipeBlock))
                {
                    if (adjacent is GridGasTank tank && tank.Enabled
                        && !VoxelEngine.Networks.WrenchBlacklist.IsBlocked(pipeBlock.gameObject, tank.gameObject))
                    {
                        bool typeOk = tank.gasType == type || (!forOutput && tank.stored <= 0.001f);
                        bool stockpileOk = includeStockpile || tank.mode != GridTankMode.Stockpile;
                        if (typeOk && stockpileOk && yieldedTanks.Add(tank)) yield return tank;
                    }
                }
                foreach (var maybeTank in ProximityBlocks(grid, pipeBlock, cs))
                {
                    if (maybeTank is not GridGasTank tank || !tank.Enabled
                        || VoxelEngine.Networks.WrenchBlacklist.IsBlocked(pipeBlock.gameObject, tank.gameObject)) continue;
                    bool typeOk = tank.gasType == type || (!forOutput && tank.stored <= 0.001f);
                    bool stockpileOk = includeStockpile || tank.mode != GridTankMode.Stockpile;
                    if (typeOk && stockpileOk && yieldedTanks.Add(tank)) yield return tank;
                }
                // 5-cell cardinal corridor to catch tanks offset from the pipe run
                ProbeGasTankCorridor(pipeBlock, type, forOutput, includeStockpile, yieldedTanks);
            }
        }

        // Scratch buffers for proximity helpers.
        private static readonly Collider[] s_gasProbe = new Collider[12];
        private static readonly List<GridBlock> s_gasProximityResult = new(12);

        /// <summary>World-space adjacency: A is a machine/tank, B is a gas pipe.
        /// Counts as linked when the pipe is within reach of one of the machine's
        /// named gas ports, OR close enough to the machine's body.</summary>
        private static bool BlocksAreGasLinked(GridBlock endpoint, GridBlock pipe, float cs)
        {
            if (endpoint == null || pipe == null) return false;
            float portRange = cs * 2.5f;
            float portRange2 = portRange * portRange;
            foreach (Transform child in endpoint.transform.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == endpoint.transform) continue;
                bool gasPort = false;
                for (int i = 0; i < VoxelEngine.Maritime.MaritimePorts.GasPrefixes.Length; i++)
                {
                    if (child.name.StartsWith(VoxelEngine.Maritime.MaritimePorts.GasPrefixes[i], System.StringComparison.Ordinal)) { gasPort = true; break; }
                }
                if (!gasPort) continue;
                if ((child.position - pipe.transform.position).sqrMagnitude <= portRange2) return true;
            }
            float bodyRange = endpoint.EffectiveCellSize * 2.0f;
            return (endpoint.transform.position - pipe.transform.position).sqrMagnitude <= bodyRange * bodyRange;
        }

        private static IEnumerable<GridBlock> ProximityPipes(GridEntity grid, GridBlock origin, float cs)
        {
            s_gasProximityResult.Clear();
            if (grid == null || origin == null) yield break;
            float radius = Mathf.Max(origin.EffectiveCellSize, GridSize.Small.CellSize()) * 1.35f;
            int hitCount = Physics.OverlapSphereNonAlloc(origin.transform.position, radius, s_gasProbe, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hitCount; i++)
            {
                var col = s_gasProbe[i];
                if (col == null) continue;
                var block = col.GetComponentInParent<GridBlock>();
                if (block == null || block == origin || block.Grid != grid) continue;
                if (!IsGasPipe(block)) continue;
                if (s_gasProximityResult.Contains(block)) continue;
                s_gasProximityResult.Add(block);
            }
            for (int i = 0; i < s_gasProximityResult.Count; i++)
                yield return s_gasProximityResult[i];
        }

        private static IEnumerable<GridBlock> ProximityBlocks(GridEntity grid, GridBlock origin, float cs)
        {
            s_gasProximityResult.Clear();
            if (grid == null || origin == null) yield break;
            float radius = Mathf.Max(origin.EffectiveCellSize, GridSize.Small.CellSize()) * 1.35f;
            int hitCount = Physics.OverlapSphereNonAlloc(origin.transform.position, radius, s_gasProbe, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hitCount; i++)
            {
                var col = s_gasProbe[i];
                if (col == null) continue;
                var block = col.GetComponentInParent<GridBlock>();
                if (block == null || block == origin || block.Grid != grid) continue;
                if (s_gasProximityResult.Contains(block)) continue;
                s_gasProximityResult.Add(block);
            }
            for (int i = 0; i < s_gasProximityResult.Count; i++)
                yield return s_gasProximityResult[i];
        }

        private static readonly Collider[] s_gasRowProbe = new Collider[16];

        private static void ProbeGasTankCorridor(GridBlock pipeBlock, Gas.GasType type,
            bool forOutput, bool includeStockpile, HashSet<GridGasTank> yieldedTanks)
        {
            if (pipeBlock == null) return;
            float cs = pipeBlock.Grid != null ? pipeBlock.Grid.gridSize.CellSize() : 2.5f;
            Transform frame = pipeBlock.Grid != null ? pipeBlock.Grid.transform : null;
            VoxelEngine.Networks.PipeAdjacency.ProbeCardinal(pipeBlock.transform.position, frame, cs, 5,
                s_gasRowProbe, col =>
                {
                    var tank = col.GetComponent<GridGasTank>();
                    if (tank == null) tank = col.GetComponentInParent<GridGasTank>();
                    if (tank != null && tank.Enabled && !yieldedTanks.Contains(tank))
                    {
                        bool typeOk = tank.gasType == type || (!forOutput && tank.stored <= 0.001f);
                        bool stockpileOk = includeStockpile || tank.mode != GridTankMode.Stockpile;
                        if (typeOk && stockpileOk) yieldedTanks.Add(tank);
                    }
                    return false;
                });
        }

        private static bool IsGasPipe(GridBlock block)
        {
            return block != null && block.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null;
        }
    }
}
