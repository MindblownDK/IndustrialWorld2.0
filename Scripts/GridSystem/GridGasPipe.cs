// Assets/Scripts/VoxelEngine/GridSystem/GridGasPipe.cs
//
// Gas pipe (grid only). Registers with the gas network and uses the same brass
// PipeVisualBuilder profile as the static gas pipe.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Networks;

namespace VoxelEngine.GridSystem
{
    public class GridGasPipe : GridBlock
    {
        [Tooltip("Litres of gas this pipe segment can pass per second (cosmetic / future throttling).")]
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
            blockName = "Gas Pipe";
            EnsureVisuals();
            if (Grid != null && GridGasNetwork.Instance != null)
                GridGasNetwork.Instance.RegisterPipe(Grid, this);
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            if (Grid != null && GridGasNetwork.Instance != null)
                GridGasNetwork.Instance.UnregisterPipe(Grid, this);
        }

        private void EnsureVisuals()
        {
            _visuals = GetComponent<PipeVisualBuilder>();
            if (_visuals == null) _visuals = gameObject.AddComponent<PipeVisualBuilder>();
            _visuals.neighbourPositionsProvider = GetNeighbourPositions;
            _visuals.style = PipeStyle.Brass;
            _visuals.shellTint = new Color(0.78f, 0.62f, 0.20f);
            _visuals.accentTint = new Color(0.98f, 0.85f, 0.35f);
            _visuals.innerMediumTint = new Color(0.40f, 0.95f, 0.70f);
            _visuals.gridSize = Grid != null ? Grid.gridSize.CellSize() : GridSize.Large.CellSize();
        }

        private List<Vector3> GetNeighbourPositions()
        {
            _neighbourPositions.Clear();
            if (Grid == null) return _neighbourPositions;
            AddIfGasEndpoint(GridPos + Vector3Int.right);
            AddIfGasEndpoint(GridPos + Vector3Int.left);
            AddIfGasEndpoint(GridPos + Vector3Int.up);
            AddIfGasEndpoint(GridPos + Vector3Int.down);
            AddIfGasEndpoint(GridPos + new Vector3Int(0, 0, 1));
            AddIfGasEndpoint(GridPos + new Vector3Int(0, 0, -1));
            return _neighbourPositions;
        }

        private void AddIfGasEndpoint(Vector3Int pos)
        {
            var block = Grid.GetBlock(pos);
            if (block is GridGasPipe || block is GridGasTank || block is GridH2O2Generator || block is GridHydrogenEngine || block is GridThruster)
                _neighbourPositions.Add(Grid.GridToWorld(pos));
        }
    }
}
