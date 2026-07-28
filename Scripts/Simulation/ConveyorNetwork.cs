// Assets/Scripts/VoxelEngine/Simulation/ConveyorNetwork.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — CONVEYOR NETWORK REGISTRY                   ║
// ║  Tracks all active conveyor belts in the scene. Provides fast   ║
// ║  lookup for connection scanning and statistics.                 ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Singleton registry of all active conveyor belts. Belts self-register
    /// on enable and unregister on disable. The network doesn't simulate —
    /// each belt runs its own Update() loop. This registry exists for:
    ///   • Connection scanning (find neighbours fast)
    ///   • UI statistics (total belts, items in transit)
    ///   • Save/load serialization
    /// </summary>
    public class ConveyorNetwork : MonoBehaviour
    {
        public static ConveyorNetwork Instance { get; private set; }

        private readonly List<ConveyorBelt> _belts = new(256);

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("ConveyorNetwork");
            Instance = go.AddComponent<ConveyorNetwork>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Register(ConveyorBelt belt)
        {
            if (belt != null && !_belts.Contains(belt))
                _belts.Add(belt);
        }

        public void Unregister(ConveyorBelt belt)
        {
            _belts.Remove(belt);
        }

        /// <summary>Total active belt segments.</summary>
        public int BeltCount => _belts.Count;

        /// <summary>Total items currently riding across all belts.</summary>
        public int TotalItemsInTransit
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _belts.Count; i++)
                {
                    if (_belts[i] != null)
                        total += _belts[i].Items.Count;
                }
                return total;
            }
        }

        /// <summary>Read-only access to all belts (for save/load, UI).</summary>
        public IReadOnlyList<ConveyorBelt> Belts => _belts;

        /// <summary>
        /// Find the belt closest to a world position within a given radius.
        /// Used by the build system to snap new belts to existing segments.
        /// </summary>
        public ConveyorBelt FindNearestBelt(Vector3 worldPos, float maxDist = 2f)
        {
            ConveyorBelt best = null;
            float bestDistSqr = maxDist * maxDist;

            for (int i = 0; i < _belts.Count; i++)
            {
                var b = _belts[i];
                if (b == null) continue;
                float d = (b.transform.position - worldPos).sqrMagnitude;
                if (d < bestDistSqr)
                {
                    bestDistSqr = d;
                    best = b;
                }
            }
            return best;
        }
    }
}
