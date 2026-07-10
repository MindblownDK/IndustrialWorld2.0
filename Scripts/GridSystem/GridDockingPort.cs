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

        [Tooltip("Magnetic lock strength (N) — joint break force, like SE docking.")]
        public float lockStrength = 1000000f;
        [Tooltip("Auto-lock to another docking port / dock on contact.")]
        public bool autoDock = true;

        public ItemContainer container;

        public bool IsDocked => _joint != null;
        public BaseDock ConnectedBaseDock { get; private set; }

        private FixedJoint _joint;
        private float _dockTimer;

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
            Disconnect();
            if (Grid != null && GridItemNetwork.Instance != null)
                GridItemNetwork.Instance.UnregisterStore(Grid, this);
        }

        private void EnsureRefs()
        {
            if (this == null) return; // block was destroyed (ground/removed) this frame
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

        // ── Docking (magnetic lock via FixedJoint) ───────
        private void FixedUpdate()
        {
            if (!Enabled || IsDocked || !autoDock || Grid == null || Grid.Body == null) return;
            _dockTimer += Time.fixedDeltaTime;
            if (_dockTimer < 0.3f) return;
            _dockTimer = 0f;
            TryDock();
        }

        /// <summary>Lock onto a docking port / base dock the connector is facing.</summary>
        public void TryDock()
        {
            if (IsDocked || Grid == null || Grid.Body == null) return;
            float cs = Grid.gridSize.CellSize();
            if (Physics.Raycast(transform.position, transform.up, out var hit, cs * 1.0f))
            {
                var otherDock = hit.collider.GetComponentInParent<GridDockingPort>();
                var baseDock  = hit.collider.GetComponentInParent<BaseDock>();
                if (otherDock != null && otherDock.Grid == Grid) return; // not our own ship

                _joint = Grid.gameObject.AddComponent<FixedJoint>();
                _joint.breakForce = lockStrength;
                _joint.breakTorque = lockStrength;
                _joint.connectedBody = hit.collider.attachedRigidbody; // null = locked to world/base
                _joint.enableCollision = false;
                ConnectedBaseDock = baseDock;
            }
        }

        public void Connect(GridDockingPort other) => TryDock();

        public void Disconnect()
        {
            if (_joint != null) { Destroy(_joint); _joint = null; }
            ConnectedBaseDock = null;
        }

        public void Undock() => Disconnect();
        public void ToggleDock() { if (IsDocked) Disconnect(); else TryDock(); }

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
