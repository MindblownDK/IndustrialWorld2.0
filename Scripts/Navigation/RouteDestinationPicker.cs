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
    /// <summary>Modal screen-to-world selection. UI blocking prevents mining/building click-through.</summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class RouteDestinationPicker : MonoBehaviour
    {
        private static RouteDestinationPicker _active;
        private GridRouteRecorder _recorder;
        private RouteTravelMode _mode;
        private int _openedFrame, _finishFrame = -1;
        private bool _blocked;
        private float _hintClock;
        private readonly RaycastHit[] _hits = new RaycastHit[128];

        public static void Begin(GridRouteRecorder recorder, RouteTravelMode mode)
        {
            if (_active != null || recorder == null || recorder.Grid == null || recorder.Book == null
                || recorder.Book.IsRecording) return;
            var local = LocalRoutePilot.For(recorder.Grid);
            if (local.IsActive || (recorder.Grid.WheelAutopilot != null && recorder.Grid.WheelAutopilot.IsDriving)) return;
            GameUIController.Instance?.CloseAll();
            var picker = recorder.gameObject.AddComponent<RouteDestinationPicker>();
            _active = picker;
            picker._recorder = recorder;
            picker._mode = mode;
            picker._openedFrame = Time.frameCount;
            UIState.PushBlock();
            picker._blocked = true;
            picker.Hint();
        }

        private void Hint() => BuildFeedbackHud.Show("SELECT " + _mode.ToString().ToUpperInvariant() + " DESTINATION",
            "Click a visible world point within 200 m. Escape cancels. Flight empty sky: 100 m. No vehicle starts until confirmed.");

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
            bool click, cancel;
            Vector2 mouse;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            cancel = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            click = Input.GetMouseButtonDown(0);
            cancel = Input.GetKeyDown(KeyCode.Escape);
            mouse = Input.mousePosition;
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
            Ray ray = camera.ScreenPointToRay(mouse);
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
                // Water need not have a Physics collider. Probe before the nearest solid obstruction.
                for (float distance = 1f; distance <= Mathf.Min(200f, closest); distance += 0.5f)
                {
                    Vector3 point = ray.GetPoint(distance);
                    if (WaterProbeSystem.GetSubmergence(point, 0.1f) < 0.5f) continue;
                    Vector3 here = _recorder.Grid.transform.position;
                    Vector3 up = GravityProvider.GetUp(point);
                    // Carry the current vessel reference height above the water to the destination.
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
            var book = _recorder.Book;
            bool scene = SpaceOrigin.Instance == null;
            var route = new ShipRoute { travelMode = _mode, sceneCoordinates = scene };
            string wanted = string.IsNullOrWhiteSpace(_recorder.nextRouteName) ? _mode + " Destination" : _recorder.nextRouteName.Trim();
            route.routeName = wanted;
            for (int n = 2; book.Find(route.routeName) != null; n++) route.routeName = wanted + " #" + n;
            Vector3 start = _recorder.Grid.transform.position;
            if (!RouteCoordinates.Finite(target) || Vector3.Distance(start, target) < 2f || Vector3.Distance(start, target) > 200f)
            { BuildFeedbackHud.Show("Choose a destination between 2 and 200 metres from the vehicle."); return; }
            route.AddWaypoint(RouteCoordinates.Capture(start, scene));
            route.AddWaypoint(RouteCoordinates.Capture(target, scene));
            if (!book.Append(route)) { BuildFeedbackHud.Show("Could not save this route; choose a unique name."); return; }
            _recorder.SelectRoute(route.routeName);
            _recorder.nextRouteName = "";
            BuildFeedbackHud.Show("Route saved", route.routeName + ": review, confirm and press START SELECTED ROUTE.");
            _finishFrame = Time.frameCount;
        }

        private void OnDisable()
        {
            if (_blocked) { UIState.PopBlock(); _blocked = false; }
            if (_active == this) _active = null;
        }
    }
}
