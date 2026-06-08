// Assets/Scripts/VoxelEngine/GridSystem/GridRefinery.cs
//
// Industrial Refinery - Processes Crude Oil into Kerosene and other fuels.
// Large grid only. Very expensive and heavy.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridRefinery : GridBlock
    {
        [Header("Refinery - Liquid Fuel Chain")]
        public float crudeConsumptionRate = 8f;
        public float keroseneProductionRate = 5f;
        public float powerDraw = 850f;

        private bool _isProcessing;

        public override float PowerDraw => _isProcessing ? powerDraw : 0f;

        private void FixedUpdate()
        {
            if (Grid == null) return;

            _isProcessing = Grid.HasPower;

            if (_isProcessing)
            {
                // In a full system this would consume CrudeOil from cargo
                // and produce Kerosene into a LiquidFuelTank or cargo
            }
        }
    }
}