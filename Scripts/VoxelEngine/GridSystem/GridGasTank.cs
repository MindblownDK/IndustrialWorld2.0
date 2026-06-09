// Assets/Scripts/VoxelEngine/GridSystem/GridGasTank.cs
//
// Gas storage tank for ships. Stores hydrogen or oxygen for thrusters and life
// support. Feeds the grid gas pool and (via grid gas pipes) connected thrusters.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridGasTank : GridBlock
    {
        [Header("Gas Storage")]
        public Gas.GasType gasType = Gas.GasType.Hydrogen;
        [Tooltip("Capacity in litres of gas.")]
        public float capacity = 500f;
        public float stored;

        public float Fill01 => capacity > 0 ? Mathf.Clamp01(stored / capacity) : 0f;

        // Compressed gas is light but not weightless — ~0.05 kg per stored litre.
        public override float ContentMass => stored * 0.05f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (Grid != null && gasType == Gas.GasType.Hydrogen)
                Grid.HydrogenStored += stored;
        }

        private void Update()
        {
            // Mirror the grid hydrogen pool for hydrogen tanks so the UI shows live data.
            if (Grid != null && gasType == Gas.GasType.Hydrogen)
                stored = Mathf.Min(capacity, Grid.HydrogenStored);
        }

        /// <summary>Draw up to <paramref name="litres"/> of gas. Returns litres drawn.</summary>
        public float Draw(float litres)
        {
            float take = Mathf.Min(stored, Mathf.Max(0f, litres));
            stored -= take;
            if (Grid != null && gasType == Gas.GasType.Hydrogen)
                Grid.HydrogenStored = Mathf.Max(0f, Grid.HydrogenStored - take);
            return take;
        }
    }
}
