// Assets/Scripts/VoxelEngine/Power/PowerNetworkManager.cs
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Power
{
    /// <summary>
    /// Central tick + topology manager. One per scene (auto-created on first registration).
    ///
    /// Topology rebuild is triggered lazily on the next Update after any add/remove.
    /// Tick logic each second:
    ///   1) For every network: sum potential generation, sum requested consumption,
    ///      cap each by network bottleneck, apply battery balancing.
    ///   2) Set IsPowered on every consumer based on whether their share was met.
    /// </summary>
    public class PowerNetworkManager : MonoBehaviour
    {
        public static PowerNetworkManager Instance { get; private set; }

        [Tooltip("How often (seconds) to recompute the power balance.")]
        public float tickInterval = 0.25f;

        private readonly HashSet<PowerNode>     _allNodes = new();
        private readonly List<PowerNetwork>     _networks = new();
        private bool _topologyDirty;
        private float _tickTimer;

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("PowerNetworkManager");
            Instance = go.AddComponent<PowerNetworkManager>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void Register(PowerNode node)
        {
            if (_allNodes.Add(node)) _topologyDirty = true;
        }

        public void Unregister(PowerNode node)
        {
            if (_allNodes.Remove(node)) _topologyDirty = true;
            node.network = null;
            node.neighbours?.Clear();
        }

        private void Update()
        {
            if (_topologyDirty)
            {
                RebuildTopology();
                _topologyDirty = false;
            }

            _tickTimer += Time.deltaTime;
            if (_tickTimer < tickInterval) return;
            _tickTimer = 0f;
            TickNetworks();
        }

        // ============================================================
        //                       TOPOLOGY
        // ============================================================
        private void RebuildTopology()
        {
            // Reset.
            _networks.Clear();
            foreach (var n in _allNodes) { n.network = null; n.neighbours.Clear(); }

            // Snapshot for stable iteration (a Unity Object can be destroyed mid-frame).
            var snapshot = new List<PowerNode>(_allNodes);
            snapshot.RemoveAll(n => n == null);

            // Build neighbour lists using a spatial hash (cell size = 2m) — O(N + K) instead of O(N^2).
            const float CELL = 4f;
            var hash = new Dictionary<Vector3Int, List<PowerNode>>();
            Vector3Int Cell(Vector3 p) => new Vector3Int(
                Mathf.FloorToInt(p.x / CELL),
                Mathf.FloorToInt(p.y / CELL),
                Mathf.FloorToInt(p.z / CELL));
            foreach (var n in snapshot)
            {
                var key = Cell(n.transform.position);
                if (!hash.TryGetValue(key, out var bucket)) hash[key] = bucket = new List<PowerNode>(4);
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
                        {
                            // Avoid duplicate add (we'll see each pair twice — once from each side).
                            if (!n.neighbours.Contains(b)) n.neighbours.Add(b);
                        }
                    }
                }
            }

            // BFS to assign each node to a network.
            foreach (var seed in snapshot)
            {
                if (seed.network != null) continue;
                var net = new PowerNetwork();
                _networks.Add(net);
                var queue = new Queue<PowerNode>();
                queue.Enqueue(seed); seed.network = net;
                while (queue.Count > 0)
                {
                    var n = queue.Dequeue();
                    net.nodes.Add(n);
                    foreach (var nb in n.neighbours)
                    {
                        if (nb.network == null) { nb.network = net; queue.Enqueue(nb); }
                    }
                }
                net.Recompute();
            }
        }

        // ============================================================
        //                        TICK
        // ============================================================
        private void TickNetworks()
        {
            float dt = tickInterval;
            foreach (var net in _networks)
            {
                if (net.nodes.Count == 0) continue;

                float supply = 0f;       // potential watts/s available
                float demand = 0f;       // requested watts/s
                float batteryCap   = 0f; // max can pull from batteries this tick (W)
                float batteryStore = 0f; // max can push into batteries this tick (W)

                foreach (var n in net.nodes)
                {
                    switch (n)
                    {
                        case PowerGenerator g when g.isOn: supply += g.wattsPerSecond; break;
                        case PowerConsumer  c:             demand += c.wattsPerSecond; break;
                        case PowerBattery   b:
                            // Battery can supply up to ioRate from current charge.
                            batteryCap   += Mathf.Min(b.ioRate, b.charge / Mathf.Max(0.0001f, dt) * 3600f); // W from Wh
                            batteryStore += Mathf.Min(b.ioRate, (b.capacityWattHours - b.charge) / Mathf.Max(0.0001f, dt) * 3600f);
                            break;
                    }
                }

                // Apply network bottleneck — generators can't push more than the slowest cable carries.
                float maxFlow = net.bottleneckWatts > 0 ? net.bottleneckWatts : float.PositiveInfinity;
                float supplyEff = Mathf.Min(supply, maxFlow);

                // First fill demand from generators, then top up from batteries if short.
                float deficit = Mathf.Max(0, demand - supplyEff);
                float fromBattery = Mathf.Min(deficit, Mathf.Min(batteryCap, maxFlow - supplyEff));
                float served = supplyEff + fromBattery;
                float ratio = demand > 0.0001f ? Mathf.Clamp01(served / demand) : 1f;

                // Excess generation charges batteries.
                float excess = Mathf.Max(0, supplyEff - demand);
                float toBattery = Mathf.Min(excess, Mathf.Min(batteryStore, maxFlow));

                // Apply battery state changes (Wh = W * h, dt is in seconds → /3600).
                if (fromBattery > 0 || toBattery > 0)
                {
                    // Distribute proportionally across batteries.
                    float net_fromBattery = fromBattery; // pull
                    float net_toBattery   = toBattery;   // push
                    foreach (var n in net.nodes)
                    {
                        if (!(n is PowerBattery b)) continue;
                        if (net_fromBattery > 0)
                        {
                            float pull = Mathf.Min(b.ioRate, net_fromBattery);
                            float wh   = pull * dt / 3600f;
                            wh = Mathf.Min(wh, b.charge);
                            b.charge -= wh;
                            net_fromBattery -= pull;
                        }
                        if (net_toBattery > 0)
                        {
                            float push = Mathf.Min(b.ioRate, net_toBattery);
                            float wh   = push * dt / 3600f;
                            wh = Mathf.Min(wh, b.capacityWattHours - b.charge);
                            b.charge += wh;
                            net_toBattery -= push;
                        }
                        if (net_fromBattery <= 0 && net_toBattery <= 0) break;
                    }
                }

                // Mark consumers powered/unpowered. We use proportional fairness — if served=80%
                // of demand, every consumer is "powered" but at 80% rate (we expose ratio for
                // future use; for now boolean is enough for IsPowered to gate machines).
                bool everyoneOk = ratio > 0.999f;
                foreach (var n in net.nodes)
                    if (n is PowerConsumer c) c.IsPowered = everyoneOk;

                net.producedThisTick = supplyEff;
                net.consumedThisTick = demand * ratio;
                net.storedThisTick   = toBattery - fromBattery;
            }
        }

        // For UI / debugging.
        public IReadOnlyList<PowerNetwork> Networks => _networks;
    }
}
