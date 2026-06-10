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
using VoxelEngine.Transport;

namespace VoxelEngine.Crafting
{
    [RequireComponent(typeof(CraftingStation))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class ElectricFurnace : MonoBehaviour, IItemPortHost
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

        /// <summary>
        /// Player-controlled hard-disable toggle. When false, the furnace
        /// stops drawing power and pauses smelting. Exposed via the UI's
        /// ENABLED pill to the left of the status badge.
        /// </summary>
        public bool userEnabled = true;

        [Tooltip("When on, the furnace auto-pulls smeltable items from nearby chests/containers into its input.")]
        public bool autoPull = false;
        [Tooltip("Radius (m) to scan for source containers when auto-pull is on.")]
        public float autoPullRadius = 4f;
        private float _pullTimer;

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
            _portContainers[0] = new ItemPortContainer("Input",   inputC,  canInput: true,  canOutput: false);
            _portContainers[1] = new ItemPortContainer("Outputs", outputC, canInput: false, canOutput: true);
            return _portContainers;
        }

        private void Update()
        {
            EnsureContainers();

            // Player has hard-disabled the furnace — draw nothing, smelt nothing.
            if (!userEnabled)
            {
                CurrentWattage = 0f;
                if (_power != null) _power.wattsPerSecond = 0f;
                return;
            }

            // Drive the PowerConsumer's draw — idle if not smelting, full when active.
            float wantWattage = (_current != null) ? baseWattsPerSecond * EfficiencyMultiplier : idleWattsPerSecond;
            CurrentWattage = wantWattage;
            if (_power != null) _power.wattsPerSecond = wantWattage;

            // Pause if offline.
            if (!IsOnline) return;

            // Auto-pull smeltable items from nearby containers into the input slot.
            if (autoPull)
            {
                _pullTimer += Time.deltaTime;
                if (_pullTimer >= 0.5f) { _pullTimer = 0f; AutoPullSmeltables(); }
            }

            // Pick a recipe matching the current input.
            if (_current == null) _current = FindRecipeForInput();
            if (_current == null) { _smeltProgress = 0; return; }

            _smeltProgress += Time.deltaTime * SpeedMultiplier;
            if (_smeltProgress >= EffectiveSmeltTime(_current))
                CompleteOneBatch();
        }

        private float EffectiveSmeltTime(SmeltingRecipe r) => r.smeltSeconds; // SpeedMultiplier already accelerates progress

        // Pull smeltable items from nearby chests into the input slot (auto-pull mode).
        private void AutoPullSmeltables()
        {
            var slot = inputC.GetSlot(0);
            // If the input already holds something the furnace can smelt, only top it up.
            ItemDefinition wanted = !slot.IsEmpty ? slot.item : null;

            var chests = Physics.OverlapSphere(transform.position, autoPullRadius);
            foreach (var col in chests)
            {
                var chest = col.GetComponentInParent<VoxelEngine.Building.Chest>();
                if (chest == null || chest.container == null) continue;

                for (int i = 0; i < chest.container.Size; i++)
                {
                    var s = chest.container.GetSlot(i);
                    if (s == null || s.IsEmpty || s.item == null) continue;
                    if (wanted != null && s.item != wanted) continue;
                    if (!IsSmeltable(s.item)) continue;
                    if (!inputC.HasSpace(s.item, 1)) return;

                    int take = Mathf.Min(s.count, s.item.maxStack);
                    int moved = chest.container.Remove(s.item, take);
                    if (moved > 0)
                    {
                        var leftover = inputC.Insert(new ItemStack(s.item, moved));
                        if (leftover != null && !leftover.IsEmpty) chest.container.Insert(leftover);
                        return; // one transfer per tick
                    }
                }
            }
        }

        private bool IsSmeltable(ItemDefinition item)
        {
            foreach (var r in knownRecipes)
                if (r != null && r.input == item) return true;
            return false;
        }

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
