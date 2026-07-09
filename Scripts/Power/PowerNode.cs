// Assets/Scripts/VoxelEngine/Power/PowerNode.cs
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Power
{
    public enum PowerNodeKind { Cable, Generator, Consumer, Battery }

    /// <summary>
    /// Base for anything that participates in a power network. Auto-registers/unregisters
    /// with the PowerNetworkManager.
    /// </summary>
    public abstract class PowerNode : MonoBehaviour
    {
        public abstract PowerNodeKind Kind { get; }

        [Tooltip("Distance at which this node will auto-connect to neighbouring nodes/cables.")]
        public float connectRadius = 3.0f;

        [Tooltip("If true, this node only accepts connections to neighbours sitting on the " +
                 "6-axis grid (±X/±Y/±Z, one grid cell away). Cables set this to true so " +
                 "they never connect diagonally or through walls. Machines/generators leave " +
                 "it false so a generator sitting next to a cable cluster can still tap in.")]
        public bool requireGridAlignedNeighbours = false;

        [Tooltip("Grid cell size used for the alignment check above. Should match BuildSystem.gridSize.")]
        public float gridSize = 1.0f;

        [Tooltip("Layers tested with a linecast between this node and a candidate neighbour. " +
                 "If anything on these layers blocks the line, the connection is rejected. " +
                 "Leave 0 (Default) to use a sensible automatic mask.")]
        public LayerMask connectionBlockingLayers = ~0;

        // Network membership — assigned by PowerNetworkManager.
        [System.NonSerialized] public PowerNetwork network;
        [System.NonSerialized] public List<PowerNode> neighbours = new();
        [System.NonSerialized] public List<PowerNode> manualLinks = new();

        /// <summary>Raised after PowerNetworkManager rebuilds topology, so visuals can refresh.</summary>
        public System.Action onNeighboursChanged;

        protected virtual void OnEnable()  { PowerNetworkManager.EnsureInstance(); PowerNetworkManager.Instance.Register(this); }
        protected virtual void OnDisable() { PowerNetworkManager.Instance?.Unregister(this); }

        public virtual bool CanLinkTo(PowerNode other)
        {
            if (other == null || other == this) return false;
            
            // Manual links always allowed.
            if (manualLinks.Contains(other)) return true;

            Vector3 a = transform.position;
            // ... (rest of the method)

            Vector3 b = other.transform.position;
            Vector3 delta = b - a;

            // 6-axis grid alignment check (only enforced when BOTH ends require it —
            // a machine without the flag can still attach to a cable on any of its faces).
            if (requireGridAlignedNeighbours && other.requireGridAlignedNeighbours)
            {
                float g = Mathf.Max(0.01f, gridSize);
                // Convert delta into grid-unit space and accept iff it is exactly one
                // step along a single cardinal axis (with a small tolerance for float drift).
                float dx = Mathf.Abs(delta.x) / g;
                float dy = Mathf.Abs(delta.y) / g;
                float dz = Mathf.Abs(delta.z) / g;
                const float EPS = 0.15f;
                int oneAxisCount = 0;
                if (Mathf.Abs(dx - 1f) < EPS) oneAxisCount++;
                else if (dx > EPS) return false;
                if (Mathf.Abs(dy - 1f) < EPS) oneAxisCount++;
                else if (dy > EPS) return false;
                if (Mathf.Abs(dz - 1f) < EPS) oneAxisCount++;
                else if (dz > EPS) return false;
                if (oneAxisCount != 1) return false;
            }

            // (Per-face PortConfig check removed — cables now auto-connect to any
            // face of any block; no per-machine I/O selection is required.)

            // Line-of-sight: no solid block may sit between the two cable centres.
            // We shrink the line slightly off both endpoints so we don't hit the
            // colliders of the cables themselves.
            float dist = delta.magnitude;
            if (dist < 0.001f) return true;
            Vector3 dir = delta / dist;
            const float SHRINK = 0.30f; // half a cable's thickness
            float castDist = Mathf.Max(0f, dist - SHRINK * 2f);
            if (castDist <= 0f) return true;
            Vector3 origin = a + dir * SHRINK;

            var hits = Physics.RaycastAll(origin, dir, castDist, connectionBlockingLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                // Ignore hits on either endpoint node and any of its renderers.
                var node = h.collider.GetComponentInParent<PowerNode>();
                if (node == this || node == other) continue;
                // A solid object (PlacedBlock or anything else) blocks the connection.
                return false;
            }
            return true;
        }

    }
}
