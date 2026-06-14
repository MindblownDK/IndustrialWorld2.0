// Assets/Scripts/VoxelEngine/GridSystem/GridGasNetwork.cs
//
// Per-grid gas distribution. There is ONE pipe system: the normal/static GasPipe
// component can be placed onto a grid and then counts as that grid's gas conduit.
// No separate GridGasPipe component is needed.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridGasNetwork : MonoBehaviour
    {
        private static GridGasNetwork _instance;
        public static GridGasNetwork Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("GridGasNetwork");
                    _instance = go.AddComponent<GridGasNetwork>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        public bool HasPipes(GridEntity grid)
        {
            if (grid == null) return false;
            foreach (var kv in grid.Blocks)
            {
                var block = kv.Value;
                if (block != null && block.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null)
                    return true;
            }
            return false;
        }

        public float AvailableGas(GridEntity grid, Gas.GasType type, bool includeStockpile = false)
        {
            if (grid == null || type == Gas.GasType.None || !HasPipes(grid)) return 0f;
            float total = 0f;
            foreach (var kv in grid.Blocks)
            {
                if (kv.Value is GridGasTank tank && tank.Enabled && tank.gasType == type)
                {
                    if (!includeStockpile && tank.mode == GridTankMode.Stockpile) continue;
                    total += Mathf.Max(0f, tank.stored);
                }
            }
            return total;
        }

        public float DrawGas(GridEntity grid, Gas.GasType type, float litres, bool includeStockpile = false)
        {
            if (grid == null || type == Gas.GasType.None || litres <= 0f || !HasPipes(grid)) return 0f;
            float drawn = 0f;
            foreach (var kv in grid.Blocks)
            {
                if (drawn >= litres) break;
                if (!(kv.Value is GridGasTank tank) || !tank.Enabled || tank.gasType != type) continue;
                if (!includeStockpile && tank.mode == GridTankMode.Stockpile) continue;
                drawn += tank.Draw(litres - drawn, ignoreStockpile: includeStockpile);
            }
            return drawn;
        }

        public float FillGas(GridEntity grid, Gas.GasType type, float litres)
        {
            if (grid == null || type == Gas.GasType.None || litres <= 0f || !HasPipes(grid)) return 0f;
            float filled = 0f;

            // Prefer tanks already assigned to this gas.
            foreach (var kv in grid.Blocks)
            {
                if (filled >= litres) break;
                if (kv.Value is GridGasTank tank && tank.Enabled && tank.gasType == type)
                    filled += tank.Add(type, litres - filled);
            }

            // Then allow empty tanks to adopt the gas type.
            foreach (var kv in grid.Blocks)
            {
                if (filled >= litres) break;
                if (kv.Value is GridGasTank tank && tank.Enabled && tank.stored <= 0.001f)
                    filled += tank.Add(type, litres - filled);
            }

            return filled;
        }
    }
}
