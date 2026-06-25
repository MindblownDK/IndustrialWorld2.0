// Assets/Scripts/VoxelEngine/Networks/SimulationManager.cs
//
// Central manager for ALL network simulations. Runs on a fixed tick rate.
// Rebuilds network graphs when topology changes (dirty flag).
// Does NOT run simulation in Update() — uses a fixed tick accumulator.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Networks
{
    public class SimulationManager : MonoBehaviour
    {
        public static SimulationManager Instance { get; private set; }

        [Header("Simulation")]
        [Tooltip("Ticks per second for network simulation.")]
        public float tickRate = 10f;

        private readonly List<ConnectionAnchor> _allAnchors = new();
        private readonly List<ResourceNetwork<float>> _floatNetworks = new();
        private readonly List<ResourceNetwork<int>> _intNetworks = new();
        private float _tickAccum;
        private bool _dirty = true;

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("SimulationManager");
            Instance = go.AddComponent<SimulationManager>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void RegisterAnchor(ConnectionAnchor a)
        {
            if (!_allAnchors.Contains(a)) { _allAnchors.Add(a); _dirty = true; }
        }

        public void UnregisterAnchor(ConnectionAnchor a)
        {
            if (_allAnchors.Remove(a)) _dirty = true;
        }

        public void SetDirty() => _dirty = true;

        private void Update()
        {
            if (_dirty) { RebuildNetworks(); _dirty = false; }

            _tickAccum += Time.deltaTime;
            float interval = 1f / tickRate;
            while (_tickAccum >= interval)
            {
                _tickAccum -= interval;
                TickAll(interval);
            }
        }

        // ── Network Graph Rebuild (BFS) ──────────────────────────

        private void RebuildNetworks()
        {
            _floatNetworks.Clear();
            _intNetworks.Clear();
            foreach (var a in _allAnchors)
            {
                a.network = null;
                a.dataNetwork = null;
            }

            // BFS from each unvisited anchor to build connected components.
            var visited = new HashSet<ConnectionAnchor>();
            foreach (var seed in _allAnchors)
            {
                if (seed == null || visited.Contains(seed)) continue;

                if (seed.networkType == NetworkType.Data)
                {
                    var net = new DataNetworkNew();
                    BFS(seed, visited, net);
                    _intNetworks.Add(net);
                }
                else
                {
                    ResourceNetwork<float> net = seed.networkType switch
                    {
                        NetworkType.Power => new PowerNetworkNew(),
                        NetworkType.Fluid => new FluidNetworkNew(NetworkType.Fluid),
                        NetworkType.Gas   => new FluidNetworkNew(NetworkType.Gas),
                        _ => null
                    };
                    if (net == null) continue;
                    BFS(seed, visited, net);
                    _floatNetworks.Add(net);
                }
            }
        }

        private void BFS<T>(ConnectionAnchor seed, HashSet<ConnectionAnchor> visited,
                             ResourceNetwork<T> network)
        {
            var queue = new Queue<ConnectionAnchor>();
            queue.Enqueue(seed);
            visited.Add(seed);

            while (queue.Count > 0)
            {
                var a = queue.Dequeue();
                if (!network.CanAccept(a)) continue;
                network.AddAnchor(a);

                // Set the network reference on the anchor.
                a.network = network;

                foreach (var nb in a.connections)
                {
                    if (nb == null || visited.Contains(nb)) continue;
                    if (nb.networkType != a.networkType) continue;
                    visited.Add(nb);
                    queue.Enqueue(nb);
                }
            }
        }

        // ── Tick ─────────────────────────────────────────────────

        private void TickAll(float dt)
        {
            foreach (var n in _floatNetworks) n.Tick(dt);
            foreach (var n in _intNetworks) n.Tick(dt);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Public API ───────────────────────────────────────────

        public IReadOnlyList<ResourceNetwork<float>> FloatNetworks => _floatNetworks;
        public IReadOnlyList<ResourceNetwork<int>> IntNetworks => _intNetworks;
    }
}
