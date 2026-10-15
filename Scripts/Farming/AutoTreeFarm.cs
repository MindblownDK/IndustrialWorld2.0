// Assets/Scripts/Farming/AutoTreeFarm.cs
//
// Automated Tree Farm (4x3x4 industrial arboricultural plot + gantry harvester).
// Features a wide 3.2m x 3.2m central irrigated soil basin where a full-size
// timber tree grows inside an open-top gantry frame, an animated travelling
// saw-carriage, an internal 500 L irrigation water tank, optional Biomass
// fertilizer boost, and automatic harvesting of both its own central tree and
// any standalone TreeFarmPlots within 10 m.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Fluids;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;

namespace VoxelEngine.Farming
{
    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PowerConsumer))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class AutoTreeFarm : MonoBehaviour, IMachine, IItemProvider, IItemConsumer, IItemPortHost, IFluidStore, IMachineProcessState
    {
        public const int INPUT_SLOTS = 4;
        public const int OUTPUT_SLOTS = 8;
        public const int UPGRADE_SLOTS = 2;

        [Header("Containers")]
        public ItemContainer inputC;
        public ItemContainer outputC;
        public ItemContainer upgradeC;

        [Header("Irrigation Water Tank")]
        public MachineFluidTank waterTank = new MachineFluidTank("Irrigation Water", 500f, LiquidType.Water, autoType: false);
        public float waterConsumptionLps = 0.35f;
        public float pipedWaterRefillLps = 40f;

        [Header("Forestry Tuning")]
        public float baseGrowthSeconds = 75f;
        public float irrigatedSpeedMultiplier = 1.85f;
        public float fertilizedSpeedMultiplier = 1.45f;
        public float baseWattsPerSecond = 140f;
        public float idleWattsPerSecond = 8f;
        public float externalPlotScanRadius = 10f;
        public bool autoFertilizeFromInput = true;
        public bool autoHarvestNearbyTreePlots = true;
        public bool autoPushOutputs = true;

        [Header("Yield Per Cycle")]
        public int logsPerCycle = 8;
        public int biomassPerCycle = 3;
        public int woodChipsPerCycle = 2;
        public int surplusSaplingsPerCycle = 1;

        [Header("Product References (auto-resolved if null)")]
        public ItemDefinition woodLogItem;
        public ItemDefinition saplingItem;
        public ItemDefinition biomassItem;
        public ItemDefinition woodChipsItem;

        // Runtime state
        private bool _userEnabled = true;
        private float _growth01;
        private bool _isFertilized;
        private int _totalTreesHarvested;
        private float _externalScanTimer;
        private float _exportTimer;
        private PowerConsumer _power;
        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;
        private MachineFluidTank[] _fluidTanks;

        // Visual animation transforms
        private Transform _treeRoot;
        private Transform _sawCarriage;
        private Transform _sawBlade;

        private static readonly Collider[] OverlapBuf = new Collider[48];
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // ── IMachine ────────────────────────────────────────────────────────────
        public string MachineName => "Automated Tree Farm";
        public bool IsOnline => _power == null || _power.IsPowered;
        public bool IsActive => _userEnabled && IsOnline && !IsOutputFull();
        public float Progress01 => Mathf.Clamp01(_growth01);
        public float CurrentWattage { get; private set; }
        public float SpeedMultiplier { get; set; } = 1f;
        public float EfficiencyMultiplier { get; set; } = 1f;
        public bool UserEnabled { get => _userEnabled; set => _userEnabled = value; }
        public ItemContainer PrimaryContainer => outputC;

        public bool IsIrrigated => waterTank != null && waterTank.stored >= 0.5f;
        public bool IsFertilized => _isFertilized;
        public int TotalTreesHarvested => _totalTreesHarvested;
        public float GrowthProgress01 => Progress01;
        public float EffectiveCycleSeconds => EffectiveGrowthSeconds;

        public bool harvestNearbyTreePlots
        {
            get => autoHarvestNearbyTreePlots;
            set => autoHarvestNearbyTreePlots = value;
        }

        public float baseGrowCycleSeconds
        {
            get => baseGrowthSeconds;
            set => baseGrowthSeconds = value;
        }

        public void SetUserEnabled(bool enabled) => _userEnabled = enabled;

        public string GrowthStageLabel
        {
            get
            {
                if (_growth01 < 0.25f) return "Sapling";
                if (_growth01 < 0.60f) return "Young Timber";
                if (_growth01 < 0.92f) return "Canopy Maturing";
                return "Felling & Harvesting";
            }
        }

        public float EffectiveGrowthSeconds
        {
            get
            {
                float mult = Mathf.Max(0.1f, SpeedMultiplier);
                if (IsIrrigated) mult *= Mathf.Max(1f, irrigatedSpeedMultiplier);
                if (_isFertilized) mult *= Mathf.Max(1f, fertilizedSpeedMultiplier);
                return Mathf.Max(8f, baseGrowthSeconds / mult);
            }
        }

        public string StatusText
        {
            get
            {
                if (!_userEnabled) return "DISABLED";
                if (!IsOnline) return "NO POWER";
                if (IsOutputFull()) return "OUTPUT FULL";
                if (_growth01 >= 0.92f) return "FELLING TIMBER";
                return IsIrrigated ? "GROWING (IRRIGATED)" : "GROWING (DRY)";
            }
        }

        public void EnsureContainers()
        {
            if (inputC == null) inputC = new ItemContainer("Saplings & Fertilizer", INPUT_SLOTS);
            else if (inputC.Size != INPUT_SLOTS) inputC.Resize(INPUT_SLOTS);

            if (outputC == null) outputC = new ItemContainer("Timber & Biomass Output", OUTPUT_SLOTS);
            else if (outputC.Size != OUTPUT_SLOTS) outputC.Resize(OUTPUT_SLOTS);

            if (upgradeC == null)
            {
                upgradeC = new ItemContainer("Upgrades", UPGRADE_SLOTS);
                upgradeC.OnChanged += RecalculateUpgrades;
            }
            else if (upgradeC.Size != UPGRADE_SLOTS)
            {
                upgradeC.Resize(UPGRADE_SLOTS);
            }
        }

        public void EnsureTanks()
        {
            if (waterTank == null)
                waterTank = new MachineFluidTank("Irrigation Water", 500f, LiquidType.Water, autoType: false);
            else
            {
                waterTank.label = "Irrigation Water";
                waterTank.liquid = LiquidType.Water;
                waterTank.autoType = false;
                if (waterTank.capacity <= 0f) waterTank.capacity = 500f;
            }
        }

        private void Awake()
        {
            EnsureContainers();
            EnsureTanks();
            ResolveDefaultItems();
            EnsureVisuals();
            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            _power.connectRadius = 2.4f;
            var ports = GetComponent<PortConfig>();
            if (ports != null) ports.EnsureAllFaces();
            RecalculateUpgrades();
            SimulationTickManager.EnsureInstance();
        }

        private void OnEnable()
        {
            EnsureContainers();
            EnsureTanks();
            ResolveDefaultItems();
            EnsureVisuals();
            SimulationTickManager.Instance?.Register(this, this);
        }

        private void OnDisable()
        {
            SimulationTickManager.Instance?.Unregister(this);
        }

        private void OnDestroy()
        {
            if (upgradeC != null) upgradeC.OnChanged -= RecalculateUpgrades;
        }

        private void Update()
        {
            UpdateVisualAnimation(Time.deltaTime);
        }

        public void SimulationTick(float dt)
        {
            EnsureContainers();
            EnsureTanks();
            ResolveDefaultItems();

            if (!_userEnabled)
            {
                CurrentWattage = 0f;
                if (_power != null) _power.wattsPerSecond = 0f;
                return;
            }

            // Pull irrigation water from adjacent WaterPipe networks.
            PullWaterFromNetwork(dt);

            if (!IsOnline)
            {
                CurrentWattage = baseWattsPerSecond * EfficiencyMultiplier;
                if (_power != null) _power.wattsPerSecond = CurrentWattage;
                return;
            }

            if (IsOutputFull())
            {
                CurrentWattage = idleWattsPerSecond;
                if (_power != null) _power.wattsPerSecond = CurrentWattage;
                if (autoPushOutputs) PushOutputsToNeighbors();
                return;
            }

            CurrentWattage = baseWattsPerSecond * EfficiencyMultiplier;
            if (_power != null) _power.wattsPerSecond = CurrentWattage;

            // Optional fertilizer consumption at cycle start or mid-cycle.
            if (!_isFertilized && autoFertilizeFromInput)
                TryConsumeFertilizer();

            // Sip irrigation water while growing.
            if (waterTank.stored > 0f)
                waterTank.Remove(Mathf.Min(waterTank.stored, waterConsumptionLps * dt));

            float step = dt / EffectiveGrowthSeconds;
            _growth01 += step;

            if (_growth01 >= 1f)
            {
                _growth01 = 0f;
                _isFertilized = false;
                CompleteInternalHarvestCycle();
            }

            // Scan nearby standalone TreeFarmPlots (within 10m) and auto-harvest/replant them.
            if (autoHarvestNearbyTreePlots)
            {
                _externalScanTimer += dt;
                if (_externalScanTimer >= 2.0f)
                {
                    _externalScanTimer = 0f;
                    ScanAndHarvestExternalTreePlots();
                }
            }

            if (autoPushOutputs && !outputC.IsEmpty())
            {
                _exportTimer += dt;
                if (_exportTimer >= 0.25f)
                {
                    _exportTimer = 0f;
                    PushOutputsToNeighbors();
                }
            }
        }

        private void CompleteInternalHarvestCycle()
        {
            ResolveDefaultItems();
            if (woodLogItem != null && logsPerCycle > 0)
                outputC.Insert(new ItemStack(woodLogItem, logsPerCycle));
            if (biomassItem != null && biomassPerCycle > 0)
                outputC.Insert(new ItemStack(biomassItem, biomassPerCycle));
            if (woodChipsItem != null && woodChipsPerCycle > 0)
                outputC.Insert(new ItemStack(woodChipsItem, woodChipsPerCycle));
            if (saplingItem != null && surplusSaplingsPerCycle > 0)
                outputC.Insert(new ItemStack(saplingItem, surplusSaplingsPerCycle));

            _totalTreesHarvested++;
        }

        private void ScanAndHarvestExternalTreePlots()
        {
            var plots = FindObjectsByType<TreeFarmPlot>(FindObjectsInactive.Exclude);
            float rSqr = externalPlotScanRadius * externalPlotScanRadius;
            for (int i = 0; i < plots.Length; i++)
            {
                var plot = plots[i];
                if (plot == null) continue;
                if ((plot.transform.position - transform.position).sqrMagnitude > rSqr) continue;

                // Auto-plant empty plots if we have a sapling or default auto-replant.
                if (!plot.isPlanted)
                {
                    plot.TryPlant();
                    continue;
                }

                // Auto-fertilize external plot if we have spare Biomass in inputC.
                if (autoFertilizeFromInput && !plot.isFertilized && TryConsumeFertilizer())
                {
                    plot.TryApplyFertilizer();
                }

                if (plot.IsMature && !IsOutputFull())
                {
                    if (plot.TryAutomatedHarvest(
                        out var logIt, out int logCnt,
                        out var bioIt, out int bioCnt,
                        out var sapIt, out int sapCnt,
                        autoReplant: true))
                    {
                        if (logIt != null && logCnt > 0) outputC.Insert(new ItemStack(logIt, logCnt));
                        if (bioIt != null && bioCnt > 0) outputC.Insert(new ItemStack(bioIt, bioCnt));
                        if (sapIt != null && sapCnt > 0) outputC.Insert(new ItemStack(sapIt, sapCnt));
                        _totalTreesHarvested++;
                    }
                }
            }
        }

        private bool TryConsumeFertilizer()
        {
            if (inputC == null) return false;
            for (int i = 0; i < inputC.Size; i++)
            {
                var st = inputC.GetSlot(i);
                if (st == null || st.IsEmpty || st.item == null) continue;
                string id = st.item.itemId?.ToLowerInvariant() ?? "";
                if (id.Contains("biomass") || id.Contains("compost") || id.Contains("fertilizer")
                    || st.item.displayName == "Biomass" || st.item.displayName == "Organic Fertilizer")
                {
                    if (inputC.Remove(st.item, 1) > 0)
                    {
                        _isFertilized = true;
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsOutputFull()
        {
            ResolveDefaultItems();
            if (outputC == null) return false;
            if (woodLogItem != null && !outputC.HasSpace(woodLogItem, Mathf.Max(1, logsPerCycle)))
                return true;
            return false;
        }

        private void PullWaterFromNetwork(float dt)
        {
            if (waterTank == null || waterTank.SpaceFor(LiquidType.Water) <= 0.01f) return;
            float want = Mathf.Min(waterTank.SpaceFor(LiquidType.Water), pipedWaterRefillLps * dt);
            int hits = Physics.OverlapSphereNonAlloc(transform.position, 3.6f, OverlapBuf, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits; i++)
            {
                var col = OverlapBuf[i];
                OverlapBuf[i] = null;
                if (col == null || col.transform.IsChildOf(transform)) continue;
                var node = col.GetComponentInParent<FluidNode>();
                if (node == null || node.network == null) continue;
                foreach (var n in node.network.nodes)
                {
                    if (n is WaterTank wt && wt.liquidType == LiquidType.Water && wt.water > 0.1f)
                    {
                        float taken = wt.TakeSome(want);
                        if (taken > 0f)
                        {
                            waterTank.Add(LiquidType.Water, taken);
                            return;
                        }
                    }
                }
            }
        }

        private void PushOutputsToNeighbors()
        {
            if (outputC == null || outputC.IsEmpty()) return;
            int hits = Physics.OverlapBoxNonAlloc(
                transform.position + new Vector3(0f, 0.5f, 0f),
                new Vector3(2.45f, 1.4f, 2.45f),
                OverlapBuf,
                transform.rotation,
                ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < hits; i++)
            {
                var col = OverlapBuf[i];
                OverlapBuf[i] = null;
                if (col == null || col.transform.IsChildOf(transform)) continue;

                var consumer = col.GetComponentInParent<IItemConsumer>();
                if (consumer != null && !ReferenceEquals(consumer, this))
                {
                    for (int s = 0; s < outputC.Size; s++)
                    {
                        var st = outputC.GetSlot(s);
                        if (st == null || st.IsEmpty || st.item == null) continue;
                        int cap = consumer.GetInputCapacity(st.item);
                        if (cap <= 0) continue;
                        int moved = consumer.TryInsert(st.item, Mathf.Min(st.count, Mathf.Min(cap, 8)));
                        if (moved > 0)
                        {
                            st.count -= moved;
                            outputC.SetSlot(s, st.count > 0 ? st : new ItemStack());
                            return;
                        }
                    }
                }
            }
        }

        public void RecalculateUpgrades()
        {
            float speed = 1f;
            float eff = 1f;
            if (upgradeC != null)
            {
                for (int i = 0; i < upgradeC.Size; i++)
                {
                    var st = upgradeC.GetSlot(i);
                    if (st == null || st.IsEmpty || st.item == null) continue;
                    if (st.item is FurnaceUpgradeItem upg)
                    {
                        if (upg.speedMultiplier > 0f) speed *= upg.speedMultiplier;
                        if (upg.efficiencyMultiplier > 0f) eff *= upg.efficiencyMultiplier;
                    }
                }
            }
            SpeedMultiplier = Mathf.Clamp(speed, 0.25f, 5f);
            EfficiencyMultiplier = Mathf.Clamp(eff, 0.2f, 3f);
        }

        public void ResolveDefaultItems()
        {
            if (woodLogItem != null && saplingItem != null && biomassItem != null && woodChipsItem != null) return;
            var all = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            for (int i = 0; i < all.Length; i++)
            {
                var it = all[i];
                if (it == null) continue;
                string id = it.itemId ?? "";
                if (woodLogItem == null && (id == "item_wood_log" || id == "wood_log" || it.displayName == "Wood Log"))
                    woodLogItem = it;
                else if (saplingItem == null && (id == "seed_tree_sapling" || id == "item_tree_sapling" || it.displayName == "Tree Sapling"))
                    saplingItem = it;
                else if (biomassItem == null && (id == "item_biomass" || id == "biomass" || it.displayName == "Biomass"))
                    biomassItem = it;
                else if (woodChipsItem == null && (id == "item_wood_chips" || id == "wood_chips" || it.displayName == "Wood Chips"))
                    woodChipsItem = it;
            }
        }

        // ── IItemConsumer & IItemProvider ───────────────────────────────────────
        public int GetInputCapacity(ItemDefinition item)
        {
            if (!_userEnabled || item == null) return 0;
            EnsureContainers();
            return inputC.HasSpace(item, 1) ? ItemStack.MaxItemsPerStack(item) : 0;
        }

        public int TryInsert(ItemDefinition item, int count)
        {
            if (!_userEnabled || item == null || count <= 0) return 0;
            EnsureContainers();
            var leftover = inputC.Insert(new ItemStack(item, count));
            return count - (leftover?.count ?? 0);
        }

        public ItemDefinition PeekOutput(out int count)
        {
            EnsureContainers();
            for (int i = 0; i < outputC.Size; i++)
            {
                var st = outputC.GetSlot(i);
                if (st != null && !st.IsEmpty && st.item != null)
                {
                    count = st.count;
                    return st.item;
                }
            }
            count = 0;
            return null;
        }

        public int TryExtract(ItemDefinition item, int count)
        {
            EnsureContainers();
            return outputC.Remove(item, count);
        }

        // ── IItemPortHost ───────────────────────────────────────────────────────
        public PortConfig PortConfig
        {
            get
            {
                if (_portConfig == null)
                {
                    _portConfig = GetComponent<PortConfig>();
                    if (_portConfig == null) _portConfig = gameObject.AddComponent<PortConfig>();
                    _portConfig.EnsureAllFaces();
                }
                return _portConfig;
            }
        }

        public IReadOnlyList<ItemPortContainer> GetPortContainers()
        {
            EnsureContainers();
            if (_portContainers == null || _portContainers.Length != 2)
                _portContainers = new ItemPortContainer[2];
            _portContainers[0] = new ItemPortContainer("Saplings & Fertilizer", inputC, canInput: true, canOutput: false);
            _portContainers[1] = new ItemPortContainer("Timber & Biomass Output", outputC, canInput: false, canOutput: true);
            return _portContainers;
        }

        // ── IFluidStore ─────────────────────────────────────────────────────────
        public IReadOnlyList<MachineFluidTank> FluidTanks
        {
            get
            {
                EnsureTanks();
                if (_fluidTanks == null || _fluidTanks.Length != 1)
                    _fluidTanks = new[] { waterTank };
                else
                    _fluidTanks[0] = waterTank;
                return _fluidTanks;
            }
        }

        public float Available(LiquidType type)
        {
            EnsureTanks();
            return type == LiquidType.Water ? waterTank.stored : 0f;
        }

        public float SpaceFor(LiquidType type)
        {
            EnsureTanks();
            return type == LiquidType.Water ? waterTank.SpaceFor(LiquidType.Water) : 0f;
        }

        public float Draw(LiquidType type, float maxLitres)
        {
            EnsureTanks();
            if (type != LiquidType.Water || maxLitres <= 0f) return 0f;
            return waterTank.Remove(maxLitres);
        }

        public float Fill(LiquidType type, float litres)
        {
            EnsureTanks();
            if (type != LiquidType.Water || litres <= 0f) return 0f;
            return waterTank.Add(LiquidType.Water, litres);
        }

        // ── Persistence (IMachineProcessState) ──────────────────────────────────
        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureContainers();
            EnsureTanks();
            state.progressSeconds = _growth01;
            state.SetExtra("userEnabled", _userEnabled ? 1f : 0f);
            state.SetExtra("isFertilized", _isFertilized ? 1f : 0f);
            state.SetExtra("autoFertilize", autoFertilizeFromInput ? 1f : 0f);
            state.SetExtra("autoExternal", autoHarvestNearbyTreePlots ? 1f : 0f);
            state.SetExtra("totalHarvested", _totalTreesHarvested);
            MachineProcessPersistence.CaptureTanks(state, FluidTanks);
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureContainers();
            EnsureTanks();
            _growth01 = Mathf.Clamp01(state.progressSeconds);
            _userEnabled = state.GetExtra("userEnabled", 1f) >= 0.5f;
            _isFertilized = state.GetExtra("isFertilized", 0f) >= 0.5f;
            autoFertilizeFromInput = state.GetExtra("autoFertilize", 1f) >= 0.5f;
            autoHarvestNearbyTreePlots = state.GetExtra("autoExternal", 1f) >= 0.5f;
            _totalTreesHarvested = Mathf.Max(0, Mathf.RoundToInt(state.GetExtra("totalHarvested", 0f)));
            MachineProcessPersistence.RestoreTanks(state, FluidTanks);
        }

        // ── Procedural 4x3x4 Arboricultural Plot, Gantry & Growing Tree ─────────
        public void RebuildVisuals()
        {
            var existing = transform.Find("Generated_AutoTreeFarmVisuals");
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }
            EnsureVisuals();
        }

        public void EnsureVisuals()
        {
            var rootMr = GetComponent<MeshRenderer>();
            if (rootMr != null) rootMr.enabled = false;

            var existing = transform.Find("Generated_AutoTreeFarmVisuals");
            if (existing != null)
            {
                _treeRoot = existing.Find("GrowingTreeRoot");
                _sawCarriage = existing.Find("SawCarriage");
                if (_sawCarriage != null) _sawBlade = _sawCarriage.Find("SawBlade");
                return;
            }

            var root = new GameObject("Generated_AutoTreeFarmVisuals").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            Color frameSteel = new Color(0.20f, 0.23f, 0.27f);
            Color gantrySteel = new Color(0.28f, 0.32f, 0.37f);
            Color richSoil = new Color(0.26f, 0.18f, 0.10f);
            Color hazardYellow = new Color(0.94f, 0.70f, 0.14f);
            Color waterBlue = new Color(0.18f, 0.56f, 0.88f);
            Color trunkWood = new Color(0.40f, 0.26f, 0.14f);
            Color foliageMid = new Color(0.20f, 0.56f, 0.22f);
            Color foliageBright = new Color(0.28f, 0.68f, 0.26f);

            // Wide 4m x 4m base planter foundation (y = -1.45 to -0.95) & spacious 3.2m x 3.2m soil bed
            MakePart(root, PrimitiveType.Cube, "FoundationBase", new Vector3(0f, -1.22f, 0f), new Vector3(3.88f, 0.48f, 3.88f), Vector3.zero, frameSteel);
            MakePart(root, PrimitiveType.Cube, "SoilBasin", new Vector3(0f, -0.94f, 0f), new Vector3(3.24f, 0.18f, 3.24f), Vector3.zero, richSoil);

            // Perimeter planter walls & irrigation header pipes
            MakePart(root, PrimitiveType.Cube, "Curb_N", new Vector3(0f, -0.86f, 1.76f), new Vector3(3.88f, 0.28f, 0.24f), Vector3.zero, gantrySteel);
            MakePart(root, PrimitiveType.Cube, "Curb_S", new Vector3(0f, -0.86f, -1.76f), new Vector3(3.88f, 0.28f, 0.24f), Vector3.zero, gantrySteel);
            MakePart(root, PrimitiveType.Cube, "Curb_E", new Vector3(1.76f, -0.86f, 0f), new Vector3(0.24f, 0.28f, 3.40f), Vector3.zero, gantrySteel);
            MakePart(root, PrimitiveType.Cube, "Curb_W", new Vector3(-1.76f, -0.86f, 0f), new Vector3(0.24f, 0.28f, 3.40f), Vector3.zero, gantrySteel);

            MakePart(root, PrimitiveType.Cylinder, "WaterManifold_E", new Vector3(1.56f, -0.76f, 0f), new Vector3(0.14f, 1.45f, 0.14f), new Vector3(90f, 0f, 0f), waterBlue);
            MakePart(root, PrimitiveType.Cylinder, "WaterManifold_W", new Vector3(-1.56f, -0.76f, 0f), new Vector3(0.14f, 1.45f, 0.14f), new Vector3(90f, 0f, 0f), waterBlue);

            // 4 Open-Top Gantry Columns at the outer 4m x 4m corners (leaves the entire center sky open for the tree!)
            MakePart(root, PrimitiveType.Cube, "Pillar_NE", new Vector3(1.76f, 0.12f, 1.76f), new Vector3(0.26f, 2.20f, 0.26f), Vector3.zero, frameSteel);
            MakePart(root, PrimitiveType.Cube, "Pillar_NW", new Vector3(-1.76f, 0.12f, 1.76f), new Vector3(0.26f, 2.20f, 0.26f), Vector3.zero, frameSteel);
            MakePart(root, PrimitiveType.Cube, "Pillar_SE", new Vector3(1.76f, 0.12f, -1.76f), new Vector3(0.26f, 2.20f, 0.26f), Vector3.zero, frameSteel);
            MakePart(root, PrimitiveType.Cube, "Pillar_SW", new Vector3(-1.76f, 0.12f, -1.76f), new Vector3(0.26f, 2.20f, 0.26f), Vector3.zero, frameSteel);

            // Gantry Perimeter Rails
            MakePart(root, PrimitiveType.Cube, "Rail_East", new Vector3(1.76f, 1.18f, 0f), new Vector3(0.22f, 0.18f, 3.76f), Vector3.zero, gantrySteel);
            MakePart(root, PrimitiveType.Cube, "Rail_West", new Vector3(-1.76f, 1.18f, 0f), new Vector3(0.22f, 0.18f, 3.76f), Vector3.zero, gantrySteel);
            MakePart(root, PrimitiveType.Cube, "Rail_North", new Vector3(0f, 1.18f, 1.76f), new Vector3(3.40f, 0.16f, 0.18f), Vector3.zero, hazardYellow);
            MakePart(root, PrimitiveType.Cube, "Rail_South", new Vector3(0f, 1.18f, -1.76f), new Vector3(3.40f, 0.16f, 0.18f), Vector3.zero, hazardYellow);

            // Travelling Felling Saw-Carriage (moves along Z at trunk base level y = -0.52)
            _sawCarriage = new GameObject("SawCarriage").transform;
            _sawCarriage.SetParent(root, false);
            _sawCarriage.localPosition = new Vector3(0f, -0.52f, -1.25f);
            MakePart(_sawCarriage, PrimitiveType.Cube, "CarriageBeam", new Vector3(0f, 0f, 0f), new Vector3(3.36f, 0.14f, 0.20f), Vector3.zero, hazardYellow);
            MakePart(_sawCarriage, PrimitiveType.Cube, "SawHead", new Vector3(0f, 0.06f, 0.14f), new Vector3(0.48f, 0.18f, 0.34f), Vector3.zero, gantrySteel);
            var bladeGo = MakePart(_sawCarriage, PrimitiveType.Cylinder, "SawBlade", new Vector3(0f, 0.04f, 0.36f), new Vector3(0.68f, 0.025f, 0.68f), Vector3.zero, new Color(0.75f, 0.78f, 0.82f));
            _sawBlade = bladeGo.transform;

            // Central Growing Timber Tree (anchored at soil surface y = -0.85, grows up to 3.6m tall!)
            _treeRoot = new GameObject("GrowingTreeRoot").transform;
            _treeRoot.SetParent(root, false);
            _treeRoot.localPosition = new Vector3(0f, -0.85f, 0f);
            _treeRoot.localRotation = Quaternion.identity;

            MakePart(_treeRoot, PrimitiveType.Cylinder, "Trunk_Lower", new Vector3(0f, 0.90f, 0f), new Vector3(0.46f, 0.90f, 0.46f), Vector3.zero, trunkWood);
            MakePart(_treeRoot, PrimitiveType.Cylinder, "Trunk_Upper", new Vector3(0f, 2.00f, 0f), new Vector3(0.32f, 0.70f, 0.32f), Vector3.zero, trunkWood);
            MakePart(_treeRoot, PrimitiveType.Cylinder, "Branch_L", new Vector3(-0.38f, 1.95f, 0.14f), new Vector3(0.18f, 0.46f, 0.18f), new Vector3(10f, 0f, 36f), trunkWood);
            MakePart(_treeRoot, PrimitiveType.Cylinder, "Branch_R", new Vector3(0.40f, 2.05f, -0.12f), new Vector3(0.18f, 0.46f, 0.18f), new Vector3(-8f, 0f, -34f), trunkWood);

            MakePart(_treeRoot, PrimitiveType.Sphere, "Canopy_Main", new Vector3(0f, 2.85f, 0f), new Vector3(2.35f, 1.65f, 2.35f), Vector3.zero, foliageMid);
            MakePart(_treeRoot, PrimitiveType.Sphere, "Canopy_Crown", new Vector3(0f, 3.58f, 0f), new Vector3(1.60f, 1.18f, 1.60f), Vector3.zero, foliageBright);
            MakePart(_treeRoot, PrimitiveType.Sphere, "Canopy_West", new Vector3(-0.68f, 2.50f, 0.26f), new Vector3(1.38f, 1.12f, 1.38f), Vector3.zero, foliageBright);
            MakePart(_treeRoot, PrimitiveType.Sphere, "Canopy_East", new Vector3(0.70f, 2.58f, -0.22f), new Vector3(1.38f, 1.12f, 1.38f), Vector3.zero, foliageMid);
        }

        private void UpdateVisualAnimation(float dt)
        {
            if (_treeRoot == null)
            {
                var vis = transform.Find("Generated_AutoTreeFarmVisuals");
                if (vis != null)
                {
                    _treeRoot = vis.Find("GrowingTreeRoot");
                    _sawCarriage = vis.Find("SawCarriage");
                    if (_sawCarriage != null) _sawBlade = _sawCarriage.Find("SawBlade");
                }
            }

            if (_treeRoot != null)
            {
                float t = Mathf.Clamp01(_growth01);
                float xz = Mathf.Lerp(0.18f, 1.0f, t);
                float y = Mathf.Lerp(0.16f, 1.0f, t);
                _treeRoot.localScale = new Vector3(xz, y, xz);
            }

            if (_sawCarriage != null)
            {
                // When growth01 > 0.88, the saw carriage advances from z = -1.25 to z = -0.22 to fell the trunk!
                float cutPhase = _growth01 > 0.88f ? Mathf.InverseLerp(0.88f, 1.0f, _growth01) : 0f;
                float zPos = Mathf.Lerp(-1.25f, -0.22f, Mathf.Sin(cutPhase * Mathf.PI));
                _sawCarriage.localPosition = new Vector3(0f, -0.52f, zPos);
            }

            if (_sawBlade != null && IsActive)
            {
                float spinSpeed = _growth01 > 0.85f ? 720f : 140f;
                _sawBlade.Rotate(0f, spinSpeed * dt, 0f, Space.Self);
            }
        }

        private static GameObject MakePart(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Vector3 euler, Color color)
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
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && Application.isPlaying)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor(BaseColorId, color);
                mpb.SetColor(ColorId, color);
                mr.SetPropertyBlock(mpb);
            }
            return go;
        }
    }
}
