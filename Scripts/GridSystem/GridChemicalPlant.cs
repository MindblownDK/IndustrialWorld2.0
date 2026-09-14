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
    public class GridChemicalPlant : GridBlock, IMachineProcessState
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
            !Enabled ? 0f : (_current != null) ? baseWattsPerSecond * _current.powerDrawMultiplier : idleWattsPerSecond;

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Ship Chemical Plant";
        }

        private void FixedUpdate()
        {
            if (!Enabled || Grid == null || !Grid.HasPower) { _progress = 0f; return; }

            var runner = new GridProcessingContext(Grid, this);
            var pool = selectedRecipe != null
                ? new System.Collections.Generic.List<ProcessingRecipe> { selectedRecipe }
                : knownRecipes;
            if (_current == null) _current = runner.FindRunnable(pool);
            if (_current == null) { _progress = 0f; return; }

            _progress += Time.fixedDeltaTime;
            if (_progress >= Mathf.Max(0.1f, _current.secondsPerBatch))
            {
                runner.Run(_current);
                _progress = 0f;
                _current = null;
            }
        }

        [System.NonSerialized] public ProcessingRecipe selectedRecipe;

        // ── Machine process persistence (9.57.0-dev) ────────────────────────
        // Same as the ship's refinery: cargo and grid tanks are saved as blocks of
        // their own, so the batch and the player's recipe pick are what needed a
        // home. Both ride the shared machine payload in the grid block's runtime record.

        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            state.recipeName = MachineProcessPersistence.NameOf(_current);
            state.selectedRecipeName = MachineProcessPersistence.NameOf(selectedRecipe);
            state.progressSeconds = Mathf.Max(0f, _progress);
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null) return;
            selectedRecipe = MachineProcessPersistence.Resolve(knownRecipes, state.selectedRecipeName, nameof(GridChemicalPlant));
            _current = MachineProcessPersistence.Resolve(knownRecipes, state.recipeName, nameof(GridChemicalPlant));
            _progress = _current == null
                ? 0f
                : Mathf.Clamp(state.progressSeconds, 0f, Mathf.Max(0.1f, _current.secondsPerBatch));
        }
    }
}
