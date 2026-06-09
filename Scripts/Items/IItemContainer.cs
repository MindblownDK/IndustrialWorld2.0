// Assets/Scripts/VoxelEngine/Items/IItemContainer.cs
using System;
using System.Collections.Generic;

namespace VoxelEngine.Items
{
    /// <summary>
    /// Anything that holds items in fixed-size slots: player inventory, chests,
    /// furnace input/output, assembler queue, etc.
    /// </summary>
    public interface IItemContainer
    {
        string Name { get; }
        IReadOnlyList<ItemStack> Slots { get; }

        event Action OnChanged;

        /// <summary>Insert as many of 'stack' as fit. Returns leftover (or null if all fit).</summary>
        ItemStack Insert(ItemStack stack);

        /// <summary>Remove exactly N items of 'item' from container. Returns how many actually removed.</summary>
        int Remove(ItemDefinition item, int count);

        /// <summary>Total count of 'item' across all slots.</summary>
        int CountOf(ItemDefinition item);

        /// <summary>Replace slot at index. Used by drag-and-drop UI.</summary>
        void SetSlot(int index, ItemStack stack);

        /// <summary>Get slot at index (read-only convenience).</summary>
        ItemStack GetSlot(int index);
    }
}
