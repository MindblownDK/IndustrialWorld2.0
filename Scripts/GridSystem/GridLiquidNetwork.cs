// Assets/Scripts/VoxelEngine/GridSystem/GridLiquidNetwork.cs
//
// Registry of liquid tanks and pipe segments per grid. Grid machines use this
// network to decide whether fluids may move between tanks and processors.

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
        private readonly Dictionary<GridEntity, List<GridLiquidPipe>> _pipes = new();

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

        public void RegisterPipe(GridEntity grid, GridLiquidPipe pipe)
        {
            if (grid == null || pipe == null) return;
            if (!_pipes.TryGetValue(grid, out var list)) { list = new List<GridLiquidPipe>(); _pipes[grid] = list; }
            if (!list.Contains(pipe)) list.Add(pipe);
        }

        public void UnregisterPipe(GridEntity grid, GridLiquidPipe pipe)
        {
            if (grid != null && _pipes.TryGetValue(grid, out var list)) list.Remove(pipe);
        }

        public bool HasPipes(GridEntity grid)
            => _pipes.TryGetValue(grid, out var list) && list.Count > 0;

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
