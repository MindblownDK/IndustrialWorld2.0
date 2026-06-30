// Assets/Scripts/VoxelEngine/Building/IPlacedBlockPayloadReceiver.cs
using VoxelEngine.Items;

namespace VoxelEngine.Building
{
    /// <summary>Implemented by placed blocks that restore instance state from an item payload.</summary>
    public interface IPlacedBlockPayloadReceiver
    {
        void ApplyPlacedPayload(ItemStack sourceStack);
    }

    /// <summary>Implemented by placed blocks that need custom item stacks when broken.</summary>
    public interface ICustomBlockDrop
    {
        ItemStack CreateBlockDrop(BlockItem originalItem);
    }
}
