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

        [Tooltip("Auto feeds the grid gas pool. Stockpile keeps gas reserved in this tank.")]
        public GridTankMode mode = GridTankMode.Auto;

        public float Fill01 => capacity > 0 ? Mathf.Clamp01(stored / capacity) : 0f;

        // Compressed gas is light but not weightless — ~0.05 kg per stored litre.
        public override float ContentMass => stored * 0.05f;

        /// <summary>Change the stored gas type — only allowed while empty.</summary>
        public bool SetGasType(Gas.GasType t)
        {
            if (stored > 0.001f) return false;
            gasType = t;
            blockName = $"{gasType} Tank";
            return true;
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = $"{gasType} Tank";
            // The tank keeps its own buffer and feeds the grid pool through gas pipes
            // (see Update) rather than dumping into the pool on placement.
        }

        private void Update()
        {
            if (Grid == null || gasType != Gas.GasType.Hydrogen) return;
            if (mode == GridTankMode.Stockpile) { stored = Mathf.Clamp(stored, 0f, capacity); return; }

            // When gas pipes are present on the grid, the tank feeds the shared
            // hydrogen pool that thrusters draw from — Space-Engineers auto-supply.
            bool piped = GridGasNetwork.Instance != null && GridGasNetwork.Instance.HasPipes(Grid);
            if (piped && stored > 0f)
            {
                float feed = Mathf.Min(stored, 50f * Time.deltaTime); // top up the pool
                stored -= feed;
                Grid.HydrogenStored += feed;
            }

            // Keep the displayed value sane (don't exceed capacity).
            stored = Mathf.Clamp(stored, 0f, capacity);
        }

        /// <summary>Add up to capacity. Returns litres accepted. Empty tanks adopt the inserted gas type.</summary>
        public float Add(VoxelEngine.Gas.GasType type, float litres)
        {
            if (type == VoxelEngine.Gas.GasType.None || litres <= 0f) return 0f;
            if (stored > 0.001f && gasType != type) return 0f;
            if (stored <= 0.001f) gasType = type;
            float space = Mathf.Max(0f, capacity - stored);
            float take = Mathf.Min(space, litres);
            stored += take;
            blockName = $"{gasType} Tank";
            return take;
        }

        /// <summary>Draw up to <paramref name="litres"/> of gas. Returns litres drawn.</summary>
        public float Draw(float litres)
        {
            if (mode == GridTankMode.Stockpile) return 0f;
            float take = Mathf.Min(stored, Mathf.Max(0f, litres));
            stored -= take;
            if (Grid != null && gasType == Gas.GasType.Hydrogen)
                Grid.HydrogenStored = Mathf.Max(0f, Grid.HydrogenStored - take);
            return take;
        }
    }
}
