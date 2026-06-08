// Assets/Scripts/VoxelEngine/GridSystem/GridChemicalPlant.cs
//
// Chemical Plant for mixing liquid fuels.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridChemicalPlant : GridBlock
    {
        [Header("Chemical Plant")]
        public float powerDraw = 250f;
        public float mixRate = 2f;

        private bool _isProducing;

        public override float PowerDraw => _isProducing ? powerDraw : 0f;

        private void FixedUpdate()
        {
            if (Grid == null) return;
            _isProducing = Grid.HasPower;
        }
    }
}