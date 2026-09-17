// Assets/Scripts/VoxelEngine/Building/RailTrain.cs
//
// THE TRAIN — a scheduled hauler that walks the rail graph.
//
// A train is NOT a grid vehicle. It does not fly, it cannot be driven off its route,
// and it is not simulated by the rigidbody solver. It is a scheduled agent that walks
// a list of track cells, and that is the whole point:
//
//   • It keeps running while its chunks are unloaded, because walking a graph costs
//     nothing and needs no colliders. A rover cannot do that; this is the specific
//     reason bulk haul belongs on rails.
//   • It cannot decay, drift, or fall through the world, which is what makes a
//     standing order safe to leave running for hours.
//
// This is the same reasoning that put satellites on analytic rails rather than
// rigidbody physics: a thing the player expects to keep working while they are
// elsewhere must not depend on being simulated.
//
// The visual carriage is presentation only and follows the logical position.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Building
{
    /// <summary>One entry in a train's standing order.</summary>
    [System.Serializable]
    public class ScheduleStop
    {
        /// <summary>Station name, matched at arrival. Names, not references — see RailStation.</summary>
        public string stationName = "";

        /// <summary>Wait until the station has no more work before departing.</summary>
        public bool waitUntilDone = true;

        /// <summary>Hard cap on dwell time, in seconds. Stops a jammed station stranding a train.</summary>
        public float maxDwellSeconds = 120f;

        public ScheduleStop() { }
        public ScheduleStop(string name) { stationName = name; }
    }

    public enum TrainState
    {
        /// <summary>No schedule, or the schedule cannot be run.</summary>
        Idle = 0,
        /// <summary>Moving along a solved path toward the next stop.</summary>
        Running = 1,
        /// <summary>Stopped at a station, trading cargo.</summary>
        Docked = 2,
        /// <summary>Schedule is set but the next stop is unreachable.</summary>
        Blocked = 3,
    }

    [DisallowMultipleComponent]
    public class RailTrain : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string _trainName = "";

        [Header("Performance")]
        [Tooltip("Top speed on clear, unworn track, in metres per second.")]
        public float maxSpeed = 14f;

        [Tooltip("Acceleration and braking, in metres per second squared. Low on purpose: " +
                 "mass is what a train is for, and it should feel heavy.")]
        public float acceleration = 2.5f;

        [Header("Cargo")]
        public int holdSlots = 24;

        [Tooltip("Power drawn per second while running. Zero makes the train free to operate.")]
        public float wattsWhileRunning = 0f;

        [Header("Schedule")]
        public List<ScheduleStop> schedule = new();

        [Tooltip("Run the schedule as a loop. Off runs it once and then idles at the last stop.")]
        public bool loopSchedule = true;

        /// <summary>Whether the standing order is currently armed.</summary>
        public bool Running { get; private set; }

        public TrainState State { get; private set; } = TrainState.Idle;

        /// <summary>Why the train is blocked, for the console. Empty when it is not.</summary>
        public string BlockedReason { get; private set; } = "";

        public string TrainName
        {
            get => string.IsNullOrWhiteSpace(_trainName) ? "Unnamed Train" : _trainName;
            set => _trainName = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }

        public ItemContainer Hold
        {
            get
            {
                _hold ??= new ItemContainer("Train Hold", Mathf.Max(1, holdSlots));
                return _hold;
            }
        }
        [SerializeField] private ItemContainer _hold;

        // ── Registry ─────────────────────────────────────────────────────────────
        private static readonly List<RailTrain> s_all = new();
        public static IReadOnlyList<RailTrain> All => s_all;

        private void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
        private void OnDisable() { s_all.Remove(this); }

        // ── Position on the graph ────────────────────────────────────────────────

        /// <summary>The cell the train is currently on.</summary>
        public RailTrack CurrentTrack { get; private set; }

        /// <summary>Solved route to the current target stop.</summary>
        private readonly List<RailTrack> _path = new(64);
        private int _pathIndex;

        /// <summary>How far along the edge to the next cell the train has travelled, 0..1.</summary>
        private float _edgeProgress;
        private float _speed;

        private int _scheduleIndex;
        private float _dwellTimer;
        private RailStation _dockedAt;

        /// <summary>Progress along the whole current leg, for the console readout.</summary>
        public float LegProgress01
        {
            get
            {
                if (_path.Count < 2) return 0f;
                return Mathf.Clamp01((_pathIndex + _edgeProgress) / (_path.Count - 1));
            }
        }

        public string CurrentTargetName =>
            schedule.Count > 0 && _scheduleIndex < schedule.Count
                ? schedule[_scheduleIndex].stationName
                : "";

        // ── Control ──────────────────────────────────────────────────────────────

        public void StartSchedule()
        {
            if (schedule.Count == 0)
            {
                BlockedReason = "No stops in the schedule.";
                State = TrainState.Blocked;
                return;
            }
            Running = true;
            PlanNextLeg();
        }

        public void StopSchedule()
        {
            Running = false;
            State = TrainState.Idle;
            _speed = 0f;
            _path.Clear();
            BlockedReason = "";
        }

        /// <summary>Places the train on the network at the nearest track cell.</summary>
        public bool SnapToTrack()
        {
            var track = RailNetwork.FindNearest(transform.position, 8f);
            if (track == null) return false;
            CurrentTrack = track;
            transform.position = track.RailPosition;
            _edgeProgress = 0f;
            return true;
        }

        // ── Simulation ───────────────────────────────────────────────────────────

        private void Update()
        {
            if (!Running) return;

            if (CurrentTrack == null && !SnapToTrack())
            {
                State = TrainState.Blocked;
                BlockedReason = "Train is not on any track.";
                return;
            }

            float dt = Time.deltaTime;

            if (State == TrainState.Docked) { TickDocked(dt); return; }
            if (State == TrainState.Blocked) { TickBlocked(); return; }

            TickRunning(dt);
        }

        private void TickBlocked()
        {
            // Re-plan periodically rather than every frame: a blocked train is usually
            // waiting on the player to lay track, and hammering A* changes nothing.
            _dwellTimer -= Time.deltaTime;
            if (_dwellTimer > 0f) return;
            _dwellTimer = 2f;
            PlanNextLeg();
        }

        private void TickDocked(float dt)
        {
            _dwellTimer -= dt;

            if (_dockedAt != null)
            {
                _dockedAt.ServiceTrain(Hold, dt);

                var stop = schedule[Mathf.Clamp(_scheduleIndex, 0, schedule.Count - 1)];
                bool workRemains = stop.waitUntilDone && _dockedAt.HasWorkFor(Hold);

                // The hard cap is what stops a jammed or mis-filtered station from
                // stranding a train forever with a standing order that can never complete.
                bool capped = _dwellTimer <= -stop.maxDwellSeconds;

                if (workRemains && !capped) return;
            }

            if (_dwellTimer > 0f) return;

            AdvanceSchedule();
        }

        private void TickRunning(float dt)
        {
            if (_path.Count < 2) { PlanNextLeg(); return; }

            _speed = Mathf.MoveTowards(_speed, TargetSpeed(), acceleration * dt);

            var from = _path[_pathIndex];
            var to = _path[Mathf.Min(_pathIndex + 1, _path.Count - 1)];
            if (from == null || to == null)
            {
                State = TrainState.Blocked;
                BlockedReason = "Track was removed under the route.";
                return;
            }

            float edgeLength = Mathf.Max(0.01f, Vector3.Distance(from.RailPosition, to.RailPosition));
            _edgeProgress += _speed * dt / edgeLength;

            while (_edgeProgress >= 1f)
            {
                _edgeProgress -= 1f;
                _pathIndex++;
                CurrentTrack = _path[Mathf.Min(_pathIndex, _path.Count - 1)];

                if (_pathIndex >= _path.Count - 1)
                {
                    _edgeProgress = 0f;
                    ArriveAtStop();
                    return;
                }

                from = _path[_pathIndex];
                to = _path[_pathIndex + 1];
                if (from == null || to == null)
                {
                    State = TrainState.Blocked;
                    BlockedReason = "Track was removed under the route.";
                    return;
                }
                edgeLength = Mathf.Max(0.01f, Vector3.Distance(from.RailPosition, to.RailPosition));
            }

            Vector3 a = from.RailPosition;
            Vector3 b = to.RailPosition;
            transform.position = Vector3.Lerp(a, b, _edgeProgress);

            Vector3 heading = b - a;
            if (heading.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(heading.normalized, from.transform.up);
        }

        /// <summary>
        /// Speed for the cell being entered, and a braking ramp into the final cell so the
        /// train arrives stopped rather than slamming into the platform at line speed.
        /// </summary>
        private float TargetSpeed()
        {
            var cell = _path.Count > _pathIndex ? _path[_pathIndex] : CurrentTrack;
            float limit = maxSpeed * (cell != null ? Mathf.Max(0.1f, cell.speedMultiplier) : 1f);

            int cellsRemaining = Mathf.Max(0, _path.Count - 1 - _pathIndex);
            float metresRemaining = cellsRemaining * Mathf.Max(0.5f, CellSpacing());

            // v = sqrt(2 a s): the fastest speed from which the train can still stop in the
            // distance left. Without this a long consist overshoots every station.
            float brakingLimit = Mathf.Sqrt(Mathf.Max(0f, 2f * acceleration * metresRemaining));
            return Mathf.Min(limit, brakingLimit);
        }

        private float CellSpacing()
        {
            if (_path.Count < 2) return 1f;
            var a = _path[0];
            var b = _path[1];
            if (a == null || b == null) return 1f;
            return Vector3.Distance(a.RailPosition, b.RailPosition);
        }

        // ── Schedule ─────────────────────────────────────────────────────────────

        private void ArriveAtStop()
        {
            var stop = schedule.Count > 0 ? schedule[Mathf.Clamp(_scheduleIndex, 0, schedule.Count - 1)] : null;
            _dockedAt = stop != null ? RailStation.Find(stop.stationName) : null;

            _speed = 0f;
            State = TrainState.Docked;
            _dwellTimer = _dockedAt != null ? _dockedAt.dwellSeconds : 1f;
        }

        private void AdvanceSchedule()
        {
            _dockedAt = null;

            if (_scheduleIndex + 1 >= schedule.Count && !loopSchedule)
            {
                Running = false;
                State = TrainState.Idle;
                return;
            }

            _scheduleIndex = (_scheduleIndex + 1) % Mathf.Max(1, schedule.Count);
            PlanNextLeg();
        }

        /// <summary>Solves the route to the current stop, or reports why it cannot.</summary>
        private void PlanNextLeg()
        {
            _path.Clear();
            _pathIndex = 0;
            _edgeProgress = 0f;

            if (schedule.Count == 0)
            {
                State = TrainState.Blocked;
                BlockedReason = "No stops in the schedule.";
                return;
            }

            if (CurrentTrack == null && !SnapToTrack())
            {
                State = TrainState.Blocked;
                BlockedReason = "Train is not on any track.";
                return;
            }

            var stop = schedule[Mathf.Clamp(_scheduleIndex, 0, schedule.Count - 1)];
            var station = RailStation.Find(stop.stationName);
            if (station == null)
            {
                State = TrainState.Blocked;
                BlockedReason = $"No station named '{stop.stationName}'.";
                return;
            }

            var platform = station.ServedTrack;
            if (platform == null)
            {
                State = TrainState.Blocked;
                BlockedReason = $"'{station.StationName}' has no track within {RailStation.ServiceRadius:0} m.";
                return;
            }

            // Already there: dock immediately rather than solving a zero-length path.
            if (platform == CurrentTrack)
            {
                ArriveAtStop();
                return;
            }

            if (!RailNetwork.TryFindPath(CurrentTrack, platform, _path))
            {
                State = TrainState.Blocked;
                BlockedReason = $"No connected route to '{station.StationName}'. Check for gaps, " +
                                "over-steep sections, or switches set against the route.";
                return;
            }

            State = TrainState.Running;
            BlockedReason = "";
        }

        /// <summary>Distance of the current leg in metres, for the console.</summary>
        public float LegLength => RailNetwork.PathLength(_path);

        // ── Persistence helpers ──────────────────────────────────────────────────

        public void CaptureState(out bool running, out int scheduleIndex)
        {
            running = Running;
            scheduleIndex = _scheduleIndex;
        }

        public void RestoreState(bool running, int scheduleIndex)
        {
            _scheduleIndex = Mathf.Max(0, scheduleIndex);
            if (running) StartSchedule();
        }
    }
}
