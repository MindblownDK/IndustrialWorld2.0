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
        [Tooltip("Global growth scale for this bed. Crops are meant to be watched: at 0.5 a 90-second crop "
                 + "takes three minutes watered, and every stage is visible on the block.")]
        [Range(0.05f, 2f)] public float growthSpeedMultiplier = 0.5f;

        [Header("Power (Auto Farm Plot)")]
        [Tooltip("Watts this bed draws while something is growing. Zero means the box needs no power at all, "
                 + "which is what the hand-watered planting boxes use.")]
        public float powerDraw = 0f;
        [Tooltip("When set, a bed with a power draw stops growing while the power is off instead of running on nothing.")]
        public bool requiresPower = true;

        [Header("Trees")]
        [Tooltip("Whether this bed family accepts a tree sapling at all. Only a Large bed can, and only when this is on.")]
        public bool allowTrees = true;

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

        [Header("Tree Bed")]
        [Tooltip("True when a tree owns this whole bed. Only a Large bed can be a tree bed: a tree takes the plot, " +
                 "so its nine cells are not shared with crops while it grows.")]
        public bool treeBed = false;
        [Tooltip("0..1 growth of the tree on a tree bed. Unused when no tree is planted.")]
        [Range(0f, 1f)] public float treeGrowth = 0f;

        /// <summary>Which cell the tree stands in, -1 when the bed holds no tree (18.7.3-dev).
        /// The tree still fills the whole bed visually; the cell is what the panel points at
        /// and what the other cells are locked against.</summary>
        public int treeCell = -1;

        [Header("Containers")]
        [Tooltip("Seeds and saplings waiting for a free cell. The bed plants from here on its own.")]
        public ItemContainer seedInput;
        [Tooltip("Produce taken off the bed, waiting for a route. Also readable and collectable from the panel.")]
        public ItemContainer harvestOutput;

        /// <summary>One one-slot bin per cell (18.7.3-dev). Dragging a seed into a cell's slot
        /// plants that cell and nothing else, which is what "put items into the cells" means
        /// when a bed has nine of them. Transient: a queued seed is planted immediately.</summary>
        public ItemContainer[] cellInput = new ItemContainer[0];

        public const int SEED_SLOTS = 4;
        public const int OUTPUT_SLOTS = 9;

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
                if (treeBed) return "Timber";
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
                if (treeBed) return Mathf.Clamp01(treeGrowth);
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
        public bool IsEmpty { get { return !treeBed && PlantedCount == 0; } }
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
                if (treeBed) return 1;
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
                if (treeBed) return treeGrowth >= 1f ? 1 : 0;
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
                if (treeBed) return 0;   // a tree does not dry out into a lost crop
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

        public bool AllowsTrees => allowTrees && boxSize == PlantBoxSize.Large;

        // ── Panel surface (18.7.0-dev) ──────────────────────────────────────
        // The bed's own panel reads these instead of poking at slots and private
        // flags, so the UI has one door into the block like every other machine.
        public bool TreeBed => treeBed;
        public float TreeGrowth01 => Mathf.Clamp01(treeGrowth);
        public bool HasAir => hasOxygen;
        public int CellCount => SlotCount;
        public float CellsFill01 => SlotCount <= 0 ? 0f : Mathf.Clamp01((float)PlantedCount / SlotCount);
        public Slot CellAt(int index)
            => slots != null && index >= 0 && index < slots.Length ? slots[index] : null;

        /// <summary>"Growing", "Ripe", "Dry", "Empty" - the word a prompt prints.</summary>
        public string StateLabel
        {
            get
            {
                if (IsEmpty) return "EMPTY";
                if (treeBed) return ReadyCount > 0 ? "TIMBER READY" : (treeGrowth * 100f).ToString("0") + "%";
                if (DeadCount > 0 && DeadCount == PlantedCount) return "DEAD";
                if (ReadyCount > 0) return ReadyCount + " READY";
                if (PowerMissing) return "NO POWER";
                if (NeedsWater) return "DRY";
                if (NeedsOxygen) return "NO O2";
                return (Growth01 * 100f).ToString("0") + "%";
            }
        }

        /// <summary>True when this bed draws power and has it right now.</summary>
        public bool HasPower
        {
            get
            {
                if (powerDraw <= 0f) return true;      // an unpowered bed is never unpowered
                if (_power == null) return true;       // authored without a consumer yet
                return _power.IsPowered;
            }
        }

        /// <summary>True when the block has power available where power is needed.</summary>
        public bool PowerMissing => requiresPower && !HasPower;

        private VoxelEngine.Power.PowerConsumer _power;
        private Transform[] _cellRoots = Array.Empty<Transform>();
        private MeshRenderer[][] _cellRenderers = Array.Empty<MeshRenderer[]>();
        private MeshRenderer[][] _cellLeafRenderers = Array.Empty<MeshRenderer[]>();
        private MeshRenderer[][] _cellStemRenderers = Array.Empty<MeshRenderer[]>();
        private MeshRenderer[][] _cellFruitRenderers = Array.Empty<MeshRenderer[]>();
        private MeshRenderer[] _soilRenderers = Array.Empty<MeshRenderer>();
        private MeshRenderer _waterGauge;
        private int[] _lastStage = Array.Empty<int>();
        private float _envTimer;
        private float _dumpTimer;
        private float _plantTimer;
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
            _power = GetComponent<VoxelEngine.Power.PowerConsumer>();
            EnsureSlots();
            EnsureContainers();
            ResolveTreeItems();
            hasOxygen = FarmEnvironment.HasBreathableAir(transform.position);
            CacheVisuals();
            RefreshAllVisuals(force: true);
            RefreshTreeVisual();
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

            // A bed with seeds in its bin fills itself, so a farm can be set up once.
            _plantTimer += Time.deltaTime;
            if (_plantTimer >= 1f)
            {
                _plantTimer = 0f;
                int guard = 0;
                while (PlantFromSeedBin() && guard++ < 9) { }
                PlantFromCellInputs();
            }

            TryDumpToPort();
            RefreshWaterGauge();
        }

        /// <summary>
        /// Create or resize the two containers. The seed bin is filtered to what this bed can
        /// actually grow - which is how a Small box refuses a sapling before the player has
        /// walked over with one.
        /// </summary>
        public void EnsureContainers()
        {
            if (seedInput == null) seedInput = new ItemContainer("Seeds", SEED_SLOTS);
            else if (seedInput.Size != SEED_SLOTS) seedInput.Resize(SEED_SLOTS);
            seedInput.AcceptFilter = (item, wanted) => CanPlant(item) ? wanted : 0;

            if (harvestOutput == null) harvestOutput = new ItemContainer("Harvest", OUTPUT_SLOTS);
            else if (harvestOutput.Size != OUTPUT_SLOTS) harvestOutput.Resize(OUTPUT_SLOTS);

            EnsureCellInputs();
        }

        /// <summary>Create or resize the one-slot bin that stands in front of each cell.</summary>
        public void EnsureCellInputs()
        {
            EnsureSlots();
            int cells = slots.Length;
            if (cellInput == null || cellInput.Length != cells)
            {
                var resized = new ItemContainer[cells];
                for (int i = 0; i < cells; i++)
                    resized[i] = cellInput != null && i < cellInput.Length ? cellInput[i] : null;
                cellInput = resized;
            }

            for (int i = 0; i < cells; i++)
            {
                if (cellInput[i] == null) cellInput[i] = new ItemContainer("Cell " + (i + 1), 1);
                else if (cellInput[i].Size != 1) cellInput[i].Resize(1);

                int cell = i;
                // The bin only ever accepts what THAT cell would take right now, so a locked
                // cell refuses the drag instead of silently swallowing a seed.
                cellInput[i].AcceptFilter = (item, wanted) => CanPlantInCell(cell, item) ? wanted : 0;
            }
        }

        /// <summary>The one-slot bin for one cell, created on demand for the panel.</summary>
        public ItemContainer CellInput(int index)
        {
            EnsureCellInputs();
            return index >= 0 && index < cellInput.Length ? cellInput[index] : null;
        }

        /// <summary>
        /// Drain the per-cell bins, one plant per bin. Runs on the same one-second beat as the
        /// seed bin, and reports how many cells it filled.
        /// </summary>
        private int PlantFromCellInputs()
        {
            if (cellInput == null) return 0;
            int planted = 0;
            for (int i = 0; i < cellInput.Length && i < slots.Length; i++)
            {
                var bin = cellInput[i];
                if (bin == null || bin.Size <= 0) continue;
                var stack = bin.GetSlot(0);
                if (stack == null || stack.IsEmpty || stack.item == null) continue;
                if (!CanPlantInCell(i, stack.item)) continue;

                int result = PlantInCell(i, stack.item);
                if (result < 0) continue;
                bin.Remove(stack.item, 1);
                planted++;
            }
            return planted;
        }

        /// <summary>True when this bed would accept <paramref name="item"/> right now.</summary>
        public bool CanPlant(ItemDefinition item)
        {
            if (item == null) return false;
            bool isTree = item is SeedItem seed && seed.crop == null && TreeFarmPlot.IsSaplingOrTreeSeed(item);
            if (isTree) return AllowsTrees;
            return item is SeedItem si && si.crop != null;
        }

        /// <summary>Take one seed or sapling out of the bin into a free cell, or claim the bed with a tree.</summary>
        private bool PlantFromSeedBin()
        {
            if (seedInput == null || treeBed) return false;
            for (int i = 0; i < seedInput.Size; i++)
            {
                var slot = seedInput.GetSlot(i);
                if (slot == null || slot.IsEmpty || !CanPlant(slot.item)) continue;
                int planted = TryPlantAt(transform.position, slot.item);
                if (planted < 0) continue;
                seedInput.Remove(slot.item, 1);
                return true;
            }
            return false;
        }

        /// <summary>Keep the slot array the size the footprint says it is.</summary>
        public void EnsureSlots()
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

            // A powered bed that has lost its power holds its crops where they are. It
            // does not kill them and it does not keep growing on an empty bus.
            if (PowerMissing) return;

            float scale = Mathf.Max(0f, growthSpeedMultiplier);

            if (treeBed)
            {
                // A tree owns the bed: it grows on its own clock, drinks like one cell, and
                // the nine crop cells are not used while it is there. It does not pause for
                // room air - an indoor tree gets its carbon from the room's own cycle, and a
                // tree bed that stalled on a technicality reads as a bug.
                if (treeGrowth < 1f)
                {
                    float treeSpeed = (1f / Mathf.Max(1f, treeGrowthSeconds)) * scale;
                    if (irrigated) treeSpeed *= Mathf.Max(1f, treeIrrigatedSpeedMultiplier);
                    treeGrowth = Mathf.Clamp01(treeGrowth + treeSpeed * dt);
                    RefreshTreeVisual();
                    HideCropPods();
                }
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || slot.dead) continue;
                bool planted = slot.crop != null || slot.tree;
                if (!planted || slot.growth >= 1f) continue;

                if (slot.tree)
                {
                    float treeSpeed = (1f / Mathf.Max(1f, treeGrowthSeconds)) * scale;
                    if (irrigated) treeSpeed *= Mathf.Max(1f, treeIrrigatedSpeedMultiplier);
                    slot.growth = Mathf.Clamp01(slot.growth + treeSpeed * dt);
                    RefreshCellVisual(i, force: false);
                    continue;
                }

                var crop = slot.crop;
                if (crop.requiresOxygen && !oxygenOk) continue;   // pauses, does not die

                float speed = (1f / Mathf.Max(0.5f, crop.growthTime)) * scale;
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
            int drinking = treeBed ? 1 : 0;
            for (int i = 0; i < slots.Length && !treeBed; i++)
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

            bool isTree = IsTreeSeed(item);
            if (!isTree && CropOf(item) == null) return -1;
            if (isTree && !AllowsTrees) return -2;   // caller reports "needs a Large bed"

            int wanted = SlotAt(worldPoint);
            int index = !IsCellLocked(wanted) && IsFree(wanted) ? wanted : FirstFreeIndex();
            if (index < 0) return treeBed ? -3 : -1;
            return PlantInCell(index, item);
        }

        /// <summary>
        /// Plant into one specific cell. The panel's per-cell slot calls this, so a player can
        /// choose which square of a bed a seed goes in instead of taking whatever was nearest
        /// the crosshair (18.7.3-dev).
        /// </summary>
        public int TryPlantAtCell(int index, ItemDefinition item)
        {
            if (item == null) return -1;
            EnsureSlots();
            if (index < 0 || index >= slots.Length) return -1;

            bool isTree = IsTreeSeed(item);
            if (!isTree && CropOf(item) == null) return -1;
            if (isTree && !AllowsTrees) return -2;
            if (IsCellLocked(index)) return -3;

            return PlantInCell(index, item);
        }

        /// <summary>True when this cell cannot take a seed right now - a tree owns the bed.</summary>
        public bool IsCellLocked(int index) => treeBed && index != treeCell;

        /// <summary>True when the cell is free, unlocked and able to take this item.</summary>
        public bool CanPlantInCell(int index, ItemDefinition item)
        {
            if (item == null || index < 0 || index >= slots.Length) return false;
            if (IsCellLocked(index)) return false;
            bool isTree = IsTreeSeed(item);
            if (isTree) return AllowsTrees;
            return CropOf(item) != null && IsFree(index);
        }

        private static bool IsTreeSeed(ItemDefinition item)
            => item is SeedItem seed && seed.crop == null && TreeFarmPlot.IsSaplingOrTreeSeed(item);

        private static CropDefinition CropOf(ItemDefinition item)
            => item is SeedItem seed ? seed.crop : null;

        /// <summary>
        /// Put one seed or sapling into one cell. A sapling claims the bed: the tree stands in
        /// the chosen cell and fills the plot, so every other cell is cleared into the harvest
        /// bin and locked until the tree is felled (18.7.3-dev).
        /// </summary>
        private int PlantInCell(int index, ItemDefinition item)
        {
            if (IsCellLocked(index)) return -3;

            if (IsTreeSeed(item))
            {
                if (!AllowsTrees) return -2;
                if (treeBed) return -3;
                ClearCropsForTree();
                treeBed = true;
                treeCell = index;
                treeGrowth = 0f;
                ResetSlot(index);
                RefreshTreeVisual();
                HideCropPods();
                RefreshWaterGauge();
                return index;
            }

            var crop = CropOf(item);
            if (crop == null) return -1;
            if (!IsFree(index)) return -1;

            var slot = slots[index];
            slot.tree = false;
            slot.crop = crop;
            slot.growth = 0f;
            slot.fertilized = false;
            slot.dead = false;
            droughtTimers[index] = 0f;
            _lastStage[index] = -1;
            RefreshCellVisual(index, force: true);
            RefreshWaterGauge();
            return index;
        }

        /// <summary>
        /// Empty the rest of the bed when a tree takes it: anything growing is harvested into
        /// the box's own output bin first, because a player who plants a sapling next to a
        /// half-grown crop should not simply lose it.
        /// </summary>
        private void ClearCropsForTree()
        {
            EnsureContainers();
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || (slot.crop == null && !slot.tree)) continue;

                if (!slot.dead && slot.growth >= 0.999f)
                {
                    var yield = PeekSlotHarvest(i);
                    if (!yield.IsEmpty && harvestOutput != null && harvestOutput.HasSpace(yield.item, yield.count))
                    {
                        InsertBin(yield.item, yield.count);
                        if (replantAfterPortHarvest) InsertBin(yield.seedItem, Mathf.Max(0, yield.seedCount - 1));
                        else InsertBin(yield.seedItem, yield.seedCount);
                    }
                }

                ResetSlot(i);
                RefreshCellVisual(i, force: true);
            }
        }

        /// <summary>Harvest one ready cell into a container, with the box's own replant rule.</summary>
        public bool TryHarvestAt(Vector3 worldPoint, Inventory inventory, out FarmHarvestYield harvest)
        {
            harvest = default;
            int index = SlotAt(worldPoint);
            if (!IsReadyIndex(index)) index = FirstReadyIndex();
            if (index < 0) return false;

            if (treeBed)
            {
                // Felling by hand gives the same load as the automated route: logs,
                // saplings, biomass. Self re-seeding leaves the stump and starts the
                // next tree, which is what the switch promises.
                harvest = PeekSlotHarvest(0);
                if (harvest.IsEmpty) return false;
                if (inventory != null)
                {
                    if (harvest.item != null && harvest.count > 0) inventory.Add(harvest.item, harvest.count);
                    if (harvest.seedItem != null && harvest.seedCount > 0) inventory.Add(harvest.seedItem, harvest.seedCount);
                    ResolveTreeItems();
                    if (biomassItem != null && treeBiomassPerHarvest > 0)
                        inventory.Add(biomassItem, treeBiomassPerHarvest);
                }
                if (replantAfterPortHarvest && saplingItem != null && treeSaplingsPerHarvest > 1)
                {
                    treeGrowth = 0f;
                }
                else
                {
                    treeBed = false;
                    treeCell = -1;
                    treeGrowth = 0f;
                }
                RefreshTreeVisual();
                return true;
            }

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
            for (int i = 0; i < slots.Length; i++)
                if (!IsCellLocked(i) && IsFree(i)) return i;
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
            if (treeBed) return treeGrowth >= 1f ? 0 : -1;
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
            if (treeBed)
            {
                if (treeGrowth < 1f) return default;
                ResolveTreeItems();
                return FarmHarvestYield.Crop(
                    woodLogItem, treeLogsPerHarvest,
                    saplingItem, treeSaplingsPerHarvest);
            }

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

            if (treeBed)
            {
                // Self re-seeding keeps one sapling back, so a tree bed can cycle on its own
                // felled timber instead of being replanted by hand every time.
                if (replant && saplingItem != null && treeSaplingsPerHarvest > 1)
                    treeGrowth = 0f;
                else
                {
                    treeBed = false;
                    treeCell = -1;
                    treeGrowth = 0f;
                    RefreshTreeVisual();
                }
                return true;
            }

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

        /// <summary>
        /// Harvest everything that is ready into the box's own output bin. The panel calls
        /// this so a player can empty nine cells with one click; the route push still runs
        /// first, so a plot with a belt behaves exactly as before.
        /// </summary>
        public int HarvestAllToOutput()
        {
            EnsureContainers();
            int moved = 0;

            if (treeBed)
            {
                if (treeGrowth < 1f || harvestOutput == null) return 0;
                var treeYield = PeekSlotHarvest(0);
                if (!harvestOutput.HasSpace(treeYield.item, treeYield.count)) return 0;
                if (!TakeHarvest(false, out var taken)) return 0;
                InsertBin(taken.item, taken.count);
                InsertBin(taken.seedItem, taken.seedCount);
                ResolveTreeItems();
                InsertBin(biomassItem, treeBiomassPerHarvest);
                RefreshTreeVisual();
                return 1;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (!IsReadyIndex(i)) continue;
                var peek = PeekSlotHarvest(i);
                if (peek.IsEmpty) continue;
                if (harvestOutput != null && !harvestOutput.HasSpace(peek.item, peek.count)) break;

                var harvest = peek;
                slots[i].growth = 0f;
                if (!replantAfterPortHarvest) ResetSlot(i);
                else slots[i].fertilized = false;
                RefreshCellVisual(i, force: true);

                InsertBin(harvest.item, harvest.count);
                InsertBin(harvest.seedItem, harvest.seedCount - (replantAfterPortHarvest ? 1 : 0));
                moved++;
            }
            return moved;
        }

        private void InsertBin(ItemDefinition item, int count)
        {
            if (item == null || count <= 0 || harvestOutput == null) return;
            harvestOutput.Insert(new ItemStack { item = item, count = count });
        }

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

            bool selfReplant = replantAfterPortHarvest && !treeBed
                && slots[index] != null && !slots[index].tree
                && peek.seedItem != null && peek.seedCount > 0;
            int seedsLeaving = selfReplant ? peek.seedCount - 1 : peek.seedCount;

            // A route is optional: with a belt the harvest goes on the belt, without one it
            // lands in the box's own output bin and the player collects it from the panel.
            EnsureContainers();
            bool routeOk = (peek.item == null || FarmItemRouter.HasRoomFor(transform, dumpRadius, peek.item, peek.count))
                && (seedsLeaving <= 0 || peek.seedItem == null
                    || FarmItemRouter.HasRoomFor(transform, dumpRadius, peek.seedItem, seedsLeaving));
            bool binOk = harvestOutput != null
                && (peek.item == null || harvestOutput.HasSpace(peek.item, peek.count));
            if (!routeOk && !binOk) return;

            var harvest = peek;

            if (treeBed)
            {
                // The tree bed is emptied as a whole: the tree is felled, and a kept sapling
                // resets it for the next one.
                if (replantAfterPortHarvest && saplingItem != null && treeSaplingsPerHarvest > 1)
                {
                    treeGrowth = 0f;
                }
                else
                {
                    treeBed = false;
                    treeCell = -1;
                    treeGrowth = 0f;
                }
            }
            else
            {
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
            }

            MoveOut(harvest, seedsLeaving);

            // A tree also drops biomass, which never travels with the log stack.
            if (treeBed || slots[index].tree || harvest.item == woodLogItem)
            {
                ResolveTreeItems();
                if (biomassItem != null && treeBiomassPerHarvest > 0)
                {
                    if (!FarmItemRouter.HasRoomFor(transform, dumpRadius, biomassItem, treeBiomassPerHarvest))
                        InsertBin(biomassItem, treeBiomassPerHarvest);
                    else
                        FarmItemRouter.Push(transform, dumpRadius, biomassItem, treeBiomassPerHarvest);
                }
            }

            RefreshTreeVisual();
        }

        /// <summary>Send a harvested payload to the nearest route and anything left to the bin.</summary>
        private void MoveOut(FarmHarvestYield harvest, int seedsLeaving)
        {
            if (harvest.item != null && harvest.count > 0)
            {
                int took = FarmItemRouter.Push(transform, dumpRadius, harvest.item, harvest.count);
                if (took < harvest.count) InsertBin(harvest.item, harvest.count - took);
            }
            if (seedsLeaving > 0 && harvest.seedItem != null)
            {
                int took = FarmItemRouter.Push(transform, dumpRadius, harvest.seedItem, seedsLeaving);
                if (took < seedsLeaving) InsertBin(harvest.seedItem, seedsLeaving - took);
            }
        }

        // ── Persistence ─────────────────────────────────────────────────────
        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureSlots();

            state.SetExtra("boxSlots", slots.Length);
            state.SetExtra("water", Mathf.Max(0f, waterStored));
            state.SetExtra("treeBed", treeBed ? 1f : 0f);
            state.SetExtra("treeGrowth", Mathf.Clamp01(treeGrowth));
            state.SetExtra("treeCell", treeCell);

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

            treeBed = state.GetExtra("treeBed", treeBed ? 1f : 0f) >= 0.5f;
            treeGrowth = MachineProcessPersistence.ClampOr(state.GetExtra("treeGrowth", treeGrowth), 0f, 1f, 0f);
            treeCell = Mathf.Clamp((int)state.GetExtra("treeCell", treeBed ? 0f : -1f), -1, Mathf.Max(0, slots.Length - 1));
            if (!treeBed) treeCell = -1;

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

            RefreshTreeVisual();
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

            // 18.7.0-dev: the tray is drawn to the BLOCK's footprint, not to one metre. The
            // old body was a fixed 1 m tray in the middle of a 2 m or 3 m block, which is why
            // the three sizes looked identical and only the number of cells changed. Every
            // measurement below is now derived from `side`, so a Small bed is 1 m, a Big bed
            // is 2 m and a Large bed is 3 m of real furniture.
            float span = side;                       // metres of bed along one edge
            float half = span * 0.5f;
            float edge = half - 0.02f;               // curb centre-line
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
            Color timberBark = new Color(0.40f, 0.26f, 0.14f);
            Color timberLeaf = new Color(0.22f, 0.54f, 0.24f);

            // Base tray, kerb and curbs, all sized to the footprint.
            MakePart(root, PrimitiveType.Cube, "BaseTray",
                new Vector3(0f, 0.08f, 0f), new Vector3(span * 0.98f, 0.16f, span * 0.98f), frameSteel);
            MakePart(root, PrimitiveType.Cube, "Curb_N", new Vector3(0f, 0.22f, edge), new Vector3(span * 0.98f, 0.16f, 0.09f), curbTimber);
            MakePart(root, PrimitiveType.Cube, "Curb_S", new Vector3(0f, 0.22f, -edge), new Vector3(span * 0.98f, 0.16f, 0.09f), curbTimber);
            MakePart(root, PrimitiveType.Cube, "Curb_E", new Vector3(edge, 0.22f, 0f), new Vector3(0.09f, 0.16f, span * 0.94f), curbTimber);
            MakePart(root, PrimitiveType.Cube, "Curb_W", new Vector3(-edge, 0.22f, 0f), new Vector3(0.09f, 0.16f, span * 0.94f), curbTimber);

            // One soil cell per slot, one metre apart, and a crop pod above each of them.
            for (int r = 0; r < side; r++)
            for (int c = 0; c < side; c++)
            {
                int index = r * side + c;
                float x = c - (side - 1) * 0.5f;
                float z = r - (side - 1) * 0.5f;

                MakePart(root, PrimitiveType.Cube, "Soil_" + index,
                    new Vector3(x, 0.185f, z), new Vector3(0.86f, 0.06f, 0.86f), richSoil);

                var cell = new GameObject("Crop_" + index).transform;
                cell.SetParent(root, false);
                cell.localPosition = new Vector3(x, 0.215f, z);
                cell.localRotation = Quaternion.identity;
                BuildCropPlant(cell, index);
            }

            // Irrigation strip along the inside of the back curb.
            MakePart(root, PrimitiveType.Cube, "Irrig_Strip",
                new Vector3(0f, 0.215f, edge - 0.10f), new Vector3(span * 0.74f, 0.02f, 0.05f), waterChannel);

            // Output chute on the +X face, flush with the block edge at any size.
            MakePart(root, PrimitiveType.Cube, "OutputPort_Chute",
                new Vector3(half + 0.06f, 0.14f, 0f), new Vector3(0.22f, 0.12f, 0.34f), frameSteel);
            MakePart(root, PrimitiveType.Cube, "OutputPort_Lip",
                new Vector3(half + 0.14f, 0.19f, 0f), new Vector3(0.06f, 0.02f, 0.30f), waterChannel);
            MakePart(root, PrimitiveType.Cube, "OutputPort_Indicator",
                new Vector3(half + 0.06f, 0.21f, 0.18f), new Vector3(0.06f, 0.03f, 0.06f), new Color(0.22f, 0.44f, 0.34f));

            // Water sight glass on the -X face.
            MakePart(root, PrimitiveType.Cube, "WaterGauge_Back",
                new Vector3(-half - 0.05f, 0.14f, 0f), new Vector3(0.05f, 0.20f, 0.10f), frameSteel);
            MakePart(root, PrimitiveType.Cube, "WaterGauge",
                new Vector3(-half - 0.08f, 0.14f, 0f), new Vector3(0.02f, 0.16f, 0.07f), waterChannel);

            if (large)
            {
                // Four posts at the tray corners and a frame that actually meets them. The
                // old body had a lone rail at post height with nothing under it, which read
                // as a bar floating over the bed.
                float postX = edge - 0.04f;
                float postTop = 1.70f;
                MakePart(root, PrimitiveType.Cube, "Post_NW", new Vector3(-postX, postTop * 0.5f, postX), new Vector3(0.10f, postTop, 0.10f), frameSteel);
                MakePart(root, PrimitiveType.Cube, "Post_NE", new Vector3(postX, postTop * 0.5f, postX), new Vector3(0.10f, postTop, 0.10f), frameSteel);
                MakePart(root, PrimitiveType.Cube, "Post_SW", new Vector3(-postX, postTop * 0.5f, -postX), new Vector3(0.10f, postTop, 0.10f), frameSteel);
                MakePart(root, PrimitiveType.Cube, "Post_SE", new Vector3(postX, postTop * 0.5f, -postX), new Vector3(0.10f, postTop, 0.10f), frameSteel);

                // A proper trellis: two rails per direction, resting on the post tops, so the
                // frame reads as one structure from any angle.
                for (int i = 0; i < 3; i++)
                {
                    float t = (i - 1) * 0.85f;
                    MakePart(root, PrimitiveType.Cube, "Trellis_X" + i,
                        new Vector3(0f, postTop - 0.04f, t), new Vector3(postX * 2f, 0.05f, 0.06f), frameSteel);
                    MakePart(root, PrimitiveType.Cube, "Trellis_Z" + i,
                        new Vector3(t, postTop - 0.04f, 0f), new Vector3(0.06f, 0.05f, postX * 2f), frameSteel);
                }

                // The timber that a tree on this bed will become. Hidden until a sapling is
                // planted: `RefreshTreeVisual` scales it from 0 to full.
                var tree = new GameObject("TreeRoot").transform;
                tree.SetParent(root, false);
                tree.localPosition = new Vector3(0f, 0.20f, 0f);
                MakePart(tree, PrimitiveType.Cylinder, "Trunk",
                    new Vector3(0f, 1.00f, 0f), new Vector3(0.42f, 1.00f, 0.42f), timberBark);
                MakePart(tree, PrimitiveType.Cylinder, "TrunkTop",
                    new Vector3(0f, 2.10f, 0f), new Vector3(0.26f, 0.55f, 0.26f), timberBark);
                MakePart(tree, PrimitiveType.Sphere, "Canopy_Main",
                    new Vector3(0f, 2.75f, 0f), new Vector3(2.10f, 1.30f, 2.10f), timberLeaf);
                MakePart(tree, PrimitiveType.Sphere, "Canopy_Crown",
                    new Vector3(0f, 3.35f, 0f), new Vector3(1.40f, 0.95f, 1.40f), new Color(0.28f, 0.62f, 0.26f));
                MakePart(tree, PrimitiveType.Sphere, "Canopy_West",
                    new Vector3(-0.70f, 2.35f, 0.20f), new Vector3(1.20f, 0.90f, 1.20f), timberLeaf);
                MakePart(tree, PrimitiveType.Sphere, "Canopy_East",
                    new Vector3(0.72f, 2.42f, -0.18f), new Vector3(1.20f, 0.90f, 1.20f), timberLeaf);
                tree.localScale = Vector3.zero;
            }

            MakePart(root, PrimitiveType.Cube, "StatusBeacon",
                new Vector3(half - 0.18f, 0.28f, half - 0.18f), new Vector3(0.07f, 0.06f, 0.07f), new Color(0.52f, 0.86f, 0.22f));
        }

        /// <summary>
        /// The plant that stands in one cell. 18.7.3-dev: the old pod was a stalk and
        /// three flat cards, all of which were then tinted the same stage colour, so every
        /// crop read as a dark spike. This is a small bush instead: a woody stem in two
        /// tapering sections, six rounded leaves fanned around it at rising heights, and a
        /// fruit cluster that appears once the crop is nearly ripe. The parts keep their
        /// own materials - leaf, stem and fruit are tinted separately - so a ripe crop is
        /// a green plant with fruit on it rather than one silhouette colour.
        /// </summary>
        private void BuildCropPlant(Transform cell, int index)
        {
            Color leafBase = new Color(0.36f, 0.72f, 0.30f);
            Color stemBase = new Color(0.38f, 0.36f, 0.20f);
            Color fruitBase = new Color(0.92f, 0.52f, 0.16f);
            Color moundBase = new Color(0.28f, 0.20f, 0.12f);

            // A little mound so the plant grows out of the soil instead of standing on it.
            MakePart(cell, PrimitiveType.Sphere, "Mound",
                new Vector3(0f, -0.02f, 0f), new Vector3(0.34f, 0.10f, 0.34f), moundBase);

            MakePart(cell, PrimitiveType.Cylinder, "Stem_Low",
                new Vector3(0f, 0.16f, 0f), new Vector3(0.055f, 0.17f, 0.055f), stemBase);
            MakePart(cell, PrimitiveType.Cylinder, "Stem_Top",
                new Vector3(0f, 0.44f, 0f), new Vector3(0.040f, 0.14f, 0.040f), stemBase);

            // Six leaves: two per level, each pair turned a third of a turn, so the bush
            // reads as foliage from any angle instead of a fan that vanishes side-on.
            float[] leafHeights = { 0.20f, 0.32f, 0.46f, 0.58f };
            float[] leafLengths = { 0.30f, 0.34f, 0.28f, 0.22f };
            for (int i = 0; i < leafHeights.Length; i++)
            {
                float yaw = index * 47f + i * 74f;
                float tilt = 16f + i * 6f;
                Quaternion spin = Quaternion.Euler(0f, yaw, 0f);

                Vector3 offset = spin * new Vector3(leafLengths[i] * 0.42f, 0f, 0f);
                MakePart(cell, PrimitiveType.Sphere, "Leaf_" + i,
                    new Vector3(offset.x, leafHeights[i], offset.z),
                    new Vector3(leafLengths[i], 0.055f, leafLengths[i] * 0.62f),
                    leafBase, new Vector3(0f, yaw, -tilt));

                Vector3 offset2 = spin * new Vector3(-leafLengths[i] * 0.42f, 0f, 0f);
                MakePart(cell, PrimitiveType.Sphere, "LeafB_" + i,
                    new Vector3(offset2.x, leafHeights[i] + 0.06f, offset2.z),
                    new Vector3(leafLengths[i] * 0.86f, 0.05f, leafLengths[i] * 0.55f),
                    leafBase, new Vector3(0f, yaw, tilt));
            }

            // Fruit only shows once the crop is nearly ripe; RefreshCellVisual toggles it.
            for (int i = 0; i < 3; i++)
            {
                float angle = (index * 61f + i * 120f) * Mathf.Deg2Rad;
                var at = new Vector3(Mathf.Cos(angle) * 0.13f, 0.50f + i * 0.075f, Mathf.Sin(angle) * 0.13f);
                MakePart(cell, PrimitiveType.Sphere, "Fruit_" + i, at, Vector3.one * 0.15f, fruitBase);
            }
        }

        /// <summary>Hide every crop pod - used while a tree owns the bed.</summary>
        private void HideCropPods()
        {
            for (int i = 0; i < _cellRoots.Length; i++)
                if (_cellRoots[i] != null) _cellRoots[i].gameObject.SetActive(false);
        }

        /// <summary>
        /// The tree that fills a Large bed. Hidden entirely on the smaller boxes and while the
        /// bed holds crops; scaled from a seedling to a full trunk as the sapling grows.
        /// </summary>
        private void RefreshTreeVisual()
        {
            var vis = transform.Find("Generated_PlantBoxVisuals");
            if (vis == null) return;
            var tree = vis.Find("TreeRoot");
            if (tree == null) return;

            if (!treeBed)
            {
                tree.localScale = Vector3.zero;
                return;
            }

            float g = Mathf.Clamp01(treeGrowth);
            float xz = Mathf.Lerp(0.10f, 1f, g);
            float y = Mathf.Lerp(0.08f, 1f, g);
            tree.localScale = new Vector3(xz, y, xz);

            // A tree owns the bed, so the crop pods step aside while it is there.
            for (int i = 0; i < _cellRoots.Length; i++)
                if (_cellRoots[i] != null) _cellRoots[i].gameObject.SetActive(false);
        }

        private void CacheVisuals()
        {
            EnsureSlots();
            var vis = transform.Find("Generated_PlantBoxVisuals");

            _cellRoots = new Transform[slots.Length];
            _cellRenderers = new MeshRenderer[slots.Length][];
            _cellLeafRenderers = new MeshRenderer[slots.Length][];
            _cellStemRenderers = new MeshRenderer[slots.Length][];
            _cellFruitRenderers = new MeshRenderer[slots.Length][];
            for (int i = 0; i < slots.Length; i++)
            {
                var cell = vis != null ? vis.Find("Crop_" + i) : null;
                _cellRoots[i] = cell;
                _cellRenderers[i] = cell != null
                    ? cell.GetComponentsInChildren<MeshRenderer>(true)
                    : Array.Empty<MeshRenderer>();

                // 18.7.3-dev: leaves, stem and fruit are tinted separately, so a ripe crop
                // is a green plant with fruit on it rather than one silhouette colour.
                var leaves = new List<MeshRenderer>();
                var stems = new List<MeshRenderer>();
                var fruits = new List<MeshRenderer>();
                if (cell != null)
                {
                    var renderers = _cellRenderers[i];
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        var mr = renderers[r];
                        if (mr == null) continue;
                        string partName = mr.gameObject.name;
                        if (partName.StartsWith("Leaf", System.StringComparison.Ordinal)) leaves.Add(mr);
                        else if (partName.StartsWith("Fruit", System.StringComparison.Ordinal)) fruits.Add(mr);
                        else stems.Add(mr);
                    }
                }
                _cellLeafRenderers[i] = leaves.ToArray();
                _cellStemRenderers[i] = stems.ToArray();
                _cellFruitRenderers[i] = fruits.ToArray();
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
            // A tree stands in one cell but is drawn as the bed-wide tree, so the cell itself
            // shows nothing (18.7.3-dev).
            if (treeBed && index == treeCell)
            {
                root.gameObject.SetActive(false);
                if (index < _lastStage.Length) _lastStage[index] = -1;
                return;
            }

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
                ShowFruit(index, false);
                Tint(index, DeadTint);
                return;
            }

            // Growth is continuous now, so the pod is redrawn every tick: nine transforms a
            // frame is cheaper than the stage bookkeeping that used to gate them, and it is
            // what makes a three-minute crop climb smoothly instead of jumping four times.
            int stages = slot.tree ? 5 : Mathf.Max(1, slot.crop.growthStages);
            int stage = Mathf.Clamp(Mathf.FloorToInt(slot.growth * stages), 0, stages - 1);
            _lastStage[index] = stage;

            float scale = 1f;
            if (slot.tree)
            {
                // A tree scales taller than a crop: a mature sapling reads as a young trunk.
                scale = Mathf.Lerp(0.35f, 2.6f, slot.growth);
            }
            else if (slot.crop.stageScales != null && slot.crop.stageScales.Length > 0)
            {
                // 18.6.0-dev: interpolate inside the stage instead of snapping to it. The
                // crop then climbs smoothly through a three-minute cycle rather than jumping
                // four times, which is what makes "slowly" readable on the block itself.
                int last = slot.crop.stageScales.Length - 1;
                float pos = Mathf.Clamp(slot.growth * slot.crop.stageScales.Length, 0f, last);
                int from = Mathf.FloorToInt(pos);
                int to = Mathf.Min(from + 1, last);
                scale = Mathf.Lerp(slot.crop.stageScales[from], slot.crop.stageScales[to], pos - from);
            }

            root.localScale = new Vector3(Mathf.Max(0.05f, scale), Mathf.Max(0.05f, scale), Mathf.Max(0.05f, scale));

            if (slot.tree)
            {
                ShowFruit(index, false);
                Tint(index, new Color(0.22f, 0.42f, 0.18f));
                return;
            }

            // Fruit appears on the last quarter of the cycle, so "nearly ready" is visible
            // from across the farm instead of only readable as a percentage.
            ShowFruit(index, slot.growth >= 0.72f);

            if (slot.crop.stageColors != null && slot.crop.stageColors.Length > 0)
                Tint(index, slot.crop.stageColors[Mathf.Clamp(stage, 0, slot.crop.stageColors.Length - 1)]);
            else
                Tint(index, new Color(0.36f, 0.72f, 0.30f));
        }

        /// <summary>
        /// Tint one plant. Leaves take the crop's stage colour lifted towards daylight so a
        /// maturing crop never reads as a dark spike, the stem keeps a woody tone, and the
        /// fruit takes a warm ripening colour (18.7.3-dev).
        /// </summary>
        private void Tint(int index, Color color)
        {
            if (index < 0 || index >= _cellRenderers.Length) return;

            Color leaf = Color.Lerp(color, new Color(0.62f, 0.92f, 0.42f), 0.34f);
            leaf = new Color(leaf.r * 1.12f, leaf.g * 1.12f, leaf.b * 1.05f, 1f);
            Color stem = Color.Lerp(color, new Color(0.42f, 0.40f, 0.22f), 0.62f);
            Color fruit = Color.Lerp(color, new Color(0.95f, 0.52f, 0.16f), 0.55f);

            ApplyColor(_cellLeafRenderers != null && index < _cellLeafRenderers.Length
                ? _cellLeafRenderers[index] : null, leaf);
            ApplyColor(_cellStemRenderers != null && index < _cellStemRenderers.Length
                ? _cellStemRenderers[index] : null, stem);
            ApplyColor(_cellFruitRenderers != null && index < _cellFruitRenderers.Length
                ? _cellFruitRenderers[index] : null, fruit);

            // Anything that was not classified (an older prefab body, a tree cell) still
            // gets the flat tint so nothing is left at its authoring colour.
            var set = _cellRenderers[index];
            for (int i = 0; i < set.Length; i++)
            {
                var r = set[i];
                if (r == null) continue;
                if (IsClassified(index, r)) continue;
                Write(r, color);
            }
        }

        private bool IsClassified(int index, MeshRenderer renderer)
        {
            return Contains(_cellLeafRenderers, index, renderer)
                || Contains(_cellStemRenderers, index, renderer)
                || Contains(_cellFruitRenderers, index, renderer);
        }

        private static bool Contains(MeshRenderer[][] sets, int index, MeshRenderer renderer)
        {
            if (sets == null || index < 0 || index >= sets.Length || sets[index] == null) return false;
            var set = sets[index];
            for (int i = 0; i < set.Length; i++) if (set[i] == renderer) return true;
            return false;
        }

        private static void ApplyColor(MeshRenderer[] set, Color color)
        {
            if (set == null) return;
            for (int i = 0; i < set.Length; i++) Write(set[i], color);
        }

        private static void Write(MeshRenderer renderer, Color color)
        {
            if (renderer == null) return;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>Show or hide the fruit cluster on one plant.</summary>
        private void ShowFruit(int index, bool show)
        {
            if (_cellFruitRenderers == null || index < 0 || index >= _cellFruitRenderers.Length) return;
            var set = _cellFruitRenderers[index];
            if (set == null) return;
            for (int i = 0; i < set.Length; i++)
                if (set[i] != null && set[i].gameObject.activeSelf != show)
                    set[i].gameObject.SetActive(show);
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
