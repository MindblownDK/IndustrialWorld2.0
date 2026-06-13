// Assets/Scripts/VoxelEngine/GridSystem/GridLiquidPipe.cs
//
// Liquid pipe (grid only). Registers with GridLiquidNetwork and uses the same
// copper/blue PipeVisualBuilder profile as the static water/liquid pipes.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Networks;

namespace VoxelEngine.GridSystem
{
    public class GridLiquidPipe : GridBlock
    {
        [Tooltip("Litres per second this pipe can pass (cosmetic / future throttling).")]
        public float throughput = 50f;

        private PipeVisualBuilder _visuals;
        private readonly List<Vector3> _neighbourPositions = new(6);

        private void Awake()
        {
            EnsureVisuals();
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Liquid Pipe";
            EnsureVisuals();
            if (Grid != null && GridLiquidNetwork.Instance != null)
                GridLiquidNetwork.Instance.RegisterPipe(Grid, this);
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            if (Grid != null && GridLiquidNetwork.Instance != null)
                GridLiquidNetwork.Instance.UnregisterPipe(Grid, this);
        }

        private void EnsureVisuals()
        {
            _visuals = GetComponent<PipeVisualBuilder>();
            if (_visuals == null) _visuals = gameObject.AddComponent<PipeVisualBuilder>();
            _visuals.neighbourPositionsProvider = GetNeighbourPositions;
            _visuals.style = PipeStyle.Copper;
            _visuals.shellTint = new Color(0.65f, 0.42f, 0.22f);
            _visuals.accentTint = new Color(0.20f, 0.65f, 0.95f);
            _visuals.innerMediumTint = new Color(0.20f, 0.55f, 0.95f);
            _visuals.gridSize = Grid != null ? Grid.gridSize.CellSize() : GridSize.Large.CellSize();
        }

        private List<Vector3> GetNeighbourPositions()
        {
            _neighbourPositions.Clear();
            if (Grid == null) return _neighbourPositions;
            AddIfLiquidEndpoint(GridPos + Vector3Int.right);
            AddIfLiquidEndpoint(GridPos + Vector3Int.left);
            AddIfLiquidEndpoint(GridPos + Vector3Int.up);
            AddIfLiquidEndpoint(GridPos + Vector3Int.down);
            AddIfLiquidEndpoint(GridPos + new Vector3Int(0, 0, 1));
            AddIfLiquidEndpoint(GridPos + new Vector3Int(0, 0, -1));
            return _neighbourPositions;
        }

        private void AddIfLiquidEndpoint(Vector3Int pos)
        {
            var block = Grid.GetBlock(pos);
            if (block is GridLiquidPipe || block is GridLiquidTank || block is GridH2O2Generator || block is GridRefinery || block is GridChemicalPlant)
                _neighbourPositions.Add(Grid.GridToWorld(pos));
        }
    }
}
