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
    /// cables. Glass variant exposes an inner core showing the carried stream.
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

        // ── Runtime ────────────────────────────────────────────────────────
        [System.NonSerialized] public List<ItemPipe> neighbours = new();
        private ItemContainer _buffer;
        private float _tickTimer;

        // ── Visual integration ─────────────────────────────────────────────
        private PipeVisualBuilder _visuals;
        private readonly List<Vector3> _neighbourPosBuf = new(6);

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
        }

        private void OnEnable()  { ItemPipeNetwork.EnsureInstance(); ItemPipeNetwork.Instance?.Register(this); }
        private void OnDisable() => ItemPipeNetwork.Instance?.Unregister(this);

        private void Update()
        {
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

        private List<Vector3> GetNeighbourPositions()
        {
            _neighbourPosBuf.Clear();
            foreach (var n in neighbours)
                if (n != null) _neighbourPosBuf.Add(n.transform.position);
            return _neighbourPosBuf;
        }

        /// <summary>
        /// Each tick, try to push items from our buffer into valid sinks
        /// (chests, furnaces, etc.) or forward along neighbour pipes.
        /// </summary>
        private void PushBufferDownstream()
        {
            EnsureBuffer();

            for (int i = 0; i < _buffer.Size; i++)
            {
                var stack = _buffer.GetSlot(i);
                if (stack.IsEmpty) continue;

                // 1) Try to push into any IItemContainer on nearby GameObjects
                //    that are NOT other ItemPipes (those are real sinks: chests, furnaces…).
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
                    stack.count -= accepted;

                    if (stack.count <= 0)
                    {
                        _buffer.SetSlot(i, new ItemStack());
                        break;
                    }
                }
            }
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
                        stack.count -= accepted;
                        if (stack.IsEmpty || stack.count <= 0) return;
                    }
                    continue; // chest handled its own insertion rules — don't double-push
                }

                // Check any IItemContainer (machines without per-face config)
                var containers = col.GetComponents<IItemContainer>();
                foreach (var c in containers)
                {
                    TryPushIntoContainer(c, stack);
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
    }
}
