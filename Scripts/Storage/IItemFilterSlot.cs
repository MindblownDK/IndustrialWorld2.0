// Assets/Scripts/VoxelEngine/Storage/IItemFilterSlot.cs
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    /// <summary>A UI-only slot that records an item definition without consuming the dragged stack.</summary>
    public interface IItemFilterSlot : IItemContainer
    {
        void ApplyFilter(ItemDefinition item);
    }
}
