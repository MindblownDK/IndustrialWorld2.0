// Assets/Scripts/VoxelEngine/Transport/ItemPipeNetwork.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Transport
{
    /// <summary>
    /// Singleton manager that maintains the ItemPipe neighbour graph.
    /// Uses a 5 m spatial hash for O(N) neighbour discovery instead of
    /// the old O(N^2) double loop that lagged at high pipe counts.
    /// </summary>
    public class ItemPipeNetwork : MonoBehaviour
    {
        public static ItemPipeNetwork Instance { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("ItemPipeNetwork");
            Instance = go.AddComponent<ItemPipeNetwork>();
            DontDestroyOnLoad(go);
        }

        private readonly List<ItemPipe> _pipes = new();
        private bool _dirty;
        private float _dirtyAt = -1f;
        private readonly List<Vector3> _dirtyPositions = new(4);
        private bool _globalVisualRefresh;
        private const float RebuildSettleDelay = 0.12f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Register(ItemPipe pipe)
        {
            if (pipe != null && !_pipes.Contains(pipe))
            {
                _pipes.Add(pipe);
                MarkDirty(pipe.transform.position);
            }
        }

        public void Unregister(ItemPipe pipe)
        {
            if (pipe == null) return;
            Vector3 formerPosition = pipe.transform.position;
            if (_pipes.Remove(pipe))
            {
                for (int i = 0; i < _pipes.Count; i++)
                    if (_pipes[i] != null) _pipes[i].neighbours.Remove(pipe);
                pipe.neighbours.Clear();
                MarkDirty(formerPosition);
            }
        }

        private void LateUpdate()
        {
            if (!_dirty) return;
            if (Time.unscaledTime - _dirtyAt < RebuildSettleDelay) return;
            _dirty = false;
            Rebuild();
            RefreshAffectedVisuals();
        }

        private void MarkDirty(Vector3? position = null)
        {
            if (!_dirty) _dirtyAt = Time.unscaledTime;
            _dirty = true;
            if (!position.HasValue)
            {
                _globalVisualRefresh = true;
                return;
            }

            Vector3 p = position.Value;
            for (int i = 0; i < _dirtyPositions.Count; i++)
                if ((_dirtyPositions[i] - p).sqrMagnitude < 0.01f) return;
            _dirtyPositions.Add(p);
        }

        private void RefreshAffectedVisuals()
        {
            if (_globalVisualRefresh || _dirtyPositions.Count == 0)
            {
                for (int i = 0; i < _pipes.Count; i++)
                    if (_pipes[i] != null) _pipes[i].ForceEndpointRescan();
                VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged();
            }
            else
            {
                for (int d = 0; d < _dirtyPositions.Count; d++)
                {
                    Vector3 changed = _dirtyPositions[d];
                    ItemPipe changedPipe = FindPipeAt(changed);
                    float localRadius = changedPipe != null
                        ? GridStep(changedPipe, changedPipe) * 5.15f + 0.25f
                        : 0f;

                    // Only the newly registered pipe needs an immediate expensive
                    // container-corridor scan. Nearby pipes already retain their endpoint
                    // cache; their pipe-to-pipe arms update from the rebuilt neighbour graph.
                    if (changedPipe != null) changedPipe.ForceEndpointRescan();
                    VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged(changed, localRadius);
                }
            }

            _dirtyPositions.Clear();
            _globalVisualRefresh = false;
        }

        private ItemPipe FindPipeAt(Vector3 position)
        {
            const float ExactRegistrationDistanceSqr = 0.04f;
            ItemPipe nearest = null;
            float nearestSqr = ExactRegistrationDistanceSqr;
            for (int i = 0; i < _pipes.Count; i++)
            {
                var pipe = _pipes[i];
                if (pipe == null) continue;
                float distance = (pipe.transform.position - position).sqrMagnitude;
                if (distance > nearestSqr) continue;
                nearestSqr = distance;
                nearest = pipe;
            }
            return nearest;
        }

        private void Rebuild()
        {
            for (int i = 0; i < _pipes.Count; i++)
                if (_pipes[i] != null) _pipes[i].neighbours.Clear();

            _pipes.RemoveAll(p => p == null);
            int n = _pipes.Count;
            if (n < 2) return;

            // Five-cell same-plane links fit inside this cell or its immediate
            // neighbours; coplanar validation below prevents off-plane joins.
            const float CELL = 5f;
            const float CELL_INV = 1f / CELL;
            var hash = new Dictionary<Vector3Int, List<ItemPipe>>(n * 2);
            Vector3Int Cell(Vector3 p) => new Vector3Int(
                Mathf.FloorToInt(p.x * CELL_INV),
                Mathf.FloorToInt(p.y * CELL_INV),
                Mathf.FloorToInt(p.z * CELL_INV));

            for (int i = 0; i < n; i++)
            {
                var p = _pipes[i];
                var k = Cell(p.transform.position);
                if (!hash.TryGetValue(k, out var bucket)) hash[k] = bucket = new List<ItemPipe>(4);
                bucket.Add(p);
            }

            // Index map for O(1) "is this my senior pair" test so we don't
            // double-process pairs like the old loop did.
            var index = new Dictionary<ItemPipe, int>(n);
            for (int i = 0; i < n; i++) index[_pipes[i]] = i;

            for (int i = 0; i < n; i++)
            {
                var a = _pipes[i];
                if (a == null) continue;
                Vector3 pa = a.transform.position;
                var c0 = Cell(pa);
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!hash.TryGetValue(new Vector3Int(c0.x + dx, c0.y + dy, c0.z + dz), out var bucket)) continue;
                    for (int bi = 0; bi < bucket.Count; bi++)
                    {
                        var b = bucket[bi];
                        if (b == null || b == a) continue;
                        if (index.TryGetValue(b, out int j) && j <= i) continue;

                        Vector3 pb = b.transform.position;
                        float step = GridStep(a, b);
                        float range = step * 5.1f;
                        if ((pa - pb).sqrMagnitude > range * range) continue;

                        Vector3 connectionDelta = VoxelEngine.Networks.PipeAdjacency.ConnectionDelta(a, b);
                        if (!VoxelEngine.Networks.PipeAdjacency.IsCoplanarPipeLinkDelta(connectionDelta, step, 5f, step * 0.18f)) continue;

                        if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(a, b)) continue;

                        if (!a.neighbours.Contains(b)) a.neighbours.Add(b);
                        if (!b.neighbours.Contains(a)) b.neighbours.Add(a);
                    }
                }
            }

        }

        private static float GridStep(ItemPipe a, ItemPipe b)
        {
            var blockA = (a != null ? a.GetComponentInParent<GridBlock>() : null);
            var blockB = b != null ? b.GetComponentInParent<GridBlock>() : null;
            if (blockA != null && blockB != null && blockA.Grid != null && blockA.Grid == blockB.Grid)
            {
                // All grid pipe↔pipe links use the Detail lattice step, regardless
                // of whether an old prefab forgot to mark itself as a precision
                // attachment. This prevents one-left + one-up diagonal links from
                // passing under the loose structural-grid tolerance.
                return GridSizeExt.CellSize(GridSize.Small);
            }
            return VoxelEngine.Networks.PipeAdjacency.DefaultGridSize;
        }

        public void SetDirty() => MarkDirty();
        public void SetDirty(Vector3 changedPosition) => MarkDirty(changedPosition);
    }
}
