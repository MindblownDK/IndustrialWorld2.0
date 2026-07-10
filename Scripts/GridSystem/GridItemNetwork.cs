// Assets/Scripts/VoxelEngine/GridSystem/GridItemNetwork.cs
//
// Tracks every item store on a grid (cargo containers, docking ports, …) so the
// whole ship behaves as one storage system — the master terminal and item pipes
// can see and move items across all of them, grid systems style.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridItemNetwork : MonoBehaviour
    {
        private static GridItemNetwork _instance;
        public static GridItemNetwork Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("GridItemNetwork");
                    _instance = go.AddComponent<GridItemNetwork>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private readonly Dictionary<GridEntity, List<IGridItemStore>> _stores = new();

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        // ── Generic store API ──────────────────────────────────────────────────
        public void RegisterStore(GridEntity grid, IGridItemStore store)
        {
            if (grid == null || store == null) return;
            if (!_stores.TryGetValue(grid, out var list)) { list = new List<IGridItemStore>(); _stores[grid] = list; }
            if (!list.Contains(store)) list.Add(store);
        }

        public void UnregisterStore(GridEntity grid, IGridItemStore store)
        {
            if (grid != null && _stores.TryGetValue(grid, out var list)) list.Remove(store);
        }

        public IReadOnlyList<IGridItemStore> GetStores(GridEntity grid)
            => _stores.TryGetValue(grid, out var list) ? list : System.Array.Empty<IGridItemStore>();

        // ── Backward-compatible cargo helpers ──────────────────────────────────
        public void RegisterContainer(GridEntity grid, GridCargoContainer container)
            => RegisterStore(grid, container);

        public List<GridCargoContainer> GetConnectedContainers(GridEntity grid)
        {
            var result = new List<GridCargoContainer>();
            foreach (var s in GetStores(grid)) if (s is GridCargoContainer c) result.Add(c);
            return result;
        }
    }
}
