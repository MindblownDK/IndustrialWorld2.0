// Assets/Scripts/VoxelEngine/GridSystem/GridChemicalPlant.cs
//
// Chemical Plant - Mixes Kerosene + Liquid Hydrogen + Liquid Methane
// into high-performance Liquid Fuel. Large grid only.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridChemicalPlant : GridBlock
    {
        [Header("Chemical Plant - Fuel Synthesis")]
        public float powerDraw = 720f;
        public float mixRate = 3.5f;

        [Header("Input Ratios")]
        public float keroseneRatio = 0.55f;
        public float liquidH2Ratio = 0.30f;
        public float liquidMethaneRatio = 0.15f;

        private bool _isProducing;

        public override float PowerDraw => _isProducing ? powerDraw : 0f;

        private void FixedUpdate()
        {
            if (Grid == null) return;

            _isProducing = Grid.HasPower;

            if (_isProducing)
            {
                // Full implementation would consume from cargo tanks
                // and output into LiquidFuelTank
            }
        }
    }
}