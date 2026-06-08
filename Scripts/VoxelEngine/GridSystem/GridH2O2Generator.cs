// Assets/Scripts/VoxelEngine/GridSystem/GridH2O2Generator.cs
//
// H2/O2 Generator for grids. Produces Hydrogen and Oxygen from water/ice.
// Integrated with existing Gas systems. Production quality.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridH2O2Generator : GridBlock
    {
        [Header("H2/O2 Generator")]
        public float waterPerSecond = 2f;
        public float hydrogenPerSecond = 1f;
        public float oxygenPerSecond = 0.5f;
        public float powerDraw = 150f;

        private bool _isProducing;

        public override float PowerDraw => _isProducing ? powerDraw : 0f;

        private void FixedUpdate()
        {
            if (Grid == null) return;

            _isProducing = Grid.HasPower;

            if (_isProducing)
            {
                float dt = Time.fixedDeltaTime;
                Grid.HydrogenStored += hydrogenPerSecond * dt;
                Grid.OxygenStored += oxygenPerSecond * dt;
            }
        }
    }
}