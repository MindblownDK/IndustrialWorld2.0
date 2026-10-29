// Assets/Scripts/VoxelEngine/GridSystem/GridFarmPlotBase.cs
//
// 18.4.0-dev / 18.8.0-dev — grid-mounted farm plots (Vertical Farm Plot, Large
// Farm Plot Box, and Grid Seed Planter).
//
// Shares the ground PlantBox rules while running on a ship or station grid:
//   * Oxygen comes from FarmEnvironment.HasBreathableAir (sealed breathable hull room
//     or breathable world). Crops pause without air rather than dying.
//   * Water comes from GridLiquidNetwork into the plot's internal tank (starting at
//     0 L on a newly placed plot) or from a hand-poured Liquid Canister.
//   * Seeds dropped in the seed bin (or into a specific cell's 1-slot cellInput)
//     are planted automatically.
//   * Ripe harvests move to connected GridCargoContainers first and the plot's own
//     harvest bin second, keeping one seed back when self-reseeding is enabled.
//   * On tree-capable beds (allowsTrees = true on Large Farm Plot Box), planting a
//     sapling claims the whole plot (treeBed = true, treeCell = index), harvests any
//     ripe crops into the bin first, locks the other eight cells, and grows a 3D
//     trunk-and-canopy tree visual.

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Farming;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    /// <summary>One growing cell on a grid farm plot.</summary>
    [Serializable]
    public class GridFarmCell
    {
        public CropDefinition crop;
        [Range(0f, 1f)] public float growth;
        public float dryTimer;
        public bool fertilized;
        public bool dead;
        public bool tree;
    }

    public abstract class GridFarmPlotBase : GridBlock, IGridDataProvider, IFarmBed, IMachineProcessState
    {
        [Header("Cells")]
        [Tooltip("Live state of every growing cell, row by row.")]
        public GridFarmCell[] cells = new GridFarmCell[0];

        [Tooltip("Whether a tree sapling may be planted here. Only the floor box allows it.")]
        public bool allowsTrees = false;

        [Header("Tree Bed (when allowsTrees is true)")]
        [Tooltip("True while a sapling is standing in the bed. Claims the whole plot until the timber is felled.")]
        public bool treeBed = false;
        [Range(0f, 1f)]
        public float treeGrowth = 0f;
        [Tooltip("Index of the cell the sapling was planted into (-1 when no tree is standing). Every other cell is locked while treeBed is true.")]
        public int treeCell = -1;

        [Header("Containers")]
        [Tooltip("Seeds and saplings waiting to go into a free cell. The plot plants from here on its own.")]
        public ItemContainer seedInput;
        [Tooltip("Produce taken off the beds, waiting for a route. The player can take it straight out of the panel.")]
        public ItemContainer harvestOutput;

        public const int SEED_SLOTS = 4;
        public const int OUTPUT_SLOTS = 9;
        public int seedSlots = SEED_SLOTS;
        public int harvestSlots = OUTPUT_SLOTS;

        /// <summary>
        /// One-slot input bin per cell so the player can drop a seed straight onto a
        /// specific cell in the panel instead of filling the bed in scan order.
        /// Transient - seeds dropped here are planted within a second.
        /// </summary>
        [NonSerialized]
        public ItemContainer[] cellInput = new ItemContainer[0];

        [Header("Power")]
        [Tooltip("Watts drawn while any cell is growing.")]
        public float powerDraw = 55f;
        [Tooltip("Watts drawn while idle but switched on.")]
        public float idlePowerDraw = 6f;

        [Header("Water")]
        public float waterCapacity = 200f;
        public float waterStored = 0f;
        [Tooltip("Litres per growing cell per second.")]
        public float litresPerCellSecond = 0.02f;
        public float waterPerCellLps { get => litresPerCellSecond; set => litresPerCellSecond = value; }
        [Tooltip("Litres pulled per second from the grid liquid network while the tank has room.")]
        public float refillLitresPerSecond = 2.5f;
        [Tooltip("Seconds a planted crop survives with an empty tank before it withers (0 = pause without withering).")]
        public float droughtGraceSeconds = 90f;

        [Header("Tuning")]
        public float fertilizedSpeedMultiplier = 1.5f;
        [Range(0.05f, 2f)] public float growthSpeedMultiplier = 0.5f;
        public float environmentCheckInterval = 2f;
        public bool replantAfterHarvest = true;
        public bool automatic = true;

        [Header("Trees")]
        public ItemDefinition woodLogItem;
        public ItemDefinition saplingItem;
        public ItemDefinition biomassItem;
        public float treeGrowthSeconds = 110f;
        public float treeIrrigatedSpeedMultiplier = 2f;
        public int treeLogsPerHarvest = 6;
        public int treeSaplingsPerHarvest = 2;
        public int treeBiomassPerHarvest = 2;

        /// <summary>Cells this plot owns. Fixed by the subclass, never by the save.</summary>
        public abstract int CellCount { get; }

        /// <summary>One short word for the cell layout: "VERTICAL", "FLOOR", "PLANTER".</summary>
        public abstract string PlotKind { get; }

        /// <summary>Overall scale of each cell's leafy bush visual relative to the ground PlantBox.</summary>
        protected virtual float PodVisualScale => 0.82f;

        public int cellCount => cells != null ? cells.Length : 0;

        public GridFarmCell CellAt(int index)
            => cells != null && index >= 0 && index < cells.Length ? cells[index] : null;

        public float CellsFill01
        {
            get
            {
                int total = cellCount;
                return total > 0 ? Mathf.Clamp01(PlantedCount / (float)total) : 0f;
            }
        }

        public bool TreeBed => treeBed;
        public float TreeGrowth01 => Mathf.Clamp01(treeGrowth);
        public bool HasWater => waterStored > 0.01f;
        public bool IsIrrigated => waterStored > 0.05f;
        public float Water01 => waterCapacity > 0f ? Mathf.Clamp01(waterStored / waterCapacity) : 0f;
        public float WaterFill01 => Water01;

        public bool HasAir { get; private set; }
        public bool HasWaterSupply => Grid != null && GridLiquidNetwork.Instance != null
                                      && GridLiquidNetwork.Instance.GetTanks(Grid, LiquidType.Water).Count > 0;

        // ── IFarmBed implementation ───────────────────────────────────────────
        string IFarmBed.BedName => string.IsNullOrEmpty(blockName) ? PlotKind : blockName;
        bool IFarmBed.IsReady => ReadyCount > 0;
        bool IFarmBed.NeedsWater => PlantedCount > 0 && !HasWater;
        bool IFarmBed.NeedsOxygen => PlantedCount > 0 && !HasAir;
        bool IFarmBed.IsEmpty => PlantedCount == 0 && DeadCount == 0;
        bool IFarmBed.IsDead => DeadCount > 0 && PlantedCount == 0;

        public int PlantedCount
        {
            get
            {
                if (treeBed) return 1;
                if (cells == null) return 0;
                int n = 0;
                for (int i = 0; i < cells.Length; i++)
                    if (cells[i] != null && (cells[i].crop != null || cells[i].tree) && !cells[i].dead) n++;
                return n;
            }
        }

        public int ReadyCount
        {
            get
            {
                if (treeBed) return treeGrowth >= 0.999f ? 1 : 0;
                if (cells == null) return 0;
                int n = 0;
                for (int i = 0; i < cells.Length; i++)
                    if (cells[i] != null && !cells[i].dead && (cells[i].crop != null || cells[i].tree)
                        && cells[i].growth >= 0.999f) n++;
                return n;
            }
        }

        public int DeadCount
        {
            get
            {
                if (treeBed) return 0;
                if (cells == null) return 0;
                int n = 0;
                for (int i = 0; i < cells.Length; i++)
                    if (cells[i] != null && cells[i].dead) n++;
                return n;
            }
        }

        public float Growth01
        {
            get
            {
                if (treeBed) return TreeGrowth01;
                if (cells == null) return 0f;
                float sum = 0f;
                int n = 0;
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i] == null || (cells[i].crop == null && !cells[i].tree) || cells[i].dead) continue;
                    sum += Mathf.Clamp01(cells[i].growth);
                    n++;
                }
                return n > 0 ? sum / n : 0f;
            }
        }

        public string CropName
        {
            get
            {
                if (treeBed) return "Timber";
                if (cells == null) return "Empty";
                string first = null;
                int count = 0;
                for (int i = 0; i < cells.Length; i++)
                {
                    var c = cells[i];
                    if (c == null || c.dead) continue;
                    string label = c.tree ? "Timber" : (c.crop != null ? c.crop.cropName : null);
                    if (string.IsNullOrEmpty(label)) continue;
                    count++;
                    if (first == null) first = label;
                    else if (first != label) return $"Mixed ({count})";
                }
                return first ?? "Empty";
            }
        }

        public bool IsProducing { get; private set; }

        public string Status
        {
            get
            {
                if (!Enabled) return "DISABLED";
                if (Grid == null) return "NO GRID";
                if (!Grid.HasPower) return "NO POWER";
                if (treeBed)
                {
                    if (treeGrowth >= 0.999f) return "TIMBER READY";
                    if (!IsIrrigated) return "DRY";
                    if (!HasAir) return "NO O2";
                    return "TIMBER";
                }
                if (DeadCount > 0 && PlantedCount == 0) return "LOST CROP";
                if (ReadyCount > 0) return "READY x" + ReadyCount;
                if (PlantedCount == 0) return "EMPTY";
                if (!IsIrrigated) return "DRY";
                if (NeedsAirNow()) return "NO O2";
                return "GROWING";
            }
        }

        public override float PowerDraw => !Enabled ? 0f : (PlantedCount > 0 ? powerDraw : idlePowerDraw);
        public override float ContentMass => (waterStored * 1f) + (PlantedCount * 4f);

        private float _envTimer;
        private float _pullTimer;
        private float _plantTimer;
        private float _podTimer;

        private const string VisualRootName = "Generated_FarmPods";
        private static readonly Color LeafLitTint   = new Color(0.28f, 0.64f, 0.24f);
        private static readonly Color DeadTint      = new Color(0.42f, 0.30f, 0.16f);
        private static readonly Color TrunkBark     = new Color(0.38f, 0.25f, 0.14f);
        private static readonly Color CanopyFoliage = new Color(0.22f, 0.54f, 0.22f);

        private Transform _podRoot;
        private readonly List<Transform> _pods = new List<Transform>();
        private readonly List<Renderer[]> _cellLeafRenderers = new List<Renderer[]>();
        private readonly List<Renderer[]> _cellStemRenderers = new List<Renderer[]>();
        private readonly List<Renderer[]> _cellFruitRenderers = new List<Renderer[]>();
        private Transform _treeRoot;
        private Transform _treeCanopy;
        private MaterialPropertyBlock _mpb;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        protected virtual void Awake()
        {
            EnsureState();
            HasAir = FarmEnvironment.HasBreathableAir(transform.position);
            EnsurePods();
            RefreshPods();
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            EnsureState();
            HasAir = FarmEnvironment.HasBreathableAir(transform.position);
            EnsurePods();
            RefreshPods();
        }

        public void EnsureState()
        {
            EnsureCells();
            EnsureContainers();
            EnsureCellInputs();
            ResolveTreeItems();
        }

        private void EnsureCells()
        {
            int wanted = Mathf.Max(1, CellCount);
            if (cells == null || cells.Length != wanted)
            {
                var next = new GridFarmCell[wanted];
                for (int i = 0; i < wanted; i++)
                    next[i] = cells != null && i < cells.Length && cells[i] != null ? cells[i] : new GridFarmCell();
                cells = next;
            }
            else
            {
                for (int i = 0; i < cells.Length; i++)
                    if (cells[i] == null) cells[i] = new GridFarmCell();
            }
        }

        public void EnsureContainers()
        {
            EnsureCells();
            int wantSeeds = Mathf.Max(1, seedSlots);
            int wantOut   = Mathf.Max(1, harvestSlots);

            if (seedInput == null) seedInput = new ItemContainer("Seeds", wantSeeds);
            else if (seedInput.Size != wantSeeds) seedInput.Resize(wantSeeds);
            seedInput.AcceptFilter = (item, wanted) => CanPlant(item) ? wanted : 0;

            if (harvestOutput == null) harvestOutput = new ItemContainer("Harvest", wantOut);
            else if (harvestOutput.Size != wantOut) harvestOutput.Resize(wantOut);

            EnsureCellInputs();
        }

        private void EnsureCellInputs()
        {
            int count = cells != null ? cells.Length : Mathf.Max(1, CellCount);
            if (cellInput == null || cellInput.Length != count)
            {
                var next = new ItemContainer[count];
                if (cellInput != null)
                {
                    int copy = Mathf.Min(cellInput.Length, count);
                    for (int i = 0; i < copy; i++) next[i] = cellInput[i];
                }
                cellInput = next;
            }
            for (int i = 0; i < count; i++)
            {
                int cell = i;
                if (cellInput[i] == null)
                    cellInput[i] = new ItemContainer("Cell " + (i + 1), 1);
                cellInput[i].AcceptFilter = (item, wanted) => CanPlantInCell(cell, item) ? wanted : 0;
            }
        }

        public ItemContainer CellInput(int index)
        {
            EnsureCellInputs();
            return index >= 0 && index < cellInput.Length ? cellInput[index] : null;
        }

        public bool IsCellLocked(int index)
            => treeBed && index != treeCell;

        public bool CanPlantInCell(int index, ItemDefinition item)
        {
            if (item == null || cells == null || index < 0 || index >= cells.Length) return false;
            if (IsCellLocked(index)) return false;
            if (IsTreeSeed(item))
                return allowsTrees && !treeBed;
            return CropOf(item) != null && IsFreeCell(index);
        }

        public static bool CanPlantItem(ItemDefinition item)
            => CropOf(item) != null || IsTreeSeed(item);

        public static CropDefinition CropOf(ItemDefinition item)
            => item is SeedItem seed ? seed.crop : null;

        public static bool IsTreeSeed(ItemDefinition item)
            => TreeFarmPlot.IsSaplingOrTreeSeed(item);

        public bool CanPlant(ItemDefinition item)
        {
            if (item == null) return false;
            if (item is SeedItem seed)
            {
                if (seed.crop != null) return true;
                return allowsTrees && TreeFarmPlot.IsSaplingOrTreeSeed(item);
            }
            return allowsTrees && TreeFarmPlot.IsSaplingOrTreeSeed(item);
        }

        private void ResolveTreeItems()
        {
            if (woodLogItem != null && saplingItem != null && biomassItem != null) return;

            ItemDefinition Find(string id, string fallbackAsset)
            {
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

        // ── Simulation ────────────────────────────────────────────────────────
        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            _envTimer += dt;
            if (_envTimer >= Mathf.Max(0.5f, environmentCheckInterval))
            {
                _envTimer = 0f;
                HasAir = FarmEnvironment.HasBreathableAir(transform.position);
            }

            if (!Enabled || Grid == null) { IsProducing = false; return; }

            PullWater(dt);

            bool powered = Grid.HasPower;

            _plantTimer += dt;
            if (_plantTimer >= 1f)
            {
                _plantTimer = 0f;
                PlantFromCellInputs();
                while (PlantFromSeedBin()) { }
            }
            IsProducing = powered && PlantedCount > 0;

            GrowCells(powered, dt);

            _podTimer += dt;
            if (_podTimer >= 0.2f)
            {
                _podTimer = 0f;
                EnsurePods();
                RefreshPods();
            }

            _pullTimer += dt;
            if (automatic && _pullTimer >= 1f)
            {
                _pullTimer = 0f;
                PushRipeToGrid();
            }
        }

        private void PullWater(float dt)
        {
            if (waterStored >= waterCapacity - 0.001f) return;
            if (GridLiquidNetwork.Instance == null) return;

            float want = Mathf.Min(refillLitresPerSecond * dt, waterCapacity - waterStored);
            if (want <= 0f) return;

            float got = GridLiquidNetwork.Instance.DrawLiquidFor(this, LiquidType.Water, want);
            if (got > 0f) waterStored = Mathf.Min(waterCapacity, waterStored + got);
        }

        public float TryAddWater(float litres)
        {
            if (litres <= 0f || waterCapacity <= 0f) return 0f;
            float before = waterStored;
            waterStored = Mathf.Clamp(waterStored + litres, 0f, waterCapacity);
            return waterStored - before;
        }

        private void PlantFromCellInputs()
        {
            if (cellInput == null || cells == null) return;
            int count = Mathf.Min(cellInput.Length, cells.Length);
            for (int i = 0; i < count; i++)
            {
                var bin = cellInput[i];
                if (bin == null || bin.Size <= 0) continue;
                var stack = bin.GetSlot(0);
                if (stack == null || stack.IsEmpty || stack.item == null) continue;
                if (TryPlantAtCell(i, stack.item))
                    bin.Remove(stack.item, 1);
            }
        }

        private bool PlantFromSeedBin()
        {
            if (seedInput == null || treeBed) return false;

            for (int i = 0; i < seedInput.Size; i++)
            {
                var slot = seedInput.GetSlot(i);
                if (slot == null || slot.IsEmpty || !CanPlant(slot.item)) continue;
                if (TryPlant(slot.item) < 0) continue;
                seedInput.Remove(slot.item, 1);
                return true;
            }
            return false;
        }

        private void GrowCells(bool powered, float dt)
        {
            if (!powered) return;

            float scale = Mathf.Max(0f, growthSpeedMultiplier);
            int drinking = 0;
            bool irrigated = IsIrrigated;

            if (treeBed)
            {
                if (treeGrowth < 1f && HasAir)
                {
                    float treeSpeed = (1f / Mathf.Max(1f, treeGrowthSeconds)) * scale;
                    if (irrigated)
                    {
                        treeSpeed *= Mathf.Max(1f, treeIrrigatedSpeedMultiplier);
                        drinking += 2;
                    }
                    else
                    {
                        treeSpeed *= 0.25f;
                    }
                    treeGrowth = Mathf.Clamp01(treeGrowth + treeSpeed * dt);
                }
                if (drinking > 0)
                    waterStored = Mathf.Max(0f, waterStored - litresPerCellSecond * drinking * dt);
                return;
            }

            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (cell == null || cell.dead) continue;
                bool planted = cell.crop != null || cell.tree;
                if (!planted) continue;

                if (cell.tree)
                {
                    // Migrate legacy single-cell tree to the whole-bed tree model.
                    treeBed = true;
                    treeCell = i;
                    treeGrowth = Mathf.Clamp01(cell.growth);
                    cell.tree = false;
                    break;
                }

                if (cell.growth >= 1f) continue;

                var crop = cell.crop;
                if (crop == null) continue;

                if (crop.requiresOxygen && !HasAir) continue;

                if (irrigated)
                {
                    cell.dryTimer = 0f;
                    float speed = (1f / Mathf.Max(0.5f, crop.growthTime)) * scale;
                    speed *= Mathf.Max(1f, crop.irrigatedSpeedMultiplier);
                    if (cell.fertilized) speed *= Mathf.Max(1f, fertilizedSpeedMultiplier);
                    cell.growth = Mathf.Clamp01(cell.growth + speed * dt);
                    drinking++;
                }
                else if (crop.requiresWater)
                {
                    cell.dryTimer += dt;
                    if (droughtGraceSeconds > 0f && cell.dryTimer >= droughtGraceSeconds)
                    {
                        cell.dead = true;
                    }
                    else
                    {
                        float speed = (1f / Mathf.Max(0.5f, crop.growthTime)) * scale * 0.25f;
                        if (cell.fertilized) speed *= Mathf.Max(1f, fertilizedSpeedMultiplier);
                        cell.growth = Mathf.Clamp01(cell.growth + speed * dt);
                    }
                }
                else
                {
                    float speed = (1f / Mathf.Max(0.5f, crop.growthTime)) * scale;
                    if (cell.fertilized) speed *= Mathf.Max(1f, fertilizedSpeedMultiplier);
                    cell.growth = Mathf.Clamp01(cell.growth + speed * dt);
                }
            }

            if (drinking > 0)
                waterStored = Mathf.Max(0f, waterStored - litresPerCellSecond * drinking * dt);
        }

        private bool NeedsAirNow()
        {
            if (HasAir) return false;
            if (treeBed && treeGrowth < 1f) return true;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] != null && !cells[i].dead && cells[i].crop != null && cells[i].crop.requiresOxygen
                    && cells[i].growth < 1f) return true;
            return false;
        }

        // ── Harvest ───────────────────────────────────────────────────────────
        public int HarvestAllToOutput() => PushRipeToGrid();

        public int PushRipeToGrid()
        {
            EnsureContainers();

            var containers = Grid != null && GridItemNetwork.Instance != null
                ? GridItemNetwork.Instance.GetConnectedContainers(Grid)
                : null;
            if (containers == null) containers = new List<GridCargoContainer>();

            int moved = 0;

            if (treeBed)
            {
                if (treeGrowth < 0.999f) return 0;
                ResolveTreeItems();
                if (!CanDeliver(containers, woodLogItem, treeLogsPerHarvest)) return 0;

                int saps = Mathf.Max(0, treeSaplingsPerHarvest);
                bool keepSapling = replantAfterHarvest && saplingItem != null && saps > 0;
                if (keepSapling) saps -= 1;

                Deliver(containers, woodLogItem, treeLogsPerHarvest, ref moved);
                if (saps > 0) Deliver(containers, saplingItem, saps, ref moved);
                Deliver(containers, biomassItem, treeBiomassPerHarvest, ref moved);

                if (keepSapling)
                {
                    treeGrowth = 0f;
                }
                else
                {
                    treeBed = false;
                    treeCell = -1;
                    treeGrowth = 0f;
                }
                RefreshPods();
                return moved;
            }

            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (cell == null || cell.dead || cell.growth < 0.999f) continue;
                if (cell.crop == null && !cell.tree) continue;

                if (cell.tree)
                {
                    ResolveTreeItems();
                    if (!CanDeliver(containers, woodLogItem, treeLogsPerHarvest)) continue;
                    Deliver(containers, woodLogItem, treeLogsPerHarvest, ref moved);
                    Deliver(containers, saplingItem, treeSaplingsPerHarvest, ref moved);
                    Deliver(containers, biomassItem, treeBiomassPerHarvest, ref moved);
                    cell.tree = false;
                    cell.growth = 0f;
                    continue;
                }

                HarvestSingleCellToContainers(cell, containers, replantAfterHarvest, ref moved);
            }

            if (moved > 0) RefreshPods();
            return moved;
        }

        private bool HarvestSingleCellToContainers(GridFarmCell cell, List<GridCargoContainer> containers,
            bool allowReplant, ref int moved)
        {
            if (cell == null || cell.crop == null) return false;
            var crop = cell.crop;
            bool canReplant = allowReplant && crop.seedItem != null && crop.seedReturnAmount > 0;
            int harvestQty = Mathf.Max(1, crop.harvestAmount) + (cell.fertilized ? 1 : 0);
            int seedsToGive = Mathf.Max(0, crop.seedReturnAmount);
            int seedsKept = canReplant && seedsToGive > 0 ? 1 : 0;

            if (!CanDeliver(containers, crop.harvestItem, harvestQty)) return false;
            if (seedsToGive - seedsKept > 0
                && !CanDeliver(containers, crop.seedItem, seedsToGive - seedsKept)) return false;

            Deliver(containers, crop.harvestItem, harvestQty, ref moved);
            Deliver(containers, crop.seedItem, seedsToGive - seedsKept, ref moved);

            if (canReplant)
            {
                cell.growth = 0f;
                cell.dryTimer = 0f;
                cell.fertilized = false;
            }
            else
            {
                cell.crop = null;
                cell.tree = false;
                cell.growth = 0f;
                cell.dryTimer = 0f;
                cell.fertilized = false;
                cell.dead = false;
            }
            return true;
        }

        private bool CanDeliver(List<GridCargoContainer> containers, ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return true;
            if (HasRoomAnywhere(containers, item, count)) return true;
            return harvestOutput != null && harvestOutput.HasSpace(item, count);
        }

        private void Deliver(List<GridCargoContainer> containers, ItemDefinition item, int count, ref int moved)
        {
            if (item == null || count <= 0) return;

            int left = count - PushToCargo(containers, item, count);
            moved += count - left;
            if (left <= 0 || harvestOutput == null) return;

            var leftover = harvestOutput.Insert(new ItemStack { item = item, count = left });
            int stored = left - (leftover != null && !leftover.IsEmpty ? leftover.count : 0);
            moved += stored;
        }

        private static bool HasRoomAnywhere(List<GridCargoContainer> containers, ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return true;
            for (int i = 0; i < containers.Count; i++)
            {
                var c = containers[i];
                if (c == null || c.container == null) continue;
                if (c.container.HasSpace(item, count)) return true;
            }
            return false;
        }

        private static int PushToCargo(List<GridCargoContainer> containers, ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            int remaining = count;
            for (int i = 0; i < containers.Count && remaining > 0; i++)
            {
                var c = containers[i];
                if (c == null || c.container == null) continue;
                if (!c.container.HasSpace(item, remaining)) continue;
                var leftover = c.container.Insert(new ItemStack { item = item, count = remaining });
                remaining = leftover != null && !leftover.IsEmpty ? leftover.count : 0;
            }
            return count - remaining;
        }

        // ── Player / automation control ───────────────────────────────────────
        /// <summary>
        /// Plants <paramref name="item"/> into a specific cell index. A sapling on a
        /// tree-capable bed claims the whole plot (<see cref="treeBed"/> = true) after
        /// harvesting any ripe crops into the output bin.
        /// </summary>
        public bool TryPlantAtCell(int index, ItemDefinition item)
        {
            EnsureState();
            if (!CanPlantInCell(index, item)) return false;

            if (IsTreeSeed(item))
            {
                ClearCropsForTree();
                treeBed = true;
                treeCell = index;
                treeGrowth = 0f;
                EnsurePods();
                RefreshPods();
                return true;
            }

            var crop = CropOf(item);
            if (crop == null) return false;
            var cell = cells[index];
            cell.tree = false;
            cell.crop = crop;
            cell.growth = 0f;
            cell.dryTimer = 0f;
            cell.fertilized = false;
            cell.dead = false;
            EnsurePods();
            RefreshPods();
            return true;
        }

        /// <summary>Plant a seed or a sapling in the first free cell. -1 = can't, -2 = trees need the box.</summary>
        public int TryPlant(ItemDefinition item)
        {
            if (item == null) return -1;
            EnsureState();

            bool isTree = IsTreeSeed(item);
            var crop = CropOf(item);
            if (!isTree && crop == null) return -1;
            if (isTree && !allowsTrees) return -2;

            if (isTree)
            {
                if (treeBed) return -1;
                int target = FirstFreeCell();
                if (target < 0) target = 0;
                return TryPlantAtCell(target, item) ? target : -1;
            }

            int index = FirstFreeCell();
            if (index < 0) return -1;
            return TryPlantAtCell(index, item) ? index : -1;
        }

        private void ClearCropsForTree()
        {
            if (cells == null) return;
            EnsureContainers();
            var containers = Grid != null && GridItemNetwork.Instance != null
                ? GridItemNetwork.Instance.GetConnectedContainers(Grid)
                : new List<GridCargoContainer>();
            int moved = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                if (c == null) continue;
                if (!c.dead && c.crop != null && c.growth >= 0.999f)
                    HarvestSingleCellToContainers(c, containers, allowReplant: false, ref moved);
                c.crop = null;
                c.tree = false;
                c.growth = 0f;
                c.dryTimer = 0f;
                c.fertilized = false;
                c.dead = false;
            }
        }

        public bool TryFertilize(ItemDefinition item)
        {
            if (item == null || !TreeFarmPlot.IsFertilizerOrBiomass(item)) return false;
            EnsureCells();
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (cell == null || cell.dead || cell.fertilized) continue;
                if (cell.crop == null && !cell.tree) continue;
                cell.fertilized = true;
                return true;
            }
            return false;
        }

        public int ClearDead()
        {
            int cleared = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (cell == null || !cell.dead) continue;
                cell.dead = false;
                cell.crop = null;
                cell.tree = false;
                cell.growth = 0f;
                cell.dryTimer = 0f;
                cell.fertilized = false;
                cleared++;
            }
            RefreshPods();
            return cleared;
        }

        private bool IsFreeCell(int index)
            => !treeBed
            && cells != null && index >= 0 && index < cells.Length
            && cells[index] != null && cells[index].crop == null && !cells[index].tree && !cells[index].dead;

        public int FirstFreeCell()
        {
            if (treeBed || cells == null) return -1;
            for (int i = 0; i < cells.Length; i++)
                if (IsFreeCell(i)) return i;
            return -1;
        }

        // ── Bush, stem, fruit & 3D tree visuals ───────────────────────────────
        protected virtual Vector3 PodPosition(int index, int side)
        {
            int n = Mathf.Max(1, side);
            float step = 2.5f * 0.26f;
            int x = index % n;
            int z = index / n;
            float offset = (n - 1) * 0.5f;
            return new Vector3((x - offset) * step, -0.55f, (z - offset) * step);
        }

        private void EnsurePods()
        {
            EnsureCells();
            int wanted = cellCount;
            if (_podRoot == null)
            {
                var existing = transform.Find(VisualRootName);
                if (existing != null) _podRoot = existing;
                else
                {
                    var go = new GameObject(VisualRootName);
                    go.transform.SetParent(transform, false);
                    _podRoot = go.transform;
                }
            }

            if (_pods.Count == wanted && (!allowsTrees || _treeRoot != null))
                return;

            _pods.Clear();
            _cellLeafRenderers.Clear();
            _cellStemRenderers.Clear();
            _cellFruitRenderers.Clear();

            int side = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(wanted)));
            float plantScale = PodVisualScale;
            var mat = SharedLitMaterial();

            for (int i = 0; i < wanted; i++)
            {
                string name = "Crop_" + (i + 1);
                var child = _podRoot.Find(name);
                if (child != null && child.Find("Stem_Low") == null)
                {
                    if (Application.isPlaying) Destroy(child.gameObject);
                    else DestroyImmediate(child.gameObject);
                    child = null;
                }
                if (child == null)
                    child = BuildCropPlant(_podRoot, name, i, mat);

                child.localPosition = PodPosition(i, side);
                child.localScale = Vector3.one * plantScale;
                _pods.Add(child);
                CachePlantRenderers(child);
            }

            if (allowsTrees)
            {
                var tree = _podRoot.Find("TreeRoot");
                if (tree == null)
                    tree = BuildTreeVisual(_podRoot, mat);
                _treeRoot = tree;
                _treeCanopy = tree.Find("Canopy_Main");
            }
        }

        private static Transform BuildCropPlant(Transform parent, string name, int index, Material mat)
        {
            var cellRoot = new GameObject(name).transform;
            cellRoot.SetParent(parent, false);

            Prim(PrimitiveType.Cylinder, "Mound", cellRoot,
                new Vector3(0f, 0.012f, 0f), new Vector3(0.20f, 0.012f, 0.20f),
                Quaternion.identity, mat, new Color(0.22f, 0.14f, 0.08f));

            Prim(PrimitiveType.Cylinder, "Stem_Low", cellRoot,
                new Vector3(0f, 0.08f, 0f), new Vector3(0.032f, 0.075f, 0.032f),
                Quaternion.Euler(0f, 0f, 3f), mat, new Color(0.30f, 0.44f, 0.18f));
            Prim(PrimitiveType.Cylinder, "Stem_Top", cellRoot,
                new Vector3(-0.006f, 0.19f, 0.004f), new Vector3(0.024f, 0.065f, 0.024f),
                Quaternion.Euler(4f, 0f, -5f), mat, new Color(0.32f, 0.48f, 0.20f));

            float baseYaw = (index * 37f) % 360f;
            for (int l = 0; l < 4; l++)
            {
                float yaw = baseYaw + l * 90f;
                Quaternion rot = Quaternion.Euler(28f, yaw, 0f);
                Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                float h = 0.08f + (l & 1) * 0.03f;
                Prim(PrimitiveType.Sphere, "Leaf_" + l, cellRoot,
                    dir * 0.075f + new Vector3(0f, h, 0f),
                    new Vector3(0.11f, 0.032f, 0.16f), rot, mat, LeafLitTint);
            }
            for (int l = 0; l < 4; l++)
            {
                float yaw = baseYaw + 45f + l * 90f;
                Quaternion rot = Quaternion.Euler(18f, yaw, 0f);
                Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                float h = 0.17f + (l & 1) * 0.025f;
                Prim(PrimitiveType.Sphere, "LeafB_" + l, cellRoot,
                    dir * 0.06f + new Vector3(0f, h, 0f),
                    new Vector3(0.095f, 0.030f, 0.13f), rot, mat, LeafLitTint);
            }

            Prim(PrimitiveType.Sphere, "Fruit_0", cellRoot,
                new Vector3(0.045f, 0.22f, 0.030f), new Vector3(0.065f, 0.065f, 0.065f),
                Quaternion.identity, mat, new Color(0.86f, 0.34f, 0.20f));
            Prim(PrimitiveType.Sphere, "Fruit_1", cellRoot,
                new Vector3(-0.040f, 0.24f, 0.025f), new Vector3(0.058f, 0.058f, 0.058f),
                Quaternion.identity, mat, new Color(0.86f, 0.34f, 0.20f));
            Prim(PrimitiveType.Sphere, "Fruit_2", cellRoot,
                new Vector3(0.010f, 0.26f, -0.040f), new Vector3(0.055f, 0.055f, 0.055f),
                Quaternion.identity, mat, new Color(0.86f, 0.34f, 0.20f));

            cellRoot.gameObject.SetActive(false);
            return cellRoot;
        }

        private static Transform BuildTreeVisual(Transform parent, Material mat)
        {
            var tree = new GameObject("TreeRoot").transform;
            tree.SetParent(parent, false);
            tree.localPosition = Vector3.zero;

            Prim(PrimitiveType.Cylinder, "Trunk", tree,
                new Vector3(0f, 0.48f, 0f), new Vector3(0.22f, 0.48f, 0.22f),
                Quaternion.identity, mat, TrunkBark);
            Prim(PrimitiveType.Cylinder, "TrunkTop", tree,
                new Vector3(0.02f, 1.05f, -0.01f), new Vector3(0.15f, 0.28f, 0.15f),
                Quaternion.Euler(3f, 0f, -4f), mat, TrunkBark);

            var canopy = Prim(PrimitiveType.Sphere, "Canopy_Main", tree,
                new Vector3(0f, 1.42f, 0f), new Vector3(1.15f, 0.92f, 1.15f),
                Quaternion.identity, mat, CanopyFoliage);
            Prim(PrimitiveType.Sphere, "Canopy_Crown", canopy,
                new Vector3(0.04f, 0.36f, -0.03f), new Vector3(0.72f, 0.65f, 0.72f),
                Quaternion.identity, mat, new Color(0.26f, 0.60f, 0.24f));
            Prim(PrimitiveType.Sphere, "Canopy_West", canopy,
                new Vector3(-0.34f, -0.06f, 0.10f), new Vector3(0.65f, 0.58f, 0.65f),
                Quaternion.identity, mat, new Color(0.20f, 0.48f, 0.20f));
            Prim(PrimitiveType.Sphere, "Canopy_East", canopy,
                new Vector3(0.32f, -0.04f, -0.12f), new Vector3(0.62f, 0.55f, 0.62f),
                Quaternion.identity, mat, new Color(0.24f, 0.56f, 0.22f));

            tree.gameObject.SetActive(false);
            return tree;
        }

        private static Transform Prim(PrimitiveType type, string name, Transform parent,
            Vector3 localPos, Vector3 localScale, Quaternion localRot, Material mat, Color tint)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (mat != null) r.sharedMaterial = mat;
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", tint);
                mpb.SetColor("_Color", tint);
                r.SetPropertyBlock(mpb);
            }
            return go.transform;
        }

        private void CachePlantRenderers(Transform cellRoot)
        {
            var leaves = new List<Renderer>(8);
            var stems  = new List<Renderer>(2);
            var fruits = new List<Renderer>(3);
            if (cellRoot != null)
            {
                for (int c = 0; c < cellRoot.childCount; c++)
                {
                    var ch = cellRoot.GetChild(c);
                    var r = ch.GetComponent<Renderer>();
                    if (r == null) continue;
                    string n = ch.name;
                    if (n.StartsWith("Leaf")) leaves.Add(r);
                    else if (n.StartsWith("Stem")) stems.Add(r);
                    else if (n.StartsWith("Fruit")) fruits.Add(r);
                }
            }
            _cellLeafRenderers.Add(leaves.ToArray());
            _cellStemRenderers.Add(stems.ToArray());
            _cellFruitRenderers.Add(fruits.ToArray());
        }

        public void RefreshPods()
        {
            if (_pods.Count != cellCount)
                EnsurePods();

            if (treeBed && allowsTrees)
            {
                HideCropPods();
                RefreshTreeVisual();
                return;
            }

            if (_treeRoot != null && _treeRoot.gameObject.activeSelf)
                _treeRoot.gameObject.SetActive(false);

            for (int i = 0; i < _pods.Count && i < cells.Length; i++)
                RefreshPod(i, _pods[i], cells[i]);
        }

        private void HideCropPods()
        {
            for (int i = 0; i < _pods.Count; i++)
                if (_pods[i] != null && _pods[i].gameObject.activeSelf)
                    _pods[i].gameObject.SetActive(false);
        }

        private void RefreshTreeVisual()
        {
            if (_treeRoot == null) return;
            if (!treeBed)
            {
                _treeRoot.gameObject.SetActive(false);
                return;
            }
            if (!_treeRoot.gameObject.activeSelf)
                _treeRoot.gameObject.SetActive(true);

            int side = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(cellCount)));
            int idx = (treeCell >= 0 && cells != null && treeCell < cells.Length) ? treeCell : 0;
            _treeRoot.localPosition = PodPosition(idx, side);

            float g = Mathf.Clamp01(treeGrowth);
            float trunkXZ = Mathf.Lerp(0.24f, 0.78f, g);
            float trunkY  = Mathf.Lerp(0.20f, 0.78f, g);
            _treeRoot.localScale = new Vector3(trunkXZ, trunkY, trunkXZ);
            if (_treeCanopy != null)
            {
                float c = Mathf.Lerp(0.32f, 1.0f, Mathf.SmoothStep(0f, 1f, g));
                _treeCanopy.localScale = new Vector3(1.15f * c, 0.92f * c, 1.15f * c);
            }
        }

        private void RefreshPod(int index, Transform pod, GridFarmCell cell)
        {
            if (pod == null) return;
            bool planted = cell != null && (cell.crop != null || cell.tree);
            if (!planted)
            {
                if (pod.gameObject.activeSelf) pod.gameObject.SetActive(false);
                return;
            }

            if (!pod.gameObject.activeSelf) pod.gameObject.SetActive(true);
            float baseScale = PodVisualScale;

            if (cell.dead)
            {
                pod.localScale = new Vector3(0.70f, 0.42f, 0.70f) * baseScale;
                SetFruitVisible(index, false);
                TintPlant(index, DeadTint, isDead: true);
                return;
            }

            float g = Mathf.Clamp01(cell.growth);
            Vector3 scale = InterpolatedCropScale(cell, g);
            float breadth = Mathf.Max(0.28f, scale.x * 1.15f);
            float height  = Mathf.Max(0.24f, scale.y * 1.20f);
            pod.localScale = new Vector3(breadth, height, breadth) * baseScale;

            SetFruitVisible(index, g >= 0.72f);

            int stages = cell.crop != null ? Mathf.Max(1, cell.crop.growthStages) : 4;
            int stage = Mathf.Clamp(Mathf.FloorToInt(g * stages), 0, stages - 1);
            Color tint = LeafLitTint;
            if (cell.crop != null && cell.crop.stageColors != null && cell.crop.stageColors.Length > 0)
                tint = cell.crop.stageColors[Mathf.Min(stage, cell.crop.stageColors.Length - 1)];
            TintPlant(index, tint, isDead: false);
        }

        private static Vector3 InterpolatedCropScale(GridFarmCell cell, float g)
        {
            if (cell.crop != null && cell.crop.stageScales != null && cell.crop.stageScales.Length > 0)
            {
                var arr = cell.crop.stageScales;
                float s;
                if (arr.Length == 1)
                {
                    s = arr[0];
                }
                else
                {
                    float pos = Mathf.Clamp01(g) * (arr.Length - 1);
                    int lo = Mathf.Clamp(Mathf.FloorToInt(pos), 0, arr.Length - 1);
                    int hi = Mathf.Clamp(lo + 1, 0, arr.Length - 1);
                    s = Mathf.Lerp(arr[lo], arr[hi], pos - lo);
                }
                return new Vector3(
                    Mathf.Lerp(0.28f, 0.90f, Mathf.Clamp01(s)),
                    Mathf.Max(0.18f, s),
                    Mathf.Lerp(0.28f, 0.90f, Mathf.Clamp01(s)));
            }
            return new Vector3(
                Mathf.Lerp(0.28f, 0.90f, g),
                Mathf.Lerp(0.18f, 0.95f, g),
                Mathf.Lerp(0.28f, 0.90f, g));
        }

        private void SetFruitVisible(int index, bool visible)
        {
            if (index < 0 || index >= _cellFruitRenderers.Count) return;
            var fruits = _cellFruitRenderers[index];
            for (int i = 0; i < fruits.Length; i++)
                if (fruits[i] != null && fruits[i].gameObject.activeSelf != visible)
                    fruits[i].gameObject.SetActive(visible);
        }

        private void TintPlant(int index, Color color, bool isDead)
        {
            _mpb ??= new MaterialPropertyBlock();

            Color leafColor = isDead
                ? color
                : Color.Lerp(color, new Color(0.32f, 0.78f, 0.28f), 0.30f);
            Color stemColor = isDead
                ? color * 0.80f
                : Color.Lerp(leafColor, new Color(0.32f, 0.24f, 0.14f), 0.45f);
            Color fruitColor = isDead
                ? color
                : Color.Lerp(color, new Color(0.95f, 0.52f, 0.18f), 0.55f);

            ApplyColor(index < _cellLeafRenderers.Count ? _cellLeafRenderers[index] : null, leafColor);
            ApplyColor(index < _cellStemRenderers.Count ? _cellStemRenderers[index] : null, stemColor);
            ApplyColor(index < _cellFruitRenderers.Count ? _cellFruitRenderers[index] : null, fruitColor);
        }

        private void ApplyColor(Renderer[] renderers, Color color)
        {
            if (renderers == null) return;
            _mpb.SetColor("_BaseColor", color);
            _mpb.SetColor("_Color", color);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].SetPropertyBlock(_mpb);
        }

        private static Material s_podMat;
        private static Material SharedLitMaterial()
        {
            if (s_podMat != null) return s_podMat;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh == null) return null;
            s_podMat = new Material(sh) { name = "GridFarmPlotPod_Runtime" };
            if (s_podMat.HasProperty("_Smoothness")) s_podMat.SetFloat("_Smoothness", 0.25f);
            return s_podMat;
        }

        // ── Persistence (IMachineProcessState) ────────────────────────────────
        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureCells();

            state.SetExtra("water", Mathf.Max(0f, waterStored));
            state.SetExtra("enabled", Enabled ? 1f : 0f);
            state.SetExtra("auto", automatic ? 1f : 0f);
            state.SetExtra("replant", replantAfterHarvest ? 1f : 0f);
            state.SetExtra("treeBed", treeBed ? 1f : 0f);
            state.SetExtra("treeGrowth", Mathf.Clamp01(treeGrowth));
            state.SetExtra("treeCell", treeCell);
            state.SetExtra("cells", cells.Length);
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (cell == null) continue;
                state.SetText("c" + i, cell.crop != null ? cell.crop.name : cell.tree ? "TREE" : string.Empty);
                state.SetText("s" + i, cell.crop != null && cell.crop.seedItem != null ? cell.crop.seedItem.itemId : string.Empty);
                state.SetExtra("g" + i, Mathf.Clamp01(cell.growth));
                state.SetExtra("f" + i, cell.fertilized ? 1f : 0f);
                state.SetExtra("d" + i, cell.dead ? 1f : 0f);
                state.SetExtra("dry" + i, Mathf.Max(0f, cell.dryTimer));
            }
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null || state.IsEmpty) return;
            EnsureCells();

            waterStored = Mathf.Clamp(
                MachineProcessPersistence.FiniteOr(state.GetExtra("water", waterStored), waterStored),
                0f, Mathf.Max(0f, waterCapacity));
            Enabled = state.GetExtra("enabled", Enabled ? 1f : 0f) >= 0.5f;
            automatic = state.GetExtra("auto", automatic ? 1f : 0f) >= 0.5f;
            replantAfterHarvest = state.GetExtra("replant", replantAfterHarvest ? 1f : 0f) >= 0.5f;
            treeBed = allowsTrees && state.GetExtra("treeBed", 0f) >= 0.5f;
            treeGrowth = MachineProcessPersistence.ClampOr(state.GetExtra("treeGrowth", 0f), 0f, 1f, 0f);
            treeCell = Mathf.RoundToInt(MachineProcessPersistence.FiniteOr(state.GetExtra("treeCell", treeBed ? 0f : -1f), treeBed ? 0f : -1f));

            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (cell == null) continue;

                string cropName = state.GetText("c" + i, string.Empty);
                if (string.IsNullOrEmpty(cropName))
                {
                    cell.crop = null;
                    cell.tree = false;
                    cell.growth = 0f;
                    cell.dryTimer = 0f;
                    cell.dead = false;
                    cell.fertilized = false;
                    continue;
                }

                cell.growth = MachineProcessPersistence.ClampOr(state.GetExtra("g" + i, 0f), 0f, 1f, 0f);
                cell.fertilized = state.GetExtra("f" + i, 0f) >= 0.5f;
                cell.dead = state.GetExtra("d" + i, 0f) >= 0.5f;
                cell.dryTimer = Mathf.Max(0f, MachineProcessPersistence.FiniteOr(state.GetExtra("dry" + i, 0f), 0f));

                if (cropName == "TREE")
                {
                    cell.crop = null;
                    cell.tree = false;
                    if (allowsTrees)
                    {
                        treeBed = true;
                        treeCell = i;
                        treeGrowth = cell.growth;
                    }
                    cell.growth = 0f;
                }
                else
                {
                    cell.tree = false;
                    cell.crop = CropCatalog.Resolve(cropName, state.GetText("s" + i, string.Empty));
                    if (cell.crop == null)
                    {
                        cell.growth = 0f;
                        cell.dead = false;
                    }
                }
            }

            EnsurePods();
            RefreshPods();
        }

        // ── IGridDataProvider for screens ─────────────────────────────────────
        public string SourceName => string.IsNullOrEmpty(blockName) ? "Farm Plot" : blockName;

        public string DataCategory => "Life Support";

        public string GetDisplayData()
        {
            string crop = CropName;
            string readyStr = treeBed
                ? (treeGrowth >= 0.999f ? "1/1 ready" : $"{TreeGrowth01 * 100f:0}%")
                : $"{ReadyCount}/{cells.Length} ready";
            return $"{PlotKind} FARM\n{Status}\n{crop} {readyStr}\n"
                 + $"Water {waterStored:0}/{waterCapacity:0} L {(HasWaterSupply ? "(piped)" : "(dry)")}\n"
                 + $"Air {(HasAir ? "OK" : "NONE")} · {Growth01 * 100f:0}%";
        }
    }
}
