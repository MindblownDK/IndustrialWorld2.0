using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Networks;

namespace VoxelEngine.Transport
{
    /// <summary>
    /// Item-transport pipe segment. Connects to neighbouring ItemPipes, producers
    /// (QuarryBlock, Furnace outputs) and consumers (Chests, Furnace inputs)
    /// within <see cref="connectRadius"/>.
    ///
    /// Each pipe has a small internal buffer. Every <see cref="tickInterval"/>
    /// seconds the pipe pushes its buffer toward the nearest valid sink.
    ///
    /// VISUAL: hands its live neighbour list to a <see cref="PipeVisualBuilder"/>
    /// so the pipe presents the same chunky core+arms style as Power / Data
    /// cables. The neighbour list ALSO includes adjacent Chests so the pipe
    /// visually grows a connecting arm into the container. Glass variant exposes
    /// an inner core and animates flowing items via <see cref="ItemFlowVisualizer"/>.
    /// </summary>
    public class ItemPipe : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────
        [Tooltip("Distance at which this pipe auto-connects to neighbours.")]
        public float connectRadius = 3.0f;

        [Tooltip("Max items this single pipe segment can hold in transit.")]
        public int bufferSize = 4;

        [Tooltip("Seconds between item-push ticks.")]
        public float tickInterval = 0.5f;

        [Header("Visual")]
        [Tooltip("Render as a translucent glass pipe with the carried items visible inside.")]
        public bool isGlass = false;

        [Tooltip("Distance at which an arm is drawn toward an adjacent chest / machine endpoint.")]
        public float endpointConnectRadius = 1.6f;

        // ── Runtime ────────────────────────────────────────────────────────
        [System.NonSerialized] public List<ItemPipe> neighbours = new();
        private ItemContainer _buffer;
        private float _tickTimer;

        // ── Visual integration ─────────────────────────────────────────────
        private PipeVisualBuilder _visuals;
        private ItemFlowVisualizer _flow;
        private readonly List<Vector3> _neighbourPosBuf = new(12);

        // Per-tick accumulator of directions the pipe is actively feeding, plus
        // the item being carried — handed to the flow visualizer as a continuous
        // stream so the animation is always visible while items move.
        private readonly List<Vector3> _flowDirs = new(6);
        private ItemDefinition _flowItem;

        // Cache of nearby container endpoints (chests/machines) refreshed each
        // tick — used both to draw connecting arms AND to drive flow direction.
        private readonly List<Vector3> _endpointPositions = new(6);
        private float _endpointScanTimer;

        /// <summary>Read-only view of the items currently in transit inside this pipe.</summary>
        public IReadOnlyList<ItemStack> Buffer
        {
            get { EnsureBuffer(); return _buffer.Slots; }
        }

        // ── Lifecycle ──────────────────────────────────────────────────────
        private void Awake()
        {
            EnsureBuffer();

            _visuals = GetComponent<PipeVisualBuilder>();
            if (_visuals == null) _visuals = gameObject.AddComponent<PipeVisualBuilder>();
            _visuals.neighbourPositionsProvider = GetNeighbourPositions;
            _visuals.isGlass = isGlass;
            // Item pipes use the BuildCraft / Thermal-Expansion "sleeve" style —
            // chunky terminal end-blocks at every junction, hazard-band sleeve
            // along the run.
            _visuals.style = VoxelEngine.Networks.PipeStyle.Sleeve;

            // Glass pipes animate the carried stream. Only spawn the visualizer
            // for glass — solid pipes hide the core so pellets would be invisible.
            // Resolved lazily in EnsureFlow() so it works even if `isGlass` is
            // assigned after Awake (e.g. by a placement script).
            EnsureFlow();
        }

        /// <summary>Create/find the flow visualizer once the pipe is known to be glass.</summary>
        private void EnsureFlow()
        {
            if (!isGlass || _flow != null) return;
            _flow = GetComponent<ItemFlowVisualizer>();
            if (_flow == null) _flow = gameObject.AddComponent<ItemFlowVisualizer>();
        }

        private void OnEnable()  { ItemPipeNetwork.EnsureInstance(); ItemPipeNetwork.Instance?.Register(this); }
        private void OnDisable() => ItemPipeNetwork.Instance?.Unregister(this);

        private void Update()
        {
            // Keep the endpoint (chest/machine) cache fresh so arms appear/vanish
            // when the player places or removes a container next to the pipe.
            _endpointScanTimer += Time.deltaTime;
            if (_endpointScanTimer >= 0.5f)
            {
                _endpointScanTimer = 0f;
                ScanEndpoints();
            }

            _tickTimer += Time.deltaTime;
            if (_tickTimer < tickInterval) return;
            _tickTimer -= tickInterval;
            PushBufferDownstream();
        }

        // ── Public API (called by producers like Quarry) ───────────────────

        /// <summary>
        /// How many units of a given item this pipe can currently accept.
        /// </summary>
        public int GetInputCapacity(ItemDefinition item)
        {
            EnsureBuffer();
            if (item == null) return 0;

            int free = 0;
            for (int i = 0; i < _buffer.Size; i++)
            {
                var s = _buffer.GetSlot(i);
                if (s.IsEmpty)
                    free += item.maxStack;
                else if (s.item == item && item.IsStackable)
                    free += item.maxStack - s.count;
            }
            return free;
        }

        /// <summary>
        /// Try to insert up to <paramref name="count"/> items into this pipe's buffer.
        /// Returns the number of items actually accepted.
        /// </summary>
        public int TryInsert(ItemDefinition item, int count)
        {
            EnsureBuffer();
            if (item == null || count <= 0) return 0;

            int before = _buffer.CountOf(item);
            _buffer.Insert(new ItemStack(item, count));
            int after = _buffer.CountOf(item);
            return after - before;
        }

        // ── Internals ──────────────────────────────────────────────────────

        private void EnsureBuffer()
        {
            if (_buffer == null)
                _buffer = new ItemContainer("PipeBuffer", bufferSize);
            else
                _buffer.Resize(bufferSize);
        }

        /// <summary>
        /// Neighbour positions the visual builder uses to grow connecting arms:
        /// every linked pipe PLUS every adjacent container endpoint (chest /
        /// machine). This makes the pipe visibly plug into the chest.
        /// </summary>
        private List<Vector3> GetNeighbourPositions()
        {
            _neighbourPosBuf.Clear();
            foreach (var n in neighbours)
                if (n != null) _neighbourPosBuf.Add(n.transform.position);
            foreach (var e in _endpointPositions)
                _neighbourPosBuf.Add(e);
            return _neighbourPosBuf;
        }

        /// <summary>
        /// Locate container endpoints (chests &amp; machines with an item interface)
        /// sitting directly adjacent so the visual builder can grow an arm toward
        /// them. We snap each hit to the nearest cardinal cell centre so the arm
        /// lines up with the pipe grid.
        /// </summary>
        private void ScanEndpoints()
        {
            _endpointPositions.Clear();
            var hits = Physics.OverlapSphere(transform.position, endpointConnectRadius);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                if (col.GetComponentInParent<ItemPipe>() != null) continue; // pipes already handled

                // CHESTS: only connect when the face pointing at us is an ENABLED
                // Input/Output port. A disabled / None face means NO connection —
                // the pipe must not draw an arm or exchange items there.
                var chest = col.GetComponentInParent<Chest>();
                if (chest != null)
                {
                    if (!chest.IsFaceConnectable(transform.position)) continue;
                }
                else
                {
                    // Other endpoints (machines) connect if they expose an item
                    // interface or container.
                    bool isEndpoint = col.GetComponentInParent<IInventoryInterface>() != null
                                   || col.GetComponentInParent<IItemContainer>() != null;
                    if (!isEndpoint) continue;
                }

                Vector3 to = col.bounds.center - transform.position;
                Vector3 dir = NearestCardinal(to);
                if (dir == Vector3.zero) continue;
                Vector3 endpoint = transform.position + dir; // one cell along the face
                if (!_endpointPositions.Contains(endpoint))
                    _endpointPositions.Add(endpoint);
            }
        }

        /// <summary>
        /// Force an immediate endpoint rescan + visual rebuild. Called by the
        /// network when a port config changes so connections update instantly
        /// instead of waiting for the next 0.5 s poll.
        /// </summary>
        public void ForceEndpointRescan()
        {
            ScanEndpoints();
            if (_visuals != null) _visuals.ForceRebuild();
        }

        private static Vector3 NearestCardinal(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0, 0);
            if (ay >= ax && ay >= az) return new Vector3(0, Mathf.Sign(v.y), 0);
            if (az > 0.0001f)          return new Vector3(0, 0, Mathf.Sign(v.z));
            return Vector3.zero;
        }

        /// <summary>
        /// Each tick, try to push items from our buffer into valid sinks
        /// (chests, furnaces, etc.) or forward along neighbour pipes.
        /// </summary>
        private void PushBufferDownstream()
        {
            EnsureBuffer();

            // Reset this tick's flow accumulator. Any successful move appends the
            // outbound direction; at the end we hand them to the visualizer as a
            // single continuous stream so the animation reads clearly.
            _flowDirs.Clear();
            _flowItem = null;

            for (int i = 0; i < _buffer.Size; i++)
            {
                var stack = _buffer.GetSlot(i);
                if (stack.IsEmpty) continue;

                // 1) Try to push into any sink (chest/machine).
                TryPushToSinks(stack);
                if (stack.IsEmpty || stack.count <= 0)
                {
                    _buffer.SetSlot(i, new ItemStack());
                    continue;
                }

                // 2) Forward to the first neighbour pipe that has capacity.
                foreach (var nb in neighbours)
                {
                    if (nb == null) continue;
                    int cap = nb.GetInputCapacity(stack.item);
                    if (cap <= 0) continue;

                    int send     = Mathf.Min(cap, stack.count);
                    int accepted = nb.TryInsert(stack.item, send);
                    if (accepted > 0)
                    {
                        Vector3 toDir = (nb.transform.position - transform.position).normalized;
                        RecordFlow(stack.item, toDir);
                        // Feed the receiving pipe an INBOUND stream from our side
                        // so the animation reads as continuous across segments.
                        nb.ReceiveFlow(stack.item, -toDir);
                        stack.count -= accepted;
                    }

                    if (stack.count <= 0)
                    {
                        _buffer.SetSlot(i, new ItemStack());
                        break;
                    }
                }
            }

            // Publish the accumulated stream for this tick.
            if (_flowItem != null && _flowDirs.Count > 0)
                PublishFlow();
        }

        /// <summary>
        /// Scans all colliders within connectRadius for IItemContainer components
        /// (excluding other ItemPipes) and tries to insert items.
        /// </summary>
        private void TryPushToSinks(ItemStack stack)
        {
            var hits = Physics.OverlapSphere(transform.position, connectRadius);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                if (col.GetComponentInParent<ItemPipe>() != null) continue; // skip pipes

                // Chest containers respect their ADVANCED PORT CONFIG: items only
                // enter through an enabled INPUT face whose filter accepts the item.
                var chest = col.GetComponentInParent<Chest>();
                if (chest != null && chest.container != null)
                {
                    int accepted = chest.TryAcceptFromPipe(transform.position, stack.item, stack.count);
                    if (accepted > 0)
                    {
                        Vector3 toDir = (chest.transform.position - transform.position).normalized;
                        RecordFlow(stack.item, toDir);
                        stack.count -= accepted;
                        if (stack.IsEmpty || stack.count <= 0) return;
                    }
                    continue; // chest handled its own insertion rules — don't double-push
                }

                // Check any IItemContainer (machines without per-face config)
                var containers = col.GetComponents<IItemContainer>();
                foreach (var c in containers)
                {
                    int before = stack.count;
                    TryPushIntoContainer(c, stack);
                    if (stack.count < before)
                    {
                        Vector3 toDir = (col.bounds.center - transform.position).normalized;
                        RecordFlow(stack.item, toDir);
                    }
                    if (stack.IsEmpty || stack.count <= 0) return;
                }
            }
        }

        private void TryPushIntoContainer(IItemContainer container, ItemStack stack)
        {
            if (stack.IsEmpty || stack.count <= 0) return;
            var clone    = stack.Clone();
            var leftover = container.Insert(clone);
            int accepted = stack.count - (leftover?.count ?? 0);
            stack.count -= accepted;
        }

        /// <summary>Accumulate an outbound flow direction for this tick.</summary>
        private void RecordFlow(ItemDefinition item, Vector3 worldDir)
        {
            if (item == null || worldDir.sqrMagnitude < 0.0001f) return;
            _flowItem = item;
            Vector3 norm = worldDir.normalized;
            // De-dup near-identical directions.
            foreach (var d in _flowDirs)
                if (Vector3.Dot(d, norm) > 0.97f) return;
            _flowDirs.Add(norm);
        }

        /// <summary>Hand this tick's accumulated stream to the glass visualizer.</summary>
        private void PublishFlow()
        {
            EnsureFlow();
            if (_flow == null) return;
            _flow.SetFlow(_flowItem, _flowDirs);
        }

        /// <summary>
        /// Called by an upstream pipe so the RECEIVING segment also shows an
        /// inbound stream (keeps the animation continuous across pipe joints).
        /// </summary>
        public void ReceiveFlow(ItemDefinition item, Vector3 worldDir)
        {
            EnsureFlow();
            if (_flow == null || item == null) return;
            _scratchDir.Clear();
            _scratchDir.Add(worldDir.normalized);
            _flow.SetFlow(item, _scratchDir);
        }
        private readonly List<Vector3> _scratchDir = new(1);
    }
}
