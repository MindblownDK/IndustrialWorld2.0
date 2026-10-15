// Assets/Scripts/VoxelEngine/Simulation/OreWasher.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — ORE WASHER / ENRICHMENT MACHINE             ║
// ║  Hydro-rotary trommel and sluice classifier (Section 6.2/4.6.0).║
// ║  Washes crushed ores with water into enriched washed ore and    ║
// ║  mineral tailings, or reprocesses tailings for secondary yield. ║
// ║  Connects to piped water networks, canisters, conveyors, item   ║
// ║  ports, and the soil/water runoff pollution system.             ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Environment;
using VoxelEngine.Fluids;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.Simulation
{
    [RequireComponent(typeof(PowerConsumer))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class OreWasher : MonoBehaviour, IMachine, IItemConsumer, IItemProvider, IItemPortHost, IFluidStore, IMachineProcessState
    {
        public const int INPUT_SLOTS   = 2;
        public const int OUTPUT_SLOTS  = 4;
        public const int UPGRADE_SLOTS = 2;

        // ── Inspector ─────────────────────────────────────────────────

        [Header("Washing Configuration")]
        public List<MachineRecipe> knownRecipes = new();

        [Header("Containers")]
        public ItemContainer inputC;
        public ItemContainer outputC;
        public ItemContainer upgradeC;

        [Header("Wash Water Tank")]
        [Tooltip("Internal wash-water reservoir. Can be filled by adjacent water pipes/tanks/pumps or manually with a liquid canister.")]
        public MachineFluidTank waterTank = new MachineFluidTank("Wash Water", 500f, LiquidType.Water, autoType: false);

        [Tooltip("Litres of water consumed per completed washing batch.")]
        public float waterLitresPerBatch = 20f;

        [Tooltip("Maximum litres per second pulled from an adjacent piped water network into the wash tank.")]
        public float pipedWaterRefillLps = 60f;

        [Header("Tailings & Effluent")]
        [Tooltip("When false (Retain Tailings), mineral tailings are stored in Outputs and the washer pauses if Outputs are full. When true (Sluice Discharge), excess tailings are vented directly as slurry runoff, multiplying soil/water pollution.")]
        public bool sluiceDischargeTailings = false;

        [Header("Power Tuning")]
        public float baseWattsPerSecond = 280f;
        public float idleWattsPerSecond = 8f;

        // ── Runtime ───────────────────────────────────────────────────

        private MachineRecipe _current;
        private MachineRecipe _selectedRecipe;
        private float _processProgress;
        private PowerConsumer _power;
        private PollutionEmitter _pollutionEmitter;
        private bool _userEnabled = true;
        private float _speedMult = 1f;
        private float _effMult = 1f;
        private float _pipeScanTimer;
        private MachineFluidTank[] _tanks;
        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;
        private static readonly Collider[] s_pipeOverlap = new Collider[24];

        // ── IMachine & Telemetry Properties ───────────────────────────

        public string MachineName => "Ore Washer";
        public bool IsActive => _current != null && _processProgress > 0f && !IsLowWater && !IsTailingsBackedUp;
        public bool IsOnline => _power != null && _power.IsPowered;
        public float Progress01 => _current == null ? 0f : Mathf.Clamp01(_processProgress / EffectiveProcessTime(_current));
        public float CurrentWattage { get; private set; }
        public string CurrentRecipeId => _current != null ? _current.name : (_selectedRecipe != null ? _selectedRecipe.name : string.Empty);
        public MachineRecipe CurrentRecipe => _current != null ? _current : _selectedRecipe;
        public float ProcessProgressSeconds => _processProgress;
        public bool UserEnabled
        {
            get => _userEnabled;
            set => _userEnabled = value;
        }

        public IReadOnlyList<MachineFluidTank> FluidTanks
        {
            get
            {
                EnsureTanks();
                if (_tanks == null || _tanks.Length != 1 || _tanks[0] != waterTank)
                    _tanks = new[] { waterTank };
                return _tanks;
            }
        }

        public bool IsWaterPiped { get; private set; }
        public bool IsTailingsBackedUp { get; private set; }
        public float SourceContamination01 { get; private set; }
        public float WaterQuality01 => Mathf.Clamp01(1f - SourceContamination01);
        public float SpeedMultiplier => _speedMult;
        public float EfficiencyMultiplier => _effMult;

        public bool IsLowWater
        {
            get
            {
                EnsureTanks();
                float needed = EffectiveWaterPerBatch(_current ?? _selectedRecipe);
                return waterTank.stored + 0.001f < needed;
            }
        }

        /// <summary>
        /// Live activity factor read by <see cref="PollutionEmitter"/>. Sluice discharge
        /// mode increases effluent load while active.
        /// </summary>
        public float PollutionActivity01 => IsActive && IsOnline
            ? (sluiceDischargeTailings ? 2.25f : 1f)
            : 0f;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            EnsureContainers();
            EnsureTanks();

            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            _power.connectRadius = 1.6f;

            _pollutionEmitter = GetComponent<PollutionEmitter>();

            var ports = GetComponent<PortConfig>();
            if (ports != null) ports.EnsureAllFaces();

            SimulationTickManager.EnsureInstance();
        }

        private void OnEnable()
        {
            EnsureContainers();
            EnsureTanks();
            SimulationTickManager.Instance?.Register(this, this);
        }

        private void OnDisable()
        {
            SimulationTickManager.Instance?.Unregister(this);
        }

        public void EnsureContainers()
        {
            if (inputC   == null) inputC   = new ItemContainer("Inputs",   INPUT_SLOTS);   else inputC.Resize(INPUT_SLOTS);
            if (outputC  == null) outputC  = new ItemContainer("Outputs",  OUTPUT_SLOTS);  else outputC.Resize(OUTPUT_SLOTS);
            if (upgradeC == null) upgradeC = new ItemContainer("Upgrades", UPGRADE_SLOTS); else upgradeC.Resize(UPGRADE_SLOTS);

            upgradeC.OnChanged -= RecalculateUpgrades;
            upgradeC.OnChanged += RecalculateUpgrades;
            RecalculateUpgrades();
        }

        public void EnsureTanks()
        {
            if (waterTank == null)
                waterTank = new MachineFluidTank("Wash Water", 500f, LiquidType.Water, autoType: false);
            waterTank.liquid = LiquidType.Water;
            waterTank.autoType = false;
            if (waterTank.capacity <= 0f) waterTank.capacity = 500f;
            if (string.IsNullOrWhiteSpace(waterTank.label)) waterTank.label = "Wash Water";
        }

        // ── IMachineProcessState ──────────────────────────────────────

        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureContainers();
            EnsureTanks();
            state.recipeName = _current != null ? _current.name : string.Empty;
            state.selectedRecipeName = _selectedRecipe != null ? _selectedRecipe.name : string.Empty;
            state.progressSeconds = Mathf.Max(0f, _processProgress);
            MachineProcessPersistence.CaptureTanks(state, FluidTanks);
            state.SetExtra("userEnabled", _userEnabled ? 1f : 0f);
            state.SetExtra("sluiceDischarge", sluiceDischargeTailings ? 1f : 0f);
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureContainers();
            EnsureTanks();

            MachineProcessPersistence.RestoreTanks(state, FluidTanks);
            waterTank.liquid = LiquidType.Water;

            _userEnabled = state.GetExtra("userEnabled", 1f) >= 0.5f;
            sluiceDischargeTailings = state.GetExtra("sluiceDischarge", 0f) >= 0.5f;
            _selectedRecipe = MachineProcessPersistence.Resolve(knownRecipes, state.selectedRecipeName, nameof(OreWasher));
            _current = MachineProcessPersistence.Resolve(knownRecipes, state.recipeName, nameof(OreWasher));
            if (_current == null) _current = FindRecipeForInput();
            _processProgress = _current != null
                ? Mathf.Clamp(state.progressSeconds, 0f, EffectiveProcessTime(_current))
                : 0f;
        }

        /// <summary>Legacy helper for direct state restore if called outside IMachineProcessState.</summary>
        public void RestorePersistentState(string recipeId, float progressSeconds, bool userEnabled)
        {
            EnsureContainers();
            EnsureTanks();
            _userEnabled = userEnabled;
            _current = MachineProcessPersistence.Resolve(knownRecipes, recipeId, nameof(OreWasher));
            _selectedRecipe = _current;
            if (_current == null) _current = FindRecipeForInput();
            _processProgress = _current != null
                ? Mathf.Clamp(progressSeconds, 0f, EffectiveProcessTime(_current))
                : 0f;
        }

        // ── IItemPortHost ─────────────────────────────────────────────

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
            _portContainers ??= new ItemPortContainer[2];
            _portContainers[0] = new ItemPortContainer("Inputs",  inputC,  canInput: true,  canOutput: false);
            _portContainers[1] = new ItemPortContainer("Outputs", outputC, canInput: false, canOutput: true);
            return _portContainers;
        }

        // ── IFluidStore & Canister Interaction ────────────────────────

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

        public float Draw(LiquidType type, float litres)
        {
            EnsureTanks();
            return type == LiquidType.Water ? waterTank.Remove(litres) : 0f;
        }

        public float Fill(LiquidType type, float litres)
        {
            EnsureTanks();
            return type == LiquidType.Water ? waterTank.Add(LiquidType.Water, litres) : 0f;
        }

        /// <summary>Pour 0.5 L of water from a held LiquidCanister directly into the wash tank.</summary>
        public bool TryPourCanister(ItemStack can)
        {
            if (can == null || !(can.item is LiquidCanister)) return false;
            EnsureTanks();
            var carried = LiquidCanister.CarriedLiquid(can);
            if (carried == null || carried.Value != LiquidType.Water) return false;
            float space = waterTank.SpaceFor(LiquidType.Water);
            if (space < 0.001f) return false;
            float litres = Mathf.Min(LiquidCanister.LitresPerClick, space, can.durability / 1000f);
            if (litres <= 0.001f) return false;
            float added = waterTank.Add(LiquidType.Water, litres);
            if (added <= 0.001f) return false;
            LiquidCanister.RemoveMl(can, Mathf.RoundToInt(added * 1000f));
            return true;
        }

        // ── Simulation Tick ───────────────────────────────────────────

        public void SimulationTick(float dt)
        {
            EnsureContainers();
            EnsureTanks();

            // Periodically check for adjacent water pipes/tanks/pumps and sample local runoff.
            _pipeScanTimer -= dt;
            if (_pipeScanTimer <= 0f)
            {
                _pipeScanTimer = 0.5f;
                IsWaterPiped = CheckWaterPiped();
                SourceContamination01 = PollutionService.SampleRunoff01(transform.position);
            }

            // Refill internal wash tank from connected water network if space is available.
            if (IsWaterPiped && waterTank.stored < waterTank.capacity - 0.01f)
            {
                float want = Mathf.Min(waterTank.capacity - waterTank.stored, pipedWaterRefillLps * Mathf.Max(0f, dt));
                if (want > 0.001f)
                {
                    float pulled = PullPipedWater(want);
                    if (pulled > 0f) waterTank.Add(LiquidType.Water, pulled);
                }
            }

            if (!_userEnabled)
            {
                CurrentWattage = 0f;
                IsTailingsBackedUp = false;
                if (_power != null) _power.wattsPerSecond = 0f;
                return;
            }

            if (_current == null || !HasAllInputs(_current))
                _current = FindRecipeForInput();

            bool tailingsBlocked = false;
            bool canRunBatch = _current != null && !IsLowWater && CanFitOutputs(_current, out tailingsBlocked);
            IsTailingsBackedUp = tailingsBlocked;

            float wantWattage = (_current != null && canRunBatch)
                ? baseWattsPerSecond * _effMult
                : idleWattsPerSecond;
            CurrentWattage = wantWattage;
            if (_power != null) _power.wattsPerSecond = wantWattage;

            if (!IsOnline) return;

            if (_current == null)
            {
                _processProgress = 0f;
                return;
            }

            if (!canRunBatch)
                return;

            // Contaminated feed water slightly reduces washing speed (up to -20% at max runoff).
            float waterQualitySpeed = Mathf.Lerp(1f, 0.8f, SourceContamination01);
            _processProgress += dt * _speedMult * waterQualitySpeed;
            if (_processProgress >= EffectiveProcessTime(_current))
                CompleteOneBatch();
        }

        private float EffectiveProcessTime(MachineRecipe r)
            => r != null ? Mathf.Max(0.05f, r.processSeconds) : 0.05f;

        public float EffectiveWaterPerBatch(MachineRecipe r)
            => Mathf.Max(1f, waterLitresPerBatch * Mathf.Lerp(0.85f, 1.15f, SourceContamination01));

        private void RecalculateUpgrades()
        {
            if (upgradeC == null) return;
            float speed = 1f;
            float eff = 1f;
            for (int i = 0; i < upgradeC.Size; i++)
            {
                var stack = upgradeC.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.item == null) continue;
                if (stack.item is FurnaceUpgradeItem furnMod)
                {
                    speed *= Mathf.Pow(furnMod.speedMultiplier, stack.count);
                    eff   *= Mathf.Pow(furnMod.efficiencyMultiplier, stack.count);
                }
            }
            _speedMult = speed;
            _effMult = eff;
        }

        public void SelectRecipe(MachineRecipe recipe)
        {
            if (recipe != null && !knownRecipes.Contains(recipe)) return;
            if (_selectedRecipe == recipe) return;
            _selectedRecipe = recipe;
            _current = null;
            _processProgress = 0f;
        }

        // ── Piped Water Network Integration ───────────────────────────

        private bool CheckWaterPiped()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, 3.5f, s_pipeOverlap, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var col = s_pipeOverlap[i];
                if (col == null) continue;
                var node = col.GetComponentInParent<FluidNode>();
                if (node == null) continue;

                if (node is WaterTank directTank && (directTank.IsEmpty || directTank.liquidType == LiquidType.Water) && directTank.StoredLitres > 0.01f)
                    return true;
                if (node is WaterPump directPump && directPump.liquidType == LiquidType.Water && directPump.internalLitres > 0.01f)
                    return true;

                if (node.network?.nodes != null)
                {
                    foreach (var n in node.network.nodes)
                    {
                        if (n is WaterTank tank && !tank.IsEmpty && tank.liquidType == LiquidType.Water && tank.StoredLitres > 0.01f)
                            return true;
                        if (n is WaterPump pump && pump.liquidType == LiquidType.Water && (pump.internalLitres > 0.01f || pump.HasSource))
                            return true;
                    }
                    // Also report piped if connected to a water pipe/tank network even when currently dry.
                    if (node.Kind == FluidNodeKind.Pipe || node.Kind == FluidNodeKind.Tank)
                        return true;
                }
            }
            return false;
        }

        private float PullPipedWater(float wantLitres)
        {
            if (wantLitres <= 0f) return 0f;
            float remaining = wantLitres;
            int count = Physics.OverlapSphereNonAlloc(transform.position, 3.5f, s_pipeOverlap, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count && remaining > 0.001f; i++)
            {
                var col = s_pipeOverlap[i];
                if (col == null) continue;
                var node = col.GetComponentInParent<FluidNode>();
                if (node == null) continue;

                if (node is WaterTank directTank && directTank.liquidType == LiquidType.Water)
                {
                    remaining -= directTank.TakeSome(LiquidType.Water, remaining);
                    if (remaining <= 0.001f) break;
                }
                else if (node is WaterPump directPump && directPump.liquidType == LiquidType.Water && directPump.internalLitres > 0.001f)
                {
                    float take = Mathf.Min(remaining, directPump.internalLitres);
                    directPump.internalLitres -= take;
                    remaining -= take;
                    if (remaining <= 0.001f) break;
                }

                if (node.network?.nodes == null) continue;
                foreach (var n in node.network.nodes)
                {
                    if (remaining <= 0.001f) break;
                    if (n is WaterTank tank && tank.liquidType == LiquidType.Water && tank.StoredLitres > 0.001f)
                    {
                        remaining -= tank.TakeSome(LiquidType.Water, remaining);
                    }
                    else if (n is WaterPump pump && pump.liquidType == LiquidType.Water && pump.internalLitres > 0.001f)
                    {
                        float take = Mathf.Min(remaining, pump.internalLitres);
                        pump.internalLitres -= take;
                        remaining -= take;
                    }
                }
            }
            return wantLitres - remaining;
        }

        // ── IItemConsumer ─────────────────────────────────────────────

        public int GetInputCapacity(ItemDefinition item)
        {
            if (item == null) return 0;
            EnsureContainers();
            foreach (var r in knownRecipes)
            {
                if (r == null || r.inputs == null) continue;
                foreach (var inp in r.inputs)
                {
                    if (ItemIdentity.Same(inp.item, item))
                        return inputC.HasSpace(item, 1) ? ItemStack.MaxItemsPerStack(item) : 0;
                }
            }
            return 0;
        }

        public int TryInsert(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            if (GetInputCapacity(item) <= 0) return 0;
            var leftover = inputC.Insert(new ItemStack(item, count));
            return count - (leftover?.count ?? 0);
        }

        // ── IItemProvider ─────────────────────────────────────────────

        public ItemDefinition PeekOutput(out int count)
        {
            EnsureContainers();
            for (int i = 0; i < outputC.Size; i++)
            {
                var s = outputC.GetSlot(i);
                if (!s.IsEmpty && s.item != null)
                {
                    count = s.count;
                    return s.item;
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

        // ── Recipe Matching & Batch Execution ─────────────────────────

        private MachineRecipe FindRecipeForInput()
        {
            if (_selectedRecipe != null)
                return HasAllInputs(_selectedRecipe) ? _selectedRecipe : null;

            foreach (var r in knownRecipes)
            {
                if (r == null || r.inputs == null || r.inputs.Length == 0) continue;
                if (HasAllInputs(r)) return r;
            }
            return null;
        }

        private bool HasAllInputs(MachineRecipe recipe)
        {
            if (recipe == null || recipe.inputs == null || recipe.inputs.Length == 0) return false;
            foreach (var inp in recipe.inputs)
            {
                if (inp.item == null || inp.count <= 0) continue;
                if (CountMatching(inputC, inp.item) < inp.count)
                    return false;
            }
            return true;
        }

        private static int CountMatching(ItemContainer container, ItemDefinition target)
        {
            if (container == null || target == null) return 0;
            int total = 0;
            for (int i = 0; i < container.Size; i++)
            {
                var s = container.GetSlot(i);
                if (!s.IsEmpty && ItemIdentity.Same(s.item, target))
                    total += s.count;
            }
            return total;
        }

        private static int RemoveMatching(ItemContainer container, ItemDefinition target, int count)
        {
            if (container == null || target == null || count <= 0) return 0;
            int removed = container.Remove(target, count);
            if (removed >= count) return removed;
            for (int i = 0; i < container.Size && removed < count; i++)
            {
                var s = container.GetSlot(i);
                if (s.IsEmpty || !ItemIdentity.Same(s.item, target)) continue;
                int take = Mathf.Min(count - removed, s.count);
                removed += container.Remove(s.item, take);
            }
            return removed;
        }

        private bool CanFitOutputs(MachineRecipe recipe, out bool tailingsBlocked)
        {
            tailingsBlocked = false;
            if (recipe == null || recipe.outputItem == null) return false;
            if (!outputC.HasSpace(recipe.outputItem, Mathf.Max(1, recipe.outputCount)))
                return false;

            if (!sluiceDischargeTailings && recipe.byproductItem != null && recipe.byproductCount > 0 && recipe.byproductChance >= 0.99f)
            {
                if (!outputC.HasSpace(recipe.byproductItem, recipe.byproductCount))
                {
                    tailingsBlocked = true;
                    return false;
                }
            }
            return true;
        }

        private void CompleteOneBatch()
        {
            if (_current == null) return;

            float waterNeeded = EffectiveWaterPerBatch(_current);
            if (waterTank.stored + 0.001f < waterNeeded)
            {
                _processProgress = EffectiveProcessTime(_current);
                return;
            }

            if (!CanFitOutputs(_current, out bool tailingsBlocked))
            {
                IsTailingsBackedUp = tailingsBlocked;
                _processProgress = EffectiveProcessTime(_current);
                return;
            }

            // Consume water and item inputs.
            waterTank.Remove(waterNeeded);
            foreach (var inp in _current.inputs)
            {
                RemoveMatching(inputC, inp.item, inp.count);
                ProductionStatsTracker.RecordConsumed(inp.item, inp.count);
            }

            // Produce enriched primary output.
            outputC.Insert(new ItemStack(_current.outputItem, _current.outputCount));
            ProductionStatsTracker.RecordProduced(_current.outputItem, _current.outputCount);

            // Handle byproduct (Mineral Tailings or secondary recovery).
            if (_current.byproductItem != null && _current.byproductCount > 0 && Random.value <= _current.byproductChance)
            {
                if (!sluiceDischargeTailings && outputC.HasSpace(_current.byproductItem, _current.byproductCount))
                {
                    outputC.Insert(new ItemStack(_current.byproductItem, _current.byproductCount));
                    ProductionStatsTracker.RecordProduced(_current.byproductItem, _current.byproductCount);
                }
                else
                {
                    // Sluice discharge or overflow: vent tailings slurry directly into soil/water runoff.
                    Vector3 releasePoint = _pollutionEmitter != null
                        ? _pollutionEmitter.ReleasePoint
                        : transform.position + new Vector3(0f, 0.8f, 0f);
                    PollutionService.Emit(releasePoint, new PollutionLoad
                    {
                        airborneSmog = 0.06f * _current.byproductCount,
                        runoff       = 0.16f * _current.byproductCount,
                        climateLoad  = 0.01f * _current.byproductCount
                    });
                }
            }

            IsTailingsBackedUp = false;
            _processProgress = 0f;
            _current = FindRecipeForInput();
        }
    }
}
