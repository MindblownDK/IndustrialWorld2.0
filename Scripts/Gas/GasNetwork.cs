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
            if (!_pipes.Contains(p)) { _pipes.Add(p); MarkDirty(); }
        }

        public void Unregister(GasPipe p)
        {
            if (p == null) return;
            if (_pipes.Remove(p))
            {
                for (int i = 0; i < _pipes.Count; i++)
                    _pipes[i].neighbours.Remove(p);
                p.neighbours.Clear();
                // A removal is topology-changing — bump version so visuals
                // refresh once. Additions wait for the rebuild so they fire
                // exactly once after links settle.
                VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged();
            }
        }

        private void MarkDirty()
        {
            if (!_dirty) _dirtyAt = Time.unscaledTime;
            _dirty = true;
        }

        private void LateUpdate()
        {
            if (!_dirty) return;
            if (Time.unscaledTime - _dirtyAt < RebuildSettleDelay) return;
            _dirty = false;
            Rebuild();
            VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged();
        }

        private void Rebuild()
        {
            for (int i = 0; i < _pipes.Count; i++)
                if (_pipes[i] != null) _pipes[i].neighbours.Clear();

            // Strip destroyed pipes before hashing so we don't walk tombstones.
            _pipes.RemoveAll(p => p == null);
            int n = _pipes.Count;
            if (n < 2) return;

            // ── SPATIAL HASH (cell size = 5m) ─────────────────────────────
            // O(N) neighbour discovery instead of the old O(N^2) double loop
            // that lagged hard once the player laid a hundred+ pipes. The
            // cell size is chosen so any valid pipe↔pipe link (≤ 5 lattice
            // cells in a cardinal line) lives inside this cell or one of the
            // immediate 3×3×3 neighbours.
            // Five-cell same-plane links fit inside this cell or its immediate
            // neighbours; coplanar validation below rejects off-plane candidates.
            const float CELL = 5f;
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

                        // Pipe↔pipe runs may span five cells on their shared plane.
                        // The strict coplanar predicate below rejects diagonal/off-plane joins.
                        float range = step * 5.1f;
                        if ((pa - pb).sqrMagnitude > range * range) continue;

                        Vector3 connectionDelta = VoxelEngine.Networks.PipeAdjacency.ConnectionDelta(a, b);
                        if (!VoxelEngine.Networks.PipeAdjacency.IsCoplanarPipeLinkDelta(connectionDelta, step, 5f, step * 0.18f)) continue;

                        if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(a, b)) continue;

                        if (!a.neighbours.Contains(b)) a.neighbours.Add(b);
                        if (!b.neighbours.Contains(a)) b.neighbours.Add(a);
                    }
                }
            }
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

            // BFS through pipe network — seed pipes near origin.
            var visited = new HashSet<GasPipe>();
            var queue = new Queue<GasPipe>();
            for (int i = 0; i < _pipes.Count; i++)
            {
                var startPipe = _pipes[i];
                if (startPipe == null) continue;
                if (seedFilter != null && !seedFilter(startPipe)) continue;
                if ((startPipe.transform.position - origin).sqrMagnitude > searchDist * searchDist) continue;
                if (visited.Add(startPipe)) queue.Enqueue(startPipe);
            }

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var block = cur.GetComponentInParent<GridBlock>();
                float pipeStep = block != null && block.Grid != null
                    ? GridSizeExt.CellSize(block.Grid.gridSize)
                    : GridStep(cur, cur);
                Transform frame = block != null && block.Grid != null ? block.Grid.transform : null;

                // Use a fixed ~2.75 m probe radius for tank detection — connectRadius
                // is kept for explicit links but tank discovery needs a stable face-
                // touch range that works even when connectRadius is tightened.
                var near = ProbeTankSphere(cur.transform.position, Mathf.Max(cur.connectRadius, 2.75f), type, forOutput);
                if (near != null) return near;
                var viaPipe = ProbeTankCardinal(cur.transform.position, frame, pipeStep, type, forOutput);
                if (viaPipe != null) return viaPipe;

                if (cur.neighbours == null) continue;
                for (int i = 0; i < cur.neighbours.Count; i++)
                {
                    var nb = cur.neighbours[i];
                    if (nb != null && visited.Add(nb)) queue.Enqueue(nb);
                }
            }
            return null;
        }

        private static readonly Collider[] s_tankProbe = new Collider[24];

        private static GasTank ProbeTankSphere(Vector3 centre, float radius, GasType type, bool forOutput)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(centre, radius, s_tankProbe, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hitCount; i++)
            {
                var col = s_tankProbe[i];
                var tank = col != null ? col.GetComponent<GasTank>() ?? col.GetComponentInParent<GasTank>() : null;
                if (tank == null) continue;
                if (forOutput && tank.allowOutput && tank.storedGasType == type && tank.storedAmount > 0)
                    return tank;
                if (!forOutput && tank.acceptInput && (tank.storedGasType == type || tank.storedGasType == GasType.None))
                    return tank;
            }
            return null;
        }

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
        private void OnDestroy() { if (Instance == this) Instance = null; _tankQueryCache.Clear(); }
    }
}
