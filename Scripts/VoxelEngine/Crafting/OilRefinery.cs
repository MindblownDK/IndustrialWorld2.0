// Assets/Scripts/VoxelEngine/Crafting/OilRefinery.cs
//
// Industrial multi-recipe processor. Modelled on ElectricFurnace but built
// around ProcessingRecipe (N inputs / M outputs).
//
// Layout:
//   * 2 input slots
//   * 4 output slots
//   * 2 upgrade slots (Speed / Efficiency, same item type as ElectricFurnace)
//   * Co-located PowerConsumer (auto-added in Awake)
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

        [Header("Tuning")]
        [Tooltip("Base watts/s drawn while a batch is in progress. Multiplied by recipe.powerDrawMultiplier and efficiency upgrades.")]
        public float baseWattsPerSecond = 400f;
        [Tooltip("Watts/s drawn while idle (keeps the cracking column hot).")]
        public float idleWattsPerSecond = 20f;

        // Runtime
        private ProcessingRecipe _current;
        private float _progress;
        private PowerConsumer _power;

        public float Progress01            => _current == null ? 0 : _progress / EffectiveBatchTime(_current);
        public ProcessingRecipe Current    => _current;
        public bool  IsOnline              => _power != null && _power.IsPowered;
        public float CurrentWattage        { get; private set; }
        public float SpeedMultiplier       { get; private set; } = 1f;
        public float EfficiencyMultiplier  { get; private set; } = 1f;

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
        private ProcessingRecipe FindRecipe()
        {
            for (int r = 0; r < knownRecipes.Count; r++)
            {
                var rec = knownRecipes[r];
                if (rec == null) continue;
                if (!HasAllInputs(rec)) continue;
                if (!HasOutputSpace(rec)) continue;
                return rec;
            }
            return null;
        }

        private bool HasAllInputs(ProcessingRecipe r)
        {
            if (r.inputs == null) return false;
            foreach (var ing in r.inputs)
            {
                if (ing.item == null || ing.count <= 0) continue;
                if (inputC.CountOf(ing.item) < ing.count) return false;
            }
            return true;
        }

        private bool HasOutputSpace(ProcessingRecipe r)
        {
            if (r.outputs == null) return true;
            foreach (var o in r.outputs)
            {
                if (o.item == null || o.count <= 0) continue;
                if (!outputC.HasSpace(o.item, o.count)) return false;
            }
            return true;
        }

        private void CompleteBatch()
        {
            // Re-validate (player could have drained inputs / filled outputs mid-batch).
            if (!HasOutputSpace(_current))
            {
                _progress = EffectiveBatchTime(_current); // pause; await space
                return;
            }
            foreach (var ing in _current.inputs)
            {
                if (ing.item == null || ing.count <= 0) continue;
                inputC.Remove(ing.item, ing.count);
            }
            foreach (var o in _current.outputs)
            {
                if (o.item == null || o.count <= 0) continue;
                outputC.Insert(new ItemStack(o.item, o.count));
            }
            _progress = 0f;
            _current  = FindRecipe();
        }
    }
}
