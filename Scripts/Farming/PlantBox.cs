// Assets/Scripts/VoxelEngine/Farming/PlantBox.cs
//
// 18.3.0-dev — one box, one to nine crops, and the farm stops being a row of
// identical trays.
//
// A Seed Planter was one metre and one crop. That is right for a workbench
// corner and wrong for a colony: a corridor of trays is nine blocks, nine
// cards on the farm screen and nine things to click. A planting box is the same
// idea at three sizes - a Small box holds one crop, a Big box holds four, and a
// Large bed holds nine and is the only one a tree fits in. The block is the
// unit of the farm; the slots inside it are the unit of the work.
//
// The water rule is the same for all three sizes, and it is deliberately two
// layers:
//
//   * the box holds its own water, so a player can water it by hand with a
//     filled Liquid Canister and walk away. Slots drink from that tank;
//   * a Sprinkler within reach overrides the tank completely - it is the
//     upgrade. A powered, watered sprinkler keeps every slot in its radius fed
//     for free, which is what makes a wall of beds hands-off.
//
// Nothing here needs power. Power belongs to the sprinkler that automates it,
// which is the step a player earns rather than the step they start with.
//
// Save safety: this is a new block, so an old save simply has none of these
// objects. Where a box IS saved, its nine slots ride the same additive
// machine-process record every other machine uses.

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Simulation;

namespace VoxelEngine.Farming
{
    public enum PlantBoxSize { Small = 0, Big = 1, Large = 2 }

    /// <summary>
    /// A planting box: a raised bed with one, four or nine soil cells that a player
    /// plants by hand and a sprinkler waters automatically. Large beds also take a
    /// tree sapling, which is the only place a tree can be grown indoors.
    /// </summary>
    [RequireComponent(typeof(PlacedBlock))]
    public class PlantBox : MonoBehaviour, IFarmBed, IMachineProcessState
    {
        /// <summary>One soil cell.</summary>
        [Serializable]
        public class Slot
        {
            public CropDefinition crop;
            [Range(0f, 1f)] public float growth;
            public bool fertilized;
            public bool dead;
            /// <summary>True when this cell holds a timber sapling rather than a crop.</summary>
            public bool tree;
        }

        [Header("Box")]
        [Tooltip("Small = 1 cell, Big = 2x2 = 4 cells, Large = 3x3 = 9 cells and the only size that takes a tree.")]
        public PlantBoxSize boxSize = PlantBoxSize.Small;
        [Tooltip("Cells along one edge of the bed: 1, 2 or 3. Kept explicit so a prefab can author an odd footprint without changing the enum.")]
        public int cellsPerSide = 1;

        [Header("Slots")]
        public Slot[] slots = new Slot[0];

        [Header("Water")]
        [Tooltip("Litres this box holds when it is not fed by a sprinkler.")]
        public float waterCapacity = 40f;
        public float waterStored = 40f;
        [Tooltip("Litres one planted cell drinks per second while it is growing.")]
        public float litresPerCellSecond = 0.02f;
        [Tooltip("Radius in metres that a Sprinkler reaches this box from.")]
        public float irrigationCheckRadius = 6f;
        [Tooltip("Seconds between checks for a working sprinkler or for the air in the room.")]
        public float environmentCheckInterval = 2f;

        [Header("Growth")]
        [Tooltip("Growth multiplier while a cell is fertilized.")]
        public float fertilizedSpeedMultiplier = 1.5f;

        [Header("Trees (Large beds only)")]
        public ItemDefinition woodLogItem;
        public ItemDefinition saplingItem;
        public ItemDefinition biomassItem;
        public int treeLogsPerHarvest = 6;
        public int treeSaplingsPerHarvest = 2;
        public int treeBiomassPerHarvest = 2;
        [Tooltip("Seconds from sapling to mature timber on a Large bed.")]
        public float treeGrowthSeconds = 110f;
        [Tooltip("Growth multiplier for a tree while the bed is watered.")]
        public float treeIrrigatedSpeedMultiplier = 2f;

        [Header("Output Port")]
        [Tooltip("Empty ripe cells into a belt, chute, funnel or item pipe standing next to the box.")]
        public bool autoDumpWhenRipe = true;
        [Tooltip("Keep one seed of each port harvest back and replant that cell. This is the default: a watered bed with a belt against it cycles on its own.")]
        public bool replantAfterPortHarvest = true;
        public float dumpRadius = 1.9f;
        public float dumpIntervalSeconds = 1f;

        // ── IFarmBed (aggregate) ─────────────────────────────────────────────
        // The screen gets one card for the block, not nine. A bed is reported by what
        // it is doing as a whole: its worst water state, its most urgent crop, the
        // average growth of what is actually planted.
        public string BedName
        {
            get
            {
                switch (boxSize)
                {
                    case PlantBoxSize.Large: return "LARGE BED";
                    case PlantBoxSize.Big: return "BIG BOX";
                    default: return "PLANT BOX";
                }
            }
        }

        public string CropName
        {
            get
            {
                int planted = PlantedCount;
                if (planted == 0) return string.Empty;
                if (planted == 1)
                {
                    int only = FirstPlantedIndex();
                    if (only >= 0)
                    {
                        var slot = slots[only];
                        if (slot.tree) return "TIMBER";
                        if (slot.crop != null) return slot.crop.cropName;
                    }
                    return string.Empty;
                }
                return $"{planted} CROPS";
            }
        }

        public float Growth01
        {
            get
            {
                float total = 0f;
                int count = 0;
                for (int i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];
                    if (slot == null || slot.dead) continue;
                    if (slot.crop == null && !slot.tree) continue;
                    total += Mathf.Clamp01(slot.growth);
                    count++;
                }
                return count == 0 ? 0f : total / count;
            }
        }

        public bool IsReady { get { return ReadyCount > 0; } }
        public bool IsEmpty { get { return PlantedCount == 0; } }
        public bool NeedsWater { get { return AnyThirsty && !IsIrrigated; } }
        public bool NeedsOxygen { get { return !hasOxygen && AnyNeedsOxygen; } }
        public bool IsDead { get { return DeadCount > 0; } }

        /// <summary>0..1 water the box is holding, or 1 while a sprinkler is feeding it.</summary>
        public float Water01
        {
            get
            {
                if (IsIrrigated) return 1f;
                if (waterCapacity <= 0f) return 0f;
                return Mathf.Clamp01(waterStored / waterCapacity);
            }
        }

        // ── Live state the screens and the player read ───────────────────────
        public bool IsIrrigated => sprinklerFeeding || waterStored > 0.01f;
        public bool HasSprinkler => sprinklerFeeding;
        public int SlotCount => slots != null ? slots.Length : 0;

        public int PlantedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < slots.Length; i++)
                {
                    var s = slots[i];
                    if (s != null && (s.crop != null || s.tree)) n++;
                }
                return n;
            }
        }

        public int ReadyCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < slots.Length; i++)
                {
                    var s = slots[i];
                    if (s != null && !s.dead && (s.crop != null || s.tree) && s.growth >= 0.999f) n++;
                }
                return n;
            }
        }

        public int DeadCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] != null && slots[i].dead) n++;
                return n;
            }
        }

        private bool AnyThirsty
        {
            get
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    var s = slots[i];
                    if (s == null || s.dead) continue;
                    if (s.crop == null && !s.tree) continue;
                    if (s.tree) continue;                       // timber never dies of thirst
                    if (!s.crop.requiresWater) continue;
                    return true;
                }
                return false;
            }
        }

        private bool AnyNeedsOxygen
        {
            get
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    var s = slots[i];
                    if (s == null || s.dead) continue;
                    if (s.tree) continue;                       // saplings do not need a greenhouse
                    if (s.crop == null || !s.crop.requiresOxygen) continue;
                    return true;
                }
                return false;
            }
        }

        public bool AllowsTrees => boxSize == PlantBoxSize.Large;

        /// <summary>"Growing", "Ripe", "Dry", "Empty" - the word a prompt prints.</summary>
        public string StateLabel
        {
            get
            {
                if (IsEmpty) return "EMPTY";
                if (DeadCount > 0 && DeadCount == PlantedCount) return "DEAD";
                if (ReadyCount > 0) return ReadyCount + " READY";
                if (NeedsWater) return "DRY";
                if (NeedsOxygen) return "NO O2";
                return (Growth01 * 100f).ToString("0") + "%";
            }
        }

        private Transform[] _cellRoots = Array.Empty<Transform>();
        private MeshRenderer[][] _cellRenderers = Array.Empty<MeshRenderer[]>();
        private MeshRenderer[] _soilRenderers = Array.Empty<MeshRenderer>();
        private MeshRenderer _waterGauge;
        private int[] _lastStage = Array.Empty<int>();
        private float _envTimer;
        private float _dumpTimer;
        private bool sprinklerFeeding;
        private bool hasOxygen;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly Color DeadTint = new Color(0.42f, 0.31f, 0.18f);
        private static readonly Color DrySoil = new Color(0.45f, 0.35f, 0.22f);
        private static readonly Color WetSoil = new Color(0.25f, 0.18f, 0.10f);

        // ── Lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            EnsureSlots();
            ResolveTreeItems();
            hasOxygen = FarmEnvironment.HasBreathableAir(transform.position);
            CacheVisuals();
            RefreshAllVisuals(force: true);
        }

        private void Update()
        {
            _envTimer += Time.deltaTime;
            if (_envTimer >= Mathf.Max(0.25f, environmentCheckInterval))
            {
                _envTimer = 0f;
                sprinklerFeeding = SampleSprinkler();
                hasOxygen = FarmEnvironment.HasBreathableAir(transform.position);
            }

            TickGrowth();
            TickWater();
            TryDumpToPort();
            RefreshWaterGauge();
        }

        /// <summary>Keep the slot array the size the footprint says it is.</summary>
        private void EnsureSlots()
        {
            int side = Mathf.Clamp(cellsPerSide <= 0 ? SideForSize(boxSize) : cellsPerSide, 1, 4);
            int wanted = side * side;

            if (slots == null) slots = new Slot[0];
            if (slots.Length != wanted)
            {
                var next = new Slot[wanted];
                for (int i = 0; i < wanted; i++)
                    next[i] = i < slots.Length && slots[i] != null ? slots[i] : new Slot();
                slots = next;
            }
            else
            {
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] == null) slots[i] = new Slot();
            }

            if (_lastStage == null || _lastStage.Length != wanted)
            {
                _lastStage = new int[wanted];
                for (int i = 0; i < wanted; i++) _lastStage[i] = -1;
            }
            if (droughtTimers == null || droughtTimers.Length != wanted)
            {
                var nextTimers = new float[wanted];
                if (droughtTimers != null)
                    for (int i = 0; i < wanted && i < droughtTimers.Length; i++) nextTimers[i] = droughtTimers[i];
                droughtTimers = nextTimers;
            }
        }

        private static int SideForSize(PlantBoxSize size)
        {
            switch (size)
            {
                case PlantBoxSize.Large: return 3;
                case PlantBoxSize.Big: return 2;
                default: return 1;
            }
        }

        private void ResolveTreeItems()
        {
            if (woodLogItem != null && saplingItem != null && biomassItem != null) return;

            ItemDefinition Find(string id, string fallbackAsset)
            {
                var found = VoxelEngine.Persistence.WorldStatePersistence.Instance != null
                    ? VoxelEngine.Persistence.WorldStatePersistence.Instance.FindItemById(id)
                    : null;
                if (found != null) return found;
                var all = Resources.FindObjectsOfTypeAll<ItemDefinition>();
                for (int i = 0; i < all.Length; i++)
                {
                    var item = all[i];
                    if (item == null) continue;
                    if (!string.IsNullOrEmpty(item.itemId) && item.itemId == id) return item;
                    if (item.name == fallbackAsset) return item;
                }
                return null;
            }

            if (woodLogItem == null) woodLogItem = Find("wood_log", "Item_WoodLog");
            if (saplingItem == null) saplingItem = Find("seed_tree_sapling", "Seed_TreeSapling");
            if (biomassItem == null) biomassItem = Find("biomass", "Item_Biomass") ?? Find("item_biomass", "Item_Biomass");
        }

        // ── Growth ──────────────────────────────────────────────────────────
        private void TickGrowth()
        {
            float dt = Time.deltaTime;
            bool oxygenOk = hasOxygen;
            bool irrigated = IsIrrigated;

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || slot.dead) continue;
                bool planted = slot.crop != null || slot.tree;
                if (!planted || slot.growth >= 1f) continue;

                if (slot.tree)
                {
                    float treeSpeed = 1f / Mathf.Max(1f, treeGrowthSeconds);
                    if (irrigated) treeSpeed *= Mathf.Max(1f, treeIrrigatedSpeedMultiplier);
                    slot.growth = Mathf.Clamp01(slot.growth + treeSpeed * dt);
                    RefreshCellVisual(i, force: false);
                    continue;
                }

                var crop = slot.crop;
                if (crop.requiresOxygen && !oxygenOk) continue;   // pauses, does not die

                float speed = 1f / Mathf.Max(0.5f, crop.growthTime);
                if (irrigated) speed *= Mathf.Max(1f, crop.irrigatedSpeedMultiplier);
                else if (crop.requiresWater) speed *= 0.3f;
                if (slot.fertilized) speed *= Mathf.Max(1f, fertilizedSpeedMultiplier);

                slot.growth = Mathf.Clamp01(slot.growth + speed * dt);
                RefreshCellVisual(i, force: false);
            }
        }

        /// <summary>
        /// Drink from the box's own tank. A crop that runs dry for longer than twice its
        /// drought tolerance is lost, and the cell keeps it, brown, until somebody clears
        /// it - the same rule every other bed in the project follows.
        /// </summary>
        private void TickWater()
        {
            if (sprinklerFeeding) return;

            float dt = Time.deltaTime;
            int drinking = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null || s.dead) continue;
                if (s.crop == null && !s.tree) continue;
                if (s.growth >= 1f) continue;
                drinking++;
            }

            if (drinking > 0 && waterStored > 0f)
                waterStored = Mathf.Max(0f, waterStored - litresPerCellSecond * drinking * dt);

            bool dry = waterStored <= 0.01f;
            if (!dry) return;

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || slot.dead || slot.tree) continue;
                if (slot.crop == null || !slot.crop.requiresWater || slot.growth >= 1f) continue;
                slot.growth = Mathf.Clamp01(slot.growth);

                // Drought is measured by the growth a cell has lost the chance to make:
                // a dry cell simply stops advancing and keeps its water deficit on the
                // screen as a falling gauge. It is lost only after the tolerance expires,
                // which the accumulator below tracks.
                droughtTimers[i] += dt;
                if (droughtTimers[i] > Mathf.Max(1f, slot.crop.droughtToleranceSec * 2f))
                {
                    slot.dead = true;
                    slot.fertilized = false;
                    droughtTimers[i] = 0f;
                    Debug.Log($"[Farm] {slot.crop.cropName} dried out in a {BedName} and is now dead.");
                    RefreshCellVisual(i, force: true);
                }
            }

            // Any cell that got water this frame forgets its deficit.
            for (int i = 0; i < droughtTimers.Length; i++)
                if (i >= slots.Length || slots[i] == null || slots[i].crop == null || slots[i].dead)
                    droughtTimers[i] = 0f;
        }

        private float[] droughtTimers = Array.Empty<float>();

        private bool SampleSprinkler()
        {
            var sprinklers = FindObjectsByType<Sprinkler>(FindObjectsInactive.Exclude);
            float radiusSq = irrigationCheckRadius * irrigationCheckRadius;
            for (int i = 0; i < sprinklers.Length; i++)
            {
                var s = sprinklers[i];
                if (s == null || !s.IsActive) continue;
                if (Vector3.SqrMagnitude(s.transform.position - transform.position) <= radiusSq) return true;
            }
            return false;
        }

        // ── Player actions ──────────────────────────────────────────────────
        // Every mutation the player can make goes through one of these, so the block
        // itself is the authority for its own cells (MP readiness rule).

        /// <summary>Which cell a world-space hit point belongs to, or -1.</summary>
        public int SlotAt(Vector3 worldPoint)
        {
            EnsureSlots();
            int side = cellsPerSide <= 0 ? SideForSize(boxSize) : cellsPerSide;
            if (side <= 1) return 0;

            var local = transform.InverseTransformPoint(worldPoint);
            float span = 1f / side;
            int col = Mathf.Clamp(Mathf.FloorToInt(local.x / span + side * 0.5f), 0, side - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(local.z / span + side * 0.5f), 0, side - 1);
            return Mathf.Clamp(row * side + col, 0, slots.Length - 1);
        }

        /// <summary>Plant a seed or a tree sapling into the cell under <paramref name="worldPoint"/>,
        /// or into the first free cell when that one is taken. Returns -1 when it cannot plant.</summary>
        public int TryPlantAt(Vector3 worldPoint, ItemDefinition item)
        {
            if (item == null) return -1;
            EnsureSlots();

            bool isTree = item is SeedItem seedItem && seedItem.crop == null
                          && TreeFarmPlot.IsSaplingOrTreeSeed(item);
            var crop = item is SeedItem si ? si.crop : null;
            if (!isTree && crop == null) return -1;
            if (isTree && !AllowsTrees) return -2;   // caller reports "needs a Large bed"

            int wanted = SlotAt(worldPoint);
            int index = IsFree(wanted) ? wanted : FirstFreeIndex();
            if (index < 0) return -1;

            var slot = slots[index];
            slot.tree = isTree;
            slot.crop = isTree ? null : crop;
            slot.growth = 0f;
            slot.fertilized = false;
            slot.dead = false;
            droughtTimers[index] = 0f;
            _lastStage[index] = -1;
            RefreshCellVisual(index, force: true);
            RefreshWaterGauge();
            return index;
        }

        /// <summary>Harvest one ready cell into a container, with the box's own replant rule.</summary>
        public bool TryHarvestAt(Vector3 worldPoint, Inventory inventory, out FarmHarvestYield harvest)
        {
            harvest = default;
            int index = SlotAt(worldPoint);
            if (!IsReadyIndex(index)) index = FirstReadyIndex();
            if (index < 0) return false;

            var slot = slots[index];
            var crop = slot.crop;
            harvest = PeekSlotHarvest(index);

            if (inventory != null)
            {
                if (harvest.item != null && harvest.count > 0) inventory.Add(harvest.item, harvest.count);
                if (harvest.seedItem != null && harvest.seedCount > 0) inventory.Add(harvest.seedItem, harvest.seedCount);
            }

            if (slot.tree)
            {
                // A timber cell is felled: the cell is emptied rather than replanted,
                // because a sapling is only ever planted on purpose.
                ResetSlot(index);
            }
            else
            {
                slot.growth = 0f;
                slot.fertilized = false;
                droughtTimers[index] = 0f;
                _lastStage[index] = -1;
                RefreshCellVisual(index, force: true);
            }

            return crop != null || harvest.item != null;
        }

        /// <summary>Clear the lost crop in the cell under the hit, or in the first dead cell.</summary>
        public bool TryClearDeadAt(Vector3 worldPoint, out string clearedName)
        {
            clearedName = null;
            int index = SlotAt(worldPoint);
            if (index < 0 || index >= slots.Length || slots[index] == null || !slots[index].dead)
                index = FirstDeadIndex();
            if (index < 0) return false;

            var slot = slots[index];
            clearedName = slot.tree ? "Timber sapling" : (slot.crop != null ? slot.crop.cropName : "Crop");
            ResetSlot(index);
            RefreshCellVisual(index, force: true);
            return true;
        }

        public bool TryFertilizeAt(Vector3 worldPoint, ItemDefinition item)
        {
            if (item == null || !TreeFarmPlot.IsFertilizerOrBiomass(item)) return false;
            int index = SlotAt(worldPoint);
            if (index < 0 || index >= slots.Length) return false;
            var slot = slots[index];
            if (slot == null || slot.dead || slot.fertilized) return false;
            if (slot.crop == null && !slot.tree) return false;
            slot.fertilized = true;
            return true;
        }

        /// <summary>Pour water in by hand. Returns the litres actually accepted.</summary>
        public float TryAddWater(float litres)
        {
            if (litres <= 0f || waterCapacity <= 0f) return 0f;
            float space = Mathf.Max(0f, waterCapacity - waterStored);
            float added = Mathf.Min(space, litres);
            waterStored += added;
            RefreshWaterGauge();
            return added;
        }

        // ── Slot helpers ────────────────────────────────────────────────────
        private bool IsFree(int index) => index >= 0 && index < slots.Length
            && slots[index] != null && slots[index].crop == null && !slots[index].tree;

        private int FirstFreeIndex()
        {
            for (int i = 0; i < slots.Length; i++) if (IsFree(i)) return i;
            return -1;
        }

        private int FirstPlantedIndex()
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && (slots[i].crop != null || slots[i].tree)) return i;
            return -1;
        }

        private bool IsReadyIndex(int index) => index >= 0 && index < slots.Length
            && slots[index] != null && !slots[index].dead
            && (slots[index].crop != null || slots[index].tree)
            && slots[index].growth >= 0.999f;

        private int FirstReadyIndex()
        {
            for (int i = 0; i < slots.Length; i++) if (IsReadyIndex(i)) return i;
            return -1;
        }

        private int FirstDeadIndex()
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].dead) return i;
            return -1;
        }

        private void ResetSlot(int index)
        {
            if (index < 0 || index >= slots.Length) return;
            slots[index].crop = null;
            slots[index].tree = false;
            slots[index].growth = 0f;
            slots[index].fertilized = false;
            slots[index].dead = false;
            if (index < droughtTimers.Length) droughtTimers[index] = 0f;
            if (index < _lastStage.Length) _lastStage[index] = -1;
        }

        /// <summary>What one cell would give up right now.</summary>
        public FarmHarvestYield PeekSlotHarvest(int index)
        {
            if (!IsReadyIndex(index)) return default;
            var slot = slots[index];

            if (slot.tree)
            {
                ResolveTreeItems();
                return FarmHarvestYield.Crop(
                    woodLogItem, treeLogsPerHarvest,
                    saplingItem, treeSaplingsPerHarvest);
            }

            var crop = slot.crop;
            int amount = Mathf.Max(1, crop.harvestAmount) + (slot.fertilized ? 1 : 0);
            return FarmHarvestYield.Crop(crop.harvestItem, amount, crop.seedItem,
                Mathf.Max(0, crop.seedReturnAmount));
        }

        /// <summary>
        /// The peek/commit pair the shared harvest service uses. <paramref name="replant"/>
        /// keeps the cell's crop and resets its growth; when the box is on its port policy
        /// the caller passes the box's own setting.
        /// </summary>
        public bool TakeHarvest(bool replant, out FarmHarvestYield harvest)
        {
            harvest = default;
            int index = FirstReadyIndex();
            if (index < 0) return false;

            harvest = PeekSlotHarvest(index);
            var slot = slots[index];

            if (replant && !slot.tree)
            {
                slot.growth = 0f;
                slot.fertilized = false;
                droughtTimers[index] = 0f;
                _lastStage[index] = -1;
                RefreshCellVisual(index, force: true);
            }
            else
            {
                ResetSlot(index);
                RefreshCellVisual(index, force: true);
            }
            return true;
        }

        public bool CanHarvestNow => IsReady;

        /// <summary>What the box would give up right now: its first ripe cell.</summary>
        public FarmHarvestYield PeekHarvest() => PeekSlotHarvest(FirstReadyIndex());

        /// <summary>Clear every lost cell in the box. Used by a Harvester.</summary>
        public int ClearAllDead()
        {
            int cleared = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null || !slots[i].dead) continue;
                ResetSlot(i);
                RefreshCellVisual(i, force: true);
                cleared++;
            }
            return cleared;
        }

        // ── Output port ─────────────────────────────────────────────────────
        private void TryDumpToPort()
        {
            if (!autoDumpWhenRipe) return;
            if (VoxelEngine.Networking.NetworkSession.SimulationIsRemote) return;

            _dumpTimer += Time.deltaTime;
            if (_dumpTimer < Mathf.Max(0.1f, dumpIntervalSeconds)) return;

            int index = FirstReadyIndex();
            if (index < 0) return;
            _dumpTimer = 0f;

            var peek = PeekSlotHarvest(index);
            if (peek.IsEmpty) return;

            bool selfReplant = replantAfterPortHarvest && !slots[index].tree
                && peek.seedItem != null && peek.seedCount > 0;
            int seedsLeaving = selfReplant ? peek.seedCount - 1 : peek.seedCount;

            if (peek.item != null && !FarmItemRouter.HasRoomFor(transform, dumpRadius, peek.item, peek.count)) return;
            if (seedsLeaving > 0 && peek.seedItem != null
                && !FarmItemRouter.HasRoomFor(transform, dumpRadius, peek.seedItem, seedsLeaving)) return;

            var harvest = peek;
            slots[index].growth = 0f;
            if (selfReplant)
            {
                slots[index].fertilized = false;
            }
            else
            {
                ResetSlot(index);
            }
            droughtTimers[index] = 0f;
            _lastStage[index] = -1;
            RefreshCellVisual(index, force: true);

            if (harvest.item != null && harvest.count > 0)
                FarmItemRouter.Push(transform, dumpRadius, harvest.item, harvest.count);
            if (seedsLeaving > 0 && harvest.seedItem != null)
                FarmItemRouter.Push(transform, dumpRadius, harvest.seedItem, seedsLeaving);

            // A tree also drops biomass, which never travels with the log stack.
            if (slots[index].tree || harvest.item == woodLogItem)
            {
                ResolveTreeItems();
                if (biomassItem != null && treeBiomassPerHarvest > 0)
                    FarmItemRouter.Push(transform, dumpRadius, biomassItem, treeBiomassPerHarvest);
            }
        }

        // ── Persistence ─────────────────────────────────────────────────────
        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureSlots();

            state.SetExtra("boxSlots", slots.Length);
            state.SetExtra("water", Mathf.Max(0f, waterStored));
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;
                state.SetText("c" + i, slot.crop != null ? slot.crop.name
                    : slot.tree ? "TREE" : string.Empty);
                // The seed item is the second key: a crop asset renamed by a modder still
                // resolves, because the item catalog outlives the crop name.
                state.SetText("s" + i, slot.crop != null && slot.crop.seedItem != null
                    ? slot.crop.seedItem.itemId : string.Empty);
                state.SetExtra("g" + i, Mathf.Clamp01(slot.growth));
                state.SetExtra("f" + i, slot.fertilized ? 1f : 0f);
                state.SetExtra("d" + i, slot.dead ? 1f : 0f);
            }
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null || state.IsEmpty) return;
            EnsureSlots();

            waterStored = Mathf.Clamp(
                MachineProcessPersistence.FiniteOr(state.GetExtra("water", waterStored), waterStored),
                0f, Mathf.Max(0f, waterCapacity));

            for (int i = 0; i < slots.Length; i++)
            {
                string cropName = state.GetText("c" + i, string.Empty);
                if (string.IsNullOrEmpty(cropName))
                {
                    ResetSlot(i);
                    continue;
                }

                var slot = slots[i];
                slot.growth = MachineProcessPersistence.ClampOr(state.GetExtra("g" + i, 0f), 0f, 1f, 0f);
                slot.fertilized = state.GetExtra("f" + i, 0f) >= 0.5f;
                slot.dead = state.GetExtra("d" + i, 0f) >= 0.5f;

                if (cropName == "TREE")
                {
                    slot.tree = true;
                    slot.crop = null;
                }
                else
                {
                    slot.tree = false;
                    slot.crop = CropCatalog.Resolve(cropName, state.GetText("s" + i, string.Empty));
                    if (slot.crop == null) ResetSlot(i);
                }
            }

            _lastStage = new int[slots.Length];
            for (int i = 0; i < slots.Length; i++) _lastStage[i] = -1;
            RefreshAllVisuals(force: true);
        }

        // ── Visuals ─────────────────────────────────────────────────────────
        /// <summary>
        /// Build the bed geometry and one crop pod per cell. Called by the editor setup
        /// before the prefab is saved, so a placed box never instantiates anything at
        /// play time.
        /// </summary>
        public void RebuildVisuals()
        {
            var existing = transform.Find("Generated_PlantBoxVisuals");
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }

            if (!Application.isPlaying) BuildVisuals();
            CacheVisuals();
            RefreshAllVisuals(force: true);
        }

        private void BuildVisuals()
        {
            EnsureSlots();
            int side = cellsPerSide <= 0 ? SideForSize(boxSize) : cellsPerSide;
            float span = 1f / side;
            bool large = boxSize == PlantBoxSize.Large;

            var root = new GameObject("Generated_PlantBoxVisuals").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            Color frameSteel = new Color(0.24f, 0.27f, 0.30f);
            Color curbTimber = new Color(0.42f, 0.28f, 0.16f);
            Color richSoil = new Color(0.26f, 0.17f, 0.10f);
            Color waterChannel = new Color(0.20f, 0.62f, 0.88f);
            Color cropGreen = new Color(0.30f, 0.66f, 0.28f);

            // Base tray and four timber curbs scaled to the footprint.
            MakePart(root, PrimitiveType.Cube, "BaseTray",
                new Vector3(0f, 0.08f, 0f), new Vector3(0.98f, 0.16f, 0.98f), frameSteel);
            MakePart(root, PrimitiveType.Cube, "Curb_N", new Vector3(0f, 0.22f, 0.46f), new Vector3(0.98f, 0.14f, 0.08f), curbTimber);
            MakePart(root, PrimitiveType.Cube, "Curb_S", new Vector3(0f, 0.22f, -0.46f), new Vector3(0.98f, 0.14f, 0.08f), curbTimber);
            MakePart(root, PrimitiveType.Cube, "Curb_E", new Vector3(0.46f, 0.22f, 0f), new Vector3(0.08f, 0.14f, 0.98f), curbTimber);
            MakePart(root, PrimitiveType.Cube, "Curb_W", new Vector3(-0.46f, 0.22f, 0f), new Vector3(0.08f, 0.14f, 0.98f), curbTimber);

            // One soil cell per slot, plus a crop pod above it.
            for (int r = 0; r < side; r++)
            for (int c = 0; c < side; c++)
            {
                int index = r * side + c;
                float x = (c + 0.5f) * span - 0.5f;
                float z = (r + 0.5f) * span - 0.5f;

                MakePart(root, PrimitiveType.Cube, "Soil_" + index,
                    new Vector3(x, 0.185f, z), new Vector3(span * 0.86f, 0.06f, span * 0.86f), richSoil);

                var cell = new GameObject("Crop_" + index).transform;
                cell.SetParent(root, false);
                cell.localPosition = new Vector3(x, 0.215f, z);
                cell.localRotation = Quaternion.identity;

                float s = span * 0.75f;
                MakePart(cell, PrimitiveType.Cylinder, "Stalk",
                    new Vector3(0f, 0.14f, 0f), new Vector3(0.05f * s, 0.14f, 0.05f * s), new Color(0.34f, 0.52f, 0.22f));
                MakePart(cell, PrimitiveType.Cube, "Leaf_A",
                    new Vector3(0.07f * s, 0.20f, 0.02f), new Vector3(0.18f * s, 0.03f, 0.09f * s), cropGreen, new Vector3(0f, 0f, 22f));
                MakePart(cell, PrimitiveType.Cube, "Leaf_B",
                    new Vector3(-0.07f * s, 0.26f, -0.01f), new Vector3(0.17f * s, 0.03f, 0.09f * s), cropGreen, new Vector3(0f, 0f, -20f));
                MakePart(cell, PrimitiveType.Cube, "Leaf_C",
                    new Vector3(0.01f, 0.31f, 0.06f * s), new Vector3(0.13f * s, 0.03f, 0.08f * s), cropGreen, new Vector3(12f, 30f, 0f));
            }

            // Irrigation strip along the inside of the tray.
            MakePart(root, PrimitiveType.Cube, "Irrig_Strip",
                new Vector3(0f, 0.215f, 0.36f), new Vector3(0.72f, 0.02f, 0.05f), waterChannel);

            // Output port on the +X face: a short discharge chute with its own indicator.
            MakePart(root, PrimitiveType.Cube, "OutputPort_Chute",
                new Vector3(0.50f, 0.14f, 0f), new Vector3(0.20f, 0.12f, 0.34f), frameSteel);
            MakePart(root, PrimitiveType.Cube, "OutputPort_Lip",
                new Vector3(0.57f, 0.19f, 0f), new Vector3(0.05f, 0.02f, 0.30f), waterChannel);
            MakePart(root, PrimitiveType.Cube, "OutputPort_Indicator",
                new Vector3(0.50f, 0.21f, 0.18f), new Vector3(0.06f, 0.03f, 0.06f), new Color(0.22f, 0.44f, 0.34f));

            // Water gauge: a thin sight glass on the -X face, so the hand-watered state is
            // readable without opening anything.
            MakePart(root, PrimitiveType.Cube, "WaterGauge_Back",
                new Vector3(-0.50f, 0.14f, 0f), new Vector3(0.04f, 0.20f, 0.10f), frameSteel);
            MakePart(root, PrimitiveType.Cube, "WaterGauge",
                new Vector3(-0.52f, 0.14f, 0f), new Vector3(0.02f, 0.16f, 0.07f), waterChannel);

            if (large)
            {
                // A Large bed gets corner posts tall enough to read as a bed a tree sits in.
                MakePart(root, PrimitiveType.Cube, "Post_NW", new Vector3(-0.46f, 0.55f, 0.46f), new Vector3(0.08f, 0.9f, 0.08f), frameSteel);
                MakePart(root, PrimitiveType.Cube, "Post_NE", new Vector3(0.46f, 0.55f, 0.46f), new Vector3(0.08f, 0.9f, 0.08f), frameSteel);
                MakePart(root, PrimitiveType.Cube, "Post_SW", new Vector3(-0.46f, 0.55f, -0.46f), new Vector3(0.08f, 0.9f, 0.08f), frameSteel);
                MakePart(root, PrimitiveType.Cube, "Post_SE", new Vector3(0.46f, 0.55f, -0.46f), new Vector3(0.08f, 0.9f, 0.08f), frameSteel);
                MakePart(root, PrimitiveType.Cube, "Treillage_Top", new Vector3(0f, 0.98f, 0f), new Vector3(0.9f, 0.04f, 0.06f), frameSteel);
            }

            MakePart(root, PrimitiveType.Cube, "StatusBeacon",
                new Vector3(0.40f, 0.28f, 0.40f), new Vector3(0.06f, 0.06f, 0.06f), new Color(0.52f, 0.86f, 0.22f));
        }

        private void CacheVisuals()
        {
            EnsureSlots();
            var vis = transform.Find("Generated_PlantBoxVisuals");

            _cellRoots = new Transform[slots.Length];
            _cellRenderers = new MeshRenderer[slots.Length][];
            for (int i = 0; i < slots.Length; i++)
            {
                var cell = vis != null ? vis.Find("Crop_" + i) : null;
                _cellRoots[i] = cell;
                _cellRenderers[i] = cell != null
                    ? cell.GetComponentsInChildren<MeshRenderer>(true)
                    : Array.Empty<MeshRenderer>();
            }

            var soil = new List<MeshRenderer>();
            for (int i = 0; i < slots.Length; i++)
            {
                var s = vis != null ? vis.Find("Soil_" + i) : null;
                if (s != null)
                {
                    var mr = s.GetComponent<MeshRenderer>();
                    if (mr != null) soil.Add(mr);
                }
            }
            _soilRenderers = soil.ToArray();

            var gauge = vis != null ? vis.Find("WaterGauge") : null;
            _waterGauge = gauge != null ? gauge.GetComponent<MeshRenderer>() : null;

        }

        private void RefreshAllVisuals(bool force)
        {
            for (int i = 0; i < slots.Length; i++) RefreshCellVisual(i, force);
            RefreshWaterGauge();
            RefreshSoil();
        }

        private void RefreshCellVisual(int index, bool force)
        {
            if (index < 0 || index >= _cellRoots.Length) return;
            var root = _cellRoots[index];
            if (root == null) return;

            var slot = slots[index];
            bool planted = slot != null && (slot.crop != null || slot.tree);
            if (!planted)
            {
                root.gameObject.SetActive(false);
                if (index < _lastStage.Length) _lastStage[index] = -1;
                return;
            }

            root.gameObject.SetActive(true);

            if (slot.dead)
            {
                if (!force && _lastStage[index] == -2) return;
                _lastStage[index] = -2;
                root.localScale = new Vector3(0.7f, 0.42f, 0.7f);
                Tint(index, DeadTint);
                return;
            }

            int stages = slot.tree ? 5 : Mathf.Max(1, slot.crop.growthStages);
            int stage = Mathf.Clamp(Mathf.FloorToInt(slot.growth * stages), 0, stages - 1);
            if (!force && stage == _lastStage[index]) return;
            _lastStage[index] = stage;

            float scale = 1f;
            if (slot.tree)
            {
                // A tree scales taller than a crop: a mature sapling reads as a young trunk.
                scale = Mathf.Lerp(0.35f, 2.6f, slot.growth);
            }
            else if (slot.crop.stageScales != null && slot.crop.stageScales.Length > 0)
            {
                scale = slot.crop.stageScales[Mathf.Clamp(stage, 0, slot.crop.stageScales.Length - 1)];
            }

            root.localScale = new Vector3(Mathf.Max(0.05f, scale), Mathf.Max(0.05f, scale), Mathf.Max(0.05f, scale));

            if (slot.tree)
            {
                Tint(index, new Color(0.22f, 0.42f, 0.18f));
                return;
            }
            if (slot.crop.stageColors != null && slot.crop.stageColors.Length > 0)
                Tint(index, slot.crop.stageColors[Mathf.Clamp(stage, 0, slot.crop.stageColors.Length - 1)]);
        }

        private void Tint(int index, Color color)
        {
            if (index < 0 || index >= _cellRenderers.Length) return;
            var block = new MaterialPropertyBlock();
            var set = _cellRenderers[index];
            for (int i = 0; i < set.Length; i++)
            {
                var r = set[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                r.SetPropertyBlock(block);
            }
        }

        private void RefreshSoil()
        {
            if (_soilRenderers == null) return;
            Color tint = IsIrrigated ? WetSoil : DrySoil;
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < _soilRenderers.Length; i++)
            {
                var r = _soilRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, tint);
                block.SetColor(ColorId, tint);
                r.SetPropertyBlock(block);
            }
        }

        private void RefreshWaterGauge()
        {
            if (_waterGauge == null) return;
            float level = Water01;
            Color tint = Color.Lerp(new Color(0.18f, 0.30f, 0.44f), new Color(0.20f, 0.78f, 0.98f), level);
            var block = new MaterialPropertyBlock();
            _waterGauge.GetPropertyBlock(block);
            block.SetColor(BaseColorId, tint);
            block.SetColor(ColorId, tint);
            _waterGauge.SetPropertyBlock(block);
            _waterGauge.transform.localScale = new Vector3(0.02f, Mathf.Max(0.02f, 0.16f * level), 0.07f);
            RefreshSoil();
        }

        private static void MakePart(Transform parent, PrimitiveType type, string name,
            Vector3 pos, Vector3 scale, Color color, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;

            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = scale;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && Application.isPlaying)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor(BaseColorId, color);
                mpb.SetColor(ColorId, color);
                mr.SetPropertyBlock(mpb);
            }
        }
    }
}
