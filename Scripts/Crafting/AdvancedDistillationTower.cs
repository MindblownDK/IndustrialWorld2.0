// Assets/Scripts/VoxelEngine/Crafting/AdvancedDistillationTower.cs
//
// ADVANCED DISTILLATION TOWER (9.38.0-dev) — the petroleum-era machine where
// crude oil is actually converted. The Oil Refinery keeps its legacy role
// (Refined Oil, Heavy Fuel Oil, MGO and the refined-oil plastic recipe); this
// big plant is the dedicated fractionating column the design wanted as its own
// block: one feed tank plus six typed cut tanks, so each cut is readable by its
// own pipe run and the fractions can never mix.
//
// Layout:
//   * 2 input slots / 4 output slots (item side, for future item recipes)
//   * 1 feed fluid tank + 6 typed fraction tanks, in FractionSpecs order.
//     The feed is auto-typed: it adopts whichever feed liquid is poured first
//     (crude oil for the Atmospheric Cut, refined oil for the Re-Run), and the
//     fluid store NEVER routes outputs into it — a cut can never contaminate
//     an empty feed tank.
//   * 7 world sight-gauge pivots (Transform list, authored on the prefab by
//     Setup Step 69): index 0 = feed, 1..6 = the cuts. Each pivot's localScale.y
//     is driven by its tank's fill each frame, so the coloured columns on the
//     tower model rise and fall with the tanks they stand for.
//   * Co-located PowerConsumer (auto-added in Awake).
//
// Behaviour:
//   * Each tick picks the first recipe in knownRecipes where ALL inputs are
//     present and every output has at least one slot with space. Output liquids
//     are routed by TowerFluidStore, which only fills typed cut tanks, so a cut
//     that is full back-pressures the batch instead of spilling anywhere else.
//   * Consumes inputs at batch start, produces outputs at batch end.

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
    public class AdvancedDistillationTower : MonoBehaviour, IItemPortHost
    {
        public const int INPUT_SLOTS  = 2;
        public const int OUTPUT_SLOTS = 4;

        /// <summary>The column's six cuts, in fill/draw order.</summary>
        public static readonly (LiquidType liquid, string label, float capacityL)[] FractionSpecs =
        {
            (LiquidType.Lpg,            "LPG",             120f),
            (LiquidType.Naphtha,        "Naphtha",         500f),
            (LiquidType.Kerosene,       "Kerosene",        600f),
            (LiquidType.Diesel,         "Diesel",         1000f),
            (LiquidType.Gasoline,       "Gasoline",        800f),
            (LiquidType.HeavyFuelOil,   "Heavy Fuel Oil", 1000f),
        };

        [Header("Recipes")]
        public List<ProcessingRecipe> knownRecipes = new();

        [Header("Containers (auto-created)")]
        public ItemContainer inputC;
        public ItemContainer outputC;

        [Header("Fluid Tanks")]
        [Tooltip("Feed tank. Auto-typed: adopts crude oil for the Atmospheric Cut or refined oil for the Re-Run cut — whichever is poured in first while empty. Output routing never fills it.")]
        public MachineFluidTank feed = new MachineFluidTank("Feed Tank", 2000f, LiquidType.CrudeOil, autoType: true);
        [Tooltip("One typed tank per column cut, in FractionSpecs order. Ensured at runtime and authored on the prefab by Setup Step 69; never rebuilt over a tuned tank.")]
        public List<MachineFluidTank> cutTanks = new();

        [Header("World Sight Gauges")]
        [Tooltip("Fill pivots on the tower model: index 0 = feed, 1..6 = the six cuts (FractionSpecs order). Each pivot's Y scale is driven by its tank's fill so the coloured sight columns rise and fall with the tanks. Authored by Setup Step 69.")]
        public List<Transform> gaugePivots = new();

        private MachineFluidTank[] _allTanks;   // feed + 6 cuts, cached
        private TowerFluidStore _fluidStore;

        public IReadOnlyList<MachineFluidTank> FluidTanks => AllTanks();

        [Header("Tuning")]
        [Tooltip("Base watts/s drawn while a batch is in progress. Multiplied by recipe.powerDrawMultiplier.")]
        public float baseWattsPerSecond = 500f;
        [Tooltip("Watts/s drawn while idle (keeps the column hot).")]
        public float idleWattsPerSecond = 40f;

        // Runtime
        private ProcessingRecipe _current;
        private float _progress;
        private PowerConsumer _power;

        public float Progress01           => _current == null ? 0 : _progress / EffectiveBatchTime(_current);
        public ProcessingRecipe Current   => _current;
        public bool  IsOnline             => _power != null && _power.IsPowered;
        public float CurrentWattage       { get; private set; }

        /// <summary>Player-selected recipe (from the UI). Null = auto-pick the first runnable.</summary>
        [System.NonSerialized] public ProcessingRecipe selectedRecipe;

        private void Awake()
        {
            EnsureContainers();
            if (knownRecipes == null) knownRecipes = new List<ProcessingRecipe>();
            EnsureTanks();
            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            _power.connectRadius = 2.6f;
        }

        public void EnsureContainers()
        {
            if (inputC  == null) inputC  = new ItemContainer("Inputs",  INPUT_SLOTS);   else inputC.Resize(INPUT_SLOTS);
            if (outputC == null) outputC = new ItemContainer("Outputs", OUTPUT_SLOTS);  else outputC.Resize(OUTPUT_SLOTS);
        }

        /// <summary>Make sure the feed tank and the six typed cut tanks exist.
        /// Existing entries are kept as tuned — only missing slots are created.</summary>
        public void EnsureTanks()
        {
            if (feed == null)
                feed = new MachineFluidTank("Feed Tank", 2000f, LiquidType.CrudeOil, autoType: true);
            if (cutTanks == null) cutTanks = new List<MachineFluidTank>();
            for (int i = cutTanks.Count; i < FractionSpecs.Length; i++)
                cutTanks.Add(null);
            for (int i = 0; i < FractionSpecs.Length; i++)
            {
                if (cutTanks[i] == null)
                    cutTanks[i] = new MachineFluidTank(FractionSpecs[i].label,
                        FractionSpecs[i].capacityL, FractionSpecs[i].liquid, autoType: false);
            }
            _allTanks = null;
        }

        private MachineFluidTank[] AllTanks()
        {
            if (_allTanks != null) return _allTanks;
            EnsureTanks();
            _allTanks = new MachineFluidTank[1 + FractionSpecs.Length];
            _allTanks[0] = feed;
            for (int i = 0; i < FractionSpecs.Length; i++)
                _allTanks[1 + i] = cutTanks[i];
            return _allTanks;
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
                ? baseWattsPerSecond * _current.powerDrawMultiplier
                : idleWattsPerSecond;
            CurrentWattage = wantWatts;
            if (_power != null) _power.wattsPerSecond = wantWatts;

            // Drive the world sight gauges from the tanks they represent.
            UpdateGauges();

            if (!IsOnline) return;

            if (_current == null) _current = FindRecipe();
            if (_current == null) { _progress = 0; return; }

            _progress += Time.deltaTime;
            if (_progress >= EffectiveBatchTime(_current))
                CompleteBatch();
        }

        private void UpdateGauges()
        {
            if (gaugePivots == null || gaugePivots.Count == 0) return;
            var tanks = AllTanks();
            int n = Mathf.Min(gaugePivots.Count, tanks.Length);
            for (int i = 0; i < n; i++)
            {
                var pivot = gaugePivots[i];
                if (pivot == null || tanks[i] == null) continue;
                float f = Mathf.Clamp01(tanks[i].Fill01);
                if (f < 0.001f) f = 0f;
                pivot.localScale = new Vector3(1f, f, 1f);
            }
        }

        private float EffectiveBatchTime(ProcessingRecipe r)
            => Mathf.Max(0.1f, r.secondsPerBatch);

        // ── RECIPE ──────────────────────────────────────────────────────────
        private IFluidStore Fluids()
        {
            if (_fluidStore == null) _fluidStore = new TowerFluidStore();
            _fluidStore.Bind(AllTanks());
            return _fluidStore;
        }

        /// <summary>
        /// Fluid routing for the column: the feed tank only ever supplies recipe
        /// inputs, and outputs only ever land in the six typed cut tanks — never
        /// back in the feed. An over-full cut therefore back-pressures the batch
        /// instead of contaminating the feed or mixing fractions.
        /// </summary>
        private sealed class TowerFluidStore : IFluidStore
        {
            private MachineFluidTank _feed;
            private readonly List<MachineFluidTank> _cuts = new();

            public void Bind(MachineFluidTank[] tanks)
            {
                _feed = tanks != null && tanks.Length > 0 ? tanks[0] : null;
                _cuts.Clear();
                if (tanks == null) return;
                for (int i = 1; i < tanks.Length; i++)
                    if (tanks[i] != null) _cuts.Add(tanks[i]);
            }

            public float Available(LiquidType type)
            {
                float n = 0f;
                if (_feed != null && _feed.liquid == type) n += _feed.stored;
                foreach (var t in _cuts) if (t.liquid == type) n += t.stored;
                return n;
            }

            public float SpaceFor(LiquidType type)
            {
                float n = 0f;
                foreach (var t in _cuts) n += t.SpaceFor(type);
                return n;
            }

            public float Draw(LiquidType type, float litres)
            {
                float drawn = 0f;
                if (_feed != null && _feed.liquid == type) drawn += _feed.Remove(litres);
                foreach (var t in _cuts)
                {
                    if (drawn >= litres) break;
                    if (t.liquid == type) drawn += t.Remove(litres - drawn);
                }
                return drawn;
            }

            public float Fill(LiquidType type, float litres)
            {
                float filled = 0f;
                foreach (var t in _cuts)
                {
                    if (filled >= litres) break;
                    filled += t.Add(type, litres - filled);
                }
                return filled;
            }
        }

        private ItemContainer[] InArr  => new[] { inputC };
        private ItemContainer[] OutArr => new[] { outputC };

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
