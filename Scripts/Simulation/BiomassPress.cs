// Assets/Scripts/Simulation/BiomassPress.cs
//
// Industrial Biomass Press & Bio-Refinery (2x2x2 footprint).
// Links crop farming and automated forestry into industry and power:
//   - Presses raw crops (Wheat, Corn, Carrot, Potato, Berries, Pumpkin, Seeds)
//     and timber (Wood Logs, Wood Chips, Tree Saplings) into Biomass,
//     solid Biofuel Briquettes, and liquid Biofuel (LiquidType.Biofuel).
//   - Features a 1000 L Water input tank and a 1500 L Biofuel output tank
//     with automatic WaterPipe / Biofuel tank transfer and LiquidCanister support.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Farming;
using VoxelEngine.Fluids;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.Simulation
{
    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PowerConsumer))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class BiomassPress : MonoBehaviour, IMachine, IItemConsumer, IItemProvider, IItemPortHost, IFluidStore, IMachineProcessState
    {
        public const int INPUT_SLOTS = 3;
        public const int OUTPUT_SLOTS = 4;
        public const int UPGRADE_SLOTS = 2;

        [Header("Recipes")]
        public List<ProcessingRecipe> knownRecipes = new();
        public ProcessingRecipe selectedRecipe;

        [Header("Containers")]
        public ItemContainer inputC;
        public ItemContainer outputC;
        public ItemContainer upgradeC;

        [Header("Fluid Tanks")]
        public MachineFluidTank waterTank = new MachineFluidTank("Water In", 1000f, LiquidType.Water, autoType: false);
        public MachineFluidTank biofuelTank = new MachineFluidTank("Biofuel Out", 1500f, LiquidType.Biofuel, autoType: false);

        [Header("Tuning")]
        public float baseWattsPerSecond = 220f;
        public float idleWattsPerSecond = 10f;
        public float pipedFluidTransferLps = 50f;
        public bool autoPushOutputs = true;

        [Header("Fallback Item References (auto-resolved if null)")]
        public ItemDefinition biomassItem;
        public ItemDefinition biofuelBriquetteItem;
        public ItemDefinition woodChipsItem;

        // Runtime state
        private bool _userEnabled = true;
        private ProcessingRecipe _currentRecipe;
        private int _fallbackMode; // 0=none, 1=Crop->Biomass+Biofuel, 2=Log->Chips+Biomass+Biofuel, 3=Biomass->Briquette+Biofuel, 4=Chips->Biofuel+Biomass
        private float _progressSeconds;
        private float _batchDuration = 4.0f;
        private float _exportTimer;
        private int _totalBatchesCompleted;
        private float _totalBiofuelLitresProduced;

        private PowerConsumer _power;
        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;
        private MachineFluidTank[] _fluidTanks;

        // Visual animation transforms
        private Transform _pressRam;
        private Transform _expellerWheel;

        private static readonly Collider[] OverlapBuf = new Collider[40];
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // ── IMachine ────────────────────────────────────────────────────────────
        public string MachineName => "Biomass Press";
        public bool IsOnline => _power == null || _power.IsPowered;
        public bool IsActive => _userEnabled && IsOnline && (_currentRecipe != null || _fallbackMode != 0);
        public float Progress01 => (_currentRecipe != null || _fallbackMode != 0) && _batchDuration > 0.01f
            ? Mathf.Clamp01(_progressSeconds / EffectiveBatchTime(_batchDuration))
            : 0f;
        public float CurrentWattage { get; private set; }
        public float SpeedMultiplier { get; set; } = 1f;
        public float EfficiencyMultiplier { get; set; } = 1f;
        public bool UserEnabled { get => _userEnabled; set => _userEnabled = value; }
        public ItemContainer PrimaryContainer => outputC;

        public ProcessingRecipe CurrentRecipe => _currentRecipe;
        public int TotalBatchesCompleted => _totalBatchesCompleted;
        public float TotalBiofuelLitresProduced => _totalBiofuelLitresProduced;
        public string ActiveModeLabel => ActiveProcessLabel;

        public void SetUserEnabled(bool enabled) => _userEnabled = enabled;
        public void SelectRecipe(ProcessingRecipe recipe) => selectedRecipe = recipe;

        public string ActiveProcessLabel
        {
            get
            {
                if (_currentRecipe != null) return _currentRecipe.GetDisplayName();
                return _fallbackMode switch
                {
                    1 => "Press Crop Biomass & Biofuel",
                    2 => "Chip & Press Timber",
                    3 => "Refine Biofuel & Briquette",
                    4 => "Extract Wood-Chip Biofuel",
                    _ => selectedRecipe != null ? selectedRecipe.GetDisplayName() : "Auto (Any Crop / Timber / Biomass)"
                };
            }
        }

        public void EnsureContainers()
        {
            if (inputC == null) inputC = new ItemContainer("Organic Inputs", INPUT_SLOTS);
            else if (inputC.Size != INPUT_SLOTS) inputC.Resize(INPUT_SLOTS);

            if (outputC == null) outputC = new ItemContainer("Pressed Outputs", OUTPUT_SLOTS);
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
                waterTank = new MachineFluidTank("Water In", 1000f, LiquidType.Water, autoType: false);
            else
            {
                waterTank.label = "Water In";
                waterTank.liquid = LiquidType.Water;
                waterTank.autoType = false;
                if (waterTank.capacity <= 0f) waterTank.capacity = 1000f;
            }

            if (biofuelTank == null)
                biofuelTank = new MachineFluidTank("Biofuel Out", 1500f, LiquidType.Biofuel, autoType: false);
            else
            {
                biofuelTank.label = "Biofuel Out";
                biofuelTank.liquid = LiquidType.Biofuel;
                biofuelTank.autoType = false;
                if (biofuelTank.capacity <= 0f) biofuelTank.capacity = 1500f;
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
            _power.connectRadius = 1.9f;
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

            // Exchange fluids with adjacent WaterPipe / WaterTank networks.
            ExchangeFluidsWithNetwork(dt);

            if (!IsOnline)
            {
                CurrentWattage = baseWattsPerSecond * EfficiencyMultiplier;
                if (_power != null) _power.wattsPerSecond = CurrentWattage;
                return;
            }

            // Validate or pick a recipe / fallback mode.
            if (_currentRecipe != null && !CanRunRecipe(_currentRecipe))
            {
                _currentRecipe = null;
                _progressSeconds = 0f;
            }
            if (_fallbackMode != 0 && !CanRunFallbackMode(_fallbackMode))
            {
                _fallbackMode = 0;
                _progressSeconds = 0f;
            }

            if (_currentRecipe == null && _fallbackMode == 0)
            {
                if (selectedRecipe != null)
                {
                    if (CanRunRecipe(selectedRecipe))
                    {
                        _currentRecipe = selectedRecipe;
                        _batchDuration = Mathf.Max(0.5f, selectedRecipe.secondsPerBatch);
                    }
                }
                else
                {
                    for (int i = 0; i < knownRecipes.Count; i++)
                    {
                        var r = knownRecipes[i];
                        if (r != null && CanRunRecipe(r))
                        {
                            _currentRecipe = r;
                            _batchDuration = Mathf.Max(0.5f, r.secondsPerBatch);
                            break;
                        }
                    }

                    if (_currentRecipe == null)
                    {
                        _fallbackMode = DetectFallbackMode(out _batchDuration);
                    }
                }
            }

            if (_currentRecipe == null && _fallbackMode == 0)
            {
                CurrentWattage = idleWattsPerSecond;
                if (_power != null) _power.wattsPerSecond = CurrentWattage;
                _progressSeconds = 0f;
                if (autoPushOutputs && !outputC.IsEmpty()) PushOutputsToNeighbors(dt);
                return;
            }

            float mult = _currentRecipe != null ? Mathf.Max(0.25f, _currentRecipe.powerDrawMultiplier) : 1f;
            CurrentWattage = baseWattsPerSecond * mult * EfficiencyMultiplier;
            if (_power != null) _power.wattsPerSecond = CurrentWattage;

            _progressSeconds += dt;
            float targetTime = EffectiveBatchTime(_batchDuration);
            if (_progressSeconds >= targetTime)
            {
                _progressSeconds = 0f;
                if (_currentRecipe != null)
                    CompleteRecipeBatch(_currentRecipe);
                else if (_fallbackMode != 0)
                    CompleteFallbackBatch(_fallbackMode);

                _currentRecipe = null;
                _fallbackMode = 0;
            }

            if (autoPushOutputs && !outputC.IsEmpty())
                PushOutputsToNeighbors(dt);
        }

        public float EffectiveBatchTime(float baseSec)
            => Mathf.Max(0.35f, baseSec / Mathf.Max(0.1f, SpeedMultiplier));

        private bool CanRunRecipe(ProcessingRecipe r)
        {
            if (r == null) return false;

            if (r.inputs != null)
            {
                for (int i = 0; i < r.inputs.Length; i++)
                {
                    var req = r.inputs[i];
                    if (req.item == null || req.count <= 0) continue;
                    if (inputC.Count(req.item) < req.count) return false;
                }
            }

            if (r.fluidInputs != null)
            {
                for (int i = 0; i < r.fluidInputs.Length; i++)
                {
                    var req = r.fluidInputs[i];
                    if (req.litres <= 0f) continue;
                    if (req.liquid == LiquidType.Water && waterTank.stored + 0.01f < req.litres)
                        return false;
                }
            }

            if (r.outputs != null)
            {
                for (int i = 0; i < r.outputs.Length; i++)
                {
                    var prod = r.outputs[i];
                    if (prod.item == null || prod.count <= 0) continue;
                    if (!outputC.HasSpace(prod.item, prod.count)) return false;
                }
            }

            if (r.fluidOutputs != null)
            {
                for (int i = 0; i < r.fluidOutputs.Length; i++)
                {
                    var prod = r.fluidOutputs[i];
                    if (prod.litres <= 0f) continue;
                    if (prod.liquid == LiquidType.Biofuel && biofuelTank.SpaceFor(LiquidType.Biofuel) + 0.01f < prod.litres)
                        return false;
                }
            }

            return true;
        }

        private void CompleteRecipeBatch(ProcessingRecipe r)
        {
            if (!CanRunRecipe(r)) return;

            if (r.inputs != null)
            {
                for (int i = 0; i < r.inputs.Length; i++)
                {
                    var req = r.inputs[i];
                    if (req.item != null && req.count > 0)
                        inputC.Remove(req.item, req.count);
                }
            }

            if (r.fluidInputs != null)
            {
                for (int i = 0; i < r.fluidInputs.Length; i++)
                {
                    var req = r.fluidInputs[i];
                    if (req.liquid == LiquidType.Water && req.litres > 0f)
                        waterTank.Remove(req.litres);
                }
            }

            if (r.outputs != null)
            {
                for (int i = 0; i < r.outputs.Length; i++)
                {
                    var prod = r.outputs[i];
                    if (prod.item != null && prod.count > 0)
                        outputC.Insert(new ItemStack(prod.item, prod.count));
                }
            }

            if (r.fluidOutputs != null)
            {
                for (int i = 0; i < r.fluidOutputs.Length; i++)
                {
                    var prod = r.fluidOutputs[i];
                    if (prod.liquid == LiquidType.Biofuel && prod.litres > 0f)
                    {
                        float added = biofuelTank.Add(LiquidType.Biofuel, prod.litres);
                        _totalBiofuelLitresProduced += added;
                    }
                }
            }

            _totalBatchesCompleted++;
        }

        // ── Built-in Universal Fallback Modes (handles any crop, seed, log, wood chips, or biomass) ──
        private int DetectFallbackMode(out float duration)
        {
            duration = 4.0f;
            if (CanRunFallbackMode(3)) { duration = 4.0f; return 3; } // Biomass -> Biofuel + Briquette
            if (CanRunFallbackMode(4)) { duration = 4.0f; return 4; } // Wood Chips -> Biofuel + Biomass
            if (CanRunFallbackMode(1)) { duration = 3.5f; return 1; } // Any Crop/Seed -> Biomass + Biofuel
            if (CanRunFallbackMode(2)) { duration = 4.0f; return 2; } // Wood Log -> Wood Chips + Biomass + Biofuel
            return 0;
        }

        private bool CanRunFallbackMode(int mode)
        {
            ResolveDefaultItems();
            switch (mode)
            {
                case 1: // 2x Any Crop / Food / Seed -> 3x Biomass + 20 L Biofuel
                {
                    var cropItem = FindInputByPredicate(IsCropOrSeedItem, 2);
                    if (cropItem == null) return false;
                    if (biomassItem != null && !outputC.HasSpace(biomassItem, 3)) return false;
                    return biofuelTank.SpaceFor(LiquidType.Biofuel) >= 20f;
                }
                case 2: // 1x Wood Log -> 2x Wood Chips + 2x Biomass + 15 L Biofuel
                {
                    var log = FindInputByPredicate(IsWoodLogItem, 1);
                    if (log == null) return false;
                    if (woodChipsItem != null && !outputC.HasSpace(woodChipsItem, 2)) return false;
                    if (biomassItem != null && !outputC.HasSpace(biomassItem, 2)) return false;
                    return biofuelTank.SpaceFor(LiquidType.Biofuel) >= 15f;
                }
                case 3: // 2x Biomass (+ 10 L Water if available) -> 40 L Biofuel + 1x Biofuel Briquette
                {
                    var bio = FindInputByPredicate(IsBiomassItem, 2);
                    if (bio == null) return false;
                    if (biofuelBriquetteItem != null && !outputC.HasSpace(biofuelBriquetteItem, 1)) return false;
                    return biofuelTank.SpaceFor(LiquidType.Biofuel) >= 30f;
                }
                case 4: // 2x Wood Chips -> 30 L Biofuel + 1x Biomass
                {
                    var chips = FindInputByPredicate(IsWoodChipsItem, 2);
                    if (chips == null) return false;
                    if (biomassItem != null && !outputC.HasSpace(biomassItem, 1)) return false;
                    return biofuelTank.SpaceFor(LiquidType.Biofuel) >= 30f;
                }
                default:
                    return false;
            }
        }

        private void CompleteFallbackBatch(int mode)
        {
            if (!CanRunFallbackMode(mode)) return;
            switch (mode)
            {
                case 1:
                {
                    var cropItem = FindInputByPredicate(IsCropOrSeedItem, 2);
                    if (cropItem == null) return;
                    inputC.Remove(cropItem, 2);
                    if (biomassItem != null) outputC.Insert(new ItemStack(biomassItem, 3));
                    _totalBiofuelLitresProduced += biofuelTank.Add(LiquidType.Biofuel, 20f);
                    _totalBatchesCompleted++;
                    break;
                }
                case 2:
                {
                    var log = FindInputByPredicate(IsWoodLogItem, 1);
                    if (log == null) return;
                    inputC.Remove(log, 1);
                    if (woodChipsItem != null) outputC.Insert(new ItemStack(woodChipsItem, 2));
                    if (biomassItem != null) outputC.Insert(new ItemStack(biomassItem, 2));
                    _totalBiofuelLitresProduced += biofuelTank.Add(LiquidType.Biofuel, 15f);
                    _totalBatchesCompleted++;
                    break;
                }
                case 3:
                {
                    var bio = FindInputByPredicate(IsBiomassItem, 2);
                    if (bio == null) return;
                    inputC.Remove(bio, 2);
                    bool usedWater = waterTank.stored >= 10f;
                    if (usedWater) waterTank.Remove(10f);
                    float fuelYield = usedWater ? 40f : 28f;
                    if (biofuelBriquetteItem != null) outputC.Insert(new ItemStack(biofuelBriquetteItem, 1));
                    _totalBiofuelLitresProduced += biofuelTank.Add(LiquidType.Biofuel, fuelYield);
                    _totalBatchesCompleted++;
                    break;
                }
                case 4:
                {
                    var chips = FindInputByPredicate(IsWoodChipsItem, 2);
                    if (chips == null) return;
                    inputC.Remove(chips, 2);
                    bool usedWater = waterTank.stored >= 8f;
                    if (usedWater) waterTank.Remove(8f);
                    float fuelYield = usedWater ? 35f : 24f;
                    if (biomassItem != null) outputC.Insert(new ItemStack(biomassItem, 1));
                    _totalBiofuelLitresProduced += biofuelTank.Add(LiquidType.Biofuel, fuelYield);
                    _totalBatchesCompleted++;
                    break;
                }
            }
        }

        private ItemDefinition FindInputByPredicate(System.Func<ItemDefinition, bool> pred, int minCount)
        {
            for (int i = 0; i < inputC.Size; i++)
            {
                var st = inputC.GetSlot(i);
                if (st == null || st.IsEmpty || st.item == null) continue;
                if (pred(st.item) && inputC.Count(st.item) >= minCount)
                    return st.item;
            }
            return null;
        }

        public static bool IsCropOrSeedItem(ItemDefinition item)
        {
            if (item == null) return false;
            if (item is SeedItem) return true;
            if (item is FoodItem) return true;
            string id = item.itemId?.ToLowerInvariant() ?? "";
            return id.Contains("wheat") || id.Contains("corn") || id.Contains("carrot")
                || id.Contains("potato") || id.Contains("berry") || id.Contains("pumpkin")
                || id.Contains("seed") || id.Contains("sapling");
        }

        public static bool IsWoodLogItem(ItemDefinition item)
        {
            if (item == null) return false;
            string id = item.itemId?.ToLowerInvariant() ?? "";
            return id == "item_wood_log" || id == "wood_log" || item.displayName == "Wood Log";
        }

        public static bool IsWoodChipsItem(ItemDefinition item)
        {
            if (item == null) return false;
            string id = item.itemId?.ToLowerInvariant() ?? "";
            return id.Contains("wood_chips") || item.displayName == "Wood Chips";
        }

        public static bool IsBiomassItem(ItemDefinition item)
        {
            if (item == null) return false;
            string id = item.itemId?.ToLowerInvariant() ?? "";
            return id == "item_biomass" || id == "biomass" || item.displayName == "Biomass";
        }

        private void ExchangeFluidsWithNetwork(float dt)
        {
            float maxStep = pipedFluidTransferLps * dt;
            int hits = Physics.OverlapSphereNonAlloc(transform.position, 2.8f, OverlapBuf, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits; i++)
            {
                var col = OverlapBuf[i];
                OverlapBuf[i] = null;
                if (col == null || col.transform.IsChildOf(transform)) continue;

                var node = col.GetComponentInParent<FluidNode>();
                if (node == null || node.network == null) continue;

                foreach (var n in node.network.nodes)
                {
                    if (n is WaterTank wt)
                    {
                        // Pull Water into waterTank
                        if (wt.liquidType == LiquidType.Water && wt.water > 0.1f && waterTank.SpaceFor(LiquidType.Water) > 0.01f)
                        {
                            float want = Mathf.Min(waterTank.SpaceFor(LiquidType.Water), maxStep);
                            float got = wt.TakeSome(want);
                            if (got > 0f) waterTank.Add(LiquidType.Water, got);
                        }
                        // Push Biofuel into WaterTanks configured for Biofuel (or empty tanks)
                        else if (biofuelTank.stored > 0.1f && (wt.liquidType == LiquidType.Biofuel || wt.water <= 0.01f))
                        {
                            if (wt.water <= 0.01f) wt.liquidType = LiquidType.Biofuel;
                            float space = Mathf.Max(0f, wt.capacity - wt.water);
                            float push = Mathf.Min(biofuelTank.stored, Mathf.Min(space, maxStep));
                            if (push > 0f)
                            {
                                wt.water += push;
                                biofuelTank.Remove(push);
                            }
                        }
                    }
                }
            }
        }

        private void PushOutputsToNeighbors(float dt)
        {
            _exportTimer += dt;
            if (_exportTimer < 0.20f) return;
            _exportTimer = 0f;

            int hits = Physics.OverlapBoxNonAlloc(
                transform.position + new Vector3(0f, 0.5f, 0f),
                new Vector3(1.55f, 1.15f, 1.55f),
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
                        if (upg.powerMultiplier > 0f) eff *= upg.powerMultiplier;
                    }
                }
            }
            SpeedMultiplier = Mathf.Clamp(speed, 0.25f, 5f);
            EfficiencyMultiplier = Mathf.Clamp(eff, 0.2f, 3f);
        }

        public void ResolveDefaultItems()
        {
            if (biomassItem != null && biofuelBriquetteItem != null && woodChipsItem != null) return;
            var all = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            for (int i = 0; i < all.Length; i++)
            {
                var it = all[i];
                if (it == null) continue;
                string id = it.itemId ?? "";
                if (biomassItem == null && (id == "item_biomass" || id == "biomass" || it.displayName == "Biomass"))
                    biomassItem = it;
                else if (biofuelBriquetteItem == null && (id == "item_biofuel_briquette" || id == "biofuel_briquette" || it.displayName == "Biofuel Briquette"))
                    biofuelBriquetteItem = it;
                else if (woodChipsItem == null && (id == "item_wood_chips" || id == "wood_chips" || it.displayName == "Wood Chips"))
                    woodChipsItem = it;
            }
        }

        // ── IItemConsumer & IItemProvider ───────────────────────────────────────
        public int GetInputCapacity(ItemDefinition item)
        {
            if (!_userEnabled || item == null) return 0;
            EnsureContainers();
            if (!IsCropOrSeedItem(item) && !IsWoodLogItem(item) && !IsWoodChipsItem(item) && !IsBiomassItem(item))
                return 0;
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
            _portContainers[0] = new ItemPortContainer("Organic Inputs", inputC, canInput: true, canOutput: false);
            _portContainers[1] = new ItemPortContainer("Pressed Outputs", outputC, canInput: false, canOutput: true);
            return _portContainers;
        }

        // ── IFluidStore ─────────────────────────────────────────────────────────
        public IReadOnlyList<MachineFluidTank> FluidTanks
        {
            get
            {
                EnsureTanks();
                if (_fluidTanks == null || _fluidTanks.Length != 2)
                    _fluidTanks = new[] { waterTank, biofuelTank };
                else
                {
                    _fluidTanks[0] = waterTank;
                    _fluidTanks[1] = biofuelTank;
                }
                return _fluidTanks;
            }
        }

        public float Available(LiquidType type)
        {
            EnsureTanks();
            if (type == LiquidType.Biofuel) return biofuelTank.stored;
            if (type == LiquidType.Water) return waterTank.stored;
            return 0f;
        }

        public float SpaceFor(LiquidType type)
        {
            EnsureTanks();
            if (type == LiquidType.Water) return waterTank.SpaceFor(LiquidType.Water);
            if (type == LiquidType.Biofuel) return biofuelTank.SpaceFor(LiquidType.Biofuel);
            return 0f;
        }

        public float Draw(LiquidType type, float maxLitres)
        {
            EnsureTanks();
            if (maxLitres <= 0f) return 0f;
            if (type == LiquidType.Biofuel) return biofuelTank.Remove(maxLitres);
            if (type == LiquidType.Water) return waterTank.Remove(maxLitres);
            return 0f;
        }

        public float Fill(LiquidType type, float litres)
        {
            EnsureTanks();
            if (litres <= 0f) return 0f;
            if (type == LiquidType.Water) return waterTank.Add(LiquidType.Water, litres);
            if (type == LiquidType.Biofuel) return biofuelTank.Add(LiquidType.Biofuel, litres);
            return 0f;
        }

        // ── Persistence (IMachineProcessState) ──────────────────────────────────
        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureContainers();
            EnsureTanks();
            state.recipeName = MachineProcessPersistence.NameOf(_currentRecipe);
            state.selectedRecipeName = MachineProcessPersistence.NameOf(selectedRecipe);
            state.progressSeconds = Mathf.Max(0f, _progressSeconds);
            state.SetExtra("userEnabled", _userEnabled ? 1f : 0f);
            state.SetExtra("fallbackMode", _fallbackMode);
            state.SetExtra("totalBatches", _totalBatchesCompleted);
            state.SetExtra("totalBiofuelL", _totalBiofuelLitresProduced);
            MachineProcessPersistence.CaptureTanks(state, FluidTanks);
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureContainers();
            EnsureTanks();
            MachineProcessPersistence.RestoreTanks(state, FluidTanks);
            selectedRecipe = MachineProcessPersistence.Resolve(knownRecipes, state.selectedRecipeName, nameof(BiomassPress));
            _currentRecipe = MachineProcessPersistence.Resolve(knownRecipes, state.recipeName, nameof(BiomassPress));
            _fallbackMode = Mathf.Clamp(Mathf.RoundToInt(state.GetExtra("fallbackMode", 0f)), 0, 4);
            _userEnabled = state.GetExtra("userEnabled", 1f) >= 0.5f;
            _totalBatchesCompleted = Mathf.Max(0, Mathf.RoundToInt(state.GetExtra("totalBatches", 0f)));
            _totalBiofuelLitresProduced = Mathf.Max(0f, state.GetExtra("totalBiofuelL", 0f));
            _progressSeconds = Mathf.Max(0f, state.progressSeconds);
        }

        // ── Procedural 2x2x2 Screw-Press & Biofuel Column Visuals ───────────────
        public void RebuildVisuals()
        {
            var existing = transform.Find("Generated_BiomassPressVisuals");
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

            var existing = transform.Find("Generated_BiomassPressVisuals");
            if (existing != null)
            {
                _pressRam = existing.Find("PressRam");
                _expellerWheel = existing.Find("ExpellerWheel");
                return;
            }

            var root = new GameObject("Generated_BiomassPressVisuals").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            Color hullSteel = new Color(0.20f, 0.24f, 0.26f);
            Color pressGreen = new Color(0.24f, 0.52f, 0.28f);
            Color brassCol = new Color(0.82f, 0.62f, 0.22f);
            Color biofuelGlow = new Color(0.52f, 0.88f, 0.22f);
            Color waterBlue = new Color(0.20f, 0.58f, 0.90f);

            // Base plinth (2m x 2m footprint)
            MakePart(root, PrimitiveType.Cube, "BasePlinth", new Vector3(0f, -0.78f, 0f), new Vector3(1.88f, 0.36f, 1.88f), Vector3.zero, hullSteel);

            // Main compression chamber & top feed hopper
            MakePart(root, PrimitiveType.Cube, "PressBody", new Vector3(-0.22f, -0.10f, 0f), new Vector3(1.18f, 1.02f, 1.36f), Vector3.zero, pressGreen);
            MakePart(root, PrimitiveType.Cube, "FeedHopper", new Vector3(-0.22f, 0.56f, -0.28f), new Vector3(0.76f, 0.36f, 0.68f), Vector3.zero, brassCol);

            // Overhead press arch & animated hydraulic ram
            MakePart(root, PrimitiveType.Cube, "PressArch_L", new Vector3(-0.68f, 0.42f, 0.16f), new Vector3(0.16f, 0.96f, 0.22f), Vector3.zero, hullSteel);
            MakePart(root, PrimitiveType.Cube, "PressArch_R", new Vector3(0.24f, 0.42f, 0.16f), new Vector3(0.16f, 0.96f, 0.22f), Vector3.zero, hullSteel);
            MakePart(root, PrimitiveType.Cube, "PressArch_Top", new Vector3(-0.22f, 0.86f, 0.16f), new Vector3(1.10f, 0.18f, 0.28f), Vector3.zero, hullSteel);

            var ramGo = MakePart(root, PrimitiveType.Cylinder, "PressRam", new Vector3(-0.22f, 0.52f, 0.16f), new Vector3(0.36f, 0.34f, 0.36f), Vector3.zero, brassCol);
            _pressRam = ramGo.transform;

            // Side Biofuel Clarification Column & Sight Glass
            MakePart(root, PrimitiveType.Cylinder, "BiofuelColumn", new Vector3(0.58f, 0.04f, 0.28f), new Vector3(0.52f, 0.68f, 0.52f), Vector3.zero, hullSteel);
            MakePart(root, PrimitiveType.Cylinder, "BiofuelSightGlass", new Vector3(0.58f, 0.04f, 0.56f), new Vector3(0.18f, 0.48f, 0.08f), Vector3.zero, biofuelGlow);
            MakePart(root, PrimitiveType.Cylinder, "WaterInletDrum", new Vector3(0.58f, -0.22f, -0.42f), new Vector3(0.46f, 0.42f, 0.46f), Vector3.zero, waterBlue);

            // Rotating Expeller Handwheel on front face
            var wheelGo = MakePart(root, PrimitiveType.Cylinder, "ExpellerWheel", new Vector3(-0.22f, -0.08f, 0.72f), new Vector3(0.54f, 0.05f, 0.54f), new Vector3(90f, 0f, 0f), brassCol);
            _expellerWheel = wheelGo.transform;
        }

        private void UpdateVisualAnimation(float dt)
        {
            if (_pressRam == null)
            {
                var vis = transform.Find("Generated_BiomassPressVisuals");
                if (vis != null)
                {
                    _pressRam = vis.Find("PressRam");
                    _expellerWheel = vis.Find("ExpellerWheel");
                }
            }

            if (IsActive)
            {
                float stroke = Mathf.Sin(Progress01 * Mathf.PI);
                if (_pressRam != null)
                    _pressRam.localPosition = new Vector3(-0.22f, Mathf.Lerp(0.56f, 0.26f, stroke), 0.16f);
                if (_expellerWheel != null)
                    _expellerWheel.Rotate(0f, 180f * dt, 0f, Space.Self);
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
