// Assets/Scripts/VoxelEngine/Storage/StorageImporter.cs
//
// Auto-imports items from adjacent chests into the storage network.
// Configurable whitelist/blacklist filter. Supports speed + stack upgrades.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    
    public class StorageImporter : MonoBehaviour
    {
        [Header("Import")]
        public float baseInterval = 1f;
        public int baseStackSize = 1;

        [Header("Filter")]
        public FilterMode filterMode = FilterMode.Blacklist; // default: import everything
        public List<string> filterItemIds = new();

        [Header("Upgrades")]
        public ItemContainer upgradeSlots;
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
            CurrentStackSize = baseStackSize * (1 + stackUps * 63);

            _timer += Time.deltaTime;
            if (_timer < CurrentInterval) return;
            _timer = 0;

            DoImport();
        }

        private void DoImport()
        {
            var hits = Physics.OverlapSphere(transform.position, 2f);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                var chest = col.GetComponent<Chest>();
                if (chest?.container == null) continue;

                for (int i = 0; i < chest.container.Size; i++)
                {
                    var slot = chest.container.GetSlot(i);
                    if (slot.IsEmpty) continue;
                    if (!PassesFilter(slot.item.itemId)) continue;

                    int amount = Mathf.Min(slot.count, CurrentStackSize);
                    int leftover = ConnectedRack.NetworkInsert(slot.item, amount);
                    int accepted = amount - leftover;
                    if (accepted > 0)
                    {
                        chest.container.Remove(slot.item, accepted);
                        return; // one import per tick
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
            var racks = FindObjectsByType<ServerRack>(FindObjectsInactive.Exclude);
            ServerRack best = null; float bestD = 100f;
            foreach (var r in racks)
            {
                if (!r.IsOnline) continue;
                float d = (r.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = r; }
            }
            ConnectedRack = best;
        }
    }
}
