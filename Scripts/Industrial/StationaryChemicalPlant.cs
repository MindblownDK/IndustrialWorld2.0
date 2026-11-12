// Assets/Scripts/VoxelEngine/Industrial/StationaryChemicalPlant.cs
//
// Stationary Chemical Plant — placeable world machine, the ground-based
// equivalent of the Ship Chemical Plant. Multi-input / multi-output processor
// driven by ProcessingRecipe assets (category "Chemistry"), supporting both
// item slots AND fluid tanks via the shared ProcessingExecutor.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.Industrial
{
    [RequireComponent(typeof(CraftingStation))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class StationaryChemicalPlant : MonoBehaviour, IItemPortHost, IMachineProcessState
    {
        public const int INPUT_SLOTS   = 3;
        public const int OUTPUT_SLOTS  = 3;
        /// <summary>18.15.0-dev: the plant joins the universal module system.</summary>
        public const int UPGRADE_SLOTS = 2;

        [Header("Recipes")]
        public List<ProcessingRecipe> knownRecipes = new();

        [Header("Containers (auto-created)")]
        public ItemContainer inputC;
        public ItemContainer outputC;
        [Tooltip("Universal machine module slots (18.15.0-dev). Appended last in the saved container order, so a save written before the plant had module slots still restores its inputs and outputs at the same index.")]
        public ItemContainer upgradeC;

        [Header("Fluid Tanks")]
        public MachineFluidTank fluidIn  = new MachineFluidTank("Fluid In",  2000f, LiquidType.RefinedOil, autoType: true);
        public MachineFluidTank fluidOut = new MachineFluidTank("Fluid Out", 2000f, LiquidType.LiquidFuel, autoType: true);
        public IReadOnlyList<MachineFluidTank> FluidTanks => new[] { fluidIn, fluidOut };

        [Header("Tuning")]
        public float baseWattsPerSecond = 720f;
        public float idleWattsPerSecond = 25f;

        private ProcessingRecipe _current;
        private float _progress;
        private PowerConsumer _power;

        public float Progress01         => _current == null ? 0f : Mathf.Clamp01(_progress / Mathf.Max(0.1f, _current.secondsPerBatch));
        public ProcessingRecipe Current => _current;
        public bool  IsOnline           => _power != null && _power.IsPowered;
        public float CurrentWattage     { get; private set; }
        /// <summary>18.15.0-dev: what the socketed modules add up to.</summary>
        public VoxelEngine.Simulation.MachineModuleProfile Modules { get; private set; }
            = VoxelEngine.Simulation.MachineModuleProfile.Identity;

        private void Awake()
        {
            EnsureContainers();
            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            _power.connectRadius = 1.8f;
        }

        public void EnsureContainers()
        {
            if (inputC  == null) inputC  = new ItemContainer("Inputs",  INPUT_SLOTS);  else inputC.Resize(INPUT_SLOTS);
            if (outputC == null) outputC = new ItemContainer("Outputs", OUTPUT_SLOTS); else outputC.Resize(OUTPUT_SLOTS);
            // 18.15.0-dev: the plant finishes batches, so it honours all three module roles.
            upgradeC = VoxelEngine.Simulation.MachineModules.Prepare(
                upgradeC, "Upgrades", UPGRADE_SLOTS, MachineModuleSupport.All);
            if (!_modulesHooked)
            {
                _modulesHooked = true;
                upgradeC.OnChanged += RecalculateModules;
            }
            RecalculateModules();
            fluidIn  ??= new MachineFluidTank("Fluid In",  2000f, LiquidType.RefinedOil);
            fluidOut ??= new MachineFluidTank("Fluid Out", 2000f, LiquidType.LiquidFuel);
        }

        [System.NonSerialized] private bool _modulesHooked;

        /// <summary>Re-read the socketed modules. Cheap enough to call from EnsureContainers.</summary>
        private void RecalculateModules()
            => Modules = VoxelEngine.Simulation.MachineModules.Read(upgradeC);

        // ── Machine process persistence (9.57.0-dev) ────────────────────────
        // One auto-typed tank in, one auto-typed tank out, plus the batch and the
        // player's locked recipe. A save written before this round carries no
        // record, and the plant then loads empty and idle exactly as it does today.

        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureContainers();
            state.recipeName = MachineProcessPersistence.NameOf(_current);
            state.selectedRecipeName = MachineProcessPersistence.NameOf(selectedRecipe);
            state.progressSeconds = Mathf.Max(0f, _progress);
            MachineProcessPersistence.CaptureTanks(state, FluidTanks);
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureContainers();

            MachineProcessPersistence.RestoreTanks(state, FluidTanks);

            selectedRecipe = MachineProcessPersistence.Resolve(knownRecipes, state.selectedRecipeName, nameof(StationaryChemicalPlant));
            _current = MachineProcessPersistence.Resolve(knownRecipes, state.recipeName, nameof(StationaryChemicalPlant));
            _progress = _current == null
                ? 0f
                : Mathf.Clamp(state.progressSeconds, 0f, Mathf.Max(0.1f, _current.secondsPerBatch));
        }

        // ── IItemPortHost ───────────────────────────────────────────────────
        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;

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

        private void Update()
        {
            EnsureContainers();

            // 18.15.0-dev: an Efficiency module cuts the running draw, exactly as it does on
            // the refinery, the furnace and every other machine with module slots.
            float wantWatts = (_current != null)
                ? baseWattsPerSecond * _current.powerDrawMultiplier * Modules.efficiencyMultiplier
                : idleWattsPerSecond;
            CurrentWattage = wantWatts;
            if (_power != null) _power.wattsPerSecond = wantWatts;

            if (!IsOnline) return;

            if (_current == null) _current = FindRecipe();
            if (_current == null) { _progress = 0f; return; }

            // 18.15.0-dev: a Speed module advances the batch faster.
            _progress += Time.deltaTime * Mathf.Max(0.01f, Modules.speedMultiplier);
            if (_progress >= Mathf.Max(0.1f, _current.secondsPerBatch))
                CompleteBatch();
        }

        private IFluidStore Fluids() => new MachineFluidStore(FluidTanks);
        private ItemContainer[] InArr  => new[] { inputC };
        private ItemContainer[] OutArr => new[] { outputC };

        [System.NonSerialized] public ProcessingRecipe selectedRecipe;

        private ProcessingRecipe FindRecipe()
        {
            var fluids = Fluids();
            if (selectedRecipe != null)
                return ProcessingExecutor.CanRun(selectedRecipe, InArr, OutArr, fluids) ? selectedRecipe : null;
            for (int i = 0; i < knownRecipes.Count; i++)
            {
                var r = knownRecipes[i];
                if (r != null && ProcessingExecutor.CanRun(r, InArr, OutArr, fluids)) return r;
            }
            return null;
        }

        private void CompleteBatch()
        {
            if (!ProcessingExecutor.Run(_current, InArr, OutArr, Fluids()))
            {
                _progress = Mathf.Max(0.1f, _current.secondsPerBatch);
                return;
            }
            // 18.15.0-dev: a socketed Yield module can hand one extra unit of the batch's
            // main product once the run has landed.
            VoxelEngine.Simulation.MachineModules.RollBonusUnit(Modules, outputC, _current.outputs);
            _progress = 0f;
            _current = FindRecipe();
        }
    }
}
