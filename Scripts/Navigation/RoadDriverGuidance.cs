using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.GridSystem;
using VoxelEngine.Navigation;
using VoxelEngine.UI;

namespace IndustrialWorld.Navigation
{
    /// <summary>Session-only driver assistance. Never writes throttle, steering or flight state.</summary>
    public sealed class RoadDriverGuidance : MonoBehaviour
    {
        private GridRouteRecorder _recorder;
        private readonly List<AsphaltRoad> _route = new List<AsphaltRoad>();
        private VisualElement _hud;
        private Label _heading, _detail;
        private float _clock, _opacity;
        private int _cursor;
        private string _destination;
        public string Status { get; private set; } = "Choose a waymark to plan a road route.";
        public bool HasRoute => _route.Count > 0;

        public static RoadDriverGuidance For(GridRouteRecorder recorder)
        {
            var guidance = recorder.GetComponent<RoadDriverGuidance>();
            if (guidance == null) guidance = recorder.gameObject.AddComponent<RoadDriverGuidance>();
            guidance._recorder = recorder;
            return guidance;
        }

        public void Plan(string destination)
        {
            Stop();
            var source = GridWaymark.FindSource(destination);
            if (_recorder == null || _recorder.Grid == null || source == null)
            { Status = "Vehicle or destination is no longer available."; return; }
            // Capture the road endpoint now, not an unsafe chase after a moving connector.
            Vector3 from = _recorder.Grid.Body != null ? _recorder.Grid.Body.position
                : _recorder.Grid.transform.position;
            if (!RoadRoutePlanner.TryPlan(from, source.WaymarkWorldPosition, _route, out var reason))
            { Status = reason; return; }
            // One active navigator per vehicle, even when it carries multiple recorders.
            foreach (var other in _recorder.Grid.GetComponentsInChildren<RoadDriverGuidance>())
                if (other != this) other.Stop();
            _destination = destination;
            _cursor = 0;
            Status = reason;
            _clock = 0f;
        }

        public void Stop()
        {
            _route.Clear();
            _cursor = 0;
            Status = "Road guidance stopped.";
        }

        private void Update()
        {
            if (_recorder == null) { Destroy(this); return; }
            var grid = _recorder.Grid;
            bool visible = HasRoute && grid != null && grid.IsControlled && !UIState.IsBlocking;
            if (visible) EnsureHud();
            _opacity = Mathf.MoveTowards(_opacity, visible ? 1f : 0f, Time.unscaledDeltaTime / 0.18f);
            if (_hud != null) _hud.style.opacity = _opacity;
            if (!HasRoute || grid == null) return;
            _clock -= Time.unscaledDeltaTime;
            if (_clock > 0f) return;
            _clock = 0.25f;
            RefreshGuidance(grid);
        }

        private void RefreshGuidance(GridEntity grid)
        {
            for (int i = _cursor; i < _route.Count; i++)
            {
                if (!RoadRoutePlanner.IsVehicleRoad(_route[i])
                    || (i > _cursor && !RoadRoutePlanner.AreConnected(_route[i - 1], _route[i])))
                {
                    Stop();
                    Status = "Road changed or unloaded. Open the recorder and replan.";
                    BuildFeedbackHud.Show("Road guidance stopped", Status);
                    return;
                }
            }
            Vector3 here = grid.Body != null ? grid.Body.position : grid.transform.position;
            int nearest = _cursor;
            float nearestDistance = float.MaxValue;
            // Bounded local progress; guidance is not a lane-level vehicle controller.
            int end = Mathf.Min(_route.Count, _cursor + 24);
            for (int i = _cursor; i < end; i++)
            {
                Vector3 offset = here - _route[i].transform.position;
                if (Mathf.Abs(Vector3.Dot(offset, _route[i].transform.up)) > 8f) continue;
                float distance = Vector3.ProjectOnPlane(offset, _route[i].transform.up).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = i;
            }
            if (nearestDistance > 100f)
            {
                Show("OFF ROUTE", "Return to the planned road or replan from the recorder. You control the vehicle.");
                return;
            }
            _cursor = nearest;
            for (int i = _cursor; i < _route.Count; i++)
            {
                if (!RoadRoutePlanner.IsBlocked(_route[i])) continue;
                Show("ROUTE BLOCKED", "Drawbridge unavailable ahead. Stop safely; guidance resumes when clear.");
                return;
            }
            var goal = _route[_route.Count - 1];
            if (_cursor == _route.Count - 1 && Vector3.ProjectOnPlane(here - goal.transform.position, goal.transform.up).magnitude
                <= Mathf.Max(2f, goal.cellSize))
            {
                Stop();
                Status = "Road endpoint reached near " + _destination + ". Park manually.";
                BuildFeedbackHud.Show("Road endpoint reached", Status);
                return;
            }
            float remaining = Vector3.Distance(here, _route[_cursor].transform.position);
            float ahead = 0f;
            int target = _cursor;
            for (int i = _cursor + 1; i < _route.Count; i++)
            {
                float segment = Vector3.Distance(_route[i - 1].transform.position, _route[i].transform.position);
                remaining += segment;
                if (ahead < 6f) { ahead += segment; target = i; }
            }
            var control = grid.ActiveControlFrame != null ? grid.ActiveControlFrame
                : grid.ActiveCockpit != null ? grid.ActiveCockpit.transform : grid.transform;
            Vector3 up = _route[_cursor].transform.up;
            Vector3 forward = Vector3.ProjectOnPlane(control.forward, up);
            Vector3 direction = Vector3.ProjectOnPlane(_route[target].transform.position - here, up);
            float angle = Vector3.SignedAngle(forward, direction, up);
            string heading = Mathf.Abs(angle) > 120f ? "TURN BACK WHEN SAFE"
                : angle > 18f ? "BEAR RIGHT" : angle < -18f ? "BEAR LEFT" : "FOLLOW ROAD";
            Show(heading, remaining.ToString("0") + " m to road endpoint near " + _destination
                + " · Manual driving");
        }

        private void Show(string heading, string detail)
        {
            Status = heading + " — " + detail;
            if (_heading != null) _heading.text = heading;
            if (_detail != null) _detail.text = detail;
        }

        private void EnsureHud()
        {
            if (_hud != null && _hud.panel != null) return;
            var document = FindAnyObjectByType<UIDocument>();
            if (document == null || document.rootVisualElement == null) return;
            _hud?.RemoveFromHierarchy();
            _hud = new VisualElement { name = "RoadDriverGuidance", pickingMode = PickingMode.Ignore };
            _hud.style.position = Position.Absolute;
            _hud.style.left = new Length(34f, LengthUnit.Percent);
            _hud.style.right = new Length(34f, LengthUnit.Percent);
            _hud.style.top = 64f;
            _hud.style.paddingLeft = 16;
            _hud.style.paddingRight = 16;
            _hud.style.paddingTop = 10;
            _hud.style.paddingBottom = 10;
            _hud.style.backgroundColor = new Color(0.035f, 0.055f, 0.065f, 0.94f);
            _hud.style.opacity = 0;
            UITheme.Radius(_hud, 6);
            UITheme.Border(_hud, 1, UITheme.AccentCyan);
            _heading = new Label("ROAD GUIDANCE") { pickingMode = PickingMode.Ignore };
            _heading.style.color = UITheme.AccentCyan;
            _heading.style.fontSize = 14;
            _detail = new Label(Status) { pickingMode = PickingMode.Ignore };
            _detail.style.color = UITheme.TextSecondary;
            _detail.style.whiteSpace = WhiteSpace.Normal;
            _detail.style.fontSize = 11;
            _detail.style.marginTop = 5;
            _hud.Add(_heading);
            _hud.Add(_detail);
            document.rootVisualElement.Add(_hud);
        }

        private void OnDisable()
        {
            _hud?.RemoveFromHierarchy();
            _hud = null;
            _opacity = 0f;
        }

        private void OnDestroy() => OnDisable();
    }
}
