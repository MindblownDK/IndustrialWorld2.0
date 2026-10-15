// Assets/Scripts/VoxelEngine/Simulation/MetallurgicalFoundry.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — METALLURGICAL FOUNDRY & COKE KILN            ║
// ║  Two-stage metallurgical processing (Section 6.2 / 4.6.0):       ║
// ║    • CokeKiln: Solid-fuel Beehive Coke & Alloy Kiln with         ║
// ║      automatic self-fueling from Coal/Coke for early automation. ║
// ║    • ArcFoundry: Powered 3-Phase Electric Arc Foundry with       ║
// ║      higher speed, upgrade module slots, and bonus alloy yield.  ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Environment;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.Simulation
{
    public enum FoundryTier
    {
        CokeKiln,
        ArcFoundry
    }

    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class MetallurgicalFoundry : MonoBehaviour, IMachine, IItemConsumer, IItemProvider, IItemPortHost, IMachineProcessState
    {
        [Header("Foundry Configuration")]
        public FoundryTier tier = FoundryTier.CokeKiln;
        public List<MachineRecipe> knownRecipes = new();

        [Header("Containers")]
        public ItemContainer inputC;
        public ItemContainer fuelC;
        public ItemContainer outputC;
        public ItemContainer upgradeC;

        [Header("Solid-Fuel Kiln Settings (CokeKiln)")]
        [Tooltip("When enabled on a Coke & Alloy Kiln, automatically pulls Coal or Metallurgical Coke from Inputs/Outputs into the Fuel slot when fuel runs out so a single input belt can automate coking and alloying.")]
        public bool autoSelfFuel = true;

        [Header("Electric Arc Settings (ArcFoundry)")]
        public float baseWattsPerSecond = 450f;
        public float idleWattsPerSecond = 15f;

        // ── Tier Sizing & Multipliers ─────────────────────────────────

        public bool UsesSolidFuel => tier == FoundryTier.CokeKiln;
        public int InputSlots   => tier == FoundryTier.ArcFoundry ? 4 : 3;
        public int OutputSlots  => 4;
        public int UpgradeSlots => tier == FoundryTier.ArcFoundry ? 3 : 0;
        public float TierSpeedMultiplier => tier == FoundryTier.ArcFoundry ? 1.75f : 1.0f;

        // ── Runtime ───────────────────────────────────────────────────

        private MachineRecipe _current;
        private MachineRecipe _selectedRecipe;
        private float _processProgress;
        private float _fuelRemaining;
        private float _fuelMaxDuration;
        private PowerConsumer _power;
        private bool _userEnabled = true;
        private float _speedMult = 1f;
        private float _effMult = 1f;
        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;

        // ── IMachine & UI Properties ──────────────────────────────────

        public string MachineName => tier == FoundryTier.ArcFoundry ? "Electric Arc Foundry" : "Coke & Alloy Kiln";
        public bool IsBurning => UsesSolidFuel && _fuelRemaining > 0.001f;
        public float FuelRemaining => _fuelRemaining;
        public float FuelMaxDuration => _fuelMaxDuration;
        public float FuelProgress01 => _fuelMaxDuration > 0.01f ? Mathf.Clamp01(_fuelRemaining / _fuelMaxDuration) : 0f;

        public bool IsOnline => UsesSolidFuel
            ? (_fuelRemaining > 0.001f || HasAvailableFuel())
            : (_power != null && _power.IsPowered);

        public bool IsActive => _userEnabled && _current != null && _processProgress > 0f && (!UsesSolidFuel || _fuelRemaining > 0.001f);
        public float Progress01 => _current == null ? 0f : Mathf.Clamp01(_processProgress / EffectiveProcessTime(_current));
        public float CurrentWattage { get; private set; }
        public string CurrentRecipeId => _current != null ? _current.name : (_selectedRecipe != null ? _selectedRecipe.name : string.Empty);
        public MachineRecipe CurrentRecipe => _current != null ? _current : _selectedRecipe;
        public float ProcessProgressSeconds => _processProgress;
        public float SpeedMultiplier => _speedMult * TierSpeedMultiplier;
        public float EfficiencyMultiplier => _effMult;

        public bool UserEnabled
        {
            get => _userEnabled;
            set => _userEnabled = value;
        }

        public float PollutionActivity01 => IsActive ? 1f : 0f;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            EnsureContainers();

            if (!UsesSolidFuel)
            {
                _power = GetComponent<PowerConsumer>();
                if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
                _power.connectRadius = 1.8f;
            }

            var ports = GetComponent<PortConfig>();
            if (ports != null) ports.EnsureAllFaces();

            SimulationTickManager.EnsureInstance();
        }

        private void OnEnable()
        {
            EnsureContainers();
            SimulationTickManager.Instance?.Register(this, this);
        }

        private void OnDisable()
        {
            SimulationTickManager.Instance?.Unregister(this);
        }

        public void EnsureContainers()
        {
            int inSlots  = InputSlots;
            int outSlots = OutputSlots;
            int upSlots  = Mathf.Max(1, UpgradeSlots);

            if (inputC  == null) inputC  = new ItemContainer("Inputs",  inSlots);  else inputC.Resize(inSlots);
            if (fuelC   == null) fuelC   = new ItemContainer("Fuel",    1);        else fuelC.Resize(1);
            if (outputC == null) outputC = new ItemContainer("Outputs", outSlots); else outputC.Resize(outSlots);
            if (upgradeC == null) upgradeC = new ItemContainer("Upgrades", upSlots); else upgradeC.Resize(upSlots);

            upgradeC.OnChanged -= RecalculateUpgrades;
            upgradeC.OnChanged += RecalculateUpgrades;
            RecalculateUpgrades();
        }

        private void RecalculateUpgrades()
        {
            if (!UsesSolidFuel && upgradeC != null)
            {
                float speed = 1f;
                float eff = 1f;
                for (int i = 0; i < upgradeC.Size; i++)
                {
                    var stack = upgradeC.GetSlot(i);
                    if (stack == null || stack.IsEmpty || stack.item == null) continue;
                    if (stack.item is FurnaceUpgradeItem mod)
                    {
                        speed *= Mathf.Pow(mod.speedMultiplier, stack.count);
                        eff   *= Mathf.Pow(mod.efficiencyMultiplier, stack.count);
                    }
                }
                _speedMult = speed;
                _effMult = eff;
            }
            else
            {
                _speedMult = 1f;
                _effMult = 1f;
            }
        }

        // ── IMachineProcessState ──────────────────────────────────────

        private const string FuelRemainingKey = "fuelRemaining";
        private const string FuelMaxKey       = "fuelMaxDuration";

        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureContainers();
            state.recipeName = _current != null ? _current.name : string.Empty;
            state.selectedRecipeName = _selectedRecipe != null ? _selectedRecipe.name : string.Empty;
            state.progressSeconds = Mathf.Max(0f, _processProgress);
            state.SetExtra(FuelRemainingKey, Mathf.Max(0f, _fuelRemaining));
            state.SetExtra(FuelMaxKey, Mathf.Max(0f, _fuelMaxDuration));
            state.SetExtra("userEnabled", _userEnabled ? 1f : 0f);
            state.SetExtra("autoSelfFuel", autoSelfFuel ? 1f : 0f);
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureContainers();
            _userEnabled = state.GetExtra("userEnabled", 1f) >= 0.5f;
            autoSelfFuel = state.GetExtra("autoSelfFuel", 1f) >= 0.5f;
            float maxDuration = Mathf.Max(0f, MachineProcessPersistence.FiniteOr(state.GetExtra(FuelMaxKey, 0f), 0f));
            float remaining = Mathf.Max(0f, MachineProcessPersistence.FiniteOr(state.GetExtra(FuelRemainingKey, 0f), 0f));
            _fuelMaxDuration = maxDuration;
            _fuelRemaining = Mathf.Min(remaining, maxDuration);
            _selectedRecipe = MachineProcessPersistence.Resolve(knownRecipes, state.selectedRecipeName, nameof(MetallurgicalFoundry));
            _current = MachineProcessPersistence.Resolve(knownRecipes, state.recipeName, nameof(MetallurgicalFoundry));
            if (_current == null) _current = FindRecipeForInput();
            _processProgress = _current != null
                ? Mathf.Clamp(state.progressSeconds, 0f, EffectiveProcessTime(_current))
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
            if (UsesSolidFuel)
            {
                if (_portContainers == null || _portContainers.Length != 3)
                    _portContainers = new ItemPortContainer[3];
                _portContainers[0] = new ItemPortContainer("Inputs",  inputC,  canInput: true,  canOutput: false);
                _portContainers[1] = new ItemPortContainer("Fuel",    fuelC,   canInput: true,  canOutput: false);
                _portContainers[2] = new ItemPortContainer("Outputs", outputC, canInput: false, canOutput: true);
            }
            else
            {
                if (_portContainers == null || _portContainers.Length != 2)
                    _portContainers = new ItemPortContainer[2];
                _portContainers[0] = new ItemPortContainer("Inputs",  inputC,  canInput: true,  canOutput: false);
                _portContainers[1] = new ItemPortContainer("Outputs", outputC, canInput: false, canOutput: true);
            }
            return _portContainers;
        }

        // ── Simulation Tick ───────────────────────────────────────────

        public void SimulationTick(float dt)
        {
            EnsureContainers();

            if (!_userEnabled)
            {
                CurrentWattage = 0f;
                if (_power != null) _power.wattsPerSecond = 0f;
                return;
            }

            if (_current == null || !HasAllInputs(_current))
                _current = FindRecipeForInput();

            bool hasOutputSpace = _current != null && outputC.HasSpace(_current.outputItem, Mathf.Max(1, _current.outputCount));

            if (UsesSolidFuel)
            {
                CurrentWattage = 0f;
                if (_fuelRemaining > 0f)
                    _fuelRemaining = Mathf.Max(0f, _fuelRemaining - dt);

                if (_current == null || !hasOutputSpace)
                {
                    if (_current == null) _processProgress = 0f;
                    return;
                }

                if (_fuelRemaining <= 0.001f)
                {
                    if (!TryConsumeFuel(_current))
                        return;
                }

                _processProgress += dt * TierSpeedMultiplier;
                if (_processProgress >= EffectiveProcessTime(_current))
                    CompleteOneBatch();
            }
            else
            {
                float wantWatts = (_current != null && hasOutputSpace)
                    ? baseWattsPerSecond * _effMult
                    : idleWattsPerSecond;
                CurrentWattage = wantWatts;
                if (_power != null) _power.wattsPerSecond = wantWatts;

                if (!IsOnline) return;
                if (_current == null)
                {
                    _processProgress = 0f;
                    return;
                }
                if (!hasOutputSpace) return;

                _processProgress += dt * _speedMult * TierSpeedMultiplier;
                if (_processProgress >= EffectiveProcessTime(_current))
                    CompleteOneBatch();
            }
        }

        private float EffectiveProcessTime(MachineRecipe r)
            => r != null ? Mathf.Max(0.05f, r.processSeconds) : 0.05f;

        public void SelectRecipe(MachineRecipe recipe)
        {
            if (recipe != null && !knownRecipes.Contains(recipe)) return;
            if (_selectedRecipe == recipe) return;
            _selectedRecipe = recipe;
            _current = null;
            _processProgress = 0f;
        }

        // ── Solid Fuel & Self-Fueling Logic ───────────────────────────

        private bool HasAvailableFuel()
        {
            var slot = fuelC.GetSlot(0);
            if (!slot.IsEmpty && slot.item is ResourceItem ri && ri.fuelSeconds > 0f)
                return true;
            if (!autoSelfFuel) return false;
            return FindSelfFuelCandidate(null, out _, out _);
        }

        private bool TryConsumeFuel(MachineRecipe activeRecipe)
        {
            var slot = fuelC.GetSlot(0);
            if (slot.IsEmpty && autoSelfFuel)
            {
                if (FindSelfFuelCandidate(activeRecipe, out var sourceContainer, out var fuelItem))
                {
                    if (sourceContainer.Remove(fuelItem, 1) > 0)
                        fuelC.Insert(new ItemStack(fuelItem, 1));
                }
                slot = fuelC.GetSlot(0);
            }

            if (!slot.IsEmpty && slot.item is ResourceItem ri && ri.fuelSeconds > 0f)
            {
                _fuelRemaining = ri.fuelSeconds;
                _fuelMaxDuration = ri.fuelSeconds;
                fuelC.Remove(ri, 1);
                return true;
            }
            return false;
        }

        private bool FindSelfFuelCandidate(MachineRecipe activeRecipe, out ItemContainer source, out ItemDefinition fuelItem)
        {
            // 1. Prefer Metallurgical Coke sitting in outputC (self-sustaining coking/alloying).
            for (int i = 0; i < outputC.Size; i++)
            {
                var s = outputC.GetSlot(i);
                if (!s.IsEmpty && s.item is ResourceItem ri && ri.fuelSeconds > 0f)
                {
                    source = outputC;
                    fuelItem = s.item;
                    return true;
                }
            }

            // 2. Otherwise check inputC, leaving enough items to still satisfy activeRecipe if possible.
            for (int i = 0; i < inputC.Size; i++)
            {
                var s = inputC.GetSlot(i);
                if (s.IsEmpty || s.item is not ResourceItem ri || ri.fuelSeconds <= 0f) continue;

                int neededForRecipe = 0;
                if (activeRecipe != null && activeRecipe.inputs != null)
                {
                    foreach (var inp in activeRecipe.inputs)
                        if (ItemIdentity.Same(inp.item, s.item))
                            neededForRecipe += inp.count;
                }

                if (CountMatching(inputC, s.item) > neededForRecipe)
                {
                    source = inputC;
                    fuelItem = s.item;
                    return true;
                }
            }

            source = null;
            fuelItem = null;
            return false;
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
                    if (ItemIdentity.Same(inp.item, item) && inputC.HasSpace(item, 1))
                        return ItemStack.MaxItemsPerStack(item);
                }
            }

            // On CokeKiln, also accept any solid fuel into fuelC (or inputC) from belts.
            if (UsesSolidFuel && item is ResourceItem ri && ri.fuelSeconds > 0f)
            {
                if (fuelC.HasSpace(item, 1)) return ItemStack.MaxItemsPerStack(item);
                if (inputC.HasSpace(item, 1)) return ItemStack.MaxItemsPerStack(item);
            }

            return 0;
        }

        public int TryInsert(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            if (GetInputCapacity(item) <= 0) return 0;

            bool isRecipeInput = false;
            foreach (var r in knownRecipes)
            {
                if (r == null || r.inputs == null) continue;
                foreach (var inp in r.inputs)
                    if (ItemIdentity.Same(inp.item, item)) { isRecipeInput = true; break; }
                if (isRecipeInput) break;
            }

            // If it's purely fuel (or fuelC is empty on a CokeKiln), top up fuelC first when appropriate.
            if (UsesSolidFuel && item is ResourceItem ri && ri.fuelSeconds > 0f && (!isRecipeInput || fuelC.GetSlot(0).IsEmpty))
            {
                var remFuel = fuelC.Insert(new ItemStack(item, count));
                int remCount = remFuel?.count ?? 0;
                if (remCount <= 0 || !isRecipeInput)
                    return count - remCount;
                var remIn = inputC.Insert(new ItemStack(item, remCount));
                return count - (remIn?.count ?? 0);
            }

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

        // ── Recipe Matching & Execution ───────────────────────────────

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

        private void CompleteOneBatch()
        {
            if (_current == null) return;

            if (!outputC.HasSpace(_current.outputItem, Mathf.Max(1, _current.outputCount)))
            {
                _processProgress = EffectiveProcessTime(_current);
                return;
            }

            foreach (var inp in _current.inputs)
            {
                RemoveMatching(inputC, inp.item, inp.count);
                ProductionStatsTracker.RecordConsumed(inp.item, inp.count);
            }

            outputC.Insert(new ItemStack(_current.outputItem, _current.outputCount));
            ProductionStatsTracker.RecordProduced(_current.outputItem, _current.outputCount);

            if (_current.byproductItem != null && _current.byproductCount > 0 && Random.value <= _current.byproductChance)
            {
                if (outputC.HasSpace(_current.byproductItem, _current.byproductCount))
                {
                    outputC.Insert(new ItemStack(_current.byproductItem, _current.byproductCount));
                    ProductionStatsTracker.RecordProduced(_current.byproductItem, _current.byproductCount);
                }
            }

            _processProgress = 0f;
            _current = FindRecipeForInput();
        }
    }
}
