// Assets/Scripts/VoxelEngine/Simulation/Funnel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — FUNNEL BLOCK                                ║
// ║  Directional logistical hopper for moving items between an      ║
// ║  inventory/machine side and a moving belt side.                 ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    /// <summary>Funnel operating mode.</summary>
    public enum FunnelMode
    {
        /// <summary>Pulls items from the belt side and pushes them into the inventory side.</summary>
        Import,
        /// <summary>Pulls items from the inventory side and pushes them onto the belt side.</summary>
        Export
    }

    /// <summary>
    /// Directional side-mounted hopper for loading and unloading belts.
    /// Place it on the side of a chest, machine, or other inventory with a
    /// mechanical belt on the opposite side. In Import mode it pulls items from
    /// the belt into the inventory. In Export mode it pulls from the inventory
    /// and pushes directly onto the belt.
    /// </summary>
    public class Funnel : MonoBehaviour, IItemConsumer, IItemProvider
    {
        [Header("Funnel Configuration")]
        public FunnelMode mode = FunnelMode.Import;

        [Tooltip("Seconds between each item transfer.")]
        public float transferInterval = 0.5f;

        [Tooltip("Maximum items buffered internally.")]
        public int bufferSize = 4;

        [Header("Directional Ports")]
        [Tooltip("Local direction pointing toward the inventory/machine side.")]
        public Vector3 inventoryDirection = Vector3.back;

        [Tooltip("Local direction pointing toward the belt side.")]
        public Vector3 beltDirection = Vector3.forward;

        [Tooltip("Distance from funnel center used when scanning each port.")]
        public float portOffset = 0.85f;

        [Tooltip("Radius used when scanning for neighbouring inventories, machines, and belts.")]
        public float scanRadius = 0.65f;

        [Header("Auto-detected connections")]
        [Tooltip("Current item source for the active mode.")]
        public MonoBehaviour inputSource;

        [Tooltip("Current item destination for the active mode.")]
        public MonoBehaviour outputTarget;

        // ── Runtime ───────────────────────────────────────────────────

        private ItemContainer _buffer;
        private float _transferTimer;
        private float _scanTimer;

        /// <summary>Current operating mode.</summary>
        public FunnelMode Mode => mode;

        /// <summary>Items currently buffered inside the funnel.</summary>
        public int BufferedCount
        {
            get
            {
                if (_buffer == null) return 0;
                int total = 0;
                for (int i = 0; i < _buffer.Size; i++)
                {
                    var slot = _buffer.GetSlot(i);
                    if (!slot.IsEmpty) total += slot.count;
                }
                return total;
            }
        }

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            EnsureBuffer();
        }

        private void Update()
        {
            EnsureBuffer();
            float dt = Time.deltaTime;

            _scanTimer += dt;
            if (_scanTimer >= 0.35f)
            {
                _scanTimer = 0f;
                ScanConnections();
            }

            _transferTimer += dt;
            if (_transferTimer >= transferInterval)
            {
                _transferTimer -= transferInterval;
                TransferTick();
            }
        }

        private void EnsureBuffer()
        {
            if (_buffer == null)
                _buffer = new ItemContainer("FunnelBuffer", Mathf.Max(1, bufferSize));
            else
                _buffer.Resize(Mathf.Max(1, bufferSize));
        }

        // ── Connection Scanning ───────────────────────────────────────

        private void ScanConnections()
        {
            Vector3 inventoryPos = PortWorldPosition(inventoryDirection);
            Vector3 beltPos = PortWorldPosition(beltDirection);

            if (mode == FunnelMode.Import)
            {
                inputSource = FindProviderAt(beltPos) ?? FindProviderFallback(preferBelt: true);
                outputTarget = FindConsumerAt(inventoryPos) ?? FindConsumerFallback(preferInventory: true);
            }
            else
            {
                inputSource = FindProviderAt(inventoryPos) ?? FindProviderFallback(preferBelt: false);
                outputTarget = FindConsumerAt(beltPos) ?? FindConsumerFallback(preferInventory: false);
            }
        }

        private Vector3 PortWorldPosition(Vector3 localDirection)
        {
            Vector3 direction = localDirection.sqrMagnitude > 0.0001f ? localDirection.normalized : Vector3.forward;
            return transform.position + transform.TransformDirection(direction) * portOffset;
        }

        private MonoBehaviour FindProviderAt(Vector3 worldPos)
        {
            var hits = Physics.OverlapSphere(worldPos, scanRadius);
            foreach (var col in hits)
            {
                var mb = ResolveProvider(col);
                if (mb != null) return mb;
            }
            return null;
        }

        private MonoBehaviour FindConsumerAt(Vector3 worldPos)
        {
            var hits = Physics.OverlapSphere(worldPos, scanRadius);
            foreach (var col in hits)
            {
                var mb = ResolveConsumer(col);
                if (mb != null) return mb;
            }
            return null;
        }

        private MonoBehaviour FindProviderFallback(bool preferBelt)
        {
            var hits = Physics.OverlapSphere(transform.position, portOffset + scanRadius);
            MonoBehaviour fallback = null;
            foreach (var col in hits)
            {
                var mb = ResolveProvider(col);
                if (mb == null) continue;
                if (preferBelt && mb is ConveyorBelt) return mb;
                if (!preferBelt && !(mb is ConveyorBelt)) return mb;
                fallback ??= mb;
            }
            return fallback;
        }

        private MonoBehaviour FindConsumerFallback(bool preferInventory)
        {
            var hits = Physics.OverlapSphere(transform.position, portOffset + scanRadius);
            MonoBehaviour fallback = null;
            foreach (var col in hits)
            {
                var mb = ResolveConsumer(col);
                if (mb == null) continue;
                bool isInventoryLike = mb is IItemContainer || !(mb is ConveyorBelt);
                if (preferInventory && isInventoryLike) return mb;
                if (!preferInventory && mb is ConveyorBelt) return mb;
                fallback ??= mb;
            }
            return fallback;
        }

        private MonoBehaviour ResolveProvider(Collider col)
        {
            if (col == null || col.gameObject == gameObject) return null;
            var behaviours = col.GetComponentsInParent<MonoBehaviour>(true);
            foreach (var mb in behaviours)
            {
                if (mb == null || mb == this) continue;
                if (mb is IItemProvider || mb is IItemContainer) return mb;
            }
            return null;
        }

        private MonoBehaviour ResolveConsumer(Collider col)
        {
            if (col == null || col.gameObject == gameObject) return null;
            var behaviours = col.GetComponentsInParent<MonoBehaviour>(true);
            foreach (var mb in behaviours)
            {
                if (mb == null || mb == this) continue;
                if (mb is IItemConsumer || mb is IItemContainer) return mb;
            }
            return null;
        }

        // ── Transfer Logic ────────────────────────────────────────────

        private void TransferTick()
        {
            PullFromInput();
            PushBufferToOutput();
        }

        private void PullFromInput()
        {
            if (BufferedCount >= bufferSize || inputSource == null) return;

            if (inputSource is IItemProvider provider)
            {
                var item = provider.PeekOutput(out int available);
                if (item == null || available <= 0) return;

                int want = Mathf.Min(available, bufferSize - BufferedCount);
                int got = provider.TryExtract(item, want);
                if (got > 0) _buffer.Insert(new ItemStack(item, got));
                return;
            }

            if (inputSource is IItemContainer container)
            {
                PullFromContainer(container);
            }
        }

        private void PullFromContainer(IItemContainer container)
        {
            if (container == null) return;
            for (int i = 0; i < container.Slots.Count && BufferedCount < bufferSize; i++)
            {
                var slot = container.GetSlot(i);
                if (slot.IsEmpty || slot.item == null) continue;

                int take = Mathf.Min(slot.count, bufferSize - BufferedCount);
                int removed = container.Remove(slot.item, take);
                if (removed > 0)
                {
                    _buffer.Insert(new ItemStack(slot.item, removed));
                    return;
                }
            }
        }

        private void PushBufferToOutput()
        {
            if (outputTarget == null || _buffer == null) return;

            for (int i = 0; i < _buffer.Size; i++)
            {
                var slot = _buffer.GetSlot(i);
                if (slot.IsEmpty || slot.item == null) continue;

                int moved = 0;
                if (outputTarget is IItemConsumer consumer)
                {
                    int capacity = consumer.GetInputCapacity(slot.item);
                    if (capacity > 0)
                        moved = consumer.TryInsert(slot.item, Mathf.Min(capacity, slot.count));
                }
                else if (outputTarget is IItemContainer container)
                {
                    var leftover = container.Insert(new ItemStack(slot.item, slot.count));
                    moved = slot.count - (leftover?.count ?? 0);
                }

                if (moved > 0)
                {
                    _buffer.Remove(slot.item, moved);
                    return;
                }
            }
        }

        // ── IItemConsumer ─────────────────────────────────────────────

        public int GetInputCapacity(ItemDefinition item)
        {
            if (item == null) return 0;
            return Mathf.Max(0, bufferSize - BufferedCount);
        }

        public int TryInsert(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            EnsureBuffer();
            int accepted = Mathf.Min(count, Mathf.Max(0, bufferSize - BufferedCount));
            if (accepted <= 0) return 0;
            _buffer.Insert(new ItemStack(item, accepted));
            return accepted;
        }

        // ── IItemProvider ─────────────────────────────────────────────

        public ItemDefinition PeekOutput(out int count)
        {
            EnsureBuffer();
            for (int i = 0; i < _buffer.Size; i++)
            {
                var slot = _buffer.GetSlot(i);
                if (!slot.IsEmpty && slot.item != null)
                {
                    count = slot.count;
                    return slot.item;
                }
            }

            count = 0;
            return null;
        }

        public int TryExtract(ItemDefinition item, int count)
        {
            EnsureBuffer();
            if (item == null || count <= 0) return 0;
            return _buffer.Remove(item, count);
        }

        // ── Public Controls ───────────────────────────────────────────

        /// <summary>Toggle import/export mode.</summary>
        public void ToggleMode()
        {
            mode = mode == FunnelMode.Import ? FunnelMode.Export : FunnelMode.Import;
            inputSource = null;
            outputTarget = null;
            ScanConnections();
        }

        /// <summary>Set explicit mode from UI/interaction code.</summary>
        public void SetMode(FunnelMode newMode)
        {
            mode = newMode;
            inputSource = null;
            outputTarget = null;
            ScanConnections();
        }
    }
}
