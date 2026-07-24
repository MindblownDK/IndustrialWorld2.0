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
                MarkDirty();
            }
        }

        public void Unregister(ItemPipe pipe)
        {
            if (pipe == null) return;
            if (_pipes.Remove(pipe))
            {
                for (int i = 0; i < _pipes.Count; i++)
                    if (_pipes[i] != null) _pipes[i].neighbours.Remove(pipe);
                pipe.neighbours.Clear();
                VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged();
            }
        }

        private void LateUpdate()
        {
            if (!_dirty) return;
            if (Time.unscaledTime - _dirtyAt < RebuildSettleDelay) return;
            _dirty = false;
            Rebuild();
            VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged();
        }

        private void MarkDirty()
        {
            if (!_dirty) _dirtyAt = Time.unscaledTime;
            _dirty = true;
        }

        private void Rebuild()
        {
            for (int i = 0; i < _pipes.Count; i++)
                if (_pipes[i] != null) _pipes[i].neighbours.Clear();

            _pipes.RemoveAll(p => p == null);
            int n = _pipes.Count;
            if (n < 2) goto EndpointRescan;

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
                float rA = a.connectRadius;

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
                        float radiusCap = Mathf.Max(rA, b.connectRadius);
                        if (radiusCap > range) range = Mathf.Min(radiusCap, 5f);

                        if ((pa - pb).sqrMagnitude > range * range) continue;

                        Vector3 connectionDelta = VoxelEngine.Networks.PipeAdjacency.ConnectionDelta(a, b);
                        if (!VoxelEngine.Networks.PipeAdjacency.IsCardinalLinkDelta(connectionDelta, step, 5f, step * 0.35f)) continue;

                        if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(a, b)) continue;

                        if (!a.neighbours.Contains(b)) a.neighbours.Add(b);
                        if (!b.neighbours.Contains(a)) b.neighbours.Add(a);
                    }
                }
            }

        EndpointRescan:
            // Refresh each pipe's endpoint (chest/machine) connections too.
            for (int i = 0; i < _pipes.Count; i++)
            {
                var p = _pipes[i];
                if (p != null) p.ForceEndpointRescan();
            }
        }

        private static float GridStep(ItemPipe a, ItemPipe b)
        {
            var blockA = a != null ? a.GetComponentInParent<GridBlock>() : null;
            var blockB = b != null ? b.GetComponentInParent<GridBlock>() : null;
            if (blockA != null && blockB != null && blockA.Grid != null && blockA.Grid == blockB.Grid)
            {
                bool aSmall = blockA.IsPrecisionAttachment;
                bool bSmall = blockB.IsPrecisionAttachment;
                float small = GridSizeExt.CellSize(GridSize.Small);
                if (aSmall && bSmall) return small;
                if (aSmall != bSmall) return small;
                return (blockA.EffectiveCellSize + blockB.EffectiveCellSize) * 0.5f;
            }
            return VoxelEngine.Networks.PipeAdjacency.DefaultGridSize;
        }

        /// <summary>Call after moving/adding a pipe at runtime to force re-link.</summary>
        public void SetDirty() => MarkDirty();
    }
}
