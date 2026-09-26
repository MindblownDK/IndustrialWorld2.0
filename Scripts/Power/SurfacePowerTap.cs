// Assets/Scripts/VoxelEngine/Power/SurfacePowerTap.cs
//
// A lightweight runtime helper for power cables and compact voltage terminals
// mounted directly to a static powered machine. It discovers the touching host
// after placement/load and creates one intentional topology edge, so a cable
// can tap the side of a large battery/consumer instead of requiring the player
// to find the machine transform's hidden centre cell.

using System.Collections;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Power
{
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
            if (BuildSystem.IsCreatingGhost) return;
            StartCoroutine(BindAfterPlacement());
        }

        private IEnumerator BindAfterPlacement()
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
            if (_node == null || BuildSystem.IsCreatingGhost) return;

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
                var placed = candidate.GetComponentInParent<PlacedBlock>();
                if (placed == null || placed.GetComponentInParent<GridEntity>() != null)
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
