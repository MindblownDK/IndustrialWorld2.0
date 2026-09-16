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

        private PortConfig _ports;
        private ItemPortRouting _routing;
        private ItemPortContainer[] _portContainers;

        // ── IItemPortHost ───────────────────────────────────────────────────
        public PortConfig PortConfig { get { EnsureRefs(); return _ports; } }

        public IReadOnlyList<ItemPortContainer> GetPortContainers()
        {
            EnsureRefs();
            // A free chest is a single store that can both send and receive; a locked
            // chest advertises only the half of that its lock allows, so the routing
            // layer refuses the wrong transfer even if a face were somehow mis-set.
            bool canIn  = portLock != PortLockMode.Provider;
            bool canOut = portLock != PortLockMode.Requester;
            _portContainers ??= new ItemPortContainer[1];
            _portContainers[0] = new ItemPortContainer("Storage", container, canIn, canOut);
            return _portContainers;
        }

        // ── IPortLockedHost ─────────────────────────────────────────────────
        public PortLockMode PortLock => portLock;

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
            var pinned = portLock == PortLockMode.Provider ? PortDirection.Output : PortDirection.Input;
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
        // A Requester never gives items away and a Provider never takes them, so the
        // legacy pipe API answers null/false for the half its lock forbids.
        public ItemContainer GetOutputContainer() => portLock == PortLockMode.Requester ? null : container;
        public ItemContainer GetInputContainer()  => portLock == PortLockMode.Provider  ? null : container;
        public bool HasOutputReady => portLock != PortLockMode.Requester &&
                                      container != null && _ports != null && _ports.HasAnyOutput();
        public bool CanAcceptInput => portLock != PortLockMode.Provider &&
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
            if (portLock == PortLockMode.Provider) return 0;   // supply-only: never accepts a push
            EnsureRefs();
            return _routing != null ? _routing.TryAcceptFromPipe(pipeWorldPos, item, count) : 0;
        }

        // ── Persistence bridge (used by WorldStatePersistence) ──────────────
        public ItemPortSnapshot CapturePortSnapshot()
        {
            EnsureRefs();
            return _routing != null ? _routing.CaptureSnapshot() : new ItemPortSnapshot();
        }

        public void ApplyPortSnapshot(ItemPortSnapshot snap, System.Func<string, ItemDefinition> resolveItem)
        {
            EnsureRefs();
            _routing?.ApplySnapshot(snap, resolveItem);
            EnforcePortLock();   // an older save of a now-locked chest can carry free directions
        }
    }
}
