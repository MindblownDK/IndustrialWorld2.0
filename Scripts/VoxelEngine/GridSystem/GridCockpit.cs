// Assets/Scripts/VoxelEngine/GridSystem/GridCockpit.cs
//
// Cockpit with grid size switching buttons.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridCockpit : GridBlock
    {
        [Header("Cockpit")]
        public Player.PlayerController Pilot { get; private set; }

        public void Enter(Player.PlayerController player)
        {
            if (Pilot != null) return;

            Pilot = player;
            player.enabled = false;
            player.GetComponent<Rigidbody>().isKinematic = true;

            GridUIManager.OpenCockpitUI(this);
        }

        public void Exit()
        {
            if (Pilot == null) return;

            Pilot.enabled = true;
            Pilot.GetComponent<Rigidbody>().isKinematic = false;
            Pilot = null;
        }

        // Called from cockpit UI buttons
        public void SwitchToSmallGrid()
        {
            if (Grid != null && GridSizeSwitcher.Instance != null)
                GridSizeSwitcher.Instance.SwitchGrid(Grid, GridSize.Small);
        }

        public void SwitchToLargeGrid()
        {
            if (Grid != null && GridSizeSwitcher.Instance != null)
                GridSizeSwitcher.Instance.SwitchGrid(Grid, GridSize.Large);
        }
    }
}