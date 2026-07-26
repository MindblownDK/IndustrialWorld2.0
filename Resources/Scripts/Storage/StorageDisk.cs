// Assets/Scripts/VoxelEngine/Storage/StorageDisk.cs
//
// Matter-conversion storage disk. Stored items are converted into indexed
// matter data; heavier materials consume more GB per unit than light materials.
// Tiers: 1K, 4K, 16K, 64K, 90K GB.

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
        public int MaxGigabytes => (int)tier;

        public StorageDisk()
        {
            maxStack = 1;
            category = "Storage";
            EnsureMatterDescription();
        }

        private void OnEnable() => EnsureMatterDescription();
#if UNITY_EDITOR
        private void OnValidate() => EnsureMatterDescription();
#endif

        private void EnsureMatterDescription()
        {
            if (string.IsNullOrWhiteSpace(description)
                || description.IndexOf("Holds up to", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                description = "Matter-conversion storage disk. Items are encoded as stable matter data; heavier materials consume more GB per unit.";
            }
        }
    }

    /// <summary>Runtime disk data — holds the actual stored items.</summary>
    [Serializable]
    public class DiskData
    {
        public DiskTier tier;
        public int totalStored;
        public List<StoredItemEntry> items = new();

        public int Capacity => (int)tier;
        public float UsedGigabytes
        {
            get
            {
                float gb = 0f;
                if (items == null) return 0f;
                foreach (var e in items)
                    if (e != null) gb += Mathf.Max(0.001f, e.massPerUnit <= 0f ? 1f : e.massPerUnit) * Mathf.Max(0, e.count);
                return gb;
            }
        }
        public int FreeSpace => Mathf.Max(0, Mathf.FloorToInt(Capacity - UsedGigabytes));

        /// <summary>Insert items. Returns how many were accepted.</summary>
        public int Insert(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            float gbPerUnit = Mathf.Max(0.001f, item.massPerUnit);
            int accept = Mathf.Min(count, Mathf.FloorToInt(FreeSpace / gbPerUnit));
            if (accept <= 0) return 0;

            // Find existing entry for this item.
            foreach (var e in items)
            {
                if (e.itemId == item.itemId)
                {
                    e.count += accept;
                    e.massPerUnit = gbPerUnit;
                    totalStored += accept;
                    return accept;
                }
            }
            // New entry.
            items.Add(new StoredItemEntry { itemId = item.itemId, displayName = item.displayName, count = accept, massPerUnit = gbPerUnit });
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
        public float massPerUnit = 1f;
    }
}
