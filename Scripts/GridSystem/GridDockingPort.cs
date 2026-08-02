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
        private bool _manuallyUndocked;
        private bool _madeGridKinematic;

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
            // A destroyed/broken joint must also release occupancy and physics
            // ownership, otherwise a ship could remain kinematic after a dock broke.
            if (_joint == null && (ConnectedBaseDock != null || _madeGridKinematic))
                DisconnectInternal(manual: false);

            if (!Enabled)
            {
                DisconnectInternal(manual: false);
                return;
            }
            if (IsDocked || !autoDock || _manuallyUndocked || Grid == null || Grid.Body == null) return;
            _dockTimer += Time.fixedDeltaTime;
            if (_dockTimer < 0.3f) return;
            _dockTimer = 0f;
            TryDockInternal();
        }

        /// <summary>Lock onto a docking port / base dock the connector is facing.</summary>
        public void TryDock()
        {
            _manuallyUndocked = false;
            TryDockInternal();
        }

        private void TryDockInternal()
        {
            if (IsDocked || Grid == null || Grid.Body == null) return;
            float cs = EffectiveCellSize;
            if (!Physics.Raycast(transform.position, transform.up, out var hit, cs * 1.0f, ~0, QueryTriggerInteraction.Ignore)) return;

            var otherDock = hit.collider.GetComponentInParent<GridDockingPort>();
            var baseDock = hit.collider.GetComponentInParent<BaseDock>();
            if (otherDock != null && otherDock.Grid == Grid) return; // never dock to our own ship
            if (otherDock == null && baseDock == null) return;       // never lock to arbitrary scenery
            if (otherDock != null && otherDock.IsDocked) return;
            if (baseDock != null && baseDock.isOccupied) return;

            _joint = Grid.gameObject.AddComponent<FixedJoint>();
            // A player-selected dock is a hard stationary lock. Finite break forces
            // caused rapid break/recreate flicker when a heavy ship settled on a pad.
            _joint.breakForce = float.PositiveInfinity;
            _joint.breakTorque = float.PositiveInfinity;
            _joint.connectedBody = hit.collider.attachedRigidbody; // null = static base/terrain
            _joint.enableCollision = false;
            ConnectedBaseDock = baseDock;
            if (ConnectedBaseDock != null) ConnectedBaseDock.isOccupied = true;

            Grid.Body.linearVelocity = Vector3.zero;
            Grid.Body.angularVelocity = Vector3.zero;
            if (_joint.connectedBody == null)
            {
                Grid.Body.isKinematic = true;
                _madeGridKinematic = true;
            }
        }

        public void Connect(GridDockingPort other) => TryDock();

        public void Disconnect()
        {
            DisconnectInternal(manual: true);
        }

        private void DisconnectInternal(bool manual)
        {
            if (_joint != null) { Destroy(_joint); _joint = null; }
            if (ConnectedBaseDock != null) ConnectedBaseDock.isOccupied = false;
            ConnectedBaseDock = null;
            if (_madeGridKinematic && Grid != null && Grid.Body != null)
            {
                Grid.Body.isKinematic = false;
                _madeGridKinematic = false;
            }
            if (manual) _manuallyUndocked = true;
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
