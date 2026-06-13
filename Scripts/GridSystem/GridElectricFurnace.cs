// Assets/Scripts/VoxelEngine/GridSystem/GridElectricFurnace.cs
//
// Ship-mounted electric furnace. Smelts smeltable items into ingots using the
// grid's SmeltingRecipe set. Optional auto-pull grabs smeltable items from the
// ship's connected cargo; outputs are pushed back to cargo.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridElectricFurnace : GridBlock
    {
        public const int INPUT_SLOTS  = 4;
        public const int OUTPUT_SLOTS = 4;

        [Header("Furnace")]
        public List<SmeltingRecipe> knownRecipes = new();
        public ItemContainer inputC;
        public ItemContainer outputC;

        public float baseWattsPerSecond = 300f;
        public float idleWattsPerSecond = 10f;

        [Tooltip("Auto-pull smeltable items from connected ship cargo.")]
        public bool autoPull = true;

        private SmeltingRecipe _current;
        private float _progress;
        private float _pullTimer;

        public bool   IsSmelting => _current != null;
        public float  Progress01 => _current == null ? 0f : Mathf.Clamp01(_progress / Mathf.Max(0.1f, _current.smeltSeconds));
        public float  CurrentWattage { get; private set; }

        public override float PowerDraw => Enabled ? CurrentWattage : 0f;
        public override float ContentMass =>
            (inputC != null ? MassUtil.ContainerMass(inputC) : 0f) + (outputC != null ? MassUtil.ContainerMass(outputC) : 0f);

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Ship Electric Furnace";
            if (inputC  == null) inputC  = new ItemContainer("Input",  INPUT_SLOTS);  else inputC.Resize(INPUT_SLOTS);
            if (outputC == null) outputC = new ItemContainer("Output", OUTPUT_SLOTS); else outputC.Resize(OUTPUT_SLOTS);
            inputC.AcceptFilter = (item, wanted) => IsSmeltable(item) ? wanted : 0;
            // Pull recipes from the wizard-generated set if not assigned.
            if (knownRecipes == null || knownRecipes.Count == 0)
                knownRecipes = new List<SmeltingRecipe>(Resources.FindObjectsOfTypeAll<SmeltingRecipe>());
        }

        private void Update()
        {
            if (autoPull)
            {
                _pullTimer += Time.deltaTime;
                if (_pullTimer >= 0.5f) { _pullTimer = 0f; AutoPull(); PushOutputs(); }
            }
        }

        private void FixedUpdate()
        {
            if (!Enabled || Grid == null) { CurrentWattage = 0f; _progress = 0f; return; }
            CurrentWattage = idleWattsPerSecond;
            if (!Grid.HasPower) { _progress = 0f; return; }

            if (_current == null) _current = FindRecipe();
            if (_current == null) { _progress = 0f; return; }

            CurrentWattage = baseWattsPerSecond;
            _progress += Time.fixedDeltaTime;
            if (_progress >= Mathf.Max(0.1f, _current.smeltSeconds))
            {
                if (outputC.HasSpace(_current.output, _current.outputCount) && inputC.CountOf(_current.input) >= _current.inputCount)
                {
                    inputC.Remove(_current.input, _current.inputCount);
                    outputC.Insert(new ItemStack(_current.output, _current.outputCount));
                }
                _progress = 0f;
                _current = null;
            }
        }

        private SmeltingRecipe FindRecipe()
        {
            foreach (var r in knownRecipes)
                if (r != null && r.input != null && inputC.CountOf(r.input) >= r.inputCount
                    && outputC.HasSpace(r.output, r.outputCount)) return r;
            return null;
        }

        private bool IsSmeltable(ItemDefinition item)
        {
            if (item == null || knownRecipes == null) return false;
            foreach (var r in knownRecipes) if (r != null && r.input == item) return true;
            return false;
        }

        private void AutoPull()
        {
            if (inputC == null || Grid == null || GridItemNetwork.Instance == null) return;
            foreach (var cargo in GridItemNetwork.Instance.GetConnectedContainers(Grid))
            {
                if (cargo?.container == null) continue;
                for (int i = 0; i < cargo.container.Size; i++)
                {
                    var s = cargo.container.GetSlot(i);
                    if (s == null || s.IsEmpty || s.item == null || !IsSmeltable(s.item)) continue;
                    if (!inputC.HasSpace(s.item, 1)) return;
                    int take = Mathf.Min(s.count, s.item.maxStack);
                    int moved = cargo.container.Remove(s.item, take);
                    if (moved > 0)
                    {
                        var left = inputC.Insert(new ItemStack(s.item, moved));
                        if (left != null && !left.IsEmpty) cargo.container.Insert(left);
                        return;
                    }
                }
            }
        }

        private void PushOutputs()
        {
            if (outputC == null || Grid == null || GridItemNetwork.Instance == null) return;
            var cargos = GridItemNetwork.Instance.GetConnectedContainers(Grid);
            if (cargos.Count == 0) return;
            for (int i = 0; i < outputC.Size; i++)
            {
                var s = outputC.GetSlot(i);
                if (s == null || s.IsEmpty || s.item == null) continue;
                var moving = new ItemStack(s.item, s.count);
                foreach (var cargo in cargos)
                {
                    if (cargo?.container == null) continue;
                    moving = cargo.container.Insert(moving);
                    if (moving == null || moving.IsEmpty) break;
                }
                int moved = s.count - (moving?.count ?? 0);
                if (moved > 0) outputC.Remove(s.item, moved);
            }
        }

        public void ToggleAutoPull() => autoPull = !autoPull;
    }
}
