using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;
using VoxelEngine.Navigation;
using VoxelEngine.UI;

namespace IndustrialWorld.Navigation
{
    /// <summary>One bounded line/marker mesh per visible book, not a GameObject per waypoint.
    /// Recording visibility is independent of manual preview and the inspector's other modes.</summary>
    public sealed class RoutePathOverlay : MonoBehaviour
    {
        private static GridEntity _inspectedGrid;
        public static GridEntity InspectedGrid
        {
            get => _inspectedGrid;
            set
            {
                if (_inspectedGrid != value && value != null)
                {
                    var view = value.GetComponent<RoutePathOverlay>();
                    if (view != null) view._hiddenByUser = false;
                }
                _inspectedGrid = value;
            }
        }
        private static VisualElement _hud;
        public static void Mount(VisualElement root) => _hud = root;
        private RouteBook _book;
        private GridEntity _grid;
        private string _selected = "";
        public bool ManualVisible { get; private set; }
        public string Status { get; private set; } = "Path hidden. Use Show Path or Grid Inspector > Routes.";
        private ShipRoute _cachedRoute;
        private bool _roadDirty = true, _hiddenByUser, _invalidRoad;
        private readonly List<int> _markerIndices = new List<int>(128);
        private readonly List<AsphaltRoad> _projectionRoads = new List<AsphaltRoad>(32);
        private readonly Dictionary<Vector3, Vector3> _projectedPoints = new Dictionary<Vector3, Vector3>();
        private int _projectionBudget;
        private readonly RaycastHit[] _projectionHits = new RaycastHit[32];
        public bool PreviewVisible => !_hiddenByUser && (ManualVisible || InspectedGrid == _grid);
        private string _roadReason;
        private readonly List<AsphaltRoad> _roads = new List<AsphaltRoad>();
        private readonly List<Vector3> _points = new List<Vector3>(4097);
        private readonly List<Vector3> _vertices = new List<Vector3>(1200);
        private readonly List<int> _indices = new List<int>(4000);
        private Vector3[] _linePoints;
        private GameObject _visual;
        private LineRenderer _line;
        private Mesh _mesh;
        private Material _material;
        private float _clock, _alpha;
        private VisualElement _labels;
        private readonly List<Label> _numbers = new List<Label>(16);
        private readonly List<int> _numberIndices = new List<int>(16);
        private static readonly int[] Faces = { 0,2,3, 0,3,4, 0,4,5, 0,5,2, 1,3,2, 1,4,3, 1,5,4, 1,2,5 };

        public static RoutePathOverlay For(RouteBook book)
        {
            if (book == null) return null;
            var overlay = book.GetComponent<RoutePathOverlay>();
            if (overlay == null) overlay = book.gameObject.AddComponent<RoutePathOverlay>();
            overlay._book = book;
            overlay._grid = book.GetComponent<GridEntity>();
            return overlay;
        }

        public void TogglePreview(string routeName)
        {
            SetPreview(routeName, _selected != routeName || !PreviewVisible);
        }

        public void SetPreview(string routeName, bool visible)
        {
            ManualVisible = visible;
            _hiddenByUser = !visible;
            RefreshPreview(routeName);
            Status = _book != null && _book.IsRecording ? "Recording path stays visible until recording finishes."
                : visible ? "Showing selected path…" : "Selected path hidden (including inspector preview).";
        }

        public void RefreshPreview(string routeName)
        {
            _selected = routeName ?? "";
            _projectedPoints.Clear();
            _roadDirty = true;
            _clock = 0f;
        }

        private void LateUpdate()
        {
            if (_book == null || _grid == null) { DestroyVisuals(); return; }
            bool recording = _book.IsRecording && _book.Draft != null;
            bool wanted = recording || PreviewVisible;
            _clock -= Time.unscaledDeltaTime;
            if (wanted && _clock <= 0f)
            {
                _clock = 0.15f;
                BuildPoints(recording);
                if (_points.Count > 0 && EnsureVisuals()) RebuildGeometry();
            }
            bool visible = wanted && _points.Count > 0;
            _alpha = Mathf.MoveTowards(_alpha, visible ? 1f : 0f, Time.unscaledDeltaTime / 0.18f);
            if (_material != null)
            {
                Color color = recording || _invalidRoad ? new Color(1f, 0.65f, 0.15f, _alpha) : new Color(0.2f, 0.9f, 1f, _alpha);
                if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", color);
                if (_material.HasProperty("_Color")) _material.SetColor("_Color", color);
            }
            UpdateNumbers(recording);
            if (!wanted && _alpha <= 0f) { DestroyVisuals(); Status = "Path hidden."; }
        }

        private void BuildPoints(bool recording)
        {
            _points.Clear();
            _projectionBudget = 64;
            if (_projectedPoints.Count > 8192) _projectedPoints.Clear();
            _invalidRoad = false;
            if (!recording && _grid.WheelAutopilot != null && _grid.WheelAutopilot.IsDriving)
            {
                bool copied = _grid.WheelAutopilot.CopyRoutePreview(_points);
                Status = copied ? "ACTIVE ROAD RUN · controller pavement points · " + _points.Count
                    : "Active road route unavailable.";
                return;
            }
            var localPilot = _grid.GetComponent<LocalRoutePilot>();
            var route = recording ? _book.Draft : localPilot != null && localPilot.IsActive
                ? localPilot.ActiveRoute : _book.Find(_selected);
            if (route == null && !recording && _book.Count > 0) route = _book.Routes[0];
            if (route == null) { Status = "This grid has no route to display."; return; }
            if (!RouteCoordinates.CanResolve(route)) { Status = "Route frame unavailable; path cannot be displayed."; return; }
            if (!recording && (route.travelMode == RouteTravelMode.Road || route.travelMode == RouteTravelMode.RoadNetwork))
            {
                if (_cachedRoute != route) { _cachedRoute = route; _roadDirty = true; }
                if (_roadDirty)
                {
                    _roadDirty = false;
                    _roads.Clear();
                    _roadReason = "Missing route origin.";
                    if (route.travelMode == RouteTravelMode.RoadNetwork)
                    {
                        if (RouteCoordinates.TryResolve(route, 0, out var a) && RouteCoordinates.TryResolve(route, 1, out var b))
                            RoadRoutePlanner.TryPlan(a, b, _roads, out _roadReason);
                    }
                    else if (RouteCoordinates.TryResolve(route, 0, out var start))
                        RoadDriverGuidance.TryBuildRoute(start, route, _roads, out _roadReason);
                }
                if (_roads.Count < 2)
                {
                    _invalidRoad = true;
                    AddRawPoints(route);
                    Status = "WAYPOINT PREVIEW ONLY — road plan invalid: " + _roadReason;
                    return;
                }
                foreach (var road in _roads)
                {
                    if (!RoadRoutePlanner.IsVehicleRoad(road))
                    { _points.Clear(); Status = "Road changed or unloaded. Hide/show the path to rebuild it."; return; }
                    _points.Add(RoadNavigationAnchor.SurfaceCentre(road) + road.transform.up * 1f);
                }
            }
            else
            {
                AddRawPoints(route);
                // Show the moving recording tip even between capture samples.
                if (recording && _points.Count > 0)
                {
                    Vector3 tip = DisplayPoint(RoadNavigationAnchor.ForGrid(_grid, route.travelMode), route.travelMode);
                    if ((tip - _points[_points.Count - 1]).sqrMagnitude > 0.01f) _points.Add(tip);
                }
            }
            Status = (recording ? "RECORDING · amber path always visible" : "PREVIEW · " + route.routeName)
                + " · " + _points.Count + " points · arrows show order. Start/end markers are larger.";
        }

        private void AddRawPoints(ShipRoute route)
        {
            for (int i = 0; i < Mathf.Min(4096, route.waypoints.Count); i++)
            {
                if (!RouteCoordinates.TryResolve(route, i, out var point))
                { _points.Clear(); Status = "A saved point or body anchor cannot be resolved."; return; }
                _points.Add(DisplayPoint(point, route.travelMode));
            }
        }

        private Vector3 DisplayPoint(Vector3 point, RouteTravelMode mode)
        {
            Vector3 up = GravityProvider.GetUp(point);
            if (mode == RouteTravelMode.Road || mode == RouteTravelMode.RoadNetwork)
            {
                if (_projectedPoints.TryGetValue(point, out var cached)) return cached;
                if (_projectionBudget-- <= 0) return point + up;
                VoxelEngine.Environment.RoadSurfaceUtility.QueryNearby(point, 16f, _projectionRoads);
                float best = 64f; Vector3 surface = point; bool found = false;
                foreach (var road in _projectionRoads)
                {
                    if (!RoadRoutePlanner.IsVehicleRoad(road)) continue;
                    Vector3 candidate = RoadRoutePlanner.ClosestSurfacePoint(road, point);
                    float d = (candidate - point).sqrMagnitude;
                    if (d >= best) continue;
                    best = d; surface = candidate; found = true;
                }
                if (found) { _projectedPoints[point] = surface + up; return surface + up; }
                // Visual-only recovery for old underground/pivot-based recordings. Never changes
                // their saved coordinates or makes an invalid route pass driving preflight.
                int hits = Physics.RaycastNonAlloc(new Ray(point + up * 64f, -up), _projectionHits, 128f, ~0, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                for (int i = 0; i < hits; i++)
                {
                    var hit = _projectionHits[i];
                    if (hit.collider == null || hit.collider.GetComponentInParent<GridEntity>() != null
                        || hit.collider.GetComponentInParent<VoxelEngine.Player.PlayerController>() != null || hit.distance >= nearest) continue;
                    nearest = hit.distance; surface = hit.point; found = true;
                }
                if (found) { _projectedPoints[point] = surface + up; return surface + up; }
                _projectedPoints[point] = point + up;
            }
            return point + up;
        }

        private bool EnsureVisuals()
        {
            if (_visual != null) return true;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null) { Status = "No supported unlit shader for route visualization."; return false; }
            _material = new Material(shader) { name = "RoutePathRuntime", hideFlags = HideFlags.DontSave };
            SetMaterialFloat("_Surface", 1f);
            SetMaterialFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetMaterialFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetMaterialFloat("_ZWrite", 0f);
            SetMaterialFloat("_Cull", 0f);
            _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _material.renderQueue = 3000;
            _visual = new GameObject("RoutePathRuntime") { hideFlags = HideFlags.DontSave };
            _line = _visual.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.sharedMaterial = _material;
            _line.startWidth = _line.endWidth = 0.09f;
            _line.startColor = _line.endColor = Color.white;
            _line.numCapVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            var markers = new GameObject("RoutePointMarkers") { hideFlags = HideFlags.DontSave };
            markers.transform.SetParent(_visual.transform, false);
            _mesh = new Mesh { name = "RoutePointMarkers", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            markers.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = markers.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return true;
        }

        private void SetMaterialFloat(string property, float value)
        {
            if (_material.HasProperty(property)) _material.SetFloat(property, value);
        }

        private void RebuildGeometry()
        {
            // Runtime geometry uses resolved world positions, never the moving vehicle transform.
            _visual.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (_linePoints == null || _linePoints.Length != _points.Count) _linePoints = new Vector3[_points.Count];
            _points.CopyTo(_linePoints);
            _line.positionCount = _points.Count;
            _line.SetPositions(_linePoints);
            _vertices.Clear(); _indices.Clear();
            _markerIndices.Clear();
            float length = 0f;
            for (int i = 1; i < _points.Count; i++) length += Vector3.Distance(_points[i - 1], _points[i]);
            float spacing = Mathf.Max(12f, length / 126f), since = 0f;
            _markerIndices.Add(0);
            for (int i = 1; i < _points.Count - 1 && _markerIndices.Count < 127; i++)
            {
                since += Vector3.Distance(_points[i - 1], _points[i]);
                if (since < spacing) continue;
                _markerIndices.Add(i); since = 0f;
            }
            if (_points.Count > 1) _markerIndices.Add(_points.Count - 1);
            foreach (int i in _markerIndices)
            {
                Vector3 p = _points[i];
                Vector3 up = GravityProvider.GetUp(p);
                Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
                Vector3 right = Vector3.Cross(up, forward).normalized;
                float radius = i == 0 || i == _points.Count - 1 ? 0.45f : 0.24f;
                int n = _vertices.Count;
                _vertices.Add(p + up * radius); _vertices.Add(p - up * radius);
                _vertices.Add(p + right * radius); _vertices.Add(p + forward * radius);
                _vertices.Add(p - right * radius); _vertices.Add(p - forward * radius);
                foreach (int face in Faces) _indices.Add(n + face);
                if (i == 0) continue;
                Vector3 direction = p - _points[i - 1];
                if (direction.sqrMagnitude < 0.5f) continue;
                Vector3 centre = p - direction.normalized * 0.8f;
                direction.Normalize();
                Vector3 side = Vector3.Cross(up, direction).normalized;
                if (side.sqrMagnitude < 0.01f) side = right;
                n = _vertices.Count;
                _vertices.Add(centre + direction * 0.6f);
                _vertices.Add(centre - direction * 0.3f + side * 0.3f);
                _vertices.Add(centre - direction * 0.3f - side * 0.3f);
                _indices.Add(n); _indices.Add(n + 1); _indices.Add(n + 2);
            }
            _mesh.Clear(); _mesh.SetVertices(_vertices); _mesh.SetTriangles(_indices, 0); _mesh.RecalculateBounds();
        }

        private void UpdateNumbers(bool recording)
        {
            if (_hud == null || _hud.panel == null || _alpha <= 0f || _points.Count == 0)
            { if (_labels != null) _labels.style.display = DisplayStyle.None; return; }
            if (_labels == null || _labels.parent != _hud)
            {
                _labels?.RemoveFromHierarchy(); _numbers.Clear();
                _labels = new VisualElement { name = "RoutePointNumbers", pickingMode = PickingMode.Ignore };
                _labels.style.position = Position.Absolute;
                _labels.style.left = _labels.style.top = 0;
                _labels.style.right = _labels.style.bottom = 0;
                _hud.Add(_labels);
                for (int i = 0; i < 16; i++)
                {
                    var number = new Label { pickingMode = PickingMode.Ignore };
                    number.style.position = Position.Absolute;
                    number.style.fontSize = 11;
                    number.style.unityFontStyleAndWeight = FontStyle.Bold;
                    number.style.backgroundColor = new Color(0.025f, 0.04f, 0.06f, 0.85f);
                    number.style.paddingLeft = number.style.paddingRight = 3;
                    _labels.Add(number); _numbers.Add(number);
                }
            }
            _labels.style.display = DisplayStyle.Flex;
            _labels.style.opacity = _alpha;
            var camera = Camera.main;
            _numberIndices.Clear();
            int stride = Mathf.Max(1, Mathf.CeilToInt((_markerIndices.Count - 1) / 14f));
            for (int i = 0; i < _markerIndices.Count - 1 && _numberIndices.Count < 15; i += stride)
                _numberIndices.Add(_markerIndices[i]);
            _numberIndices.Add(_points.Count - 1);
            for (int i = 0; i < _numbers.Count; i++)
            {
                var label = _numbers[i];
                label.style.display = DisplayStyle.None;
                if (camera == null || i >= _numberIndices.Count) continue;
                int pointIndex = _numberIndices[i];
                Vector3 screen = camera.WorldToScreenPoint(_points[pointIndex]);
                if (screen.z <= 0f || screen.x < 0f || screen.y < 0f || screen.x > Screen.width || screen.y > Screen.height) continue;
                Vector2 pos = RuntimePanelUtils.ScreenToPanel(_hud.panel, new Vector2(screen.x, Screen.height - screen.y));
                label.text = pointIndex == 0 ? "START" : pointIndex == _points.Count - 1 ? recording ? "REC" : "END" : pointIndex.ToString();
                label.style.left = pos.x + 5f; label.style.top = pos.y - 18f;
                label.style.color = recording ? UITheme.AccentAmber : UITheme.AccentCyan;
                label.style.display = DisplayStyle.Flex;
            }
        }

        private void DestroyVisuals()
        {
            if (_visual != null) Destroy(_visual);
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
            _visual = null; _line = null; _mesh = null; _material = null;
            _labels?.RemoveFromHierarchy(); _labels = null; _numbers.Clear();
        }
        private void OnDisable() => DestroyVisuals();
        private void OnDestroy() => DestroyVisuals();
    }
}
