// Assets/Scripts/VoxelEngine/GridSystem/GridFluidStore.cs
//
// IFluidStore backed by the GridLiquidTank blocks on a grid, so grid machines
// (Ship Refinery, Ship Chemical Plant) run the SAME fluid recipes as their
// stationary counterparts.

using System.Collections.Generic;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridFluidStore : IFluidStore
    {
        private readonly GridEntity _grid;
        public GridFluidStore(GridEntity grid) { _grid = grid; }

        private IEnumerable<GridLiquidTank> Tanks()
        {
            if (_grid == null) yield break;
            // Prefer the registered network; fall back to scanning the grid's blocks.
            if (GridLiquidNetwork.Instance != null)
            {
                foreach (var t in GridLiquidNetwork.Instance.GetTanks(_grid)) if (t != null) yield return t;
            }
            else
            {
                foreach (var kv in _grid.Blocks) if (kv.Value is GridLiquidTank t) yield return t;
            }
        }

        public float Available(LiquidType type)
        {
            float n = 0f;
            foreach (var t in Tanks()) if (t.liquidType == type) n += t.stored;
            return n;
        }

        public float SpaceFor(LiquidType type)
        {
            float n = 0f;
            foreach (var t in Tanks())
                if (t.liquidType == type) n += (t.capacity - t.stored);
            return n;
        }

        public float Draw(LiquidType type, float litres)
        {
            float drawn = 0f;
            foreach (var t in Tanks())
            {
                if (drawn >= litres) break;
                if (t.liquidType == type) drawn += t.Remove(litres - drawn);
            }
            return drawn;
        }

        public float Fill(LiquidType type, float litres)
        {
            float filled = 0f;
            foreach (var t in Tanks())
            {
                if (filled >= litres) break;
                if (t.liquidType == type) filled += t.Add(litres - filled);
            }
            return filled;
        }
    }
}
