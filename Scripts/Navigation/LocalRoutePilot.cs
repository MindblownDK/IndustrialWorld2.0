using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;
using VoxelEngine.Maritime;
using VoxelEngine.Navigation;

namespace IndustrialWorld.Navigation
{
    /// <summary>Bounded local Water/Flight execution. Road routes use the existing wheel controller.
    /// No route automatically resumes after loading; no teleport, direct velocity write or free thrust.</summary>
    public sealed class LocalRoutePilot : MonoBehaviour
    {
        private const string FlightOwner = "LOCAL ROUTE PILOT";
        private GridEntity _grid;
        private GridRouteRecorder _owner;
        private ShipRoute _route;
        private readonly List<Collider> _hull = new List<Collider>();
        private readonly RaycastHit[] _hits = new RaycastHit[128];
        private readonly Collider[] _overlaps = new Collider[128];
        private int _index;
        private float _countdown, _safetyClock, _stalled, _bestDistance, _radius;
        private ShipRoute _holdRoute;
        private bool _holding;
        public bool IsActive { get; private set; }
        public bool IsWater => _route != null && _route.travelMode == RouteTravelMode.Water;
        public Transform Frame { get; private set; }
        public string Status { get; private set; } = "Select a route, confirm, then press START SELECTED ROUTE.";
        public bool IsControlledBy(GridRouteRecorder recorder) => IsActive && _owner == recorder;
        public static LocalRoutePilot For(GridEntity grid)
        {
            if (grid == null) return null;
            var control = grid.GetComponent<LocalRoutePilot>();
            if (control == null) control = grid.gameObject.AddComponent<LocalRoutePilot>();
            control._grid = grid;
            return control;
        }

        public bool StartRun(GridRouteRecorder recorder, ShipRoute selected)
        {
            if (IsActive) return Refuse("Stop/release this run before choosing another route.");
            if (_grid == null || recorder == null || recorder.Grid != _grid || !recorder.Enabled
                || _grid.Body == null || _grid.Body.isKinematic) return Refuse("Enabled pilot and unlocked vehicle body required.");
            if (_grid.IsControlled || (_grid.Maritime != null && _grid.Maritime.HelmActive))
                return Refuse("Exit the cockpit/helm before starting an unattended water/flight run.");
            var loop = _grid.GetComponent<GridRouteAutopilot>();
            if (_grid.WheelControlHeld || _grid.AutonomousFlightActive || (loop != null && loop.IsArmed))
                return Refuse("Release wheel control and disarm other flight/loop control first.");
            if (!_grid.HasPower || _grid.Body.linearVelocity.magnitude > 0.5f)
                return Refuse("Stop the vehicle and supply control power before starting.");
            if (selected == null || !selected.IsFlyable || selected.waypoints.Count > 4096
                || (selected.travelMode != RouteTravelMode.Water && selected.travelMode != RouteTravelMode.Flight
                    && selected.travelMode != RouteTravelMode.LegacyFlight) || !RouteCoordinates.CanResolve(selected))
                return Refuse("Select a valid Water/Flight route in the current coordinate frame.");
            foreach (var block in _grid.AllBlocks)
                if ((block is GridLandingGear gear && gear.IsLocked) || (block is GridDockingPort port && port.IsDocked))
                    return Refuse("Release landing gear/docking locks before starting local navigation.");
            Frame = recorder.transform;
            foreach (var block in _grid.AllBlocks)
                if (block is GridCockpit) { Frame = block.transform; break; }
            _radius = 0.5f;
            _hull.Clear();
            _grid.GetComponentsInChildren<Collider>(_hull);
            foreach (var collider in _hull)
                if (collider != null && collider.enabled && !collider.isTrigger)
                    _radius = Mathf.Max(_radius, Vector3.Distance(_grid.Body.worldCenterOfMass, collider.bounds.center) + collider.bounds.extents.magnitude);
            _radius += 0.5f;
            if (_radius > 20f) return Refuse("Local navigation supports vehicle envelopes up to 20 m radius.");
            // Freeze the selected list: later edits to the shelf cannot steer a live vehicle.
            _route = new ShipRoute { routeName = selected.routeName, travelMode = selected.travelMode,
                sceneCoordinates = selected.sceneCoordinates, waypoints = new List<RouteWaypoint>(selected.waypoints) };
            if (!EquipmentReady(out var reason)) return Refuse(reason);
            Vector3 previous = _grid.Body.position;
            float total = 0f;
            for (int i = 0; i < _route.waypoints.Count; i++)
            {
                if (!RouteCoordinates.TryResolve(_route, i, out var point)) return Refuse("A route anchor is missing or invalid.");
                total += Vector3.Distance(previous, point);
                if (Vector3.Distance(previous, point) > 200f || total > 2000f)
                    return Refuse("Local routes allow 200 m per leg and 2 km total. Use the legacy space planner for long flights.");
                if (!SegmentClear(previous, point, out reason, true)) return Refuse("Leg " + (i + 1) + ": " + reason);
                previous = point;
            }
            _owner = recorder;
            _index = 0;
            _holding = false;
            _countdown = 5f;
            _safetyClock = _stalled = 0f;
            _bestDistance = float.MaxValue;
            IsActive = true;
            Status = "Departure in 5 seconds. Stand clear.";
            return true;
        }

        private bool Refuse(string reason) { Status = reason; return false; }

        public void Stop(string reason = "Released by operator. Vessel may drift or fall; secure it manually.")
        {
            bool owned = IsActive;
            IsActive = false;
            _holding = false;
            if (_grid != null && owned)
            {
                if (_grid.AutonomousFlightOwner == FlightOwner) _grid.ClearAutonomousFlight();
                _grid.LocalNavigationGravityCompensation = false;
                if (_grid.Maritime != null) _grid.Maritime.ClearNavigationCommand(this);
            }
            Status = reason;
        }

        public void HoldOrStop()
        {
            if (!IsActive) return;
            if (IsWater) { Stop("Water propulsion cut. Boat coasts; moor it manually."); return; }
            _holding = true;
            SetHoldPoint(_grid.Body.position);
            _countdown = 0f;
            Status = "Flight position hold. Power and thrusters remain required.";
        }

        public void TickControl(float dt)
        {
            if (!IsActive) return;
            if (_grid == null || _owner == null || !_owner.Enabled || !_owner.isActiveAndEnabled
                || _owner.Grid != _grid || _grid.Body == null || _grid.Body.isKinematic || Frame == null)
            { Stop("Pilot removed, disabled or vehicle locked. Control released."); return; }
            if (_grid.IsControlled || (_grid.Maritime != null && _grid.Maritime.HelmActive))
            { Stop("Crew took control. Autonomy released."); return; }
            if (!_grid.HasPower || !RouteCoordinates.CanResolve(_route))
            { Stop("Power or coordinate frame unavailable. Control released."); return; }
            var loop = _grid.GetComponent<GridRouteAutopilot>();
            if (_grid.WheelControlHeld || (loop != null && loop.IsArmed)
                || (_grid.AutonomousFlightActive && _grid.AutonomousFlightOwner != FlightOwner))
            { Stop("Another controller took authority. Local run released."); return; }
            Vector3 here = _grid.Body.position;
            if (!RouteCoordinates.TryResolve(_route, _index, out var target))
            { Stop("Route anchor unavailable. Control released."); return; }
            if (_holding && !RouteCoordinates.TryResolve(_holdRoute, 0, out target))
            { Stop("Hold coordinate frame unavailable."); return; }
            Vector3 up = GravityProvider.GetUp(here);
            Vector3 delta = IsWater ? Vector3.ProjectOnPlane(target - here, up) : target - here;
            float distance = delta.magnitude;
            float arrival = IsWater ? Mathf.Max(3f, _radius * 0.5f) : 1.5f;
            if (!_holding && _countdown <= 0f && distance < arrival)
            {
                if (_index + 1 < _route.waypoints.Count)
                { _index++; _bestDistance = float.MaxValue; _stalled = 0f; return; }
                if (IsWater) { Stop("Water destination reached. Propulsion off; boat may drift. Moor manually."); return; }
                _holding = true;
                SetHoldPoint(target);
            }
            _safetyClock -= dt;
            if (_safetyClock <= 0f)
            {
                _safetyClock = 0.25f;
                if (!EquipmentReady(out var reason) || !SegmentClear(here, target, out reason, _countdown > 0f))
                {
                    // Flight brakes to a powered hold; water has no reverse/braking propeller command.
                    if (!IsWater && !_holding && EquipmentReady(out _))
                    { HoldOrStop(); Status = "Hazard: " + reason + " Flight braking to hold; explicit restart required."; }
                    else Stop("Hazard: " + reason + " Control released; secure vehicle.");
                    return;
                }
            }
            if (_countdown > 0f)
            {
                _countdown = Mathf.Max(0f, _countdown - dt);
                Status = "Departure in " + Mathf.CeilToInt(_countdown) + " seconds.";
                if (!IsWater) CommandFlight(Vector3.zero);
                else _grid.Maritime.SetNavigationCommand(this, 0f, 0f);
                return;
            }
            if (!_holding)
            {
                if (distance < _bestDistance - 0.25f) { _bestDistance = distance; _stalled = 0f; }
                else _stalled += dt;
                if (_stalled > 20f) { HoldOrStop(); Status = "No route progress for 20 s. Check propulsion, steering and route direction."; return; }
            }
            if (IsWater)
            {
                float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(Frame.forward, up), delta, up);
                float speed = Vector3.ProjectOnPlane(_grid.Body.linearVelocity, up).magnitude;
                float wanted = Mathf.Min(2f, distance * 0.15f) * Mathf.Lerp(1f, 0.3f, Mathf.Clamp01(Mathf.Abs(angle) / 80f));
                float throttle = Mathf.Clamp01((wanted - speed) * 0.7f);
                _grid.Maritime.SetNavigationCommand(this, throttle, Mathf.Clamp(angle / 35f, -1f, 1f));
            }
            else CommandFlight(Vector3.ClampMagnitude(delta * 0.5f, _holding ? 2f : Mathf.Min(4f, Mathf.Sqrt(2f * distance))));
            Status = _holding ? "Flight position hold; power and thrusters required."
                : _route.travelMode + " run: point " + (_index + 1) + "/" + _route.waypoints.Count + " · " + distance.ToString("0.0") + " m";
        }

        private void SetHoldPoint(Vector3 position)
        {
            _holdRoute = new ShipRoute { sceneCoordinates = _route.sceneCoordinates,
                travelMode = RouteTravelMode.Flight };
            _holdRoute.AddWaypoint(RouteCoordinates.Capture(position, _route.sceneCoordinates));
        }

        private void CommandFlight(Vector3 velocity)
        {
            _grid.LocalNavigationGravityCompensation = true;
            _grid.SetAutonomousFlight(velocity, FlightOwner);
        }

        private bool EquipmentReady(out string reason)
        {
            reason = "";
            if (IsWater)
            {
                if (_grid.Maritime == null) { reason = "Maritime propulsion system required."; return false; }
                foreach (var block in _grid.AllBlocks)
                    if (block != null && block.Enabled && (block is GridPropeller || block is GridElectricalPropeller)
                        && Vector3.Dot(block.transform.forward, Frame.forward) > 0.8f
                        && WaterProbeSystem.GetSubmergence(block.transform.position) > 0.2f)
                        return true;
                reason = "An enabled submerged forward-facing marine propeller is required. Align pilot/cockpit forward and supply shaft/fuel or electrical power.";
                return false;
            }
            for (int i = 0; i < 6; i++)
            {
                Vector3 direction = i < 2 ? Frame.forward : i < 4 ? Frame.right : Frame.up;
                if ((i & 1) != 0) direction = -direction;
                if (ThrustIn(direction) < _grid.Body.mass * 1.5f)
                { reason = "Local flight needs operational thrust/braking on all six axes (at least 1.5 m/s² each)."; return false; }
            }
            Vector3 gravity = _grid.WheelGravity;
            if (gravity.sqrMagnitude > 0.01f && ThrustIn(-gravity.normalized) < _grid.Body.mass * (gravity.magnitude + 1.5f))
            { reason = "Insufficient lift to counter gravity plus manoeuvring margin."; return false; }
            return true;
        }

        private float ThrustIn(Vector3 direction)
        {
            float force = 0f;
            foreach (var block in _grid.AllBlocks)
                if (block is GridThruster thruster && thruster.IsOperational)
                    force += Mathf.Max(0f, Vector3.Dot(thruster.PushDirection, direction)) * RatedThrust(thruster);
            return force;
        }

        // Never call AvailableThrust for a preflight check: it consumes hydrogen.
        public static float RatedThrust(GridThruster thruster) => Mathf.Max(0f, thruster.maxThrustN)
            * (thruster.thrusterType == ThrusterType.Atmospheric ? thruster.AtmosphericEfficiency : 1f);

        private bool SegmentClear(Vector3 from, Vector3 to, out string reason, bool departure = false)
        {
            reason = "";
            Vector3 offset = to - from;
            float distance = offset.magnitude;
            if (!RouteCoordinates.Finite(from) || !RouteCoordinates.Finite(to) || distance > 200f)
            { reason = "Waypoint is invalid or outside the local 200 m leg range."; return false; }
            if (IsWater)
            {
                int samples = Mathf.Max(1, Mathf.CeilToInt(distance / 2f));
                for (int i = 0; i <= samples; i++)
                {
                    Vector3 point = Vector3.Lerp(from, to, (float)i / samples);
                    Vector3 up = GravityProvider.GetUp(point);
                    if (WaterProbeSystem.GetSurfaceHeight(point) <= WaterProbeSystem.NoWaterHeight * 0.5f
                        || WaterProbeSystem.GetSubmergence(point - up * _radius, 0.25f) < 0.5f)
                    { reason = "Water/depth is absent or unloaded along this leg."; return false; }
                }
            }
            int overlapCount = Physics.OverlapSphereNonAlloc(from, _radius, _overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (overlapCount == _overlaps.Length) { reason = "Hull overlap query saturated."; return false; }
            for (int i = 0; i < overlapCount; i++)
                if (_overlaps[i] != null && !IgnoreCollider(_overlaps[i], departure))
                { reason = "Hull safety envelope already overlaps an obstacle."; return false; }
            if (distance < 0.05f) return true;
            int count = Physics.SphereCastNonAlloc(from, _radius, offset / distance, _hits, distance,
                ~0, QueryTriggerInteraction.Ignore);
            if (count == _hits.Length) { reason = "Obstacle query saturated."; return false; }
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i].collider;
                if (hit == null || IgnoreCollider(hit, departure)) continue;
                reason = "Hull envelope intersects terrain, another vehicle or an obstacle.";
                return false;
            }
            return true;
        }

        private bool IgnoreCollider(Collider collider, bool departure) => collider.transform.IsChildOf(_grid.transform)
            || (departure && collider.GetComponentInParent<VoxelEngine.Player.PlayerController>() != null);

        private void OnDisable() => Stop("Controller disabled; authority released.");
        private void OnDestroy() => Stop("Controller removed; authority released.");
    }
}
