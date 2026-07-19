// Assets/Scripts/VoxelEngine/GridSystem/GridSizeSwitcher.cs
//
// Compatibility shim for scenes that still reference the retired grid-type
// switcher. Unified constructs accept both detail and structural block scales.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridSizeSwitcher : MonoBehaviour
    {
        public static GridSizeSwitcher Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) Destroy(gameObject);
            else Instance = this;
        }

        /// <summary>
        /// Retained for serialized UnityEvent compatibility. Grid conversion is no
        /// longer required because both authored block scales share one construct.
        /// </summary>
        public void SwitchGrid(GridEntity grid, GridSize targetSize)
        {
            if (grid == null) return;
            Debug.Log("[UnifiedGrid] Grid-type switching is retired; detail and structural blocks already share this grid.");
        }
    }
}
