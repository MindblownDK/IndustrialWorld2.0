// Assets/Scripts/VoxelEngine/Power/PowerNode.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Transport;

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

        /// <summary>Maximum automatically discovered links. Poles/connectors can override this.</summary>
        public virtual int MaxAutoConnections => int.MaxValue;

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

        // Shared line-of-sight raycast buffer — CanLinkTo runs once per node pair
        // per topology rebuild, and Physics.RaycastAll allocates a fresh array EVERY
        // call. With ~20 cables + machines that was hundreds of allocs per rebuild.
        private static readonly RaycastHit[] s_losBuffer = new RaycastHit[48];

        // Manual links (from manual wires)
        [System.NonSerialized] public List<PowerNode> manualLinks = new();
        [System.NonSerialized] public Dictionary<PowerNode, float> manualLinkCapacities = new();

        /// <summary>Raised after PowerNetworkManager rebuilds topology, so visuals can refresh.</summary>
        public System.Action onNeighboursChanged;

        protected virtual void OnEnable()
        {
            // Ghost prefabs are instantiated before BuildSystem strips their runtime
            // behaviours. Registering one for that frame made phantom networks and
            // surface taps possible during building previews.
            if (VoxelEngine.Building.BuildSystem.IsCreatingGhost) return;

            PowerNetworkManager.EnsureInstance();
            PowerNetworkManager.Instance.Register(this);

            // These small utility nodes can intentionally sit directly on a large
            // static machine face. The tap rebinds itself after save/load from that
            // physical contact; no fragile object reference needs persistence.
            if ((this is PowerCable
                 || GetComponent<VoxelEngine.Simulation.CompactVoltageStation>() != null)
                && GetComponent<SurfacePowerTap>() == null)
                gameObject.AddComponent<SurfacePowerTap>();
        }

        protected virtual void OnDisable()
        {
            PowerNetworkManager.Instance?.Unregister(this);
        }

        public virtual bool CanLinkTo(PowerNode other)
        {
            if (other == null || other == this) return false;

            // Manual links always allowed.
            if (manualLinks.Contains(other)) return true;

            Vector3 a = transform.position;
            Vector3 b = other.transform.position;
            Vector3 delta = b - a;

            if (requireGridAlignedNeighbours && other.requireGridAlignedNeighbours)
            {
                float g = Mathf.Max(0.01f, gridSize);
                Vector3 gridDelta = delta;
                var thisBlock = GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
                var otherBlock = other.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
                if (thisBlock != null && otherBlock != null && thisBlock.Grid != null
                    && thisBlock.Grid == otherBlock.Grid)
                {
                    g = VoxelEngine.GridSystem.GridSizeExt.CellSize(
                        VoxelEngine.GridSystem.GridSize.Small);
                    gridDelta = thisBlock.Grid.transform.InverseTransformVector(delta);
                }
                float distForGrid = gridDelta.magnitude;

                // On flat worlds, keep the strict one-cardinal-axis rule. On radial
                // planets the build grid is locally tangent to the surface, so adjacent
                // cables are often not aligned to global X/Y/Z. In that case, accepting
                // a single grid-step distance is the robust connection rule.
                bool radial = VoxelEngine.Cosmos.GravityProvider.IsRadial;
                // Energy-pipe pairs must share a real direct cardinal face.
                // A player places an intermediate pipe when an L route is wanted;
                // topology never invents a diagonal elbow between two endpoints.
                if (this is PowerCable && other is PowerCable)
                {
                    if (!VoxelEngine.Networks.PipeAdjacency.IsCardinalLinkDelta(
                            gridDelta, g, 1f, g * 0.12f)) return false;
                }
                else if (radial)
                {
                    if (distForGrid < g * 0.55f || distForGrid > g * 1.35f) return false;
                }
                else
                {
                    float dx = Mathf.Abs(gridDelta.x) / g;
                    float dy = Mathf.Abs(gridDelta.y) / g;
                    float dz = Mathf.Abs(gridDelta.z) / g;
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
            }

            float dist = delta.magnitude;
            if (dist < 0.001f) return true;
            Vector3 dir = delta / dist;
            const float SHRINK = 0.30f;
            float castDist = Mathf.Max(0f, dist - SHRINK * 2f);
            if (castDist <= 0f) return true;
            Vector3 origin = a + dir * SHRINK;

            int hitCount = Physics.RaycastNonAlloc(
                origin, dir, s_losBuffer, castDist, connectionBlockingLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                var h = s_losBuffer[i];
                if (h.collider == null) continue;
                var node = h.collider.GetComponentInParent<PowerNode>();
                if (node == this || node == other) continue;
                return false;
            }
            return true;
        }

    }

    [DisallowMultipleComponent]
    public sealed class SurfacePowerTap : MonoBehaviour
    {
        private const float BindRadius = 0.48f;
        private static readonly Collider[] s_hostProbe = new Collider[32];

        private PowerNode _node;
        private PowerNode _host;
        private Collider _hostCollider;
        private Vector3 _hostSurfacePoint;

        private void Awake()
        {
            _node = GetComponent<PowerNode>();
        }

        private void OnEnable()
        {
            if (VoxelEngine.Building.BuildSystem.IsCreatingGhost) return;
            StartCoroutine(BindAfterPlacement());
        }

        private System.Collections.IEnumerator BindAfterPlacement()
        {
            // Instantiate invokes node registration before BuildSystem adds the
            // PlacedBlock marker. Waiting for the physics step gives both static
            // collider registration and save restore one settled pose.
            yield return new WaitForFixedUpdate();
            TryBindTouchingHost();
        }

        private void OnDisable()
        {
            DisconnectHost();
        }

        /// <summary>Returns the exact host-face point used by a mounted cable's
        /// visual arm. The topology edge itself lives in PowerNode.manualLinks.</summary>
        public bool TryGetHostSurfacePoint(PowerNode expectedHost, out Vector3 point)
        {
            point = default;
            if (_host == null || _host != expectedHost) return false;
            point = _hostSurfacePoint;
            return true;
        }

        private void TryBindTouchingHost()
        {
            if (_node == null || VoxelEngine.Building.BuildSystem.IsCreatingGhost) return;

            PowerNode best = null;
            Collider bestCollider = null;
            float bestDistance = float.MaxValue;
            Vector3 position = transform.position;
            int count = Physics.OverlapSphereNonAlloc(position, BindRadius, s_hostProbe,
                ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var collider = s_hostProbe[i];
                s_hostProbe[i] = null;
                if (collider == null) continue;

                var candidate = collider.GetComponentInParent<PowerNode>();
                if (candidate == null || candidate == _node || candidate.Kind == PowerNodeKind.Cable)
                    continue;
                var placed = candidate.GetComponentInParent<VoxelEngine.Building.PlacedBlock>();
                if (placed == null || placed.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>() != null)
                    continue;

                Vector3 surface = collider.ClosestPoint(position);
                float distance = (surface - position).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = candidate;
                bestCollider = collider;
            }

            if (best == null)
            {
                DisconnectHost();
                return;
            }

            if (_host != best) DisconnectHost();
            _host = best;
            _hostCollider = bestCollider;
            _hostSurfacePoint = bestCollider != null
                ? bestCollider.ClosestPoint(transform.position)
                : best.transform.position;

            if (!_node.manualLinks.Contains(best)) _node.manualLinks.Add(best);
            if (!best.manualLinks.Contains(_node)) best.manualLinks.Add(_node);
            PowerNetworkManager.Instance?.SetDirty();
        }

        private void DisconnectHost()
        {
            if (_host == null || _node == null)
            {
                _host = null;
                _hostCollider = null;
                return;
            }
            _node.manualLinks.Remove(_host);
            _node.manualLinkCapacities.Remove(_host);
            _host.manualLinks.Remove(_node);
            _host.manualLinkCapacities.Remove(_node);
            _host = null;
            _hostCollider = null;
            PowerNetworkManager.Instance?.SetDirty();
        }
    }
}
