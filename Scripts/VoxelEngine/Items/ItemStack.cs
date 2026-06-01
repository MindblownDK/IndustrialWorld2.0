// Assets/Scripts/VoxelEngine/Items/ItemStack.cs
using System;
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// One slot's worth of items. For stackables: count > 1 with shared state.
    /// For tools/instance items: count == 1 with per-instance state (durability).
    ///
    /// 'item == null' represents an empty slot.
    /// </summary>
    [Serializable]
    public class ItemStack
    {
        public ItemDefinition item;
        public int            count;
        // Per-instance state (for ToolItem we store remaining durability here).
        public int            durability;

        public bool IsEmpty => item == null || count <= 0;

        public ItemStack() { }
        public ItemStack(ItemDefinition item, int count = 1)
        {
            this.item       = item;
            this.count      = count;
            this.durability = (item is ToolItem t) ? t.maxDurability : 0;
        }

        public ItemStack Clone()
        {
            return new ItemStack
            {
                item       = item,
                count      = count,
                durability = durability
            };
        }

        public static bool CanMerge(ItemStack a, ItemStack b)
        {
            if (a.IsEmpty || b.IsEmpty) return true;
            if (a.item != b.item) return false;
            if (!a.item.IsStackable) return false;
            return a.count < a.item.maxStack;
        }
    }
}
