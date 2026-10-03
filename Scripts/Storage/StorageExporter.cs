// Assets/Scripts/VoxelEngine/Storage/StorageExporter.cs
//
// Auto-exports items from the storage network to adjacent chests.
// Configurable whitelist/blacklist filter. Supports speed + stack upgrades.
//
// 14.44.0 - the exporter learns to keep shelves stocked:
//   • third upgrade slot takes the CRAFTING CARD: when the network cannot
//     supply a whitelisted item the exporter wants to move, the shortfall
//     is ordered from the Server Controller's auto-crafter (pattern
//     required, merge-guarded so a ticking exporter never inflates jobs)
//   • KEEP STOCKED target: with a target set, the exporter fills each
//     adjacent container up to that many of every whitelisted item and
//     then idles until the stock drops (0 = fill forever, legacy rule)

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    public enum FilterMode { Whitelist, Blacklist }

    [RequireComponent(typeof(PlacedBlock))]

    public class StorageExporter : MonoBehaviour
    {
        /// <summary>Stable id of the Crafting Card upgrade (setup step 112).</summary>
        public const string CraftingCardItemId = "crafting_card";

        [Header("Export")]
        public float baseInterval = 1f; // seconds between exports
        public int baseStackSize = 1;   // items per export

        [Header("Filter")]
        public FilterMode filterMode = FilterMode.Whitelist;
        public List<string> filterItemIds = new();

        [Header("Keep Stocked (14.44.0)")]
        [Tooltip("With a target set, each adjacent container is filled up to this many of every whitelisted item, then the exporter idles. 0 = fill forever.")]
        public int stockTarget = 0;

        [Header("Upgrades")]
        public ItemContainer upgradeSlots; // 3 slots: speed + stack + crafting card
        public int maxSpeedUpgrades = 4;
        public int maxStackUpgrades = 1;

        public float CurrentInterval { get; private set; }
        public int CurrentStackSize { get; private set; }
        public bool HasCraftingCard { get; private set; }
        public ServerRack ConnectedRack { get; private set; }

        private AutoCrafter _crafter;
        private float _timer, _searchTimer;

        private void Awake() => EnsureContainers();

        public void EnsureContainers()
        {
            // 14.44.0: grown from 2 to 3 slots (crafting card). Resize never
            // shrinks, and old two-slot saves restore into the first two.
            if (upgradeSlots == null) upgradeSlots = new ItemContainer("Upgrades", 3);
            else upgradeSlots.Resize(3);
        }

        private void Update()
        {
            _searchTimer += Time.deltaTime;
            if (_searchTimer >= 2f) { _searchTimer = 0; FindRack(); }

            if (ConnectedRack == null || !ConnectedRack.IsOnline) return;

            // Calculate effective rates from upgrades.
            int speedUps = 0, stackUps = 0;
            bool card = false;
            for (int i = 0; i < upgradeSlots.Size; i++)
            {
                var s = upgradeSlots.GetSlot(i);
                if (s.IsEmpty) continue;
                if (s.item.itemId.Contains("speed")) speedUps += s.count;
                if (s.item.itemId.Contains("stack")) stackUps += s.count;
                if (s.item.itemId == CraftingCardItemId) card = true;
            }
            speedUps = Mathf.Min(speedUps, maxSpeedUpgrades);
            stackUps = Mathf.Min(stackUps, maxStackUpgrades);
            CurrentInterval = baseInterval / (1 + speedUps);
            CurrentStackSize = baseStackSize * (1 + stackUps * 63); // 1 stack = 64 items
            HasCraftingCard = card;

            _timer += Time.deltaTime;
            if (_timer < CurrentInterval) return;
            _timer = 0;

            DoExport();
        }

        private void DoExport()
        {
            // Destination containers: adjacent chests (legacy probe).
            var destinations = CollectDestinations();
            if (destinations.Count == 0) return;

            // Whitelist mode walks the FILTER list, not the network listing -
            // an item at zero network stock must still be seen, or the
            // Crafting Card could never order it crafted.
            if (filterMode == FilterMode.Whitelist && filterItemIds.Count > 0)
            {
                ExportWhitelist(destinations);
                return;
            }

            // Blacklist / unfiltered path: legacy behavior, network-driven.
            var allItems = ConnectedRack.GetAllItems();
            foreach (var entry in allItems)
            {
                if (!PassesFilter(entry.itemId)) continue;
                if (entry.count <= 0) continue;

                var itemDef = FindItemDef(entry.itemId);
                if (itemDef == null) continue;

                int amount = Mathf.Min(entry.count, CurrentStackSize);
                if (TryDeliver(itemDef, amount, destinations)) return; // one export per tick
            }
        }

        private void ExportWhitelist(List<ItemContainer> destinations)
        {
            int craftOrders = 0;
            foreach (var id in filterItemIds)
            {
                var itemDef = FindItemDef(id);
                if (itemDef == null) continue;

                // How much do the shelves still want?
                int wanted = 0;
                foreach (var dest in destinations)
                {
                    if (stockTarget > 0)
                    {
                        int room = stockTarget - dest.CountOf(itemDef);
                        if (room > 0) wanted = Mathf.Max(wanted, Mathf.Min(room, CurrentStackSize));
                    }
                    else wanted = CurrentStackSize;
                }
                if (wanted <= 0) continue;   // stocked up - idle on this item

                int net = ConnectedRack.NetworkCount(id);
                int move = Mathf.Min(wanted, net);
                bool delivered = move > 0 && TryDeliver(itemDef, move, destinations);

                // Crafting Card: the network came up short - order the
                // shortfall from the auto-crafter (pattern + merge guard
                // live in TryRequestAutomationCraft).
                if (HasCraftingCard && net < wanted && craftOrders < 4)
                {
                    var crafter = ConnectedCrafter();
                    if (crafter != null && crafter.TryRequestAutomationCraft(id, wanted - net))
                        craftOrders++;
                }

                if (delivered) return; // one export per tick, like always
            }
        }

        /// <summary>Deliver into the first destination with room. Extracts
        /// from the network FIRST and refunds whatever the destination
        /// refuses, so neither side can ever be double-counted.</summary>
        private bool TryDeliver(ItemDefinition itemDef, int amount, List<ItemContainer> destinations)
        {
            if (itemDef == null || amount <= 0) return false;
            foreach (var dest in destinations)
            {
                int room = stockTarget > 0
                    ? Mathf.Min(amount, stockTarget - dest.CountOf(itemDef))
                    : amount;
                if (room <= 0) continue;

                int got = ConnectedRack.NetworkExtract(itemDef.itemId, room);
                if (got <= 0) return false;

                var leftover = dest.Insert(new ItemStack(itemDef, got));
                int refused = leftover != null && !leftover.IsEmpty ? leftover.count : 0;
                if (refused > 0) ConnectedRack.NetworkInsert(itemDef, refused);
                if (got - refused > 0) return true;
            }
            return false;
        }

        private List<ItemContainer> CollectDestinations()
        {
            var list = new List<ItemContainer>();
            var hits = Physics.OverlapSphere(transform.position, 2f);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                var chest = col.GetComponent<Chest>();
                if (chest?.container != null && !list.Contains(chest.container))
                    list.Add(chest.container);
            }
            return list;
        }

        public AutoCrafter ConnectedCrafter()
        {
            if (ConnectedRack == null) return null;
            if (_crafter == null || _crafter.gameObject != ConnectedRack.gameObject)
                _crafter = ConnectedRack.GetComponent<AutoCrafter>();
            return _crafter;
        }

        private bool PassesFilter(string itemId)
        {
            if (filterItemIds.Count == 0) return filterMode == FilterMode.Blacklist;
            bool inList = filterItemIds.Contains(itemId);
            return filterMode == FilterMode.Whitelist ? inList : !inList;
        }

        private void FindRack()
        {
            // 14.40.0: piped or touching - network membership, never radius.
            ConnectedRack = StorageNetwork.ControllerOf(this);
        }

        private static ItemDefinition FindItemDef(string id)
        {
            var all = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            foreach (var it in all) if (it.itemId == id) return it;
            return null;
        }
    }
}
