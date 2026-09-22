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
        public void SetDirty() { s_tankCache.Clear(); _topo.Clear(); _seedMemo.Clear(); }

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
            {
                if (block is VoxelEngine.Gas.GasVent vent && vent.Enabled && vent.Accepts(type)) return true;
                if (block is VoxelEngine.Gas.GridFlareStack flare && flare.Enabled && flare.AcceptsGas(type)) return true;
            }
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
                else if (block is VoxelEngine.Gas.GridFlareStack flare && flare.Enabled)
                    dumped += flare.AcceptGas(type, litres - dumped);
            }
            return dumped;
        }

        /// <summary>
        /// Every non-pipe block reachable from this endpoint through its pipe run: the same
        /// walk the fill/draw paths use, exposed for vent and tap queries. Links come from
        /// the cached grid topology (no physics); enablement stays a live check.
        /// </summary>
        public IEnumerable<GridBlock> ConnectedEndpoints(GridBlock endpoint)
        {
            var grid = endpoint != null ? endpoint.Grid : null;
            if (grid == null) yield break;

            var topo = EnsureTopology(grid);
            if (topo == null) yield break;

            var yielded = new HashSet<GridBlock>();
            foreach (var pipe in ReachablePipes(endpoint))
            {
                if (topo.pipeBlocks.TryGetValue(pipe, out var blocks))
                {
                    foreach (var block in blocks)
                    {
                        if (block == null || block == endpoint || !block.Enabled) continue;
                        if (yielded.Add(block)) yield return block;
                    }
                }
                if (topo.pipeVents.TryGetValue(pipe, out var vents))
                {
                    foreach (var vent in vents)
                    {
                        if (vent == null || vent == endpoint || !vent.Enabled) continue;
                        if (yielded.Add(vent)) yield return vent;
                    }
                }
            }
        }

        /// <summary>Breadth-first sweep of the pipe run touching this endpoint. Every gas
        /// consumer walk needs the same set, so it lives here once.</summary>
        private List<GridBlock> CollectGasPipes(GridBlock endpoint)
        {
            var grid = endpoint != null ? endpoint.Grid : null;
            if (grid == null) return new List<GridBlock>();
            var topo = EnsureTopology(grid);
            if (topo == null) return new List<GridBlock>();
            return CollectGasPipesFrom(grid, endpoint, SeedsFor(grid, topo, endpoint));
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

            var topo = EnsureTopology(grid);
            if (topo == null) return pipes;

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
                ExpandCachedLinks(grid, topo, from, cardinalCheckAdj: false, visited, queue);
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
                {
                    var cachedTank = cached.tanks[i];
                    if (cachedTank != null && cachedTank.Enabled) yield return cachedTank;
                }
                yield break;
            }

            var fresh = new List<GridGasTank>(8);
            foreach (var tank in ConnectedTanks(endpoint, type, forOutput, includeStockpile))
                if (tank != null) fresh.Add(tank);
            s_tankCache[key] = (Time.time, fresh);
            for (int i = 0; i < fresh.Count; i++)
                yield return fresh[i];
        }

        /// <summary>Tanks reachable from an endpoint, walking the cached grid topology
        /// (no physics, no port-tree scans). Link discovery runs at topology time with
        /// the same predicates; blacklist edges and tank state stay live per query.</summary>
        private IEnumerable<GridGasTank> ConnectedTanks(GridBlock endpoint, Gas.GasType type, bool forOutput, bool includeStockpile)
        {
            var grid = endpoint != null ? endpoint.Grid : null;
            if (grid == null || type == Gas.GasType.None) yield break;

            var topo = EnsureTopology(grid);
            if (topo == null) yield break;

            var yieldedTanks = new HashSet<GridGasTank>();
            foreach (var pipeBlock in ReachablePipes(endpoint))
            {
                // Directly plumbed tanks (adjacent + proximity): wrench-blacklist edges apply.
                if (topo.pipeTanksDirect.TryGetValue(pipeBlock, out var direct))
                {
                    for (int i = 0; i < direct.Count; i++)
                    {
                        var tank = direct[i];
                        if (tank == null || !tank.Enabled) continue;
                        if (WrenchBlacklist.IsBlocked(pipeBlock.gameObject, tank.gameObject)) continue;
                        bool typeOk = tank.gasType == type || (!forOutput && tank.stored <= 0.001f);
                        bool stockpileOk = includeStockpile || tank.mode != GridTankMode.Stockpile;
                        if (typeOk && stockpileOk && yieldedTanks.Add(tank)) yield return tank;
                    }
                }
                // Corridor/brute-force tanks: the legacy walkers never blacklist-checked
                // these paths, so neither do we (link membership is still topological).
                if (topo.pipeTanksCorridor.TryGetValue(pipeBlock, out var corridor))
                {
                    for (int i = 0; i < corridor.Count; i++)
                    {
                        var tank = corridor[i];
                        if (tank == null || !tank.Enabled) continue;
                        bool typeOk = tank.gasType == type || (!forOutput && tank.stored <= 0.001f);
                        bool stockpileOk = includeStockpile || tank.mode != GridTankMode.Stockpile;
                        if (typeOk && stockpileOk && yieldedTanks.Add(tank)) yield return tank;
                    }
                }
            }
        }

        // Scratch buffers — enlarged to 32 to avoid missing in dense builds
        // REACHED PIPE TOPOLOGY (12.24.1-dev perf fix)
        // Thrusters query AvailableGasFor/DrawGasFor every physics tick, and every uncached
        // query walked OverlapSpheres, corridor sweeps and port-tree scans - hundreds of
        // physics probes per second per ship (the 5 FPS freezes). Link discovery now runs
        // once per topology change into a per-grid snapshot, using the exact same
        // predicates, and queries follow cached links (microseconds). Live state - wrench
        // blacklist, tank enabled/type/mode - is still evaluated per query, so routing
        // behaviour is unchanged.

        /// <summary>Per-grid snapshot of gas plumbing. Rebuilt when the grid's block
        /// set changes; one pipe is re-probed on a rolling timer as a backstop.</summary>
        private class GridGasTopology
        {
            public readonly HashSet<GridBlock> pipes = new();
            public readonly List<GridBlock> pipeOrder = new();
            public readonly Dictionary<GridBlock, List<GridBlock>> pipeLinksAdj = new();
            public readonly Dictionary<GridBlock, List<GridBlock>> pipeLinksProx = new();
            public readonly Dictionary<GridBlock, List<GridGasTank>> pipeTanksDirect = new();
            public readonly Dictionary<GridBlock, List<GridGasTank>> pipeTanksCorridor = new();
            public readonly Dictionary<GridBlock, List<GridBlock>> pipeBlocks = new();
            public readonly Dictionary<GridBlock, List<GridBlock>> pipeVents = new();
            public readonly Dictionary<GridBlock, List<GridCryobed>> pipeCryos = new();
            public int blockCount;
        }

        private readonly Dictionary<GridEntity, GridGasTopology> _topo = new();
        private readonly Dictionary<GridBlock, (GridGasTopology topo, int count, float time, List<GridBlock> seeds)> _seedMemo = new();
        private readonly List<GridBlock> _deadSeeds = new();
        private const float SeedMemoTtl = 2f;
        private readonly List<GridEntity> _rollingGrids = new();
        private readonly List<GridEntity> _deadGrids = new();
        private int _rollingGridIdx;
        private int _rollingPipeIdx;
        private float _rollingAt;
        private const float RollingPipeProbeSeconds = 0.25f;

        private void Update()
        {
            if (Time.unscaledTime - _rollingAt < RollingPipeProbeSeconds) return;
            _rollingAt = Time.unscaledTime;
            RollingRefresh();
        }

        private GridGasTopology EnsureTopology(GridEntity grid)
        {
            if (grid == null) return null;
            if (!_topo.TryGetValue(grid, out var topo) || topo == null)
            {
                topo = new GridGasTopology();
                _topo[grid] = topo;
                RebuildTopology(grid, topo);
                return topo;
            }
            int count = 0;
            bool nulls = false;
            foreach (var block in grid.AllBlocks)
            {
                if (block == null) nulls = true;
                else count++;
            }
            if (nulls || count != topo.blockCount) RebuildTopology(grid, topo);
            return topo;
        }

        private void RebuildTopology(GridEntity grid, GridGasTopology topo)
        {
            topo.pipes.Clear();
            topo.pipeOrder.Clear();
            topo.pipeLinksAdj.Clear();
            topo.pipeLinksProx.Clear();
            topo.pipeTanksDirect.Clear();
            topo.pipeTanksCorridor.Clear();
            topo.pipeBlocks.Clear();
            topo.pipeVents.Clear();
            topo.pipeCryos.Clear();

            var nonPipes = new List<GridBlock>();
            var tanks = new List<GridGasTank>();
            var vents = new List<GridBlock>();
            var cryos = new List<GridCryobed>();
            int count = 0;
            foreach (var block in grid.AllBlocks)
            {
                if (block == null) continue;
                count++;
                if (IsGasPipe(block))
                {
                    topo.pipes.Add(block);
                    topo.pipeOrder.Add(block);
                    continue;
                }
                nonPipes.Add(block);
                if (block is GridGasTank tank) tanks.Add(tank);
                else if (block is GridCryobed cryo) cryos.Add(cryo);
                if (block is Gas.GasVent || block is VoxelEngine.Gas.GridFlareStack) vents.Add(block);
            }
            topo.blockCount = count;

            float cs = grid.gridSize.CellSize();
            float detail = GridSize.Small.CellSize();
            for (int i = 0; i < topo.pipeOrder.Count; i++)
            {
                var pipe = topo.pipeOrder[i];
                ComputePipeLinks(grid, topo, pipe, cs);
                ComputePipeTanks(grid, topo, pipe, cs, detail, tanks);
                ComputePipeBlocks(grid, topo, pipe, cs, detail, nonPipes, vents, cryos);
            }
        }

        private void ComputePipeLinks(GridEntity grid, GridGasTopology topo, GridBlock pipe, float cs)
        {
            var adj = new List<GridBlock>();
            foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, pipe))
            {
                if (adjacent != null && adjacent != pipe && IsGasPipe(adjacent) && !adj.Contains(adjacent))
                    adj.Add(adjacent);
            }
            topo.pipeLinksAdj[pipe] = adj;

            var prox = new List<GridBlock>();
            foreach (var candidate in ProximityPipes(grid, pipe, cs))
            {
                if (candidate == null || candidate == pipe) continue;
                if (!AreDetailPipesCardinalLinked(grid, pipe, candidate)) continue;
                if (!adj.Contains(candidate) && !prox.Contains(candidate)) prox.Add(candidate);
            }
            topo.pipeLinksProx[pipe] = prox;
        }

        private void ComputePipeTanks(GridEntity grid, GridGasTopology topo, GridBlock pipe,
            float cs, float detail, List<GridGasTank> tanks)
        {
            var direct = new List<GridGasTank>();
            foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, pipe))
            {
                if (adjacent is GridGasTank tank && !direct.Contains(tank)) direct.Add(tank);
            }
            foreach (var nearby in ProximityBlocks(grid, pipe, cs))
            {
                if (nearby is GridGasTank tank && !direct.Contains(tank)) direct.Add(tank);
            }
            topo.pipeTanksDirect[pipe] = direct;

            var corridor = new List<GridGasTank>();
            ProbePipeTankCorridorAll(grid, pipe, detail, corridor);
            float smallStep = GridSize.Small.CellSize();
            var prefixes = VoxelEngine.Maritime.MaritimePorts.GasPrefixes;
            for (int i = 0; i < tanks.Count; i++)
            {
                var tank = tanks[i];
                if (tank == null || direct.Contains(tank) || corridor.Contains(tank)) continue;
                if (IsTankPortWithinDetailLink(grid, pipe, tank, prefixes, smallStep))
                    corridor.Add(tank);
            }
            topo.pipeTanksCorridor[pipe] = corridor;
        }

        private void ComputePipeBlocks(GridEntity grid, GridGasTopology topo, GridBlock pipe,
            float cs, float detail, List<GridBlock> nonPipes, List<GridBlock> vents, List<GridCryobed> cryos)
        {
            var ventLinks = new List<GridBlock>();
            for (int i = 0; i < vents.Count; i++)
            {
                if (vents[i] != null && BlocksAreGasLinked(vents[i], pipe, detail)) ventLinks.Add(vents[i]);
            }
            topo.pipeVents[pipe] = ventLinks;

            var prefixes = VoxelEngine.Maritime.MaritimePorts.GasPrefixes;
            var blockLinks = new List<GridBlock>();
            for (int i = 0; i < nonPipes.Count; i++)
            {
                var block = nonPipes[i];
                if (block is Gas.GasVent || block is VoxelEngine.Gas.GridFlareStack) continue;
                if (IsTankPortWithinDetailLink(grid, pipe, block, prefixes, detail)) blockLinks.Add(block);
            }
            topo.pipeBlocks[pipe] = blockLinks;

            var cryoLinks = new List<GridCryobed>();
            for (int i = 0; i < cryos.Count; i++)
            {
                if (IsTankPortWithinDetailLink(grid, pipe, cryos[i], prefixes, detail)) cryoLinks.Add(cryos[i]);
            }
            topo.pipeCryos[pipe] = cryoLinks;
        }

        /// <summary>Topology-time corridor sweep: the same probe pattern as the legacy
        /// filtered walk, but collects every port-aligned tank while type/mode/enabled
        /// stay live query filters. Like the legacy walk it deliberately applies no
        /// grid check, so a docked neighbour's tank on the corridor still links.</summary>
        private static void ProbePipeTankCorridorAll(GridEntity grid, GridBlock pipeBlock,
            float detail, List<GridGasTank> collected)
        {
            if (pipeBlock == null || collected == null) return;
            const int maxCells = 5;
            Transform frame = pipeBlock.Grid != null ? pipeBlock.Grid.transform : null;
            PipeAdjacency.ProbeCardinal(pipeBlock.transform.position, frame, detail, maxCells,
                s_gasRowProbe, col =>
                {
                    var tank = col.GetComponent<GridGasTank>();
                    if (tank == null) tank = col.GetComponentInParent<GridGasTank>();
                    if (tank != null
                        && IsTankPortWithinDetailLink(grid, pipeBlock, tank,
                            VoxelEngine.Maritime.MaritimePorts.GasPrefixes, detail)
                        && !collected.Contains(tank)) collected.Add(tank);
                    return false;
                }, radiusScale: 2.2f);
        }

        private void RefreshPipeLinks(GridEntity grid, GridGasTopology topo, GridBlock pipe)
        {
            if (grid == null || topo == null || pipe == null || pipe.Grid != grid) return;
            if (!IsGasPipe(pipe) || !topo.pipes.Contains(pipe)) return;
            float cs = grid.gridSize.CellSize();
            float detail = GridSize.Small.CellSize();
            var tanks = new List<GridGasTank>();
            var vents = new List<GridBlock>();
            var cryos = new List<GridCryobed>();
            var nonPipes = new List<GridBlock>();
            foreach (var block in grid.AllBlocks)
            {
                if (block == null || IsGasPipe(block)) continue;
                nonPipes.Add(block);
                if (block is GridGasTank tank) tanks.Add(tank);
                else if (block is GridCryobed cryo) cryos.Add(cryo);
                if (block is Gas.GasVent || block is VoxelEngine.Gas.GridFlareStack) vents.Add(block);
            }
            ComputePipeLinks(grid, topo, pipe, cs);
            ComputePipeTanks(grid, topo, pipe, cs, detail, tanks);
            ComputePipeBlocks(grid, topo, pipe, cs, detail, nonPipes, vents, cryos);
        }

        private void RollingRefresh()
        {
            _rollingGrids.Clear();
            _deadGrids.Clear();
            foreach (var pair in _topo)
            {
                if (pair.Key == null) _deadGrids.Add(pair.Key);
                else _rollingGrids.Add(pair.Key);
            }
            for (int i = 0; i < _deadGrids.Count; i++) _topo.Remove(_deadGrids[i]);
            _deadSeeds.Clear();
            foreach (var pair in _seedMemo)
            {
                if (pair.Key == null) _deadSeeds.Add(pair.Key);
            }
            for (int i = 0; i < _deadSeeds.Count; i++) _seedMemo.Remove(_deadSeeds[i]);
            if (_rollingGrids.Count == 0) return;
            _rollingGridIdx %= _rollingGrids.Count;
            var grid = _rollingGrids[_rollingGridIdx];
            _rollingGridIdx++;
            if (!_topo.TryGetValue(grid, out var topo) || topo.pipeOrder.Count == 0) return;
            _rollingPipeIdx %= topo.pipeOrder.Count;
            var pipe = topo.pipeOrder[_rollingPipeIdx];
            _rollingPipeIdx++;
            if (pipe == null || !topo.pipes.Contains(pipe)) return;
            RefreshPipeLinks(grid, topo, pipe);
        }

        /// <summary>Endpoint seed pipes, memoized per topology: the endpoint's own
        /// adjacency only changes when the grid's block set does.</summary>
        private List<GridBlock> SeedsFor(GridEntity grid, GridGasTopology topo, GridBlock endpoint)
        {
            if (endpoint != null && _seedMemo.TryGetValue(endpoint, out var memo)
                && memo.topo == topo && memo.count == topo.blockCount
                && memo.seeds != null && Time.time - memo.time < SeedMemoTtl)
                return memo.seeds;

            var seeds = new List<GridBlock>(4);
            if (grid != null && topo != null && endpoint != null)
            {
                float cs = grid.gridSize.CellSize();
                foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, endpoint))
                {
                    if (adjacent != null && topo.pipes.Contains(adjacent) && !seeds.Contains(adjacent))
                        seeds.Add(adjacent);
                }
                for (int i = 0; i < topo.pipeOrder.Count; i++)
                {
                    var pipe = topo.pipeOrder[i];
                    if (pipe != null && BlocksAreGasLinked(endpoint, pipe, cs) && !seeds.Contains(pipe))
                        seeds.Add(pipe);
                }
                _seedMemo[endpoint] = (topo, topo.blockCount, Time.time, seeds);
            }
            return seeds;
        }

        /// <summary>BFS over cached pipe links with live wrench-blacklist edges.
        /// The cryo walker additionally cardinal-checks adjacent steps (legacy).</summary>
        private List<GridBlock> ReachablePipes(GridBlock endpoint, bool cardinalCheckAdj = false)
        {
            var pipes = new List<GridBlock>();
            var grid = endpoint != null ? endpoint.Grid : null;
            if (grid == null) return pipes;
            var topo = EnsureTopology(grid);
            if (topo == null) return pipes;

            var visited = new HashSet<GridBlock>();
            var queue = new Queue<GridBlock>();
            foreach (var seed in SeedsFor(grid, topo, endpoint))
            {
                if (seed != null && !WrenchBlacklist.IsBlocked(endpoint.gameObject, seed.gameObject)
                    && visited.Add(seed)) queue.Enqueue(seed);
            }
            while (queue.Count > 0)
            {
                var from = queue.Dequeue();
                pipes.Add(from);
                ExpandCachedLinks(grid, topo, from, cardinalCheckAdj, visited, queue);
            }
            return pipes;
        }

        private void ExpandCachedLinks(GridEntity grid, GridGasTopology topo, GridBlock from,
            bool cardinalCheckAdj, HashSet<GridBlock> visited, Queue<GridBlock> queue)
        {
            if (topo.pipeLinksAdj.TryGetValue(from, out var adj))
            {
                for (int i = 0; i < adj.Count; i++)
                {
                    var next = adj[i];
                    if (next == null) continue;
                    if (cardinalCheckAdj && !AreDetailPipesCardinalLinked(grid, from, next)) continue;
                    if (WrenchBlacklist.IsBlocked(from.gameObject, next.gameObject)) continue;
                    if (visited.Add(next)) queue.Enqueue(next);
                }
            }
            if (topo.pipeLinksProx.TryGetValue(from, out var prox))
            {
                for (int i = 0; i < prox.Count; i++)
                {
                    var next = prox[i];
                    if (next == null) continue;
                    if (WrenchBlacklist.IsBlocked(from.gameObject, next.gameObject)) continue;
                    if (visited.Add(next)) queue.Enqueue(next);
                }
            }
        }

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

        private static bool AreDetailPipesCardinalLinked(GridEntity grid, GridBlock a, GridBlock b)
        {
            if (grid == null || a == null || b == null) return false;
            float detail = GridSize.Small.CellSize();
            Vector3 localDelta = grid.transform.InverseTransformVector(b.transform.position - a.transform.position);
            return PipeAdjacency.IsCoplanarPipeLinkDelta(localDelta, detail, 5f, detail * 0.18f);
        }

        /// <summary>Cryobeds reachable from an endpoint, walking the cached grid topology.</summary>
        private IEnumerable<GridCryobed> ConnectedCryobeds(GridBlock endpoint)
        {
            var grid = endpoint != null ? endpoint.Grid : null;
            if (grid == null) yield break;

            var topo = EnsureTopology(grid);
            if (topo == null) yield break;

            var yielded = new HashSet<GridCryobed>();
            foreach (var pipeBlock in ReachablePipes(endpoint, cardinalCheckAdj: true))
            {
                if (!topo.pipeCryos.TryGetValue(pipeBlock, out var cryos)) continue;
                for (int i = 0; i < cryos.Count; i++)
                {
                    var cryo = cryos[i];
                    if (cryo == null || !cryo.Enabled) continue;
                    if (yielded.Add(cryo)) yield return cryo;
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
