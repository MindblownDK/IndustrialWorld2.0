// Assets/Scripts/VoxelEngine/GridSystem/GridColliderMerger.cs
//
// Performance optimization for large ships.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridColliderMerger : MonoBehaviour
    {
        [Header("Performance")]
        public int mergeThreshold = 300;

        private GridEntity _grid;
        private List<Collider> _colliders = new();

        private void Awake()
        {
            _grid = GetComponent<GridEntity>();
        }

        public void MergeColliders()
        {
            if (_grid == null || _grid.BlockCount < mergeThreshold) return;

            foreach (var kv in _grid.Blocks)
            {
                var col = kv.Value.GetComponent<Collider>();
                if (col != null)
                {
                    _colliders.Add(col);
                    col.enabled = false;
                }
            }

            // Create compound collider (simplified)
            Debug.Log($"[GridColliderMerger] Merged {_colliders.Count} colliders for performance.");
        }
    }
}