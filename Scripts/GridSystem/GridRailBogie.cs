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

        // ── Path history, for followers ──────────────────────────────────────────
        //
        // A coupled wagon must retrace the EXACT route the leader took, not trail it like
        // a rope. A rope-style follower (hold a fixed distance from the leader's current
        // position) cuts every corner and ends up off the track on any curve.
        //
        // So the leader records where it has been, and each follower samples that trail at
        // its own distance back. This is the standard solution and it is why followers need
        // no track logic of their own - they inherit the leader's routing, including which
        // way a switch was thrown, for free.
        private readonly List<PathSample> _trail = new(256);
        private float _distanceTravelled;

        private readonly struct PathSample
        {
            public readonly Vector3 Position;
            public readonly Vector3 Heading;
            public readonly float Distance;

            public PathSample(Vector3 position, Vector3 heading, float distance)
            {
                Position = position; Heading = heading; Distance = distance;
            }
        }

        /// <summary>How much trail to keep, in metres. Longer than any sane consist.</summary>
        private const float MaxTrailMetres = 240f;

        /// <summary>Total distance this bogie has travelled, for follower spacing.</summary>
        public float DistanceTravelled => _distanceTravelled;

        /// <summary>
        /// Position and heading this bogie occupied <paramref name="metresBack"/> ago.
        /// Returns false when the trail is not yet long enough - a freshly coupled wagon
        /// then simply holds station until the leader has moved far enough to place it.
        /// </summary>
        public bool TrySamplePath(float metresBack, out Vector3 position, out Vector3 heading)
        {
            position = transform.position;
            heading = transform.forward;

            if (_trail.Count == 0) return false;

            float target = _distanceTravelled - Mathf.Max(0f, metresBack);

            // Older samples are at the FRONT of the list, newest at the back.
            if (target <= _trail[0].Distance) return false;

            for (int i = _trail.Count - 1; i >= 0; i--)
            {
                if (_trail[i].Distance > target) continue;

                // Interpolate between this sample and the next for smooth motion; snapping
                // to whole samples makes a wagon visibly stutter at low speed.
                var a = _trail[i];
                if (i + 1 >= _trail.Count)
                {
                    position = a.Position;
                    heading = a.Heading;
                    return true;
                }

                var b = _trail[i + 1];
                float span = Mathf.Max(0.0001f, b.Distance - a.Distance);
                float t = Mathf.Clamp01((target - a.Distance) / span);

                position = Vector3.Lerp(a.Position, b.Position, t);
                heading = Vector3.Slerp(a.Heading, b.Heading, t);
                return true;
            }

            return false;
        }

        private void RecordTrail(Vector3 position, Vector3 heading)
        {
            // Only record on genuine movement, or a stationary train fills the buffer with
            // duplicate samples and pushes its own history out of range.
            if (_trail.Count > 0)
            {
                var last = _trail[_trail.Count - 1];
                if ((last.Position - position).sqrMagnitude < 0.0004f) return;
            }

            _trail.Add(new PathSample(position, heading, _distanceTravelled));

            // Drop history the longest possible consist can no longer reach.
            while (_trail.Count > 2 && _distanceTravelled - _trail[0].Distance > MaxTrailMetres)
                _trail.RemoveAt(0);
        }

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

            // Break the chain cleanly in BOTH directions. A destroyed wagon that stayed
            // referenced would leave the grid ahead thinking it still has something behind
            // it (blocking a re-couple) and the grid behind following a corpse.
            if (LeadBogie != null) LeadBogie.TrailBogie = TrailBogie;
            if (TrailBogie != null)
            {
                TrailBogie.LeadBogie = LeadBogie;
                // Losing a wagon mid-consist must not silently weld the gap shut: the one
                // behind inherits the removed wagon's spacing so it does not lurch forward.
                if (LeadBogie != null) TrailBogie._couplingGap += _couplingGap;
                else TrailBogie.Uncouple();
            }
            LeadBogie = null;
            TrailBogie = null;

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
            // A coupled wagon is driven entirely by its leader's recorded path, so it must
            // NOT run the track logic below. Two bogies both resolving switches would let a
            // consist split itself across a junction.
            if (LeadBogie != null) { FollowLeader(); return; }

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

            _distanceTravelled += _speed * dt;
            RecordTrail(position, heading.sqrMagnitude > 0.0001f ? heading.normalized : transform.forward);

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
        //  COUPLING  (multi-car consists)
        // ════════════════════════════════════════════════════════════════

        /// <summary>The bogie immediately ahead of this one, or null when this is the head.</summary>
        public GridRailBogie LeadBogie { get; private set; }

        /// <summary>The bogie immediately behind, or null when this is the tail.</summary>
        public GridRailBogie TrailBogie { get; private set; }

        /// <summary>Distance to hold behind the leader, in metres. Set when coupling.</summary>
        private float _couplingGap;

        /// <summary>True when this bogie leads a consist rather than being towed.</summary>
        public bool IsConsistHead => LeadBogie == null;

        /// <summary>How many grids are in this consist, counting from the head.</summary>
        public int ConsistLength
        {
            get
            {
                int n = 1;
                var next = TrailBogie;
                // Bounded rather than while(true): a corrupted cycle must not hang the game.
                while (next != null && n < 64) { n++; next = next.TrailBogie; }
                return n;
            }
        }

        /// <summary>
        /// Couples this grid behind <paramref name="leader"/>.
        ///
        /// Returns false with a reason rather than silently failing, because "I pressed
        /// couple and nothing happened" is indistinguishable from a bug.
        /// </summary>
        public bool TryCoupleTo(GridRailBogie leader, out string reason)
        {
            if (leader == null) { reason = "No locomotive selected."; return false; }
            if (leader == this) { reason = "A construct cannot couple to itself."; return false; }
            if (LeadBogie != null) { reason = "Already coupled to something."; return false; }
            if (leader.TrailBogie != null) { reason = $"{leader.name} already has a wagon behind it."; return false; }
            if (!leader.IsOnRails) { reason = "The locomotive is not on rails."; return false; }

            // Walk the chain forward: coupling to something already behind us would form a
            // closed loop, and every consist walk in this file would then never terminate.
            var ahead = leader;
            int guard = 0;
            while (ahead != null && guard++ < 64)
            {
                if (ahead == this) { reason = "That would form a loop."; return false; }
                ahead = ahead.LeadBogie;
            }

            float gap = Vector3.Distance(transform.position, leader.transform.position);
            if (gap > MaxCouplingRange)
            {
                reason = $"Too far apart ({gap:0.#} m). Move within {MaxCouplingRange:0} m.";
                return false;
            }

            LeadBogie = leader;
            leader.TrailBogie = this;

            // Hold the spacing the player actually parked at, so a consist keeps the shape
            // they built rather than snapping to an arbitrary constant.
            _couplingGap = Mathf.Max(MinCouplingGap, gap);

            // A towed wagon must not also try to drive: two powered bogies on one consist
            // fight each other and the trailing one wins on alternate frames.
            powered = false;
            CapturePhysics();

            reason = "";
            return true;
        }

        /// <summary>Uncouples this grid from the one ahead, leaving both free.</summary>
        public void Uncouple()
        {
            if (LeadBogie != null) LeadBogie.TrailBogie = null;
            LeadBogie = null;

            // Re-latch to whatever cell it is standing on, so an uncoupled wagon is
            // immediately drivable rather than stranded off-network.
            if (CurrentTrack == null) TrySnapToTrack();
        }

        /// <summary>Uncouples everything behind this bogie as well, for a full breakup.</summary>
        public void UncoupleAll()
        {
            var next = TrailBogie;
            int guard = 0;
            while (next != null && guard++ < 64)
            {
                var following = next.TrailBogie;
                next.Uncouple();
                next = following;
            }
        }

        public const float MaxCouplingRange = 12f;
        private const float MinCouplingGap = 2f;

        /// <summary>
        /// Places this wagon on the leader's recorded path at its coupling distance.
        ///
        /// Distance is accumulated along the CHAIN, not measured straight-line, so the
        /// third wagon sits three gaps back along the actual route rather than three gaps
        /// as the crow flies - which is the difference between a train on a curve and a
        /// train cutting across the inside of it.
        /// </summary>
        private void FollowLeader()
        {
            var head = LeadBogie;
            float distanceBack = _couplingGap;

            int guard = 0;
            while (head != null && head.LeadBogie != null && guard++ < 64)
            {
                distanceBack += head._couplingGap;
                head = head.LeadBogie;
            }

            if (head == null) return;

            if (!head.TrySamplePath(distanceBack, out Vector3 position, out Vector3 heading))
            {
                // The leader has not travelled far enough to have history that far back.
                // Hold station rather than snapping to the leader and telescoping the train.
                return;
            }

            _speed = head.Speed;
            CurrentTrack = head.CurrentTrack;   // keeps readouts honest about where it is
            BlockedReason = "";

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(position);
            var rotation = heading.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(heading.normalized, up)
                : transform.rotation;

            if (_rb != null && _rb.isKinematic)
            {
                _rb.MovePosition(position);
                _rb.MoveRotation(rotation);
            }
            else
            {
                transform.position = position;
                transform.rotation = rotation;
            }

            // A wagon keeps its own trail so the wagon behind IT can follow in turn.
            _distanceTravelled = head.DistanceTravelled - distanceBack;
            RecordTrail(position, heading);
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
                if (LeadBogie != null)
                    return $"Coupled  ·  towed at {_speed:0.0} m/s";
                if (CurrentTrack == null) return "Off rails";
                if (!string.IsNullOrEmpty(BlockedReason)) return BlockedReason;

                int cars = ConsistLength;
                string consist = cars > 1 ? $"  ·  {cars} cars" : "";
                return $"{(reversed ? "Reverse" : "Forward")}  ·  {_speed:0.0} m/s{consist}";
            }
        }

        /// <summary>
        /// The nearest other bogie this one could couple to, for the console's one-press
        /// couple button. Prefers the closest so a yard full of wagons behaves predictably.
        /// </summary>
        public GridRailBogie FindCouplingCandidate()
        {
            GridRailBogie best = null;
            float bestSq = MaxCouplingRange * MaxCouplingRange;

            for (int i = 0; i < s_all.Count; i++)
            {
                var other = s_all[i];
                if (other == null || other == this) continue;
                if (other.TrailBogie != null) continue;          // already has a wagon behind
                if (other.Grid == Grid) continue;                // same construct

                float d = (other.transform.position - transform.position).sqrMagnitude;
                if (d >= bestSq) continue;

                bestSq = d;
                best = other;
            }
            return best;
        }

        /// <summary>Exposed so the console can show which construct it would couple to.</summary>
        public GridEntity Grid => _grid != null ? _grid : _grid = GetComponent<GridEntity>();
    }
}
