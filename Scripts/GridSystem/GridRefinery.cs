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
    public class GridRefinery : GridBlock, IMachineProcessState
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
            !Enabled ? 0f : (_current != null) ? baseWattsPerSecond * _current.powerDrawMultiplier : idleWattsPerSecond;

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
                runner.Run(_current);                 // consumes items + fluids, produces outputs
                _progress = 0f;
                _current = null;                      // re-pick next tick
            }
        }

        /// <summary>Player-selected recipe (from the UI). Null = auto-pick.</summary>
        [System.NonSerialized] public ProcessingRecipe selectedRecipe;

        // ── Machine process persistence (9.57.0-dev) ────────────────────────
        // A ship's refinery keeps its items in grid cargo and its fluids in grid
        // tanks, and both of those are already saved as blocks of their own, so the
        // only state that lived and died with the session was the batch and the
        // recipe the player picked. Those ride the same shared machine payload the
        // stationary machines use, carried through the grid block's runtime record.

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
            selectedRecipe = MachineProcessPersistence.Resolve(knownRecipes, state.selectedRecipeName, nameof(GridRefinery));
            _current = MachineProcessPersistence.Resolve(knownRecipes, state.recipeName, nameof(GridRefinery));
            _progress = _current == null
                ? 0f
                : Mathf.Clamp(state.progressSeconds, 0f, Mathf.Max(0.1f, _current.secondsPerBatch));
        }
    }
}
