// Assets/Scripts/VoxelEngine/GridSystem/GridFarmPlotBase.cs
//
// 18.4.0-dev — grid-mounted farm plots.
//
// Everything in this file exists because a farm on a ship is not the same problem as
// a farm on the ground. Three things differ and each one shows up here:
//
//   * Oxygen. A grid farm has no planet to lean on. It is breathable when the room it
//     stands in is sealed and breathable, OR when the ship is parked on a world with
//     air - exactly the rule `FarmEnvironment.HasBreathableAir` already encodes for the
//     ground beds, so a crop never gets a different answer depending on what it stands
//     on. Crops that need oxygen pause without it; they never die of it.
//   * Water. There is no sprinkler mast on a hull. Water comes in through the grid
//     liquid network into the plot's own tank, and a dry tank simply stops growth
//     until the plumbing catches up.
//   * Harvest. There is no player walking past. A ripe cell is moved into a connected
//     grid cargo container, and only when that container has room - a plot never
//     destroys a harvest it cannot deliver.
//
// The per-cell model matches the ground PlantBox, so a player who has used one already
// knows how the other behaves.

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
        public bool fertilized;
        public bool dead;
        public bool tree;
    }

    /// <summary>
    /// Shared behaviour for the grid farm plots: the vertical stack and the floor box.
    /// Subclasses say how many cells they have, how they are laid out and whether a tree
    /// is allowed; everything else - power, water, oxygen, growth, harvest, persistence -
    /// is one implementation, so the two blocks cannot drift apart.
    /// </summary>
    public abstract class GridFarmPlotBase : GridBlock, IGridDataProvider, IMachineProcessState
    {
        [Header("Cells")]
        [Tooltip("Live state of every growing cell, row by row.")]
        public GridFarmCell[] cells = new GridFarmCell[0];

        [Tooltip("Whether a tree sapling may be planted here. Only the floor box allows it.")]
        public bool allowsTrees = false;

        [Header("Containers")]
        [Tooltip("Seeds and saplings waiting to go into a free cell. The plot plants from here on its own.")]
        public ItemContainer seedInput;
        [Tooltip("Produce taken off the beds, waiting for a route. The player can take it straight out of the panel.")]
        public ItemContainer harvestOutput;

        public const int SEED_SLOTS = 4;
        public const int OUTPUT_SLOTS = 9;

        [Header("Power")]
        [Tooltip("Watts drawn while any cell is growing.")]
        public float powerDraw = 55f;
        [Tooltip("Watts drawn while idle but switched on. A plot with lamps and pumps is never free.")]
        public float idlePowerDraw = 6f;

        [Header("Water")]
        public float waterCapacity = 200f;
        public float waterStored = 120f;
        [Tooltip("Litres per growing cell per second.")]
        public float litresPerCellSecond = 0.02f;
        [Tooltip("Litres pulled per second from the grid liquid network while the tank has room.")]
        public float refillLitresPerSecond = 2.5f;

        [Header("Tuning")]
        public float fertilizedSpeedMultiplier = 1.5f;
        [Tooltip("Seconds between oxygen and atmosphere samples.")]
        public float environmentCheckInterval = 2f;
        [Tooltip("Keep one seed of every harvest back and replant the cell.")]
        public bool replantAfterHarvest = true;
        [Tooltip("Move ripe produce into connected grid cargo on its own.")]
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

        /// <summary>The panel walks the beds by index; the array itself stays this class's business.</summary>
        public int cellCount => cells != null ? cells.Length : 0;

        /// <summary>One bed, or null when the index is outside the plot.</summary>
        public GridFarmCell CellAt(int index)
            => cells != null && index >= 0 && index < cells.Length ? cells[index] : null;

        /// <summary>0..1 of the beds that carry a living crop - the "Beds" gauge.</summary>
        public float CellsFill01
        {
            get
            {
                int total = cellCount;
                return total > 0 ? Mathf.Clamp01(PlantedCount / (float)total) : 0f;
            }
        }

        /// <summary>One short word for the cell layout: "VERTICAL", "FLOOR".</summary>
        public abstract string PlotKind { get; }

        public float WaterFill01 => waterCapacity > 0f ? Mathf.Clamp01(waterStored / waterCapacity) : 0f;

        /// <summary>True when the plot has breathable air - a sealed room or a breathable world.</summary>
        public bool HasAir { get; private set; }

        /// <summary>True when a connected liquid source is filling the tank.</summary>
        public bool HasWaterSupply => Grid != null && GridLiquidNetwork.Instance != null
                                      && GridLiquidNetwork.Instance.GetTanks(Grid, LiquidType.Water).Count > 0;

        public bool IsIrrigated => waterStored > 0.05f;

        public int PlantedCount
        {
            get
            {
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
                int n = 0;
                for (int i = 0; i < cells.Length; i++)
                    if (cells[i] != null && !cells[i].dead && (cells[i].crop != null || cells[i].tree)
                        && cells[i].growth >= 1f) n++;
                return n;
            }
        }

        public int DeadCount
        {
            get
            {
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

        public bool IsProducing { get; private set; }

        public string Status
        {
            get
            {
                if (!Enabled) return "DISABLED";
                if (Grid == null) return "NO GRID";
                if (Grid != null && !Grid.HasPower) return "NO POWER";
                if (DeadCount > 0 && DeadCount == PlantedCount) return "LOST CROP";
                if (ReadyCount > 0) return "READY x" + ReadyCount;
                if (PlantedCount == 0) return "EMPTY";
                if (!IsIrrigated) return "DRY";
                if (NeedsAirNow()) return "NO O2";
                return "GROWING";
            }
        }

        public override float PowerDraw
        {
            get
            {
                if (!Enabled) return 0f;
                return PlantedCount > 0 ? powerDraw : idlePowerDraw;
            }
        }

        public override float ContentMass => (waterStored * 1f) + (PlantedCount * 4f);

        private float _envTimer;
        private float _pullTimer;
        private float _plantTimer;

        // ── Lifecycle ───────────────────────────────────────────────────────
        protected virtual void Awake()
        {
            EnsureCells();
            EnsureContainers();
            ResolveTreeItems();
            HasAir = FarmEnvironment.HasBreathableAir(transform.position);
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            EnsureCells();
            EnsureContainers();
            ResolveTreeItems();
            HasAir = FarmEnvironment.HasBreathableAir(transform.position);
        }

        private void EnsureCells()
        {
            int wanted = Mathf.Max(1, CellCount);
            if (cells == null) cells = new GridFarmCell[0];
            if (cells.Length == wanted) return;   // never resize a live array; it would drop crops

            var next = new GridFarmCell[wanted];
            for (int i = 0; i < wanted; i++)
                next[i] = i < cells.Length && cells[i] != null ? cells[i] : new GridFarmCell();
            cells = next;
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

        /// <summary>
        /// Create or resize the two containers. Filters keep the seed bin honest: only
        /// something that can actually be planted goes in, so the plot never spends a
        /// cycle discovering that a rock is not a seed.
        /// </summary>
        public void EnsureContainers()
        {
            if (seedInput == null) seedInput = new ItemContainer("Seeds", SEED_SLOTS);
            else if (seedInput.Size != SEED_SLOTS) seedInput.Resize(SEED_SLOTS);
            seedInput.AcceptFilter = (item, wanted) => CanPlant(item) ? wanted : 0;

            if (harvestOutput == null) harvestOutput = new ItemContainer("Harvest", OUTPUT_SLOTS);
            else if (harvestOutput.Size != OUTPUT_SLOTS) harvestOutput.Resize(OUTPUT_SLOTS);
        }

        /// <summary>True when this plot would accept <paramref name="item"/> in a free cell.</summary>
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

        /// <summary>Take one seed or sapling out of the seed bin into the first free cell.</summary>
        private bool PlantFromSeedBin()
        {
            if (seedInput == null) return false;
            int free = FirstFreeCell();
            if (free < 0) return false;

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

        // ── Simulation ──────────────────────────────────────────────────────
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

            // A plot with seeds in its bin fills its own empty cells, so a farm can be
            // set up once instead of walked every cycle.
            _plantTimer += dt;
            if (_plantTimer >= 1f)
            {
                _plantTimer = 0f;
                while (PlantFromSeedBin()) { }
            }
            IsProducing = powered && PlantedCount > 0;

            GrowCells(powered, dt);

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

        private void GrowCells(bool powered, float dt)
        {
            if (!powered) return;   // lamps and pumps are what the power buys

            int drinking = 0;
            bool irrigated = IsIrrigated;

            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (cell == null || cell.dead) continue;
                bool planted = cell.crop != null || cell.tree;
                if (!planted || cell.growth >= 1f) continue;

                if (cell.tree)
                {
                    float treeSpeed = 1f / Mathf.Max(1f, treeGrowthSeconds);
                    if (irrigated) treeSpeed *= Mathf.Max(1f, treeIrrigatedSpeedMultiplier);
                    cell.growth = Mathf.Clamp01(cell.growth + treeSpeed * dt);
                    if (irrigated) drinking++;
                    continue;
                }

                var crop = cell.crop;
                if (crop == null) continue;

                // Oxygen is a pause, not a death sentence: a ship that loses its air
                // loses its growth rate, not its crop.
                if (crop.requiresOxygen && !HasAir) continue;

                float speed = 1f / Mathf.Max(0.5f, crop.growthTime);
                if (irrigated) { speed *= Mathf.Max(1f, crop.irrigatedSpeedMultiplier); drinking++; }
                else if (crop.requiresWater) speed *= 0.3f;
                if (cell.fertilized) speed *= Mathf.Max(1f, fertilizedSpeedMultiplier);

                cell.growth = Mathf.Clamp01(cell.growth + speed * dt);
            }

            if (drinking > 0)
                waterStored = Mathf.Max(0f, waterStored - litresPerCellSecond * drinking * dt);
        }

        private bool NeedsAirNow()
        {
            if (HasAir) return false;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] != null && !cells[i].dead && cells[i].crop != null && cells[i].crop.requiresOxygen
                    && cells[i].growth < 1f) return true;
            return false;
        }

        // ── Harvest ─────────────────────────────────────────────────────────
        /// <summary>
        /// Move every ripe cell out to a route or into the plot's own output bin. A cell is
        /// only emptied once the whole payload has somewhere to go, so a full hold and an
        /// empty hold both end with the crop still on the bed rather than gone.
        /// </summary>
        public int PushRipeToGrid()
        {
            EnsureContainers();

            // A route is optional: with no cargo connected the harvest lands in the plot's
            // own output bin, which the player empties from the panel. Nothing is ever
            // destroyed because a ship has no hold yet.
            var containers = Grid != null && GridItemNetwork.Instance != null
                ? GridItemNetwork.Instance.GetConnectedContainers(Grid)
                : null;
            if (containers == null) containers = new List<GridCargoContainer>();

            int moved = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (cell == null || cell.dead || cell.growth < 1f) continue;
                if (cell.crop == null && !cell.tree) continue;

                bool canReplant = replantAfterHarvest && !cell.tree && cell.crop != null
                                  && cell.crop.seedItem != null && cell.crop.seedReturnAmount > 0;

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

                var crop = cell.crop;
                int seedsToGive = Mathf.Max(0, crop.seedReturnAmount);
                int seedsKept = canReplant && seedsToGive > 0 ? 1 : 0;
                if (!CanDeliver(containers, crop.harvestItem, crop.harvestAmount)) continue;
                if (seedsToGive - seedsKept > 0
                    && !CanDeliver(containers, crop.seedItem, seedsToGive - seedsKept)) continue;

                Deliver(containers, crop.harvestItem, crop.harvestAmount, ref moved);
                Deliver(containers, crop.seedItem, seedsToGive - seedsKept, ref moved);

                if (canReplant)
                {
                    cell.growth = 0f;
                    cell.fertilized = false;
                }
                else
                {
                    cell.crop = null;
                    cell.growth = 0f;
                    cell.fertilized = false;
                }
            }
            return moved;
        }

        /// <summary>True when the route OR the plot's own bin can take the whole payload.</summary>
        private bool CanDeliver(List<GridCargoContainer> containers, ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return true;
            if (HasRoomAnywhere(containers, item, count)) return true;
            return harvestOutput != null && harvestOutput.HasSpace(item, count);
        }

        /// <summary>Hand a stack to the route first and to the plot's own bin second.</summary>
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
            // No cargo at all means the machine is unfinished plumbing, not broken:
            // report "no room" so nothing is destroyed.
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

        // ── Player / automation control ─────────────────────────────────────
        /// <summary>Plant a seed or a sapling in the first free cell. -1 = can't, -2 = trees need the box.</summary>
        public int TryPlant(ItemDefinition item)
        {
            if (item == null) return -1;
            EnsureCells();

            bool isTree = item is SeedItem seed && seed.crop == null && TreeFarmPlot.IsSaplingOrTreeSeed(item);
            var crop = item is SeedItem si ? si.crop : null;
            if (!isTree && crop == null) return -1;
            if (isTree && !allowsTrees) return -2;

            int index = FirstFreeCell();
            if (index < 0) return -1;

            var cell = cells[index];
            cell.tree = isTree;
            cell.crop = isTree ? null : crop;
            cell.growth = 0f;
            cell.fertilized = false;
            cell.dead = false;
            return index;
        }

        /// <summary>Fertilize the first growing cell that is not fertilized yet.</summary>
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

        /// <summary>Clear every lost cell. Returns how many were cleared.</summary>
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
                cell.fertilized = false;
                cleared++;
            }
            return cleared;
        }

        public int FirstFreeCell()
        {
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] != null && cells[i].crop == null && !cells[i].tree && !cells[i].dead) return i;
            return -1;
        }

        // ── Persistence ─────────────────────────────────────────────────────
        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureCells();

            state.SetExtra("water", Mathf.Max(0f, waterStored));
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
            }
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null || state.IsEmpty) return;
            EnsureCells();

            waterStored = Mathf.Clamp(
                MachineProcessPersistence.FiniteOr(state.GetExtra("water", waterStored), waterStored),
                0f, Mathf.Max(0f, waterCapacity));

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
                    cell.dead = false;
                    cell.fertilized = false;
                    continue;
                }

                cell.growth = MachineProcessPersistence.ClampOr(state.GetExtra("g" + i, 0f), 0f, 1f, 0f);
                cell.fertilized = state.GetExtra("f" + i, 0f) >= 0.5f;
                cell.dead = state.GetExtra("d" + i, 0f) >= 0.5f;

                if (cropName == "TREE")
                {
                    cell.tree = true;
                    cell.crop = null;
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
        }

        // ── Screens ─────────────────────────────────────────────────────────
        public string SourceName => string.IsNullOrEmpty(blockName) ? "Farm Plot" : blockName;

        public string DataCategory => "Life Support";

        public string GetDisplayData()
        {
            string crop = "-";
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == null) continue;
                if (cells[i].crop != null) { crop = cells[i].crop.cropName; break; }
                if (cells[i].tree) { crop = "Timber"; break; }
            }

            return $"{PlotKind} FARM\n{Status}\n{crop} {ReadyCount}/{cells.Length} ready\n"
                 + $"Water {waterStored:0}/{waterCapacity:0} L {(HasWaterSupply ? "(piped)" : "(dry)")}\n"
                 + $"Air {(HasAir ? "OK" : "NONE")} · {Growth01 * 100f:0}%";
        }
    }
}
