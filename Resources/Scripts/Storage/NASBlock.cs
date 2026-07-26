// Assets/Scripts/VoxelEngine/Storage/NASBlock.cs
//
// Network Attached Storage — holds 10 storage disks.
// Connects to the ServerRack via data cables to expand storage capacity.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class NASBlock : MonoBehaviour
    {
        [Header("Storage")]
        public ItemContainer diskSlots; // 10 disk slots

        public System.Collections.Generic.List<DiskData> activeDisks = new();

        public int TotalStored { get; private set; }
        public int TotalCapacity { get; private set; }

        private float _timer;

        private void Awake()
        {
            if (diskSlots == null) diskSlots = new ItemContainer("NAS Disks", 10);
            else diskSlots.Resize(10);
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < 1f) return;
            _timer = 0;
            SyncDisks();
        }

        private void SyncDisks()
        {
            while (activeDisks.Count < diskSlots.Size) activeDisks.Add(null);
            float usedGb = 0f;
            TotalStored = 0; TotalCapacity = 0;

            for (int i = 0; i < diskSlots.Size; i++)
            {
                var slot = diskSlots.GetSlot(i);
                if (slot.IsEmpty || !(slot.item is StorageDisk sd))
                { activeDisks[i] = null; continue; }

                var data = slot.payload as DiskData;
                if (data == null || data.tier != sd.tier)
                {
                    data = activeDisks[i] != null && activeDisks[i].tier == sd.tier
                        ? activeDisks[i]
                        : new DiskData { tier = sd.tier };
                    slot.payload = data;
                    diskSlots.SetSlot(i, slot);
                }
                activeDisks[i] = data;

                usedGb += activeDisks[i].UsedGigabytes;
                TotalCapacity += activeDisks[i].Capacity;
            }
            TotalStored = Mathf.CeilToInt(usedGb);
        }

        /// <summary>Called by ServerRack to include this NAS's disks in the network.</summary>
        public System.Collections.Generic.List<DiskData> GetActiveDisks() => activeDisks;
    }
}
