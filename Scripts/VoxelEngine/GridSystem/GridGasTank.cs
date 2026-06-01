// Assets/Scripts/VoxelEngine/GridSystem/GridGasTank.cs
//
// Gas storage tank for ships. Stores hydrogen or oxygen for thrusters.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridGasTank : GridBlock
    {
        [Header("Gas Storage")]
        public Gas.GasType gasType = Gas.GasType.Hydrogen;
        public float capacity = 500f;
        public float stored;

        public float Fill01 => capacity > 0 ? Mathf.Clamp01(stored / capacity) : 0;

        public override void OnPlaced()
        {
            base.OnPlaced();
            // Register stored gas with grid entity.
            if (Grid != null && gasType == Gas.GasType.Hydrogen)
                Grid.HydrogenStored += stored;
        }

        private void Update()
        {
            // Sync with grid hydrogen pool.
            if (Grid != null && gasType == Gas.GasType.Hydrogen)
            {
                stored = Mathf.Min(capacity, Grid.HydrogenStored);
            }
        }
    }
}
