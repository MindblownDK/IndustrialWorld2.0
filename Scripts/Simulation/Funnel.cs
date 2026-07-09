// Assets/Scripts/VoxelEngine/Simulation/Funnel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — FUNNEL BLOCK                                ║
// ║  Bidirectional item transfer between belts and machines/chests. ║
// ║  Two modes:                                                     ║
// ║    IMPORT: Pulls items from a belt and pushes into a machine.   ║
// ║    EXPORT: Pulls items from a machine and pushes onto a belt.   ║
// ║  Player toggles mode by right-clicking the placed block.        ║
// ║  Uses IItemConsumer + IItemProvider for full belt integration.  ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    /// <summary>Funnel operating mode.</summary>
    public enum FunnelMode
    {
        /// <summary>Pulls from belt (below/side) → pushes into machine (above/side).</summary>
        Import,
        /// <summary>Pulls from machine (above/side) → pushes onto belt (below/side).</summary>
        Export
    }

    /// <summary>
    /// Funnel block that bridges conveyor belts and machines/chests.
    /// Implements both IItemConsumer (receives from belts) and IItemProvider
    /// (feeds into belts) so it works in either direction.
    ///
    /// Placement:
    ///   • Place ON TOP of a belt → acts as Import (belt → machine above).
    ///   • Place BELOW a machine output → acts as Export (machine → belt below).
    ///   • Right-click to toggle Import/Export mode.
    ///
    /// Transfer rate: one item every <see cref="transferInterval"/> seconds.
    /// </summary>
    public class Funnel : MonoBehaviour, IItemConsumer, IItemProvider
    {
        [Header("Funnel Configuration")]
        public FunnelMode mode = FunnelMode.Import;

        [Tooltip("Seconds between each item transfer.")]
        public float transferInterval = 0.5f;

        [Tooltip("Maximum items buffered internally.")]
        public int bufferSize = 4;

        [Header("Auto-detected connections")]
        [Tooltip("The belt or machine on the INPUT side.")]
        public MonoBehaviour inputSource;
        [Tooltip("The belt or machine on the OUTPUT side.")]
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
                    if (!_buffer.GetSlot(i).IsEmpty) total += _buffer.GetSlot(i).count;
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

            // Periodically scan for connections.
            _scanTimer += dt;
            if (_scanTimer >= 0.5f)
            {
                _scanTimer = 0f;
                ScanConnections();
            }

            // Transfer items at the configured interval.
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
                _buffer = new ItemContainer("FunnelBuffer", bufferSize);
            else
                _buffer.Resize(bufferSize);
        }

        // ── Connection Scanning ───────────────────────────────────────

        private void ScanConnections()
        {
            // Input source: the belt/machine BELOW or on the BACK of the funnel.
            Vector3 inputPos = transform.position + Vector3.down * 0.8f;
            inputSource = FindInterfaceAt(inputPos, inputSource);

            // Output target: the machine/belt ABOVE or on the FRONT of the funnel.
            Vector3 outputPos = transform.position + Vector3.up * 0.8f;
            outputTarget = FindInterfaceAt(outputPos, outputTarget);

            // Also check sides (±X, ±Z) for belt connections.
            if (inputSource == null)
                inputSource = FindInterfaceAt(transform.position + transform.forward * 0.8f, null);
            if (outputTarget == null)
                outputTarget = FindInterfaceAt(transform.position - transform.forward * 0.8f, null);
        }

        private MonoBehaviour FindInterfaceAt(Vector3 worldPos, MonoBehaviour current)
        {
            if (current != null) return current; // Keep existing connection.

            var hits = Physics.OverlapSphere(worldPos, 0.6f);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;

                // Check for IItemConsumer or IItemProvider.
                var consumer = col.GetComponentInParent<MonoBehaviour>() as IItemConsumer;
                if (consumer != null) return consumer as MonoBehaviour;

                var provider = col.GetComponentInParent<MonoBehaviour>() as IItemProvider;
                if (provider != null) return provider as MonoBehaviour;

                // Check for ItemContainer (chests, machines).
                var container = col.GetComponentInParent<IItemContainer>();
                if (container != null) return col.GetComponentInParent<MonoBehaviour>();
            }
            return null;
        }

        // ── Transfer Logic ────────────────────────────────────────────

        private void TransferTick()
        {
            if (mode == FunnelMode.Import)
                TransferImport();
            else
                TransferExport();
        }

        /// <summary>
        /// IMPORT mode: Pull from input source → buffer → push to output target.
        /// </summary>
        private void TransferImport()
        {
            // Step 1: Pull from input (belt below).
            if (BufferedCount < bufferSize && inputSource != null)
            {
                var provider = inputSource as IItemProvider;
                if (provider != null)
                {
                    var item = provider.PeekOutput(out int available);
                    if (item != null && available > 0)
                    {
                        int want = Mathf.Min(available, bufferSize - BufferedCount);
                        int got = provider.TryExtract(item, want);
                        if (got > 0)
                            _buffer.Insert(new ItemStack(item, got));
                    }
                }
                else
                {
                    // Input might be a raw IItemContainer (chest).
                    var container = inputSource.GetComponent<IItemContainer>();
                    if (container != null)
                    {
                        for (int i = 0; i < container.Slots.Count; i++)
                        {
                            var slot = container.GetSlot(i);
                            if (!slot.IsEmpty && slot.item != null && BufferedCount < bufferSize)
                            {
                                int take = Mathf.Min(slot.count, bufferSize - BufferedCount);
                                int removed = container.Remove(slot.item, take);
                                if (removed > 0)
                                    _buffer.Insert(new ItemStack(slot.item, removed));
                                break; // One item type per tick.
                            }
                        }
                    }
                }
            }

            // Step 2: Push buffer to output (machine above).
            PushBufferToOutput();
        }

        /// <summary>
        /// EXPORT mode: Pull from input (machine above) → buffer → push to output (belt below).
        /// </summary>
        private void TransferExport()
        {
            // Step 1: Pull from input (machine output).
            if (BufferedCount < bufferSize && inputSource != null)
            {
                var provider = inputSource as IItemProvider;
                if (provider != null)
                {
                    var item = provider.PeekOutput(out int available);
                    if (item != null && available > 0)
                    {
                        int want = Mathf.Min(available, bufferSize - BufferedCount);
                        int got = provider.TryExtract(item, want);
                        if (got > 0)
                            _buffer.Insert(new ItemStack(item, got));
                    }
                }
                else
                {
                    var container = inputSource.GetComponent<IItemContainer>();
                    if (container != null)
                    {
                        for (int i = 0; i < container.Slots.Count; i++)
                        {
                            var slot = container.GetSlot(i);
                            if (!slot.IsEmpty && slot.item != null && BufferedCount < bufferSize)
                            {
                                int take = Mathf.Min(slot.count, bufferSize - BufferedCount);
                                int removed = container.Remove(slot.item, take);
                                if (removed > 0)
                                    _buffer.Insert(new ItemStack(slot.item, removed));
                                break;
                            }
                        }
                    }
                }
            }

            // Step 2: Push buffer to output (belt below).
            PushBufferToOutput();
        }

        private void PushBufferToOutput()
        {
            if (outputTarget == null) return;

            var consumer = outputTarget as IItemConsumer;
            if (consumer != null)
            {
                for (int i = 0; i < _buffer.Size; i++)
                {
                    var slot = _buffer.GetSlot(i);
                    if (slot.IsEmpty) continue;

                    int cap = consumer.GetInputCapacity(slot.item);
                    if (cap <= 0) continue;

                    int send = Mathf.Min(slot.count, cap);
                    int accepted = consumer.TryInsert(slot.item, send);
                    if (accepted > 0)
                    {
                        slot.count -= accepted;
                        _buffer.SetSlot(i, slot.count > 0 ? slot : new ItemStack());
                    }
                    break; // One item type per tick.
                }
            }
            else
            {
                // Output might be a raw container or belt.
                var container = outputTarget.GetComponent<IItemContainer>();
                if (container != null)
                {
                    for (int i = 0; i < _buffer.Size; i++)
                    {
                        var slot = _buffer.GetSlot(i);
                        if (slot.IsEmpty) continue;

                        var leftover = container.Insert(new ItemStack(slot.item, slot.count));
                        int accepted = slot.count - (leftover?.count ?? 0);
                        if (accepted > 0)
                        {
                            slot.count -= accepted;
                            _buffer.SetSlot(i, slot.count > 0 ? slot : new ItemStack());
                        }
                        break;
                    }
                }
            }
        }

        // ── IItemConsumer (belts can push items INTO this funnel) ─────

        public int GetInputCapacity(ItemDefinition item)
        {
            if (item == null) return 0;
            return _buffer.HasSpace(item, 1) ? item.maxStack : 0;
        }

        public int TryInsert(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            var leftover = _buffer.Insert(new ItemStack(item, count));
            return count - (leftover?.count ?? 0);
        }

        // ── IItemProvider (belts can pull items FROM this funnel) ─────

        public ItemDefinition PeekOutput(out int count)
        {
            count = 0;
            for (int i = 0; i < _buffer.Size; i++)
            {
                var slot = _buffer.GetSlot(i);
                if (!slot.IsEmpty && slot.item != null)
                {
                    count = slot.count;
                    return slot.item;
                }
            }
            return null;
        }

        public int TryExtract(ItemDefinition item, int count)
        {
            return _buffer.Remove(item, count);
        }

        // ── Public API ────────────────────────────────────────────────

        /// <summary>Toggle between Import and Export mode.</summary>
        public void ToggleMode()
        {
            mode = (mode == FunnelMode.Import) ? FunnelMode.Export : FunnelMode.Import;
            // Reset connections so they re-scan for the new direction.
            inputSource = null;
            outputTarget = null;
        }
    }
}
