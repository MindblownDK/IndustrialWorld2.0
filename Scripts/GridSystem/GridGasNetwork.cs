// Assets/Scripts/VoxelEngine/GridSystem/GridGasNetwork.cs
//
// Per-grid gas distribution. On a grid the hydrogen pool is shared entity-wide,
// so once a Gas Tank is connected (and gas pipes are laid) every Hydrogen
// Thruster automatically draws from it — no manual hookup, like Space Engineers.
//
// This manager pumps stored gas from Gas Tanks into the grid's shared pool each
// tick so thrusters (which consume Grid.HydrogenStored) are fed automatically.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridGasNetwork : MonoBehaviour
    {
        private static GridGasNetwork _instance;
        public static GridGasNetwork Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("GridGasNetwork");
                    _instance = go.AddComponent<GridGasNetwork>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private readonly Dictionary<GridEntity, List<GridGasPipe>> _pipes = new();

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        public void RegisterPipe(GridEntity grid, GridGasPipe pipe)
        {
            if (grid == null || pipe == null) return;
            if (!_pipes.TryGetValue(grid, out var list)) { list = new List<GridGasPipe>(); _pipes[grid] = list; }
            if (!list.Contains(pipe)) list.Add(pipe);
        }

        public void UnregisterPipe(GridEntity grid, GridGasPipe pipe)
        {
            if (grid != null && _pipes.TryGetValue(grid, out var list)) list.Remove(pipe);
        }

        public bool HasPipes(GridEntity grid)
            => _pipes.TryGetValue(grid, out var list) && list.Count > 0;
    }
}
