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

            // Park the player at the cockpit so they visually sit in the seat.
            player.transform.position = transform.position;

            // Take control of the grid for flight + mark this as the active seat so
            // the "I" key opens the master terminal instead of the player inventory.
            if (Grid != null) Grid.ActiveCockpit = this;
            ActivePilotSeat = this;
        }

        /// <summary>Open the Space-Engineers-style master terminal for this grid.</summary>
        public void OpenTerminal()
        {
            VoxelEngine.UI.GameUIController.Instance?.OpenGridTerminal(Grid);
        }

        public void Exit()
        {
            if (Pilot == null) return;

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