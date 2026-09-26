// Assets/Scripts/VoxelEngine/Gas/GasNetwork.cs
//
// Manages gas pipe connectivity. Transfers gas between producers (reactors,
// electrolysers) and consumers (turbines, engines) via connected GasTanks.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Gas
{
    public class GasNetwork : MonoBehaviour
    {
        public static GasNetwork Instance { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("GasNetwork");
            Instance = go.AddComponent<GasNetwork>();
            DontDestroyOnLoad(go);
        }

        private readonly List<GasPipe> _pipes = new();
        private bool _dirty;
        private float _dirtyAt = -1f;
        private readonly List<Vector3> _dirtyPositions = new(4);
        private bool _globalVisualRefresh;

        // ── Tank map (12.24.0-dev perf fix) ──────────────────────────────
        // The old query path ran a physics BFS per question: every visited pipe
        // fired a sphere probe plus a 31-probe cardinal corridor sweep, so one
        // tank lookup on a 10-pipe run cost 300+ overlap queries — twice a second
        // per machine. The map moves that physics to topology-change time plus a
        // slow rolling refresh; queries become dictionary lookups with a cheap
        // range-relevance check (no physics, no BFS, no per-query allocation).
        private readonly Dictionary<GasPipe, int> _netId = new();
        private readonly Dictionary<int, List<GasPipe>> _netPipes = new();
        private readonly Dictionary<int, List<GasTank>> _netTanks = new();
        private int _tankMapCursor;
        private float _rollingAt;
        private const float RollingPipeProbeSeconds = 0.25f;
        private const float TankLinkRelevanceM = 7f; // corridor reach (5 cells + margin) past any net pipe
        private static readonly HashSet<int> s_seedNets = new();
        private static readonly List<GasPipe> s_floodStack = new();

        // Coalesce rapid register/unregister bursts (e.g. placing pipes in a row)
        // into a single rebuild so we don't do O(N) work every frame the player
        // holds the place button. Mirrors PowerNetworkManager.
        private const float RebuildSettleDelay = 0.12f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void Register(GasPipe p)
        {
            if (p == null) return;
            if (!_pipes.Contains(p)) { _pipes.Add(p); MarkDirty(p.transform.position); }
        }

        public void Unregister(GasPipe p)
        {
            if (p == null) return;
            Vector3 formerPosition = p.transform.position;
            if (_pipes.Remove(p))
            {
                for (int i = 0; i < _pipes.Count; i++)
                    if (_pipes[i] != null) _pipes[i].neighbours.Remove(p);
                p.neighbours.Clear();
                _netId.Clear();
                _netPipes.Clear();
                _netTanks.Clear();
                MarkDirty(formerPosition);
            }
        }

        private void MarkDirty(Vector3? position = null)
        {
            if (!_dirty) _dirtyAt = Time.unscaledTime;
            _dirty = true;
            if (!position.HasValue)
            {
                _globalVisualRefresh = true;
                return;
            }

            Vector3 p = position.Value;
            for (int i = 0; i < _dirtyPositions.Count; i++)
                if ((_dirtyPositions[i] - p).sqrMagnitude < 0.01f) return;
            _dirtyPositions.Add(p);
        }

        private void LateUpdate()
        {
            if (_dirty && Time.unscaledTime - _dirtyAt >= RebuildSettleDelay)
            {
                _dirty = false;
                Rebuild();
                RefreshAffectedVisuals();
            }
            // Rolling re-probe: one pipe per tick keeps the tank map honest when
            // grids drift relative to each other, with no detectable burst.
            if (_pipes.Count > 0 && Time.unscaledTime - _rollingAt >= RollingPipeProbeSeconds)
            {
                _rollingAt = Time.unscaledTime;
                _tankMapCursor %= _pipes.Count;
                CollectPipeTanks(_pipes[_tankMapCursor]);
                _tankMapCursor++;
            }
        }

        private void RefreshAffectedVisuals()
        {
            if (_globalVisualRefresh || _dirtyPositions.Count == 0)
            {
                VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged();
            }
            else
            {
                for (int i = 0; i < _dirtyPositions.Count; i++)
                {
                    Vector3 changed = _dirtyPositions[i];
                    VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged(changed, ResolveChangedPipeRadius(changed));
                }
            }
            _dirtyPositions.Clear();
            _globalVisualRefresh = false;
        }

        private float ResolveChangedPipeRadius(Vector3 changedPosition)
        {
            float nearestSqr = 0.04f; // register path reports the pipe's exact transform position
            GasPipe nearest = null;
            for (int i = 0; i < _pipes.Count; i++)
            {
                var pipe = _pipes[i];
                if (pipe == null) continue;
                float distance = (pipe.transform.position - changedPosition).sqrMagnitude;
                if (distance > nearestSqr) continue;
                nearestSqr = distance;
                nearest = pipe;
            }
            return nearest != null ? GridStep(nearest, nearest) * 5.15f + 0.25f : 0f;
        }

        private void Rebuild()
        {
            for (int i = 0; i < _pipes.Count; i++)
                if (_pipes[i] != null) _pipes[i].neighbours.Clear();

            // Strip destroyed pipes before hashing so we don't walk tombstones.
            _pipes.RemoveAll(p => p == null);
            int n = _pipes.Count;
            if (n < 2) return;

            // ── SPATIAL HASH (cell size = 5.25m) ──────────────────────────
            // O(N) neighbour discovery instead of the old O(N^2) double loop
            // that lagged hard once the player laid a hundred+ pipes. The
            // cell size covers any five-cell primary run plus one bounded
            // orthogonal elbow, so valid pipe pairs remain in this bucket or
            // its immediate 3×3×3 neighbours.
            const float CELL = 5.25f;
            const float CELL_INV = 1f / CELL;
            var hash = new Dictionary<Vector3Int, List<GasPipe>>(n * 2);
            Vector3Int Cell(Vector3 p) => new Vector3Int(
                Mathf.FloorToInt(p.x * CELL_INV),
                Mathf.FloorToInt(p.y * CELL_INV),
                Mathf.FloorToInt(p.z * CELL_INV));
            for (int i = 0; i < n; i++)
            {
                var p = _pipes[i];
                var k = Cell(p.transform.position);
                if (!hash.TryGetValue(k, out var bucket)) hash[k] = bucket = new List<GasPipe>(4);
                bucket.Add(p);
            }

            // Build an index map so we don't O(N) IndexOf() in the inner loop.
            var index = new Dictionary<GasPipe, int>(n);
            for (int i = 0; i < n; i++) index[_pipes[i]] = i;

            for (int i = 0; i < n; i++)
            {
                var a = _pipes[i];
                if (a == null) continue;
                Vector3 pa = a.transform.position;
                var c0 = Cell(pa);
                var ga = a.GetComponentInParent<GridBlock>();

                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!hash.TryGetValue(new Vector3Int(c0.x + dx, c0.y + dy, c0.z + dz), out var bucket)) continue;
                    for (int bi = 0; bi < bucket.Count; bi++)
                    {
                        var b = bucket[bi];
                        if (b == null || b == a) continue;
                        // Avoid double-processing — only consider pairs (i,j) with j>i.
                        if (!index.TryGetValue(b, out int j) || j <= i) continue;

                        Vector3 pb = b.transform.position;
                        float step = GridStep(a, b, ga);

                        // Pipe pairs may span five primary cells plus one bounded
                        // orthogonal riser; three-axis diagonals remain excluded.
                        float range = step * 5.2f;
                        if ((pa - pb).sqrMagnitude > range * range) continue;

                        Vector3 connectionDelta = VoxelEngine.Networks.PipeAdjacency.ConnectionDelta(a, b);
                        if (!VoxelEngine.Networks.PipeAdjacency.IsBendablePipeLinkDelta(connectionDelta, step, 5f, step * 0.18f)) continue;

                        if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(a, b)) continue;

                        if (!a.neighbours.Contains(b)) a.neighbours.Add(b);
                        if (!b.neighbours.Contains(a)) b.neighbours.Add(a);
                    }
                }
            }

            AssignNetworkIds();
            if (_netTanks.Count == 0) RefreshTankMap(); // cold start: one placement-time pass
            _tankMapCursor = 0;
        }

        // ── Tank map ─────────────────────────────────────────────────────
        // Connected-component ids over the neighbour graph, so a tank query can
        // admit whole networks without walking them.

        private void AssignNetworkIds()
        {
            _netId.Clear();
            _netPipes.Clear();
            int next = 1;
            for (int i = 0; i < _pipes.Count; i++)
            {
                var root = _pipes[i];
                if (root == null || _netId.ContainsKey(root)) continue;
                s_floodStack.Clear();
                s_floodStack.Add(root);
                _netId[root] = next;
                var members = new List<GasPipe> { root };
                while (s_floodStack.Count > 0)
                {
                    var cur = s_floodStack[s_floodStack.Count - 1];
                    s_floodStack.RemoveAt(s_floodStack.Count - 1);
                    if (cur.neighbours == null) continue;
                    for (int k = 0; k < cur.neighbours.Count; k++)
                    {
                        var nb = cur.neighbours[k];
                        if (nb == null || _netId.ContainsKey(nb)) continue;
                        _netId[nb] = next;
                        members.Add(nb);
                        s_floodStack.Add(nb);
                    }
                }
                _netPipes[next] = members;
                next++;
            }
        }

        private void RefreshTankMap()
        {
            foreach (var list in _netTanks.Values) list.Clear();
            for (int i = 0; i < _pipes.Count; i++) CollectPipeTanks(_pipes[i]);
        }

        /// <summary>Probe the tanks touching one pipe (sphere + cardinal corridor)
        /// and file them under the pipe's network. Runs at rebuild time and in the
        /// rolling refresh — never inside a tank query.</summary>
        private void CollectPipeTanks(GasPipe pipe)
        {
            if (pipe == null || !_netId.TryGetValue(pipe, out int id)) return;
            if (!_netTanks.TryGetValue(id, out var list)) _netTanks[id] = list = new List<GasTank>(4);
            list.RemoveAll(t => t == null);

            // Use a fixed ~2.75 m probe radius for tank detection — connectRadius
            // is kept for explicit links but tank discovery needs a stable face-
            // touch range that works even when connectRadius is tightened.
            int hitCount = Physics.OverlapSphereNonAlloc(pipe.transform.position,
                Mathf.Max(pipe.connectRadius, 2.75f), s_tankProbe, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hitCount; i++)
            {
                var col = s_tankProbe[i];
                var tank = col != null ? col.GetComponent<GasTank>() ?? col.GetComponentInParent<GasTank>() : null;
                if (tank != null && !list.Contains(tank)) list.Add(tank);
            }

            var block = pipe.GetComponentInParent<GridBlock>();
            float pipeStep = block != null && block.Grid != null
                ? GridSizeExt.CellSize(block.Grid.gridSize)
                : GridStep(pipe, pipe);
            Transform frame = block != null && block.Grid != null ? block.Grid.transform : null;
            VoxelEngine.Networks.PipeAdjacency.ProbeCardinal(pipe.transform.position, frame, pipeStep, 5, s_tankProbe, col =>
            {
                var tank = col.GetComponent<GasTank>() ?? col.GetComponentInParent<GasTank>();
                if (tank != null && !list.Contains(tank)) list.Add(tank);
                return false; // collect everything — queries filter by type and direction
            });
        }

        /// <summary>A cached link stays valid while its tank sits within corridor
        /// reach of any pipe in the network — grids that drift apart stop sharing
        /// gas instead of spookily drawing across the gap. Pure distance math.</summary>
        private bool TankStillRelevant(int net, GasTank tank)
        {
            if (!_netPipes.TryGetValue(net, out var members)) return false;
            Vector3 at = tank.transform.position;
            float rangeSqr = TankLinkRelevanceM * TankLinkRelevanceM;
            for (int i = 0; i < members.Count; i++)
            {
                var pipe = members[i];
                if (pipe == null) continue;
                if ((pipe.transform.position - at).sqrMagnitude <= rangeSqr) return true;
            }
            return false;
        }

        // Memoize identical tank lookups briefly: engines, taps and pumps probe on
        // their own 0.5 s windows, and the pipe BFS + corridor probes are the hot
        // path on long runs.
        private const float TankQueryTtl = 0.35f;
        private readonly Dictionary<long, (float time, GasTank tank)> _tankQueryCache = new();

        private static long TankQueryKey(Vector3 origin, GasType type, bool forOutput, bool filtered)
        {
            int x = Mathf.RoundToInt(origin.x * 2f);
            int y = Mathf.RoundToInt(origin.y * 2f);
            int z = Mathf.RoundToInt(origin.z * 2f);
            long k = ((long)(x & 0xFFFFF) << 43) ^ ((long)(y & 0xFFFFF) << 23) ^ ((long)(z & 0xFFFFF) << 3);
            return k ^ ((long)(int)type << 1) ^ (forOutput ? 1L : 0L) ^ (filtered ? 2L : 0L);
        }

        /// <summary>Find a GasTank of the given type reachable from a position via gas pipes.
        /// <paramref name="seedFilter"/> (optional) restricts which pipes may SEED the
        /// network walk — the exhaust gas tap uses it so only pipes anchored to its own
        /// exhaust pipe feed exhaust into a network (a shared oxygen line stays clean).</summary>
        public GasTank FindTankNear(Vector3 origin, GasType type, bool forOutput, float searchDist = 3f,
            float corridorStep = 0f, System.Predicate<GasPipe> seedFilter = null)
        {
            long key = TankQueryKey(origin, type, forOutput, seedFilter != null);
            if (_tankQueryCache.TryGetValue(key, out var memo) && Time.time - memo.time < TankQueryTtl)
            {
                if (!memo.tank) return null;
                return memo.tank;
            }
            var result = FindTankNearUncached(origin, type, forOutput, searchDist, corridorStep, seedFilter);
            _tankQueryCache[key] = (Time.time, result);
            return result;
        }

        private GasTank FindTankNearUncached(Vector3 origin, GasType type, bool forOutput, float searchDist,
            float corridorStep, System.Predicate<GasPipe> seedFilter)
        {
            // Direct adjacency check first.
            int hitCount = Physics.OverlapSphereNonAlloc(origin, searchDist, s_tankProbe, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hitCount; i++)
            {
                var col = s_tankProbe[i];
                var tank = col != null ? col.GetComponent<GasTank>() ?? col.GetComponentInParent<GasTank>() : null;
                if (tank == null) continue;
                if (forOutput && tank.allowOutput && (tank.storedGasType == type || tank.storedGasType == GasType.None) && tank.storedAmount > 0)
                    return tank;
                if (!forOutput && tank.acceptInput && (tank.storedGasType == type || tank.storedGasType == GasType.None))
                    return tank;
            }

            float step = corridorStep > 0.0001f ? corridorStep : VoxelEngine.Networks.PipeAdjacency.DefaultGridSize;
            var viaPort = ProbeTankCardinal(origin, null, step, type, forOutput);
            if (viaPort != null) return viaPort;

            // Cached network lookup — the physics that used to live in this BFS
            // now runs at rebuild time plus a rolling refresh (see CollectPipeTanks).
            // Seed semantics are unchanged: only pipes near the origin (passing the
            // seed filter) admit their networks, exactly like the old BFS seeds.
            s_seedNets.Clear();
            for (int i = 0; i < _pipes.Count; i++)
            {
                var startPipe = _pipes[i];
                if (startPipe == null) continue;
                if (seedFilter != null && !seedFilter(startPipe)) continue;
                if ((startPipe.transform.position - origin).sqrMagnitude > searchDist * searchDist) continue;
                if (_netId.TryGetValue(startPipe, out int seedNet)) s_seedNets.Add(seedNet);
            }
            foreach (int seedNet in s_seedNets)
            {
                if (!_netTanks.TryGetValue(seedNet, out var tanks)) continue;
                for (int i = 0; i < tanks.Count; i++)
                {
                    var tank = tanks[i];
                    if (tank == null || !tank.gameObject.activeInHierarchy) continue;
                    if (!TankStillRelevant(seedNet, tank)) continue;
                    if (forOutput && tank.allowOutput && tank.storedGasType == type && tank.storedAmount > 0)
                        return tank;
                    if (!forOutput && tank.acceptInput && (tank.storedGasType == type || tank.storedGasType == GasType.None))
                        return tank;
                }
            }
            return null;
        }

        private static readonly Collider[] s_tankProbe = new Collider[24];

        private static GasTank ProbeTankCardinal(Vector3 origin, Transform gridFrame, float step, GasType type, bool forOutput)
        {
            GasTank found = null;
            VoxelEngine.Networks.PipeAdjacency.ProbeCardinal(origin, gridFrame, step, 5, s_tankProbe, col =>
            {
                var tank = col.GetComponent<GasTank>();
                if (tank == null) tank = col.GetComponentInParent<GasTank>();
                if (tank == null) return false;
                if (forOutput && tank.allowOutput && (tank.storedGasType == type || tank.storedGasType == GasType.None) && tank.storedAmount > 0)
                {
                    found = tank; return true;
                }
                if (!forOutput && tank.acceptInput && (tank.storedGasType == type || tank.storedGasType == GasType.None))
                {
                    found = tank; return true;
                }
                return false;
            });
            return found;
        }

        private static float GridStep(GasPipe a, GasPipe b, GridBlock ga = null)
        {
            var blockA = ga ?? (a != null ? a.GetComponentInParent<GridBlock>() : null);
            var blockB = b != null ? b.GetComponentInParent<GridBlock>() : null;
            if (blockA != null && blockB != null && blockA.Grid != null && blockA.Grid == blockB.Grid)
            {
                // All grid pipe↔pipe links use the Detail lattice step, regardless
                // of whether an old prefab forgot to mark itself as a precision
                // attachment. This prevents one-left + one-up diagonal links from
                // passing under the loose structural-grid tolerance.
                return GridSizeExt.CellSize(GridSize.Small);
            }
            return VoxelEngine.Networks.PipeAdjacency.DefaultGridSize;
        }

        public void SetDirty() => MarkDirty();
        public void SetDirty(Vector3 changedPosition) => MarkDirty(changedPosition);
        private void OnDestroy() { if (Instance == this) Instance = null; _tankQueryCache.Clear(); }
    }
}
