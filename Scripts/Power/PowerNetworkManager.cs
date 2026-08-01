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
        private float _topologyDirtyAt = -1f;
        private float _tickTimer;

        /// <summary>
        /// Per-node neighbour-set signatures captured BEFORE each rebuild, so the
        /// "neighbours changed" event only fires for nodes whose connections ACTUALLY
        /// changed. Without this gate, placing ONE cable re-meshed the visuals of
        /// EVERY cable in the world — an O(N²) GameObject churn across a cabling
        /// session (20 cables → hundreds of destroyed/respawned mesh children per
        /// placement) which is exactly the placement-time lag spike players felt.
        /// </summary>
        private readonly Dictionary<PowerNode, int> _neighbourSignatures = new();

        /// <summary>
        /// Coalesce placement bursts: while the player holds the place button, every
        /// new cable marks the topology dirty — batch those into one rebuild after a
        /// short settle delay instead of a full rebuild per frame.
        /// </summary>
        private const float TopologyRebuildDelay = 0.12f;

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
            if (_allNodes.Add(node)) MarkTopologyDirty();
        }

        public void Unregister(PowerNode node)
        {
            if (_allNodes.Remove(node)) MarkTopologyDirty();
            node.network = null;
            node.neighbours?.Clear();
        }

        /// <summary>Force a topology rebuild on the next Update tick — used
        /// by the wrench after it edits the WrenchBlacklist so the visual
        /// change is reflected instantly without waiting for register churn.</summary>
        public void SetDirty() => MarkTopologyDirty();

        private void MarkTopologyDirty()
        {
            if (!_topologyDirty) _topologyDirtyAt = Time.unscaledTime;
            _topologyDirty = true;
        }

        private void Update()
        {
            if (_topologyDirty && Time.unscaledTime - _topologyDirtyAt >= TopologyRebuildDelay)
            {
                // Clear BEFORE rebuilding: anything that dirties the topology DURING
                // the rebuild (a script reacting to onNeighboursChanged) must schedule
                // a follow-up pass, not have its flag swallowed by the post-clear.
                _topologyDirty = false;
                RebuildTopology();
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
            // Capture each node's CURRENT neighbour-set signature before wiping, so
            // after the rebuild we can tell exactly which nodes actually changed.
            _neighbourSignatures.Clear();
            foreach (var n in _allNodes)
                if (n != null) _neighbourSignatures[n] = NeighbourSignature(n);

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
                        if ((n.transform.position - b.transform.position).sqrMagnitude > r * r) continue;

                        // New: ask BOTH ends whether the link is legal.
                        // This enforces grid-alignment for cables AND line-of-sight checks,
                        // so cables never connect diagonally or through solid blocks.
                        if (!n.CanLinkTo(b)) continue;
                        if (!b.CanLinkTo(n)) continue;

                        // Wrench blacklist — the player explicitly broke this link.
                        // Skip it on every subsequent topology rebuild until a wrench
                        // re-bonds them (or one of them is removed/replaced).
                        if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(n, b)) continue;

                        // Anti-redundancy: if both are cables AND they're on the same cardinal axis
                        // with an intermediate node between them, only keep the closer link to
                        // prevent multiple arms from chain cables all reaching the same block.
                        // A cable chain A->B->C should only have each pair directly connected, not
                        // C also connecting directly to A's attached machine.
                        if (n.Kind == PowerNodeKind.Cable && b.Kind == PowerNodeKind.Cable &&
                            AreOnSameAxisAndInBetween(n.transform.position, b.transform.position))
                        {
                            // Check if there's an even closer cable between them that already
                            // has a direct path to b. If so, skip this link.
                            if (HasCloserCableOnAxis(n, b, snapshot)) continue;
                        }

                        // Avoid duplicate add (we'll see each pair twice — once from each side).
                        if (!n.neighbours.Contains(b)) 
                        {
                            if (n.neighbours.Count >= n.MaxAutoConnections) continue;
                            if (b.neighbours.Count >= b.MaxAutoConnections) continue;
                            n.neighbours.Add(b);
                            if (!b.neighbours.Contains(n) && b.neighbours.Count < b.MaxAutoConnections)
                                b.neighbours.Add(n);
                            
                            // Record connection faces for cables connecting to machines
                            RecordConnectionFace(n, b);
                            RecordConnectionFace(b, n);
                        }
                    }
                }
            }

            // Manual wire links are intentional long-range topology edges. Add them
            // after automatic proximity discovery so compact one-wire connectors can
            // still auto-tap a nearby generator/consumer AND keep their one manual
            // wire span to another station.
            foreach (var n in snapshot)
            {
                if (n.manualLinks == null) continue;
                foreach (var linked in n.manualLinks)
                {
                    if (linked == null || linked == n || !snapshot.Contains(linked)) continue;
                    if (!n.neighbours.Contains(linked)) n.neighbours.Add(linked);
                    if (!linked.neighbours.Contains(n)) linked.neighbours.Add(n);
                }
            }

            // === Secondary pass: prune cable→machine links that are shadowed by a closer cable ===
            // If a machine is connected to multiple cables on the same axis, only the closest
            // cable should keep the link. Others should drop it to avoid redundant visual arms.
            PruneShadowedCableMachineLinks(snapshot);

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

            // Notify only nodes whose connection set ACTUALLY changed so visuals
            // (cable arms, indicator LEDs, etc.) rebuild. A brand-new node has no
            // recorded signature and always gets its first notify; untouched parts
            // of the factory keep their meshes — no more factory-wide re-mesh
            // storm every time a cable is placed.
            foreach (var n in snapshot)
            {
                if (n == null) continue;
                int sig = NeighbourSignature(n);
                if (!_neighbourSignatures.TryGetValue(n, out int oldSig) || oldSig != sig)
                    n.onNeighboursChanged?.Invoke();
            }
        }

        /// <summary>Hash of a node's neighbour set (order + members) for change detection.</summary>
        private static int NeighbourSignature(PowerNode n)
        {
            unchecked
            {
                int h = 17;
                var list = n.neighbours;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                        if (list[i] != null) h = h * 31 + list[i].GetEntityId().GetHashCode();
                    h = h * 31 + list.Count;
                }
                return h;
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
                            // Per-battery flow telemetry for the UI (W in/out this tick).
                            b.lastChargeInW = 0f;
                            b.lastDischargeOutW = 0f;
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
                            b.lastDischargeOutW = wh > 0f ? wh * 3600f / dt : 0f;
                            net_fromBattery -= pull;
                        }
                        if (net_toBattery > 0)
                        {
                            float push = Mathf.Min(b.ioRate, net_toBattery);
                            float wh   = push * dt / 3600f;
                            wh = Mathf.Min(wh, b.capacityWattHours - b.charge);
                            b.charge += wh;
                            b.lastChargeInW = wh > 0f ? wh * 3600f / dt : 0f;
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

        // ============================================================
        //              AXIS-CONFLICT HELPERS (anti-redundancy)
        // ============================================================

        /// <summary>
        /// Returns true if a and b are on the same cardinal axis (±X, ±Y, or ±Z),
        /// exactly one grid step apart, and there is an intermediate node between them.
        /// </summary>
        private bool AreOnSameAxisAndInBetween(Vector3 a, Vector3 b)
        {
            Vector3 delta = b - a;
            float dx = Mathf.Abs(delta.x), dy = Mathf.Abs(delta.y), dz = Mathf.Abs(delta.z);
            float gs = 1f; // gridSize is always 1m for cables
            const float TOL = 0.15f;

            // Must be exactly 1 step along one axis, others must be near-zero.
            int axisCount = 0;
            if (Mathf.Abs(dx - gs) < TOL) axisCount++;
            else if (dx > TOL) return false;
            if (Mathf.Abs(dy - gs) < TOL) axisCount++;
            else if (dy > TOL) return false;
            if (Mathf.Abs(dz - gs) < TOL) axisCount++;
            else if (dz > TOL) return false;

            return axisCount == 1; // exactly one axis has distance ~= 1m
        }

        /// <summary>
        /// Checks if there's a third cable (c) that is:
        /// - On the same axis between a and b
        /// - Closer to b than a is
        /// - Already connected to b
        /// If so, a's connection to b is "shadowed" and should be skipped.
        /// </summary>
        private bool HasCloserCableOnAxis(PowerNode a, PowerNode b, List<PowerNode> snapshot)
        {
            Vector3 posA = a.transform.position;
            Vector3 posB = b.transform.position;
            Vector3 delta = posB - posA;

            // Determine which axis we're on
            Vector3 axisDir;
            if (Mathf.Abs(delta.x) > 0.5f) axisDir = new Vector3(Mathf.Sign(delta.x), 0, 0);
            else if (Mathf.Abs(delta.y) > 0.5f) axisDir = new Vector3(0, Mathf.Sign(delta.y), 0);
            else axisDir = new Vector3(0, 0, Mathf.Sign(delta.z));

            // Find cables between A and B on this axis
            foreach (var candidate in snapshot)
            {
                if (candidate == a || candidate == b) continue;
                if (candidate.Kind != PowerNodeKind.Cable) continue;

                Vector3 cPos = candidate.transform.position;
                Vector3 toC = cPos - posA;

                // Is C between A and B on this axis?
                float dot = Vector3.Dot(toC, axisDir);
                if (dot <= 0 || dot >= Vector3.Dot(delta, axisDir)) continue; // not between

                // Is C's direction on the same axis?
                Vector3 cDelta = posB - cPos;
                Vector3 cAxisDir;
                if (Mathf.Abs(cDelta.x) > 0.5f) cAxisDir = new Vector3(Mathf.Sign(cDelta.x), 0, 0);
                else if (Mathf.Abs(cDelta.y) > 0.5f) cAxisDir = new Vector3(0, Mathf.Sign(cDelta.y), 0);
                else cAxisDir = new Vector3(0, 0, Mathf.Sign(cDelta.z));

                if (cAxisDir != axisDir) continue; // not on same axis direction

                // C is between A and B, closer to B. If C already connects to B, skip A→B.
                if (candidate.neighbours.Contains(b)) return true;
            }

            return false;
        }

        /// <summary>
        /// After all connections are built, prune redundant cable→machine links.
        /// If multiple cables on the same axis connect to the same non-cable node,
        /// only the closest cable keeps its connection. Others drop it.
        /// </summary>
        private void PruneShadowedCableMachineLinks(List<PowerNode> snapshot)
        {
            // Group connections by target node
            var targetToCables = new Dictionary<PowerNode, List<PowerNode>>();

            // Find all cable→nonCable connections
            foreach (var cable in snapshot)
            {
                if (cable.Kind != PowerNodeKind.Cable) continue;
                if (cable.neighbours == null) continue;

                foreach (var target in cable.neighbours)
                {
                    if (target == null) continue;
                    if (target.Kind == PowerNodeKind.Cable) continue; // only care about machines

                    if (!targetToCables.TryGetValue(target, out var list))
                        targetToCables[target] = list = new List<PowerNode>();
                    list.Add(cable);
                }
            }

            // For each machine with multiple cables connecting to it
            foreach (var kvp in targetToCables)
            {
                if (kvp.Value.Count < 2) continue; // only one cable, no conflict

                var machine = kvp.Key;
                var cablesOnAxis = new Dictionary<Vector3, List<(PowerNode cable, float dist)>>();

                foreach (var cable in kvp.Value)
                {
                    Vector3 delta = machine.transform.position - cable.transform.position;
                    Vector3 axisDir = NearestAxis(delta);
                    float dist = delta.magnitude;

                    if (!cablesOnAxis.TryGetValue(axisDir, out var list))
                        cablesOnAxis[axisDir] = list = new List<(PowerNode, float)>();
                    list.Add((cable, dist));
                }

                // For each axis, keep only the closest cable, prune others
                foreach (var axisKvp in cablesOnAxis)
                {
                    if (axisKvp.Value.Count < 2) continue;

                    // Sort by distance (closest first)
                    axisKvp.Value.Sort((a, b) => a.dist.CompareTo(b.dist));

                    // Keep first (closest), prune rest
                    for (int i = 1; i < axisKvp.Value.Count; i++)
                    {
                        var farCable = axisKvp.Value[i].cable;
                        farCable.neighbours.Remove(machine);
                        machine.neighbours.Remove(farCable);
                    }
                }
            }
        }

        private Vector3 NearestAxis(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0, 0);
            if (ay >= ax && ay >= az) return new Vector3(0, Mathf.Sign(v.y), 0);
            return new Vector3(0, 0, Mathf.Sign(v.z));
        }

        /// <summary>
        /// When a cable connects to a machine, record which face of the machine it connects to.
        /// This is used for visual arm placement and connection validation.
        /// </summary>
        private void RecordConnectionFace(PowerNode cable, PowerNode machine)
        {
            if (cable.Kind != PowerNodeKind.Cable) return;

            var portConfig = machine.GetComponent<Transport.PortConfig>();
            if (portConfig == null) return;

            var match = portConfig.GetMatchingFace(cable.transform.position, Transport.PortDirection.Input);
            if (!match.HasValue)
                match = portConfig.GetMatchingFace(cable.transform.position, Transport.PortDirection.Output);

            if (!match.HasValue) return;

            // Record on the cable which face it uses
            if (cable is PowerCable powerCable)
            {
                powerCable.RecordConnectionFace(machine, match.Value.face);
            }
        }
    }
}
