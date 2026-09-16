// Assets/Scripts/VoxelEngine/Crafting/Furnace.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Transport;

namespace VoxelEngine.Crafting
{
    /// <summary>
    /// Three-slot smelter:  [Input] + [Fuel]  →  [Output]
    /// Auto-smelts as long as input has a matching SmeltingRecipe and fuel is available.
    /// Exposes its containers to the shared item-port system via <see cref="IItemPortHost"/>.
    /// </summary>
    [RequireComponent(typeof(CraftingStation))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class Furnace : MonoBehaviour, IItemPortHost, IMachineProcessState
    {
        [Header("Recipes")]
        public List<SmeltingRecipe> knownRecipes = new();

        [Header("Containers (auto-created)")]
        public ItemContainer inputC;
        public ItemContainer fuelC;
        public ItemContainer outputC;

        // Runtime state
        private SmeltingRecipe _current;
        private float _smeltProgress;
        private float _fuelRemaining;
        private float _fuelMaxDuration;     // duration of the last consumed fuel item (for UI bar)

        public float SmeltProgress01 => _current == null ? 0 : _smeltProgress / _current.smeltSeconds;
        public float FuelRemaining     => _fuelRemaining;
        public float FuelMaxDuration   => _fuelMaxDuration;
        public float FuelProgress01    => _fuelMaxDuration > 0 ? Mathf.Clamp01(_fuelRemaining / _fuelMaxDuration) : 0;
        public bool  IsBurning       => _fuelRemaining > 0f;
        public SmeltingRecipe Current => _current;

        private void Awake()
        {
            EnsureContainers();
        }

        // Public so the UI controller can call it before reading slots — defends against
        // serialized scene instances that were created before slot containers existed.
        public void EnsureContainers()
        {
            if (inputC  == null) inputC  = new ItemContainer("Input",  1);
            else inputC.Resize(1);
            if (fuelC   == null) fuelC   = new ItemContainer("Fuel",   1);
            else fuelC.Resize(1);
            if (outputC == null) outputC = new ItemContainer("Output", 1);
            else outputC.Resize(1);
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
            _portContainers ??= new ItemPortContainer[3];
            _portContainers[0] = new ItemPortContainer("Input",  inputC,  canInput: true,  canOutput: false);
            _portContainers[1] = new ItemPortContainer("Fuel",   fuelC,   canInput: true,  canOutput: false);
            _portContainers[2] = new ItemPortContainer("Output", outputC, canInput: false, canOutput: true);
            return _portContainers;
        }

        // Optional power requirement. If a PowerConsumer is on the same GameObject AND
        // it reports !IsPowered, the furnace pauses smelting until the network supplies enough.
        private VoxelEngine.Power.PowerConsumer _powerReq;

        private void Awake_Power()
        {
            _powerReq = GetComponent<VoxelEngine.Power.PowerConsumer>();
        }

        private void Update()
        {
            EnsureContainers();  // belt-and-suspenders
            float dt = Time.deltaTime;

            if (_powerReq == null) _powerReq = GetComponent<VoxelEngine.Power.PowerConsumer>();
            if (_powerReq != null && !_powerReq.IsPowered)
            {
                // A brownout stops the batch before anything else is even looked at, so
                // this has to name itself — otherwise a furnace on a weak network looks
                // exactly like a broken one.
                ReportStallChange();
                return;
            }

            // Pick a recipe matching the current input.
            if (_current == null) _current = FindRecipeForInput();

            if (_current == null) { _smeltProgress = 0; ReportStallChange(); return; }

            // Need fuel.
            if (_fuelRemaining <= 0f)
            {
                if (!TryConsumeFuel()) { ReportStallChange(); return; }
            }
            ReportStallChange();

            // Burn fuel.
            _fuelRemaining -= dt;

            // Make progress.
            _smeltProgress += dt;
            if (_smeltProgress >= _current.smeltSeconds)
            {
                CompleteOneBatch();
            }
        }

        /// <summary>Log the stall reason the first time it appears and whenever it changes.</summary>
        private void ReportStallChange()
        {
            string reason = StallReason;
            if (reason == _lastStallReason) return;
            _lastStallReason = reason;
            if (string.IsNullOrEmpty(reason)) return;
            int inputCount = inputC != null ? inputC.GetSlot(0).count : 0;
            string inputName = inputC != null && inputC.GetSlot(0).item != null ? inputC.GetSlot(0).item.name : "nothing";
            int assigned = knownRecipes != null ? knownRecipes.Count : 0;
            int broken = CountBrokenRecipes();
            string recipes = assigned == 0
                ? "no smelting recipe is assigned to this furnace — run the crafting content setup step"
                : broken > 0
                    ? $"{assigned} smelting recipe(s) assigned but {broken} of them have no input or output item, so they can never match — re-run the crafting content setup step to repair the links"
                    : $"{assigned} smelting recipe(s) assigned, all linked";
            Debug.Log($"[Furnace] Not smelting: {reason}. Input slot holds {inputCount} x {inputName}; {recipes}.");
        }

        /// <summary>
        /// How many assigned recipes could never match anything: a recipe with no input
        /// item is skipped by <see cref="FindRecipeForInput"/>, and one with no output
        /// could not complete a batch. This is the difference between "this furnace was
        /// never given recipes" and "its recipes lost their links" — the first needs them
        /// assigned, the second needs the authoring step re-run.
        /// </summary>
        private int CountBrokenRecipes()
        {
            if (knownRecipes == null) return 0;
            int broken = 0;
            foreach (var r in knownRecipes)
                if (r == null || r.input == null || r.output == null) broken++;
            return broken;
        }

        /// <summary>
        /// True when at least one assigned recipe lost its input or output link. The panel
        /// uses this to tell the player which setup step repairs it instead of showing a
        /// bare "no recipe" line.
        /// </summary>
        public bool HasBrokenRecipes => CountBrokenRecipes() > 0;

        private SmeltingRecipe FindRecipeForInput()
        {
            var slot = inputC.GetSlot(0);
            if (slot.IsEmpty) return null;
            foreach (var r in knownRecipes)
            {
                if (r == null || r.input == null) continue;
                // Identity, not reference: the same logical ore exists as more than one
                // asset in the project, and a stack from the "other" one must still smelt.
                if (ItemIdentity.Same(r.input, slot.item) && slot.count >= r.inputCount) return r;
            }
            return null;
        }

        private bool TryConsumeFuel()
        {
            var s = fuelC.GetSlot(0);
            if (s.IsEmpty) return false;
            if (s.item is ResourceItem ri && ri.fuelSeconds > 0f)
            {
                _fuelRemaining = ri.fuelSeconds;
                _fuelMaxDuration = ri.fuelSeconds;
                fuelC.Remove(ri, 1);
                return true;
            }
            return false;
        }

        // ============================================================
        //        10.2.0-dev — batch, progress and burning fuel survive a reload
        // ============================================================
        // Without this the furnace came back cold and mid-batch: the batch in
        // progress was lost, and with it the fuel item already consumed for it —
        // a reload quietly refunded nothing and threw away the burn. The record
        // is additive: a save written before this round has no payload, so the
        // furnace loads exactly as it did before, free to pick its own recipe.
        private const string FuelRemainingKey = "fuelRemaining";
        private const string FuelMaxKey       = "fuelMaxDuration";

        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            state.recipeName = _current != null ? _current.name : string.Empty;
            state.progressSeconds = Mathf.Max(0f, _smeltProgress);
            state.SetExtra(FuelRemainingKey, Mathf.Max(0f, _fuelRemaining));
            state.SetExtra(FuelMaxKey, Mathf.Max(0f, _fuelMaxDuration));
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null || state.IsEmpty) return;

            // The record was written from a live batch, so its recipe should still be
            // the one this input implies. When it is not — the input was pulled out
            // before the save, or the recipe list changed — the furnace re-picks
            // instead of resuming a batch it no longer has the material for.
            var recipe = MachineProcessPersistence.Resolve(knownRecipes, state.recipeName, "Furnace");
            _current = recipe != null ? recipe : FindRecipeForInput();

            float seconds = _current != null ? _current.smeltSeconds : 0f;
            _smeltProgress = MachineProcessPersistence.ClampOr(state.progressSeconds, 0f, seconds, 0f);

            // The fuel bar only means something against the duration of the fuel item
            // that was burned for it, so both halves come back together and the
            // remaining burn can never outlive them.
            float maxDuration = Mathf.Max(0f, MachineProcessPersistence.FiniteOr(state.GetExtra(FuelMaxKey, 0f), 0f));
            float remaining = Mathf.Max(0f, MachineProcessPersistence.FiniteOr(state.GetExtra(FuelRemainingKey, 0f), 0f));
            _fuelMaxDuration = maxDuration;
            _fuelRemaining = Mathf.Min(remaining, maxDuration);
        }

        // ============================================================
        //        11.0.0-dev — say why the furnace is not smelting
        // ============================================================
        // A furnace that does nothing gives the player four different causes with no
        // way to tell them apart: no recipe for the input, no burnable fuel, no power,
        // or a full output. This names the one that is actually stopping it, for the
        // panel to show and for the log to report once per change.
        private string _lastStallReason = string.Empty;

        /// <summary>Why the furnace is not making progress right now. Empty when it is smelting.</summary>
        public string StallReason
        {
            get
            {
                if (_powerReq != null && !_powerReq.IsPowered) return "No power";
                if (inputC == null || inputC.GetSlot(0).IsEmpty) return "No input";
                if (_current == null) return "No recipe for this input";
                if (_fuelRemaining <= 0f)
                {
                    var fuel = fuelC != null ? fuelC.GetSlot(0) : default;
                    if (fuel.IsEmpty) return "No fuel";
                    if (!(fuel.item is ResourceItem ri) || ri.fuelSeconds <= 0f)
                        return "Fuel slot holds something that does not burn";
                }
                if (_current != null && !outputC.HasSpace(_current.output, _current.outputCount))
                    return "Output full";
                return string.Empty;
            }
        }

        private void CompleteOneBatch()
        {
            // Make sure output fits.
            if (!outputC.HasSpace(_current.output, _current.outputCount))
            {
                _smeltProgress = _current.smeltSeconds; // pause until output drained
                return;
            }
            inputC.Remove(_current.input, _current.inputCount);
            outputC.Insert(new ItemStack(_current.output, _current.outputCount));
            _smeltProgress = 0f;
            _current = FindRecipeForInput();
        }
    }
}
