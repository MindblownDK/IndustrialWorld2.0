// Assets/Scripts/VoxelEngine/GridSystem/GridFluidStore.cs
//
// IFluidStore backed by GridLiquidTank blocks connected to a specific grid
// machine through the unified WaterPipe topology.

using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridFluidStore : IFluidStore
    {
        private readonly GridEntity _grid;
        private readonly GridBlock _endpoint;

        public GridFluidStore(GridEntity grid, GridBlock endpoint = null)
        {
            _grid = grid;
            _endpoint = endpoint;
        }

        public float Available(LiquidType type)
        {
            if (_grid == null || _endpoint == null || GridLiquidNetwork.Instance == null) return 0f;
            return GridLiquidNetwork.Instance.AvailableLiquidFor(_endpoint, type);
        }

        public float SpaceFor(LiquidType type)
        {
            if (_grid == null || _endpoint == null || GridLiquidNetwork.Instance == null) return 0f;
            return GridLiquidNetwork.Instance.SpaceForLiquidFrom(_endpoint, type);
        }

        public float Draw(LiquidType type, float litres)
        {
            if (_grid == null || _endpoint == null || GridLiquidNetwork.Instance == null) return 0f;
            return GridLiquidNetwork.Instance.DrawLiquidFor(_endpoint, type, litres);
        }

        public float Fill(LiquidType type, float litres)
        {
            if (_grid == null || _endpoint == null || GridLiquidNetwork.Instance == null) return 0f;
            return GridLiquidNetwork.Instance.FillLiquidFrom(_endpoint, type, litres);
        }
    }
}
