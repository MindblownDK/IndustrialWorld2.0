// Assets/Scripts/VoxelEngine/GridSystem/BaseDock.cs
//
// A base-side docking station. Place on the ground near your base.
// Ships with GridDockingPort can dock here. Connect item cables/pipes
// to route cargo from ships to your storage.

using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.GridSystem
{
    [RequireComponent(typeof(PlacedBlock))]
    public class BaseDock : MonoBehaviour
    {
        [Tooltip("Display name shown in the dock UI.")]
        public string dockName = "Landing Pad";

        [HideInInspector]
        public bool isOccupied;

        /// <summary>Undock the ship currently docked here.</summary>
        public void UndockShip()
        {
            var ports = FindObjectsByType<GridDockingPort>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var port in ports)
            {
                if (port.ConnectedBaseDock == this)
                {
                    port.Undock();
                    return;
                }
            }
        }
    }
}
