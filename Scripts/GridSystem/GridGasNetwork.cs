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

        // ── DESIGN RULE (9.27.0) ──────────────────────────────────────────────
        // Gas on a grid moves through PIPES ONLY. There is no grid-wide gas pool.
        // The three grid-wide helpers below bypass pipe topology, have no callers,
        // and are retained purely so any old serialized reference still compiles.
        // Use the endpoint-based DrawGasFor / FillGasFor / AvailableGasFor instead,
        // which walk the actual pipe network out of a block's own gas ports.

        [System.Obsolete("Gas moves through pipes only. Use AvailableGasFor(block, ...) so the pipe topology is respected.")]
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

        /// <summary>
        /// Whether this block's own gas run reaches storage at all, whatever is in it. It is
        /// how an engine tells "plumbed but empty" (a fault to fix) apart from "never plumbed"
        /// (free to breathe the room): the difference is whether a line ends here, not what the
        /// line holds. `type` may be GasType.None to ask about any gas.
        /// </summary>
        public bool HasStorageFor(GridBlock endpoint, Gas.GasType type)
        {
            if (endpoint == null) return false;
            foreach (var tank in CachedTanks(endpoint, type, forOutput: true, includeStockpile: true))
                if (tank != null && tank.Enabled) return true;
            // A cryobed on an oxygen line is storage too, even when it is charged to the brim.
            if (type == Gas.GasType.Oxygen)
                foreach (var _ in ConnectedCryobeds(endpoint)) return true;
            return false;
        }

        /// <summary>True when a vent reachable from this endpoint can destroy gas right now.</summary>
        public bool HasVentFor(GridBlock endpoint, Gas.GasType type)
        {
            if (endpoint == null || endpoint.Grid == null || type == Gas.GasType.None) return false;
            foreach (var block in ConnectedEndpoints(endpoint))
                if (block is VoxelEngine.Gas.GasVent vent && vent.Enabled && vent.Accepts(type)) return true;
            return false;
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
            if (type == Gas.GasType.Oxygen && producer.Grid != null && filled < litres)
            {
                foreach (var cryobed in ConnectedCryobeds(producer))
                {
                    if (filled >= litres) break;
                    if (cryobed == null || !cryobed.Enabled) continue;
                    filled += cryobed.AddOxygen(litres - filled);
                }
            }
            // ── Vent over (9.32.0) ────────────────────────────────────────────
            // Whatever no tank wanted goes down the vent run and out of the ship.
            // This is what makes exhaust a disposable product instead of a
            // storage obligation: plumb a run to a GasVent and it is gone.
            if (producer.Grid != null && filled < litres)
                filled += DumpToVents(producer, type, litres - filled);
            return filled;
        }

        /// <summary>
        /// Pushes gas straight out of the vents reachable from this endpoint, ignoring tanks.
        /// A producer that already wrote into its own tank uses this for the overflow, so a
        /// disposal line works even when the run carries no vessel at all.
        /// </summary>
        public float DumpGas(GridBlock endpoint, Gas.GasType type, float litres) => DumpToVents(endpoint, type, litres);

        /// <summary>Pushes gas out of the network through every vent reachable from this
        /// endpoint. Returns how much was actually destroyed (it can be all of it).</summary>
        private float DumpToVents(GridBlock endpoint, Gas.GasType type, float litres)
        {
            if (litres <= 0.0001f) return 0f;
            float dumped = 0f;
            foreach (var block in ConnectedEndpoints(endpoint))
            {
                if (dumped >= litres) break;
                if (block is VoxelEngine.Gas.GasVent vent && vent.Enabled)
                    dumped += vent.Accept(type, litres - dumped);
            }
            return dumped;
        }

        /// <summary>
        /// Every non-pipe block reachable from this endpoint through its pipe run: the same
        /// walk the fill/draw paths use, exposed for vent and tap queries. The pipe graph is
        /// collected first and the scan runs over the grid afterwards, which keeps the
        /// traversal in one place (ConnectedGasPipes) instead of three.
        /// </summary>
        public IEnumerable<GridBlock> ConnectedEndpoints(GridBlock endpoint)
        {
            var grid = endpoint != null ? endpoint.Grid : null;
            if (grid == null) yield break;

            var pipes = CollectGasPipes(endpoint);
            if (pipes.Count == 0) yield break;

            float detail = GridSize.Small.CellSize();
            foreach (var block in grid.AllBlocks)
            {
                if (block == null || block == endpoint || IsGasPipe(block)) continue;
                if (!block.Enabled) continue;
                bool linked = block is VoxelEngine.Gas.GasVent;   // a vent is a plain box:
                foreach (var pipe in pipes)                        // centre proximity is enough
                {
                    if (!linked && !IsTankPortWithinDetailLink(grid, pipe, block,
                            VoxelEngine.Maritime.MaritimePorts.GasPrefixes, detail)) continue;
                    if (linked && !BlocksAreGasLinked(block, pipe, detail)) continue;
                    yield return block;
                    break;
                }
            }
        }

        /// <summary>Breadth-first sweep of the pipe run touching this endpoint. Every gas
        /// consumer walk needs the same set, so it lives here once.</summary>
        private List<GridBlock> CollectGasPipes(GridBlock endpoint)
        {
            var grid = endpoint != null ? endpoint.Grid : null;
            if (grid == null) return new List<GridBlock>();

            float cs = grid.gridSize.CellSize();
            var seeds = new List<GridBlock>(4);
            foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, endpoint))
                if (IsGasPipe(adjacent)) seeds.Add(adjacent);
            foreach (var pipe in grid.AllBlocks)
                if (IsGasPipe(pipe) && BlocksAreGasLinked(endpoint, pipe, cs)) seeds.Add(pipe);
            return CollectGasPipesFrom(grid, endpoint, seeds);
        }

        /// <summary>
        /// The same sweep with the caller's own seed pipes — used by an exhaust tap, which
        /// may only ride the run ANCHORED to its stack and must never reach into a neighbouring
        /// oxygen line just because the two runs happen to pass each other.
        /// </summary>
        public List<GridBlock> CollectGasPipesFrom(GridEntity grid, GridBlock endpoint, List<GridBlock> seedPipes)
        {
            var pipes = new List<GridBlock>();
            if (grid == null || seedPipes == null || seedPipes.Count == 0) return pipes;

            float cs = grid.gridSize.CellSize();
            var visited = new HashSet<GridBlock>();
            var queue = new Queue<GridBlock>();

            void Seed(GridBlock pipe)
            {
                if (pipe == null || !IsGasPipe(pipe)) return;
                if (endpoint != null && WrenchBlacklist.IsBlocked(endpoint.gameObject, pipe.gameObject)) return;
                if (visited.Add(pipe)) queue.Enqueue(pipe);
            }

            for (int i = 0; i < seedPipes.Count; i++) Seed(seedPipes[i]);

            while (queue.Count > 0)
            {
                var from = queue.Dequeue();
                pipes.Add(from);
                foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, from))
                {
                    if (!IsGasPipe(adjacent)) continue;
                    if (WrenchBlacklist.IsBlocked(from.gameObject, adjacent.gameObject)) continue;
                    if (visited.Add(adjacent)) queue.Enqueue(adjacent);
                }
                foreach (var pipe in ProximityPipes(grid, from, cs))
                {
                    if (!AreDetailPipesCardinalLinked(grid, from, pipe)) continue;
                    if (WrenchBlacklist.IsBlocked(from.gameObject, pipe.gameObject)) continue;
                    if (visited.Add(pipe)) queue.Enqueue(pipe);
                }
            }
            return pipes;
        }

        [System.Obsolete("Gas moves through pipes only. Use DrawGasFor(block, ...) so the pipe topology is respected.")]
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

        [System.Obsolete("Gas moves through pipes only. Use FillGasFrom(block, ...) so the pipe topology is respected.")]
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
            var corridorTanks = new List<GridGasTank>(4);
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
                    if (!AreDetailPipesCardinalLinked(grid, pipeBlock, pipe)) continue;
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
                ProbeGasTankCorridor(pipeBlock, type, forOutput, includeStockpile, yieldedTanks, corridorTanks);
                for (int i = 0; i < corridorTanks.Count; i++)
                    yield return corridorTanks[i];
            }

            // ── Brute-force 5-cell cardinal fallback ──────────────────
            // Any tank within 5 cells (cardinal) of ANY visited pipe is connected,
            // even if the OverlapSphere probe missed due to collider gaps or slight
            // off-axis placement. This guarantees the advertised "5 grid squares".
            if (visitedPipes.Count > 0)
            {
                float smallStep = GridSize.Small.CellSize();
                foreach (var block in grid.AllBlocks)
                {
                    if (block is not GridGasTank tank || !tank.Enabled || yieldedTanks.Contains(tank)) continue;
                    bool typeOk = tank.gasType == type || (!forOutput && tank.stored <= 0.001f);
                    bool stockpileOk = includeStockpile || tank.mode != GridTankMode.Stockpile;
                    if (!typeOk || !stockpileOk) continue;
                    foreach (var pipe in visitedPipes)
                    {
                        if (IsTankPortWithinDetailLink(grid, pipe, tank,
                                VoxelEngine.Maritime.MaritimePorts.GasPrefixes, smallStep))
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
            // Five detail cells are allowed on the same plane. The strict coplanar
            // predicate filters candidates after this broad but bounded probe.
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
            bool forOutput, bool includeStockpile, HashSet<GridGasTank> yieldedTanks, List<GridGasTank> newlyLinked)
        {
            newlyLinked?.Clear();
            if (pipeBlock == null) return;
            float detail = GridSize.Small.CellSize();
            const int maxCells = 5;
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
                        bool portAligned = IsTankPortWithinDetailLink(pipeBlock.Grid, pipeBlock, tank,
                            VoxelEngine.Maritime.MaritimePorts.GasPrefixes, detail);
                        if (typeOk && stockpileOk && portAligned && yieldedTanks.Add(tank)) newlyLinked?.Add(tank);
                    }
                    return false;
                }, radiusScale: 2.2f);
        }

        private static bool AreDetailPipesCardinalLinked(GridEntity grid, GridBlock a, GridBlock b)
        {
            if (grid == null || a == null || b == null) return false;
            float detail = GridSize.Small.CellSize();
            Vector3 localDelta = grid.transform.InverseTransformVector(b.transform.position - a.transform.position);
            return PipeAdjacency.IsCoplanarPipeLinkDelta(localDelta, detail, 5f, detail * 0.18f);
        }

        private static IEnumerable<GridCryobed> ConnectedCryobeds(GridBlock endpoint)
        {
            var grid = endpoint != null ? endpoint.Grid : null;
            if (grid == null) yield break;
            float cs = grid.gridSize.CellSize();
            float detail = GridSize.Small.CellSize();
            var visitedPipes = new HashSet<GridBlock>();
            var queue = new Queue<GridBlock>();

            void Seed(GridBlock pipe)
            {
                if (pipe == null || !IsGasPipe(pipe) || WrenchBlacklist.IsBlocked(endpoint.gameObject, pipe.gameObject)) return;
                if (visitedPipes.Add(pipe)) queue.Enqueue(pipe);
            }

            foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, endpoint))
                if (IsGasPipe(adjacent)) Seed(adjacent);
            foreach (var block in grid.AllBlocks)
                if (IsGasPipe(block) && BlocksAreGasLinked(endpoint, block, cs)) Seed(block);

            while (queue.Count > 0)
            {
                var pipeBlock = queue.Dequeue();
                foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, pipeBlock))
                    if (IsGasPipe(adjacent)
                        && !WrenchBlacklist.IsBlocked(pipeBlock.gameObject, adjacent.gameObject)
                        && AreDetailPipesCardinalLinked(grid, pipeBlock, adjacent)
                        && visitedPipes.Add(adjacent)) queue.Enqueue(adjacent);
                foreach (var pipe in ProximityPipes(grid, pipeBlock, cs))
                    if (AreDetailPipesCardinalLinked(grid, pipeBlock, pipe)
                        && !WrenchBlacklist.IsBlocked(pipeBlock.gameObject, pipe.gameObject)
                        && visitedPipes.Add(pipe)) queue.Enqueue(pipe);
            }

            foreach (var block in grid.AllBlocks)
            {
                if (block is not GridCryobed cryo || !cryo.Enabled) continue;
                foreach (var pipe in visitedPipes)
                {
                    if (IsTankPortWithinDetailLink(grid, pipe, cryo,
                            VoxelEngine.Maritime.MaritimePorts.GasPrefixes, detail))
                    {
                        yield return cryo;
                        break;
                    }
                }
            }
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

        private static bool IsGasPipe(GridBlock block)
        {
            return block != null && block.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null;
        }
    }
}
