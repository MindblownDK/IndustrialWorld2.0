// Assets/Scripts/VoxelEngine/GridSystem/GridCockpit.cs
//
// Fully functional cockpit with UI integration.

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

            // Open cockpit UI
            GridUIManager.OpenCockpitUI(this);

            Debug.Log("[Cockpit] Player entered cockpit");
        }

        public void Exit()
        {
            if (Pilot == null) return;

            Pilot.enabled = true;
            Pilot.GetComponent<Rigidbody>().isKinematic = false;
            Pilot = null;

            Debug.Log("[Cockpit] Player exited cockpit");
        }
    }
}