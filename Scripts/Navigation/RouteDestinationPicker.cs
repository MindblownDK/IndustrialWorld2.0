using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Maritime;
using VoxelEngine.Navigation;
using VoxelEngine.UI;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IndustrialWorld.Navigation
{
    /// <summary>Crosshair world selection. Tool clicks are reserved without freezing movement or camera look.
    /// v9.56: Road destinations now snap to pavement surface and refuse off-road picks; naming respects the planner field.</summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class RouteDestinationPicker : MonoBehaviour
    {
        private static RouteDestinationPicker _active;
        private GridRouteRecorder _recorder;
        private RouteTravelMode _mode;
        private int _openedFrame, _finishFrame = -1;
        private bool _ownsToolInput;
        private float _hintClock;
        private readonly RaycastHit[] _hits = new RaycastHit[128];
        private readonly List<VoxelEngine.Building.AsphaltRoad> _roadScratch = new List<VoxelEngine.Building.AsphaltRoad>(32);

        public static void Begin(GridRouteRecorder recorder, RouteTravelMode mode)
        {
            if (_active != null) { BuildFeedbackHud.Show("DESTINATION", "A destination selection is already active."); return; }
            if (recorder == null || !recorder.Enabled || !recorder.isActiveAndEnabled || recorder.Grid == null || recorder.Book == null)
            { BuildFeedbackHud.Show("DESTINATION", "An enabled planner on a grid is required."); return; }
            if (recorder.Book.IsRecording) { BuildFeedbackHud.Show("DESTINATION", "Finish the recording first."); return; }
            var local = LocalRoutePilot.For(recorder.Grid);
            var loop = recorder.Grid.GetComponent<GridRouteAutopilot>();
            if (local.IsActive || (loop != null && loop.IsArmed)
                || (recorder.Grid.WheelAutopilot != null && recorder.Grid.WheelAutopilot.IsDriving))
            { BuildFeedbackHud.Show("DESTINATION", "Stop/release active navigation before changing its destination."); return; }
            GameUIController.Instance?.CloseAll();
            var picker = recorder.gameObject.AddComponent<RouteDestinationPicker>();
            _active = picker;
            picker._recorder = recorder;
            picker._mode = mode;
            picker._openedFrame = Time.frameCount;
            UIState.PushWorldToolBlock();
            picker._ownsToolInput = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            picker.Hint();
        }

        private void Hint()
        {
            string extra = _mode == RouteTravelMode.Road ? " Aim directly at loaded pavement (within 8 m)." : "";
            BuildFeedbackHud.Show("SELECT " + _mode.ToString().ToUpperInvariant() + " DESTINATION",
                "Move and look normally. Aim the crosshair and left-click a point within 200 m. Escape cancels. Flight empty sky: 100 m." + extra);
        }

        private void Update()
        {
            if (_recorder == null || _recorder.Grid == null || !_recorder.Enabled) { Destroy(this); return; }
            if (_finishFrame >= 0)
            {
                if (Time.frameCount <= _finishFrame) return;
                GameUIController.Instance?.OpenMachine(_recorder);
                Destroy(this);
                return;
            }
            if (Time.frameCount <= _openedFrame || UIState.IsHardPause) return;
            if (UIState.IsBlocking) { Destroy(this); return; }
            bool click, cancel;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            cancel = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            click = Input.GetMouseButtonDown(0);
            cancel = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (cancel)
            {
                UIState.PauseConsumedFrame = Time.frameCount;
                _finishFrame = Time.frameCount;
                return;
            }
            _hintClock += Time.unscaledDeltaTime;
            if (_hintClock > 3f) { _hintClock = 0f; Hint(); }
            if (!click) return;
            var camera = Camera.main;
            if (camera == null) { BuildFeedbackHud.Show("No active main camera for destination selection."); return; }
            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int count = Physics.RaycastNonAlloc(ray, _hits, 200f, ~0, QueryTriggerInteraction.Ignore);
            if (count == _hits.Length) { BuildFeedbackHud.Show("Selection query saturated; choose a clearer view."); return; }
            float closest = float.MaxValue;
            Vector3 target = Vector3.zero;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(_recorder.Grid.transform)
                    || hit.collider.GetComponentInParent<VoxelEngine.Player.PlayerController>() != null || hit.distance >= closest) continue;
                closest = hit.distance;
                target = hit.point;
                if (_mode == RouteTravelMode.Flight)
                    target += hit.normal * Mathf.Max(5f, _recorder.Grid.transform.localScale.magnitude);
                found = true;
            }
            if (_mode == RouteTravelMode.Water)
            {
                found = false;
                for (float distance = 1f; distance <= Mathf.Min(200f, closest); distance += 0.5f)
                {
                    Vector3 point = ray.GetPoint(distance);
                    if (WaterProbeSystem.GetSubmergence(point, 0.1f) < 0.5f) continue;
                    Vector3 here = _recorder.Grid.transform.position;
                    Vector3 up = GravityProvider.GetUp(point);
                    float height = WaterProbeSystem.GetSurfaceHeight(here);
                    if (GravityProvider.IsRadial) target = point + up * Mathf.Clamp(-height, -5f, 10f);
                    else target = point + up * Mathf.Clamp(here.y - height, -5f, 10f);
                    found = true;
                    break;
                }
                if (found && WaterProbeSystem.GetSurfaceHeight(target) <= WaterProbeSystem.NoWaterHeight * 0.5f) found = false;
            }
            else if (!found && _mode == RouteTravelMode.Flight) { target = ray.GetPoint(100f); found = true; }

            if (!found)
            { BuildFeedbackHud.Show("No destination", "Choose loaded road/water, or a flight point within reach."); return; }

            // Road-specific snapping: must be on/near pavement, otherwise refuse
            if (_mode == RouteTravelMode.Road)
            {
                var road = RoadRoutePlanner.FindEndpoint(target, _roadScratch);
                if (road == null)
                {
                    BuildFeedbackHud.Show("No road surface", "No available loaded road within 8 m of that point. Aim directly at pavement, check grounded tyres and road loading.");
                    return;
                }
                // Snap to surface centre or closest surface point to avoid far-away spawns
                target = RoadRoutePlanner.ClosestSurfacePoint(road, target);
                // Ensure still within 200m after snap (closest point could shift slightly)
            }

            var book = _recorder.Book;
            bool scene = SpaceOrigin.Instance == null;
            var route = new ShipRoute { travelMode = _mode, sceneCoordinates = scene };
            // Respect the name field; if empty, use a clear default. Always make unique.
            string rawName = _recorder.nextRouteName != null ? _recorder.nextRouteName.Trim() : "";
            string wanted = string.IsNullOrWhiteSpace(rawName) ? _mode + " Destination" : rawName;
            route.routeName = wanted;
            for (int n = 2; book.Find(route.routeName) != null; n++) route.routeName = wanted + " #" + n;
            Vector3 start = RoadNavigationAnchor.ForGrid(_recorder.Grid, _mode);
            if (!RouteCoordinates.Finite(target) || !RouteCoordinates.Finite(start))
            { BuildFeedbackHud.Show("Invalid coordinates", "Could not resolve world position."); return; }
            float dist = Vector3.Distance(start, target);
            if (dist < 2f || dist > 200f)
            { BuildFeedbackHud.Show("Distance out of range", $"Choose a destination between 2 and 200 m from the vehicle (currently {dist:0} m)."); return; }
            route.AddWaypoint(RouteCoordinates.Capture(start, scene));
            route.AddWaypoint(RouteCoordinates.Capture(target, scene));
            if (!book.Append(route)) { BuildFeedbackHud.Show("Could not save this route; choose a unique name."); return; }
            _recorder.SelectRoute(route.routeName);
            _recorder.nextRouteName = "";
            // Ensure overlay preview updates immediately
            RoutePathOverlay.For(book)?.RefreshPreview(route.routeName);
            BuildFeedbackHud.Show("Route saved", route.routeName + ": review in planner, then select and start it on the Auto-Run Pilot.");
            _finishFrame = Time.frameCount;
        }

        private void OnDisable()
        {
            if (_ownsToolInput) { UIState.PopWorldToolBlock(); _ownsToolInput = false; }
            if (_active == this) _active = null;
        }
    }
}
