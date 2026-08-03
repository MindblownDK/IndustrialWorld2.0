// Assets/Scripts/VoxelEngine/Fluids/FluidNetworkManager.cs
//
// Mirror of PowerNetworkManager but for water. Simpler — we don't enforce per-tick flow
// caps yet; pipes are connectivity only and tanks aggregate water across the network.
// Topology rebuild uses a 5 m spatial hash so large pipe farms stay frame-rate friendly.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Fluids
{
    public class FluidNetworkManager : MonoBehaviour
    {
        public static FluidNetworkManager Instance { get; private set; }

        private readonly HashSet<FluidNode> _all = new();
        private readonly List<FluidNetwork> _networks = new();
        private bool _topologyDirty;
        private float _topologyDirtyAt = -1f;
        private const float RebuildSettleDelay = 0.12f;

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("FluidNetworkManager");
            Instance = go.AddComponent<FluidNetworkManager>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void Register(FluidNode n)   { if (n != null && _all.Add(n)) MarkDirty(); }
        public void Unregister(FluidNode n)
        {
            if (n == null) return;
            if (_all.Remove(n))
            {
                n.network = null; n.neighbours?.Clear();
                MarkDirty();
                VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged();
            }
        }

        /// <summary>Force a topology rebuild — used by the wrench after blacklist edits.</summary>
        public void SetDirty() => MarkDirty();

        private void MarkDirty()
        {
            if (!_topologyDirty) _topologyDirtyAt = Time.unscaledTime;
            _topologyDirty = true;
        }

        private void Update()
        {
            if (!_topologyDirty) return;
            if (Time.unscaledTime - _topologyDirtyAt < RebuildSettleDelay) return;
            _topologyDirty = false;
            Rebuild();
            VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged();
        }

        private void Rebuild()
        {
            _networks.Clear();
            foreach (var n in _all)
            {
                if (n == null) continue;
                n.network = null;
                n.neighbours.Clear();
            }
            var snapshot = new List<FluidNode>(_all);
            snapshot.RemoveAll(n => n == null);

            // ── SPATIAL HASH (cell = 5m) — O(N) neighbour discovery ──────
            // Any two nodes that can legally link (≤ 5 lattice cells in a
            // cardinal line) sit in the same or adjacent hash cells.
            const float CELL = 5f;
            const float CELL_INV = 1f / CELL;
            var hash = new Dictionary<Vector3Int, List<FluidNode>>(snapshot.Count * 2);
            Vector3Int Cell(Vector3 p) => new Vector3Int(
                Mathf.FloorToInt(p.x * CELL_INV),
                Mathf.FloorToInt(p.y * CELL_INV),
                Mathf.FloorToInt(p.z * CELL_INV));
            foreach (var n in snapshot)
            {
                var k = Cell(n.transform.position);
                if (!hash.TryGetValue(k, out var bucket)) hash[k] = bucket = new List<FluidNode>(4);
                bucket.Add(n);
            }

            // Build a quick index so we can skip already-handled pairs.
            var index = new Dictionary<FluidNode, int>(snapshot.Count);
            for (int i = 0; i < snapshot.Count; i++) index[snapshot[i]] = i;

            foreach (var n in snapshot)
            {
                var c0 = Cell(n.transform.position);
                float rA = n.connectRadius;
                bool nIsPipe = n.Kind == FluidNodeKind.Pipe;
                var gbA = n.GetComponentInParent<GridBlock>();

                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!hash.TryGetValue(new Vector3Int(c0.x + dx, c0.y + dy, c0.z + dz), out var bucket)) continue;
                    foreach (var b in bucket)
                    {
                        if (b == n) continue;
                        if (index.TryGetValue(b, out int j) && j < index[n]) continue; // avoid duplicates

                        Vector3 pa = n.transform.position, pb = b.transform.position;
                        bool bIsPipe = b.Kind == FluidNodeKind.Pipe;
                        bool involvesPipe = nIsPipe || bIsPipe;

                        // GridStep now honours precision (0.5 m) detail pipes so a
                        // tank placed ONE FACE (0.5 m) from a pipe actually links.
                        float step = GridStep(n, b, gbA);

                        // Pipe↔endpoint connections use a tighter, predictable
                        // range: 5× the governing step capping at 2.75 m — this
                        // replaces the old "max(connectRadius, 5*step)" rule where
                        // the default 3 m connectRadius let pipes reach 3+ metres
                        // across to nodes nowhere near them (the "spam connection"
                        // lag/false-arm source).
                        float range;
                        if (involvesPipe && !(nIsPipe && bIsPipe))
                        {
                            // Endpoints (tanks/pumps) have a structural 2.5 m origin,
                            // so a pipe one face (0.5 m) from their shell is still
                            // ~1.5–2 m from the transform. Use 5× the governing step
                            // (capped at 2.75 m) which is guaranteed to reach one
                            // face-touch at the smallest grid size while staying
                            // tight enough to prevent cross-room links.
                            float detailStep = GridSizeExt.CellSize(GridSize.Small);
                            range = Mathf.Max(step * 5.1f, detailStep * 5.1f, 2.75f);
                            float authored = Mathf.Max(rA, b.connectRadius);
                            if (authored > range) range = Mathf.Min(authored, 3.5f);
                        }
                        else if (nIsPipe && bIsPipe)
                        {
                            range = step * 5.1f;
                        }
                        else
                        {
                            range = Mathf.Max(rA, b.connectRadius);
                        }

                        if ((pa - pb).sqrMagnitude > range * range) continue;

                        Vector3 connectionDelta = VoxelEngine.Networks.PipeAdjacency.ConnectionDelta(n, b);
                        bool ok = nIsPipe && bIsPipe
                            ? VoxelEngine.Networks.PipeAdjacency.IsCoplanarPipeLinkDelta(connectionDelta, step, 5f, step * 0.18f)
                            : involvesPipe
                                ? VoxelEngine.Networks.PipeAdjacency.IsCardinalLinkDelta(connectionDelta, step, 5f, step * 0.45f)
                                : VoxelEngine.Networks.PipeAdjacency.IsAxisAlignedWithinDelta(connectionDelta, step, 2.5f, step * 0.45f);
                        if (!ok) continue;

                        if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(n, b)) continue;

                        if (!n.neighbours.Contains(b)) n.neighbours.Add(b);
                        if (!b.neighbours.Contains(n)) b.neighbours.Add(n);
                    }
                }
            }
            foreach (var seed in snapshot)
            {
                if (seed.network != null) continue;
                var net = new FluidNetwork(); _networks.Add(net);
                var q = new Queue<FluidNode>();
                q.Enqueue(seed); seed.network = net;
                while (q.Count > 0)
                {
                    var n = q.Dequeue();
                    net.nodes.Add(n);
                    foreach (var nb in n.neighbours)
                        if (nb.network == null) { nb.network = net; q.Enqueue(nb); }
                }
                net.Recompute();
            }
        }

        private static float GridStep(FluidNode a, FluidNode b, GridBlock gbA = null)
        {
            var blockA = gbA ?? (a != null ? a.GetComponentInParent<GridBlock>() : null);
            var blockB = b != null ? b.GetComponentInParent<GridBlock>() : null;
            if (blockA != null && blockB != null && blockA.Grid != null && blockA.Grid == blockB.Grid)
            {
                // Pipe↔pipe links always use the Detail lattice step. Old/legacy pipe
                // prefabs can arrive on a grid without IsPrecisionAttachment set; using
                // structural size then allowed one-left + one-up diagonal links through.
                if (a != null && b != null && a.Kind == FluidNodeKind.Pipe && b.Kind == FluidNodeKind.Pipe)
                    return GridSizeExt.CellSize(GridSize.Small);

                bool aSmall = blockA.IsPrecisionAttachment;
                bool bSmall = blockB.IsPrecisionAttachment;
                float small = GridSizeExt.CellSize(GridSize.Small);
                if (aSmall && bSmall) return small;
                if (aSmall != bSmall) return small;
                return (blockA.EffectiveCellSize + blockB.EffectiveCellSize) * 0.5f;
            }
            return VoxelEngine.Networks.PipeAdjacency.DefaultGridSize;
        }

        public IReadOnlyList<FluidNetwork> Networks => _networks;
    }
}

