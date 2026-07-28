// Assets/Scripts/VoxelEngine/GridSystem/GridUIManager.cs
//
// Basic UI system for grid blocks.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public static class GridUIManager
    {
        public static void OpenCockpitUI(GridCockpit cockpit)
        {
            Debug.Log("[UI] Unified Grid cockpit opened");
        }

        public static void OpenChemicalPlantUI(GridChemicalPlant plant)
        {
            Debug.Log("[UI] Chemical Plant UI Opened");
        }

        public static void OpenRefineryUI(GridRefinery refinery)
        {
            Debug.Log("[UI] Refinery UI Opened");
        }
    }
}