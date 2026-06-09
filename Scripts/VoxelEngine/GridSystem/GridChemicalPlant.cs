// Assets/Scripts/VoxelEngine/GridSystem/GridChemicalPlant.cs
//
// Chemical Plant (grid block). Large grid only.
//
// Mixes intermediate fuels into high-performance Liquid Fuel. Data-driven by
// ProcessingRecipe assets (category "Chemistry") and shares the cargo-driven
// runner with GridRefinery, so the grid + stationary chemical plants stay
// behaviourally identical.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;

namespace VoxelEngine.GridSystem
{
    public class GridChemicalPlant : GridBlock
    {
        [Header("Chemical Plant — Fuel Synthesis")]
        public List<ProcessingRecipe> knownRecipes = new();

        public float baseWattsPerSecond = 720f;
        public float idleWattsPerSecond = 20f;

        private ProcessingRecipe _current;
        private float _progress;

        public ProcessingRecipe Current => _current;
        public float Progress01 => _current == null ? 0f : Mathf.Clamp01(_progress / Mathf.Max(0.1f, _current.secondsPerBatch));

        public override float PowerDraw =>
            (_current != null) ? baseWattsPerSecond * _current.powerDrawMultiplier : idleWattsPerSecond;

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Ship Chemical Plant";
        }

        private void FixedUpdate()
        {
            if (Grid == null || !Grid.HasPower) { _progress = 0f; return; }

            if (_current == null) _current = ProcessingRecipeRunner.FindRunnable(knownRecipes, Grid);
            if (_current == null) { _progress = 0f; return; }

            _progress += Time.fixedDeltaTime;
            if (_progress >= Mathf.Max(0.1f, _current.secondsPerBatch))
            {
                if (ProcessingRecipeRunner.RunBatch(_current, Grid)) _progress = 0f;
                else _progress = _current.secondsPerBatch;
                _current = null;
            }
        }
    }
}
