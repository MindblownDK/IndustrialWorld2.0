// Assets/Scripts/VoxelEngine/GridSystem/GridLiquidNetwork.cs
//
// Registry of liquid tanks per grid. There is ONE pipe system: the normal/static
// WaterPipe component can be placed onto a grid and then counts as that grid's
// liquid conduit. No separate GridLiquidPipe component is needed.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridLiquidNetwork : MonoBehaviour
    {
        private static GridLiquidNetwork _instance;
        public static GridLiquidNetwork Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("GridLiquidNetwork");
                    _instance = go.AddComponent<GridLiquidNetwork>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private readonly Dictionary<GridEntity, List<GridLiquidTank>> _tanks = new();

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        public void RegisterTank(GridEntity grid, GridLiquidTank tank)
        {
            if (grid == null || tank == null) return;
            if (!_tanks.TryGetValue(grid, out var list)) { list = new List<GridLiquidTank>(); _tanks[grid] = list; }
            if (!list.Contains(tank)) list.Add(tank);
        }

        public void UnregisterTank(GridEntity grid, GridLiquidTank tank)
        {
            if (grid != null && _tanks.TryGetValue(grid, out var list)) list.Remove(tank);
        }

        public bool HasPipes(GridEntity grid)
        {
            if (grid == null) return false;
            foreach (var kv in grid.Blocks)
            {
                var block = kv.Value;
                if (block != null && block.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null)
                    return true;
            }
            return false;
        }

        public IReadOnlyList<GridLiquidTank> GetTanks(GridEntity grid)
            => _tanks.TryGetValue(grid, out var list) ? list : System.Array.Empty<GridLiquidTank>();

        /// <summary>Tanks on a grid carrying a specific liquid type.</summary>
        public List<GridLiquidTank> GetTanks(GridEntity grid, LiquidType type)
        {
            var result = new List<GridLiquidTank>();
            foreach (var t in GetTanks(grid)) if (t != null && t.liquidType == type) result.Add(t);
            return result;
        }
    }
}
