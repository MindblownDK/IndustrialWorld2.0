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
//     release, and rotation always stays live. See GridEntity.cruiseTranslating.
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
    public enum NavFlightState { Off = 0, Departing = 1, Cruise = 2, Hold = 3 }

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

        public static NavFlightAutopilot For(GridEntity grid)
        {
            if (grid == null) return null;
            var control = grid.GetComponent<NavFlightAutopilot>();
            if (control == null) control = grid.gameObject.AddComponent<NavFlightAutopilot>();
            control._grid = grid;
            return control;
        }

        // ── Engage / disengage ─────────────────────────────────────────────

        /// <summary>P key / map button: halt the live flight, or fly the nearest ship in reach.</summary>
        public static void Toggle()
        {
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
            var player = Object.FindAnyObjectByType<PlayerController>();
            if (player == null) { Say("No pilot found.", Warn); return; }
            Vector3 at = player.transform.position;
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
            _wasControlled = _grid.IsControlled;
            _distM = rel.magnitude;
            _speedMs = _grid.Body.linearVelocity.magnitude;
            if (_grid.IsControlled)
            {
                State = NavFlightState.Cruise;
                Say($"Engaged — {NavigationTarget.TargetName}, {OrbitalTrackingService.FormatKm(_distM / 1000d)} out. " +
                    $"Stick overrides, {GameSettings.GetKey(InputAction.Autopilot)} disengages.", Go);
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
            if (_grid != null && _grid.AutonomousFlightActive && _grid.AutonomousFlightOwner == FlightOwner)
                _grid.ClearAutonomousFlight();
            State = NavFlightState.Off;
            if (wasActive) Active = null;
            if (!string.IsNullOrEmpty(reason)) Say(reason, wasActive ? Idle : Warn);
        }

        private void SilentRelease()
        {
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
            if (!_grid.HasPower)
            { Disengage("Control power lost — holding position."); return; }
            if (_grid.Body == null || _grid.Body.isKinematic)
            { Disengage("Drive locked — holding position."); return; }
            var origin = SpaceOrigin.Instance;
            if (origin == null)
            { Disengage("Star map fix lost — holding position."); return; }

            // Seat changes never stop the ship; they change who is responsible for it.
            if (_grid.IsControlled != _wasControlled)
            {
                _wasControlled = _grid.IsControlled;
                if (!_wasControlled) Say("Continuing unmanned — press P within reach to halt the ship.", Warn);
                else Say("Pilot aboard — stick overrides, cruise resumes on release.", Go);
            }

            if (State == NavFlightState.Departing)
            {
                _countdown -= Time.fixedDeltaTime;
                if (_countdown > 0f) return;
                State = NavFlightState.Cruise;
                Say($"Departing for {NavigationTarget.TargetName}.", Go);
            }

            Vector3 shipPos = _grid.Body.position;
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
            }

            _grid.SetAutonomousFlight(desired, FlightOwner);
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

        /// <summary>One live line for the orbital map: state, target, distance, speed, ETA.</summary>
        public static string StatusLine
        {
            get
            {
                var a = Active;
                if (a == null || !a.Engaged || a._grid == null) return "";
                if (a.State == NavFlightState.Departing)
                    return $"AUTO·DEPARTING {NavigationTarget.TargetName} in {Mathf.CeilToInt(a._countdown)} — STAND CLEAR";
                if (a._grid.HasManualThrustInput())
                    return $"AUTO·OVERRIDE — stick has {a._grid.name}, cruise resumes on release";
                if (a.State == NavFlightState.Hold)
                    return $"AUTO·HOLD at {NavigationTarget.TargetName}" + (a._grid.IsControlled ? "" : " (unmanned)");
                double etaS = a._speedMs > 30f ? a._distM / a._speedMs : a._distM / 40d;
                return $"AUTO·CRUISE {NavigationTarget.TargetName} · {OrbitalTrackingService.FormatKm(a._distM / 1000d)} · " +
                       $"{a._speedMs:0} m/s · ETA {FormatEta(etaS)}" + (a._grid.IsControlled ? "" : " (unmanned)");
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
