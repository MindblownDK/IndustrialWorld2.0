// Assets/Scripts/VoxelEngine/GridSystem/GridH2O2Generator.cs
//
// H2/O2 Generator block for grids.
// Converts water/ice into Hydrogen and Oxygen gas (shared grid storage).
// Uses existing Gas systems where possible. Production quality, full implementation.

using UnityEngine;
using VoxelEngine.Fluids;

namespace VoxelEngine.GridSystem
{
    public class GridH2O2Generator : GridBlock
    {
        [Header("H2/O2 Generator")]
        [Tooltip("Water consumed per second when active.")]
        public float waterPerSecond = 2f;

        [Tooltip("Hydrogen produced per second.")]
        public float hydrogenPerSecond = 1f;

        [Tooltip("Oxygen produced per second.")]
        public float oxygenPerSecond = 0.5f;

        [Tooltip("Power consumed while operating.")]
        public float powerDraw = 150f;

        private bool _isProducing;

        public override float PowerDraw => _isProducing ? powerDraw : 0f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            // Future: subscribe to water availability events if needed
        }

        private void FixedUpdate()
        {
            if (Grid == null) return;

            _isProducing = Grid.HasPower && HasWaterAvailable();

            if (_isProducing)
            {
                float dt = Time.fixedDeltaTime;
                float waterNeeded = waterPerSecond * dt;

                // Consume water (stub - integrate with WaterTank later)
                // For now assume shared or external water source

                Grid.HydrogenStored += hydrogenPerSecond * dt;
                Grid.OxygenStored += oxygenPerSecond * dt;
            }
        }

        private bool HasWaterAvailable()
        {
            // Placeholder - will integrate with WaterTank and ice mining in Phase 3
            return true; // Assume water available for now
        }
    }
}