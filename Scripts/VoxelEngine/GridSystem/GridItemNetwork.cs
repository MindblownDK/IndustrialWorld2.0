// Assets/Scripts/VoxelEngine/GridSystem/GridItemNetwork.cs
//
// Manages connected cargo containers on a grid (for item cables).

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridItemNetwork : MonoBehaviour
    {
        public static GridItemNetwork Instance { get; private set; }

        private Dictionary<GridEntity, List<GridCargoContainer>> _containers 
            = new Dictionary<GridEntity, List<GridCargoContainer>>();

        private void Awake()
        {
            if (Instance != null && Instance != this) 
                Destroy(gameObject);
            else 
                Instance = this;
        }

        public void RegisterContainer(GridEntity grid, GridCargoContainer container)
        {
            if (!_containers.ContainsKey(grid))
                _containers[grid] = new List<GridCargoContainer>();

            if (!_containers[grid].Contains(container))
                _containers[grid].Add(container);
        }

        public List<GridCargoContainer> GetConnectedContainers(GridEntity grid)
        {
            return _containers.TryGetValue(grid, out var list) 
                ? list 
                : new List<GridCargoContainer>();
        }
    }
}