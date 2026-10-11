// Assets/Scripts/VoxelEngine/Storage/CompositeItemSource.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          COMPOSITE ITEM CONTAINER PROXY (17.17.2-dev)             ║
// ║  Wraps player inventory + an optional OPEN container + an        ║
// ║  optional ServerRack network as ONE crafting source.             ║
// ║  CountOf sums every layer. Remove draws inventory first, then   ║
// ║  the open container, then the network. Insert always goes to    ║
// ║  the inventory: craft results belong to the player.             ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    /// <summary>
    /// Transparent proxy over up to three item layers: the player inventory, the
    /// container whose panel is currently open (chest, drawer, loot bag, pen, pad -
    /// whatever the player is standing at and looking into), and the storage
    /// network behind a server rack. Crafting reads all of them; results still
    /// land in the inventory only.
    /// </summary>
    public class CompositeItemSource : IItemContainer
    {
        private readonly ItemContainer   _inventory;
        private readonly ServerRack      _rack;      // may be null
        private readonly IItemContainer  _open;      // may be null

        public CompositeItemSource(ItemContainer inventory, ServerRack rack, IItemContainer openContainer)
        {
            _inventory = inventory;
            _rack      = rack;
            // Never let the inventory appear twice through the open-container slot.
            _open      = openContainer != null && !ReferenceEquals(openContainer, inventory)
                ? openContainer
                : null;
        }

        // ── IItemContainer ─────────────────────────────────────────
        public string Name => "Inventory + Open Container + Network";

        // Forward slot access to the real inventory (used by drag-drop, not crafting).
        public IReadOnlyList<ItemStack> Slots => _inventory?.Slots ?? new ItemStack[0];

        public event Action OnChanged
        {
            add
            {
                if (_inventory != null) _inventory.OnChanged += value;
                if (_open != null) _open.OnChanged += value;
            }
            remove
            {
                if (_inventory != null) _inventory.OnChanged -= value;
                if (_open != null) _open.OnChanged -= value;
            }
        }

        public ItemStack GetSlot(int i) => _inventory?.GetSlot(i) ?? new ItemStack();
        public void SetSlot(int i, ItemStack s) => _inventory?.SetSlot(i, s);

        /// <summary>Craft results always go into the player inventory.</summary>
        public ItemStack Insert(ItemStack stack) => _inventory?.Insert(stack) ?? stack;

        /// <summary>Count = inventory + open container + network.</summary>
        public int CountOf(ItemDefinition item)
        {
            if (item == null) return 0;
            return (_inventory?.CountOf(item) ?? 0)
                 + (_open?.CountOf(item) ?? 0)
                 + (_rack?.NetworkCount(item.itemId) ?? 0);
        }

        public int CountOfId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;
            return (_inventory?.CountOfId(itemId) ?? 0)
                 + CountOfIdInOpen(itemId)
                 + (_rack?.NetworkCount(itemId) ?? 0);
        }
        public int RemoveId(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId) || count <= 0) return 0;
            int remaining = count;
            if (_inventory != null && remaining > 0)
                remaining -= _inventory.RemoveId(itemId, remaining);
            if (_open != null && remaining > 0)
                remaining -= RemoveIdFromOpen(itemId, remaining);
            if (_rack != null && remaining > 0 && _rack.IsOnline)
                remaining -= _rack.NetworkExtract(itemId, remaining);
            return count - remaining;
        }

        /// <summary>Consume from inventory first, then the open container, then the
        /// network. Each layer removes real items, so no path can over-consume.</summary>
        public int Remove(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            int remaining = count;

            if (_inventory != null && remaining > 0)
                remaining -= _inventory.Remove(item, remaining);
            if (_open != null && remaining > 0)
                remaining -= _open.Remove(item, remaining);
            if (_rack != null && remaining > 0 && _rack.IsOnline)
                remaining -= _rack.NetworkExtract(item.itemId, remaining);

            return count - remaining;
        }

        // ── open-container id helpers ──────────────────────────────
        // The open layer is an interface: ItemContainers get their native id scan,
        // anything else is walked slot by slot.
        private int CountOfIdInOpen(string itemId)
        {
            if (_open == null) return 0;
            if (_open is ItemContainer box) return box.CountOfId(itemId);
            int total = 0;
            var slots = _open.Slots;
            if (slots == null) return 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var stack = slots[i];
                if (stack == null || stack.IsEmpty || stack.item == null) continue;
                if (string.Equals(stack.item.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                    total += stack.count;
            }
            return total;
        }

        private int RemoveIdFromOpen(string itemId, int count)
        {
            if (_open == null) return 0;
            if (_open is ItemContainer box) return box.RemoveId(itemId, count);
            int removed = 0;
            var slots = _open.Slots;
            if (slots == null) return 0;
            for (int i = 0; i < slots.Count && removed < count; i++)
            {
                var stack = _open.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.item == null) continue;
                if (!string.Equals(stack.item.itemId, itemId, StringComparison.OrdinalIgnoreCase)) continue;
                int take = Mathf.Min(stack.count, count - removed);
                if (take <= 0) continue;
                if (take >= stack.count) _open.SetSlot(i, new ItemStack());
                else _open.SetSlot(i, new ItemStack(stack.item, stack.count - take));
                removed += take;
            }
            return removed;
        }
    }
}
