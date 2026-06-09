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
    public class Furnace : MonoBehaviour, IItemPortHost
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
            if (_powerReq != null && !_powerReq.IsPowered) return; // brownout: pause

            // Pick a recipe matching the current input.
            if (_current == null) _current = FindRecipeForInput();

            if (_current == null) { _smeltProgress = 0; return; }

            // Need fuel.
            if (_fuelRemaining <= 0f)
            {
                if (!TryConsumeFuel()) return;
            }

            // Burn fuel.
            _fuelRemaining -= dt;

            // Make progress.
            _smeltProgress += dt;
            if (_smeltProgress >= _current.smeltSeconds)
            {
                CompleteOneBatch();
            }
        }

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
