// Assets/Scripts/VoxelEngine/GridSystem/GridDockingPort.cs
//
// Docking connector with a buffer inventory and full item-port routing — uses
// the same Transport port system as chests/machines, so it can input OR output
// items through the logistics network with per-face direction + item filters.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Transport;

namespace VoxelEngine.GridSystem
{
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class GridDockingPort : GridBlock, IItemPortHost, IGridItemStore
    {
        [Header("Docking Port")]
        [Tooltip("Buffer inventory items pass through while docked.")]
        public int slots = 12;
        public bool autoExport = true;

        public ItemContainer container;

        public bool IsDocked { get; private set; }
        public BaseDock ConnectedBaseDock { get; private set; }

        public override float ContentMass => container != null ? MassUtil.ContainerMass(container) : 0f;

        // ── IGridItemStore (participates in the grid item network) ──────────────
        public ItemContainer ItemStore => container;
        public string StoreLabel => "Docking Port";

        private PortConfig _ports;
        private ItemPortRouting _routing;
        private ItemPortContainer[] _portContainers;

        // ── IItemPortHost ───────────────────────────────────────────────────
        public PortConfig PortConfig { get { EnsureRefs(); return _ports; } }
        public ItemPortRouting Routing { get { EnsureRefs(); return _routing; } }

        public IReadOnlyList<ItemPortContainer> GetPortContainers()
        {
            EnsureRefs();
            // A docking port is a single buffer that can both send and receive.
            _portContainers ??= new ItemPortContainer[1];
            _portContainers[0] = new ItemPortContainer("Dock Buffer", container, canInput: true, canOutput: true);
            return _portContainers;
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Docking Port";
            if (container == null) container = new ItemContainer("Dock Buffer", slots);
            else container.Resize(slots);
            EnsureRefs();
            if (Grid != null && GridItemNetwork.Instance != null)
                GridItemNetwork.Instance.RegisterStore(Grid, this);
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            if (Grid != null && GridItemNetwork.Instance != null)
                GridItemNetwork.Instance.UnregisterStore(Grid, this);
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
            if (container == null) container = new ItemContainer("Dock Buffer", slots);
        }

        // ── Docking ──────────────────────────────────────────────────────────
        public void Connect(GridDockingPort other) => IsDocked = true;

        public void Disconnect()
        {
            IsDocked = false;
            ConnectedBaseDock = null;
        }

        public void Undock() => Disconnect();

        // ── Pipe-facing helpers (delegate to routing) ─────────────────────────
        public bool IsFaceConnectable(Vector3 fromWorldPos)
        {
            EnsureRefs();
            return _routing != null && _routing.IsFaceConnectable(fromWorldPos);
        }

        public int TryAcceptFromPipe(Vector3 pipeWorldPos, ItemDefinition item, int count)
        {
            EnsureRefs();
            return _routing != null ? _routing.TryAcceptFromPipe(pipeWorldPos, item, count) : 0;
        }
    }
}
