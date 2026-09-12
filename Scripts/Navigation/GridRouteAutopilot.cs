// Assets/Scripts/VoxelEngine/Navigation/GridRouteAutopilot.cs
//
// THE AUTO-RUN — the ship flies the book's route, stops where it was told, and quits when the
// arithmetic says stop.
//
// This is deliberately the smallest honest autonomy in the game, and three rules keep it that way:
//
//   1. It commands a VELOCITY, through `GridEntity.SetAutonomousFlight`, which is a sibling of the
//      dampener channel. It never seats a ghost pilot, so an unmanned shuttle cannot inherit the
//      cockpit's camera, tools or "somebody is aboard" privileges, and the pilot's keys outrank it
//      the instant they are pressed.
//   2. It prices what it still has to fly with the SAME call the panel shows — `ShipRoute.RemainingFrom`
//      through `GridRoutePlanner.Evaluate`. A schedule that uses different numbers from its own route
//      book is how a fleet ends up parked in the dark between two planets.
//   3. Every stop condition is armed by the player, and one of them is `UNTIL IT RUNS OUT`: no reserve,
//      fly until the arithmetic refuses, then sit where you are and say why. It is offered because it
//      is a real thing a player wants to do, and confirm-once because it is a real way to lose a ship.
//
// What it does not do, and will not until its own round: avoid terrain, fly a dock approach, schedule
// cargo operations, reroute around a hazard, or use a jump leg.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Navigation
{
    /// <summary>What a loop does when it reaches the far end.</summary>
    public enum AutoRunMode { ContinuousRoundTrip = 0, OneWayThenPark = 1, FixedRunCount = 2 }

    /// <summary>Where the loop is, in one word, for the HUD and the panel.</summary>
    public enum AutoRunState { Halted = 0, Outbound = 1, Inbound = 2, ServiceOutbound = 3, ServiceInbound = 4, Paused = 5, Blocked = 6 }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10)]     // decide first, so the grid's physics step sees this tick's command
    public class GridRouteAutopilot : MonoBehaviour
    {
        [Header("Loop")]
        [Tooltip("The route flown outward. Its recorded points are the road; the waymarks below are " +
                 "what the ends of that road are called now.")]
        public string routeName = "";

        [Tooltip("Named waymarks to run between. A waymark is read live, so a pad on a ship that has " +
                 "moved, or a connector on a base orbiting a moon, is flown to where it actually is.")]
        public string startWaymark = "";
        public string endWaymark = "";

        public AutoRunMode mode = AutoRunMode.ContinuousRoundTrip;
        public int fixedRuns = 4;

        [Header("Targets — held on the ship, because the ship owns the tanks")]
        [Tooltip("State of charge the visitor leaves at. The connector serves; the ship decides when " +
                 "it has had enough, which is the only arrangement that stops a station being blamed " +
                 "for a shuttle that will not leave.")]
        [Range(0.05f, 1f)] public float targetCharge01 = 0.9f;

        [Tooltip("Fraction of the ship's own fuel and hydrogen capacity to top up to. Zero turns a " +
                 "service off without losing the number.")]
        [Range(0f, 1f)] public float targetFuel01 = 0.85f;
        [Range(0f, 1f)] public float targetHydrogen01 = 0.85f;

        [Header("Armed stop conditions")]
        [Tooltip("Below this share of stored energy the loop will not start a leg, and an airborne " +
                 "loop lands where it can. Zero disables the reserve rule entirely — that is what " +
                 "UNTIL IT RUNS OUT means, and it is confirm-once from the panel.")]
        [Range(0f, 1f)] public float minimumReserve01 = 0.25f;

        [Tooltip("Halt when the worst block on the hull is hurt past this fraction, or when cargo " +
                 "fills or empties the way the player set it.")]
        [Range(0f, 1f)] public float haltOnWorstBlockHurt01 = 0.25f;
        public bool stopWhenCargoFull = true;
        public bool stopWhenCargoEmpty = false;

        [Header("Handling")]
        [Tooltip("How close, in cells, counts as having arrived at a pad.")]
        public float holdRadiusCells = 4f;
        [Tooltip("Speed under which an arrival is an arrival, m/s.")]
        public float settleSpeedMs = 3f;
        [Tooltip("The loop never plans faster than the route book's own cruise ceiling unless you raise " +
                 "this deliberately, because the plan and the flight would then disagree.")]
        public float maxFlightMs = 0f;
        [Tooltip("How long the ship will wait at a pad for its targets before it gives up and says so.")]
        public float serviceTimeoutSeconds = 900f;

        // ── Runtime ──────────────────────────────────────────────────────────
        public AutoRunState State { get; private set; } = AutoRunState.Halted;
        public string BlockReason { get; private set; } = "";
        public int RunsCompleted { get; private set; }
        public float LastServiceSeconds { get; private set; }
        /// <summary>Whatever pad is serving this ship: a grid connector or a static ground pad. The
        /// loop does not care which, and it must not start caring — that is how two refuel surfaces
        /// grow into two behaviours.
        public IRefuelPad ServingPad { get; private set; }
        /// <summary>Kept for 9.35.0-dev callers; null when the pad is a static one.</summary>
        public GridConnectorBlock ServingConnector => ServingPad as GridConnectorBlock;

        GridEntity _grid;
        float _decideTimer;
        float _serviceClock;
        Vector3 _holdPoint;
        bool _holdPointValid;
        readonly List<string> _log = new(6);

        public IReadOnlyList<string> Log => _log;
        public bool IsArmed => State != AutoRunState.Halted;
        public bool IsFlying => State == AutoRunState.Outbound || State == AutoRunState.Inbound;
        public bool IsHoldingForTransfer => State == AutoRunState.ServiceOutbound || State == AutoRunState.ServiceInbound;

        /// <summary>Read by the connector, every tick, to decide whether the head of its queue may
        /// leave. `Wants*` are the ship's own arithmetic; the pad never tells a ship it is full.</summary>
        public bool WantsPower => Grid != null && Charge01 < Mathf.Clamp01(targetCharge01);
        public bool WantsHydrogen => targetHydrogen01 > 0.001f && Hydrogen01 < Mathf.Clamp01(targetHydrogen01);
        public bool WantsFuel => targetFuel01 > 0.001f && Fuel01 < Mathf.Clamp01(targetFuel01);
        public bool TargetsMet => !WantsPower && !WantsHydrogen && !WantsFuel;

        public float Charge01
        {
            get
            {
                if (Grid == null) return 1f;
                float stored = 0f, cap = 0f;
                foreach (var b in Grid.AllBlocks)
                    if (b is GridBattery bat) { stored += Mathf.Max(0f, bat.storedWh); cap += Mathf.Max(0f, bat.capacityWh); }
                return cap > 0.01f ? Mathf.Clamp01(stored / cap) : 1f;
            }
        }

        public float Hydrogen01
        {
            get
            {
                if (Grid == null) return 1f;
                float stored = 0f, cap = 0f;
                foreach (var b in Grid.AllBlocks)
                    if (b is GridGasTank tank && tank.gasType == VoxelEngine.Gas.GasType.Hydrogen)
                    { stored += Mathf.Max(0f, tank.stored); cap += Mathf.Max(0f, tank.capacity); }
                return cap > 0.01f ? Mathf.Clamp01(stored / cap) : (Grid.HydrogenStored > 0.01f ? 1f : 0f);
            }
        }

        public float Fuel01
        {
            get
            {
                if (Grid == null) return 1f;
                var network = GridLiquidNetwork.Instance;
                var tanks = network != null ? network.GetTanks(Grid) : null;
                if (tanks == null || tanks.Count == 0) return 1f;
                float stored = 0f, cap = 0f;
                for (int i = 0; i < tanks.Count; i++)
                {
                    var t = tanks[i];
                    if (t == null || t.liquidType == LiquidType.Water) continue;   // water is not fuel
                    stored += Mathf.Max(0f, t.stored); cap += Mathf.Max(0f, t.capacity);
                }
                return cap > 0.01f ? Mathf.Clamp01(stored / cap) : 1f;
            }
        }

        GridEntity Grid => _grid != null ? _grid : (_grid = GetComponent<GridEntity>());
        /// <summary>Autonomy authority lives on the recorder: a grid flies a loop only if it can cost
        /// one. Looked for on the grid root first (where a builder puts a ship block) and then below,
        /// so the same code works whether the recorder was parented to the grid or dropped on it.</summary>
        GridRouteRecorder Recorder => GetComponentInParent<GridRouteRecorder>() != null
            ? GetComponentInParent<GridRouteRecorder>()
            : GetComponentInChildren<GridRouteRecorder>();

        void Awake()
        {
            _grid = GetComponent<GridEntity>();
            if (_grid == null) _grid = GetComponentInParent<GridEntity>();
        }

        private void ClearOwnedFlight()
        {
            if (Grid != null && Grid.AutonomousFlightOwner != null
                && Grid.AutonomousFlightOwner.StartsWith("AUTO RUN", System.StringComparison.Ordinal))
                Grid.ClearAutonomousFlight();
        }

        void OnDestroy() { ClearOwnedFlight(); }
        void OnDisable() { ClearOwnedFlight(); }

        void Update()
        {
            if (Grid == null || !IsArmed) { ClearOwnedFlight(); return; }

            // The pilot's keys win, immediately and without argument. A ship whose human grabs the
            // controls mid-leg is not a failure of the loop; it is the loop working as designed.
            if (Grid.HasManualThrustInput())
            {
                ClearOwnedFlight();
                return;
            }

            _decideTimer -= Time.unscaledDeltaTime;
            if (_decideTimer <= 0f)
            {
                _decideTimer = 0.2f;                 // 5 Hz decisions: a shuttle is not a fighter
                Decide();
            }
        }

        /// <summary>The command is refreshed every physics step, deliberately, while the *decisions*
        /// above run at 5 Hz. `GridEntity` releases a command it has not seen for a moment, so a
        /// command written at the decision rate would go stale three times a second and the ship would
        /// shrug the autopilot off mid-leg. Steering is cheap; the reasoning is not.</summary>
        void FixedUpdate()
        {
            if (Grid == null || !IsArmed) return;
            if (Grid.HasManualThrustInput()) return;      // the pilot has the helm: no command, no claim
            Steer();
        }

        // ── Commands ─────────────────────────────────────────────────────────
        public bool Arm()
        {
            if (Grid == null) return false;
            var local = Grid.GetComponent<IndustrialWorld.Navigation.LocalRoutePilot>();
            if (local != null && local.IsActive)
            { BlockReason = "Stop local navigation before arming the shuttle loop."; return false; }
            var chosen = Recorder != null && Recorder.Book != null ? Recorder.Book.Find(routeName) : null;
            if (chosen != null && (chosen.sceneCoordinates || chosen.travelMode != RouteTravelMode.LegacyFlight))
            { BlockReason = "Use START SELECTED ROUTE in Local Navigation for this route."; return false; }
            // Authority comes from the recorder: a grid flies a loop only if it can cost one. Two
            // answers to "can this ship make the trip" is how a schedule and a plan drift apart, and
            // that drift is what leaves a ship parked between two worlds with a happy-looking log.
            if (Recorder == null)
            {
                BlockReason = "No route recorder on this grid: nothing here can cost the flight, so nothing here may commit to it.";
                State = AutoRunState.Blocked;
                PushLog(BlockReason);
                return false;
            }
            if (string.IsNullOrWhiteSpace(startWaymark) && string.IsNullOrWhiteSpace(endWaymark)
                && (Recorder?.Book?.Find(routeName) == null || !Recorder.Book.Find(routeName).IsFlyable))
            {
                BlockReason = "Nothing to fly: name both ends, or pick a recorded route with at least two points.";
                State = AutoRunState.Blocked;
                PushLog(BlockReason);
                return false;
            }
            BlockReason = "";
            State = State == AutoRunState.Paused ? AutoRunState.Outbound : AutoRunState.Outbound;
            _serviceClock = 0f;
            PushLog("Armed · " + (string.IsNullOrWhiteSpace(endWaymark) ? routeName : endWaymark));
            return true;
        }

        public void Disarm(string reason = null)
        {
            State = AutoRunState.Halted;
            ClearOwnedFlight();
            ServingPad = null;
            if (!string.IsNullOrEmpty(reason)) { BlockReason = reason; PushLog(reason); }
            else PushLog("Disarmed");
        }

        public void Pause()
        {
            if (!IsArmed) return;
            State = AutoRunState.Paused;
            ClearOwnedFlight();
            PushLog("Paused");
        }

        /// <summary>Reloaded mid-flight, but on purpose not *in* flight: the schedule and the run
        /// count come back, the ship sits still, and one button press hands it the helm again. A world
        /// that resumes a burn the instant it loads is a world that moves a ship into a planet because
        /// the player alt-tabbed on the wrong second.</summary>
        public void RestorePaused(int runs)
        {
            RunsCompleted = Mathf.Max(0, runs);
            BlockReason = "Reloaded: the loop is here, the ship is not flying it yet.";
            State = AutoRunState.Paused;
            PushLog("Reloaded · run " + RunsCompleted);
        }

        public void Resume()
        {
            if (State != AutoRunState.Paused) return;
            State = AutoRunState.Outbound;
            PushLog("Resumed");
        }

        /// <summary>The dangerous switch, made explicit rather than hidden: zero reserve means the
        /// flight rules stop holding anything back. The panel confirms before it calls this.</summary>
        public void SetRunUntilItRunsOut(bool on)
        {
            minimumReserve01 = on ? 0f : RouteRules.MinimumReserve01;
            PushLog(on ? "RESERVE RULE OFF — flying until the numbers refuse" : "Reserve rule restored");
        }

        // ── The decision loop ────────────────────────────────────────────────
        void Decide()
        {
            var grid = Grid;
            if (grid == null) { Disarm("No grid to fly."); return; }

            if (HurtFraction() < 1f - Mathf.Clamp01(haltOnWorstBlockHurt01))
            {
                HoldStation("Halt: hull damage past the armed threshold.");
                return;
            }
            if (stopWhenCargoFull && CargoFill01() >= 0.999f) { HoldStation("Halt: cargo full, as armed."); return; }
            if (stopWhenCargoEmpty && CargoFill01() <= 0.001f) { HoldStation("Halt: cargo empty, as armed."); return; }

            switch (State)
            {
                case AutoRunState.ServiceOutbound:
                case AutoRunState.ServiceInbound:
                    ServiceTick();
                    break;

                case AutoRunState.Outbound:
                case AutoRunState.Inbound:
                case AutoRunState.Blocked:
                    TravelTick();
                    break;
            }
        }

        void ServiceTick()
        {
            _serviceClock += 0.2f;
            if (TargetsMet)
            {
                LastServiceSeconds = _serviceClock;
                _serviceClock = 0f;
                var pad = ServingPad;
                ServingPad = null;
                if (pad != null) pad.Eject(Grid);           // the pad's queue is the pad's business, but a
            }                                               // ship that is done announces that it is done
            else if (serviceTimeoutSeconds > 0.01f && _serviceClock > serviceTimeoutSeconds)
            {
                HoldStation("Timed out waiting for service: " + DeficitLine() + ".");
                if (ServingPad != null) { ServingPad.Eject(Grid); ServingPad = null; }
            }
        }

        void TravelTick()
        {
            var grid = Grid;
            string label = State == AutoRunState.Inbound ? startWaymark : endWaymark;
            if (string.IsNullOrWhiteSpace(label))
            {
                // No named end: fly the recorded route itself, which is what a player who never touched
                // a connector still gets out of a route book.
                var route = Recorder?.Book?.Find(routeName);
                if (route == null || !route.IsFlyable) { HoldStation("The selected route is gone."); return; }
                PriceAndFly(route, grid, State == AutoRunState.Inbound);
                return;
            }

            var src = GridWaymark.FindSource(label);
            if (src == null) { HoldStation("Waymark '" + label + "' has no block behind it any more."); return; }
            if (!src.WaymarkAcceptsTraffic) { HoldStation("Waymark '" + label + "' is switched off."); return; }

            // Re-price the remaining trip against what is in the ship *now*: cargo, damage, stored
            // energy. A leg that was affordable when the loop was armed and is not any more is the
            // exact case the reserve rule exists for, and it is far cheaper to catch here than in the
            // middle of a gravity well.
            var synthetic = new ShipRoute { routeName = "Leg to " + label };
            synthetic.waypoints.Add(new RouteWaypoint(CosmicNow(grid), null, "here"));
            var leg = new RouteWaypoint(CosmicOf(src, grid), null, label);
            leg.waymarkName = label;             // assign, do not object-init: the field is part of
            synthetic.waypoints.Add(leg);        // the point, not a decoration on a temporary
            PriceAndFly(synthetic, grid, false);
        }

        void PriceAndFly(ShipRoute route, GridEntity grid, bool reverse)
        {
            var plan = GridRoutePlanner.Evaluate(route, grid);
            if (!plan.IsValid) { HoldStation("Cannot price the leg: no star map, or nothing to fly."); return; }

            // The reserve rule. `minimumReserve01 == 0` is the armed "runs out" mode, and then the only
            // thing that stops the ship is the planner refusing outright — which is what "until it runs
            // out" promises, stated as a number rather than as a mood.
            float reserve = Mathf.Clamp01(minimumReserve01);
            if (plan.ReserveMargin01 < reserve)
            {
                HoldStation("Leg refused: " + (reserve * 100f).ToString("0") + "% reserve wanted, "
                            + (plan.ReserveMargin01 * 100f).ToString("0") + "% available.");
                return;
            }
            if (HasBlockingWarning(plan))
            {
                HoldStation("Leg refused: " + FirstWarningText(plan));
                return;
            }

            BlockReason = "";
            _holdPoint = AimPoint(route, grid, reverse);
            _holdPointValid = true;
            if (State == AutoRunState.Blocked) State = AutoRunState.Outbound;
        }

        /// <summary>The pad's service envelope, not its collider: a ship the size of a deckhouse
        /// arriving "at the connector" arrives beside it.</summary>
        Vector3 AimPoint(ShipRoute route, GridEntity grid, bool reverse)
        {
            var origin = SpaceOrigin.Instance;
            string label = reverse ? route.startWaymark : route.endWaymark;
            var src = string.IsNullOrWhiteSpace(label) ? null : GridWaymark.FindSource(label);
            if (src is IRefuelPad aim)
            {
                ServingPad = aim;
                float cs = aim is GridConnectorBlock ? (aim as GridConnectorBlock).EffectiveCellSize : 1f;
                Vector3 here = origin != null ? origin.transform.position : grid.transform.position;
                Vector3 away = (here - aim.WaymarkWorldPosition);
                away = away.sqrMagnitude > 0.01f ? away.normalized
                    : (aim is Component c ? c.transform.up : Vector3.up);
                return aim.WaymarkWorldPosition + away * (Mathf.Max(1f, holdRadiusCells) * cs);
            }
            var wps = route.waypoints;
            if (wps.Count > 0)
            {
                var wp = wps[reverse ? 0 : wps.Count - 1];
                double3 km = wp.ResolvedPositionKm(CosmicRegistry.Instance);
                // A waypoint that resolved to the origin of the cosmic frame is not a destination, it
                // is the absence of one (a waymark whose pad burned down and was never re-pinned).
                // Holding station where we are is honest; flying to (0,0,0) is how a shuttle becomes
                // a permanent fixture of another planet's mantle.
                if (origin != null && (math.abs(km.x) > 1e-6d || math.abs(km.y) > 1e-6d || math.abs(km.z) > 1e-6d))
                    return origin.GetScenePos(km);
            }
            return grid.transform.position;
        }

        // ── Per-frame steering ───────────────────────────────────────────────
        void Steer()
        {
            var grid = Grid;
            if (grid == null || !_holdPointValid) return;

            Vector3 rb = grid.Body != null ? grid.Body.position : grid.transform.position;
            Vector3 to = _holdPoint - rb;
            float dist = to.magnitude;

            float cap = maxFlightMs > 0.01f ? maxFlightMs : RouteRules.MaxPracticalCruiseMs;

            // Arrive: the classic speed limit that makes a heavy ship's approach its own. The braking
            // figure comes from the drive's real authority, so a weak drive starts slowing early and a
            // strong one does not need to — and the plan's hold speed and this number cannot disagree,
            // because both are derived from the same thrust and the same mass.
            var thrust = grid.GetThrustByDirection();
            float accel = Mathf.Max(0.05f, (thrust.fwd + thrust.back + thrust.up + thrust.down + thrust.left + thrust.right)
                                           / Mathf.Max(1f, grid.TotalMass) * 0.5f);
            float settle = Mathf.Max(0.5f, settleSpeedMs);
            float holdR = Mathf.Max(0.5f, holdRadiusCells) * (grid.gridSize.CellSize());

            if (IsHoldingForTransfer)
            {
                // At a pad: ease into the envelope and let the dampeners hold it. A shuttle that
                // noses into the queue is a shuttle that stays in the queue; one that flies through it
                // is a shuttle that has to start the approach again.
                float slow = Mathf.Min(cap, Mathf.Max(settle, dist * 0.8f));
                Vector3 wanted = dist > holdR * 1.5f ? to / Mathf.Max(0.001f, dist) * slow
                    : (dist > holdR ? to / Mathf.Max(0.001f, dist) * settle : Vector3.zero);
                grid.SetAutonomousFlight(wanted, "AUTO RUN — " + PadLabel());
                return;
            }

            if (dist <= holdR && speedOf(grid) <= settle)
            {
                // Arrived. If the waymark is a connector, ask for service; if it is a pin, the leg is
                // simply over and the loop turns around, which is the whole of what a shuttle is.
                grid.SetAutonomousFlight(Vector3.zero, "AUTO RUN — holding at " + WaymarkLabel());
                OnArrived();
                return;
            }

            float stopDistance = speedOf(grid) * speedOf(grid) / (2f * accel);
            if (dist < stopDistance + holdR)
            {
                // Braking hard enough to land inside the envelope: point the drive at where we came
                // from and let the ship's own authority do the arithmetic.
                grid.SetAutonomousFlight(-to.normalized * Mathf.Min(cap, speedOf(grid)),
                    "AUTO RUN — braking for " + WaymarkLabel());
                return;
            }

            // Approach profile: full ceiling speed in the open, then a commanded speed that decays with
            // remaining distance so the ship arrives slow instead of arriving *fast* and asking the
            // dampeners to forgive it. This is the line that decides whether a loop reads as a shuttle
            // or as a rock with intentions.
            float profile = cap * Mathf.InverseLerp(holdR * 2.5f, stopDistance + holdR * 6f, dist);
            grid.SetAutonomousFlight(to / Mathf.Max(0.001f, dist) * Mathf.Max(settle * 1.5f, profile),
                "AUTO RUN — " + WaymarkLabel());
        }

        static float speedOf(GridEntity g) => g?.Body != null ? g.Body.linearVelocity.magnitude : 0f;

        void OnArrived()
        {
            if (State != AutoRunState.Outbound && State != AutoRunState.Inbound) return;

            bool headingIn = State == AutoRunState.Inbound;
            string label = headingIn ? startWaymark : endWaymark;
            var src = string.IsNullOrWhiteSpace(label) ? null : GridWaymark.FindSource(label);

            if (src is IRefuelPad pad && (targetCharge01 > 0.01f || targetFuel01 > 0.01f || targetHydrogen01 > 0.01f))
            {
                ServingPad = pad;
                _serviceClock = 0f;
                State = headingIn ? AutoRunState.ServiceInbound : AutoRunState.ServiceOutbound;
                PushLog("At " + label + " · queue depth " + pad.QueueDepth);
                return;
            }

            // Turn around, or stop, exactly as the mode says.
            if (headingIn)
            {
                RunsCompleted++;
                if (mode == AutoRunMode.OneWayThenPark) { Disarm("One-way run finished."); return; }
                if (mode == AutoRunMode.FixedRunCount && RunsCompleted >= Mathf.Max(1, fixedRuns))
                {
                    Disarm("Fixed run count reached (" + RunsCompleted + ").");
                    return;
                }
            }
            State = headingIn ? AutoRunState.Outbound : AutoRunState.Inbound;
            PushLog(headingIn ? "Inbound leg complete · run " + RunsCompleted : "Turned around at " + (label ?? "the far end"));
        }

        void HoldStation(string reason)
        {
            if (State != AutoRunState.Blocked && !string.IsNullOrEmpty(reason)) PushLog(reason);
            BlockReason = reason ?? BlockReason;
            State = AutoRunState.Blocked;
            _holdPoint = Grid != null ? (Grid.Body != null ? Grid.Body.position : Grid.transform.position) : Vector3.zero;
            _holdPointValid = true;
            ServingPad = null;
            Grid?.SetAutonomousFlight(Vector3.zero, "AUTO RUN — holding");
        }

        // ── Numbers the loop is held to ──────────────────────────────────────
        float HurtFraction()
        {
            if (Grid == null) return 0f;
            float worst = 1f;
            foreach (var b in Grid.AllBlocks)
            {
                if (b == null || b.maxHP <= 0.01f) continue;
                float f = Mathf.Clamp01(b.currentHP / b.maxHP);
                if (f < worst) worst = f;
            }
            return 1f - worst;     // "how hurt is the worst thing on this grid"
        }

        float CargoFill01()
        {
            if (Grid == null) return 0f;
            float stored = 0f, cap = 0f;
            foreach (var b in Grid.AllBlocks)
            {
                if (b is not GridCargoContainer hold) continue;
                cap += Mathf.Max(0f, hold.maxMassKg);
                stored += Mathf.Max(0f, hold.CurrentMassKg);
            }
            // No cargo holds at all: "full" and "empty" are both true of an empty ship, so report
            // 1f and let `stopWhenCargoEmpty` be the rule that decides. Guessing here would stop a
            // tanker that has not been fitted with cargo yet.
            return cap > 0.01f ? Mathf.Clamp01(stored / cap) : 1f;
        }

        /// <summary>The same sentence the pad panel prints, from the ship's side. One implementation on
        /// purpose: a pad that reports a different number from the shuttle's own panel is a bug the
        /// player can never pin down.</summary>
        public string DeficitForPad() => DeficitLine();

        string DeficitLine()
        {
            var bits = new List<string>(3);
            if (WantsPower) bits.Add("charge " + (Charge01 * 100f).ToString("0") + "%/" + (targetCharge01 * 100f).ToString("0") + "%");
            if (WantsFuel) bits.Add("fuel " + (Fuel01 * 100f).ToString("0") + "%/" + (targetFuel01 * 100f).ToString("0") + "%");
            if (WantsHydrogen) bits.Add("H2 " + (Hydrogen01 * 100f).ToString("0") + "%/" + (targetHydrogen01 * 100f).ToString("0") + "%");
            return bits.Count == 0 ? "targets met" : string.Join(", ", bits);
        }

        string WaymarkLabel()
        {
            if (State == AutoRunState.Inbound || State == AutoRunState.ServiceInbound)
                return string.IsNullOrWhiteSpace(startWaymark) ? "the near end" : startWaymark;
            return string.IsNullOrWhiteSpace(endWaymark) ? "the far end" : endWaymark;
        }

        string PadLabel() => ServingPad != null ? ServingPad.WaymarkLabel : WaymarkLabel();

        static double3 CosmicNow(GridEntity grid)
        {
            var origin = SpaceOrigin.Instance;
            if (origin == null || grid == null) return double3.zero;
            return origin.GetCosmicKm(grid.transform.position);
        }

        static double3 CosmicOf(IWaymarkSource src, GridEntity grid)
        {
            var origin = SpaceOrigin.Instance;
            if (origin == null) return double3.zero;
            return origin.GetCosmicKm(src.WaymarkWorldPosition);
        }

        static bool HasBlockingWarning(RoutePlan plan)
        {
            if (!plan.IsValid || plan.Warnings == null) return false;
            for (int i = 0; i < plan.Warnings.Count; i++)
            {
                switch (plan.Warnings[i])
                {
                    case RouteWarningCode.NoThrust:
                    case RouteWarningCode.PowerShortfall:
                    case RouteWarningCode.ReserveShortfall:
                    case RouteWarningCode.HydrogenShortfall:
                    case RouteWarningCode.ArrivalUnsafe:
                    case RouteWarningCode.AtmosphereHazard:
                        return true;
                }
            }
            return false;
        }

        static string FirstWarningText(RoutePlan plan)
        {
            if (!plan.IsValid) return "the planner cannot price this leg";
            if (plan.Warnings == null || plan.Warnings.Count == 0) return "no reason given";
            return RoutePlan.TextFor(plan.Warnings[0]);
        }

        void PushLog(string line)
        {
            _log.Add(line);
            while (_log.Count > 6) _log.RemoveAt(0);
        }

        // ── Panel-facing one-liner ───────────────────────────────────────────
        public string StatusLine
        {
            get
            {
                string st = State switch
                {
                    AutoRunState.Halted => "HALTED",
                    AutoRunState.Paused => "PAUSED",
                    AutoRunState.Blocked => "HELD",
                    AutoRunState.Outbound => "OUTBOUND",
                    AutoRunState.Inbound => "INBOUND",
                    AutoRunState.ServiceOutbound => "AT PAD",
                    _ => "AT PAD",
                };
                string extra = IsHoldingForTransfer ? " · " + DeficitLine() : (string.IsNullOrEmpty(BlockReason) ? "" : " · " + BlockReason);
                return st + " · runs " + RunsCompleted + extra;
            }
        }
    }
}
