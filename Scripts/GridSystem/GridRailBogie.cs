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
using System.Linq;
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

        [Header("Load")]
        [Tooltip("Mass in kg one bogie carries before it starts slowing the train. Adding " +
                 "bogies raises the total the consist can haul at full speed.")]
        public float ratedLoadKg = 6000f;

        [Tooltip("Slowest fraction of top speed an overloaded train can still manage. Never " +
                 "zero: a train that cannot move at all reads as a bug, not as overloaded.")]
        [Range(0.05f, 1f)] public float minLoadSpeedFactor = 0.15f;

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

        // ── Destination routing (12.1.0) ────────────────────────────────────────
        //
        // A schedule needs "go to THIS station", not "follow whatever the points happen to
        // do". The path is solved once with the same A* the planner uses, then honoured cell
        // by cell; where the path has nothing to say (beyond the destination, or after a
        // player reroutes the train by hand) the ordinary switch rules take over again.
        private readonly List<RailTrack> _path = new(64);
        private RailTrack _destination;

        /// <summary>The cell this bogie is currently routed to, or null when roaming.</summary>
        public RailTrack Destination => _destination;

        /// <summary>Raised when the bogie enters its destination cell.</summary>
        public event System.Action<GridRailBogie> Arrived;

        /// <summary>
        /// Routes the bogie to <paramref name="goal"/> over the rail graph. Returns false when
        /// no connected route exists - the caller reports that rather than the train sitting
        /// down without saying why.
        /// </summary>
        public bool SetDestination(RailTrack goal)
        {
            if (goal == null || CurrentTrack == null) return false;
            if (!VoxelEngine.Building.RailNetwork.TryFindPath(CurrentTrack, goal, _path))
            {
                _path.Clear();
                return false;
            }
            _destination = goal;
            return true;
        }

        /// <summary>Drops any routed destination and hands control back to the points.</summary>
        public void ClearDestination()
        {
            _destination = null;
            _path.Clear();
        }

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

        private float _nextAutoSnap;

        // ── Berth servicing (12.3.0) ───────────────────────────────────────────
        // A train standing at a working station trades cargo with it. Before this
        // existed, ServiceTrain had no caller at all: trains arrived, waited and
        // left without a single item moving, and the hold waits in a schedule were
        // watching a hold nothing touched. The pass runs at 4 Hz while the train
        // is effectively stopped, over every cargo store on the grid.
        private float _nextService;
        private float _nextStoreScan;
        private readonly List<IGridItemStore> _stores = new();
        private RailStation _serviceStation;
        private float _serviceTotal;

        /// <summary>The station this train is currently berthed at and trading with, if any.</summary>
        public RailStation ServicingStation => _serviceStation;

        /// <summary>One player-facing line about the transfer in progress.</summary>
        public string ServiceNote { get; private set; } = "";

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

            // A destroyed train MUST drop its signal claims. A claim held by a dead object
            // would block that section of line forever, and the only symptom would be
            // trains mysteriously refusing to pass a stretch of empty track.
            VoxelEngine.Building.RailSignalling.ReleaseAll(this);

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

            // A snap is a POSE, not a coordinate. Two things the old snap got wrong:
            // it left the construct's old rotation in place (a train parked at an
            // angle "snapped" while still facing across the line), and it put the
            // GRID ORIGIN on the railhead - but the truck block can sit anywhere on
            // the hull, so the wheels stayed in the air while the bogie insisted it
            // was railed. Now: face along the track, then shift the whole construct
            // so the truck block itself sits on the railhead.
            transform.rotation = track.transform.rotation;
            var truck = GetComponentInChildren<GridRailTruck>();
            Vector3 anchor = truck != null && truck.transform != null
                ? truck.transform.position : transform.position;
            transform.position += track.RailPosition - anchor;
            return true;
        }

        private void ServiceTick()
        {
            if (!IsOnRails || _speed > 0.05f)
            {
                if (_serviceStation != null) { _serviceStation = null; ServiceNote = ""; }
                return;
            }

            if (Time.time >= _nextStoreScan)
            {
                _nextStoreScan = Time.time + 5f;
                _stores.Clear();
                foreach (var store in GetComponentsInChildren<IGridItemStore>(true))
                    if (store.ItemStore != null) _stores.Add(store);
            }

            if (Time.time < _nextService) return;
            _nextService = Time.time + 0.25f;

            var station = VoxelEngine.Building.RailStation.Nearest(
                transform.position, VoxelEngine.Building.RailStation.ServiceRadius);
            if (station == null || station.role == VoxelEngine.Building.StationRole.Passing)
            {
                if (_serviceStation != null) { _serviceStation = null; ServiceNote = ""; }
                return;
            }

            // A new berth starts a new tally, so the note never totals two stations.
            if (_serviceStation != station) { _serviceStation = station; _serviceTotal = 0f; }

            int moved = 0;
            for (int i = 0; i < _stores.Count; i++)
                moved += station.ServiceTrain(_stores[i].ItemStore, 0.25f);
            _serviceTotal += moved;

            bool loading = station.role == VoxelEngine.Building.StationRole.Load;
            string name = station.StationName;
            ServiceNote = moved > 0
                ? (loading
                    ? $"Loading at {name} - {_serviceTotal:0} items aboard"
                    : $"Unloading at {name} - {_serviceTotal:0} items delivered")
                : (loading
                    ? $"Berthed at {name} - station hold empty or train full"
                    : $"Berthed at {name} - train empty or station hold full");
        }

        /// <summary>Lifts the grid off the rails and hands it back to normal physics.</summary>
        public void Detach()
        {
            CurrentTrack = null;
            _previousTrack = null;
            ClearDestination();
            _speed = 0f;
            BlockedReason = "Not on rails.";

            // Lifted off the rails: it occupies no section any more.
            VoxelEngine.Building.RailSignalling.ReleaseAll(this);

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
            // AUTO-SNAP (12.2.0): a truck built beside the line, or a line extended
            // under a parked wagon, should join the rails by itself when the truck's
            // console says so - polled, not per-frame, because FindNearest walks the
            // network and a parked train has nothing else to do.
            if (!IsOnRails && Time.time >= _nextAutoSnap)
            {
                _nextAutoSnap = Time.time + 1f;
                var truck = GetComponentInChildren<GridRailTruck>();
                if (truck != null && truck.autoSnap) TrySnapToTrack();
            }

            ServiceTick();

            // A coupled wagon is driven entirely by its leader's recorded path, so it must
            // NOT run the track logic below. Two bogies both resolving switches would let a
            // consist split itself across a junction.
            if (LeadBogie != null) { FollowLeader(); return; }

            if (CurrentTrack == null) return;

            if (_destination != null && CurrentTrack == _destination)
            {
                _destination = null;
                _path.Clear();
                Arrived?.Invoke(this);
            }

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
                float target = maxSpeed * Mathf.Max(0.1f, CurrentTrack.speedMultiplier) * LoadSpeedFactor();

                // ── Signalling (11.34.0) ──
                // Look far enough ahead to stop before an occupied section rather than
                // inside it. The lookahead scales with speed, because a fast train needs
                // more warning than a shunting one - a fixed distance would either stop a
                // slow train absurdly early or fail to stop a fast one in time.
                int lookahead = Mathf.Clamp(
                    Mathf.CeilToInt(StoppingDistanceMetres() / Mathf.Max(0.5f, CellSpacing())) + 2,
                    2, VoxelEngine.Building.RailSignalling.LookaheadCells);

                var blocker = VoxelEngine.Building.RailSignalling.FindBlockingCell(
                    CurrentTrack, _previousTrack, this, lookahead);

                if (blocker != null)
                {
                    // Brake for the occupied section. Not an instant stop: a train that
                    // halts the frame a signal turns red looks broken and throws anything
                    // riding on it.
                    _speed = Mathf.MoveTowards(_speed, 0f, acceleration * dt);
                    BlockedReason = "Signal: track ahead occupied.";
                    if (_speed <= 0.001f)
                    {
                        // Fully stopped and waiting. Hold only the section underneath us so
                        // the line behind reopens for other trains.
                        ClaimCurrentSections();
                        return;
                    }
                }
                else
                {
                    _speed = Mathf.MoveTowards(_speed, target, acceleration * dt);
                    BlockedReason = "";
                }
            }

            ClaimCurrentSections();
            AdvanceAlongTrack(dt);
        }

        /// <summary>
        /// How much of top speed this consist can actually manage under its current load.
        ///
        /// Mass is summed across the WHOLE consist and divided by the combined rated load
        /// of every bogie in it, so the rule the player asked for falls out directly:
        /// more weight is slower, more bogies is faster, and the gain is capped because
        /// the factor never exceeds 1.
        ///
        /// Load is shared rather than per-bogie on purpose. A real consist spreads its
        /// weight across every axle, and checking each bogie against only the grid it sits
        /// on would let a player defeat the rule by putting the heavy wagon in the middle.
        /// </summary>
        public float LoadSpeedFactor()
        {
            float totalMass = 0f;
            float totalRated = 0f;

            var car = ConsistClaimant();       // walk from the head so every car is counted
            int guard = 0;
            while (car != null && guard++ < 64)
            {
                if (car.Grid != null) totalMass += Mathf.Max(0f, car.Grid.TotalMass);
                totalRated += Mathf.Max(1f, car.ratedLoadKg);
                car = car.TrailBogie;
            }

            if (totalRated <= 0f) return 1f;

            float ratio = totalMass / totalRated;
            if (ratio <= 1f) return 1f;        // within rating: no penalty at all

            // Inverse falloff rather than linear: doubling the overload halves the speed,
            // which keeps an overloaded train slow but always moving.
            return Mathf.Max(minLoadSpeedFactor, 1f / ratio);
        }

        /// <summary>Total mass of the whole consist, for the console readout.</summary>
        public float ConsistMassKg
        {
            get
            {
                float total = 0f;
                var car = ConsistClaimant();
                int guard = 0;
                while (car != null && guard++ < 64)
                {
                    if (car.Grid != null) total += Mathf.Max(0f, car.Grid.TotalMass);
                    car = car.TrailBogie;
                }
                return total;
            }
        }

        /// <summary>Combined rated load of every bogie in the consist.</summary>
        public float ConsistRatedKg
        {
            get
            {
                float total = 0f;
                var car = ConsistClaimant();
                int guard = 0;
                while (car != null && guard++ < 64)
                {
                    total += Mathf.Max(0f, car.ratedLoadKg);
                    car = car.TrailBogie;
                }
                return total;
            }
        }

        /// <summary>
        /// Distance this train needs to stop from its current speed, plus a margin.
        /// v^2 / 2a is the exact figure; the margin covers the cell granularity.
        /// </summary>
        private float StoppingDistanceMetres()
            => (_speed * _speed) / (2f * Mathf.Max(0.1f, acceleration)) + 2f;

        /// <summary>Approximate distance between adjacent cells on the current line.</summary>
        private float CellSpacing()
        {
            if (CurrentTrack == null) return 1f;
            var links = CurrentTrack.Links;
            if (links.Count == 0) return 1f;
            return Mathf.Max(0.5f, Vector3.Distance(CurrentTrack.RailPosition, links[0].RailPosition));
        }

        /// <summary>
        /// Claims the section under the train and the one it is entering, and releases
        /// everything else it was holding.
        ///
        /// Both are held at once because a train straddles a boundary while crossing it -
        /// claiming only the destination would leave the cell under its own tail free for
        /// another train to enter.
        ///
        /// The claim is made by the CONSIST HEAD, not per bogie, so a five-car train is one
        /// claimant rather than five competing ones fighting over the same section.
        /// </summary>
        private void ClaimCurrentSections()
        {
            if (CurrentTrack == null) return;

            var claimant = ConsistClaimant();
            int here = VoxelEngine.Building.RailSignalling.SectionIdOf(CurrentTrack);

            var next = ResolveNextTrack();
            int ahead = next != null
                ? VoxelEngine.Building.RailSignalling.SectionIdOf(next)
                : here;

            VoxelEngine.Building.RailSignalling.TryClaim(CurrentTrack, claimant);
            if (next != null) VoxelEngine.Building.RailSignalling.TryClaim(next, claimant);

            VoxelEngine.Building.RailSignalling.ReleaseAllExcept(claimant, here, ahead);
        }

        /// <summary>
        /// The object that owns this consist's signal claims - always the head, so every
        /// wagon shares one identity and a train never blocks itself.
        /// </summary>
        private GridRailBogie ConsistClaimant()
        {
            var head = this;
            int guard = 0;
            while (head.LeadBogie != null && guard++ < 64) head = head.LeadBogie;
            return head;
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

            // A routed run honours its solved path through every junction; the path is
            // resynced from the front so a train shunted by hand simply falls off the stale
            // prefix and keeps following whatever of the route still lies ahead of it.
            if (_path.Count > 0)
            {
                while (_path.Count > 0 && _path[0] != CurrentTrack) _path.RemoveAt(0);
                if (_path.Count >= 2)
                {
                    var want = _path[1];
                    if (want != null && links.Contains(want)) return want;
                }
            }

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
