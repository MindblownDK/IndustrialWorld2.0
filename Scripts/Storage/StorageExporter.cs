// Assets/Scripts/VoxelEngine/Storage/StorageExporter.cs
//
// Auto-exports items from the storage network to adjacent chests/pipes.
// Configurable whitelist/blacklist filter. Supports speed + stack upgrades.

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
        [Header("Export")]
        public float baseInterval = 1f; // seconds between exports
        public int baseStackSize = 1;   // items per export

        [Header("Filter")]
        public FilterMode filterMode = FilterMode.Whitelist;
        public List<string> filterItemIds = new();

        [Header("Upgrades")]
        public ItemContainer upgradeSlots; // 2 slots: speed + stack
        public int maxSpeedUpgrades = 4;
        public int maxStackUpgrades = 1;

        public float CurrentInterval { get; private set; }
        public int CurrentStackSize { get; private set; }
        public ServerRack ConnectedRack { get; private set; }

        private float _timer, _searchTimer;

        private void Awake() => EnsureContainers();

        public void EnsureContainers()
        {
            if (upgradeSlots == null) upgradeSlots = new ItemContainer("Upgrades", 2);
            else upgradeSlots.Resize(2);
        }

        private void Update()
        {
            _searchTimer += Time.deltaTime;
            if (_searchTimer >= 2f) { _searchTimer = 0; FindRack(); }

            
            if (ConnectedRack == null || !ConnectedRack.IsOnline) return;

            // Calculate effective rates from upgrades.
            int speedUps = 0, stackUps = 0;
            for (int i = 0; i < upgradeSlots.Size; i++)
            {
                var s = upgradeSlots.GetSlot(i);
                if (s.IsEmpty) continue;
                if (s.item.itemId.Contains("speed")) speedUps += s.count;
                if (s.item.itemId.Contains("stack")) stackUps += s.count;
            }
            speedUps = Mathf.Min(speedUps, maxSpeedUpgrades);
            stackUps = Mathf.Min(stackUps, maxStackUpgrades);
            CurrentInterval = baseInterval / (1 + speedUps);
            CurrentStackSize = baseStackSize * (1 + stackUps * 63); // 1 stack = 64 items

            _timer += Time.deltaTime;
            if (_timer < CurrentInterval) return;
            _timer = 0;

            DoExport();
        }

        private void DoExport()
        {
            var allItems = ConnectedRack.GetAllItems();
            foreach (var entry in allItems)
            {
                if (!PassesFilter(entry.itemId)) continue;
                if (entry.count <= 0) continue;

                int amount = Mathf.Min(entry.count, CurrentStackSize);

                // Try to push to adjacent chests.
                var hits = Physics.OverlapSphere(transform.position, 2f);
                foreach (var col in hits)
                {
                    if (col.gameObject == gameObject) continue;
                    var chest = col.GetComponent<Chest>();
                    if (chest?.container == null) continue;

                    // Find the item definition to create a stack.
                    var itemDef = FindItemDef(entry.itemId);
                    if (itemDef == null) continue;

                    var leftover = chest.container.Insert(new ItemStack(itemDef, amount));
                    int accepted = amount - (leftover?.count ?? 0);
                    if (accepted > 0)
                    {
                        ConnectedRack.NetworkExtract(entry.itemId, accepted);
                        return; // one export per tick
                    }
                }
            }
        }

        private bool PassesFilter(string itemId)
        {
            if (filterItemIds.Count == 0) return filterMode == FilterMode.Blacklist;
            bool inList = filterItemIds.Contains(itemId);
            return filterMode == FilterMode.Whitelist ? inList : !inList;
        }

        private void FindRack()
        {
            var racks = FindObjectsByType<ServerRack>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            ServerRack best = null; float bestD = 100f;
            foreach (var r in racks)
            {
                if (!r.IsOnline) continue;
                float d = (r.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = r; }
            }
            ConnectedRack = best;
        }

        private static ItemDefinition FindItemDef(string id)
        {
            var all = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            foreach (var it in all) if (it.itemId == id) return it;
            return null;
        }
    }
}
