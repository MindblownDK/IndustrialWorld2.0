// Assets/Scripts/VoxelEngine/GridSystem/GridManager.cs
//
// Central manager for grid entities. Handles spawning, saving/loading (future), 
// and integration with VoxelWorld. Singleton pattern for easy access.
// Production quality, performance focused for large ships.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridManager : MonoBehaviour
    {
        public static GridManager Instance { get; private set; }

        private readonly List<GridEntity> _activeGrids = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public GridEntity SpawnGrid(Vector3 position, GridSize size, string name = null)
        {
            var entity = GridEntity.Create(position, size);
            if (!string.IsNullOrEmpty(name)) entity.name = name;
            _activeGrids.Add(entity);
            return entity;
        }

        public void RegisterGrid(GridEntity entity)
        {
            if (!_activeGrids.Contains(entity)) _activeGrids.Add(entity);
        }

        public void UnregisterGrid(GridEntity entity)
        {
            _activeGrids.Remove(entity);
        }

        // Future: Save/Load all grids, integration with VoxelWorld chunks
        // Performance: Can add spatial partitioning for very large worlds
    }
}