// Assets/Scripts/VoxelEngine/GridSystem/GridLiquidNetwork.cs
//
// Registry of liquid tanks per grid. Phase 2 only tracks tanks so machines and
// UIs can find them; the full liquid-pipe transport behaviour arrives in a
// later phase (grid liquid pipes).

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridLiquidNetwork : MonoBehaviour
    {
        public static GridLiquidNetwork Instance { get; private set; }

        private readonly Dictionary<GridEntity, List<GridLiquidTank>> _tanks = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
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
