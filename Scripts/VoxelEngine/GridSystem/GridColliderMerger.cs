// Assets/Scripts/VoxelEngine/GridSystem/GridColliderMerger.cs
//
// Performance optimization for very large ships.
// Merges multiple block colliders into a single compound collider or mesh collider.
// Prevents lag/crash on 500+ block grids. Use when BlockCount > threshold.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridColliderMerger : MonoBehaviour
    {
        [Header("Performance")]
        public int mergeThreshold = 300;
        public bool autoMergeOnPlace = true;

        private GridEntity _grid;
        private List<Collider> _individualColliders = new();

        private void Awake()
        {
            _grid = GetComponent<GridEntity>();
            if (_grid != null && autoMergeOnPlace)
            {
                _grid.OnBlockPlaced += OnBlockChanged;
            }
        }

        private void OnBlockChanged(Vector3Int pos, GridBlock block)
        {
            if (_grid.BlockCount >= mergeThreshold && _individualColliders.Count == 0)
            {
                MergeColliders();
            }
        }

        public void MergeColliders()
        {
            if (_grid == null) return;

            // Collect all box colliders
            _individualColliders.Clear();
            foreach (var kv in _grid.Blocks)
            {
                var col = kv.Value.GetComponent<Collider>();
                if (col != null)
                {
                    _individualColliders.Add(col);
                    col.enabled = false; // disable individuals
                }
            }

            // Create compound collider parent
            var compound = new GameObject("MergedColliders");
            compound.transform.SetParent(transform, false);
            var compoundRb = compound.AddComponent<Rigidbody>();
            compoundRb.isKinematic = true;

            foreach (var col in _individualColliders)
            {
                var newCol = compound.AddComponent<BoxCollider>();
                newCol.center = transform.InverseTransformPoint(col.transform.position);
                newCol.size = col.bounds.size;
            }

            Debug.Log($"[GridColliderMerger] Merged {_individualColliders.Count} colliders for performance on large grid.");
        }

        public void Unmerge()
        {
            // Restore individual colliders (for editing)
            foreach (var col in _individualColliders)
            {
                if (col != null) col.enabled = true;
            }
            _individualColliders.Clear();
        }
    }
}