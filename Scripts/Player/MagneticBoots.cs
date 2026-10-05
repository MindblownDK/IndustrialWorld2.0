// Assets/Scripts/VoxelEngine/Player/MagneticBoots.cs
//
// 14.61.0 - magnetic boots + grid-relative movement.
//
// Three jobs, all about making a ship feel like solid ground:
//
//   1. CARRY. While the jetpack is OFF and the player's feet touch a grid,
//      the player rides it: the hull's motion (linear AND rotational) is
//      applied to the CharacterController every frame, so a walking crewman
//      stays exactly where he stands while the ship flies, turns or brakes.
//
//   2. STICK. In low gravity (deep space, small moons) the boots provide the
//      "down": the hull contact normal becomes the player's up and a boot
//      force replaces gravity, so the crew walks the hull plating instead of
//      floating off it. Step off the edge and the boots release - zero-g
//      drift is by design out there.
//
//   3. REFERENCE. While the jetpack is ON near a grid, the inertial
//      dampeners null velocity RELATIVE TO THAT GRID, not relative to the
//      world: hovering beside a cruising ship means matching its speed, not
//      braking to a dead stop in space while the ship sails away.
//
// PlayerController owns the call order: it adds this component in Awake and
// pumps Tick() right before the walk/fly update, so the carry lands in the
// same frame as the player's own movement.

using UnityEngine;
using VoxelEngine.Settings;

namespace VoxelEngine.Player
{
    [DisallowMultipleComponent]
    public class MagneticBoots : MonoBehaviour
    {
        [Header("Attachment")]
        [Tooltip("Feet probe length along -up. Within this distance of a grid surface the boots grab.")]
        public float probeDistance = 2.6f;
        [Tooltip("Below this gravity (m/s²) the boots provide their own 'down' along the hull normal.")]
        public float lowGravityThreshold = 2f;
        [Tooltip("Artificial boot pull (m/s²) used as gravity in low-g. Slightly under Earth for the mag-boot feel.")]
        public float bootGravity = 9f;

        [Header("Jetpack reference")]
        [Tooltip("While flying, dampeners reference the nearest grid within this range (m).")]
        public float flyReferenceRange = 14f;

        /// <summary>Boots engaged: jetpack off + feet on a grid (carry active).</summary>
        public bool Engaged { get; private set; }
        /// <summary>Low-g hull walk: UpDirection/GravityOverride replace the planet field.</summary>
        public bool OverrideActive { get; private set; }
        public Vector3 UpDirection { get; private set; } = Vector3.up;
        public Vector3 GravityOverride { get; private set; }
        public VoxelEngine.GridSystem.GridEntity AttachedGrid { get; private set; }

        /// <summary>14.62.0 — explicit relative-dampener target (Ctrl+dampener key on a
        /// grid under the crosshair). Overrides the proximity scan at ANY range: the
        /// jetpack treats THAT grid's velocity as "at rest" until the lock is cleared.</summary>
        public VoxelEngine.GridSystem.GridEntity LockedReference { get; private set; }

        /// <summary>Grid currently serving as the dampener reference (locked target
        /// first, else the proximity grid), or null at world rest. For the motion HUD.</summary>
        public VoxelEngine.GridSystem.GridEntity ActiveReferenceGrid
        {
            get
            {
                if (LockedReference != null && LockedReference.Body != null) return LockedReference;
                return _refGrid;
            }
        }

        private CharacterController _cc;
        private PlayerController _pc;

        // Carry anchor: where the player stood, in the grid's local space.
        private bool _hasAnchor;
        private Vector3 _anchorLocal;
        private Vector3 _prevForwardLocal;

        // Fly-reference cache (OverlapSphere is not free - refresh at 4 Hz).
        private Rigidbody _refGridBody;
        private VoxelEngine.GridSystem.GridEntity _refGrid;
        private float _nextRefScan;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _pc = GetComponent<PlayerController>();
        }

        /// <summary>Pumped by PlayerController right before its walk/fly update.</summary>
        public void Tick(float dt)
        {
            if (_cc == null || !_cc.enabled || GameSettings.FlyMode)
            {
                Release();
                return;
            }

            // 14.62.0 — the PLAYER's own dampener switch decides whether the boots
            // follow a deck. The ship's dampener state is irrelevant here: a crewman
            // on a drifting, dampener-less freighter still rides it. Only turning
            // YOUR dampeners off cuts you loose.
            if (_pc != null && !_pc.DampenersOn)
            {
                Release();
                return;
            }

            // ── find the grid under the feet ──
            Vector3 up = transform.up; // the controller keeps the body aligned to its up
            var grid = ProbeGrid(up, out RaycastHit hull);
            if (grid == null || grid.Body == null)
            {
                Release();
                return;
            }

            // ── carry: apply the hull's motion since last frame ──
            if (Engaged && AttachedGrid == grid && _hasAnchor)
            {
                Vector3 carriedWorld = grid.transform.TransformPoint(_anchorLocal);
                Vector3 delta = carriedWorld - transform.position;
                if (delta.sqrMagnitude > 1e-10f && delta.sqrMagnitude < 400f)
                    _cc.Move(delta);

                // Yaw-carry: when the ship turns, the crew turns with the deck.
                Vector3 oldForward = grid.transform.TransformDirection(_prevForwardLocal);
                Vector3 newForward = Vector3.ProjectOnPlane(oldForward, up);
                Vector3 curForward = Vector3.ProjectOnPlane(transform.forward, up);
                if (newForward.sqrMagnitude > 1e-6f && curForward.sqrMagnitude > 1e-6f && _pc != null)
                {
                    float dYaw = Vector3.SignedAngle(curForward, newForward, up);
                    if (Mathf.Abs(dYaw) > 0.001f && Mathf.Abs(dYaw) < 30f) _pc.AddExternalYaw(dYaw);
                }
            }

            Engaged = true;
            AttachedGrid = grid;
            _anchorLocal = grid.transform.InverseTransformPoint(transform.position);
            _prevForwardLocal = grid.transform.InverseTransformDirection(transform.forward);
            _hasAnchor = true;

            // ── stick: in low gravity the hull normal is "down" ──
            Vector3 fieldGravity = VoxelEngine.Cosmos.GravityProvider.GetGravity(transform.position);
            if (fieldGravity.magnitude < lowGravityThreshold)
            {
                OverrideActive = true;
                UpDirection = hull.normal.sqrMagnitude > 0.5f ? hull.normal : up;
                GravityOverride = -UpDirection * bootGravity;
            }
            else
            {
                OverrideActive = false;
            }
        }

        private VoxelEngine.GridSystem.GridEntity ProbeGrid(Vector3 up, out RaycastHit hit)
        {
            Vector3 origin = transform.position + up * 0.4f;
            if (Physics.Raycast(origin, -up, out hit, probeDistance,
                                ~0, QueryTriggerInteraction.Ignore))
            {
                var grid = hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>();
                if (grid != null) return grid;
            }
            // A short spherecast forgives railings, sloped plates and small gaps.
            if (Physics.SphereCast(origin, 0.3f, -up, out hit, probeDistance,
                                   ~0, QueryTriggerInteraction.Ignore))
                return hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>();
            return null;
        }

        private void Release()
        {
            Engaged = false;
            OverrideActive = false;
            AttachedGrid = null;
            _hasAnchor = false;
        }

        /// <summary>Velocity the jetpack dampeners should treat as "at rest": the
        /// locked relative target when one is set (any range), else the nearest grid's
        /// point velocity when one is close, else zero (world rest).</summary>
        public Vector3 FlyReferenceVelocity(Vector3 position)
        {
            // Explicit lock wins — linear velocity, not point velocity: at hundreds of
            // metres the rotation lever arm would turn a slow tumble into nonsense.
            if (LockedReference != null && LockedReference.Body != null)
                return LockedReference.Body.linearVelocity;

            if (Time.unscaledTime >= _nextRefScan)
            {
                _nextRefScan = Time.unscaledTime + 0.25f;
                _refGridBody = null;
                _refGrid = null;
                float best = float.MaxValue;
                var hits = Physics.OverlapSphere(position, flyReferenceRange, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits.Length; i++)
                {
                    var g = hits[i].GetComponentInParent<VoxelEngine.GridSystem.GridEntity>();
                    if (g == null || g.Body == null) continue;
                    float d = (hits[i].bounds.ClosestPoint(position) - position).sqrMagnitude;
                    if (d < best) { best = d; _refGridBody = g.Body; _refGrid = g; }
                }
            }
            return _refGridBody != null ? _refGridBody.GetPointVelocity(position) : Vector3.zero;
        }

        /// <summary>Ctrl+dampener key — lock/unlock the grid under the crosshair as the
        /// relative dampener target. Locking a moving grid makes the jetpack match ITS
        /// velocity ("relative dampeners"); pressing again, or with nothing in sight,
        /// clears the lock back to world rest.</summary>
        public void ToggleReferenceLock()
        {
            var cam = Camera.main;
            Transform eye = cam != null ? cam.transform : transform;

            VoxelEngine.GridSystem.GridEntity target = null;
            float best = float.MaxValue;
            var hits = Physics.RaycastAll(eye.position, eye.forward, 2500f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                // Never lock yourself: skip anything that is part of this player.
                if (hits[i].collider.GetComponentInParent<PlayerController>() == _pc && _pc != null) continue;
                var g = hits[i].collider.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>();
                if (g == null || g.Body == null) continue;
                if (hits[i].distance < best) { best = hits[i].distance; target = g; }
            }

            if (target != null && target != LockedReference)
            {
                LockedReference = target;
                VoxelEngine.UI.BuildFeedbackHud.Show("Relative Dampeners",
                    $"Matching velocity: {target.name}", null, new Color(0.35f, 0.90f, 0.80f));
            }
            else if (LockedReference != null)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Relative Dampeners",
                    $"Lock cleared: {LockedReference.name}", null, new Color(1f, 0.70f, 0.25f));
                LockedReference = null;
            }
            else
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Relative Dampeners",
                    "No grid under the crosshair", null, new Color(1f, 0.70f, 0.25f));
            }
        }
    }
}
