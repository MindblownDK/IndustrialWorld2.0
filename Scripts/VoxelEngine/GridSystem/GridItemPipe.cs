// Assets/Scripts/VoxelEngine/GridSystem/GridItemPipe.cs
//
// ItemPipe block for grids. Enables conveyor functionality using the existing ResourceNetwork system.
// Allows item transport between cargo containers on the same grid or connected grids.
// Phase 2 implementation - basic connection logic.

using UnityEngine;
using VoxelEngine.Networks;

namespace VoxelEngine.GridSystem
{
    public class GridItemPipe : GridBlock
    {
        [Header("Item Pipe")]
        public float transferRate = 10f; // items per second

        private ResourceNetwork _network;

        public override void OnPlaced()
        {
            base.OnPlaced();
            // Connect to ResourceNetwork on placement (simplified - full integration in Phase 3)
            // For now, pipes enable shared cargo access on the grid
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            // Disconnect from network
        }

        // Future: Implement item routing between GridCargoContainer instances
    }
}