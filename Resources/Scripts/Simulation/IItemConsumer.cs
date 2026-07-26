// Assets/Scripts/VoxelEngine/Simulation/IItemConsumer.cs
//
// Contract for any block that accepts item input (machines, storage, belts).

using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Implemented by blocks that accept items from belts, chutes, pipes, or
    /// player interaction. The conveyor system queries this to decide where
    /// to deposit items.
    /// </summary>
    public interface IItemConsumer
    {
        /// <summary>
        /// How many units of <paramref name="item"/> this consumer can currently accept.
        /// Returns 0 when full or when the item type is not accepted.
        /// </summary>
        int GetInputCapacity(ItemDefinition item);

        /// <summary>
        /// Try to insert up to <paramref name="count"/> items.
        /// Returns the number actually accepted.
        /// </summary>
        int TryInsert(ItemDefinition item, int count);
    }
}
