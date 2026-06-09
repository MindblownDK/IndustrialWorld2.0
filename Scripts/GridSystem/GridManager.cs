// Assets/Scripts/VoxelEngine/GridSystem/GridManager.cs
//
// Central manager for grid entities.

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
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public GridEntity SpawnGrid(Vector3 position, GridSize size)
        {
            var entity = GridEntity.Create(position, size);
            _activeGrids.Add(entity);
            return entity;
        }
    }
}