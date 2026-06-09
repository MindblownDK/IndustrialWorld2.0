// Assets/Scripts/VoxelEngine/GridSystem/IGridItemStore.cs
//
// Anything on a grid that exposes a shared item inventory to the grid item
// network — cargo containers, docking ports, etc. Lets the master terminal and
// item pipes treat the whole ship as one storage system.

using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public interface IGridItemStore
    {
        /// <summary>The item container this block contributes to the network.</summary>
        ItemContainer ItemStore { get; }

        /// <summary>Human-readable label for the master terminal.</summary>
        string StoreLabel { get; }
    }
}
