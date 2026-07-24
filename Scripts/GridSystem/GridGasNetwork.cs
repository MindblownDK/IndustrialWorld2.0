// Assets/Scripts/VoxelEngine/GridSystem/GridGasNetwork.cs
//
// Per-grid gas distribution. There is ONE pipe system: the normal/static GasPipe
// component can be placed onto a grid and then counts as that grid's gas conduit.
// Gas transfer is topology-based: a producer/consumer must touch a connected gas
// pipe run that reaches a compatible tank.
//
// v5.63.1-dev — FIX: gas pipes ↔ gas tanks now reliably connect at 5 squares:
//   • Increased probe/collider buffers (12→32) so dense builds don't miss tanks.
//   • Widened proximity radius for detail pipes (2.25→3.25m) and bodyRange (2×→3×).
//   • Probe corridor radiusScale 1.6→2.2, plus brute-force cardinal-link fallback
//     that scans ALL grid tanks within 5 cells of ANY visited pipe (mirrors liquid).
//   • Added dedicated EnsureGasTankPorts in setup (see VoxelEngineSetupWindow).

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

        private const float TankCacheTtl = 0.15f;
        private static readonly Dictionary<long, (float time, List<GridGasTank> tanks)> s_tankCache = new();
        private static long TankCacheKey(GridBlock endpoint, Gas.GasType type, bool forOutput, bool includeStockpile)
        {
            var id = endpoint != null ? endpoint.GetEntityId().GetHashCode() : 0;
            return ((long)id << 24) ^ ((long)(int)type << 8) ^ ((forOutput ? 1L : 0L) << 4) ^ (includeStockpile ? 1L : 0L);
        }
        public void SetDirty() => s_tankCache.Clear();

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

        private IEnumerable<GridGasTank> CachedTanks(GridBlock endpoint, Gas.GasType type, bool forOutput, bool includeStockpile)
        {
            long key = TankCacheKey(endpoint, type, forOutput, includeStockpile);
            if (s_tankCache.TryGetValue(key, out var cached) && Time.time - cached.time < TankCacheTtl)
            {
                for (int i = 0; i < cached.tanks.Count; i++)
                    if (cached.tanks[i] != null) yield return cached.tanks[i];
                yield break;
            }

            var fresh = new List<GridGasTank>(8);
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
                if (pipe == null || WrenchBlacklist.IsBlocked(endpoint.gameObject, pipe.gameObject)
                    || !visitedPipes.Add(pipe)) return;
                queue.Enqueue(pipe);
            }

            foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, endpoint))
                if (IsGasPipe(adjacent)) SeedPipe(adjacent);
            foreach (var pipe in grid.AllBlocks)
            {
                if (pipe == null || !IsGasPipe(pipe)) continue;
                if (BlocksAreGasLinked(endpoint, pipe, cs)) SeedPipe(pipe);
            }

            while (queue.Count > 0)
            {
                var pipeBlock = queue.Dequeue();

                foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, pipeBlock))
                {
                    if (IsGasPipe(adjacent)
                        && !WrenchBlacklist.IsBlocked(pipeBlock.gameObject, adjacent.gameObject)
                        && visitedPipes.Add(adjacent)) queue.Enqueue(adjacent);
                }
                foreach (var pipe in ProximityPipes(grid, pipeBlock, cs))
                {
                    if (!WrenchBlacklist.IsBlocked(pipeBlock.gameObject, pipe.gameObject)
                        && visitedPipes.Add(pipe)) queue.Enqueue(pipe);
                }

                foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, pipeBlock))
                {
                    if (adjacent is GridGasTank tank && tank.Enabled
                        && !WrenchBlacklist.IsBlocked(pipeBlock.gameObject, tank.gameObject))
                    {
                        bool typeOk = tank.gasType == type || (!forOutput && tank.stored <= 0.001f);
                        bool stockpileOk = includeStockpile || tank.mode != GridTankMode.Stockpile;
                        if (typeOk && stockpileOk && yieldedTanks.Add(tank)) yield return tank;
                    }
                }
                foreach (var maybeTank in ProximityBlocks(grid, pipeBlock, cs))
                {
                    if (maybeTank is not GridGasTank tank || !tank.Enabled
                        || WrenchBlacklist.IsBlocked(pipeBlock.gameObject, tank.gameObject)) continue;
                    bool typeOk = tank.gasType == type || (!forOutput && tank.stored <= 0.001f);
                    bool stockpileOk = includeStockpile || tank.mode != GridTankMode.Stockpile;
                    if (typeOk && stockpileOk && yieldedTanks.Add(tank)) yield return tank;
                }
                ProbeGasTankCorridor(pipeBlock, type, forOutput, includeStockpile, yieldedTanks);
            }

            // ── Brute-force 5-cell cardinal fallback ──────────────────
            // Any tank within 5 cells (cardinal) of ANY visited pipe is connected,
            // even if the OverlapSphere probe missed due to collider gaps or slight
            // off-axis placement. This guarantees the advertised "5 grid squares".
            if (visitedPipes.Count > 0)
            {
                float smallStep = GridSize.Small.CellSize();
                float structuralStep = grid.gridSize.CellSize();
                float maxRange = Mathf.Max(structuralStep * 5f, 3f);
                foreach (var block in grid.AllBlocks)
                {
                    if (block is not GridGasTank tank || !tank.Enabled || yieldedTanks.Contains(tank)) continue;
                    bool typeOk = tank.gasType == type || (!forOutput && tank.stored <= 0.001f);
                    bool stockpileOk = includeStockpile || tank.mode != GridTankMode.Stockpile;
                    if (!typeOk || !stockpileOk) continue;
                    foreach (var pipe in visitedPipes)
                    {
                        Vector3 delta = pipe != null ? pipe.transform.position - tank.transform.position : Vector3.zero;
                        Vector3 localDelta = grid.transform.InverseTransformVector(delta);
                        if (PipeAdjacency.IsCardinalLinkDelta(localDelta, smallStep, 5f, smallStep * 0.55f)
                            || PipeAdjacency.IsCardinalLinkDelta(localDelta, structuralStep, 5f, structuralStep * 0.35f)
                            || delta.sqrMagnitude <= maxRange * maxRange && PipeAdjacency.IsAxisAlignedWithinDelta(localDelta, smallStep, 5f, smallStep * 0.75f))
                        {
                            if (yieldedTanks.Add(tank)) yield return tank;
                            break;
                        }
                    }
                }
            }
        }

        // Scratch buffers — enlarged to 32 to avoid missing in dense builds
        private static readonly Collider[] s_gasProbe = new Collider[32];
        private static readonly List<GridBlock> s_gasProximityResult = new(32);

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
            // More tolerant body range — gas tanks sit 1 face away and should still link
            float bodyRange = endpoint.EffectiveCellSize * 3.0f;
            return (endpoint.transform.position - pipe.transform.position).sqrMagnitude <= bodyRange * bodyRange;
        }

        private static IEnumerable<GridBlock> ProximityPipes(GridEntity grid, GridBlock origin, float cs)
        {
            s_gasProximityResult.Clear();
            if (grid == null || origin == null) yield break;
            float radius = origin.IsPrecisionAttachment
                ? Mathf.Max(GridSize.Large.CellSize() * 1.5f, 3.25f)
                : Mathf.Max(origin.EffectiveCellSize, GridSize.Small.CellSize()) * 2.0f;
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
            float radius = origin.IsPrecisionAttachment
                ? Mathf.Max(GridSize.Large.CellSize() * 1.5f, 3.25f)
                : Mathf.Max(origin.EffectiveCellSize, GridSize.Small.CellSize()) * 2.0f;
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

        private static readonly Collider[] s_gasRowProbe = new Collider[32];

        private static void ProbeGasTankCorridor(GridBlock pipeBlock, Gas.GasType type,
            bool forOutput, bool includeStockpile, HashSet<GridGasTank> yieldedTanks)
        {
            if (pipeBlock == null) return;
            float structural = pipeBlock.Grid != null ? pipeBlock.Grid.gridSize.CellSize() : 2.5f;
            float detail = GridSize.Small.CellSize();
            float reach = Mathf.Max(structural * 5f, 3f);
            int maxCells = Mathf.CeilToInt(reach / detail);
            Transform frame = pipeBlock.Grid != null ? pipeBlock.Grid.transform : null;
            PipeAdjacency.ProbeCardinal(pipeBlock.transform.position, frame, detail, maxCells,
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
                }, radiusScale: 2.2f);
        }

        private static bool IsGasPipe(GridBlock block)
        {
            return block != null && block.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null;
        }
    }
}
