// Assets/Scripts/VoxelEngine/Simulation/Assembler.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — ASSEMBLER MACHINE                            ║
// ║  Multi-input crafting machine. Takes ingots and components,     ║
// ║  produces higher-tier items (gears, circuits, motors, etc.).    ║
// ║  Implements IMachine for centralized tick + belt integration.   ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Assembler tier. Higher tiers have more input slots and faster speed.
    /// </summary>
    public enum AssemblerTier { Mk1, Mk2, Mk3 }

    /// <summary>
    /// Multi-input crafting machine. Matches recipes by checking all input
    /// slots against known recipe requirements. Produces one output per batch.
    /// </summary>
    [RequireComponent(typeof(PowerConsumer))]
    public class Assembler : MonoBehaviour, IMachine, IItemConsumer, IItemProvider
    {
        // ── Inspector ─────────────────────────────────────────────────

        [Header("Assembler Configuration")]
        public AssemblerTier tier = AssemblerTier.Mk1;
        public List<MachineRecipe> knownRecipes = new();

        [Header("Containers")]
        public ItemContainer inputC;
        public ItemContainer outputC;
        public ItemContainer upgradeC;

        [Header("Tuning")]
        public float baseWattsPerSecond = 300f;
        public float idleWattsPerSecond = 10f;

        // ── Tier Properties ───────────────────────────────────────────

        public int InputSlots   => tier switch { AssemblerTier.Mk2 => 6, AssemblerTier.Mk3 => 9, _ => 4 };
        public int OutputSlots  => tier switch { AssemblerTier.Mk2 => 6, AssemblerTier.Mk3 => 8, _ => 4 };
        public int UpgradeSlots => tier switch { AssemblerTier.Mk2 => 3, AssemblerTier.Mk3 => 4, _ => 2 };
        public float TierSpeedMultiplier => tier switch { AssemblerTier.Mk2 => 1.5f, AssemblerTier.Mk3 => 2.5f, _ => 1f };

        // ── Runtime ───────────────────────────────────────────────────

        private MachineRecipe _current;
        private float _processProgress;
        private PowerConsumer _power;
        private bool _userEnabled = true;
        private float _speedMult = 1f;
        private float _effMult = 1f;

        // ── IMachine ──────────────────────────────────────────────────

        public string MachineName => $"Assembler {tier}";
        public bool IsActive => _current != null && _processProgress > 0f;
        public bool IsOnline => _power != null && _power.IsPowered;
        public float Progress01 => _current == null ? 0f : _processProgress / EffectiveProcessTime(_current);
        public float CurrentWattage { get; private set; }
        public string CurrentRecipeId => _current != null ? _current.name : string.Empty;
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

        private void EnsureContainers()
        {
            int inSlots  = InputSlots;
            int outSlots = OutputSlots;
            int upSlots  = UpgradeSlots;

            if (inputC   == null) inputC   = new ItemContainer("Inputs",   inSlots);  else inputC.Resize(inSlots);
            if (outputC  == null) outputC  = new ItemContainer("Outputs",  outSlots); else outputC.Resize(outSlots);
            if (upgradeC == null) upgradeC = new ItemContainer("Upgrades", upSlots);  else upgradeC.Resize(upSlots);
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
            if (_current == null) _current = FindRecipeForInputs();
            _processProgress = _current != null
                ? Mathf.Clamp(progressSeconds, 0f, EffectiveProcessTime(_current))
                : 0f;
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

            float wantWattage = (_current != null) ? baseWattsPerSecond * _effMult : idleWattsPerSecond;
            CurrentWattage = wantWattage;
            if (_power != null) _power.wattsPerSecond = wantWattage;

            if (!IsOnline) return;

            // Find recipe.
            if (_current == null) _current = FindRecipeForInputs();
            if (_current == null)
            {
                _processProgress = 0f;
                return;
            }

            _processProgress += dt * _speedMult * TierSpeedMultiplier;
            if (_processProgress >= EffectiveProcessTime(_current))
                CompleteOneBatch();
        }

        private float EffectiveProcessTime(MachineRecipe r) => r.processSeconds;

        // ── IItemConsumer ─────────────────────────────────────────────

        public int GetInputCapacity(ItemDefinition item)
        {
            if (item == null) return 0;
            // Accept any item that matches a known recipe input.
            if (!IsKnownInput(item)) return 0;
            return inputC.HasSpace(item, 1) ? item.maxStack : 0;
        }

        public int TryInsert(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            var leftover = inputC.Insert(new ItemStack(item, count));
            return count - (leftover?.count ?? 0);
        }

        private bool IsKnownInput(ItemDefinition item)
        {
            foreach (var r in knownRecipes)
            {
                if (r == null || r.inputs == null) continue;
                foreach (var inp in r.inputs)
                    if (inp.item == item) return true;
            }
            return true; // accept all by default — assembler is flexible
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

        private MachineRecipe FindRecipeForInputs()
        {
            foreach (var r in knownRecipes)
            {
                if (r == null || r.inputs == null || r.inputs.Length == 0) continue;
                if (HasAllInputs(r)) return r;
            }
            return null;
        }

        private bool HasAllInputs(MachineRecipe recipe)
        {
            foreach (var inp in recipe.inputs)
            {
                if (inputC.CountOf(inp.item) < inp.count)
                    return false;
            }
            return true;
        }

        private void CompleteOneBatch()
        {
            if (_current == null) return;

            // Check output space.
            if (!outputC.HasSpace(_current.outputItem, _current.outputCount))
            {
                _processProgress = EffectiveProcessTime(_current);
                return;
            }

            // Consume all inputs.
            foreach (var inp in _current.inputs)
                inputC.Remove(inp.item, inp.count);

            // Produce output.
            outputC.Insert(new ItemStack(_current.outputItem, _current.outputCount));

            // Byproduct.
            if (_current.byproductItem != null && _current.byproductCount > 0)
            {
                if (Random.value <= _current.byproductChance)
                {
                    if (outputC.HasSpace(_current.byproductItem, _current.byproductCount))
                        outputC.Insert(new ItemStack(_current.byproductItem, _current.byproductCount));
                }
            }

            _processProgress = 0f;
            _current = FindRecipeForInputs();
        }
    }
}
