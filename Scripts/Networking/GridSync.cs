// Assets/Scripts/VoxelEngine/Networking/GridSync.cs
//
// 14.25.0-dev - Multiplayer milestone 9, part one: movable grids on the wire.
//
// THE TWO DECISIONS THIS FILE ENCODES
//
// 1. A PIECE IS ADDRESSED GRID-LOCALLY, NEVER IN WORLD SPACE.
//    Every other sync class in this project identifies a thing by where it is:
//    BlockSync finds a block at a world position, TerrainSync keys a chunk by
//    its voxel coordinate. That works because none of those things move. A grid
//    moves - it is the one thing in the game whose whole purpose is to move -
//    so a world position is not an identity for anything bolted to it. The
//    moment the ship is flying, "the block at (104.2, 61.8, -33.0)" names a
//    different cell every frame, and on two machines whose physics have drifted
//    by half a metre it names two different cells at the same instant.
//
//    So a piece is (grid net id, integer cell). The grid id is stable across a
//    save/load and across every reconnect; the cell never changes while the
//    piece is attached. Both halves are exact - no floating point in an
//    identity - which means an edit sent while a ship is under thrust lands on
//    precisely the cell the sender meant, however far the two hulls have drifted.
//
// 2. ONLY THE HOST SIMULATES A GRID. CLIENTS ARE TOLD WHERE IT IS.
//    The alternative - both machines running the same rigidbody from the same
//    inputs and hoping they agree - cannot work and gets worse the longer a
//    session runs. Rigidbody integration is not deterministic across machines,
//    thrust depends on power which depends on a hundred block states, and any
//    disagreement compounds because each machine then feeds its own wrong
//    position back into its own next step. That is the divergence that ends with
//    one player watching a ship fly off while the other sits still on it.
//
//    So on a client every grid rigidbody is KINEMATIC and is driven purely by
//    the host's pose stream. There is no second simulation to disagree with the
//    first. Between packets the client dead-reckons from the last known velocity
//    so the ship keeps gliding instead of stuttering, and corrections are eased
//    in rather than snapped - except for a genuinely large error (a warp jump),
//    which is applied immediately because easing a 10 km correction would look
//    far worse than a cut.
//
// WHAT CROSSES THE WIRE
//
//   - Structure: the whole grid as the exact SavedGrid JSON the host would have
//     written to disk, resent when the ship's shape changes. Reusing the save
//     record means there is no second serializer that can drift out of step with
//     the first, which is the same trick the per-player record uses (14.24.0).
//     It is heavier than a per-cell delta and it is deliberate for now: a record
//     that is always complete cannot leave a client holding a half-built ship.
//   - Pose: position, rotation and both velocities, unreliable and often. A lost
//     pose packet must never be retransmitted - by the time it arrived it would
//     be describing the past.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Networking
{
    /// <summary>One whole grid on the wire: its stable id plus its save record.</summary>
    public struct GridRecord
    {
        public string NetId;
        public string Json;
    }

    /// <summary>A guest's stick and throttle, on their way to the host.</summary>
    public struct GridFlightInput
    {
        public string NetId;
        public Vector3 Thrust;
        public float Yaw;
        public float Pitch;
        public float Roll;
        public bool Dampeners;
    }

    /// <summary>Where a grid is, according to the only machine entitled to say.</summary>
    public struct GridPose
    {
        public string NetId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
    }

    /// <summary>
    /// The stable network identity of one grid, and - on a client - the thing that
    /// drives it. Attached on demand; a grid in a single-player session carries one
    /// too, because the id is written to the save and must survive the world being
    /// loaded offline and later hosted.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GridNetTag : MonoBehaviour
    {
        [SerializeField] private string _id = "";

        /// <summary>Error beyond which a correction is cut rather than eased. A warp
        /// jump or an origin re-anchor is a teleport; easing it would be a long, wrong slide.</summary>
        private const float SnapDistance = 25f;

        /// <summary>How fast an eased correction closes. Higher converges sooner and
        /// jitters more; this is about a quarter of a second to close the error.</summary>
        private const float CorrectionRate = 12f;

        /// <summary>Longest a client will dead-reckon on one pose before it stops
        /// predicting. Past this the host has gone quiet and guessing further is worse
        /// than holding still.</summary>
        private const float MaxExtrapolationSeconds = 0.5f;

        private GridEntity _grid;
        private Rigidbody _rb;

        private bool _hasNetPose;
        private Vector3 _netPosition;
        private Quaternion _netRotation = Quaternion.identity;
        private Vector3 _netVelocity;
        private Vector3 _netAngularVelocity;
        private float _netReceivedAt;

        public string Id => _id;

        public GridEntity Grid
        {
            get
            {
                if (_grid == null) _grid = GetComponent<GridEntity>();
                return _grid;
            }
        }

        /// <summary>True when this machine must NOT simulate this grid and should take
        /// its pose from the network instead. GridEntity checks this before stepping.</summary>
        public bool DrivenRemotely => NetworkSession.Mode == SessionMode.Client;

        internal void Assign(string id)
        {
            if (_id == id) return;
            GridSync.Unregister(_id, this);
            _id = id;
            GridSync.Register(_id, this);
        }

        private void OnEnable() => GridSync.Register(_id, this);
        private void OnDisable() => GridSync.Unregister(_id, this);

        private void OnDestroy()
        {
            // scene.isLoaded separates a ship actually being destroyed from the whole
            // scene coming down at quit or teardown. Without it, leaving a hosted world
            // would announce the destruction of the entire fleet to clients who are
            // themselves on the way out.
            if (!gameObject.scene.isLoaded) return;
            // A record being re-applied destroys the old hull to rebuild it. That is not
            // a sinking, and announcing it would race the replacement.
            if (GridSync.IsApplyingRemote) return;
            GridSync.AnnounceRemoved(_id);
        }

        /// <summary>Latest authoritative pose. Stamped on arrival so the client knows how
        /// stale it is and how far to dead-reckon from it.</summary>
        internal void ReceivePose(GridPose pose)
        {
            _netPosition = pose.Position;
            _netRotation = pose.Rotation;
            _netVelocity = pose.Velocity;
            _netAngularVelocity = pose.AngularVelocity;
            _netReceivedAt = Time.time;
            _hasNetPose = true;
        }

        internal GridPose CapturePose()
        {
            var body = Grid != null ? Grid.Body : null;
            if (body != null)
            {
                return new GridPose
                {
                    NetId = _id,
                    Position = body.position,
                    Rotation = body.rotation,
                    Velocity = body.linearVelocity,
                    AngularVelocity = body.angularVelocity
                };
            }
            return new GridPose
            {
                NetId = _id,
                Position = transform.position,
                Rotation = transform.rotation,
                Velocity = Vector3.zero,
                AngularVelocity = Vector3.zero
            };
        }

        /// <summary>True when the hull has moved enough since the last sent pose to be
        /// worth another packet. A parked ship costs nothing.</summary>
        internal bool IsWorthSending(Vector3 lastSentPosition, Quaternion lastSentRotation)
        {
            var pose = CapturePose();
            if (pose.Velocity.sqrMagnitude > 0.0004f) return true;           // >2 cm/s
            if (pose.AngularVelocity.sqrMagnitude > 0.0004f) return true;
            if ((pose.Position - lastSentPosition).sqrMagnitude > 0.0001f) return true;   // >1 cm
            return Quaternion.Angle(pose.Rotation, lastSentRotation) > 0.1f;
        }

        private void FixedUpdate()
        {
            if (!DrivenRemotely) return;

            if (_rb == null) _rb = GetComponent<Rigidbody>();
            if (_rb == null) return;

            // A client never integrates a grid. Kinematic also stops the client's own
            // physics from shoving the hull around on contact, which would be a
            // correction the host never agreed to and never hears about.
            if (!_rb.isKinematic)
            {
                _rb.isKinematic = true;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }

            if (!_hasNetPose) return;

            // Dead reckoning: carry the last known pose forward at the last known
            // velocity, so a ship under thrust keeps gliding between packets instead
            // of stepping once per arrival.
            float age = Mathf.Min(Time.time - _netReceivedAt, MaxExtrapolationSeconds);
            Vector3 target = _netPosition + _netVelocity * age;
            Quaternion targetRotation = IntegrateAngular(_netRotation, _netAngularVelocity, age);

            Vector3 position;
            Quaternion rotation;
            if ((target - _rb.position).sqrMagnitude > SnapDistance * SnapDistance)
            {
                // Too far to ease. This is a jump, an origin re-anchor or a first
                // sighting - cut to it.
                position = target;
                rotation = targetRotation;
            }
            else
            {
                float t = 1f - Mathf.Exp(-CorrectionRate * Time.fixedDeltaTime);
                position = Vector3.Lerp(_rb.position, target, t);
                rotation = Quaternion.Slerp(_rb.rotation, targetRotation, t);
            }

            // MovePosition rather than a transform write: a kinematic body that MOVES
            // sweeps its colliders, so anything touching the hull is pushed properly
            // instead of being found already inside it next step.
            _rb.MovePosition(position);
            _rb.MoveRotation(rotation);
        }

        private static Quaternion IntegrateAngular(Quaternion rotation, Vector3 angularVelocity, float seconds)
        {
            float speed = angularVelocity.magnitude;
            if (speed < 0.0001f || seconds <= 0f) return rotation;
            var spin = Quaternion.AngleAxis(speed * Mathf.Rad2Deg * seconds, angularVelocity / speed);
            return spin * rotation;
        }
    }

    public static class GridSync
    {
        /// <summary>Raised while a remote grid edit is being applied locally, so the
        /// gameplay hooks never announce an echo back into the network.</summary>
        public static bool IsApplyingRemote { get; private set; }

        private static readonly Dictionary<string, GridNetTag> _byId = new();

        // ─────────────── identity ───────────────

        internal static void Register(string id, GridNetTag tag)
        {
            if (string.IsNullOrEmpty(id) || tag == null) return;
            _byId[id] = tag;
        }

        internal static void Unregister(string id, GridNetTag tag)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_byId.TryGetValue(id, out var current) && current == tag) _byId.Remove(id);
        }

        /// <summary>The stable id of a grid, minting one if it has never had an id.
        /// Safe to call on any machine: an id invented by a client is replaced the
        /// moment the host's record for that grid arrives.</summary>
        public static string IdOf(GridEntity grid)
        {
            if (grid == null) return "";
            var tag = grid.GetComponent<GridNetTag>();
            if (tag == null) tag = grid.gameObject.AddComponent<GridNetTag>();
            if (string.IsNullOrEmpty(tag.Id)) tag.Assign(System.Guid.NewGuid().ToString("N"));
            return tag.Id;
        }

        /// <summary>Give a grid an id decided elsewhere - a save file or the host.
        /// An empty id is ignored, so a pre-14.25.0 save simply mints a fresh one.</summary>
        public static void Adopt(GridEntity grid, string id)
        {
            if (grid == null || string.IsNullOrEmpty(id)) return;
            var tag = grid.GetComponent<GridNetTag>();
            if (tag == null) tag = grid.gameObject.AddComponent<GridNetTag>();
            tag.Assign(id);
        }

        /// <summary>The grid with this id, or null. Never falls back to a position
        /// search: a wrong grid is worse than no grid.</summary>
        public static GridEntity Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!_byId.TryGetValue(id, out var tag) || tag == null)
            {
                _byId.Remove(id);
                return null;
            }
            return tag.Grid;
        }

        internal static GridNetTag FindTag(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            _byId.TryGetValue(id, out var tag);
            return tag;
        }

        /// <summary>Everything registered, for the pose broadcaster. Entries whose grid
        /// has been destroyed are dropped as they are found.</summary>
        internal static void CollectTags(List<GridNetTag> into)
        {
            into.Clear();
            List<string> dead = null;
            foreach (var kv in _byId)
            {
                if (kv.Value == null || kv.Value.Grid == null)
                {
                    (dead ??= new List<string>()).Add(kv.Key);
                    continue;
                }
                into.Add(kv.Value);
            }
            if (dead != null)
                foreach (var key in dead) _byId.Remove(key);
        }

        // ─────────────── local state -> network ───────────────

        private static bool CanSync =>
            NetworkSession.Mode != SessionMode.Offline
            && NetworkBootstrap.Instance != null
            && !NetworkBootstrap.Instance.WorldMismatch;

        /// <summary>Lazy walk of every grid as a complete record. One at a time so the
        /// join catch-up can serialize a ship per frame rather than a fleet per frame.</summary>
        public static IEnumerable<GridRecord> StreamSnapshot()
        {
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) yield break;
            foreach (var grid in Object.FindObjectsByType<GridEntity>(FindObjectsSortMode.None))
            {
                if (grid == null || grid.BlockCount == 0) continue;
                string json = persistence.CaptureGridJson(grid);
                if (string.IsNullOrEmpty(json)) continue;
                yield return new GridRecord { NetId = IdOf(grid), Json = json };
            }
        }

        public static List<GridRecord> GatherSnapshot() => new List<GridRecord>(StreamSnapshot());

        /// <summary>One grid, wire-ready. Null when it has nothing worth sending.</summary>
        public static GridRecord? CaptureOne(GridEntity grid)
        {
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null || grid == null || grid.BlockCount == 0) return null;
            string json = persistence.CaptureGridJson(grid);
            if (string.IsNullOrEmpty(json)) return null;
            return new GridRecord { NetId = IdOf(grid), Json = json };
        }

        /// <summary>The host telling everyone a ship's shape changed. Clients never call
        /// this - a client that rebuilt a hull locally would be guessing.</summary>
        public static void AnnounceStructure(GridEntity grid)
        {
            if (!CanSync || IsApplyingRemote) return;
            if (!NetworkSession.IsAuthority) return;
            var record = CaptureOne(grid);
            if (record.HasValue) NetworkBootstrap.Instance.SendGridRecord(record.Value);
        }

        /// <summary>The host telling everyone a ship is gone.</summary>
        public static void AnnounceRemoved(string netId)
        {
            if (!CanSync || IsApplyingRemote || string.IsNullOrEmpty(netId)) return;
            if (!NetworkSession.IsAuthority) return;
            NetworkBootstrap.Instance.SendGridRemoved(netId);
        }

        // ─────────────── network -> local world ───────────────

        /// <summary>Adopt one grid record. An id we already hold is rebuilt from scratch
        /// rather than patched: the record is complete, so replacing is both simpler and
        /// strictly more correct than trying to work out the difference.</summary>
        public static void ApplyRecord(GridRecord record)
        {
            if (string.IsNullOrEmpty(record.Json)) return;
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) return;

            IsApplyingRemote = true;
            try
            {
                var existing = Find(record.NetId);
                if (existing != null) Object.Destroy(existing.gameObject);

                var grid = persistence.ApplyGridRecord(record.Json);
                if (grid != null) Adopt(grid, record.NetId);
            }
            finally { IsApplyingRemote = false; }
        }

        // ── part reassembly ───────────────────────────────────────────
        //
        // A ship arrives in pieces. Parts are held per net id until the set is
        // complete, then applied in one go - a half-assembled record is never
        // handed to the restore path, so a client can never be left holding a
        // ship with half its blocks.

        private sealed class PendingRecord
        {
            public string[] Parts;
            public int Filled;
            public float StartedAt;
        }

        private static readonly Dictionary<string, PendingRecord> _pending = new();

        /// <summary>A record part that has gone this long without its siblings is
        /// abandoned. The host resends on its own schedule, so a dropped set costs a
        /// moment rather than a permanently wrong ship.</summary>
        private const float PartTimeoutSeconds = 20f;

        public static void ReceiveRecordPart(string netId, int part, int totalParts, string payload)
        {
            if (string.IsNullOrEmpty(netId) || totalParts <= 0 || part < 0 || part >= totalParts) return;

            if (!_pending.TryGetValue(netId, out var pending) || pending.Parts.Length != totalParts)
            {
                pending = new PendingRecord
                {
                    Parts = new string[totalParts],
                    Filled = 0,
                    StartedAt = Time.time
                };
                _pending[netId] = pending;
            }

            if (pending.Parts[part] == null) pending.Filled++;
            pending.Parts[part] = payload ?? "";

            if (pending.Filled < totalParts)
            {
                SweepStalePending();
                return;
            }

            _pending.Remove(netId);
            ApplyRecord(new GridRecord { NetId = netId, Json = string.Concat(pending.Parts) });
        }

        private static void SweepStalePending()
        {
            List<string> stale = null;
            foreach (var kv in _pending)
                if (Time.time - kv.Value.StartedAt > PartTimeoutSeconds)
                    (stale ??= new List<string>()).Add(kv.Key);
            if (stale == null) return;
            foreach (var key in stale)
            {
                _pending.Remove(key);
                Debug.LogWarning($"[GridSync] Incomplete grid record for '{key}' timed out; waiting for the host to resend.");
            }
        }

        public static void ApplySnapshot(List<GridRecord> records)
        {
            if (records == null) return;
            foreach (var record in records) ApplyRecord(record);
        }

        public static void ApplyRemoved(string netId)
        {
            var grid = Find(netId);
            if (grid == null) return;
            IsApplyingRemote = true;
            try { Object.Destroy(grid.gameObject); }
            finally { IsApplyingRemote = false; }
        }

        /// <summary>A pose from the host. Dropped silently when the grid is unknown -
        /// its record is already on its way and the next pose will land.</summary>
        public static void ApplyPose(GridPose pose)
        {
            FindTag(pose.NetId)?.ReceivePose(pose);
        }

        // ── control authority (14.26.0) ──────────────────────────────
        //
        // The host simulates every grid, so a guest in a cockpit cannot fly by
        // moving the hull - it would be overwritten by the next pose packet, and
        // the host would never learn the ship was meant to be under thrust. What
        // travels instead is the INPUT: stick and throttle go up, the host flies
        // the ship it already owns, and the result comes back down the pose
        // stream like any other motion.
        //
        // That costs the guest a round trip of latency on their own controls and
        // it is the honest version: one machine decides where the ship is, and no
        // client-side prediction can quietly disagree with it. Prediction can be
        // layered on later; a divergence built in at the foundation cannot be
        // taken out later.
        //
        // A claim is required before input is accepted. The host validates it
        // against its own connection table - one pilot per hull, first claim
        // wins, released when the seat is left or the connection drops - so a
        // client cannot fly a ship it is not sitting in.

        /// <summary>The grid this machine's local player has claimed, if any.</summary>
        public static string LocalControlClaim { get; private set; } = "";

        /// <summary>Local player took a cockpit. On a client this asks the host for
        /// control; on the host it is simply recorded, since the host already flies it.</summary>
        public static void ClaimControl(GridEntity grid)
        {
            if (grid == null) return;
            string id = IdOf(grid);
            if (LocalControlClaim == id) return;
            ReleaseControl();
            LocalControlClaim = id;
            if (NetworkSession.Mode == SessionMode.Client && CanSync)
                NetworkBootstrap.Instance.SendGridControl(id, true);
        }

        /// <summary>Local player left the seat.</summary>
        public static void ReleaseControl()
        {
            if (string.IsNullOrEmpty(LocalControlClaim)) return;
            string id = LocalControlClaim;
            LocalControlClaim = "";
            if (NetworkSession.Mode == SessionMode.Client && CanSync)
                NetworkBootstrap.Instance.SendGridControl(id, false);
        }

        /// <summary>A guest's stick and throttle, on their way to the machine that
        /// actually flies the ship. Silent unless this is a client holding the claim.</summary>
        /// <summary>Input packets per second. The cockpit writes the stick every frame;
        /// sending that rate would be three times the pose stream for a quarter of the
        /// information. 20 Hz matches the pose cadence, so input and the motion it
        /// causes arrive on the same clock.</summary>
        private const float InputHz = 20f;
        private static float _nextInputAt;
        private static Vector3 _lastSentThrust;
        private static float _lastSentYaw, _lastSentPitch, _lastSentRoll;
        private static bool _lastSentDampeners;

        public static void AnnounceFlightInput(GridEntity grid, Vector3 thrust,
            float yaw, float pitch, float roll, bool dampeners)
        {
            if (NetworkSession.Mode != SessionMode.Client || !CanSync || IsApplyingRemote) return;
            if (grid == null || string.IsNullOrEmpty(LocalControlClaim)) return;
            var tag = grid.GetComponent<GridNetTag>();
            if (tag == null || tag.Id != LocalControlClaim) return;

            // A CHANGE always goes immediately - letting go of the throttle must not
            // wait for the next slot, because the hull keeps accelerating until it
            // lands. Only an unchanged stick is rate limited.
            bool changed = dampeners != _lastSentDampeners
                           || (thrust - _lastSentThrust).sqrMagnitude > 0.0001f
                           || Mathf.Abs(yaw - _lastSentYaw) > 0.001f
                           || Mathf.Abs(pitch - _lastSentPitch) > 0.001f
                           || Mathf.Abs(roll - _lastSentRoll) > 0.001f;
            if (!changed && Time.unscaledTime < _nextInputAt) return;

            _nextInputAt = Time.unscaledTime + 1f / InputHz;
            _lastSentThrust = thrust;
            _lastSentYaw = yaw;
            _lastSentPitch = pitch;
            _lastSentRoll = roll;
            _lastSentDampeners = dampeners;

            NetworkBootstrap.Instance.SendGridInput(new GridFlightInput
            {
                NetId = tag.Id,
                Thrust = thrust,
                Yaw = yaw,
                Pitch = pitch,
                Roll = roll,
                Dampeners = dampeners
            });
        }

        /// <summary>Host side: drive a grid from a validated guest's input.</summary>
        public static void ApplyFlightInput(GridFlightInput input)
        {
            var grid = Find(input.NetId);
            if (grid == null) return;
            IsApplyingRemote = true;
            try
            {
                grid.DampenersOn = input.Dampeners;
                grid.SetFlightInput(input.Thrust, input.Yaw, input.Pitch, input.Roll);
            }
            finally { IsApplyingRemote = false; }
        }

        /// <summary>Host side: a pilot let go or dropped out. The hull must not keep
        /// flying on the last input it was given.</summary>
        public static void CutFlightInput(string netId)
        {
            var grid = Find(netId);
            if (grid == null) return;
            IsApplyingRemote = true;
            try { grid.SetFlightInput(Vector3.zero, 0f, 0f, 0f); }
            finally { IsApplyingRemote = false; }
        }

        /// <summary>Forget everything. Called on teardown so a second session never
        /// resolves an id minted in the first.</summary>
        public static void Clear()
        {
            _byId.Clear();
            _pending.Clear();
            LocalControlClaim = "";
        }
    }

    /// <summary>
    /// Host-side broadcaster. Structurally the same round-robin shape as the container
    /// and machine pollers: a cheap pass that only speaks when something changed.
    ///
    /// Pose and structure run on deliberately different clocks. A pose is small, it is
    /// wanted constantly, and a lost one is worthless by the time it could be resent -
    /// so it goes out unreliably, many times a second, only for hulls that are actually
    /// moving. A structure record is large and rare, so it goes out reliably and only
    /// when the ship's shape has actually changed.
    /// </summary>
    public sealed class GridSyncManager : MonoBehaviour
    {
        public static GridSyncManager Instance { get; private set; }

        /// <summary>Pose packets per second per moving grid.</summary>
        private const float PoseHz = 20f;

        /// <summary>Seconds between structure checks. A hull does not change shape fast
        /// enough to need a tighter loop, and the check walks every cell.</summary>
        private const float StructureCheckSeconds = 1f;

        private readonly List<GridNetTag> _tags = new();
        private readonly Dictionary<string, int> _structureHash = new();
        private readonly Dictionary<string, Vector3> _lastSentPosition = new();
        private readonly Dictionary<string, Quaternion> _lastSentRotation = new();

        private float _nextPoseAt;
        private float _nextStructureAt;

        /// <summary>False until the first structure pass has recorded what every hull
        /// currently looks like. Without this the first pass after going online reads
        /// every grid as "changed" and broadcasts the entire fleet for nothing - the
        /// join catch-up already hands a new client every ship that existed.</summary>
        private bool _structureSeeded;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Called when a session ends so the next one seeds its own baseline
        /// rather than trusting shapes recorded in a world that is no longer loaded.</summary>
        public void ForgetBaseline()
        {
            _structureSeeded = false;
            _structureHash.Clear();
            _lastSentPosition.Clear();
            _lastSentRotation.Clear();
        }

        private void LateUpdate()
        {
            // Only the host speaks. A client's grids are told where they are.
            if (NetworkSession.Mode != SessionMode.Host) return;
            if (NetworkBootstrap.Instance == null || NetworkBootstrap.Instance.WorldMismatch) return;

            GridSync.CollectTags(_tags);
            if (_tags.Count == 0) return;

            if (Time.time >= _nextPoseAt)
            {
                _nextPoseAt = Time.time + 1f / PoseHz;
                BroadcastPoses();
            }

            if (Time.time >= _nextStructureAt)
            {
                _nextStructureAt = Time.time + StructureCheckSeconds;
                BroadcastChangedStructures(announce: _structureSeeded);
                _structureSeeded = true;
            }
        }

        private void BroadcastPoses()
        {
            foreach (var tag in _tags)
            {
                if (tag == null || string.IsNullOrEmpty(tag.Id)) continue;

                _lastSentPosition.TryGetValue(tag.Id, out var lastPos);
                _lastSentRotation.TryGetValue(tag.Id, out var lastRot);
                if (!tag.IsWorthSending(lastPos, lastRot)) continue;

                var pose = tag.CapturePose();
                NetworkBootstrap.Instance.SendGridPose(pose);
                _lastSentPosition[tag.Id] = pose.Position;
                _lastSentRotation[tag.Id] = pose.Rotation;
            }
        }

        /// <summary>Walk every hull and resend the ones whose shape changed. With
        /// <paramref name="announce"/> false it only records the current shapes, which is
        /// what the first pass does.</summary>
        private void BroadcastChangedStructures(bool announce)
        {
            foreach (var tag in _tags)
            {
                if (tag == null || string.IsNullOrEmpty(tag.Id)) continue;
                var grid = tag.Grid;
                if (grid == null || grid.BlockCount == 0) continue;

                int hash = StructureHashOf(grid);
                if (_structureHash.TryGetValue(tag.Id, out int known) && known == hash) continue;
                _structureHash[tag.Id] = hash;
                if (announce) GridSync.AnnounceStructure(grid);
            }
        }

        /// <summary>Cheap shape fingerprint: which cells are filled, and how hurt each
        /// one is. Deliberately NOT a content hash - a cargo container filling up is the
        /// container poller's business, not a reason to resend a whole hull.</summary>
        private static int StructureHashOf(GridEntity grid)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + grid.BlockCount;
                foreach (var kv in grid.Blocks)
                {
                    hash = hash * 31 + kv.Key.GetHashCode();
                    var block = kv.Value;
                    if (block == null) continue;
                    hash = hash * 31 + Mathf.RoundToInt(block.Damage01 * 16f);
                    hash = hash * 31 + (block.Enabled ? 1 : 0);
                }
                return hash;
            }
        }
    }
}
