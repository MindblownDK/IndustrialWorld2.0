// Assets/Scripts/VoxelEngine/Storage/StorageDisk.cs
//
// Digital storage disk. Stores items as data (no physical slots).
// Tiers: 1K, 4K, 16K, 64K, 90K items.

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    public enum DiskTier { Disk1K = 1000, Disk4K = 4000, Disk16K = 16000, Disk64K = 64000, Disk90K = 90000 }

    /// <summary>
    /// A storage disk that holds items digitally. Inserted into a ServerRack.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Storage/Storage Disk", fileName = "Disk_New")]
    public class StorageDisk : ItemDefinition
    {
        [Header("Disk")]
        public DiskTier tier = DiskTier.Disk1K;
        public int MaxItems => (int)tier;

        public StorageDisk() { maxStack = 1; category = "Storage"; }
    }

    /// <summary>Runtime disk data — holds the actual stored items.</summary>
    [Serializable]
    public class DiskData
    {
        public DiskTier tier;
        public int totalStored;
        public List<StoredItemEntry> items = new();

        public int Capacity => (int)tier;
        public int FreeSpace => Capacity - totalStored;

        /// <summary>Insert items. Returns how many were accepted.</summary>
        public int Insert(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            int accept = Mathf.Min(count, FreeSpace);
            if (accept <= 0) return 0;

            // Find existing entry for this item.
            foreach (var e in items)
            {
                if (e.itemId == item.itemId)
                {
                    e.count += accept;
                    totalStored += accept;
                    return accept;
                }
            }
            // New entry.
            items.Add(new StoredItemEntry { itemId = item.itemId, displayName = item.displayName, count = accept });
            totalStored += accept;
            return accept;
        }

        /// <summary>Extract items. Returns how many were extracted.</summary>
        public int Extract(string itemId, int count)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].itemId == itemId)
                {
                    int take = Mathf.Min(count, items[i].count);
                    items[i].count -= take;
                    totalStored -= take;
                    if (items[i].count <= 0) items.RemoveAt(i);
                    return take;
                }
            }
            return 0;
        }

        public int CountOf(string itemId)
        {
            foreach (var e in items)
                if (e.itemId == itemId) return e.count;
            return 0;
        }
    }

    [Serializable]
    public class StoredItemEntry
    {
        public string itemId;
        public string displayName;
        public int count;
    }
}
