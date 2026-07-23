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
                Vector3 pa = a.transform.position, pb = b.transform.position;
                float step = GridStep(a, b);
                float range = Mathf.Max(Mathf.Max(a.connectRadius, b.connectRadius), step * 5f);

                // Distance gate is cheap, do it first.
                if ((pa - pb).sqrMagnitude > range * range) continue;

                // Shared Grid alignment ignores individual pipe rotation. World pipes
                // continue using world-grid axes.
                Vector3 connectionDelta = VoxelEngine.Networks.PipeAdjacency.ConnectionDelta(a, b);
                if (!VoxelEngine.Networks.PipeAdjacency.IsCardinalLinkDelta(connectionDelta, step, 5f, step * 0.35f)) continue;

                // Wrench blacklist — player wrenched these two apart; honour it
                // until a wrench reconnect or one of them is broken/replaced.
                if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(a, b)) continue;

                a.neighbours.Add(b); b.neighbours.Add(a);
            }
        }

        /// <summary>Find a GasTank of the given type reachable from a position via gas pipes.</summary>
        public GasTank FindTankNear(Vector3 origin, GasType type, bool forOutput, float searchDist = 3f,
            float corridorStep = 0f)
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

            // Five-cell cardinal corridor straight off the consumer's port: a tank
            // parked up to five lattice cells in a straight row from the port counts
            // as plugged in — no pipe needs to physically hump the tank shell.
            float step = corridorStep > 0.0001f ? corridorStep : Networks.PipeAdjacency.DefaultGridSize;
            var viaPort = ProbeTankCardinal(origin, null, step, type, forOutput);
            if (viaPort != null) return viaPort;

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
                    var block = cur.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
                    // Five LATTICE cells — on a construct the probe honours the grid's
                    // own cell size (Large 2.5 m), matching how players count spaces.
                    float pipeStep = block != null && block.Grid != null
                        ? VoxelEngine.GridSystem.GridSizeExt.CellSize(block.Grid.gridSize)
                        : GridStep(cur, cur);
                    Transform frame = block != null && block.Grid != null ? block.Grid.transform : null;
                    // Short radius (touching tanks) + the five-cell corridor.
                    var near = ProbeTankSphere(cur.transform.position, cur.connectRadius, type, forOutput);
                    if (near != null) return near;
                    var viaPipe = ProbeTankCardinal(cur.transform.position, frame, pipeStep, type, forOutput);
                    if (viaPipe != null) return viaPipe;
                    foreach (var nb in cur.neighbours)
                        if (visited.Add(nb)) queue.Enqueue(nb);
                }
            }
            return null;
        }

        private static readonly Collider[] s_tankProbe = new Collider[16];

        private static GasTank ProbeTankSphere(Vector3 centre, float radius, GasType type, bool forOutput)
        {
            var tankHits = Physics.OverlapSphere(centre, radius);
            foreach (var col in tankHits)
            {
                var tank = col != null ? col.GetComponent<GasTank>() ?? col.GetComponentInParent<GasTank>() : null;
                if (tank == null) continue;
                if (forOutput && tank.allowOutput && tank.storedGasType == type && tank.storedAmount > 0)
                    return tank;
                if (!forOutput && tank.acceptInput && (tank.storedGasType == type || tank.storedGasType == GasType.None))
                    return tank;
            }
            return null;
        }

        /// <summary>Tank reachable from <paramref name="origin"/> along a straight
        /// cardinal lattice row (max five cells) — deduction of "connects from a
        /// distance in a valid direction" for gas endpoints.</summary>
        private static GasTank ProbeTankCardinal(Vector3 origin, Transform gridFrame, float step, GasType type, bool forOutput)
        {
            GasTank found = null;
            Networks.PipeAdjacency.ProbeCardinal(origin, gridFrame, step, 5, s_tankProbe, col =>
            {
                var tank = col.GetComponent<GasTank>();
                if (tank == null) tank = col.GetComponentInParent<GasTank>();
                if (tank == null) return false;
                if (forOutput && tank.allowOutput && (tank.storedGasType == type || tank.storedGasType == GasType.None) && tank.storedAmount > 0)
                {
                    found = tank; return true;
                }
                if (!forOutput && tank.acceptInput && (tank.storedGasType == type || tank.storedGasType == GasType.None))
                {
                    found = tank; return true;
                }
                return false;
            });
            return found;
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
