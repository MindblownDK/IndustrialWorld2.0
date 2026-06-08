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
    public class Chest : MonoBehaviour, IInventoryInterface, IItemPortHost
    {
        [Tooltip("Number of slots inside this chest.")]
        public int size = 30;
        [Tooltip("Display name shown above the panel.")]
        public string displayName = "Chest";

        public ItemContainer container;

        private PortConfig _ports;
        private ItemPortRouting _routing;
        private ItemPortContainer[] _portContainers;

        // ── IItemPortHost ───────────────────────────────────────────────────
        public PortConfig PortConfig { get { EnsureRefs(); return _ports; } }

        public IReadOnlyList<ItemPortContainer> GetPortContainers()
        {
            EnsureRefs();
            // A chest is a single store that can both send and receive.
            _portContainers ??= new[]
            {
                new ItemPortContainer("Storage", container, canInput: true, canOutput: true)
            };
            // Container ref can change after Awake (deserialize) — keep it fresh.
            _portContainers[0] = new ItemPortContainer("Storage", container, true, true);
            return _portContainers;
        }

        /// <summary>Routing component (per-face direction + filters + logistics).</summary>
        public ItemPortRouting Routing { get { EnsureRefs(); return _routing; } }

        // ── IInventoryInterface (legacy pipe API) ───────────────────────────
        public ItemContainer GetOutputContainer() => container;
        public ItemContainer GetInputContainer()  => container;
        public bool HasOutputReady => container != null && _ports != null && _ports.HasAnyOutput();
        public bool CanAcceptInput => container != null && _ports != null && _ports.HasAnyInput();

        // ── Lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            if (container == null) container = new ItemContainer(displayName, size);
            else container.Resize(size);
            EnsureRefs();
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
        }
    }
}
