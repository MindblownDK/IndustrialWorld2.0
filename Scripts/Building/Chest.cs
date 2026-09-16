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
        [Tooltip("Free: faces cycle None/Input/Output. Provider: every active face outputs. " +
                 "Requester: every active face inputs. Locked chests only expose ON/OFF per face.")]
        public PortLockMode portLock = PortLockMode.Free;

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
            bool canIn  = portLock != PortLockMode.Requester;
            bool canOut = portLock != PortLockMode.Provider;
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
        /// Pin every face to the locked direction. An OFF face stays off; an active face
        /// is rewritten to Output (Provider) or Input (Requester). A Free chest is a no-op,
        /// so calling this unconditionally is always safe.
        /// </summary>
        public void EnforcePortLock()
        {
            if (portLock == PortLockMode.Free) return;
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
            if (portLock == PortLockMode.Requester) return 0;   // network-fed: pipes never push into it
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
        }
    }
}
