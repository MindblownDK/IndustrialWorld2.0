// Assets/Scripts/VoxelEngine/GridSystem/GridGasTank.cs
//
// Gas storage tank for ships. Stores hydrogen or oxygen for thrusters and life
// support. Feeds the grid gas pool and (via grid gas pipes) connected thrusters.
// v5.43.0-dev — Implements IGridDataProvider for screen display.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridGasTank : GridBlock, IGridDataProvider
    {
        [Header("Gas Storage")]
        public Gas.GasType gasType = Gas.GasType.Hydrogen;
        [Tooltip("Capacity in litres of gas.")]
        public float capacity = 500f;
        public float stored;

        [Tooltip("Auto feeds the grid gas pool. Stockpile keeps gas reserved in this tank.")]
        public GridTankMode mode = GridTankMode.Auto;

        public float Fill01 => capacity > 0 ? Mathf.Clamp01(stored / capacity) : 0f;

        public override float ContentMass => stored * 0.05f;

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
        }

        private void Update()
        {
            stored = Mathf.Clamp(stored, 0f, capacity);
        }

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

        public float Draw(float litres, bool ignoreStockpile = false)
        {
            if (!ignoreStockpile && mode == GridTankMode.Stockpile) return 0f;
            float take = Mathf.Min(stored, Mathf.Max(0f, litres));
            stored -= take;
            return take;
        }

        // -- IGridDataProvider -----------------------------------------
        public string SourceName => blockName;
        public string DataCategory => "Gas";
        public string GetDisplayData()
        {
            return $"GAS\n{gasType}\n{Fill01 * 100f:0}%\n{stored:0.0} / {capacity:0.0} L\nMode: {mode}";
        }
    }
}
