// Assets/Scripts/VoxelEngine/GridSystem/GridCockpit.cs
//
// Cockpit with terminal + grid size switching.

using UnityEngine;
using VoxelEngine.Settings;

namespace VoxelEngine.GridSystem
{
    public class GridCockpit : GridBlock
    {
        [Header("Cockpit")]
        public Player.PlayerController Pilot { get; private set; }

        /// <summary>The cockpit the local player is currently seated in (null if on foot).</summary>
        public static GridCockpit ActivePilotSeat { get; private set; }

        private void Update()
        {
            if (Pilot != null)
            {
                if (GameSettings.WasPressed(InputAction.ExitCockpit))
                {
                    Exit();
                }
            }
        }

        public void Enter(Player.PlayerController player)
        {
            if (Pilot != null) return;

            Pilot = player;
            player.enabled = false;
            // The player uses a CharacterController (not a Rigidbody) — disable it
            // so the seated player doesn't collide while piloting. Guard both so a
            // different player rig never throws.
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            // Parent the player to the cockpit so they ride along with the grid as
            // it moves/rolls/rotates (fixes "ship rolls away, player stays put").
            _originalParent = player.transform.parent;
            player.transform.SetParent(transform, worldPositionStays: true);
            player.transform.position = transform.position;
            player.transform.localRotation = Quaternion.identity;

            // Take control of the grid for flight + mark this as the active seat so
            // the "I" key opens the master terminal instead of the player inventory.
            if (Grid != null) Grid.ActiveCockpit = this;
            ActivePilotSeat = this;
        }

        private Transform _originalParent;

        /// <summary>Open the Space-Engineers-style master terminal for this grid.</summary>
        public void OpenTerminal()
        {
            VoxelEngine.UI.GameUIController.Instance?.OpenGridTerminal(Grid);
        }

        public void Exit()
        {
            if (Pilot == null) return;

            // Unparent the player from the grid and drop them beside the cockpit.
            Pilot.transform.SetParent(_originalParent, worldPositionStays: true);
            Pilot.transform.position = transform.position + transform.up * 1.2f + transform.right * 1.5f;

            Pilot.enabled = true;
            var cc = Pilot.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
            var rb = Pilot.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            Pilot = null;
            if (Grid != null && Grid.ActiveCockpit == this) Grid.ActiveCockpit = null;
            if (ActivePilotSeat == this) ActivePilotSeat = null;
            VoxelEngine.UI.GameUIController.Instance?.CloseAll();
        }

        public void SwitchToSmallGrid()
        {
            GridSizeSwitcher.Instance?.SwitchGrid(Grid, GridSize.Small);
        }

        public void SwitchToLargeGrid()
        {
            GridSizeSwitcher.Instance?.SwitchGrid(Grid, GridSize.Large);
        }
    }
}