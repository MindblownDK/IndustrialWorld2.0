// Assets/Scripts/VoxelEngine/Transport/ItemPipeNetwork.cs
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Transport
{
    /// <summary>
    /// Singleton manager that maintains the ItemPipe neighbour graph.
    /// Same pattern as <see cref="VoxelEngine.Fluids.FluidNetworkManager"/>
    /// and <see cref="VoxelEngine.Power.PowerNetworkManager"/>.
    ///
    /// Add this component to a manager GameObject in your scene.
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
                _dirty = true;
            }
        }

        public void Unregister(ItemPipe pipe)
        {
            if (_pipes.Remove(pipe))
            {
                foreach (var p in _pipes)
                    p.neighbours.Remove(pipe);
                pipe.neighbours.Clear();
            }
        }

        private void LateUpdate()
        {
            if (!_dirty) return;
            Rebuild();
            _dirty = false;
        }

        /// <summary>
        /// Brute-force neighbour discovery by distance. Fine for &lt; 500 pipes.
        /// </summary>
        private void Rebuild()
        {
            foreach (var p in _pipes)
                p.neighbours.Clear();

            for (int i = 0; i < _pipes.Count; i++)
            {
                for (int j = i + 1; j < _pipes.Count; j++)
                {
                    var a = _pipes[i];
                    var b = _pipes[j];
                    float maxDist = Mathf.Max(a.connectRadius, b.connectRadius);
                    if (Vector3.SqrMagnitude(a.transform.position - b.transform.position)
                        <= maxDist * maxDist)
                    {
                        if (!a.neighbours.Contains(b)) a.neighbours.Add(b);
                        if (!b.neighbours.Contains(a)) b.neighbours.Add(a);
                    }
                }
            }
        }

        /// <summary>Call after moving/adding a pipe at runtime to force re-link.</summary>
        public void SetDirty() => _dirty = true;
    }
}
