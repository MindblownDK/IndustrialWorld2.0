// Assets/Scripts/VoxelEngine/Storage/ServerRack.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║                    SERVER CONTROLLER (14.40.0)                   ║
// ║  The brain of the digital storage network. One per network.      ║
// ║  • Holds RAM (pattern memory) and CPU (craft speed). No disks -  ║
// ║    those live in NAS shelves. No PSU - power comes from Power    ║
// ║    Stations piped into the network.                              ║
// ║  • Computes the TOTAL system power draw of every device on the   ║
// ║    network and distributes that load across the grid-connected   ║
// ║    Power Stations. Delivered < draw = the whole system is down.  ║
// ║  • Two controllers on one network: deterministic election, the   ║
// ║    loser shows CONTROLLER CONFLICT and stands down.              ║
// ║  • Insert/extract walk storage targets by PRIORITY: drawers and  ║
// ║    NAS shelves each carry a player-set priority number.          ║
// ╚══════════════════════════════════════════════════════════════════╝
//
// The class keeps its historical name (ServerRack) so prefabs, saves and
// sync payloads stay stable; every player-facing string says Server
// Controller.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class ServerRack : MonoBehaviour
    {
        [Header("Slots")]
        public ItemContainer ramSlots;    // 4 RAM modules
        public ItemContainer cpuSlot;     // 1 CPU

        [Header("Runtime")]
        public List<DiskData> activeDisks = new();
        public List<NASBlock> connectedNAS = new();

        // ── Public Properties ──────────────────────────────────────
        public int   TotalStored          { get; private set; }
        public int   TotalCapacity        { get; private set; }
        public float TotalStoredGb        { get; private set; }
        public int   PatternSlots         { get; private set; }
        public float CraftSpeedMultiplier { get; private set; } = 1f;
        public bool  IsOnline             { get; private set; }

        /// <summary>Total watts the whole network wants right now.</summary>
        public float SystemDrawWatts      { get; private set; }
        /// <summary>Watts actually delivered by grid-powered Power Stations.</summary>
        public float DeliveredWatts       { get; private set; }
        /// <summary>Combined PSU rating of all stations on the network.</summary>
        public float StationRatingWatts   { get; private set; }
        /// <summary>True when stations cannot cover the system draw.</summary>
        public bool  IsPowerShort         { get; private set; }
        /// <summary>True when a second controller is piped into this network
        /// and this one lost the deterministic election.</summary>
        public bool  HasConflict          { get; private set; }
        /// <summary>Legacy name kept for older callers: power trouble of any kind.</summary>
        public bool  IsPsuOverloaded      => IsPowerShort;

        // Per-device base draws (watts). NAS and security carry their own.
        public const float DRAW_CONTROLLER_BASE = 50f;
        public const float DRAW_RAM_MODULE      = 15f;
        public const float DRAW_CPU_PER_SPEED   = 10f;
        public const float DRAW_TERMINAL        = 5f;
        public const float DRAW_IMPORTER        = 5f;
        public const float DRAW_EXPORTER        = 5f;
        public const float DRAW_MANIPULATOR     = 5f;
        public const float DRAW_DRAWER_CTRL     = 5f;
        public const float DRAW_TRANSMITTER     = 10f;

        private float _tickTimer;

        // Scratch member lists (reused every tick).
        private readonly List<NASBlock>                _nasBuf        = new();
        private readonly List<Powerstation>            _stationBuf    = new();
        private readonly List<StorageTerminal>         _terminalBuf   = new();
        private readonly List<CraftingTerminal>        _craftTermBuf  = new();
        private readonly List<PatternTerminal>         _patTermBuf    = new();
        private readonly List<StorageImporter>         _importerBuf   = new();
        private readonly List<StorageExporter>         _exporterBuf   = new();
        private readonly List<DiskManipulator>         _manipBuf      = new();
        private readonly List<StorageDrawerController> _drawerBuf     = new();
        private readonly List<ExternalStorageBlock>    _externalBuf   = new();
        private readonly List<WirelessTransmitter>     _transmitterBuf = new();
        private readonly List<SecurityBlock>           _securityBuf   = new();

        /// <summary>One prioritized storage target: either a NAS shelf (digital
        /// disks) or an external physical source (drawer controller).</summary>
        private struct StorageTarget
        {
            public int priority;
            public NASBlock nas;
            public IExternalStorageSource external;
        }
        private readonly List<StorageTarget> _targets = new();

        // ── Unity ──────────────────────────────────────────────────
        private void Awake()
        {
            EnsureContainers();
            // 14.40.0: the controller no longer draws from the power grid
            // itself - Power Stations pay the whole system bill. A legacy
            // PowerConsumer on the prefab is neutralized, not destroyed
            // (removing prefab components at runtime is unsafe).
            var legacyPower = GetComponent<Power.PowerConsumer>();
            if (legacyPower != null) legacyPower.wattsPerSecond = 0f;
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
            if (ramSlots == null) ramSlots = new ItemContainer("RAM", 4);
            else ramSlots.Resize(4);
            ramSlots.OnChanged -= ValidateRamSlots;
            ramSlots.OnChanged += ValidateRamSlots;

            if (cpuSlot == null) cpuSlot = new ItemContainer("CPU", 1);
            else cpuSlot.Resize(1);
            cpuSlot.OnChanged -= ValidateCpuSlot;
            cpuSlot.OnChanged += ValidateCpuSlot;
        }

        // ── Slot Validation ────────────────────────────────────────
        // Eject items that don't belong - the controller is type-safe.

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

        // ── Network members (for UIs) ──────────────────────────────
        public int NasCount         { get; private set; }
        public int StationCount     { get; private set; }
        public int TerminalCount    { get; private set; }
        public int DrawerCtrlCount  { get; private set; }
        public int TransmitterCount { get; private set; }
        public int SecurityCount    { get; private set; }
        public int ExternalCount    { get; private set; }

        // ── Recalculation ──────────────────────────────────────────
        private void Recalculate()
        {
            EnsureContainers();

            HasConflict = StorageNetwork.IsConflicting(this);

            // Gather members (empty lists when conflicting - a stood-down
            // controller claims nothing).
            if (!HasConflict)
            {
                StorageNetwork.MembersOf(this, _nasBuf);
                StorageNetwork.MembersOf(this, _stationBuf);
                StorageNetwork.MembersOf(this, _terminalBuf);
                StorageNetwork.MembersOf(this, _craftTermBuf);
                StorageNetwork.MembersOf(this, _patTermBuf);
                StorageNetwork.MembersOf(this, _importerBuf);
                StorageNetwork.MembersOf(this, _exporterBuf);
                StorageNetwork.MembersOf(this, _manipBuf);
                StorageNetwork.MembersOf(this, _drawerBuf);
                StorageNetwork.MembersOf(this, _externalBuf);
                StorageNetwork.MembersOf(this, _transmitterBuf);
                StorageNetwork.MembersOf(this, _securityBuf);
            }
            else
            {
                _nasBuf.Clear(); _stationBuf.Clear(); _terminalBuf.Clear();
                _craftTermBuf.Clear(); _patTermBuf.Clear(); _importerBuf.Clear();
                _exporterBuf.Clear(); _manipBuf.Clear(); _drawerBuf.Clear();
                _externalBuf.Clear(); _transmitterBuf.Clear(); _securityBuf.Clear();
            }

            NasCount = _nasBuf.Count; StationCount = _stationBuf.Count;
            TerminalCount = _terminalBuf.Count + _craftTermBuf.Count + _patTermBuf.Count;
            DrawerCtrlCount = _drawerBuf.Count; TransmitterCount = _transmitterBuf.Count;
            SecurityCount = _securityBuf.Count; ExternalCount = _externalBuf.Count;

            // ── CPU / RAM ──────────────────────────────────────────
            var cpu = cpuSlot.GetSlot(0);
            float cpuSpeed = (!cpu.IsEmpty && cpu.item is ServerComponent cc
                && cc.componentType == ComponentType.CPU) ? cc.value : 1f;
            CraftSpeedMultiplier = cpuSpeed;

            PatternSlots = 0;
            int ramModules = 0;
            for (int i = 0; i < ramSlots.Size; i++)
            {
                var ram = ramSlots.GetSlot(i);
                if (!ram.IsEmpty && ram.item is ServerComponent rc && rc.componentType == ComponentType.RAM)
                {
                    PatternSlots += Mathf.RoundToInt(rc.value) * ram.count;
                    ramModules += ram.count;
                }
            }

            // ── Total system power draw ────────────────────────────
            float draw = DRAW_CONTROLLER_BASE
                       + ramModules * DRAW_RAM_MODULE
                       + (cpuSpeed > 1f ? cpuSpeed * DRAW_CPU_PER_SPEED : 0f);
            foreach (var nas in _nasBuf) draw += nas.DrawWatts;
            draw += _terminalBuf.Count * DRAW_TERMINAL;
            draw += _craftTermBuf.Count * DRAW_TERMINAL;
            draw += _patTermBuf.Count * DRAW_TERMINAL;
            draw += _importerBuf.Count * DRAW_IMPORTER;
            draw += _exporterBuf.Count * DRAW_EXPORTER;
            draw += _manipBuf.Count * DRAW_MANIPULATOR;
            draw += _drawerBuf.Count * DRAW_DRAWER_CTRL;
            draw += _externalBuf.Count * ExternalStorageBlock.DRAW_WATTS;
            draw += _transmitterBuf.Count * DRAW_TRANSMITTER;
            foreach (var sec in _securityBuf) draw += sec.DrawWatts;
            SystemDrawWatts = draw;

            // ── Distribute the load across grid-powered stations ───
            StationRatingWatts = 0f;
            DeliveredWatts = 0f;
            foreach (var st in _stationBuf) StationRatingWatts += st.RatingWatts;
            foreach (var st in _stationBuf)
            {
                float share = StationRatingWatts > 0f
                    ? draw * (st.RatingWatts / StationRatingWatts)
                    : 0f;
                st.AssignLoad(Mathf.Min(share, st.RatingWatts));
                if (st.IsGridPowered) DeliveredWatts += st.RatingWatts;
            }

            IsPowerShort = DeliveredWatts + 0.5f < SystemDrawWatts;
            IsOnline = !HasConflict && !IsPowerShort && _stationBuf.Count > 0;

            // ── Gather disks from NAS shelves, priority order ──────
            BuildTargets();

            connectedNAS.Clear();
            activeDisks.Clear();
            float usedGb = 0f;
            TotalCapacity = 0;
            foreach (var t in _targets)
            {
                if (t.nas == null) continue;
                connectedNAS.Add(t.nas);
                foreach (var d in t.nas.GetActiveDisks())
                {
                    if (d == null) continue;
                    activeDisks.Add(d);
                    usedGb += d.UsedGigabytes;
                    TotalCapacity += d.Capacity;
                }
            }
            TotalStoredGb = usedGb;
            TotalStored = Mathf.CeilToInt(usedGb);
        }

        /// <summary>Rebuild the prioritized target list: NAS shelves and drawer
        /// controllers, highest priority first; position-stable within a tier.</summary>
        private void BuildTargets()
        {
            _targets.Clear();
            foreach (var nas in _nasBuf)
                if (nas != null)
                    _targets.Add(new StorageTarget { priority = nas.priority, nas = nas });
            foreach (var dc in _drawerBuf)
                if (dc != null && dc is IExternalStorageSource src && src.IsAvailable)
                    _targets.Add(new StorageTarget { priority = src.Priority, external = src });
            // 14.41.0: External Storage bridges - chests and lone drawers made
            // network-visible, ranked by the same priority number.
            foreach (var ext in _externalBuf)
                if (ext != null && ext.IsAvailable)
                    _targets.Add(new StorageTarget { priority = ext.Priority, external = ext });
            _targets.Sort((a, b) => b.priority.CompareTo(a.priority));
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
            Drop(ramSlots);
            Drop(cpuSlot);
        }

        private void SpawnDropped(ItemStack stack, Vector3? overridePos = null)
        {
            var p = overridePos ?? (transform.position + Vector3.up * 0.5f
                                    + Random.insideUnitSphere * 0.4f);
            Items.DroppedItem.Spawn(stack.Clone(), p, Vector3.up);
        }

        // ── Storage API ────────────────────────────────────────────
        public int NetworkInsert(ItemDefinition item, int count)
        {
            if (!IsOnline || item == null || count <= 0) return count;
            int remaining = count;
            foreach (var t in _targets)
            {
                if (t.external != null)
                {
                    remaining = t.external.Insert(item, remaining);
                }
                else if (t.nas != null)
                {
                    foreach (var d in t.nas.GetActiveDisks())
                    {
                        if (d == null) continue;
                        remaining -= d.Insert(item, remaining);
                        if (remaining <= 0) return 0;
                    }
                }
                if (remaining <= 0) return 0;
            }
            return remaining;
        }

        public int NetworkExtract(string itemId, int count)
        {
            if (!IsOnline || count <= 0) return 0;
            int extracted = 0;
            foreach (var t in _targets)
            {
                if (t.external != null)
                {
                    extracted += t.external.Extract(itemId, count - extracted);
                }
                else if (t.nas != null)
                {
                    foreach (var d in t.nas.GetActiveDisks())
                    {
                        if (d == null) continue;
                        extracted += d.Extract(itemId, count - extracted);
                        if (extracted >= count) return extracted;
                    }
                }
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
            foreach (var t in _targets)
                t.external?.AppendAllItems(merged);
            var list = new List<StoredItemEntry>(merged.Values);
            list.Sort((a, b) => b.count.CompareTo(a.count));
            return list;
        }

        public int NetworkCount(string itemId)
        {
            int total = 0;
            foreach (var d in activeDisks)
                if (d != null) total += d.CountOf(itemId);
            foreach (var t in _targets)
                if (t.external != null) total += t.external.CountOf(itemId);
            return total;
        }

        // ── Legacy registration API (kept as no-ops so older callers keep
        //    compiling; membership is network-resolved now) ──────────
        public void RegisterExternalStorage(IExternalStorageSource source) { }
        public void UnregisterExternalStorage(IExternalStorageSource source) { }
    }
}
