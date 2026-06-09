// Assets/Scripts/VoxelEngine/Storage/NetworkItemSource.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          NETWORK-AWARE ITEM CONTAINER PROXY                     ║
// ║  Wraps player inventory + ServerRack network storage.           ║
// ║  Used by the crafting system so recipes check both sources.     ║
// ║  CountOf sums inventory + network. Remove draws from inventory  ║
// ║  first, then from network. Insert always goes to inventory.     ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    /// <summary>
    /// Transparent proxy: player inventory + storage network as one IItemContainer.
    /// CountOf = inventory + network.
    /// Remove  = inventory first, then network for the remainder.
    /// Insert  = player inventory only (craft results go there).
    /// </summary>
    public class NetworkItemSource : IItemContainer
    {
        private readonly ItemContainer _inventory;
        private readonly ServerRack    _rack;

        public NetworkItemSource(ItemContainer inventory, ServerRack rack)
        {
            _inventory = inventory;
            _rack      = rack;
        }

        // ── IItemContainer ─────────────────────────────────────────
        public string Name => "Network + Inventory";

        // Forward slot access to the real inventory (used by drag-drop, not crafting).
        public IReadOnlyList<ItemStack> Slots => _inventory?.Slots ?? new ItemStack[0];

        public event Action OnChanged
        {
            add    { if (_inventory != null) _inventory.OnChanged += value; }
            remove { if (_inventory != null) _inventory.OnChanged -= value; }
        }

        public ItemStack GetSlot(int i) => _inventory?.GetSlot(i) ?? new ItemStack();
        public void SetSlot(int i, ItemStack s) => _inventory?.SetSlot(i, s);

        /// <summary>Craft results always go into the player inventory.</summary>
        public ItemStack Insert(ItemStack stack) => _inventory?.Insert(stack) ?? stack;

        /// <summary>Count = inventory count + network count.</summary>
        public int CountOf(ItemDefinition item)
        {
            if (item == null) return 0;
            return (_inventory?.CountOf(item) ?? 0)
                 + (_rack?.NetworkCount(item.itemId) ?? 0);
        }

        /// <summary>Consume from inventory first, then from the network.</summary>
        public int Remove(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            int remaining = count;

            if (_inventory != null && remaining > 0)
                remaining -= _inventory.Remove(item, remaining);

            if (_rack != null && remaining > 0 && _rack.IsOnline)
                remaining -= _rack.NetworkExtract(item.itemId, remaining);

            return count - remaining;
        }
    }
}

