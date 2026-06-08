// Assets/Scripts/VoxelEngine/GridSystem/GridRefinery.cs
//
// Refinery for crude oil → kerosene.

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
            _isProcessing = Grid.HasPower;
        }
    }
}