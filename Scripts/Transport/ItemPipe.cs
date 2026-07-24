using System.Collections.Generic;
using UnityEngine;
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
        // Directed flow segments accumulated this tick: each is (fromDir → toDir)
        // in world space so the visualizer animates a true one-way path through
        // the pipe (entry side → hub → exit side) instead of pulsing outward.
        private readonly List<(Vector3 from, Vector3 to)> _flowSegments = new(6);
        private ItemDefinition _flowItem;

        // Candidate endpoint colliders gathered per scan: touch-range sphere PLUS
        // the five-cell cardinal corridor (chests/machines join from up to five
        // lattice cells away in a straight row — same rule as every other pipe).
        private readonly List<Collider> _endpointColliders = new(24);
        private readonly HashSet<Collider> _endpointColliderSet = new();
        private static readonly Collider[] s_endpointProbe = new Collider[24];
        private float _nextEndpointGatherAt;

        /// <summary>Fill <see cref="_endpointColliders"/> with every collider the pipe
        /// should consider an endpoint candidate: anything in touch range, plus the
        /// five-cell cardinal corridor in this pipe's lattice frame.</summary>
        private void GatherEndpointColliders()
        {
            // Memoize the 5-cell corridor sweep per pipe: it runs inside both the
            // visual scan and the push tick, and 31 OverlapSphere probes per call add
            // up fast with long pipe runs — endpoints rarely change within 0.5 s.
            if (Time.time < _nextEndpointGatherAt && _endpointColliders.Count > 0) return;
            _nextEndpointGatherAt = Time.time + 0.5f;

            _endpointColliders.Clear();
            _endpointColliderSet.Clear();
            int near = Physics.OverlapSphereNonAlloc(transform.position, endpointConnectRadius,
                s_endpointProbe, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < near; i++)
            {
                var col = s_endpointProbe[i]; s_endpointProbe[i] = null;
                if (col != null && _endpointColliderSet.Add(col)) _endpointColliders.Add(col);
            }

            var block = GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            // Five LATTICE cells: grid-mounted pipes probe on the grid's own cell size.
            float step = block != null && block.Grid != null
                ? VoxelEngine.GridSystem.GridSizeExt.CellSize(block.Grid.gridSize)
                : VoxelEngine.Networks.PipeAdjacency.DefaultGridSize;
            Transform frame = block != null && block.Grid != null ? block.Grid.transform : null;
            VoxelEngine.Networks.PipeAdjacency.ProbeCardinal(transform.position, frame, step, 5,
                s_endpointProbe, col =>
                {
                    if (col != null && _endpointColliderSet.Add(col)) _endpointColliders.Add(col);
                    return false; // collect everything — endpoints never stop the sweep
                });
        }

        // World-space direction the most recent item ARRIVED from (set by an
        // upstream pipe via ReceiveFlow). Used as the "entry" side when this
        // pipe forwards onward, so flow looks continuous across joints.
        private Vector3 _pendingEntryDir;
        private float   _pendingEntryUntil;

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
            // Item pipes carry visible PELLETS, not a fluid medium — keep the
            // glass tube HOLLOW so the pellets are seen through the clear shell
            // instead of being hidden behind an opaque inner core.
            _visuals.hollowGlass = isGlass;
            // Item pipes use the sleeved industrial pipe style —
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

        private void OnEnable()
        {
            if (VoxelEngine.Building.BuildSystem.IsCreatingGhost) return;
            ItemPipeNetwork.EnsureInstance();
            ItemPipeNetwork.Instance?.Register(this);
        }
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
                    free += ItemStack.MaxItemsPerStack(item);
                else if (s.item == item && item.IsStackable)
                    free += ItemStack.MaxItemsPerStack(item) - s.count;
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
                if (n != null) _neighbourPosBuf.Add(Vector3.Lerp(transform.position, n.transform.position, 0.5f));
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
            GatherEndpointColliders();
            foreach (var col in _endpointColliders)
            {
                if (col.gameObject == gameObject) continue;
                if (col.GetComponentInParent<ItemPipe>() != null) continue; // pipes already handled

                // DIRECT ENDPOINTS (drawers, single-item deep stores): these own their
                // capacity/filter logic and can still expose the same face config.
                var direct = col.GetComponentInParent<IDirectItemPortEndpoint>();
                if (direct != null)
                {
                    if (!direct.IsFaceConnectable(transform.position)) continue;
                }
                else
                {
                    // PORT HOSTS (chests, furnaces, processors…): only connect when the
                    // face pointing at us is an ENABLED Input/Output port. A disabled /
                    // None face means NO connection — no arm, no item exchange.
                    var routing = col.GetComponentInParent<ItemPortRouting>();
                    if (routing != null)
                    {
                        if (!routing.IsFaceConnectable(transform.position)) continue;
                    }
                    else
                    {
                        // Legacy endpoints without per-face routing still connect if
                        // they expose an item interface or container.
                        bool isEndpoint = col.GetComponentInParent<IInventoryInterface>() != null
                                       || col.GetComponentInParent<IItemContainer>() != null;
                        if (!isEndpoint) continue;
                    }
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
            _nextEndpointGatherAt = 0f; // bypass the memo — config changed just now
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

            // Reset this tick's flow accumulator. Each successful move appends a
            // directed (from → to) segment; at the end we hand them to the
            // visualizer so pellets travel one-way through the pipe.
            _flowSegments.Clear();
            _flowItem = null;

            // The side items arrived from this tick (defaults to "none/hub" when
            // injected directly by an adjacent chest with no upstream pipe).
            Vector3 entryDir = (Time.time < _pendingEntryUntil) ? _pendingEntryDir : Vector3.zero;

            for (int i = 0; i < _buffer.Size; i++)
            {
                var stack = _buffer.GetSlot(i);
                if (stack.IsEmpty) continue;

                // 1) Try to push into any sink (chest/machine).
                TryPushToSinks(stack, entryDir);
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
                        // Don't let an item visually U-turn out the same side it
                        // entered: if entry == exit (shouldn't happen) drop entry.
                        Vector3 fromDir = entryDir;
                        if (fromDir.sqrMagnitude > 0.01f && Vector3.Dot(fromDir, toDir) > 0.9f)
                            fromDir = Vector3.zero;
                        RecordSegment(stack.item, fromDir, toDir);
                        // Tell the receiving pipe which side WE are on so it can
                        // continue the same one-way path next tick.
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

            // Publish the accumulated directed stream for this tick.
            if (_flowItem != null && _flowSegments.Count > 0)
                PublishFlow();
        }

        /// <summary>
        /// Scans all colliders within connectRadius for IItemContainer components
        /// (excluding other ItemPipes) and tries to insert items.
        /// </summary>
        private void TryPushToSinks(ItemStack stack, Vector3 entryDir)
        {
            // Sinks = touch range PLUS the five-cell cardinal corridor: a chest or
            // machine up to five lattice cells straight off the pipe still takes
            // delivery (same rule tanks got for liquid/gas). Directed flow segments
            // keep the pellets animating the full hop in glass pipes.
            GatherEndpointColliders();
            foreach (var col in _endpointColliders)
            {
                if (col.gameObject == gameObject) continue;
                if (col.GetComponentInParent<ItemPipe>() != null) continue; // skip pipes

                // DIRECT ENDPOINTS (drawers, virtual stores) own their capacity/filter logic.
                var direct = col.GetComponentInParent<IDirectItemPortEndpoint>();
                if (direct != null)
                {
                    int accepted = direct.TryAcceptFromPipe(transform.position, stack.item, stack.count);
                    if (accepted > 0)
                    {
                        var directComponent = direct as Component;
                        Vector3 target = directComponent != null ? directComponent.transform.position : col.bounds.center;
                        Vector3 toDir = (target - transform.position).normalized;
                        RecordSegment(stack.item, SafeEntry(entryDir, toDir), toDir);
                        stack.count -= accepted;
                        if (stack.IsEmpty || stack.count <= 0) return;
                    }
                    continue;
                }

                // PORT HOSTS respect their ADVANCED PORT CONFIG: items only enter
                // through an enabled INPUT face whose filter accepts the item, and
                // route into that face's chosen container.
                var routing = col.GetComponentInParent<ItemPortRouting>();
                if (routing != null)
                {
                    int accepted = routing.TryAcceptFromPipe(transform.position, stack.item, stack.count);
                    if (accepted > 0)
                    {
                        Vector3 toDir = (routing.transform.position - transform.position).normalized;
                        RecordSegment(stack.item, SafeEntry(entryDir, toDir), toDir);
                        stack.count -= accepted;
                        if (stack.IsEmpty || stack.count <= 0) return;
                    }
                    continue; // routing handled its own insertion rules — don't double-push
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
                        RecordSegment(stack.item, SafeEntry(entryDir, toDir), toDir);
                    }
                    if (stack.IsEmpty || stack.count <= 0) return;
                }
            }
        }

        /// <summary>Drop the entry side if it points the same way as the exit (avoids a U-turn).</summary>
        private static Vector3 SafeEntry(Vector3 entryDir, Vector3 exitDir)
        {
            if (entryDir.sqrMagnitude > 0.01f && Vector3.Dot(entryDir, exitDir) > 0.9f)
                return Vector3.zero;
            return entryDir;
        }

        private void TryPushIntoContainer(IItemContainer container, ItemStack stack)
        {
            if (stack.IsEmpty || stack.count <= 0) return;
            var clone    = stack.Clone();
            var leftover = container.Insert(clone);
            int accepted = stack.count - (leftover?.count ?? 0);
            stack.count -= accepted;
        }

        /// <summary>Accumulate a directed (from → to) flow segment for this tick.</summary>
        private void RecordSegment(ItemDefinition item, Vector3 fromDir, Vector3 toDir)
        {
            if (item == null || toDir.sqrMagnitude < 0.0001f) return;
            _flowItem = item;
            // SNAP to the nearest cardinal axis so a slightly-off endpoint pivot
            // (e.g. a chest whose origin sits higher than the pipe) can't tilt the
            // pellet path up/down — items must visibly ride ALONG the tube.
            Vector3 to   = NearestCardinal(toDir);
            Vector3 from = fromDir.sqrMagnitude > 0.0001f ? NearestCardinal(fromDir) : Vector3.zero;
            if (to.sqrMagnitude < 0.0001f) return;
            // De-dup identical segments (same exit + entry).
            foreach (var seg in _flowSegments)
                if (Vector3.Dot(seg.to, to) > 0.97f &&
                    Vector3.Dot(seg.from, from) > 0.97f) return;
            _flowSegments.Add((from, to));
        }

        /// <summary>Hand this tick's accumulated directed stream to the glass visualizer.</summary>
        private void PublishFlow()
        {
            EnsureFlow();
            if (_flow == null) return;
            _flow.SetFlow(_flowItem, _flowSegments);
        }

        /// <summary>
        /// Called by an upstream pipe: records which world-space side items are
        /// arriving FROM, so when THIS pipe forwards them next tick the pellet
        /// path continues one-way across the joint. The hint expires quickly so a
        /// stale entry doesn't mislead later flow.
        /// </summary>
        public void ReceiveFlow(ItemDefinition item, Vector3 fromWorldDir)
        {
            if (item == null || fromWorldDir.sqrMagnitude < 0.0001f) return;
            _pendingEntryDir   = fromWorldDir.normalized;
            _pendingEntryUntil = Time.time + Mathf.Max(tickInterval * 2f, 0.5f);
        }
    }
}
