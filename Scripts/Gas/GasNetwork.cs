// Assets/Scripts/VoxelEngine/Gas/GasNetwork.cs
//
// Manages gas pipe connectivity. Transfers gas between producers (reactors,
// electrolysers) and consumers (turbines, engines) via connected GasTanks.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Gas
{
    public class GasNetwork : MonoBehaviour
    {
        public static GasNetwork Instance { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("GasNetwork");
            Instance = go.AddComponent<GasNetwork>();
            DontDestroyOnLoad(go);
        }
        private readonly List<GasPipe> _pipes = new();
        private bool _dirty;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void Register(GasPipe p)
        {
            if (!_pipes.Contains(p)) { _pipes.Add(p); _dirty = true; }
        }

        public void Unregister(GasPipe p)
        {
            if (_pipes.Remove(p))
            {
                foreach (var nb in _pipes) nb.neighbours.Remove(p);
                p.neighbours.Clear();
            }
        }

        private void LateUpdate()
        {
            if (_dirty) { Rebuild(); _dirty = false; }
        }

        private void Rebuild()
        {
            foreach (var p in _pipes) p.neighbours.Clear();
            for (int i = 0; i < _pipes.Count; i++)
            for (int j = i + 1; j < _pipes.Count; j++)
            {
                var a = _pipes[i]; var b = _pipes[j];
                float r = Mathf.Max(a.connectRadius, b.connectRadius);
                Vector3 pa = a.transform.position, pb = b.transform.position;

                // Distance gate is cheap, do it first.
                if ((pa - pb).sqrMagnitude > r * r) continue;

                // STRICT cardinal-neighbour gate (mirrors the wire renderer). On grids,
                // pipes are spaced by the grid cell size (2.5m for large grids), not 1m.
                float step = GridStep(a, b);
                if (!VoxelEngine.Networks.PipeAdjacency.IsCardinalNeighbour(pa, pb, step, step * 0.35f)) continue;

                // Wrench blacklist — player wrenched these two apart; honour it
                // until a wrench reconnect or one of them is broken/replaced.
                if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(a, b)) continue;

                a.neighbours.Add(b); b.neighbours.Add(a);
            }
        }

        /// <summary>Find a GasTank of the given type reachable from a position via gas pipes.</summary>
        public GasTank FindTankNear(Vector3 origin, GasType type, bool forOutput, float searchDist = 3f)
        {
            // Direct adjacency check first.
            var hits = Physics.OverlapSphere(origin, searchDist);
            foreach (var col in hits)
            {
                var tank = col.GetComponent<GasTank>();
                if (tank != null)
                {
                    if (forOutput && tank.allowOutput && (tank.storedGasType == type || tank.storedGasType == GasType.None) && tank.storedAmount > 0)
                        return tank;
                    if (!forOutput && tank.acceptInput && (tank.storedGasType == type || tank.storedGasType == GasType.None))
                        return tank;
                }
            }

            // BFS through pipe network.
            foreach (var startPipe in _pipes)
            {
                if ((startPipe.transform.position - origin).sqrMagnitude > searchDist * searchDist) continue;
                var visited = new HashSet<GasPipe>();
                var queue = new Queue<GasPipe>();
                queue.Enqueue(startPipe); visited.Add(startPipe);
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    var tankHits = Physics.OverlapSphere(cur.transform.position, cur.connectRadius);
                    foreach (var col in tankHits)
                    {
                        var tank = col.GetComponent<GasTank>();
                        if (tank == null) continue;
                        if (forOutput && tank.allowOutput && tank.storedGasType == type && tank.storedAmount > 0)
                            return tank;
                        if (!forOutput && tank.acceptInput && (tank.storedGasType == type || tank.storedGasType == GasType.None))
                            return tank;
                    }
                    foreach (var nb in cur.neighbours)
                        if (visited.Add(nb)) queue.Enqueue(nb);
                }
            }
            return null;
        }

        private static float GridStep(GasPipe a, GasPipe b)
        {
            var blockA = a != null ? a.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>() : null;
            var blockB = b != null ? b.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>() : null;
            if (blockA != null && blockB != null && blockA.Grid != null && blockA.Grid == blockB.Grid)
                return (blockA.EffectiveCellSize + blockB.EffectiveCellSize) * 0.5f;
            return VoxelEngine.Networks.PipeAdjacency.DefaultGridSize;
        }

        public void SetDirty() => _dirty = true;
        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
