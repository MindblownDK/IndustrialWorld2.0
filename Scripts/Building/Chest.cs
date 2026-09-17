// Assets/Scripts/VoxelEngine/Building/Chest.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Transport;

namespace VoxelEngine.Building
{
    /// <summary>
    /// Storage container with ADVANCED PORT CONFIGURATION.
    ///
    /// Per-face None / Input / Output config + per-face item whitelists, driven by
    /// the shared <see cref="ItemPortRouting"/> component (same system every
    /// machine now uses). A chest exposes ONE container that can both send and
    /// receive, so each face's container dropdown is trivial — but the plumbing is
    /// identical to a multi-container furnace.
    /// </summary>
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class Chest : MonoBehaviour, IInventoryInterface, IItemPortHost, IPortLockedHost
    {
        [Tooltip("Number of slots inside this chest.")]
        public int size = 30;
        [Tooltip("Display name shown above the panel.")]
        public string displayName = "Chest";
        [Tooltip("Free: faces cycle None/Input/Output. Provider: pipes fill it, the network " +
                 "empties it. Requester: the network fills it, pipes empty it. Buffer: both, " +
                 "and its faces stay free. Provider/Requester expose only ON/OFF per face.")]
        public PortLockMode portLock = PortLockMode.Free;

        [Tooltip("Buffer only: how many of each requested item to keep in stock. The network " +
                 "tops the buffer up to this number and no further, so a buffer cannot drain " +
                 "the providers it shares a network with.")]
        [Min(1)] public int bufferStockTarget = 64;

        public ItemContainer container;

        /// <summary>
        /// What a Requester asks the wireless network to deliver. Deliberately SEPARATE from
        /// the per-face port filters: those decide what leaves this chest down a pipe, while
        /// this decides what the network brings in. Empty means the chest requests nothing,
        /// so an unconfigured Requester sits quiet instead of hoovering up the base.
        /// Unused by a Provider or a free chest.
        /// </summary>
        [SerializeField] private List<ItemDefinition> _requests = new();

        /// <summary>The items this chest asks the network for. Never null.</summary>
        public IReadOnlyList<ItemDefinition> Requests
        {
            get { _requests ??= new List<ItemDefinition>(); return _requests; }
        }

        /// <summary>
        /// How many of <paramref name="item"/> this chest still wants from the network.
        /// A Requester pulls without limit (int.MaxValue, capped per pass by the network);
        /// a Buffer only tops up to <see cref="bufferStockTarget"/>, which is what stops it
        /// from emptying the providers it also supplies from.
        /// Zero means the chest is satisfied and the network should skip it.
        /// </summary>
        public int ShortfallOf(ItemDefinition item)
        {
            if (item == null || container == null) return 0;
            if (!RequestsFromNetwork) return 0;

            bool requested = false;
            foreach (var r in Requests)
                if (ItemIdentity.Same(r, item)) { requested = true; break; }
            if (!requested) return 0;

            if (portLock != PortLockMode.Buffer) return int.MaxValue;

            int held = 0;
            for (int i = 0; i < container.Size; i++)
            {
                var slot = container.GetSlot(i);
                if (!slot.IsEmpty && ItemIdentity.Same(slot.item, item)) held += slot.count;
            }
            return Mathf.Max(0, bufferStockTarget - held);
        }

        /// <summary>Add an item to the wireless request list. Duplicates are ignored.</summary>
        public void AddRequest(ItemDefinition item)
        {
            if (item == null) return;
            _requests ??= new List<ItemDefinition>();
            foreach (var existing in _requests)
                if (ItemIdentity.Same(existing, item)) return;
            _requests.Add(item);
        }

        /// <summary>Remove an item from the wireless request list.</summary>
        public void RemoveRequest(ItemDefinition item)
        {
            if (item == null || _requests == null) return;
            for (int i = _requests.Count - 1; i >= 0; i--)
                if (ItemIdentity.Same(_requests[i], item)) _requests.RemoveAt(i);
        }

        /// <summary>Replace the whole request list — used by save restore.</summary>
        public void SetRequests(IEnumerable<ItemDefinition> items)
        {
            _requests = new List<ItemDefinition>();
            if (items == null) return;
            foreach (var item in items) AddRequest(item);
        }

        private PortConfig _ports;
        private ItemPortRouting _routing;
        private ItemPortContainer[] _portContainers;

        // ── IItemPortHost ───────────────────────────────────────────────────
        public PortConfig PortConfig { get { EnsureRefs(); return _ports; } }

        public IReadOnlyList<ItemPortContainer> GetPortContainers()
        {
            EnsureRefs();
            // A free chest is a single store that can both send and receive. A locked chest
            // advertises only the half its PORT role allows, which is the opposite of its
            // wireless role: a Provider is FED by pipes (input) and supplies the network
            // wirelessly; a Requester is filled by the network and FEEDS pipes (output).
            // A Buffer is both halves at once, so it advertises both.
            bool canIn  = portLock != PortLockMode.Requester;
            bool canOut = portLock != PortLockMode.Provider;
            if (portLock == PortLockMode.Buffer) { canIn = true; canOut = true; }
            _portContainers ??= new ItemPortContainer[1];
            _portContainers[0] = new ItemPortContainer("Storage", container, canIn, canOut);
            return _portContainers;
        }

        // ── IPortLockedHost ─────────────────────────────────────────────────
        public PortLockMode PortLock => portLock;

        /// <summary>
        /// The direction every active face is pinned to. A Provider is fed BY pipes, so its
        /// ports are inputs; a Requester feeds pipes, so its ports are outputs. This is the
        /// mirror of the block's wireless role, and the single place that decides it.
        /// </summary>
        public PortDirection PinnedDirection =>
            portLock == PortLockMode.Provider ? PortDirection.Input : PortDirection.Output;

        /// <summary>
        /// True when the faces are actually pinned to one direction. A Buffer takes part in
        /// the wireless network but still needs both halves of its ports, so it is locked in
        /// role yet free in direction — every direction query must ask this first rather than
        /// assuming "portLock != Free" means "pinned".
        /// </summary>
        public bool IsDirectionPinned =>
            portLock == PortLockMode.Provider || portLock == PortLockMode.Requester;

        /// <summary>True when this chest takes part in the wireless logistics network.</summary>
        public bool IsOnLogisticsNetwork => portLock != PortLockMode.Free;

        /// <summary>True when the network may draw stock OUT of this chest.</summary>
        public bool SuppliesNetwork =>
            portLock == PortLockMode.Provider || portLock == PortLockMode.Buffer;

        /// <summary>True when the network keeps this chest stocked against a request list.</summary>
        public bool RequestsFromNetwork =>
            portLock == PortLockMode.Requester || portLock == PortLockMode.Buffer;

        /// <summary>
        /// Pin every face to the locked direction. An OFF face stays off; an active face
        /// is rewritten to Output (Provider) or Input (Requester). A Free chest is a no-op,
        /// so calling this unconditionally is always safe.
        /// </summary>
        public void EnforcePortLock()
        {
            if (!IsDirectionPinned) return;   // Free and Buffer both keep free directions
            EnsureRefs();
            if (_ports == null) return;
            var pinned = PinnedDirection;
            bool changed = false;
            for (int i = 0; i < _ports.ports.Length; i++)
            {
                var p = _ports.ports[i];
                if (!p.enabled || p.direction == PortDirection.None) continue;
                if (p.direction == pinned) continue;
                _ports.SetDirection(p.face, pinned);
                changed = true;
            }
            if (changed) _ports.RefreshIndicators();
        }

        /// <summary>Routing component (per-face direction + filters + logistics).</summary>
        public ItemPortRouting Routing { get { EnsureRefs(); return _routing; } }

        // ── IInventoryInterface (legacy pipe API) ───────────────────────────
        // Mirrors GetPortContainers: a Provider only ACCEPTS from pipes (the network empties
        // it wirelessly), a Requester only FEEDS them (the network fills it wirelessly).
        // A Buffer is exempt from both restrictions: it accepts from pipes AND feeds them.
        public ItemContainer GetOutputContainer() => portLock == PortLockMode.Provider  ? null : container;
        public ItemContainer GetInputContainer()  => portLock == PortLockMode.Requester ? null : container;
        public bool HasOutputReady => portLock != PortLockMode.Provider &&
                                      container != null && _ports != null && _ports.HasAnyOutput();
        public bool CanAcceptInput => portLock != PortLockMode.Requester &&
                                      container != null && _ports != null && _ports.HasAnyInput();

        // ── Lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            if (container == null) container = new ItemContainer(displayName, size);
            else container.Resize(size);
            EnsureRefs();
            EnforcePortLock();
        }

        private void OnEnable()
        {
            // Only a locked chest takes part in wireless logistics; a free chest registers
            // as neither provider nor requester and costs the network nothing.
            if (portLock == PortLockMode.Free) return;
            LogisticsNetwork.EnsureInstance();
            LogisticsNetwork.Instance?.Register(this);
        }

        private void OnDisable()
        {
            LogisticsNetwork.Instance?.Unregister(this);
        }

        /// <summary>
        /// Change the chest's lock at runtime and re-file it in the logistics network.
        /// Kept as the one entry point so the network can never hold a chest under a lock
        /// it no longer has.
        /// </summary>
        public void SetPortLock(PortLockMode mode)
        {
            if (portLock == mode) return;
            portLock = mode;
            _portContainers = null;      // input/output capability just changed
            EnforcePortLock();

            if (mode == PortLockMode.Free)
            {
                LogisticsNetwork.Instance?.Unregister(this);
            }
            else
            {
                LogisticsNetwork.EnsureInstance();
                LogisticsNetwork.Instance?.Register(this);
            }
        }

        private void EnsureRefs()
        {
            if (_ports == null)
            {
                _ports = GetComponent<PortConfig>();
                if (_ports == null) _ports = gameObject.AddComponent<PortConfig>();
                _ports.EnsureAllFaces();
            }
            if (_routing == null)
            {
                _routing = GetComponent<ItemPortRouting>();
                if (_routing == null) _routing = gameObject.AddComponent<ItemPortRouting>();
            }
        }

        // ── Pipe-facing helpers (delegate to routing) ───────────────────────

        /// <summary>True if the face pointing at the pipe is an enabled Input/Output port.</summary>
        public bool IsFaceConnectable(Vector3 fromWorldPos)
        {
            EnsureRefs();
            return _routing != null && _routing.IsFaceConnectable(fromWorldPos);
        }

        /// <summary>Accept items pushed in by a pipe (honours INPUT face + filter).</summary>
        public int TryAcceptFromPipe(Vector3 pipeWorldPos, ItemDefinition item, int count)
        {
            if (portLock == PortLockMode.Requester) return 0;   // network-fed: pipes never push into it (a Buffer does accept)
            EnsureRefs();
            return _routing != null ? _routing.TryAcceptFromPipe(pipeWorldPos, item, count) : 0;
        }

        // ── Persistence bridge (used by WorldStatePersistence) ──────────────
        public ItemPortSnapshot CapturePortSnapshot()
        {
            EnsureRefs();
            var snap = _routing != null ? _routing.CaptureSnapshot() : new ItemPortSnapshot();
            // The wireless request list rides along on the port snapshot the chest already
            // saves, so it costs no new save plumbing and stays backward compatible.
            snap.requestItemIds ??= new List<string>();
            snap.requestItemIds.Clear();
            foreach (var item in Requests)
                if (item != null && !string.IsNullOrEmpty(item.itemId))
                    snap.requestItemIds.Add(item.itemId);

            // Only a buffer has a meaningful target; leaving it 0 elsewhere keeps the
            // snapshot empty for every other chest, exactly as before.
            snap.bufferStockTarget = portLock == PortLockMode.Buffer ? bufferStockTarget : 0;
            return snap;
        }

        public void ApplyPortSnapshot(ItemPortSnapshot snap, System.Func<string, ItemDefinition> resolveItem)
        {
            EnsureRefs();
            _routing?.ApplySnapshot(snap, resolveItem);
            EnforcePortLock();   // an older save of a now-locked chest can carry free directions

            // Restore the request list. A save from before this field existed simply has
            // none, which leaves the chest requesting nothing — the correct default.
            if (snap != null && snap.requestItemIds != null && resolveItem != null)
            {
                var restored = new List<ItemDefinition>();
                foreach (var id in snap.requestItemIds)
                {
                    var def = resolveItem(id);
                    if (def != null) restored.Add(def);
                }
                SetRequests(restored);
            }

            // A pre-11.6.0-dev save writes 0 here, which must not silently zero the target.
            if (snap != null && snap.bufferStockTarget > 0)
                bufferStockTarget = snap.bufferStockTarget;
        }
    }
}
