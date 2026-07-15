// Assets/Scripts/VoxelEngine/Simulation/Crusher.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — CRUSHER MACHINE                              ║
// ║  Crushes stone → gravel, ore → dust for bonus yield.            ║
// ║  Implements IMachine for the centralized tick system.           ║
// ║  Implements IItemConsumer/IItemProvider for belt integration.    ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Ore crusher / grinder. Takes raw ore or stone and produces
    /// crushed variants with a chance of bonus output (dust).
    /// </summary>
    [RequireComponent(typeof(PowerConsumer))]
    public class Crusher : MonoBehaviour, IMachine, IItemConsumer, IItemProvider
    {
        // ── Inspector ─────────────────────────────────────────────────

        [Header("Recipes")]
        public List<MachineRecipe> knownRecipes = new();

        [Header("Containers")]
        public ItemContainer inputC;
        public ItemContainer outputC;
        public ItemContainer upgradeC;

        [Header("Tuning")]
        [Tooltip("Base watts drawn while crushing.")]
        public float baseWattsPerSecond = 250f;
        [Tooltip("Watts drawn while idle.")]
        public float idleWattsPerSecond = 8f;

        [Header("Slots")]
        public const int INPUT_SLOTS   = 1;
        public const int OUTPUT_SLOTS  = 4;
        public const int UPGRADE_SLOTS = 2;

        // ── Runtime ───────────────────────────────────────────────────

        private MachineRecipe _current;
        private MachineRecipe _selectedRecipe;
        private float _processProgress;
        private PowerConsumer _power;
        private bool _userEnabled = true;
        private float _speedMultiplier = 1f;
        private float _efficiencyMultiplier = 1f;

        // ── IMachine ──────────────────────────────────────────────────

        public string MachineName => "Crusher";
        public bool IsActive => _current != null && _processProgress > 0f;
        public bool IsOnline => _power != null && _power.IsPowered;
        public float Progress01 => _current == null ? 0f : _processProgress / EffectiveProcessTime(_current);
        public float CurrentWattage { get; private set; }
        public string CurrentRecipeId => _current != null ? _current.name : (_selectedRecipe != null ? _selectedRecipe.name : string.Empty);
        public MachineRecipe CurrentRecipe => _current != null ? _current : _selectedRecipe;
        public float ProcessProgressSeconds => _processProgress;
        public bool UserEnabled
        {
            get => _userEnabled;
            set => _userEnabled = value;
        }

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            EnsureContainers();

            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            _power.connectRadius = 1.6f;

            // Register with simulation tick manager.
            SimulationTickManager.EnsureInstance();
        }

        private void OnEnable()
        {
            SimulationTickManager.Instance?.Register(this, this);
        }

        private void OnDisable()
        {
            SimulationTickManager.Instance?.Unregister(this);
        }

        public void EnsureContainers()
        {
            if (inputC   == null) inputC   = new ItemContainer("Input",    INPUT_SLOTS);    else inputC.Resize(INPUT_SLOTS);
            if (outputC  == null) outputC  = new ItemContainer("Outputs",  OUTPUT_SLOTS);   else outputC.Resize(OUTPUT_SLOTS);
            if (upgradeC == null) upgradeC = new ItemContainer("Upgrades", UPGRADE_SLOTS);  else upgradeC.Resize(UPGRADE_SLOTS);
        }

        /// <summary>Restores additive machine state after its containers are loaded.</summary>
        public void RestorePersistentState(string recipeId, float progressSeconds, bool userEnabled)
        {
            EnsureContainers();
            _userEnabled = userEnabled;
            _current = null;
            if (!string.IsNullOrEmpty(recipeId))
            {
                foreach (var recipe in knownRecipes)
                {
                    if (recipe != null && recipe.name == recipeId)
                    {
                        _current = recipe;
                        break;
                    }
                }
            }
            _selectedRecipe = _current;
            if (_current == null) _current = FindRecipeForInput();
            _processProgress = _current != null
                ? Mathf.Clamp(progressSeconds, 0f, EffectiveProcessTime(_current))
                : 0f;
        }

        // ── Simulation Tick ───────────────────────────────────────────

        public void SimulationTick(float dt)
        {
            EnsureContainers();

            // Disabled — draw nothing.
            if (!_userEnabled)
            {
                CurrentWattage = 0f;
                if (_power != null) _power.wattsPerSecond = 0f;
                return;
            }

            // Power draw.
            float wantWattage = (_current != null) ? baseWattsPerSecond * _efficiencyMultiplier : idleWattsPerSecond;
            CurrentWattage = wantWattage;
            if (_power != null) _power.wattsPerSecond = wantWattage;

            // No power — pause.
            if (!IsOnline) return;

            // Find recipe if idle.
            if (_current == null) _current = FindRecipeForInput();
            if (_current == null)
            {
                _processProgress = 0f;
                return;
            }

            // Advance processing.
            _processProgress += dt * _speedMultiplier;
            if (_processProgress >= EffectiveProcessTime(_current))
                CompleteOneBatch();
        }

        private float EffectiveProcessTime(MachineRecipe r) => r != null ? Mathf.Max(0.05f, r.processSeconds) : 0.05f;

        public void SelectRecipe(MachineRecipe recipe)
        {
            if (recipe != null && !knownRecipes.Contains(recipe)) return;
            if (_selectedRecipe == recipe) return;
            _selectedRecipe = recipe;
            _current = null;
            _processProgress = 0f;
        }

        // ── IItemConsumer ─────────────────────────────────────────────

        public int GetInputCapacity(ItemDefinition item)
        {
            if (item == null) return 0;
            return inputC.HasSpace(item, 1) ? item.maxStack : 0;
        }

        public int TryInsert(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            var leftover = inputC.Insert(new ItemStack(item, count));
            return count - (leftover?.count ?? 0);
        }

        // ── IItemProvider ─────────────────────────────────────────────

        public ItemDefinition PeekOutput(out int count)
        {
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
            return outputC.Remove(item, count);
        }

        // ── Recipe Matching ───────────────────────────────────────────

        private MachineRecipe FindRecipeForInput()
        {
            var slot = inputC.GetSlot(0);
            if (slot.IsEmpty || slot.item == null) return null;

            if (_selectedRecipe != null && RecipeMatchesInput(_selectedRecipe, slot.item, slot.count))
                return _selectedRecipe;

            foreach (var r in knownRecipes)
            {
                if (r == null) continue;
                if (r.inputs == null || r.inputs.Length == 0) continue;
                if (RecipeMatchesInput(r, slot.item, slot.count))
                    return r;
            }
            return null;
        }

        private static bool RecipeMatchesInput(MachineRecipe r, ItemDefinition item, int count)
        {
            return r != null
                && r.inputs != null
                && r.inputs.Length > 0
                && r.inputs[0].item == item
                && count >= r.inputs[0].count;
        }

        private void CompleteOneBatch()
        {
            if (_current == null) return;

            // Check output space.
            if (!outputC.HasSpace(_current.outputItem, _current.outputCount))
            {
                _processProgress = EffectiveProcessTime(_current); // pause until drained
                return;
            }

            // Consume inputs.
            foreach (var inp in _current.inputs)
                inputC.Remove(inp.item, inp.count);

            // Produce primary output.
            outputC.Insert(new ItemStack(_current.outputItem, _current.outputCount));

            // Byproduct chance.
            if (_current.byproductItem != null && _current.byproductCount > 0)
            {
                if (Random.value <= _current.byproductChance)
                {
                    if (outputC.HasSpace(_current.byproductItem, _current.byproductCount))
                        outputC.Insert(new ItemStack(_current.byproductItem, _current.byproductCount));
                }
            }

            _processProgress = 0f;
            _current = FindRecipeForInput();
        }
    }
}
