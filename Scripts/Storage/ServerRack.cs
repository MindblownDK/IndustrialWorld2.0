// Assets/Scripts/VoxelEngine/Storage/ServerRack.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║                       SERVER RACK                               ║
// ║  Holds 6 storage disks, RAM, CPU, PSU.                         ║
// ║  • Slot validation: only correct component types accepted.      ║
// ║  • Disk data persists when disk items are removed.              ║
// ║  • Drops all installed items on destroy.                        ║
// ║  • PSU overload shuts down rack and shows warning.             ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class ServerRack : MonoBehaviour
    {
        [Header("Slots")]
        public ItemContainer diskSlots;   // 6 storage disks
        public ItemContainer ramSlots;    // 4 RAM modules
        public ItemContainer cpuSlot;     // 1 CPU
        public ItemContainer psuSlot;     // 1 PSU

        [Header("Runtime")]
        public List<DiskData>  activeDisks   = new();
        public List<NASBlock>  connectedNAS  = new();

        // ── Public Properties ──────────────────────────────────────
        public int   TotalStored           { get; private set; }
        public int   TotalCapacity         { get; private set; }
        public int   PatternSlots          { get; private set; }
        public float CraftSpeedMultiplier  { get; private set; } = 1f;
        public float MaxPowerWatts         { get; private set; }
        public bool  IsOnline              { get; private set; }

        /// <summary>True when actual power draw exceeds PSU rating — rack shuts down.</summary>
        public bool IsPsuOverloaded        { get; private set; }

        // ── Disk-data persistence ────────────────────────────────────
        // DiskData now lives on the ItemStack itself (via ItemStack.payload), so
        // a partially-filled disk taken OUT of one rack keeps its contents and
        // can be slotted back into a different rack — or into a Disk Manipulator
        // — without losing items. The rack still keeps a per-slot reference in
        // activeDisks so the Recalculate() pass can find storage targets quickly.
        // The legacy in-rack registry was per-rack/per-slot which orphaned data
        // the moment a disk left the slot; removed.

        private PowerConsumer _power;
        private float         _tickTimer;
        private readonly List<IExternalStorageSource> _externalStorageSources = new();

        // ── Unity ──────────────────────────────────────────────────
        private void Awake()
        {
            EnsureContainers();
            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();

            // Subscribe to slot changes for validation.
            if (cpuSlot  != null) cpuSlot.OnChanged  += ValidateCpuSlot;
            if (ramSlots != null) ramSlots.OnChanged  += ValidateRamSlots;
            if (psuSlot  != null) psuSlot.OnChanged   += ValidatePsuSlot;
        }

        private void OnDestroy() => DropAllItems();

        private void Update()
        {
            _tickTimer += Time.deltaTime;
            if (_tickTimer < 0.5f) return;
            _tickTimer = 0;
            Recalculate();
        }

        // ── Container Setup ────────────────────────────────────────
        public void EnsureContainers()
        {
            if (diskSlots == null)
            {
                diskSlots = new ItemContainer("Disks", 6);
            }
            else diskSlots.Resize(6);
            // Always re-subscribe (remove first to avoid duplicates).
            diskSlots.OnChanged -= PersistDiskData;
            diskSlots.OnChanged += PersistDiskData;

            if (ramSlots == null) ramSlots = new ItemContainer("RAM", 4);
            else ramSlots.Resize(4);
            ramSlots.OnChanged -= ValidateRamSlots;
            ramSlots.OnChanged += ValidateRamSlots;

            if (cpuSlot == null) cpuSlot = new ItemContainer("CPU", 1);
            else cpuSlot.Resize(1);
            cpuSlot.OnChanged -= ValidateCpuSlot;
            cpuSlot.OnChanged += ValidateCpuSlot;

            if (psuSlot == null) psuSlot = new ItemContainer("PSU", 1);
            else psuSlot.Resize(1);
            psuSlot.OnChanged -= ValidatePsuSlot;
            psuSlot.OnChanged += ValidatePsuSlot;
        }

        // ── Slot Validation ────────────────────────────────────────
        // Eject items that don't belong in a slot — keeps the rack type-safe.

        private void ValidateCpuSlot()
        {
            var s = cpuSlot.GetSlot(0);
            if (!s.IsEmpty && !(s.item is ServerComponent sc && sc.componentType == ComponentType.CPU))
            {
                cpuSlot.SetSlot(0, new ItemStack());
                SpawnDropped(s);
            }
        }

        private void ValidateRamSlots()
        {
            for (int i = 0; i < ramSlots.Size; i++)
            {
                var s = ramSlots.GetSlot(i);
                if (!s.IsEmpty && !(s.item is ServerComponent sc && sc.componentType == ComponentType.RAM))
                {
                    ramSlots.SetSlot(i, new ItemStack());
                    SpawnDropped(s);
                }
            }
        }

        private void ValidatePsuSlot()
        {
            var s = psuSlot.GetSlot(0);
            if (!s.IsEmpty && !(s.item is ServerComponent sc && sc.componentType == ComponentType.PSU))
            {
                psuSlot.SetSlot(0, new ItemStack());
                SpawnDropped(s);
            }
        }

        // ── Disk Data Persistence ──────────────────────────────────
        // When disk items are placed or removed, the DiskData is cached
        // by asset GUID so the data survives the slot being emptied.

        private void PersistDiskData()
        {
            // Mirror the disk slots into activeDisks for the simulation loop.
            // The DiskData itself lives on ItemStack.payload — it's created on
            // first insertion of a brand-new disk, and re-used on every subsequent
            // insertion into this or any other rack/manipulator. That means a
            // partially-filled disk pulled out, dropped, picked up, and slotted
            // into a different rack still carries its full contents.
            for (int i = 0; i < diskSlots.Size; i++)
            {
                var slot = diskSlots.GetSlot(i);
                while (activeDisks.Count <= i) activeDisks.Add(null);

                if (!slot.IsEmpty && slot.item is StorageDisk sd)
                {
                    var data = slot.payload as DiskData;
                    if (data == null || data.tier != sd.tier)
                    {
                        data = new DiskData { tier = sd.tier };
                        slot.payload = data;
                        // Write the modified stack back so the container persists the payload reference.
                        diskSlots.SetSlot(i, slot);
                    }
                    activeDisks[i] = data;
                }
                else
                {
                    activeDisks[i] = null;
                }
            }
        }

        // ── Recalculation ──────────────────────────────────────────
        private void Recalculate()
        {
            EnsureContainers();

            // ── PSU: determine max watts ───────────────────────────
            float psuWatts = 0f;
            var psu = psuSlot.GetSlot(0);
            if (!psu.IsEmpty && psu.item is ServerComponent psuComp && psuComp.componentType == ComponentType.PSU)
                psuWatts = psuComp.value;
            MaxPowerWatts = psuWatts + _externalPsuWatts;

            // ── Calculate draw ─────────────────────────────────────
            float draw = CalculatePowerDraw();
            if (_power != null) _power.wattsPerSecond = draw;

            // ── PSU overload check ─────────────────────────────────
            // Overloaded = draw exceeds PSU rating OR no PSU installed.
            IsPsuOverloaded = (psuWatts <= 0f || draw > psuWatts + 0.5f);
            IsOnline = (_power != null && _power.IsPowered) && !IsPsuOverloaded;

            // ── CPU ───────────────────────────────────────────────
            var cpu = cpuSlot.GetSlot(0);
            CraftSpeedMultiplier = (!cpu.IsEmpty && cpu.item is ServerComponent cc
                && cc.componentType == ComponentType.CPU)
                ? cc.value : 1f;

            // ── RAM ───────────────────────────────────────────────
            PatternSlots = 0;
            for (int i = 0; i < ramSlots.Size; i++)
            {
                var ram = ramSlots.GetSlot(i);
                if (!ram.IsEmpty && ram.item is ServerComponent rc && rc.componentType == ComponentType.RAM)
                    PatternSlots += Mathf.RoundToInt(rc.value);
            }

            // ── Sync & total disks ─────────────────────────────────
            SyncDisks();
            float usedGb = 0f;
            TotalStored = 0; TotalCapacity = 0;
            foreach (var d in activeDisks)
            {
                if (d == null) continue;
                usedGb += d.UsedGigabytes;
                TotalCapacity += d.Capacity;
            }
            TotalStored = Mathf.CeilToInt(usedGb);
        }

        private void SyncDisks()
        {
            while (activeDisks.Count < diskSlots.Size) activeDisks.Add(null);

            for (int i = 0; i < diskSlots.Size; i++)
            {
                var slot = diskSlots.GetSlot(i);
                if (slot.IsEmpty || !(slot.item is StorageDisk sd))
                { activeDisks[i] = null; continue; }

                // Read DiskData from the stack's payload (mints a fresh one for
                // brand-new disks). This is the same logic used by PersistDiskData;
                // both paths converge on the stack-bound payload so contents follow
                // the disk wherever it goes.
                var data = slot.payload as DiskData;
                if (data == null || data.tier != sd.tier)
                {
                    data = new DiskData { tier = sd.tier };
                    slot.payload = data;
                    diskSlots.SetSlot(i, slot);
                }
                activeDisks[i] = data;
            }

            // Include NAS disks connected via data cables.
            connectedNAS.Clear();
            foreach (var anchor in GetComponents<Networks.ConnectionAnchor>())
            {
                if (anchor.network == null) continue;
                if (anchor.network is Networks.DataNetworkNew dn)
                {
                    foreach (var a in dn.anchors)
                    {
                        if (a == null || a.owner == null) continue;
                        var nas = a.owner.GetComponent<NASBlock>();
                        if (nas != null && !connectedNAS.Contains(nas))
                        {
                            connectedNAS.Add(nas);
                            foreach (var d in nas.GetActiveDisks())
                                if (d != null && !activeDisks.Contains(d)) activeDisks.Add(d);
                        }
                    }
                }
            }
        }

        private float CalculatePowerDraw()
        {
            float draw = 50f; // base draw
            for (int i = 0; i < diskSlots.Size; i++)
                if (!diskSlots.GetSlot(i).IsEmpty) draw += 20f;
            var cpu = cpuSlot.GetSlot(0);
            if (!cpu.IsEmpty && cpu.item is ServerComponent cc)
                draw += cc.value * 10f;
            for (int i = 0; i < ramSlots.Size; i++)
                if (!ramSlots.GetSlot(i).IsEmpty) draw += 15f;
            return draw;
        }

        // ── Drop Items on Destroy ──────────────────────────────────
        private void DropAllItems()
        {
            Vector3 pos = transform.position + Vector3.up * 0.8f;
            void Drop(ItemContainer c)
            {
                if (c == null) return;
                for (int i = 0; i < c.Size; i++)
                {
                    var s = c.GetSlot(i);
                    if (!s.IsEmpty) SpawnDropped(s, pos);
                }
            }
            Drop(diskSlots);
            Drop(ramSlots);
            Drop(cpuSlot);
            Drop(psuSlot);
        }

        private void SpawnDropped(ItemStack stack, Vector3? overridePos = null)
        {
            var p = overridePos ?? (transform.position + Vector3.up * 0.5f
                                    + Random.insideUnitSphere * 0.4f);
            Items.DroppedItem.Spawn(stack.Clone(), p, Vector3.up);
        }

        // ── External PSU (Powerstation) ────────────────────────────
        // Powerstations call this each tick to contribute their wattage.
        private readonly Dictionary<Powerstation, float> _externalPsus = new();
        private float _externalPsuWatts = 0f;

        /// <summary>Called by Powerstation every 0.5s to register its contribution.</summary>
        public void RegisterExternalPsu(float watts, Powerstation source)
        {
            _externalPsus[source] = watts;
            _externalPsuWatts = 0f;
            foreach (var kv in _externalPsus) _externalPsuWatts += kv.Value;
            MaxPowerWatts = GetBasePsuWatts() + _externalPsuWatts;
        }

        private float GetBasePsuWatts()
        {
            var psu = psuSlot?.GetSlot(0) ?? new ItemStack();
            if (!psu.IsEmpty && psu.item is ServerComponent sc && sc.componentType == ComponentType.PSU)
                return sc.value;
            return 0f;
        }

        // ── External physical storage (drawer controllers) ─────────
        public void RegisterExternalStorage(IExternalStorageSource source)
        {
            if (source == null || _externalStorageSources.Contains(source)) return;
            _externalStorageSources.Add(source);
            _externalStorageSources.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public void UnregisterExternalStorage(IExternalStorageSource source)
        {
            if (source != null) _externalStorageSources.Remove(source);
        }

        private void PruneExternalStorage()
        {
            for (int i = _externalStorageSources.Count - 1; i >= 0; i--)
            {
                var s = _externalStorageSources[i];
                if (s == null || !s.IsAvailable) _externalStorageSources.RemoveAt(i);
            }
        }

        // ── Storage API ────────────────────────────────────────────
        public int NetworkInsert(ItemDefinition item, int count)
        {
            if (!IsOnline || item == null || count <= 0) return count;
            int remaining = count;
            PruneExternalStorage();
            foreach (var source in _externalStorageSources)
            {
                remaining = source.Insert(item, remaining);
                if (remaining <= 0) return 0;
            }
            foreach (var d in activeDisks)
            {
                if (d == null) continue;
                int accepted = d.Insert(item, remaining);
                remaining -= accepted;
                if (remaining <= 0) return 0;
            }
            return remaining;
        }

        public int NetworkExtract(string itemId, int count)
        {
            if (!IsOnline || count <= 0) return 0;
            int extracted = 0;
            PruneExternalStorage();
            foreach (var source in _externalStorageSources)
            {
                int got = source.Extract(itemId, count - extracted);
                extracted += got;
                if (extracted >= count) return extracted;
            }
            foreach (var d in activeDisks)
            {
                if (d == null) continue;
                int got = d.Extract(itemId, count - extracted);
                extracted += got;
                if (extracted >= count) return extracted;
            }
            return extracted;
        }

        public List<StoredItemEntry> GetAllItems()
        {
            var merged = new Dictionary<string, StoredItemEntry>();
            foreach (var d in activeDisks)
            {
                if (d == null) continue;
                foreach (var e in d.items)
                {
                    if (merged.TryGetValue(e.itemId, out var ex)) ex.count += e.count;
                    else merged[e.itemId] = new StoredItemEntry
                        { itemId = e.itemId, displayName = e.displayName, count = e.count, massPerUnit = e.massPerUnit <= 0f ? 1f : e.massPerUnit };
                }
            }
            PruneExternalStorage();
            foreach (var source in _externalStorageSources)
                source.AppendAllItems(merged);
            var list = new List<StoredItemEntry>(merged.Values);
            list.Sort((a, b) => b.count.CompareTo(a.count));
            return list;
        }

        public int NetworkCount(string itemId)
        {
            int total = 0;
            foreach (var d in activeDisks)
                if (d != null) total += d.CountOf(itemId);
            PruneExternalStorage();
            foreach (var source in _externalStorageSources)
                total += source.CountOf(itemId);
            return total;
        }
    }
}
