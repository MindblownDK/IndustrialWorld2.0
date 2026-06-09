// Assets/Scripts/VoxelEngine/Industrial/StationaryChemicalPlant.cs
//
// Stationary Chemical Plant — placeable world machine, the ground-based
// equivalent of the grid Chemical Plant. Multi-input / multi-output processor
// driven by ProcessingRecipe assets (category "Chemistry").
//
// Built on the same pattern as OilRefinery (CraftingStation + PortConfig +
// ItemPort routing) so it slots straight into the logistics network and shares
// the same recipe authoring pipeline.

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

            float wantWatts = (_current != null)
                ? baseWattsPerSecond * _current.powerDrawMultiplier
                : idleWattsPerSecond;
            CurrentWattage = wantWatts;
            if (_power != null) _power.wattsPerSecond = wantWatts;

            if (!IsOnline) return;

            if (_current == null) _current = FindRecipe();
            if (_current == null) { _progress = 0f; return; }

            _progress += Time.deltaTime;
            if (_progress >= Mathf.Max(0.1f, _current.secondsPerBatch))
                CompleteBatch();
        }

        private ProcessingRecipe FindRecipe()
        {
            for (int i = 0; i < knownRecipes.Count; i++)
            {
                var r = knownRecipes[i];
                if (r == null) continue;
                if (HasAllInputs(r) && HasOutputSpace(r)) return r;
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
            if (!HasOutputSpace(_current)) { _progress = _current.secondsPerBatch; return; }

            foreach (var ing in _current.inputs)
                if (ing.item != null && ing.count > 0) inputC.Remove(ing.item, ing.count);

            foreach (var o in _current.outputs)
                if (o.item != null && o.count > 0) outputC.Insert(new ItemStack { item = o.item, count = o.count });

            _progress = 0f;
            _current = null;
        }
    }
}
