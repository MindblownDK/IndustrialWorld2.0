// Assets/Scripts/VoxelEngine/Navigation/NavFlightAutopilot.cs
//
// FLY TO — cruise control that flies the ship to the orbital map's nav target.
//
// The map has always been able to POINT (NavigationTarget); this is the first thing
// that FLIES the pointing. One ship at a time, vacuum legs only, honest physics:
//
//   • It commands a VELOCITY through GridEntity.SetAutonomousFlight — the same
//     channel the auto-run loops use, never a ghost pilot. A seated pilot keeps
//     their seat, camera and tools (they are genuinely aboard), the stick
//     overrides translation the instant it moves and the cruise resumes on
//     release. Gyros swing the strongest thrust axis onto the flight line, and
//     the warp cone onto the target; the mouse is parked so it cannot fight the
//     nose. See GridEntity.cruiseTranslating.
//   • Speed is governed by a braking curve, not a wish: v = sqrt(2·a·d) from the
//     ship's own directional thrust and live mass, capped at MaxCruiseMs. A ship
//     with no braking thrust along the flight line is refused (or released) with
//     the reason said out loud, and a leg that makes no progress for 25 seconds
//     is abandoned rather than flown forever.
//   • Arrival at a body holds at the surface plus ArrivalBodyAltitudeM — the same
//     90 km shelf the warp drive arrives on — and keeps station there. It will
//     NOT fly into the sun.
//   • Unseated engage gets a 3-second STAND CLEAR countdown, like the local pilot.
//     Exiting the seat mid-cruise does not stop the ship: it continues unmanned
//     and says so. P halts it from anywhere in reach.
//
// What it does not do, and will not until its own round: avoid terrain or other
// ships, fly in atmosphere, dock, use a jump leg, or follow a multi-stop route.
// Those are the auto-run's and the warp round's jobs.

using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;
using VoxelEngine.Player;
using VoxelEngine.Settings;
using VoxelEngine.UI;

namespace VoxelEngine.Navigation
{
    /// <summary>Where the fly-to leg is, in one word, for the map and the toasts.</summary>
    public enum NavFlightState { Off = 0, Departing = 1, Cruise = 2, Hold = 3, WarpAim = 4, WarpCharge = 5 }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10)]     // decide first, so the grid's physics step sees this tick's command
    public class NavFlightAutopilot : MonoBehaviour
    {
        public const string FlightOwner = "NAV AUTOPILOT";

        /// <summary>Fastest the cruise ever commands, m/s. Deep-space legs at this speed
        /// sit inside the engine's demonstrated envelope (orbital frame velocities are
        /// km/s scale), and the braking curve, not this number, governs arrival.</summary>
        public const float MaxCruiseMs = 2500f;

        /// <summary>Arrival shelf above a body's surface, m — the warp drive's own
        /// arrival altitude, so both ways of arriving park on the same shelf.</summary>
        public const float ArrivalBodyAltitudeM = 90000f;

        /// <summary>Arrival bubble around a ship, station or other non-body target, m.</summary>
        public const float ArrivalContactM = 300f;

        /// <summary>How far from the player a ship may be and still answer the P key, m.</summary>
        public const float EngageReachM = 60f;

        private const float BrakeMargin = 0.85f;      // only ever trust 85% of rated brake thrust
        private const float ArriveSpeedMs = 8f;       // slower than this inside the bubble counts as arrived
        private const float DepartCountdownS = 3f;    // unseated engage warns before the ship moves
        private const float NoProgressS = 25f;        // then the leg is abandoned, loudly

        /// <summary>The one engaged fly-to leg, or null. One ship at a time: the map
        /// status line and the P key both talk about exactly this flight.</summary>
        public static NavFlightAutopilot Active { get; private set; }
        private static int _lastToggleFrame = -1;

        public NavFlightState State { get; private set; } = NavFlightState.Off;
        public bool Engaged => Active == this && State != NavFlightState.Off;

        private GridEntity _grid;
        private float _standoffM = ArrivalContactM;
        private float _countdown;
        private float _stalled;
        private float _bestDistance = float.MaxValue;
        private bool _wasControlled;
        private float _distM;
        private float _speedMs;
        private GridWarpDrive _drive;
        private bool _warpAbandoned;
        private bool _stickArmed;
        private bool _warpCooling;
        private bool _legIsCapture;
        private int _warpFails;
        private float _warpRetryAt;
        private float _aimBest = float.MaxValue;
        private float _aimStalled;
        private float _aimAngle;
        private float _lastCharge01;
        private float _chargeStallT;
        private float _legNeedWh;
        private float _legPooledWh;

        public static NavFlightAutopilot For(GridEntity grid)
        {
            if (grid == null) return null;
            var control = grid.GetComponent<NavFlightAutopilot>();
            if (control == null) control = grid.gameObject.AddComponent<NavFlightAutopilot>();
            control._grid = grid;
            return control;
        }

        // ── Engage / disengage ─────────────────────────────────────────────

        /// <summary>Hotkey / map button: halt the live flight, or fly the nearest ship in reach.</summary>
        public static void Toggle()
        {
            // One toggle per frame: two hotkey paths once fired the same press twice
            // (engage + instant disengage). This makes that impossible by construction.
            if (_lastToggleFrame == Time.frameCount) return;
            _lastToggleFrame = Time.frameCount;
            if (Active != null && Active.Engaged)
            {
                if (Active.State == NavFlightState.Departing) Active.Disengage("Departure cancelled — ship is yours.");
                else Active.Disengage("Autopilot off — ship is yours.");
                return;
            }
            TryEngageNearest();
        }

        public static void ToggleFromMap() => Toggle();

        public static void TryEngageNearest()
        {
            // Seated: the hull you are in is the ship, even if the disabled player
            // pawn was left at the last foot position (more than EngageReachM away).
            var seated = GridCockpit.ActiveControlGrid;
            if (seated != null && seated.Body != null)
            {
                var seatedPilot = For(seated);
                if (seatedPilot == null) { Say("Autopilot failed to attach to that ship.", Warn); return; }
                if (!seatedPilot.TryEngage(out string seatedReason)) Say(seatedReason, Warn);
                return;
            }

            var player = Object.FindAnyObjectByType<PlayerController>();
            if (player == null) { Say("No pilot found.", Warn); return; }
            Vector3 at = player.transform.position;
            var seat = GridCockpit.ActiveControlSeat;
            if (seat != null) at = seat.transform.position;
            GridEntity nearest = null;
            float best = EngageReachM;
            var grids = Object.FindObjectsByType<GridEntity>(FindObjectsInactive.Exclude);
            for (int i = 0; i < grids.Length; i++)
            {
                var g = grids[i];
                if (g == null || g.Body == null) continue;
                float d = Vector3.Distance(at, g.Body.position);
                if (d < best) { best = d; nearest = g; }
            }
            if (nearest == null)
            {
                Say($"No ship in reach ({EngageReachM:0} m) — stand by your ship and press {GameSettings.GetKey(InputAction.Autopilot)}.", Warn);
                return;
            }
            var pilot = For(nearest);
            if (pilot == null) { Say("Autopilot failed to attach to that ship.", Warn); return; }
            if (!pilot.TryEngage(out string reason)) Say(reason, Warn);
        }

        private static bool HasPilotBlock(GridEntity grid)
        {
            if (grid == null) return false;
            foreach (var block in grid.AllBlocks)
                if (block is IndustrialWorld.Navigation.AutoRunPilot pilot && pilot.Enabled) return true;
            return false;
        }

        public bool TryEngage(out string reason)
        {
            reason = "";
            if (Active != null && Active != this && Active.Engaged)
            { reason = "Another ship is already flying — one fly-to leg at a time."; return false; }
            if (!NavigationTarget.HasTarget)
            { reason = "Set a nav target on the orbital map (M) first."; return false; }
            if (NavigationTarget.TargetKind == MapEntryKind.Sun)
            { reason = "The autopilot will not fly into the sun."; return false; }
            if (!NavigationTarget.TryResolve(out _))
            { reason = "Nav target lost — pick it again on the map."; return false; }
            if (_grid == null || _grid.Body == null || _grid.Body.isKinematic)
            { reason = "That grid cannot fly."; return false; }
            if (NavigationTarget.TryResolveGrid(out var targetGrid) && targetGrid == _grid)
            { reason = "That nav target is this ship."; return false; }
            if (!_grid.HasPower)
            { reason = "No control power — the drive needs a live bus."; return false; }
            if (!AtmosphereManager.IsInSpace(_grid.Body.position))
            { reason = "Reach space first — the fly-to leg is vacuum-only."; return false; }
            foreach (var block in _grid.AllBlocks)
                if ((block is GridLandingGear gear && gear.IsLocked) || (block is GridDockingPort port && port.IsDocked))
                { reason = "Release landing gear and docking locks first."; return false; }
            if (_grid.WheelControlHeld)
            { reason = "Release wheel control first."; return false; }
            if (_grid.AutonomousFlightActive)
            { reason = "Another controller is already flying this ship."; return false; }
            var local = _grid.GetComponent<IndustrialWorld.Navigation.LocalRoutePilot>();
            if (local != null && local.IsActive)
            { reason = "Stop the local route run first."; return false; }
            var loop = _grid.GetComponent<GridRouteAutopilot>();
            if (loop != null && loop.IsArmed)
            { reason = "Disarm the auto-run loop first."; return false; }
            bool anyThruster = false;
            foreach (var block in _grid.AllBlocks)
                if (block is GridThruster t && t.IsOperational) { anyThruster = true; break; }
            if (!anyThruster)
            { reason = "No working thrusters — fit and power at least one."; return false; }
            if (!HasPilotBlock(_grid))
            { reason = "No autopilot block aboard — fit an AutoRunPilot to fly hands-free."; return false; }

            var origin = SpaceOrigin.Instance;
            if (origin == null)
            { reason = "No star map fix — try again in a moment."; return false; }
            NavigationTarget.TryResolve(out double3 targetCosmic);
            Vector3 rel = origin.GetScenePos(targetCosmic) - _grid.Body.position;
            if (rel.sqrMagnitude < 1f)
            { reason = "Already there — the target is on top of the ship."; return false; }
            if (!BrakingAuthority(-rel.normalized, out _))
            { reason = "No braking thrust along the flight line — turn the ship or fit reverse thrust."; return false; }

            Active = this;
            _standoffM = ComputeStandoffM();
            _bestDistance = float.MaxValue;
            _stalled = 0f;
            _drive = null;
            _warpAbandoned = false;
            _warpCooling = false;
            _warpFails = 0;
            _warpRetryAt = 0f;
            _stickArmed = false;
            _aimBest = float.MaxValue;
            _aimStalled = 0f;
            _lastCharge01 = 0f;
            _chargeStallT = 0f;
            _wasControlled = _grid.IsControlled;
            _distM = rel.magnitude;
            _speedMs = _grid.Body.linearVelocity.magnitude;
            if (_grid.IsControlled)
            {
                State = NavFlightState.Cruise;
                Say($"Engaged — {NavigationTarget.TargetName}, {OrbitalTrackingService.FormatKm(_distM / 1000d)} out. " +
                    $"Touch the stick or press {GameSettings.GetKey(InputAction.Autopilot)} to take over.", Go);
            }
            else
            {
                State = NavFlightState.Departing;
                _countdown = DepartCountdownS;
                Say($"Departure in {DepartCountdownS:0} seconds — stand clear of {_grid.name}.", Warn);
            }
            return true;
        }

        public void Disengage(string reason)
        {
            bool wasActive = Active == this;
            if (_drive != null) { _drive.SetAutoRecharge(false); _drive = null; }
            if (_grid != null && _grid.AutonomousFlightActive && _grid.AutonomousFlightOwner == FlightOwner)
                _grid.ClearAutonomousFlight();
            State = NavFlightState.Off;
            if (wasActive) Active = null;
            if (!string.IsNullOrEmpty(reason)) Say(reason, wasActive ? Idle : Warn);
        }

        private void SilentRelease()
        {
            if (_drive != null) { _drive.SetAutoRecharge(false); _drive = null; }
            if (_grid != null && _grid.AutonomousFlightActive && _grid.AutonomousFlightOwner == FlightOwner)
                _grid.ClearAutonomousFlight();
            State = NavFlightState.Off;
            if (Active == this) Active = null;
        }

        private void OnDisable() { if (Active == this) SilentRelease(); }
        private void OnDestroy() { if (Active == this) SilentRelease(); }

        // ── Flight ─────────────────────────────────────────────────────────

        private void FixedUpdate()
        {
            if (!Engaged || _grid == null) { if (Engaged) SilentRelease(); return; }

            // Live refusals: anything that would have stopped the engage stops the leg.
            if (!NavigationTarget.HasTarget || !NavigationTarget.TryResolve(out double3 targetCosmic))
            { Disengage("Nav target lost — holding position."); return; }
            if (NavigationTarget.TargetKind == MapEntryKind.Sun)
            { Disengage("Nav target is the sun — releasing the ship."); return; }
            if (!_grid.HasPower)
            { Disengage("Control power lost — holding position."); return; }
            if (_grid.Body == null || _grid.Body.isKinematic)
            { Disengage("Drive locked — holding position."); return; }
            var origin = SpaceOrigin.Instance;
            if (origin == null)
            { Disengage("Star map fix lost — holding position."); return; }
            if (!HasPilotBlock(_grid))
            { Disengage("Autopilot block missing — fit an AutoRunPilot to fly hands-free."); return; }
            // The stick is a take-over, not a suggestion: any manual thrust input
            // disengages (armed on first thrust-free tick so engaging mid-flight
            // while thrusting does not kick instantly). Mouse never triggers this
            // — the autopilot owns the gyros, including warp aim.
            if (_grid.HasManualThrustInput())
            {
                if (_stickArmed) { Disengage("Autopilot disabled — player input detected."); return; }
            }
            else _stickArmed = true;

            // Seat changes never stop the ship; they change who is responsible for it.
            if (_grid.IsControlled != _wasControlled)
            {
                _wasControlled = _grid.IsControlled;
                if (!_wasControlled) Say($"Continuing unmanned — press {GameSettings.GetKey(InputAction.Autopilot)} within reach to halt the ship.", Warn);
                else Say("Pilot aboard — touch the stick to take over.", Go);
            }

            if (State == NavFlightState.Departing)
            {
                _countdown -= Time.fixedDeltaTime;
                if (_countdown > 0f) return;
                State = NavFlightState.Cruise;
                Say($"Departing for {NavigationTarget.TargetName}.", Go);
            }

            Vector3 shipPos = _grid.Body.position;
            if (!AtmosphereManager.IsInSpace(shipPos))
            { Disengage("Entered atmosphere — vacuum legs only. Ship is yours."); return; }
            Vector3 velocity = _grid.Body.linearVelocity;
            Vector3 rel = origin.GetScenePos(targetCosmic) - shipPos;
            float d = rel.magnitude;
            float speed = velocity.magnitude;
            _distM = d;
            _speedMs = speed;

            // A hold is a promise to stay, not to sleep: if the target wanders out of
            // the bubble (a moving contact), the cruise resumes and says so.
            if (State == NavFlightState.Hold && d > _standoffM + 500f)
            {
                State = NavFlightState.Cruise;
                _bestDistance = d;
                _stalled = 0f;
                Say($"Target moved — resuming cruise to {NavigationTarget.TargetName}.", Go);
            }

            Vector3 desired;
            if (State == NavFlightState.Hold || (d <= _standoffM && speed < ArriveSpeedMs))
            {
                if (State != NavFlightState.Hold)
                {
                    State = NavFlightState.Hold;
                    Say($"Holding at {NavigationTarget.TargetName} — {GameSettings.GetKey(InputAction.Autopilot)} releases the ship.", Go);
                }
                desired = Vector3.zero;
            }
            else
            {
                State = NavFlightState.Cruise;
                if (d < 1f) { desired = Vector3.zero; }
                else
                {
                    // Brake toward the bubble, never toward the rock: the curve spends
                    // the whole remaining distance stopping, from live thrust and mass.
                    Vector3 brakeDir = speed > 1f ? -velocity / speed : -rel / d;
                    if (!BrakingAuthority(brakeDir, out float brakeA))
                    { Disengage("Lost braking thrust along the flight line — holding position."); return; }
                    float vAllow = Mathf.Sqrt(2f * brakeA * Mathf.Max(0f, d - _standoffM));
                    desired = rel / d * Mathf.Min(vAllow, MaxCruiseMs);
                }

                // No progress is a decision, not a patience test.
                if (desired.magnitude > 20f)
                {
                    if (d < _bestDistance - 1f) { _bestDistance = d; _stalled = 0f; }
                    else
                    {
                        _stalled += Time.fixedDeltaTime;
                        if (_stalled > NoProgressS)
                        { Disengage("Making no progress — blocked, becalmed or out of thrust. Holding."); return; }
                    }
                }
                else { _bestDistance = Mathf.Min(_bestDistance, d); _stalled = 0f; }
                WarpTick(rel, d);
            }

            if (State != NavFlightState.WarpAim && State != NavFlightState.WarpCharge)
                SteerCruise(desired, rel);
            _grid.SetAutonomousFlight(desired, FlightOwner);
        }

        // ── Warp legs ────────────────────────────────────────────────────
        // Long legs jump: a body inside the drive's capture band is taken in one
        // planet-lock jump (which lands on this autopilot's own hold shelf), anything
        // past one fixed hop closes by repeated aimed hops, and short legs cruise.
        // The ship keeps cruising while it aims and charges — a jump zeroes velocity
        // anyway, so there is nothing to gain by stopping first.

        private void WarpTick(Vector3 rel, float d)
        {
            _warpCooling = false;
            if (_drive == null || _drive.Grid != _grid || !_drive.Enabled)
                _drive = FindDrive();
            bool wantWarp = !_warpAbandoned && _drive != null && Time.unscaledTime >= _warpRetryAt;
            var kind = NavigationTarget.TargetKind;
            bool bodyTarget = kind == MapEntryKind.Planet || kind == MapEntryKind.Moon;
            _legIsCapture = false;
            if (wantWarp)
            {
                double dKm = d / 1000d;
                _legIsCapture = bodyTarget && dKm > _drive.minJumpKm * 2d && dKm <= 20000d;
                bool hopLeg = !_legIsCapture && d > _drive.LiveHopKm * 1000f + 100000f;
                wantWarp = _legIsCapture || hopLeg;
            }
            if (!wantWarp)
            {
                if (State == NavFlightState.WarpAim || State == NavFlightState.WarpCharge)
                    State = NavFlightState.Cruise;
                if (_drive != null) _drive.SetAutoRecharge(false);
                _aimBest = float.MaxValue;
                _aimStalled = 0f;
                return;
            }
            if (_drive.Cooldown01 > 0f)
            {
                State = NavFlightState.Cruise;
                _warpCooling = true;
                return;
            }
            if (d < 1f) { State = NavFlightState.Cruise; return; }

            // Energy: the leg must be banked before the spool matters. The drive
            // auto-charges for the leg (player toggle untouched); aiming continues
            // in parallel so the ship is lined up when the bank fills. Price and hop
            // already include the live mass penalty, so a loaded hauler banks more.
            double legKm = _legIsCapture ? d / 1000d : _drive.LiveHopKm;
            // Bank the leg price plus the drive's arrival reserve, or every capture
            // leg would land a reserve short and re-spool for a second hop.
            _legNeedWh = (float)(legKm * GridWarpDrive.EffectiveRateWhPerKm(_grid) * (1f + Mathf.Clamp01(_drive.arrivalReserveFraction)));
            _legPooledWh = GridWarpDrive.PooledStoredWh(_grid);
            _drive.SetAutoRecharge(_legPooledWh < _legNeedWh - 0.01f);

            Vector3 dir = rel / d;
            // Aim through the exact frame the drive fires along (cockpit when there is
            // one, else grid forward) — checking grid forward while the drive fires
            // along a sideways cockpit would jump the wrong way.
            Transform aimFrame = _drive.Grid.ActiveCockpit != null
                ? _drive.Grid.ActiveCockpit.transform : _grid.transform;
            float angle = Vector3.Angle(aimFrame.forward, dir);
            float fireAngle = _legIsCapture ? Mathf.Min(7f, _drive.targetConeDeg * 0.5f) : 2f;
            _aimAngle = angle;
            if (!HasGyroAuthority())
            { AbandonWarp("no gyro authority — fit gyroscopes to aim the warp cone"); return; }
            // Point the frame the drive fires along at the target. Seated or not —
            // the mouse is parked while the autopilot is live, so the gyros have
            // to do this or the cone never lines up.
            SteerAxisToward(aimFrame.forward, dir);
            if (angle < _aimBest - 0.2f) { _aimBest = angle; _aimStalled = 0f; }
            else
            {
                _aimStalled += Time.fixedDeltaTime;
                if (_aimStalled > 90f) { AbandonWarp("cannot aim the ship"); return; }
            }

            _drive.BeginCharge(); // idempotent: charges, holds at full, never double-starts
            State = angle <= fireAngle ? NavFlightState.WarpCharge : NavFlightState.WarpAim;

            if (_drive.IsCharging)
            {
                if (Mathf.Abs(_drive.Charge01 - _lastCharge01) < 0.0005f) _chargeStallT += Time.fixedDeltaTime;
                else { _chargeStallT = 0f; _lastCharge01 = _drive.Charge01; }
            }
            else { _chargeStallT = 0f; _lastCharge01 = _drive.Charge01; }

            if (_drive.IsReady && angle <= fireAngle && _legPooledWh >= _legNeedWh - 0.01f)
            {
                if (_drive.TryWarp(skipConfirm: true))
                {
                    State = NavFlightState.Cruise;
                    _warpFails = 0;
                    _grid.SetAutonomousRotation(0f, 0f, 0f);
                    _bestDistance = float.MaxValue;
                    _stalled = 0f;
                    _aimBest = float.MaxValue;
                    _aimStalled = 0f;
                }
                else
                {
                    _warpFails++;
                    if (_warpFails >= 3) AbandonWarp("warp drive refused to fire");
                    else
                    {
                        _warpRetryAt = Time.unscaledTime + 10f;
                        State = NavFlightState.Cruise;
                        Say("Warp refused — cruising, will retry.", Warn);
                    }
                }
            }
        }

        private void AbandonWarp(string reason)
        {
            _warpAbandoned = true;
            State = NavFlightState.Cruise;
            _grid.SetAutonomousRotation(0f, 0f, 0f);
            Say($"Warp abandoned ({reason}) — cruising the rest.", Warn);
        }

        private GridWarpDrive FindDrive()
        {
            GridWarpDrive fallback = null;
            foreach (var block in _grid.AllBlocks)
            {
                if (block is not GridWarpDrive w || !w.Enabled || w.Grid != _grid) continue;
                if (w.IsReady) return w;
                if (w.IsCharging && fallback == null) fallback = w;
                else if (w.Cooldown01 <= 0f && (fallback == null || !fallback.IsCharging)) fallback = w;
                else if (fallback == null) fallback = w;
            }
            return fallback;
        }

        /// <summary>Cruise: swing the strongest thrust axis onto the commanded
        /// velocity (or the remaining line to the target when holding still).
        /// A ship with no gyros still translates; it just keeps its current heading.</summary>
        private void SteerCruise(Vector3 desired, Vector3 rel)
        {
            if (!HasGyroAuthority())
            {
                _grid.SetAutonomousRotation(0f, 0f, 0f);
                return;
            }
            Vector3 point = desired.sqrMagnitude > 1f ? desired : rel;
            if (point.sqrMagnitude < 1f)
            {
                _grid.SetAutonomousRotation(0f, 0f, 0f);
                return;
            }
            SteerAxisToward(BestThrustWorldAxis(), point);
        }

        /// <summary>World-space push direction of the strongest of the six axes.
        /// That is the hull's "nose" for a cruise: point it at the destination and
        /// the main engines do the work instead of the weak laterals.</summary>
        private Vector3 BestThrustWorldAxis()
        {
            var t = _grid.GetThrustByDirection();
            float best = t.fwd;
            Vector3 local = Vector3.forward;
            if (t.back  > best) { best = t.back;  local = Vector3.back; }
            if (t.right > best) { best = t.right; local = Vector3.right; }
            if (t.left  > best) { best = t.left;  local = Vector3.left; }
            if (t.up    > best) { best = t.up;    local = Vector3.up; }
            if (t.down  > best) { best = t.down;  local = Vector3.down; }
            if (best <= 0f) return _grid.transform.forward;
            return _grid.transform.TransformDirection(local);
        }

        /// <summary>P-controller: torque the given world axis onto worldDir.
        /// Grid-local yaw/pitch, no commanded roll. Full rate past ~25 degrees.</summary>
        private void SteerAxisToward(Vector3 worldAxis, Vector3 worldDir)
        {
            if (worldAxis.sqrMagnitude < 1e-8f || worldDir.sqrMagnitude < 1e-8f)
            {
                _grid.SetAutonomousRotation(0f, 0f, 0f);
                return;
            }
            worldAxis.Normalize();
            worldDir.Normalize();
            float angle = Vector3.Angle(worldAxis, worldDir);
            if (angle < 0.5f)
            {
                _grid.SetAutonomousRotation(0f, 0f, 0f);
                return;
            }
            Vector3 axis = Vector3.Cross(worldAxis, worldDir);
            if (axis.sqrMagnitude < 1e-8f)
                axis = Vector3.Cross(worldAxis, Mathf.Abs(worldAxis.y) < 0.9f ? Vector3.up : Vector3.right);
            float gain = Mathf.Clamp01(angle / 25f) * 2f;
            Vector3 localAxis = _grid.transform.InverseTransformDirection(axis.normalized * gain);
            _grid.SetAutonomousRotation(
                Mathf.Clamp(localAxis.y, -1f, 1f),
                Mathf.Clamp(localAxis.x, -1f, 1f), 0f);
        }

        private bool HasGyroAuthority()
        {
            foreach (var block in _grid.AllBlocks)
                if (block is GridGyroscope gy && gy.Enabled && gy.torquePower > 0f) return true;
            return false;
        }

        /// <summary>Conservative braking acceleration available along a world-space
        /// direction, from rated directional thrust and live body mass.</summary>
        private bool BrakingAuthority(Vector3 worldDir, out float accelMs2)
        {
            accelMs2 = 0f;
            if (_grid == null || _grid.Body == null) return false;
            Vector3 local = _grid.transform.InverseTransformDirection(worldDir.normalized);
            var t = _grid.GetThrustByDirection();
            float newtons = t.fwd * Mathf.Max(0f, local.z) + t.back * Mathf.Max(0f, -local.z)
                          + t.right * Mathf.Max(0f, local.x) + t.left * Mathf.Max(0f, -local.x)
                          + t.up * Mathf.Max(0f, local.y) + t.down * Mathf.Max(0f, -local.y);
            float mass = Mathf.Max(1f, _grid.Body.mass);
            if (newtons < 0.05f * mass) return false;
            accelMs2 = Mathf.Max(0.05f, newtons * BrakeMargin / mass);
            return true;
        }

        private static float ComputeStandoffM()
        {
            var kind = NavigationTarget.TargetKind;
            bool isBody = kind == MapEntryKind.Sun || kind == MapEntryKind.Planet || kind == MapEntryKind.Moon;
            if (!isBody) return ArrivalContactM;
            var entries = OrbitalTrackingService.Entries;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Name == NavigationTarget.TargetName && entries[i].Kind == kind)
                    return (float)(entries[i].RadiusKm * 1000.0 + ArrivalBodyAltitudeM);
            return ArrivalBodyAltitudeM + 60000f; // unknown body: a cautious extra shelf
        }

        // ── Key pump + status ──────────────────────────────────────────────

        public static void Tick()
        {
            if (UIState.TextInputActive) return;
            if (GameSettings.WasPressed(InputAction.Autopilot)) Toggle();
        }

        /// <summary>The grid the active autopilot is flying (null when disengaged).</summary>
        public static GridEntity ActiveGrid
        {
            get
            {
                var a = Active;
                return a != null && a.Engaged && a._grid != null ? a._grid : null;
            }
        }

        /// <summary>Engaged state of the active autopilot (Off when disengaged).</summary>
        public static NavFlightState ActiveState => Active != null && Active.Engaged ? Active.State : NavFlightState.Off;

        /// <summary>One live line for an in-progress warp leg (aim/charge/bank); empty
        /// unless the active autopilot is currently flying a warp leg. Shared by the
        /// orbital map status and the cockpit warp readout.</summary>
        public static string WarpLegLine
        {
            get
            {
                var a = Active;
                if (a == null || !a.Engaged || a._grid == null) return "";
                if (a.State != NavFlightState.WarpAim && a.State != NavFlightState.WarpCharge) return "";
                string aim = $"AIM {a._aimAngle:0.0}° OFF";
                string chg = a._drive != null
                    ? (a._drive.IsReady ? "DRIVE READY" : $"CHARGE {a._drive.Charge01 * 100f:0}%")
                    : "NO DRIVE";
                string bank = a._drive != null && a._drive.Grid != null
                    ? $"BANK {a._legPooledWh / 1000f:0.0}/{a._legNeedWh / 1000f:0.0} kWh" +
                      (a._legPooledWh < a._legNeedWh - 0.01f ? (a._drive.GridStarved ? " STARVED" : "") : " OK")
                    : "";
                string stall = a._chargeStallT > 5f ? " · STALLED (power?)" : "";
                string leg = a._legIsCapture ? " · LOCK" : " · HOP";
                return (a.State == NavFlightState.WarpAim ? "WARP·AIM " : "WARP·CHARGE ") + aim +
                       " · " + chg + (string.IsNullOrEmpty(bank) ? "" : " · " + bank) + leg + stall +
                       (a._grid.IsControlled ? "" : " (unmanned)");
            }
        }

        /// <summary>One live line for the orbital map: state, target, distance, speed, ETA.</summary>
        public static string StatusLine
        {
            get
            {
                var a = Active;
                if (a == null || !a.Engaged || a._grid == null) return "";
                if (a.State == NavFlightState.Departing)
                    return $"AUTO·DEPARTING {NavigationTarget.TargetName} in {Mathf.CeilToInt(a._countdown)} — STAND CLEAR";
                string warpLeg = WarpLegLine;
                if (!string.IsNullOrEmpty(warpLeg)) return warpLeg;

                if (a.State == NavFlightState.Hold)
                    return $"AUTO·HOLD at {NavigationTarget.TargetName}" + (a._grid.IsControlled ? "" : " (unmanned)");
                double etaS = a._speedMs > 30f ? a._distM / a._speedMs : a._distM / 40d;
                string line = $"AUTO·CRUISE {NavigationTarget.TargetName} · {OrbitalTrackingService.FormatKm(a._distM / 1000d)} · " +
                       $"{a._speedMs:0} m/s · ETA {FormatEta(etaS)}" + (a._grid.IsControlled ? "" : " (unmanned)");
                if (a._warpCooling && a._drive != null)
                    line += $" · WARP COOLDOWN {FormatEta(a._drive.Cooldown01 * a._drive.cooldownSeconds)}";
                else if (a._warpAbandoned)
                    line += " · NO WARP";
                return line;
            }
        }

        private static string FormatEta(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d) return "--:--";
            long s = (long)seconds;
            long h = s / 3600, m = (s % 3600) / 60;
            if (h > 0) return $"{h}:{m:00}:{(s % 60):00}";
            return $"{m}:{(s % 60):00}";
        }

        private static readonly Color Go = new Color(0.55f, 0.95f, 0.65f);
        private static readonly Color Warn = new Color(1f, 0.7f, 0.25f);
        private static readonly Color Idle = new Color(0.70f, 0.75f, 0.80f);

        private static void Say(string msg, Color color) =>
            BuildFeedbackHud.Show("Autopilot", msg, null, color);
    }
}
