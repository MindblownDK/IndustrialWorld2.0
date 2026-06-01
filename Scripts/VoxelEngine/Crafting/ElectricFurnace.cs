// Assets/Scripts/VoxelEngine/Crafting/ElectricFurnace.cs
//
// Power-driven smelter:
//   * 1 input slot + 4 OUTPUT slots (parallel batch output)
//   * No fuel slot — power comes from a co-located PowerConsumer
//   * Online/Offline status driven by power network
//   * Accepts upgrade modules (Speed / Efficiency) via dedicated upgrade slots

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.Crafting
{
    [RequireComponent(typeof(CraftingStation))]
    public class ElectricFurnace : MonoBehaviour
    {
        [Header("Recipes")]
        public List<SmeltingRecipe> knownRecipes = new();

        [Header("Containers (auto-created)")]
        public ItemContainer inputC;
        public ItemContainer outputC;
        public ItemContainer upgradeC;     // 4 slots — accepts Speed / Efficiency upgrades

        [Header("Tuning")]
        [Tooltip("Base watts/s drawn while smelting. Modified by efficiency upgrades.")]
        public float baseWattsPerSecond = 200f;
        [Tooltip("Watts/s drawn while idle (heating element kept warm).")]
        public float idleWattsPerSecond = 5f;

        // Runtime
        private SmeltingRecipe _current;
        private float _smeltProgress;
        private PowerConsumer _power;
        private CraftingStation _station;

        public float SmeltProgress01     => _current == null ? 0 : _smeltProgress / EffectiveSmeltTime(_current);
        public float CurrentWattage      { get; private set; }
        public bool  IsOnline            => _power != null && _power.IsPowered;
        public SmeltingRecipe Current    => _current;
        public float SpeedMultiplier     { get; private set; } = 1f;
        public float EfficiencyMultiplier{ get; private set; } = 1f;

        public const int OUTPUT_SLOTS  = 4;
        public const int UPGRADE_SLOTS = 4;

        private void Awake()
        {
            EnsureContainers();

            _station = GetComponent<CraftingStation>();
            _power   = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            _power.connectRadius = 1.6f;

            upgradeC.OnChanged += RecalculateUpgrades;
            RecalculateUpgrades();
        }

        private void OnDestroy()
        {
            if (upgradeC != null) upgradeC.OnChanged -= RecalculateUpgrades;
        }

        public void EnsureContainers()
        {
            if (inputC   == null) inputC   = new ItemContainer("Input",   1);             else inputC.Resize(1);
            if (outputC  == null) outputC  = new ItemContainer("Outputs", OUTPUT_SLOTS);  else outputC.Resize(OUTPUT_SLOTS);
            if (upgradeC == null) upgradeC = new ItemContainer("Upgrades",UPGRADE_SLOTS); else upgradeC.Resize(UPGRADE_SLOTS);
        }

        private void Update()
        {
            EnsureContainers();
            // Drive the PowerConsumer's draw — idle if not smelting, full when active.
            float wantWattage = (_current != null) ? baseWattsPerSecond * EfficiencyMultiplier : idleWattsPerSecond;
            CurrentWattage = wantWattage;
            if (_power != null) _power.wattsPerSecond = wantWattage;

            // Pause if offline.
            if (!IsOnline) return;

            // Pick a recipe matching the current input.
            if (_current == null) _current = FindRecipeForInput();
            if (_current == null) { _smeltProgress = 0; return; }

            _smeltProgress += Time.deltaTime * SpeedMultiplier;
            if (_smeltProgress >= EffectiveSmeltTime(_current))
                CompleteOneBatch();
        }

        private float EffectiveSmeltTime(SmeltingRecipe r) => r.smeltSeconds; // SpeedMultiplier already accelerates progress

        // ============================================================
        //                       Upgrades
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
        //                       Smelting
        // ============================================================
        private SmeltingRecipe FindRecipeForInput()
        {
            var slot = inputC.GetSlot(0);
            if (slot.IsEmpty) return null;
            foreach (var r in knownRecipes)
            {
                if (r == null || r.input == null) continue;
                if (r.input == slot.item && slot.count >= r.inputCount) return r;
            }
            return null;
        }

        private void CompleteOneBatch()
        {
            // Verify any output slot has space.
            if (!outputC.HasSpace(_current.output, _current.outputCount))
            {
                _smeltProgress = EffectiveSmeltTime(_current); // pause until output drained
                return;
            }
            inputC.Remove(_current.input, _current.inputCount);
            outputC.Insert(new ItemStack(_current.output, _current.outputCount));
            _smeltProgress = 0f;
            _current = FindRecipeForInput();
        }
    }
}
