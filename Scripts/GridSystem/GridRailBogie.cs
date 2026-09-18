// Assets/Scripts/VoxelEngine/GridSystem/GridRailBogie.cs
//
// TRAIN SYSTEM V2, PHASE 1 — a player-built GRID that runs on rails.
//
// WHY THIS REPLACES THE 11.15.0 TRAIN
// The old `RailTrain` was a bespoke entity: a scheduled agent walking its own private
// graph. That bought unloaded-chunk operation, but it made a train the one large
// buildable in the game that is NOT a grid. It could not be designed block by block,
// could not carry arbitrary grid blocks, could not be damaged, painted, pressurised or
// inspected, and needed a parallel console. Every future feature had to be written
// twice - once for grids, once for rails.
//
// This is the same decision the Orbital Programme already proved: a satellite is an
// ordinary grid the player DECLARES a satellite, not a separate object. A locomotive is
// now an ordinary grid the player attaches a bogie to.
//
// THE OPEN QUESTION, SETTLED
// The roadmap flagged one blocker: "how does a grid-based train keep running while its
// chunks are unloaded?" - because that property is the whole reason rail beat rovers.
//
// The answer turned out to be that the premise was wrong. Grids are NOT chunk-streamed:
// they are persistent scene objects saved by body anchor, and nothing distance-culls
// them. Rail track is `PlacedBlock`, which is likewise never distance-culled. So a
// grid-on-rails keeps ticking wherever the player is, with no dormant analytic mode
// needed at all. The feature that looked like the hard part does not need building.
//
// What a bogie still must NOT do is depend on the player being nearby for its physics to
// behave, which is why it drives the transform along the rail analytically rather than
// pushing a rigidbody and hoping.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.GridSystem
{
    /// <summary>
    /// Attaches a grid to the rail network and moves it along the track.
    ///
    /// Deliberately a COMPONENT ON THE GRID rather than a new entity type: everything that
    /// already works on a grid - blocks, power, damage, paint, pressurisation, the
    /// inspection overlay, the master terminal - keeps working with no extra code.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GridEntity))]
    public class GridRailBogie : MonoBehaviour
    {
        [Header("Performance")]
        [Tooltip("Top speed on clear track, metres per second.")]
        public float maxSpeed = 14f;

        [Tooltip("Acceleration and braking, m/s^2. Low on purpose - mass is what a train is " +
                 "for, and it should feel heavy.")]
        public float acceleration = 2.5f;

        [Tooltip("How far from a rail the grid may be and still latch onto it.")]
        public float snapRange = 6f;

        [Header("Operation")]
        [Tooltip("Drive the train forward along the rail. The direction is whichever way it " +
                 "was last travelling.")]
        public bool powered;

        [Tooltip("Reverse the direction of travel.")]
        public bool reversed;

        // ── Runtime state ────────────────────────────────────────────────────────

        /// <summary>The rail cell the grid currently occupies.</summary>
        public RailTrack CurrentTrack { get; private set; }

        /// <summary>The cell it came from, so a junction never bounces it backwards.</summary>
        private RailTrack _previousTrack;

        /// <summary>0..1 along the edge from CurrentTrack to the next cell.</summary>
        private float _edgeProgress;
        private float _speed;

        public bool IsOnRails => CurrentTrack != null;
        public float Speed => _speed;

        /// <summary>Why the train is not moving, for the terminal. Empty when it is.</summary>
        public string BlockedReason { get; private set; } = "";

        private GridEntity _grid;
        private Rigidbody _rb;

        // ── Registry ─────────────────────────────────────────────────────────────
        private static readonly List<GridRailBogie> s_all = new();
        public static IReadOnlyList<GridRailBogie> All => s_all;

        private void Awake()
        {
            _grid = GetComponent<GridEntity>();
            _rb = _grid != null ? _grid.Body : null;
        }

        private void OnEnable()
        {
            if (!s_all.Contains(this)) s_all.Add(this);
            TrySnapToTrack();
        }

        private void OnDisable()
        {
            s_all.Remove(this);
            ReleasePhysics();
        }

        // ════════════════════════════════════════════════════════════════
        //  LATCHING ON AND OFF
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Finds the nearest rail cell and latches the grid onto it.
        ///
        /// Returns false when there is no track in range, which is a legitimate state: a
        /// player can build a locomotive in a yard and drive it onto rails later.
        /// </summary>
        public bool TrySnapToTrack()
        {
            var track = RailNetwork.FindNearest(transform.position, snapRange);
            if (track == null)
            {
                BlockedReason = "No rail within range.";
                return false;
            }

            CurrentTrack = track;
            _previousTrack = null;
            _edgeProgress = 0f;
            _speed = 0f;
            BlockedReason = "";

            CapturePhysics();
            transform.position = track.RailPosition;
            return true;
        }

        /// <summary>Lifts the grid off the rails and hands it back to normal physics.</summary>
        public void Detach()
        {
            CurrentTrack = null;
            _previousTrack = null;
            _speed = 0f;
            BlockedReason = "Not on rails.";
            ReleasePhysics();
        }

        /// <summary>
        /// While railed the grid is kinematic and driven by transform.
        ///
        /// A rail is a hard constraint, and fighting the solver to hold one is how a train
        /// ends up jittering, climbing its own track or being shoved off by a collision.
        /// Taking the grid out of the solver makes the constraint exact - and, importantly,
        /// makes movement independent of whether the player is nearby to simulate it.
        /// </summary>
        private void CapturePhysics()
        {
            if (_rb == null) return;
            if (_wasKinematic.HasValue) return;    // already captured

            _wasKinematic = _rb.isKinematic;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
        }

        private void ReleasePhysics()
        {
            if (_rb == null || !_wasKinematic.HasValue) return;
            _rb.isKinematic = _wasKinematic.Value;
            _wasKinematic = null;
        }

        private bool? _wasKinematic;

        // ════════════════════════════════════════════════════════════════
        //  MOVEMENT
        // ════════════════════════════════════════════════════════════════

        private void FixedUpdate()
        {
            if (CurrentTrack == null) return;

            float dt = Time.fixedDeltaTime;

            if (!powered)
            {
                // Coast to a stop rather than halting instantly: a stationary train that
                // stops dead the frame power is cut reads as a bug, not as braking.
                _speed = Mathf.MoveTowards(_speed, 0f, acceleration * dt);
                BlockedReason = _speed > 0.01f ? "" : "Not powered.";
                if (_speed <= 0.001f) return;
            }
            else if (_grid != null && !_grid.HasPower)
            {
                _speed = Mathf.MoveTowards(_speed, 0f, acceleration * dt);
                BlockedReason = "No power on the grid.";
                if (_speed <= 0.001f) return;
            }
            else
            {
                float target = maxSpeed * Mathf.Max(0.1f, CurrentTrack.speedMultiplier);
                _speed = Mathf.MoveTowards(_speed, target, acceleration * dt);
                BlockedReason = "";
            }

            AdvanceAlongTrack(dt);
        }

        private void AdvanceAlongTrack(float dt)
        {
            var next = ResolveNextTrack();
            if (next == null)
            {
                // End of the line. Stop AT the railhead rather than running off it.
                _speed = 0f;
                _edgeProgress = 0f;
                BlockedReason = "End of track.";
                transform.position = CurrentTrack.RailPosition;
                return;
            }

            Vector3 from = CurrentTrack.RailPosition;
            Vector3 to = next.RailPosition;
            float edgeLength = Mathf.Max(0.01f, Vector3.Distance(from, to));

            _edgeProgress += _speed * dt / edgeLength;

            while (_edgeProgress >= 1f)
            {
                _edgeProgress -= 1f;
                _previousTrack = CurrentTrack;
                CurrentTrack = next;

                next = ResolveNextTrack();
                if (next == null)
                {
                    _speed = 0f;
                    _edgeProgress = 0f;
                    BlockedReason = "End of track.";
                    transform.position = CurrentTrack.RailPosition;
                    return;
                }

                from = CurrentTrack.RailPosition;
                to = next.RailPosition;
                edgeLength = Mathf.Max(0.01f, Vector3.Distance(from, to));
            }

            Vector3 position = Vector3.Lerp(from, to, _edgeProgress);
            Vector3 heading = to - from;

            // Move the rigidbody rather than the transform when one exists: MovePosition
            // keeps the physics representation in step, so anything standing on the train
            // (a player, cargo) is carried instead of being left behind.
            if (_rb != null && _rb.isKinematic)
            {
                _rb.MovePosition(position);
                if (heading.sqrMagnitude > 0.0001f)
                {
                    Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(position);
                    _rb.MoveRotation(Quaternion.LookRotation(heading.normalized, up));
                }
            }
            else
            {
                transform.position = position;
                if (heading.sqrMagnitude > 0.0001f)
                {
                    Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(position);
                    transform.rotation = Quaternion.LookRotation(heading.normalized, up);
                }
            }
        }

        /// <summary>
        /// The cell ahead. Honours switch settings, and never reverses into the cell it
        /// just came from unless the player explicitly asked to reverse.
        /// </summary>
        private RailTrack ResolveNextTrack()
        {
            if (CurrentTrack == null) return null;

            var links = CurrentTrack.Links;
            if (links.Count == 0) return null;

            // Reversing swaps which neighbour counts as "behind", so the same logic drives
            // the train both ways without a second code path.
            var behind = reversed ? ForwardCandidate() : _previousTrack;
            var ahead = CurrentTrack.NextFrom(behind);

            // A single-link cell is a railhead: NextFrom returns null and the caller stops.
            return ahead;
        }

        /// <summary>
        /// When reversing, "behind" is the cell the train would otherwise head into, so the
        /// existing NextFrom logic naturally sends it the other way.
        /// </summary>
        private RailTrack ForwardCandidate()
        {
            if (CurrentTrack == null) return null;
            var links = CurrentTrack.Links;
            for (int i = 0; i < links.Count; i++)
                if (links[i] != _previousTrack) return links[i];
            return null;
        }

        // ════════════════════════════════════════════════════════════════
        //  PERSISTENCE HELPERS
        // ════════════════════════════════════════════════════════════════

        public void CaptureState(out bool isPowered, out bool isReversed)
        {
            isPowered = powered;
            isReversed = reversed;
        }

        public void RestoreState(bool isPowered, bool isReversed)
        {
            powered = isPowered;
            reversed = isReversed;
            // Re-latch from the restored world position; the rail graph rebuilds itself
            // from placed blocks, so the cell reference cannot be saved directly.
            TrySnapToTrack();
        }

        /// <summary>Short status for the grid terminal.</summary>
        public string StatusLabel
        {
            get
            {
                if (CurrentTrack == null) return "Off rails";
                if (!string.IsNullOrEmpty(BlockedReason)) return BlockedReason;
                return $"{(reversed ? "Reverse" : "Forward")}  ·  {_speed:0.0} m/s";
            }
        }
    }
}
