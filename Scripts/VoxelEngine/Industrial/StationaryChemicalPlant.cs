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
    public class StationaryChemicalPlant : MonoBehaviour, IItemPortHost
    {
        public const int INPUT_SLOTS  = 3;
        public const int OUTPUT_SLOTS = 3;

        [Header("Recipes")]
        public List<ProcessingRecipe> knownRecipes = new();

        [Header("Containers (auto-created)")]
        public ItemContainer inputC;
        public ItemContainer outputC;

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
            fluidIn  ??= new MachineFluidTank("Fluid In",  2000f, LiquidType.RefinedOil);
            fluidOut ??= new MachineFluidTank("Fluid Out", 2000f, LiquidType.LiquidFuel);
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

            float wantWatts = (_current != null) ? baseWattsPerSecond * _current.powerDrawMultiplier : idleWattsPerSecond;
            CurrentWattage = wantWatts;
            if (_power != null) _power.wattsPerSecond = wantWatts;

            if (!IsOnline) return;

            if (_current == null) _current = FindRecipe();
            if (_current == null) { _progress = 0f; return; }

            _progress += Time.deltaTime;
            if (_progress >= Mathf.Max(0.1f, _current.secondsPerBatch))
                CompleteBatch();
        }

        private IFluidStore Fluids() => new MachineFluidStore(FluidTanks);
        private ItemContainer[] InArr  => new[] { inputC };
        private ItemContainer[] OutArr => new[] { outputC };

        private ProcessingRecipe FindRecipe()
        {
            var fluids = Fluids();
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
            _progress = 0f;
            _current = FindRecipe();
        }
    }
}
