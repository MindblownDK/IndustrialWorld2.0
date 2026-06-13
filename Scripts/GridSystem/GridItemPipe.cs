// Assets/Scripts/VoxelEngine/GridSystem/GridItemPipe.cs
//
// Grid item conveyor pipe. Registers visually with the same PipeVisualBuilder
// style used by static item pipes so one pipe language works on stations + grids.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Networks;

namespace VoxelEngine.GridSystem
{
    public class GridItemPipe : GridBlock
    {
        [Header("Item Pipe")]
        public float transferRate = 10f;

        private PipeVisualBuilder _visuals;
        private readonly List<Vector3> _neighbourPositions = new(6);

        private void Awake()
        {
            EnsureVisuals();
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Item Pipe";
            EnsureVisuals();
        }

        private void EnsureVisuals()
        {
            _visuals = GetComponent<PipeVisualBuilder>();
            if (_visuals == null) _visuals = gameObject.AddComponent<PipeVisualBuilder>();
            _visuals.neighbourPositionsProvider = GetNeighbourPositions;
            _visuals.style = PipeStyle.Sleeve;
            _visuals.shellTint = new Color(0.18f, 0.18f, 0.20f);
            _visuals.accentTint = new Color(0.95f, 0.55f, 0.12f);
            _visuals.innerMediumTint = new Color(0.95f, 0.75f, 0.25f);
            _visuals.gridSize = Grid != null ? Grid.gridSize.CellSize() : GridSize.Large.CellSize();
        }

        private List<Vector3> GetNeighbourPositions()
        {
            _neighbourPositions.Clear();
            if (Grid == null) return _neighbourPositions;
            AddIfPipe(GridPos + Vector3Int.right);
            AddIfPipe(GridPos + Vector3Int.left);
            AddIfPipe(GridPos + Vector3Int.up);
            AddIfPipe(GridPos + Vector3Int.down);
            AddIfPipe(GridPos + new Vector3Int(0, 0, 1));
            AddIfPipe(GridPos + new Vector3Int(0, 0, -1));
            return _neighbourPositions;
        }

        private void AddIfPipe(Vector3Int pos)
        {
            var block = Grid.GetBlock(pos);
            if (block is GridItemPipe || block is GridCargoContainer || block is GridDockingPort || block is GridDrill || block is GridElectricFurnace)
                _neighbourPositions.Add(Grid.GridToWorld(pos));
        }
    }
}
