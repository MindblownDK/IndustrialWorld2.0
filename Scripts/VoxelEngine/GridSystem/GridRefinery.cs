// Assets/Scripts/VoxelEngine/GridSystem/GridRefinery.cs
//
// Refinery block for grids. Processes crude oil into Kerosene and other distillates.
// Part of the full liquid fuel production chain. Complex process as requested.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridRefinery : GridBlock
    {
        [Header("Refinery")]
        public float crudeConsumptionRate = 5f;
        public float keroseneProductionRate = 3f;
        public float powerDraw = 300f;

        private bool _isProcessing;

        public override float PowerDraw => _isProcessing ? powerDraw : 0f;

        private void FixedUpdate()
        {
            if (Grid == null) return;

            _isProcessing = Grid.HasPower; // + crude available stub

            if (_isProcessing)
            {
                // Production logic stub - full implementation with item consumption in Phase 3
                // Will integrate with cargo containers and new CrudeOil item
            }
        }
    }
}