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

        /// <summary>
        /// Per-instance state object for items that carry their own runtime data
        /// (e.g. a StorageDisk carries a DiskData of its stored items). Survives
        /// being picked up / dropped / moved between containers and disables stack
        /// merging with stacks of a different payload.
        /// Use any reference type — keep it small and serialisation-safe.
        /// </summary>
        [System.NonSerialized] public object payload;

        public bool IsEmpty => item == null || count <= 0;

        public ItemStack() { }
        public ItemStack(ItemDefinition item, int count = 1)
        {
            this.item       = item;
            this.count      = count;
            // Tools track wear; jetpacks track fuel/charge (0..fuelCapacity).
            this.durability = item is ToolItem t ? t.maxDurability
                            : item is JetpackItem j ? j.FuelCapacity
                            : 0;
        }

        public ItemStack Clone()
        {
            return new ItemStack
            {
                item       = item,
                count      = count,
                durability = durability,
                payload    = payload,        // share reference — the payload object IS the state
            };
        }

        public static int MaxItemsPerStack(ItemDefinition item)
        {
            if (item == null) return 0;
            return item.IsStackable ? ItemContainer.DefaultMaxItemsPerStack : 1;
        }

        public static bool CanMerge(ItemStack a, ItemStack b)
        {
            if (a.IsEmpty || b.IsEmpty) return true;
            if (a.item != b.item) return false;
            if (!a.item.IsStackable) return false;
            if (a.count >= MaxItemsPerStack(a.item)) return false;
            // Never merge two payload-bearing stacks — each instance is unique.
            if (a.payload != null || b.payload != null) return false;
            return true;
        }
    }
}

