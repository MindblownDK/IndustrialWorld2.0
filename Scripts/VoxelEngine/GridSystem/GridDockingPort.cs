// Assets/Scripts/VoxelEngine/GridSystem/GridDockingPort.cs
//
// Docking connector with filter support.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridDockingPort : GridBlock
    {
        [Header("Docking Port")]
        public bool autoExport = true;
        public bool usePortConfigFilter = true;

        public bool IsDocked { get; private set; }
        public BaseDock ConnectedBaseDock { get; private set; }

        public void Connect(GridDockingPort other)
        {
            IsDocked = true;
        }

        public void Disconnect()
        {
            IsDocked = false;
            ConnectedBaseDock = null;
        }

        public void Undock()
        {
            Disconnect();
        }
    }
}