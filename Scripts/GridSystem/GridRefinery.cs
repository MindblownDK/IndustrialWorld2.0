// Assets/Scripts/VoxelEngine/GridSystem/GridRefinery.cs
//
// Industrial Refinery (grid block). Large grid only.
//
// Parity with the stationary OilRefinery: it is now data-driven by the SAME
// ProcessingRecipe assets. Instead of its own input/output slots it draws raw
// inputs from — and pushes finished outputs into — the GridCargoContainer
// blocks on its parent GridEntity, so the recipe set is shared 1:1 with the
// world Oil Refinery.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridRefinery : GridBlock
    {
        [Header("Refinery — Liquid Fuel Chain")]
        [Tooltip("Same ProcessingRecipe assets the stationary Oil Refinery uses.")]
        public List<ProcessingRecipe> knownRecipes = new();

        [Tooltip("Base watts/s drawn while a batch is processing. Multiplied by recipe.powerDrawMultiplier.")]
        public float baseWattsPerSecond = 850f;
        [Tooltip("Watts/s drawn while idle (keeps the cracking column hot).")]
        public float idleWattsPerSecond = 20f;

        private ProcessingRecipe _current;
        private float _progress;

        public ProcessingRecipe Current => _current;
        public float Progress01 => _current == null ? 0f : Mathf.Clamp01(_progress / Mathf.Max(0.1f, _current.secondsPerBatch));

        public override float PowerDraw =>
            (_current != null) ? baseWattsPerSecond * _current.powerDrawMultiplier : idleWattsPerSecond;

        private void FixedUpdate()
        {
            if (!Enabled || Grid == null || !Grid.HasPower) { _progress = 0f; return; }

            var runner = new GridProcessingContext(Grid);
            var pool = selectedRecipe != null
                ? new System.Collections.Generic.List<ProcessingRecipe> { selectedRecipe }
                : knownRecipes;
            if (_current == null) _current = runner.FindRunnable(pool);
            if (_current == null) { _progress = 0f; return; }

            _progress += Time.fixedDeltaTime;
            if (_progress >= Mathf.Max(0.1f, _current.secondsPerBatch))
            {
                runner.Run(_current);                 // consumes items + fluids, produces outputs
                _progress = 0f;
                _current = null;                      // re-pick next tick
            }
        }

        /// <summary>Player-selected recipe (from the UI). Null = auto-pick.</summary>
        [System.NonSerialized] public ProcessingRecipe selectedRecipe;
    }
}
