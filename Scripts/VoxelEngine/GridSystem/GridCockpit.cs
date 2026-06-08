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
            player.GetComponent<Rigidbody>().isKinematic = true;

            // Open the Grid Terminal
            if (GridTerminalUI.Instance != null)
                GridTerminalUI.Instance.Open(Grid);
        }

        public void Exit()
        {
            if (Pilot == null) return;

            Pilot.enabled = true;
            Pilot.GetComponent<Rigidbody>().isKinematic = false;
            Pilot = null;

            if (GridTerminalUI.Instance != null)
                GridTerminalUI.Instance.Close();
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