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

        /// <summary>
        /// True when a structural large-cell placement would envelop any detail block
        /// (pipes, ports, lights, or authored detail). Face-touching detail remains
        /// allowed; only actual shared volume is rejected.
        /// </summary>
        public bool HasStructuralVolumeConflict(Vector3Int largePos)
        {
            Vector3 center = (Vector3)largePos * GridSize.Large.CellSize();
            const float combinedHalfExtent = 1.5f; // 1.25 m structural + 0.25 m detail
            const float overlapEpsilon = 0.002f;
            foreach (var block in _blocks.Values)
            {
                if (block == null) continue;
                Vector3 delta = block.transform.localPosition - center;
                if (Mathf.Abs(delta.x) < combinedHalfExtent - overlapEpsilon
                    && Mathf.Abs(delta.y) < combinedHalfExtent - overlapEpsilon
                    && Mathf.Abs(delta.z) < combinedHalfExtent - overlapEpsilon)
                    return true;
            }
            return false;
        }

        public bool CanPlaceStructuralBlock(Vector3Int largePos)
        {
            Vector3 center = (Vector3)largePos * GridSize.Large.CellSize();
            const float combinedHalfExtent = 1.5f; // 1.25 m structural + 0.25 m detail
            bool supported = false;

            foreach (var block in _blocks.Values)
            {
                if (block == null) continue;
                Vector3 delta = block.transform.localPosition - center;
                float ax = Mathf.Abs(delta.x);
                float ay = Mathf.Abs(delta.y);
                float az = Mathf.Abs(delta.z);

                if (ax < combinedHalfExtent && ay < combinedHalfExtent && az < combinedHalfExtent)
                    return false;

                bool touchesFace =
                    (Mathf.Abs(ax - combinedHalfExtent) < 0.02f && ay <= combinedHalfExtent && az <= combinedHalfExtent)
                    || (Mathf.Abs(ay - combinedHalfExtent) < 0.02f && ax <= combinedHalfExtent && az <= combinedHalfExtent)
                    || (Mathf.Abs(az - combinedHalfExtent) < 0.02f && ax <= combinedHalfExtent && ay <= combinedHalfExtent);
                supported |= touchesFace;
            }

            return supported;
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
            VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged();
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
            var grid = Grid;
            grid?.RecalculateMass();
            VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged();
            if (_blocks.Count == 0 && grid != null && grid.BlockCount == 0)
                Destroy(grid.gameObject);
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
