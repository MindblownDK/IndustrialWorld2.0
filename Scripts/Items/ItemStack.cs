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
        // For jetpacks this is the PRIMARY fuel pool: hydrogen (ml) on packs that
        // burn H₂, or power (Wh) on pure-power packs. Hybrid packs also carry a
        // secondary power pool in <see cref="charge"/>.
        public int            durability;
        // Secondary per-instance pool (additive / save-compatible). Hybrid jetpacks
        // store their power charge (Wh) here so the power side is tracked
        // independently from the hydrogen tank. 0 on legacy stacks → refills
        // from portable batteries / charged cells exactly like an empty pack.
        public int            charge;

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
            // Tools track wear. Jetpacks start on EMPTY tanks — fuel/charge must
            // come from real sources (H₂ tanks, batteries, cells, world docks)
            // instead of materialising a full pack out of thin air.
            // 9.16.0 — liquid canisters also start EMPTY (durability stores millilitres).
            this.durability = item is LiquidCanister ? 0 : (item is ToolItem t ? t.maxDurability : 0);
            this.charge     = 0;
        }

        public ItemStack Clone()
        {
            return new ItemStack
            {
                item       = item,
                count      = count,
                durability = durability,
                charge     = charge,
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

