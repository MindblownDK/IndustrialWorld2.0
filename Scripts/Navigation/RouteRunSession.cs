using System;
using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Navigation;
using VoxelEngine.UI;

namespace IndustrialWorld.Navigation
{
    /// <summary>Persistent-in-session UI state and command results. A panel rebuild cannot erase
    /// a refusal or its checkbox. This component does not store or author routes.
    /// v9.56: renamed Start() to TryStartRoute() to avoid Unity Start() conflict, and uses
    /// footprint-based saved-network resolver.</summary>
    public sealed class RouteRunSession : MonoBehaviour
    {
        public string SelectedRouteName = "";
        public bool Confirmed;
        private GridEntity _grid;
        private int _attempt;
        private string _message = "Select a saved route, confirm and press Start.";
        private int _watch; // 1 wheel, 2 local, 3 legacy space controller
        private string _lastControllerStatus;

        public static RouteRunSession For(GridEntity grid)
        {
            if (grid == null) return null;
            var session = grid.GetComponent<RouteRunSession>();
            if (session == null) session = grid.gameObject.AddComponent<RouteRunSession>();
            session._grid = grid;
            return session;
        }

        public string Status
        {
            get
            {
                string live = ControllerStatus();
                if (live != null && live != _lastControllerStatus)
                { _lastControllerStatus = live; _message = "Run " + _attempt + ": " + live; }
                return _message;
            }
        }

        private string ControllerStatus()
        {
            if (_grid == null) return null;
            if (_watch == 1) return _grid.WheelAutopilot != null ? _grid.WheelAutopilot.Status : "Wheel controller unavailable.";
            if (_watch == 2) return _grid.GetComponent<LocalRoutePilot>()?.Status;
            if (_watch == 3) return _grid.GetComponent<GridRouteAutopilot>()?.StatusLine;
            return null;
        }

        private bool Report(string message, bool accepted = false)
        {
            _lastControllerStatus = ControllerStatus();
            _message = "Start attempt " + _attempt + ": " + message;
            BuildFeedbackHud.Show(accepted ? "AUTO RUN ACCEPTED" : "AUTO RUN NOT STARTED", message);
            return accepted;
        }

        // Renamed from Start() to avoid Unity's magic Start() collision (CS error: Start() cannot take parameters)
        public bool TryStartRoute(AutoRunPilot pilot)
        {
            _attempt++;
            _watch = 0;
            if (pilot == null || _grid == null || pilot.Grid != _grid || !pilot.Enabled
                || !pilot.isActiveAndEnabled || !_grid.isActiveAndEnabled)
                return Report("An enabled Auto-Run Pilot on an active vehicle is required.");
            if (!Confirmed) return Report("Tick the unattended-run confirmation first.");
            Confirmed = false;
            var book = pilot.Book;
            if (book == null) return Report("This vehicle has no route book. Use its Route Planner first.");
            if (book.IsRecording) return Report("Finish or discard the recording in the Route Planner before starting.");
            var route = book.Find(SelectedRouteName);
            if (route == null || !route.IsFlyable) return Report("Select a saved route with at least two points on this grid.");
            var local = LocalRoutePilot.For(_grid);
            var loop = _grid.GetComponent<GridRouteAutopilot>();
            if (local.IsActive || (_grid.WheelAutopilot != null && _grid.WheelAutopilot.IsDriving)
                || (loop != null && loop.IsArmed))
                return Report("A controller is already active. Stop and release/disarm it before starting again.");
            try
            {
                if (route.travelMode == RouteTravelMode.RoadNetwork)
                {
                    var approach = new System.Collections.Generic.List<VoxelEngine.Building.AsphaltRoad>();
                    var across = new System.Collections.Generic.List<VoxelEngine.Building.AsphaltRoad>();
                    if (!RouteCoordinates.TryResolve(route, 0, out var a) || !RouteCoordinates.TryResolve(route, 1, out var b))
                        return Report("Saved network anchors unavailable. Prepare a new network run.");
                    Vector3 vehiclePos = RoadNavigationAnchor.ForGrid(_grid, RouteTravelMode.Road);
                    if (!RoadNetworkRun.TryResolveSavedNetwork(vehiclePos, a, b, approach, across, out var reason))
                        return Report(reason);
                    var networkWheels = RoadWheelAutopilot.For(_grid);
                    networkWheels.StartNetwork(pilot, RoadDriverGuidance.For(pilot), approach, across);
                    _watch = 1;
                    return Report(networkWheels.Status, networkWheels.IsDriving);
                }
                if (route.travelMode == RouteTravelMode.Road)
                {
                    var guidance = RoadDriverGuidance.For(pilot);
                    if (!guidance.Plan(route)) return Report(guidance.Status);
                    var wheels = RoadWheelAutopilot.For(_grid);
                    wheels.StartRun(pilot, guidance);
                    _watch = 1;
                    return Report(wheels.Status, wheels.IsDriving);
                }
                if (route.travelMode == RouteTravelMode.LegacyFlight)
                {
                    if (!pilot.HasStarMap) return Report("This legacy space route requires a loaded star map. Create a local Flight route in the planner instead.");
                    if (_grid.WheelControlHeld || _grid.AutonomousFlightActive)
                        return Report("Release wheel/flight authority before starting the space controller.");
                    if (loop == null) loop = _grid.gameObject.AddComponent<GridRouteAutopilot>();
                    if (!loop.isActiveAndEnabled) return Report("The space controller is disabled.");
                    loop.routeName = route.routeName;
                    loop.startWaymark = loop.endWaymark = "";
                    loop.mode = AutoRunMode.OneWayThenPark;
                    bool armed = loop.Arm();
                    _watch = 3;
                    return Report(loop.StatusLine, armed);
                }
                bool started = local.StartRun(pilot, route);
                _watch = 2;
                return Report(local.Status, started);
            }
            catch (Exception error)
            {
                Debug.LogException(error, pilot);
                if (loop != null && loop.IsArmed) loop.Disarm("Start failed.");
                if (_grid.WheelAutopilot != null && _grid.WheelAutopilot.IsDriving)
                    _grid.WheelAutopilot.Park("Start failed; wheel brake applied.");
                if (local.IsActive) local.Stop("Start failed; authority released.");
                return Report("Start failed: " + error.Message + " — see the Unity Console for the stack trace.");
            }
        }

        // Backwards compat shim for any external callers that still call Start(pilot) via dynamic? 
        // We keep an obsolete wrapper that forwards, but renamed to avoid Unity magic method.
        [Obsolete("Use TryStartRoute instead. Renamed to avoid Unity Start() conflict.")]
        public bool StartRoute(AutoRunPilot pilot) => TryStartRoute(pilot);

        public void Stop(bool release)
        {
            if (_grid == null) return;
            Confirmed = false;
            var local = _grid.GetComponent<LocalRoutePilot>();
            var loop = _grid.GetComponent<GridRouteAutopilot>();
            if (loop != null && loop.IsArmed) loop.Disarm("Stopped by operator.");
            if (local != null && local.IsActive)
            {
                if (release) local.Stop(); else local.HoldOrStop();
            }
            if (_grid.WheelAutopilot != null)
            {
                if (release && _grid.WheelControlHeld) _grid.WheelAutopilot.Release();
                else if (_grid.WheelAutopilot.IsDriving) _grid.WheelAutopilot.Park("Stopped by operator. Wheel brake applied.");
            }
            _lastControllerStatus = ControllerStatus();
            _message = release ? "Released. Water can drift and airborne ships can fall; secure the vehicle."
                : "Stop requested: wheel brake / water propulsion off / local flight hold. " + (_lastControllerStatus ?? "");
            BuildFeedbackHud.Show("AUTO RUN", _message);
        }
    }
}
