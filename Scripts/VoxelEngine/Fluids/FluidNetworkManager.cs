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

            // O(N) spatial hash.
            const float CELL = 2f;
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
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!hash.TryGetValue(new Vector3Int(c0.x + dx, c0.y + dy, c0.z + dz), out var bucket)) continue;
                    foreach (var b in bucket)
                    {
                        if (b == n) continue;
                        float r = Mathf.Max(rA, b.connectRadius);
                        if ((n.transform.position - b.transform.position).sqrMagnitude <= r * r)
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

        public IReadOnlyList<FluidNetwork> Networks => _networks;
    }
}
