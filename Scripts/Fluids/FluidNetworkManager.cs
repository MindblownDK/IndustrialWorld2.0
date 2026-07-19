// Assets/Scripts/VoxelEngine/Fluids/FluidNetworkManager.cs
//
// Mirror of PowerNetworkManager but for water. Simpler — we don't enforce per-tick flow
// caps yet; pipes are connectivity only and tanks aggregate water across the network.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Fluids
{
    public class FluidNetworkManager : MonoBehaviour
    {
        public static FluidNetworkManager Instance { get; private set; }

        private readonly HashSet<FluidNode> _all = new();
        private readonly List<FluidNetwork> _networks = new();
        private bool _topologyDirty;

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

        public void Register(FluidNode n)   { if (_all.Add(n))    _topologyDirty = true; }
        public void Unregister(FluidNode n) { if (_all.Remove(n)) _topologyDirty = true; n.network = null; n.neighbours?.Clear(); }

        /// <summary>Force a topology rebuild — used by the wrench after blacklist edits.</summary>
        public void SetDirty() => _topologyDirty = true;

        private void Update()
        {
            if (_topologyDirty) { Rebuild(); _topologyDirty = false; }
        }

        private void Rebuild()
        {
            _networks.Clear();
            foreach (var n in _all) { n.network = null; n.neighbours.Clear(); }
            var snapshot = new List<FluidNode>(_all);
            snapshot.RemoveAll(n => n == null);

            // O(N) spatial hash. Five metres keeps every valid five-cell world
            // pipe span inside this cell or one of its immediate neighbours.
            const float CELL = 5f;
            var hash = new Dictionary<Vector3Int, List<FluidNode>>();
            Vector3Int Cell(Vector3 p) => new Vector3Int(
                Mathf.FloorToInt(p.x / CELL), Mathf.FloorToInt(p.y / CELL), Mathf.FloorToInt(p.z / CELL));
            foreach (var n in snapshot)
            {
                var k = Cell(n.transform.position);
                if (!hash.TryGetValue(k, out var bucket)) hash[k] = bucket = new List<FluidNode>(4);
                bucket.Add(n);
            }
            foreach (var n in snapshot)
            {
                var c0 = Cell(n.transform.position);
                float rA = n.connectRadius;
                bool nIsPipe = n.Kind == FluidNodeKind.Pipe;
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!hash.TryGetValue(new Vector3Int(c0.x + dx, c0.y + dy, c0.z + dz), out var bucket)) continue;
                    foreach (var b in bucket)
                    {
                        if (b == n) continue;
                        Vector3 pa = n.transform.position, pb = b.transform.position;

                        // Pipe↔pipe links may bridge five cardinal cells. Pipe↔tank /
                        // pipe↔pump links retain the shorter endpoint rule.
                        bool bIsPipe = b.Kind == FluidNodeKind.Pipe;
                        float step = GridStep(n, b);
                        float range = nIsPipe && bIsPipe
                            ? Mathf.Max(Mathf.Max(rA, b.connectRadius), step * 5f)
                            : Mathf.Max(rA, b.connectRadius);
                        if ((pa - pb).sqrMagnitude > range * range) continue;
                        bool ok = (nIsPipe && bIsPipe)
                            ? VoxelEngine.Networks.PipeAdjacency.IsCardinalLink(pa, pb, step, 5f, step * 0.35f)
                            : VoxelEngine.Networks.PipeAdjacency.IsAxisAlignedWithin(pa, pb, step, 2.5f, step * 0.35f);
                        if (!ok) continue;

                        // Wrench blacklist — explicit player disconnect persists.
                        if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(n, b)) continue;

                        if (!n.neighbours.Contains(b)) n.neighbours.Add(b);
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

        private static float GridStep(FluidNode a, FluidNode b)
        {
            var blockA = a != null ? a.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>() : null;
            var blockB = b != null ? b.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>() : null;
            if (blockA != null && blockB != null && blockA.Grid != null && blockA.Grid == blockB.Grid)
                return (blockA.EffectiveCellSize + blockB.EffectiveCellSize) * 0.5f;
            return VoxelEngine.Networks.PipeAdjacency.DefaultGridSize;
        }

        public IReadOnlyList<FluidNetwork> Networks => _networks;
    }
}
