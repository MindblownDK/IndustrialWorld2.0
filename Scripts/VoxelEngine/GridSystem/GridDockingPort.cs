// Assets/Scripts/VoxelEngine/GridSystem/GridDockingPort.cs
//
// Docking port — connects a ship to a base dock or another ship.
// When docked and connected to item cables, auto-exports cargo from
// the ship's cargo containers to the base's chests.
//
// Configurable: auto-export on/off (default: ON).

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Transport;

namespace VoxelEngine.GridSystem
{
    public class GridDockingPort : GridBlock
    {
        [Header("Docking")]
        [Tooltip("Range to detect a compatible dock.")]
        public float dockRange = 2.0f;
        [Tooltip("Auto-export items from ship cargo to connected base.")]
        public bool autoExport = true;

        [Header("Port Config (Filter)")]
        [Tooltip("Use existing PortConfig system for export/import filters.")]
        public bool usePortConfigFilter = true;
        [Tooltip("Seconds between export ticks.")]
        public float exportInterval = 1f;

        public bool IsDocked { get; private set; }
        public GridDockingPort ConnectedPort { get; private set; }
        /// <summary>Base dock (PlacedBlock with DockingPort) we're connected to.</summary>
        public BaseDock ConnectedBaseDock { get; private set; }

        private float _exportTimer;
        private float _dockCheckTimer;

        private void Update()
        {
            // Check for docking.
            _dockCheckTimer += Time.deltaTime;
            if (_dockCheckTimer >= 1f)
            {
                _dockCheckTimer = 0;
                if (!IsDocked) TryDock();
            }

            // Auto-export.
            if (IsDocked && autoExport)
            {
                _exportTimer += Time.deltaTime;
                if (_exportTimer >= exportInterval)
                {
                    _exportTimer = 0;
                    DoExport();
                }
            }
        }

        private void TryDock()
        {
            if (Grid == null || Grid.Body == null) return;
            // Only dock when velocity is very low.
            if (Grid.Body.linearVelocity.magnitude > 0.5f) return;

            var hits = Physics.OverlapSphere(transform.position, dockRange);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;

                // Check for base dock.
                var baseDock = col.GetComponent<BaseDock>();
                if (baseDock != null && !baseDock.isOccupied)
                {
                    DockToBase(baseDock);
                    return;
                }

                // Check for another ship's docking port.
                var otherPort = col.GetComponent<GridDockingPort>();
                if (otherPort != null && otherPort.Grid != Grid && !otherPort.IsDocked)
                {
                    DockToShip(otherPort);
                    return;
                }
            }
        }

        private void DockToBase(BaseDock dock)
        {
            IsDocked = true;
            ConnectedBaseDock = dock;
            dock.isOccupied = true;

            // Lock the grid in place.
            if (Grid.Body != null)
            {
                Grid.Body.isKinematic = true;
                Grid.Body.linearVelocity = Vector3.zero;
                Grid.Body.angularVelocity = Vector3.zero;
            }

            VoxelEngine.UI.BuildFeedbackHud.Show("Docked to Base",
                autoExport ? "Auto-export: ON" : "Auto-export: OFF",
                null, VoxelEngine.UI.UITheme.AccentCyan);
        }

        private void DockToShip(GridDockingPort other)
        {
            IsDocked = true;
            ConnectedPort = other;
            other.IsDocked = true;
            other.ConnectedPort = this;

            if (Grid.Body != null)
            {
                Grid.Body.isKinematic = true;
                Grid.Body.linearVelocity = Vector3.zero;
            }

            VoxelEngine.UI.BuildFeedbackHud.Show("Docked to Ship",
                "", null, VoxelEngine.UI.UITheme.AccentCyan);
        }

        public void Undock()
        {
            if (!IsDocked) return;

            if (ConnectedBaseDock != null)
            {
                ConnectedBaseDock.isOccupied = false;
                ConnectedBaseDock = null;
            }
            if (ConnectedPort != null)
            {
                ConnectedPort.IsDocked = false;
                ConnectedPort.ConnectedPort = null;
                ConnectedPort = null;
            }

            IsDocked = false;
            if (Grid?.Body != null)
            {
                Grid.Body.isKinematic = false;
                Grid.Body.useGravity = true;
            }

            VoxelEngine.UI.BuildFeedbackHud.Show("Undocked", "", null,
                VoxelEngine.UI.UITheme.AccentOrange);
        }

        /// <summary>Export items from ship cargo to connected base chests.</summary>
        private void DoExport()
        {
            if (Grid == null) return;

            // Find base-side item containers (chests connected via item pipes).
            IItemContainer targetContainer = null;

            if (ConnectedBaseDock != null)
            {
                // Look for chests connected via item pipes near the base dock.
                var hits = Physics.OverlapSphere(ConnectedBaseDock.transform.position, 3f);
                foreach (var col in hits)
                {
                    var chest = col.GetComponent<VoxelEngine.Building.Chest>();
                    if (chest != null && chest.container != null)
                    {
                        targetContainer = chest.container;
                        break;
                    }
                    var pipe = col.GetComponent<ItemPipe>();
                    if (pipe != null)
                    {
                        // Pipes push to chests — put items into the pipe.
                        // For now, find a chest via the pipe's push logic.
                    }
                }
            }

            if (targetContainer == null) return;

            // Export from all cargo containers on the grid.
            foreach (var kv in Grid.Blocks)
            {
                if (kv.Value is GridCargoContainer cargo && cargo.container != null)
                {
                    for (int i = 0; i < cargo.container.Size; i++)
                    {
                        var slot = cargo.container.GetSlot(i);
                        if (slot.IsEmpty) continue;

                        var leftover = targetContainer.Insert(slot.Clone());
                        if (leftover == null || leftover.count <= 0)
                            cargo.container.SetSlot(i, new ItemStack());
                        else if (leftover.count < slot.count)
                        {
                            slot.count = leftover.count;
                            cargo.container.SetSlot(i, slot);
                        }
                    }
                }
            }
        }
    }
}
