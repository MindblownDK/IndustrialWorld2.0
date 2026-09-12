using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Environment;
using VoxelEngine.GridSystem;
using VoxelEngine.Navigation;

namespace IndustrialWorld.Navigation
{
    /// <summary>Unattended ground runs. Network runs explicitly allow low-speed reverse legs; faults never restart.</summary>
    public sealed class RoadWheelAutopilot : MonoBehaviour
    {
        private GridEntity _grid;
        private GridRouteRecorder _recorder;
        private readonly List<AsphaltRoad> _route = new List<AsphaltRoad>();
        private readonly List<float> _remainingFrom = new List<float>();
        private readonly List<GridWheel> _wheels = new List<GridWheel>();
        private readonly HashSet<GridBlock> _blocks = new HashSet<GridBlock>();
        private readonly List<AsphaltRoad> _surfaceScratch = new List<AsphaltRoad>();
        private readonly Collider[] _overlaps = new Collider[128];
        private readonly RaycastHit[] _hits = new RaycastHit[128];
        private int _cursor;
        private int _travelDirection = 1;
        private bool _networkMode;
        private readonly List<AsphaltRoad> _networkReturn = new List<AsphaltRoad>();
        private Vector3 TravelForward => Frame.forward * _travelDirection;
        private float _countdown, _steer, _wheelbase, _maxSteer, _axleMid;
        private float _halfWidth, _halfLength, _height, _bodyTop, _stuckTime;
        private float _surfaceClock, _routeClock, _statusClock;
        private float _deceleration;
        public Transform Frame { get; private set; }
        public bool IsDriving { get; private set; }
        public bool IsControlledBy(GridRouteRecorder recorder) => IsDriving && _recorder == recorder;
        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public string Status { get; private set; } = "Wheel autopilot idle.";

        public static RoadWheelAutopilot For(GridEntity grid)
        {
            var control = grid.GetComponent<RoadWheelAutopilot>();
            if (control == null) control = grid.gameObject.AddComponent<RoadWheelAutopilot>();
            control._grid = grid;
            grid.WheelAutopilot = control;
            return control;
        }

        public bool CopyRoutePreview(List<Vector3> points)
        {
            points.Clear();
            if (!IsDriving) return false;
            foreach (var road in _route)
            {
                if (road == null) { points.Clear(); return false; }
                points.Add(RoadNavigationAnchor.SurfaceCentre(road) + road.transform.up * 1f);
            }
            return points.Count > 1;
        }

        public void StartRun(GridRouteRecorder recorder, RoadDriverGuidance guidance, bool allowReverse = false)
        {
            if (!isActiveAndEnabled || _grid == null || !_grid.isActiveAndEnabled || _grid.Body == null || recorder == null || !recorder.Enabled)
            { Status = "Enabled recorder and vehicle body required."; return; }
            if (IsDriving) { Status = "Stop the active run before changing its route."; return; }
            var localPilot = _grid.GetComponent<LocalRoutePilot>();
            if (localPilot != null && localPilot.IsActive)
            { Status = "Stop water/flight navigation before starting wheel control."; return; }
            _recorder = recorder;
            _networkMode = allowReverse; _networkReturn.Clear(); _travelDirection = 1;
            var flight = _grid.GetComponent<GridRouteAutopilot>();
            if ((flight != null && flight.IsArmed) || _grid.AutonomousFlightActive)
            { Status = "Disarm flight auto-run before starting wheel control."; return; }
            if (_grid.Body.isKinematic || _grid.Body.linearVelocity.magnitude > 0.5f)
            { Status = "Stop and unlock the vehicle before engaging."; return; }
            if (!_grid.HasPower) { Status = "Vehicle has no drive power."; return; }
            if (guidance == null || !guidance.CopyRemainingRoute(_route))
            { Status = "Plan a loaded-road route first."; return; }
            _wheels.Clear();
            _blocks.Clear();
            Frame = _grid.ActiveCockpit != null ? _grid.ActiveCockpit.transform : null;
            foreach (var block in _grid.AllBlocks)
            {
                _blocks.Add(block);
                if (Frame == null && block is GridCockpit cockpit) Frame = cockpit.transform;
                if (block is GridWheel wheel && wheel.Enabled) _wheels.Add(wheel);
                if ((block is GridLandingGear gear && gear.IsLocked)
                    || (block is GridDockingPort dock && dock.IsDocked))
                { Status = "Release landing-gear/docking locks first."; return; }
            }
            if (Frame == null && recorder is AutoRunPilot) Frame = recorder.transform;
            if (Frame == null || _wheels.Count < 4 || _wheels.Count > 16)
            { Status = "An Auto-Run Pilot or cockpit reference, and 4–16 enabled wheels are required."; return; }
            float minZ = float.MaxValue, maxZ = float.MinValue;
            _halfWidth = _halfLength = _bodyTop = 0f;
            foreach (var wheel in _wheels)
            {
                if (!wheel.IsGrounded || RoadRoutePlanner.IsBlocked(wheel.GroundRoad))
                { Status = "All wheels must be supported by available pavement."; return; }
                Vector3 local = Frame.InverseTransformPoint(wheel.transform.position);
                minZ = Mathf.Min(minZ, local.z);
                maxZ = Mathf.Max(maxZ, local.z);
            }
            _axleMid = (minZ + maxZ) * 0.5f;
            _wheelbase = maxZ - minZ;
            _maxSteer = 45f;
            bool steering = false;
            foreach (var wheel in _wheels)
                if (Frame.InverseTransformPoint(wheel.transform.position).z > _axleMid && wheel.isSteerable)
                { steering = true; _maxSteer = Mathf.Min(_maxSteer, wheel.steerAngle); }
            if (!steering || _maxSteer < 5f || _wheelbase < 1f)
            { Status = "Need a front steering axle, a rear axle, and at least 1 m wheelbase."; return; }
            // Fixed collider envelope for this run; any block topology change parks it.
            foreach (var collider in _grid.GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger || !collider.enabled) continue;
                Bounds bounds = collider.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = Frame.InverseTransformDirection(corner - _grid.Body.worldCenterOfMass);
                    _halfWidth = Mathf.Max(_halfWidth, Mathf.Abs(local.x));
                    _halfLength = Mathf.Max(_halfLength, Mathf.Abs(local.z));
                    _bodyTop = Mathf.Max(_bodyTop, local.y + 0.2f);
                }
            }
            _halfWidth = Mathf.Max(0.5f, _halfWidth) + 0.2f;
            _halfLength = Mathf.Max(_wheelbase * 0.5f, _halfLength) + 0.3f;
            if (_halfWidth > 12f || _halfLength > 16f)
            { Status = "Vehicle exceeds the first road-controller size envelope."; return; }
            if (!CachePathProgress()) return;
            if (allowReverse && !ChooseTravelDirection()) return;
            _stuckTime = _surfaceClock = _routeClock = _statusClock = 0f;
            _countdown = RoadWheelMath.StartDelay;
            Throttle = 0f; Brake = 1f;
            IsDriving = true;
            _grid.SetWheelParkingBrake(false);
            Status = "STARTING in 5 s — leave the vehicle path clear. One-way run, 4 m/s maximum.";
        }

        private bool CachePathProgress()
        {
            _cursor = 0;
            float nearest = float.MaxValue;
            _remainingFrom.Clear();
            for (int i = 0; i < _route.Count; i++)
            {
                if (!RoadRoutePlanner.IsVehicleRoad(_route[i]))
                { Status = "Planned road is no longer available. Replan first."; return false; }
                float distance = (RoadNavigationAnchor.SurfaceCentre(_route[i]) - _grid.Body.worldCenterOfMass).sqrMagnitude;
                if (distance < nearest) { nearest = distance; _cursor = i; }
                _remainingFrom.Add(0f);
            }
            for (int i = _route.Count - 2; i >= 0; i--)
                _remainingFrom[i] = _remainingFrom[i + 1]
                    + Vector3.Distance(RoadNavigationAnchor.SurfaceCentre(_route[i]), RoadNavigationAnchor.SurfaceCentre(_route[i + 1]));
            return true;
        }

        private bool ChooseTravelDirection()
        {
            int next = Mathf.Min(_route.Count - 1, _cursor + 1);
            Vector3 direction = next > _cursor ? RoadNavigationAnchor.SurfaceCentre(_route[next]) - RoadNavigationAnchor.SurfaceCentre(_route[_cursor])
                : RoadNavigationAnchor.SurfaceCentre(_route[_cursor]) - RoadNavigationAnchor.SurfaceCentre(_route[Mathf.Max(0, _cursor - 1)]);
            float angle = Mathf.Abs(Vector3.SignedAngle(Frame.forward, direction, _route[_cursor].transform.up));
            if (angle > 65f && angle < 115f) { Status = "Align the vehicle along the road before starting a network run."; return false; }
            _travelDirection = angle >= 115f ? -1 : 1;
            return true;
        }

        public void StartNetwork(GridRouteRecorder recorder, RoadDriverGuidance guidance, List<AsphaltRoad> approach, List<AsphaltRoad> across)
        {
            if (IsDriving) { Status = "Stop the active run before changing its route."; return; }
            if (guidance == null || approach == null || across == null || across.Count < 2)
            { Status = "Prepare a valid two-end network route first."; return; }
            bool approachNeeded = approach.Count >= 2;
            guidance.UseRoadPath(approachNeeded ? approach : across, "Road network");
            StartRun(recorder, guidance, true);
            if (IsDriving && approachNeeded) _networkReturn.AddRange(across);
            if (IsDriving) Status = "NETWORK: " + (_networkReturn.Count > 0 ? "approach nearer end" : "drive to other end")
                + (_travelDirection < 0 ? " in reverse" : " forward") + ". Starting in 5 seconds.";
        }

        private bool ContinueNetwork()
        {
            if (_networkReturn.Count == 0) return false;
            _route.Clear(); _route.AddRange(_networkReturn); _networkReturn.Clear();
            if (!CachePathProgress() || !ChooseTravelDirection()) { Park(Status); return true; }
            _countdown = 5f; Throttle = _steer = 0f; Brake = 1f;
            _stuckTime = _surfaceClock = _routeClock = _statusClock = 0f;
            Status = "Near end reached. Braking for 5 seconds, then driving to the other end.";
            return true;
        }

        public float SteeringFor(GridWheel wheel)
        {
            // Front-axle steering only during autonomy; authored manual settings are untouched.
            if (Frame == null || !wheel.isSteerable
                || Frame.InverseTransformPoint(wheel.transform.position).z <= _axleMid) return 0f;
            return _steer * Mathf.Min(_maxSteer, wheel.steerAngle);
        }

        public void Park(string reason)
        {
            IsDriving = false;
            _networkReturn.Clear(); _networkMode = false;
            Throttle = _steer = 0f;
            Brake = 1f;
            _grid?.SetWheelParkingBrake(true);
            Status = reason;
        }

        public void Release()
        {
            IsDriving = false;
            _networkReturn.Clear(); _networkMode = false;
            Throttle = Brake = _steer = 0f;
            _grid?.SetWheelParkingBrake(false);
            Status = "Wheel control released. Manual driving / securing the vehicle is your responsibility.";
        }

        // Called by GridEntity before power resolution, not an independently ordered FixedUpdate.
        public void TickControl(float deltaTime)
        {
            if (_grid == null) return;
            if (_grid.IsControlled && _grid.ThrustInput.sqrMagnitude > 0.01f)
            { if (IsDriving || _grid.WheelParkingBrake) Release(); return; }
            if (!IsDriving) return;
            if (_recorder == null || !_recorder.Enabled || Frame == null || _grid.Body == null || _grid.Body.isKinematic)
            { Park("Recorder/control frame unavailable. Parked."); return; }
            var flight = _grid.GetComponent<GridRouteAutopilot>();
            if (_grid.AutonomousFlightActive || (flight != null && flight.IsArmed))
            { Park("Flight control conflict. Disarm flight and explicitly re-engage."); return; }
            int count = 0;
            foreach (var block in _grid.AllBlocks)
            {
                count++;
                if (!_blocks.Contains(block)
                    || (block is GridWheel addedWheel && addedWheel.Enabled && !_wheels.Contains(addedWheel))
                    || (block is GridLandingGear gear && gear.IsLocked)
                    || (block is GridDockingPort dock && dock.IsDocked))
                { Park("Vehicle changed or locked. Recheck and re-engage."); return; }
            }
            if (count != _blocks.Count) { Park("Vehicle blocks removed. Parked."); return; }
            Vector3 contact = Vector3.zero;
            float grip = 1f, drive = 0f;
            foreach (var wheel in _wheels)
            {
                if (wheel == null || !wheel.Enabled || !wheel.IsGrounded || RoadRoutePlanner.IsBlocked(wheel.GroundRoad))
                { Park("Wheel support or pavement unavailable. Braking; explicit re-engagement required."); return; }
                contact += wheel.GroundPoint;
                grip = Mathf.Min(grip, wheel.GroundGrip);
                drive += Mathf.Max(0f, wheel.driveForce);
            }
            contact /= _wheels.Count;
            _deceleration = Mathf.Min(3f, drive / Mathf.Max(1f, _grid.Body.mass) * 0.5f) * Mathf.Max(0f, grip)
                - _grid.WheelGravity.magnitude * 0.15f; // reserve downhill authority for the supported grade
            if (_deceleration < 0.5f) { Park("Insufficient tyre braking authority for this load."); return; }
            _routeClock -= deltaTime;
            if (_routeClock <= 0f)
            {
                _routeClock = 0.5f;
                for (int i = _cursor; i < _route.Count; i++)
                {
                    if (!RoadRoutePlanner.IsVehicleRoad(_route[i])
                        || (i > _cursor && !RoadRoutePlanner.AreConnected(_route[i - 1], _route[i])))
                    { Park("Road changed or unloaded. Replan before driving."); return; }
                }
            }
            if (_route.Count == 0 || _route[_cursor] == null)
            { Park("Current road unloaded. Parked."); return; }
            Vector3 up = _route[_cursor].transform.up;
            Vector3 here = _grid.Body.worldCenterOfMass;
            // Advance only consecutively; never select a distant hairpin by nearest distance.
            while (_cursor + 1 < _route.Count)
            {
                if (_route[_cursor + 1] == null) { Park("Next road unloaded. Parked."); return; }
                Vector3 a = Vector3.ProjectOnPlane(RoadNavigationAnchor.SurfaceCentre(_route[_cursor]) - here, up);
                Vector3 b = Vector3.ProjectOnPlane(RoadNavigationAnchor.SurfaceCentre(_route[_cursor + 1]) - here, up);
                if (b.sqrMagnitude >= a.sqrMagnitude) break;
                _cursor++;
            }
            up = _route[_cursor].transform.up;
            float deviation = Vector3.ProjectOnPlane(RoadNavigationAnchor.SurfaceCentre(_route[_cursor]) - here, up).magnitude;
            if (deviation > Mathf.Max(2f, _route[_cursor].cellSize))
            { Park("Off planned road. Reposition manually, then replan."); return; }
            float speed = Vector3.ProjectOnPlane(_grid.Body.linearVelocity, up).magnitude;
            if (speed > (_networkMode ? 1.5f : RoadWheelMath.CruiseSpeed) + 1f)
            { Park("Overspeed. Braking; inspect slope, load and external forces."); return; }
            if (Vector3.Dot(_grid.Body.linearVelocity, TravelForward) < -0.5f)
            { Park("Vehicle rolling against the commanded direction. Braking."); return; }
            float horizon = RoadWheelMath.StopDistance(speed, _deceleration) + _halfLength;
            int target = _cursor;
            Vector3 segmentForward = _cursor + 1 < _route.Count
                ? RoadNavigationAnchor.SurfaceCentre(_route[_cursor + 1]) - RoadNavigationAnchor.SurfaceCentre(_route[_cursor])
                : _cursor > 0 && _route[_cursor - 1] != null
                    ? RoadNavigationAnchor.SurfaceCentre(_route[_cursor]) - RoadNavigationAnchor.SurfaceCentre(_route[_cursor - 1]) : TravelForward;
            float along = Vector3.Dot(here - RoadNavigationAnchor.SurfaceCentre(_route[_cursor]),
                Vector3.ProjectOnPlane(segmentForward, up).normalized);
            float remaining = Mathf.Max(0f, _remainingFrom[_cursor] - along), look = 0f;
            for (int i = _cursor + 1; i < _route.Count && look < Mathf.Max(4f, _wheelbase + speed); i++)
            {
                if (_route[i] == null || _route[i - 1] == null) { Park("Road unloaded. Parked."); return; }
                float length = Vector3.Distance(RoadNavigationAnchor.SurfaceCentre(_route[i - 1]), RoadNavigationAnchor.SurfaceCentre(_route[i]));
                target = i;
                look += length;
            }
            Vector3 to = RoadNavigationAnchor.SurfaceCentre(_route[target]) - here;
            if (remaining < _halfLength + 1.5f && speed < 0.3f)
            { if (!ContinueNetwork()) Park("Road endpoint reached. Parking brake applied."); return; }
            if (Mathf.Abs(Vector3.SignedAngle(TravelForward, Vector3.ProjectOnPlane(to, up), up)) > 65f)
            { Park("Route turn exceeds the steering envelope. Reposition manually."); return; }
            _steer = RoadWheelMath.Steering(TravelForward, to, up, _wheelbase, _maxSteer) * _travelDirection;
            _surfaceClock -= deltaTime;
            if (_surfaceClock <= 0f)
            {
                _surfaceClock = 0.2f;
                if (!ClearPavementAhead(up, horizon)
                    || !SweptPavementClear(here, up, Mathf.Min(horizon, Mathf.Max(0f, remaining - _halfLength - 1f))))
                { Park("Pavement too narrow, too steep, missing, or crossing unavailable ahead."); return; }
            }
            _height = Mathf.Max(1.5f, Vector3.Dot(here - contact, up) + _bodyTop - 0.35f);
            if (_countdown > 0f)
            {
                _countdown = Mathf.Max(0f, _countdown - deltaTime);
                Throttle = 0f; Brake = 1f;
                Status = "STARTING in " + Mathf.CeilToInt(_countdown) + " s — stand clear.";
                return;
            }
            if (ObstacleAhead(here - up * Vector3.Dot(here - contact, up), up, horizon))
            { Park("Obstacle or crowded safety probe. Braking; re-engage only after clearing the path."); return; }
            float targetSpeed = RoadWheelMath.SpeedLimit(remaining - _halfLength, _steer, _deceleration);
            if (_networkMode) targetSpeed = Mathf.Min(targetSpeed, 1.5f);
            Brake = speed > targetSpeed + 0.2f ? 1f : 0f;
            float requested = Brake > 0f ? 0f : Mathf.Clamp01((targetSpeed - speed) * 0.35f);
            Throttle = Mathf.MoveTowards(Throttle, requested * _travelDirection, deltaTime * 0.6f);
            if (Brake > 0f) Throttle = 0f;
            _stuckTime = Mathf.Abs(Throttle) > 0.2f && speed < 0.2f ? _stuckTime + deltaTime : 0f;
            if (_stuckTime > 5f) { Park("No progress for 5 s. Parked; inspect the vehicle."); return; }
            _statusClock -= deltaTime;
            if (_statusClock <= 0f)
            {
                _statusClock = 0.2f;
                Status = (_networkMode ? (_networkReturn.Count > 0 ? "NETWORK APPROACH" : "NETWORK END-TO-END") + (_travelDirection < 0 ? " REVERSE · " : " FORWARD · ") : "WHEEL AUTO · ") + speed.ToString("0.0") + " m/s · " + remaining.ToString("0") + " m remaining";
            }
        }

        private bool ClearPavementAhead(Vector3 up, float horizon)
        {
            float travelled = 0f;
            for (int i = _cursor; i < _route.Count; i++)
            {
                var road = _route[i];
                if (RoadRoutePlanner.IsBlocked(road)) return false;
                Vector3 a = RoadNavigationAnchor.SurfaceCentre(road);
                if (i + 1 < _route.Count && _route[i + 1] == null) return false;
                Vector3 b = i + 1 < _route.Count ? RoadNavigationAnchor.SurfaceCentre(_route[i + 1]) : a;
                Vector3 delta = b - a;
                Vector3 direction = Vector3.ProjectOnPlane(delta, up);
                if (direction.sqrMagnitude < 0.01f) direction = TravelForward;
                if (Mathf.Abs(Vector3.Dot(delta, up)) > Mathf.Max(0.15f, direction.magnitude * 0.15f)) return false;
                Vector3 right = Vector3.Cross(up, direction).normalized;
                int samples = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude));
                for (int n = 0; n <= samples; n++)
                {
                    Vector3 centre = Vector3.Lerp(a, b, n / (float)samples);
                    if (!PavedAt(centre, up) || !PavedAt(centre + right * _halfWidth, up)
                        || !PavedAt(centre - right * _halfWidth, up)) return false;
                }
                travelled += delta.magnitude;
                if (travelled >= horizon) return true;
            }
            return true;
        }

        private bool SweptPavementClear(Vector3 here, Vector3 up, float travel)
        {
            Vector3 forward = Vector3.ProjectOnPlane(TravelForward, up).normalized;
            Vector3 point = here;
            float curvature = Mathf.Tan(_steer * _maxSteer * Mathf.Deg2Rad) / _wheelbase * _travelDirection;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Min(40f, travel)));
            float step = Mathf.Min(40f, travel) / steps;
            for (int n = 0; n <= steps; n++)
            {
                Vector3 right = Vector3.Cross(up, forward).normalized;
                if (!ProjectedPavement(point, up)
                    || !ProjectedPavement(point + forward * _halfLength + right * _halfWidth, up)
                    || !ProjectedPavement(point + forward * _halfLength - right * _halfWidth, up)
                    || !ProjectedPavement(point - forward * _halfLength + right * _halfWidth, up)
                    || !ProjectedPavement(point - forward * _halfLength - right * _halfWidth, up)) return false;
                forward = Quaternion.AngleAxis(curvature * step * Mathf.Rad2Deg, up) * forward;
                point += forward * step;
            }
            return true;
        }

        private bool ProjectedPavement(Vector3 point, Vector3 up)
        {
            // Height comes from the planned road, not arbitrary pavement above/below the run.
            AsphaltRoad closest = null;
            float best = float.MaxValue;
            int end = Mathf.Min(_route.Count, _cursor + 48);
            for (int i = Mathf.Max(0, _cursor - 16); i < end; i++)
            {
                var road = _route[i];
                if (road == null) continue;
                float distance = Vector3.ProjectOnPlane(point - RoadNavigationAnchor.SurfaceCentre(road), up).sqrMagnitude;
                if (distance >= best) continue;
                closest = road;
                best = distance;
            }
            if (closest == null) return false;
            point -= up * Vector3.Dot(point - RoadNavigationAnchor.SurfaceCentre(closest), up);
            return PavedAt(point, up);
        }

        private bool PavedAt(Vector3 point, Vector3 up)
        {
            RoadSurfaceUtility.QueryNearby(point, 4f, _surfaceScratch);
            foreach (var road in _surfaceScratch)
            {
                if (RoadRoutePlanner.IsBlocked(road)) continue;
                float height = road.SurfaceOffset(point, up);
                if (!float.IsNaN(height) && Mathf.Abs(height) <= 0.6f) return true;
            }
            return false;
        }

        private bool ObstacleAhead(Vector3 contact, Vector3 up, float reach)
        {
            Vector3 forward = Vector3.ProjectOnPlane(TravelForward, up).normalized;
            Vector3 half = new Vector3(_halfWidth, _height * 0.5f, _halfLength);
            int segments = Mathf.Abs(_steer) < 0.05f ? 1 : 4;
            float step = Mathf.Min(40f, reach) / segments;
            float bend = Mathf.Tan(_steer * _maxSteer * Mathf.Deg2Rad) / _wheelbase * _travelDirection * step * Mathf.Rad2Deg;
            for (int segment = 0; segment < segments; segment++)
            {
                Vector3 centre = contact + up * (0.35f + _height * 0.5f);
                Quaternion rotation = Quaternion.LookRotation(forward, up);
                int overlap = Physics.OverlapBoxNonAlloc(centre, half, _overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
                if (overlap == _overlaps.Length) return true;
                for (int i = 0; i < overlap; i++) if (IsObstacle(_overlaps[i], contact, up)) return true;
                Vector3 direction = Quaternion.AngleAxis(bend * 0.5f, up) * forward;
                int hits = Physics.BoxCastNonAlloc(centre, half, direction, _hits, rotation,
                    step, ~0, QueryTriggerInteraction.Ignore);
                if (hits == _hits.Length) return true;
                for (int i = 0; i < hits; i++) if (IsObstacle(_hits[i].collider, contact, up)) return true;
                contact += direction * step;
                forward = Quaternion.AngleAxis(bend, up) * forward;
            }
            return false;
        }

        private bool IsObstacle(Collider collider, Vector3 ground, Vector3 up)
        {
            if (collider == null || collider.GetComponentInParent<GridEntity>() == _grid) return false;
            if (collider.GetComponentInParent<AsphaltRoad>() == null) return true;
            // Pavement below the bumper is support, not an obstacle. Never exempt an overhead
            // road or stacked slab merely because it also has an AsphaltRoad component.
            Bounds bounds = collider.bounds;
            float top = Vector3.Dot(bounds.center - ground, up)
                + Mathf.Abs(up.x) * bounds.extents.x + Mathf.Abs(up.y) * bounds.extents.y
                + Mathf.Abs(up.z) * bounds.extents.z;
            return top > 0.35f;
        }

        private void OnDisable()
        {
            if (IsDriving) Park("Wheel controller unavailable. Parking brake applied.");
        }
        private void OnDestroy() => OnDisable();
    }
}
