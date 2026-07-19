// Assets/Scripts/VoxelEngine/GridSystem/GridPrecisionAttachmentLayer.cs
//
// Stores small-grid structural detail blocks on a large-grid entity using the
// small-grid lattice without changing the large grid's existing block coordinates.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    [DisallowMultipleComponent]
    public sealed class GridPrecisionAttachmentLayer : MonoBehaviour
    {
        private readonly Dictionary<Vector3Int, GridBlock> _blocks = new();

        public IReadOnlyDictionary<Vector3Int, GridBlock> Blocks => _blocks;
        public int Count => _blocks.Count;
        public float CellSize => GridSize.Small.CellSize();

        private GridEntity Grid => GetComponent<GridEntity>();

        public bool CanPlace(Vector3Int precisionPos) => !_blocks.ContainsKey(precisionPos);

        public bool HasNeighbor(Vector3Int precisionPos)
        {
            return _blocks.ContainsKey(precisionPos + Vector3Int.right)
                || _blocks.ContainsKey(precisionPos + Vector3Int.left)
                || _blocks.ContainsKey(precisionPos + Vector3Int.up)
                || _blocks.ContainsKey(precisionPos + Vector3Int.down)
                || _blocks.ContainsKey(precisionPos + new Vector3Int(0, 0, 1))
                || _blocks.ContainsKey(precisionPos + new Vector3Int(0, 0, -1));
        }

        public GridBlock GetBlock(Vector3Int precisionPos)
        {
            _blocks.TryGetValue(precisionPos, out var block);
            return block;
        }

        public bool AddBlock(Vector3Int precisionPos, Vector3Int hostLargePos, GridBlock block, Quaternion localRotation)
        {
            var grid = Grid;
            if (grid == null || grid.gridSize != GridSize.Large || block == null || !CanPlace(precisionPos))
                return false;

            _blocks.Add(precisionPos, block);
            block.Grid = grid;
            block.GridPos = grid.WorldToGrid(grid.transform.TransformPoint((Vector3)precisionPos * CellSize));
            block.IsPrecisionAttachment = true;
            block.PrecisionGridPos = precisionPos;
            block.PrecisionHostGridPos = hostLargePos;

            block.transform.SetParent(transform, false);
            block.transform.localPosition = (Vector3)precisionPos * CellSize;
            block.transform.localRotation = localRotation;

            if (block.GetComponentInChildren<Collider>(true) == null)
            {
                var collider = block.gameObject.AddComponent<BoxCollider>();
                collider.size = Vector3.one * CellSize;
            }

            block.OnPlaced();
            grid.RecalculateMass();
            return true;
        }

        public void RemoveBlock(Vector3Int precisionPos)
        {
            if (!_blocks.TryGetValue(precisionPos, out var block)) return;
            _blocks.Remove(precisionPos);
            if (block != null)
            {
                block.OnRemoved();
                Destroy(block.gameObject);
            }
            Grid?.RecalculateMass();
        }

        public bool Contains(GridBlock block)
        {
            return block != null
                && block.IsPrecisionAttachment
                && _blocks.TryGetValue(block.PrecisionGridPos, out var stored)
                && stored == block;
        }
    }
}
