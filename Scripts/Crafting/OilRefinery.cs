// Assets/Scripts/VoxelEngine/Crafting/OilRefinery.cs
//
// Industrial Oil Refinery — the legacy multi-recipe processor of the refined-oil
// era. Built around ProcessingRecipe (N inputs / M outputs): two fluid tanks
// (an auto-typing input tank and an auto-typing output tank) plus item slots for
// recipes that also take or make items (plastic, etc.).
//
// Layout:
//   * 2 input slots / 4 output slots (item side)
//   * 2 upgrade slots (Speed / Efficiency, same item type as ElectricFurnace)
//   * 1 input fluid tank + 1 output fluid tank (both auto-type)
//   * Co-located PowerConsumer (auto-added in Awake)
//
// NOTE (9.38.0-dev): crude conversion now lives ONLY in the dedicated
// DistillationPlant block — see Scripts/Crafting/DistillationPlant.cs. The
// refinery is a plastics and legacy-stock machine: it runs the refined-oil
// plastic recipe and the naphtha-fed plastic recipe the plant's research
// unlocks. Refine Crude Oil, Distil Heavy Fuel Oil and Distil Marine Gas Oil
// are no longer attached here (or on the ship refinery) — the recipe ASSETS are
// kept, because saves that already hold them keep running, and Step 69 detaches
// them from both prefabs. Old saves and placed refineries keep working.
//
// Behaviour:
//   * Each tick picks the first recipe in knownRecipes where ALL inputs
//     are present and every output has at least one slot with space.
//   * Consumes inputs at batch start, produces outputs at batch end.
//   * Pulls baseWattsPerSecond * recipe.powerDrawMultiplier * efficiency
//     while a batch is in progress; idleWattsPerSecond otherwise.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.Crafting
{
    [RequireComponent(typeof(CraftingStation))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class OilRefinery : MonoBehaviour, IItemPortHost
    {
        public const int INPUT_SLOTS   = 2;
        public const int OUTPUT_SLOTS  = 4;
        public const int UPGRADE_SLOTS = 2;

        [Header("Recipes")]
        public List<ProcessingRecipe> knownRecipes = new();

        [Header("Containers (auto-created)")]
        public ItemContainer inputC;
        public ItemContainer outputC;
        public ItemContainer upgradeC;

        [Header("Fluid Tanks")]
        [Tooltip("Input tank. Auto-typed: adopts whatever feed liquid is poured in first (crude, refined oil, or naphtha for the plastic feed).")]
        public MachineFluidTank fluidIn  = new MachineFluidTank("Fluid In",  2000f, LiquidType.CrudeOil,   autoType: true);
        [Tooltip("Output tank. Auto-typed: adopts whatever liquid the running recipe produces first (Refined Oil, Heavy Fuel Oil, MGO).")]
        public MachineFluidTank fluidOut = new MachineFluidTank("Fluid Out", 2000f, LiquidType.RefinedOil, autoType: true);

        public IReadOnlyList<MachineFluidTank> FluidTanks => new[] { fluidIn, fluidOut };

        [Header("Tuning")]
        [Tooltip("Base watts/s drawn while a batch is in progress. Multiplied by recipe.powerDrawMultiplier and efficiency upgrades.")]
        public float baseWattsPerSecond = 400f;
        [Tooltip("Watts/s drawn while idle.")]
        public float idleWattsPerSecond = 20f;

        // Runtime
        private ProcessingRecipe _current;
        private float _progress;
        private PowerConsumer _power;

        public float Progress01           => _current == null ? 0 : _progress / EffectiveBatchTime(_current);
        public ProcessingRecipe Current   => _current;
        public bool  IsOnline             => _power != null && _power.IsPowered;
        public float CurrentWattage       { get; private set; }
        public float SpeedMultiplier      { get; private set; } = 1f;
        public float EfficiencyMultiplier { get; private set; } = 1f;

        private void Awake()
        {
            EnsureContainers();
            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            _power.connectRadius = 1.8f;

            upgradeC.OnChanged += RecalculateUpgrades;
            RecalculateUpgrades();
        }

        private void OnDestroy()
        {
            if (upgradeC != null) upgradeC.OnChanged -= RecalculateUpgrades;
        }

        public void EnsureContainers()
        {
            if (inputC   == null) inputC   = new ItemContainer("Inputs",   INPUT_SLOTS);   else inputC.Resize(INPUT_SLOTS);
            if (outputC  == null) outputC  = new ItemContainer("Outputs",  OUTPUT_SLOTS);  else outputC.Resize(OUTPUT_SLOTS);
            if (upgradeC == null) upgradeC = new ItemContainer("Upgrades", UPGRADE_SLOTS); else upgradeC.Resize(UPGRADE_SLOTS);
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

            // Drive power draw.
            float wantWatts = (_current != null)
                ? baseWattsPerSecond * _current.powerDrawMultiplier * EfficiencyMultiplier
                : idleWattsPerSecond;
            CurrentWattage = wantWatts;
            if (_power != null) _power.wattsPerSecond = wantWatts;

            if (!IsOnline) return;

            if (_current == null) _current = FindRecipe();
            if (_current == null) { _progress = 0; return; }

            _progress += Time.deltaTime * SpeedMultiplier;
            if (_progress >= EffectiveBatchTime(_current))
                CompleteBatch();
        }

        private float EffectiveBatchTime(ProcessingRecipe r)
            => Mathf.Max(0.1f, r.secondsPerBatch);

        // ============================================================
        //                          UPGRADES
        // ============================================================
        private void RecalculateUpgrades()
        {
            float speed = 1f, eff = 1f;
            for (int i = 0; i < upgradeC.Size; i++)
            {
                var s = upgradeC.GetSlot(i);
                if (s.IsEmpty) continue;
                if (s.item is FurnaceUpgradeItem u)
                {
                    speed *= Mathf.Pow(u.speedMultiplier, s.count);
                    eff   *= Mathf.Pow(u.efficiencyMultiplier, s.count);
                }
            }
            SpeedMultiplier      = speed;
            EfficiencyMultiplier = eff;
        }

        // ============================================================
        //                           RECIPE
        // ============================================================
        private IFluidStore Fluids() => new MachineFluidStore(FluidTanks);

        private ItemContainer[] InArr  => new[] { inputC };
        private ItemContainer[] OutArr => new[] { outputC };

        /// <summary>Player-selected recipe (from the UI). Null = auto-pick the first runnable.</summary>
        [System.NonSerialized] public ProcessingRecipe selectedRecipe;

        private ProcessingRecipe FindRecipe()
        {
            var fluids = Fluids();
            // If the player locked a recipe, only run that one.
            if (selectedRecipe != null)
                return ProcessingExecutor.CanRun(selectedRecipe, InArr, OutArr, fluids) ? selectedRecipe : null;
            for (int r = 0; r < knownRecipes.Count; r++)
            {
                var rec = knownRecipes[r];
                if (rec != null && ProcessingExecutor.CanRun(rec, InArr, OutArr, fluids)) return rec;
            }
            return null;
        }

        private void CompleteBatch()
        {
            // Run via the shared executor (handles both item + fluid I/O). If output
            // space vanished mid-batch, pause until it frees up.
            if (!ProcessingExecutor.Run(_current, InArr, OutArr, Fluids()))
            {
                _progress = EffectiveBatchTime(_current);
                return;
            }
            _progress = 0f;
            _current  = FindRecipe();
        }
    }
}
