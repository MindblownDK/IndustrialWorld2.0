// Assets/Scripts/VoxelEngine/Storage/NASBlock.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║                      NAS SHELF (14.40.0)                         ║
// ║  THE home of storage disks - the Server Controller holds none.  ║
// ║  8 drive bays, disks only (anything else is ejected). Carries   ║
// ║  a player-set PRIORITY: higher shelves fill first on insert     ║
// ║  and drain first on extract. Draws system power per disk.       ║
// ║  Connects by Data Pipe or by touching - never by radius.        ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class NASBlock : MonoBehaviour
    {
        public const int BAYS = 8;

        [Header("Storage")]
        public ItemContainer diskSlots; // 8 drive bays

        [Header("Network")]
        [Tooltip("Higher priority shelves fill first on insert and drain first on extract.")]
        public int priority = 0;

        public System.Collections.Generic.List<DiskData> activeDisks = new();

        public int   TotalStored   { get; private set; }
        public int   TotalCapacity { get; private set; }
        public float TotalStoredGb { get; private set; }

        /// <summary>System watts this shelf wants: base + per installed disk.</summary>
        public float DrawWatts { get; private set; } = 8f;

        private float _timer;

        private void Awake()
        {
            EnsureContainers();
        }

        private void OnDestroy() => DropAllDisks();

        public void EnsureContainers()
        {
            if (diskSlots == null) diskSlots = new ItemContainer("NAS Disks", BAYS);
            else diskSlots.Resize(BAYS);
            diskSlots.OnChanged -= OnBaysChanged;
            diskSlots.OnChanged += OnBaysChanged;
        }

        /// <summary>14.41.0: a bay edit mounts/unmounts the disk IMMEDIATELY.
        /// The 1 s Update cadence alone meant the UI rebuilt (container event)
        /// before the disk was mounted - the panel showed EMPTY BAY until the
        /// player closed and reopened the screen.</summary>
        private bool _syncingBays;
        private void OnBaysChanged()
        {
            // SyncDisks writes payloads back into the slots, which raises
            // OnChanged again - without the guard this recurses forever.
            if (_syncingBays) return;
            _syncingBays = true;
            try
            {
                ValidateDiskSlots();
                SyncDisks();
            }
            finally { _syncingBays = false; }
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < 1f) return;
            _timer = 0;
            SyncDisks();
        }

        /// <summary>Only StorageDisk items may sit in a drive bay - RAM sticks,
        /// CPUs and coal stacks are ejected on the spot.</summary>
        private void ValidateDiskSlots()
        {
            for (int i = 0; i < diskSlots.Size; i++)
            {
                var s = diskSlots.GetSlot(i);
                if (!s.IsEmpty && !(s.item is StorageDisk))
                {
                    diskSlots.SetSlot(i, new ItemStack());
                    Items.DroppedItem.Spawn(s.Clone(),
                        transform.position + Vector3.up * 0.6f, Vector3.up);
                }
            }
        }

        public void SetPriority(int value)
        {
            priority = Mathf.Clamp(value, -99, 999);
        }

        private void SyncDisks()
        {
            EnsureContainers();
            while (activeDisks.Count < diskSlots.Size) activeDisks.Add(null);
            while (activeDisks.Count > diskSlots.Size) activeDisks.RemoveAt(activeDisks.Count - 1);

            float usedGb = 0f;
            int disks = 0;
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
                disks++;

                usedGb += activeDisks[i].UsedGigabytes;
                TotalCapacity += activeDisks[i].Capacity;
            }
            TotalStoredGb = usedGb;
            TotalStored = Mathf.CeilToInt(usedGb);
            DrawWatts = 8f + disks * 4f;
        }

        /// <summary>Fill state of one bay: -1 = empty bay, otherwise 0..1.</summary>
        public float BayFill01(int bayIndex)
        {
            if (bayIndex < 0 || bayIndex >= activeDisks.Count) return -1f;
            var d = activeDisks[bayIndex];
            if (d == null) return -1f;
            return d.Capacity > 0 ? Mathf.Clamp01(d.UsedGigabytes / d.Capacity) : 0f;
        }

        private void DropAllDisks()
        {
            if (diskSlots == null) return;
            for (int i = 0; i < diskSlots.Size; i++)
            {
                var s = diskSlots.GetSlot(i);
                if (!s.IsEmpty)
                    Items.DroppedItem.Spawn(s.Clone(),
                        transform.position + Vector3.up * 0.5f, Vector3.up);
            }
        }

        /// <summary>Called by the Server Controller to include this shelf's disks.</summary>
        public System.Collections.Generic.List<DiskData> GetActiveDisks() => activeDisks;
    }
}
