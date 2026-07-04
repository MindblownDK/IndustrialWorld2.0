// Assets/Scripts/VoxelEngine/GridSystem/ProcessingRecipeRunner.cs
//
// Wires a grid machine's data sources (cargo containers for items, liquid tanks
// for fluids) into the shared ProcessingExecutor so grid machines run the exact
// same recipes as their stationary counterparts.

using System.Collections.Generic;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    /// <summary>Per-tick view of a grid's processing resources (items in cargo, fluids in tanks).</summary>
    public struct GridProcessingContext
    {
        private readonly GridEntity _grid;
        private readonly GridBlock _endpoint;

        public GridProcessingContext(GridEntity grid, GridBlock endpoint = null)
        {
            _grid = grid;
            _endpoint = endpoint;
        }

        private List<ItemContainer> Cargo()
        {
            var list = new List<ItemContainer>();
            if (_grid == null) return list;
            if (GridItemNetwork.Instance != null)
            {
                foreach (var c in GridItemNetwork.Instance.GetConnectedContainers(_grid))
                    if (c != null && c.container != null) list.Add(c.container);
            }
            else
            {
                foreach (var kv in _grid.Blocks)
                    if (kv.Value is GridCargoContainer c && c.container != null) list.Add(c.container);
            }
            return list;
        }

        public ProcessingRecipe FindRunnable(List<ProcessingRecipe> recipes)
        {
            if (recipes == null || _grid == null) return null;
            var cargo = Cargo();
            var fluids = new GridFluidStore(_grid, _endpoint);
            for (int i = 0; i < recipes.Count; i++)
            {
                var r = recipes[i];
                if (r != null && ProcessingExecutor.CanRun(r, cargo, cargo, fluids)) return r;
            }
            return null;
        }

        public bool Run(ProcessingRecipe r)
        {
            if (r == null || _grid == null) return false;
            var cargo = Cargo();
            var fluids = new GridFluidStore(_grid, _endpoint);
            return ProcessingExecutor.Run(r, cargo, cargo, fluids);
        }
    }
}
