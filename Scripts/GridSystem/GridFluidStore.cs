// Assets/Scripts/VoxelEngine/GridSystem/GridFluidStore.cs
//
// IFluidStore backed by the GridLiquidTank blocks on a grid. Liquid transfer is
// enabled only when the grid has liquid pipes, so processors need visible pipe
// infrastructure instead of magically reaching every tank.

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
            if (GridLiquidNetwork.Instance != null && !GridLiquidNetwork.Instance.HasPipes(_grid)) yield break;

            // Prefer the registered network; fall back to scanning the grid's blocks.
            if (GridLiquidNetwork.Instance != null)
            {
                foreach (var t in GridLiquidNetwork.Instance.GetTanks(_grid))
                    if (t != null && t.mode != GridTankMode.Stockpile) yield return t;
            }
            else
            {
                foreach (var kv in _grid.Blocks)
                    if (kv.Value is GridLiquidTank t && t.mode != GridTankMode.Stockpile) yield return t;
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
