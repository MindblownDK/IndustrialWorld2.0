// Assets/Scripts/VoxelEngine/Simulation/IItemProvider.cs
//
// Contract for any block that outputs items (machines, storage, belts).

using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Implemented by blocks that expose items for extraction by belts, chutes,
    /// or other consumers. The conveyor system queries this to pick up items.
    /// </summary>
    public interface IItemProvider
    {
        /// <summary>
        /// Peek at the next available item and count without removing it.
        /// Returns null when nothing is available.
        /// </summary>
        ItemDefinition PeekOutput(out int count);

        /// <summary>
        /// Extract up to <paramref name="count"/> items of the given type.
        /// Returns the number actually removed.
        /// </summary>
        int TryExtract(ItemDefinition item, int count);
    }
}
