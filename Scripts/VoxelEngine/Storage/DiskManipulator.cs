// Assets/Scripts/VoxelEngine/Storage/DiskManipulator.cs
//
// Transfer items from one disk to another (for upgrading disk tiers).
// Has 2 disk slots: Source and Destination. Transfers all items from source to dest.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class DiskManipulator : MonoBehaviour
    {
        public ItemContainer sourceSlot;  // 1 slot
        public ItemContainer destSlot;    // 1 slot

        public bool IsTransferring { get; private set; }
        public float Progress01 { get; private set; }
        public string StatusText { get; private set; } = "Insert disks";

        private DiskData _srcDisk;
        private DiskData _dstDisk;
        private float _timer;
        private int _totalToTransfer;
        private int _transferred;

        private void Awake()
        {
            if (sourceSlot == null) sourceSlot = new ItemContainer("Source Disk", 1);
            if (destSlot == null) destSlot = new ItemContainer("Dest Disk", 1);
        }

        public void EnsureContainers()
        {
            if (sourceSlot == null) sourceSlot = new ItemContainer("Source Disk", 1);
            if (destSlot == null) destSlot = new ItemContainer("Dest Disk", 1);
        }

        private void Update()
        {
            EnsureContainers();

            // Validate slots — only StorageDisk items allowed.
            var srcSlot = sourceSlot.GetSlot(0);
            var dstSlot = destSlot.GetSlot(0);

            if (srcSlot.IsEmpty || !(srcSlot.item is StorageDisk))
            { _srcDisk = null; IsTransferring = false; StatusText = "Insert source disk"; return; }
            if (dstSlot.IsEmpty || !(dstSlot.item is StorageDisk))
            { _dstDisk = null; IsTransferring = false; StatusText = "Insert destination disk"; return; }

            // Pull the disks' persistent DiskData straight off the inserted ItemStacks.
            // This means a disk full of items keeps those items when slotted here, and
            // the transfer below operates on the SAME object the rack saw — so when the
            // disk is pulled out and put back in a rack, the new contents are still there.
            _srcDisk = srcSlot.payload as DiskData;
            if (_srcDisk == null || _srcDisk.tier != ((StorageDisk)srcSlot.item).tier)
            {
                _srcDisk = new DiskData { tier = ((StorageDisk)srcSlot.item).tier };
                srcSlot.payload = _srcDisk;
                sourceSlot.SetSlot(0, srcSlot);
            }

            _dstDisk = dstSlot.payload as DiskData;
            if (_dstDisk == null || _dstDisk.tier != ((StorageDisk)dstSlot.item).tier)
            {
                _dstDisk = new DiskData { tier = ((StorageDisk)dstSlot.item).tier };
                dstSlot.payload = _dstDisk;
                destSlot.SetSlot(0, dstSlot);
            }

            if (_srcDisk.totalStored == 0)
            { IsTransferring = false; StatusText = "Source disk empty"; Progress01 = 1f; return; }

            if (_dstDisk.FreeSpace <= 0)
            { IsTransferring = false; StatusText = "Dest disk full!"; return; }

            // Transfer items one batch per tick.
            IsTransferring = true;
            _timer += Time.deltaTime;
            if (_timer < 0.1f) return;
            _timer = 0;

            if (_totalToTransfer == 0) _totalToTransfer = _srcDisk.totalStored;

            // Transfer one item type per tick.
            if (_srcDisk.items.Count > 0)
            {
                var entry = _srcDisk.items[0];
                int amount = Mathf.Min(entry.count, _dstDisk.FreeSpace);
                if (amount > 0)
                {
                    // Find the ItemDefinition.
                    var allItems = Resources.FindObjectsOfTypeAll<ItemDefinition>();
                    ItemDefinition itemDef = null;
                    foreach (var it in allItems) if (it.itemId == entry.itemId) { itemDef = it; break; }

                    if (itemDef != null)
                    {
                        _dstDisk.Insert(itemDef, amount);
                        _srcDisk.Extract(entry.itemId, amount);
                        _transferred += amount;
                    }
                }
            }

            Progress01 = _totalToTransfer > 0 ? (float)_transferred / _totalToTransfer : 0;
            StatusText = $"Transferring... {Progress01 * 100f:0}%";

            if (_srcDisk.totalStored == 0)
            {
                StatusText = "Transfer complete!";
                IsTransferring = false;
                _totalToTransfer = 0;
                _transferred = 0;
            }
        }
    }
}
