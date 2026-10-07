// Assets/Scripts/VoxelEngine/UI/LogisticsMapScreen.cs
//
// THE LOGISTICS MAP (`L`) — every surface network on one sheet.
//
// Rail lines, drone routes, base zones and roads were all built separately and never
// had a shared view. This is that view. Its real job is not decoration: it is where
// the player finds out whether the networks they built actually JOIN UP — the station
// with no track, the drone port with no power, the base zone nothing serves.
//
// Deliberately a LOCAL-SURFACE map in metres, distinct from the orbital map's
// kilometre scale. They answer different questions and share no projection.
//
// Painted through `generateVisualContent` with pooled Labels in a picking-ignored
// overlay — never `MeshGenerationContext.DrawText`, which needs a paint-time font and
// is not dependable across Unity versions. That lesson came from the orbital map.
//
// 14.19.1-dev: the label pass is a SEPARATE pass (`LayoutLabels`), never run from
// inside the painter. Touching a VisualElement's text during `generateVisualContent`
// throws "VisualElements cannot change their render data under an active visual
// tree" — the paint now only paints, and every view change goes through
// `InvalidateView()` so geometry and labels update together, same frame.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Cosmos;
using VoxelEngine.Environment;
using VoxelEngine.Materials;
using VoxelEngine.Settings;
using Cursor = UnityEngine.Cursor;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class LogisticsMapScreen
    {
        private static VisualElement _root, _screen, _canvas, _sidebar, _labelLayer, _hoverCard;
        private static Label _headerLabel, _statusLabel, _hoverTitle, _hoverDetail;
        private static ScrollView _list;
        private static bool _open, _blocking, _pointerInside;
        private static Vector2 _pointerCanvas;

        // ── View ─────────────────────────────────────────────────────────────────
        private static float _zoom = 1f;
        private static Vector2 _pan;
        private static Vector3 _anchor;
        private static bool _dragging;
        private static Vector2 _dragStart, _panStart;

        /// <summary>Metres per pixel at zoom 1. Set when the map frames itself on open.</summary>
        private static float _baseScale = 0.5f;

        // Layer toggles. A map that cannot be simplified is unreadable on a mature base.
        private static bool _showRail = true;
        private static bool _showDrones = true;
        private static bool _showZones = true;
        private static bool _showRoads = true;
        private static bool _showBuildings = true;
        private static bool _showDeposits = true;
        // Pollution is telemetry, not a permanent HUD treatment: explicitly opt in.
        private static bool _showPollution;

        private static float _refreshTimer;
        private static readonly List<PollutionMapCell> _pollutionCells = new(128);
        private static readonly List<PollutionSourceReading> _sourceReadings = new(16);

        private const int TerrainRasterCells = 29;
        private static readonly List<TerrainTile> _terrainTiles = new(TerrainRasterCells * TerrainRasterCells);
        private static CelestialBody _terrainBody;
        private static Vector3 _terrainAnchor;
        private static Vector2 _terrainViewCentre;
        private static Vector2 _terrainViewportSize;
        private static float _terrainMetresPerPixel;
        private static MapFrame _projectionFrame;

        private readonly struct MapFrame
        {
            public readonly CelestialBody Body;
            public readonly SphereWorld Sphere;
            public readonly Vector3 Anchor;
            public readonly Vector3 Up;
            public readonly Vector3 East;
            public readonly Vector3 North;

            public MapFrame(CelestialBody body, SphereWorld sphere, Vector3 anchor,
                Vector3 up, Vector3 east, Vector3 north)
            {
                Body = body;
                Sphere = sphere;
                Anchor = anchor;
                Up = up;
                East = east;
                North = north;
            }
        }

        private readonly struct TerrainTile
        {
            public readonly float EastMetres;
            public readonly float NorthMetres;
            public readonly float HalfEastMetres;
            public readonly float HalfNorthMetres;
            public readonly Color Colour;
            public readonly bool Ocean;

            public TerrainTile(float eastMetres, float northMetres,
                float halfEastMetres, float halfNorthMetres, Color colour, bool ocean)
            {
                EastMetres = eastMetres;
                NorthMetres = northMetres;
                HalfEastMetres = halfEastMetres;
                HalfNorthMetres = halfNorthMetres;
                Colour = colour;
                Ocean = ocean;
            }
        }

        private static readonly Color Backdrop = new(0.030f, 0.038f, 0.052f, 0.985f);
        private static readonly Color GridInk = new(0.12f, 0.17f, 0.22f, 0.55f);
        private static readonly Color RailInk = new(0.72f, 0.76f, 0.84f, 0.95f);
        private static readonly Color DroneInk = new(0.42f, 0.74f, 0.96f, 0.80f);
        private static readonly Color RoadInk = new(0.30f, 0.32f, 0.36f, 0.85f);
        private static readonly Color BuildingInk = new(0.62f, 0.68f, 0.72f, 0.78f);
        private static readonly Color ZoneInk = new(0.95f, 0.72f, 0.28f, 0.55f);
        private static readonly Color StationInk = new(1.00f, 0.78f, 0.32f);
        private static readonly Color TrainInk = new(0.40f, 0.92f, 0.58f);
        private static readonly Color PortInk = new(0.45f, 0.80f, 1.00f);
        private static readonly Color AlertInk = new(1.00f, 0.48f, 0.34f);
        private static readonly Color PlayerInk = new(1.00f, 1.00f, 1.00f);
        private static readonly Color DepositInk = new(0.80f, 0.55f, 0.95f);
        private static readonly Color PollutionInk = new(0.88f, 0.48f, 0.20f);
        private static readonly float[] AirAlertThresholds = { 0.05f, 0.20f, 0.45f, 0.70f };
        private static readonly float[] RunoffAlertThresholds = { 0.03f, 0.18f, 0.42f, 0.70f };

        public static bool IsOpen => _open;

        // ── Mounting ─────────────────────────────────────────────────────────────
        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _screen != null && _screen.parent == uiRoot) return;
            _root = uiRoot;
            if (_screen != null) _screen.RemoveFromHierarchy();
            Build();
        }

        private static void Build()
        {
            _screen = new VisualElement { name = "LogisticsMapScreen" };
            _screen.style.position = Position.Absolute;
            _screen.style.left = 0; _screen.style.top = 0;
            _screen.style.right = 0; _screen.style.bottom = 0;
            _screen.style.flexDirection = FlexDirection.Row;
            _screen.style.backgroundColor = new StyleColor(Backdrop);
            _screen.style.display = DisplayStyle.None;
            _root.Add(_screen);

            // ── Canvas ──
            _canvas = new VisualElement { name = "LogisticsMapCanvas" };
            _canvas.style.flexGrow = 1;
            _canvas.generateVisualContent += Paint;
            _screen.Add(_canvas);

            _canvas.RegisterCallback<WheelEvent>(e =>
            {
                _zoom = Mathf.Clamp(_zoom * (e.delta.y > 0 ? 0.88f : 1.14f), 0.05f, 40f);
                InvalidateView();
                e.StopPropagation();
            });
            // Labels are positioned against the canvas rect, so a resize moves
            // every one of them - relayout on geometry just like on pan.
            _canvas.RegisterCallback<GeometryChangedEvent>(_ => InvalidateView());
            _canvas.RegisterCallback<PointerEnterEvent>(e =>
            {
                _pointerInside = true;
                _pointerCanvas = _canvas.WorldToLocal((Vector2)e.position);
                UpdateHover();
            });
            _canvas.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _pointerInside = false;
                HideHover();
            });
            _canvas.RegisterCallback<PointerDownEvent>(e =>
            {
                _dragging = true;
                _dragStart = e.position;
                _panStart = _pan;
                HideHover();
                _canvas.CapturePointer(e.pointerId);
            });
            _canvas.RegisterCallback<PointerMoveEvent>(e =>
            {
                _pointerInside = true;
                _pointerCanvas = _canvas.WorldToLocal((Vector2)e.position);
                if (_dragging)
                {
                    _pan = _panStart + ((Vector2)e.position - _dragStart);
                    InvalidateView();
                    return;
                }
                UpdateHover();
            });
            _canvas.RegisterCallback<PointerUpEvent>(e =>
            {
                _dragging = false;
                _canvas.ReleasePointer(e.pointerId);
                _pointerCanvas = _canvas.WorldToLocal((Vector2)e.position);
                UpdateHover();
            });

            _headerLabel = new Label("LOGISTICS MAP");
            _headerLabel.style.position = Position.Absolute;
            _headerLabel.style.left = 16; _headerLabel.style.top = 12;
            _headerLabel.style.fontSize = 13;
            _headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _headerLabel.style.letterSpacing = 2f;
            _headerLabel.style.color = new StyleColor(new Color(0.55f, 0.95f, 0.65f));
            _headerLabel.pickingMode = PickingMode.Ignore;
            _canvas.Add(_headerLabel);

            _statusLabel = new Label("");
            _statusLabel.style.position = Position.Absolute;
            _statusLabel.style.left = 16; _statusLabel.style.top = 30;
            _statusLabel.style.fontSize = 9;
            _statusLabel.style.letterSpacing = 1.2f;
            _statusLabel.style.color = new StyleColor(new Color(0.48f, 0.56f, 0.66f));
            _statusLabel.pickingMode = PickingMode.Ignore;
            _canvas.Add(_statusLabel);

            _labelLayer = new VisualElement { name = "LogisticsMapLabels" };
            _labelLayer.style.position = Position.Absolute;
            _labelLayer.style.left = 0; _labelLayer.style.top = 0;
            _labelLayer.style.right = 0; _labelLayer.style.bottom = 0;
            _labelLayer.pickingMode = PickingMode.Ignore;
            _canvas.Add(_labelLayer);

            var hint = new Label("HOVER TO INSPECT   ·   DRAG TO PAN   ·   SCROLL TO ZOOM   ·   L / ESC TO CLOSE");
            hint.style.position = Position.Absolute;
            hint.style.bottom = 12;
            hint.style.width = Length.Percent(100);
            hint.style.unityTextAlign = TextAnchor.LowerCenter;
            hint.style.fontSize = 8;
            hint.style.letterSpacing = 1.4f;
            hint.style.color = new StyleColor(new Color(0.38f, 0.44f, 0.55f));
            hint.pickingMode = PickingMode.Ignore;
            _canvas.Add(hint);

            _hoverCard = new VisualElement { name = "LogisticsMapHover" };
            _hoverCard.style.position = Position.Absolute;
            _hoverCard.style.width = 360;
            _hoverCard.style.paddingLeft = 11;
            _hoverCard.style.paddingRight = 11;
            _hoverCard.style.paddingTop = 8;
            _hoverCard.style.paddingBottom = 8;
            _hoverCard.style.backgroundColor = new StyleColor(new Color(0.035f, 0.045f, 0.060f, 0.96f));
            _hoverCard.style.borderLeftWidth = 3;
            _hoverCard.style.borderTopWidth = 1;
            _hoverCard.style.borderRightWidth = 1;
            _hoverCard.style.borderBottomWidth = 1;
            _hoverCard.style.borderTopColor = new StyleColor(new Color(0.24f, 0.31f, 0.38f, 0.95f));
            _hoverCard.style.borderRightColor = new StyleColor(new Color(0.24f, 0.31f, 0.38f, 0.95f));
            _hoverCard.style.borderBottomColor = new StyleColor(new Color(0.24f, 0.31f, 0.38f, 0.95f));
            _hoverCard.style.display = DisplayStyle.None;
            _hoverCard.pickingMode = PickingMode.Ignore;
            T.Radius(_hoverCard, 7);

            _hoverTitle = new Label();
            _hoverTitle.style.fontSize = 11;
            _hoverTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _hoverTitle.style.letterSpacing = 1.1f;
            _hoverTitle.pickingMode = PickingMode.Ignore;
            _hoverCard.Add(_hoverTitle);

            _hoverDetail = new Label();
            _hoverDetail.style.fontSize = 9;
            _hoverDetail.style.marginTop = 3;
            _hoverDetail.style.whiteSpace = WhiteSpace.Normal;
            _hoverDetail.style.color = new StyleColor(new Color(0.68f, 0.75f, 0.82f));
            _hoverDetail.pickingMode = PickingMode.Ignore;
            _hoverCard.Add(_hoverDetail);
            _canvas.Add(_hoverCard);

            // ── Sidebar ──
            _sidebar = new VisualElement { name = "LogisticsMapSidebar" };
            _sidebar.style.width = 300;
            _sidebar.style.flexShrink = 0;
            _sidebar.style.backgroundColor = new StyleColor(new Color(0.045f, 0.055f, 0.075f, 0.97f));
            _sidebar.style.paddingLeft = 10; _sidebar.style.paddingRight = 10;
            _sidebar.style.paddingTop = 12; _sidebar.style.paddingBottom = 12;
            _sidebar.style.borderLeftWidth = 1;
            _sidebar.style.borderLeftColor = new StyleColor(new Color(0.16f, 0.22f, 0.28f));
            _screen.Add(_sidebar);

            var layersTitle = new Label("LAYERS");
            layersTitle.style.fontSize = 10;
            layersTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            layersTitle.style.letterSpacing = 1.6f;
            layersTitle.style.color = new StyleColor(new Color(0.55f, 0.95f, 0.65f));
            layersTitle.style.marginBottom = 6;
            _sidebar.Add(layersTitle);

            AddLayerToggle("RAIL", RailInk, () => _showRail, v => _showRail = v);
            AddLayerToggle("DRONE ROUTES", DroneInk, () => _showDrones, v => _showDrones = v);
            AddLayerToggle("BASE ZONES", ZoneInk, () => _showZones, v => _showZones = v);
            AddLayerToggle("ROADS", RoadInk, () => _showRoads, v => _showRoads = v);
            AddLayerToggle("BUILDINGS", BuildingInk, () => _showBuildings, v => _showBuildings = v);
            AddLayerToggle("DEPOSITS", DepositInk, () => _showDeposits, v => _showDeposits = v);
            AddLayerToggle("POLLUTION", PollutionInk, () => _showPollution, v => _showPollution = v);

            var listTitle = new Label("NETWORK");
            listTitle.style.fontSize = 10;
            listTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            listTitle.style.letterSpacing = 1.6f;
            listTitle.style.color = new StyleColor(new Color(0.55f, 0.95f, 0.65f));
            listTitle.style.marginTop = 12;
            listTitle.style.marginBottom = 6;
            _sidebar.Add(listTitle);

            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.style.flexGrow = 1;
            _sidebar.Add(_list);
        }

        private static void AddLayerToggle(string label, Color ink,
            System.Func<bool> get, System.Action<bool> set)
        {
            var row = new Button(() => { set(!get()); RefreshLayerButtons(); Refresh(); })
            { text = label };
            row.name = "layer:" + label;
            row.style.height = 22;
            row.style.fontSize = 9;
            row.style.marginBottom = 3;
            row.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.style.paddingLeft = 8;
            row.style.color = new StyleColor(ink);
            row.userData = get;
            _sidebar.Add(row);
        }

        private static void RefreshLayerButtons()
        {
            _sidebar.Query<Button>().ForEach(b =>
            {
                if (b.userData is not System.Func<bool> get) return;
                bool on = get();
                b.style.backgroundColor = new StyleColor(on
                    ? new Color(0.14f, 0.22f, 0.28f) : new Color(0.08f, 0.09f, 0.11f));
                b.style.opacity = on ? 1f : 0.45f;
            });
        }

        // ── Open / close ─────────────────────────────────────────────────────────
        public static void Tick()
        {
            bool textInput = UIState.TextInputActive;

            if (!textInput && GameSettings.WasPressed(InputAction.LogisticsMap))
            {
                if (_open) Close();
                else Open();
            }

            if (!_open) return;

            if (!textInput && GameSettings.WasPressed(InputAction.Pause))
            {
                Close();
                UIState.PauseConsumedFrame = Time.frameCount;
                return;
            }

            // Slow refresh: gathering walks every network, and nothing on a logistics map
            // changes fast enough to need it per frame.
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 1f;
                Refresh();
            }
        }

        private static void Open()
        {
            if (_screen == null) return;
            _open = true;
            _screen.style.display = DisplayStyle.Flex;
            if (!_blocking) { UIState.PushBlock(); _blocking = true; }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _pan = Vector2.zero;
            Refresh();
            FrameAll();
            RefreshLayerButtons();
            InvalidateView();   // FrameAll changed the scale after Refresh painted
        }

        public static void Close()
        {
            if (!_open) return;
            _open = false;
            _pointerInside = false;
            HideHover();
            if (_screen != null) _screen.style.display = DisplayStyle.None;
            if (_blocking) { UIState.PopBlock(); _blocking = false; }
        }

        private static Vector3 ViewerPosition()
        {
            var cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        private static void Refresh()
        {
            LogisticsMapData.Rebuild(ViewerPosition());
            // Keep sparse cells available for water hover inspection even when the painted
            // pollution layer is off; nothing is rendered or shown permanently.
            PollutionService.GetActiveBodyMapCells(_pollutionCells);
            BuildList();

            string pollution = string.Empty;
            bool environmentAlert = false;
            if (_showPollution)
            {
                PollutionTelemetry telemetry = PollutionService.TelemetryAt(ViewerPosition());
                EcologyReading ecology = EcologyPressure.Sample(ViewerPosition());
                pollution = $"   ·   AIR {telemetry.Band} {telemetry.LocalAir01 * 100f:0}%" +
                    $"   ·   SOIL {telemetry.RunoffBand} {telemetry.LocalRunoff01 * 100f:0}%" +
                    (ecology.Status != "NO BIOSPHERE" ? $"   ·   ECOLOGY {ecology.Status}" : string.Empty);
                environmentAlert = HasEnvironmentAlert(telemetry);
            }
            _statusLabel.text =
                $"{LogisticsMapData.RailCells} RAIL CELLS   ·   {LogisticsMapData.StationCount} STATIONS   ·   " +
                $"{LogisticsMapData.TrainCount} TRAINS   ·   {LogisticsMapData.PortCount} PORTS   ·   " +
                $"{LogisticsMapData.BuildingCount} BUILDINGS   ·   {LogisticsMapData.ZoneCount} BASES   ·   " +
                $"{LogisticsMapData.DepositCount} DEPOSITS" + pollution
                + (environmentAlert ? "   ·   ENVIRONMENT ALERT" : string.Empty);

            InvalidateView();
        }

        /// <summary>Repaints the canvas AND re-places the pooled labels. One
        /// entry point, because the two must never drift apart - and because
        /// labels may not be touched from inside the painter.</summary>
        private static void InvalidateView()
        {
            if (_canvas == null) return;
            _canvas.MarkDirtyRepaint();
            LayoutLabels();
            if (!_dragging) UpdateHover();
        }

        /// <summary>Fits the whole network in view, so the map never opens on empty space.</summary>
        private static void FrameAll()
        {
            var bounds = LogisticsMapData.Extent;
            Vector3 viewer = ViewerPosition();
            _anchor = bounds.size.sqrMagnitude > 0.001f ? bounds.center : viewer;

            // A cartesian bounds centre sits slightly inside a curved world. Lift it back to
            // the viewer's surface radius so the tangent basis and analytic terrain agree.
            CelestialBody body = GravityProvider.ActiveBody;
            if (body != null)
            {
                Vector3 centre = body.transform.position;
                Vector3 direction = _anchor - centre;
                if (direction.sqrMagnitude < 0.0001f) direction = viewer - centre;
                float radius = Mathf.Max(100f, Vector3.Distance(viewer, centre));
                _anchor = centre + direction.normalized * radius;
            }

            MapFrame frame = BuildMapFrame(_anchor);
            float minEast = 0f, maxEast = 0f, minNorth = 0f, maxNorth = 0f;
            void Include(Vector3 world)
            {
                Vector3 delta = world - frame.Anchor;
                float east = Vector3.Dot(delta, frame.East);
                float north = Vector3.Dot(delta, frame.North);
                minEast = Mathf.Min(minEast, east);
                maxEast = Mathf.Max(maxEast, east);
                minNorth = Mathf.Min(minNorth, north);
                maxNorth = Mathf.Max(maxNorth, north);
            }

            var markers = LogisticsMapData.Markers;
            for (int i = 0; i < markers.Count; i++) Include(markers[i].World);
            var links = LogisticsMapData.Links;
            for (int i = 0; i < links.Count; i++)
            {
                Include(links[i].A);
                Include(links[i].B);
            }
            var buildings = LogisticsMapData.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                Include(buildings[i].World + frame.East * buildings[i].RadiusMetres);
                Include(buildings[i].World - frame.East * buildings[i].RadiusMetres);
                Include(buildings[i].World + frame.North * buildings[i].RadiusMetres);
                Include(buildings[i].World - frame.North * buildings[i].RadiusMetres);
            }

            Rect r = _canvas.contentRect;
            float w = r.width > 10 ? r.width : 900f;
            float h = r.height > 10 ? r.height : 700f;
            float spanEast = Mathf.Max(16f, maxEast - minEast);
            float spanNorth = Mathf.Max(16f, maxNorth - minNorth);

            // Metres per pixel needed to fit each tangent axis, with a margin so markers
            // near the edge are not clipped by their own labels.
            float need = Mathf.Max(spanEast / (w * 0.82f), spanNorth / (h * 0.82f));
            _baseScale = Mathf.Max(0.02f, need);
            _zoom = 1f;
        }

        // ── Sidebar list ─────────────────────────────────────────────────────────
        private static void BuildList()
        {
            _list.Clear();

            var markers = LogisticsMapData.Markers;

            // Problems first. On a mature base this list is long, and the entries that
            // need the player are the only reason to read it.
            bool anyAlert = false;
            for (int i = 0; i < markers.Count; i++)
            {
                if (!markers[i].Alert) continue;
                if (!anyAlert) { AddSection("NEEDS ATTENTION"); anyAlert = true; }
                _list.Add(BuildRow(markers[i]));
            }

            if (_showPollution)
            {
                PollutionTelemetry telemetry = PollutionService.TelemetryAt(ViewerPosition());
                AddEnvironmentAlerts(telemetry);
                AddSection("AIR QUALITY");
                var air = new Label(
                    $"{telemetry.Band}   ·   LOCAL {telemetry.LocalAir01 * 100f:0}%   ·   " +
                    $"BODY {telemetry.BodyAir01 * 100f:0.0}%");
                air.style.fontSize = 10;
                air.style.color = new StyleColor(PollutionColour(telemetry.LocalAir01));
                air.style.marginBottom = 2;
                _list.Add(air);
                string trend = Mathf.Abs(telemetry.TrendPerMinute) < 0.005f ? "STABLE"
                    : telemetry.TrendPerMinute > 0f ? "RISING" : "RECOVERING";
                var detail = new Label($"{trend}   ·   {telemetry.ActiveCells} ACTIVE CELLS");
                detail.style.fontSize = 9;
                detail.style.color = new StyleColor(T.TextMuted);
                detail.style.marginBottom = 2;
                _list.Add(detail);

                AddSection("SOIL / WATER");
                var runoff = new Label(
                    $"{telemetry.RunoffBand}   ·   LOCAL {telemetry.LocalRunoff01 * 100f:0}%   ·   " +
                    $"BODY {telemetry.BodyRunoff01 * 100f:0.0}%");
                runoff.style.fontSize = 10;
                runoff.style.color = new StyleColor(RunoffColour(telemetry.LocalRunoff01));
                runoff.style.marginBottom = 2;
                _list.Add(runoff);
                string runoffTrend = Mathf.Abs(telemetry.RunoffTrendPerMinute) < 0.005f ? "STABLE"
                    : telemetry.RunoffTrendPerMinute > 0f ? "SPREADING" : "RECOVERING";
                var runoffDetail = new Label(runoffTrend + "   ·   RAIN TRANSFERS SMOG TO RUNOFF");
                runoffDetail.style.fontSize = 9;
                runoffDetail.style.whiteSpace = WhiteSpace.Normal;
                runoffDetail.style.color = new StyleColor(T.TextMuted);
                runoffDetail.style.marginBottom = 2;
                _list.Add(runoffDetail);

                EcologyReading ecology = EcologyPressure.Sample(ViewerPosition());
                if (ecology.Status != "NO BIOSPHERE")
                {
                    AddSection("ECOLOGY");
                    string state = ecology.SupportsNativeEcology
                        ? $"{ecology.Status}   ·   VITALITY {ecology.Vitality01 * 100f:0}%"
                        : "BARREN   ·   NO NATIVE BIOSPHERE";
                    var ecologyState = new Label(state);
                    ecologyState.style.fontSize = 10;
                    ecologyState.style.color = new StyleColor(ecology.SupportsNativeEcology
                        ? EcologyColour(ecology.Pressure01) : T.TextMuted);
                    ecologyState.style.marginBottom = 2;
                    _list.Add(ecologyState);
                    string wildlife = ecology.SupportsNativeEcology
                        ? $"WILDLIFE ACTIVITY {ecology.PassiveActivity01 * 100f:0}%"
                        : "CONVENTIONAL LIVESTOCK UNSUITABLE";
                    var ecologyDetail = new Label(
                        wildlife + $"   ·   HOSTILE PRESSURE {ecology.HostilePressureMultiplier:0.00}x");
                    ecologyDetail.style.fontSize = 9;
                    ecologyDetail.style.whiteSpace = WhiteSpace.Normal;
                    ecologyDetail.style.color = new StyleColor(T.TextMuted);
                    ecologyDetail.style.marginBottom = 2;
                    _list.Add(ecologyDetail);
                }
            }

            AddSection("RAIL");
            bool anyRail = false;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m.Alert) continue;
                if (m.Kind != MapOverlayKind.RailStation && m.Kind != MapOverlayKind.Train) continue;
                _list.Add(BuildRow(m));
                anyRail = true;
            }
            if (!anyRail) AddEmpty("No rail network.");

            AddSection("DRONE PORTS");
            bool anyPort = false;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m.Alert || m.Kind != MapOverlayKind.DronePort) continue;
                _list.Add(BuildRow(m));
                anyPort = true;
            }
            if (!anyPort) AddEmpty("No drone ports.");

            AddSection("SURVEYED DEPOSITS");
            bool anyDeposit = false;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m.Kind != MapOverlayKind.Deposit) continue;
                _list.Add(BuildRow(m));
                anyDeposit = true;
            }
            if (!anyDeposit)
                AddEmpty("No orbital resource scanner in service.");

            var zones = LogisticsMapData.Zones;
            AddSection("BASE ZONES");
            if (zones.Count == 0) AddEmpty("No logistic chest clusters.");
            for (int i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                var row = new Label($"{zone.Label}   ·   {zone.Members} chests   ·   {zone.Radius:0} m");
                row.style.fontSize = 10;
                row.style.color = new StyleColor(ZoneInk);
                row.style.marginBottom = 2;
                _list.Add(row);
            }
        }

        private static bool HasEnvironmentAlert(PollutionTelemetry telemetry)
        {
            EcologyReading ecology = EcologyPressure.Sample(ViewerPosition());
            if (ecology.Status != "NO BIOSPHERE" && ecology.Pressure01 >= 0.50f) return true;
            if (telemetry.LocalAir01 >= 0.45f || telemetry.LocalRunoff01 >= 0.42f
                || telemetry.BodyAir01 >= 0.45f || telemetry.BodyRunoff01 >= 0.42f) return true;
            return TryForecastBand(telemetry.BodyAir01, telemetry.TrendPerMinute, air: true,
                    out _, out _)
                || TryForecastBand(telemetry.BodyRunoff01, telemetry.RunoffTrendPerMinute, air: false,
                    out _, out _);
        }

        private static void AddEnvironmentAlerts(PollutionTelemetry telemetry)
        {
            bool severeAir = telemetry.LocalAir01 >= 0.45f;
            bool severeRunoff = telemetry.LocalRunoff01 >= 0.42f;
            bool severeBodyAir = telemetry.BodyAir01 >= 0.45f;
            bool severeBodyRunoff = telemetry.BodyRunoff01 >= 0.42f;
            bool airForecast = TryForecastBand(telemetry.BodyAir01, telemetry.TrendPerMinute,
                air: true, out string nextAir, out string airEta);
            bool runoffForecast = TryForecastBand(telemetry.BodyRunoff01,
                telemetry.RunoffTrendPerMinute, air: false, out string nextRunoff, out string runoffEta);
            EcologyReading ecology = EcologyPressure.Sample(ViewerPosition());
            bool ecologicalDecline = ecology.SupportsNativeEcology && ecology.Pressure01 >= 0.50f;
            bool hostileEscalation = !ecology.SupportsNativeEcology
                && ecology.Status != "NO BIOSPHERE" && ecology.Pressure01 >= 0.50f;
            if (!severeAir && !severeRunoff && !severeBodyAir && !severeBodyRunoff
                && !airForecast && !runoffForecast && !ecologicalDecline && !hostileEscalation) return;

            AddSection("ENVIRONMENT ALERTS");
            if (ecologicalDecline)
                AddEnvironmentAlert($"ECOLOGY · {ecology.Status}",
                    "Vegetation and passive wildlife are declining; remove local air and runoff sources.",
                    EcologyColour(ecology.Pressure01));
            else if (hostileEscalation)
                AddEnvironmentAlert("POLLUTION-DRIVEN HOSTILES",
                    $"Local hostile pressure is {ecology.HostilePressureMultiplier:0.00}x; remove nearby air and runoff sources.",
                    EcologyColour(ecology.Pressure01));
            if (severeAir)
                AddEnvironmentAlert($"{AirBand(telemetry.LocalAir01)} AIR AT VIEWER",
                    "Power an Atmospheric Carbon Harvester near the orange/red cells.",
                    PollutionColour(telemetry.LocalAir01));
            if (severeRunoff)
                AddEnvironmentAlert($"{RunoffBand(telemetry.LocalRunoff01)} RUNOFF AT VIEWER",
                    "Remediate brown/purple cells; contaminated water pumps lose throughput.",
                    RunoffColour(telemetry.LocalRunoff01));
            if (severeBodyAir && !severeAir)
                AddEnvironmentAlert($"BODY AIR · {AirBand(telemetry.BodyAir01)}",
                    "A major plume is active elsewhere; inspect orange/red map cells.",
                    PollutionColour(telemetry.BodyAir01));
            if (severeBodyRunoff && !severeRunoff)
                AddEnvironmentAlert($"BODY RUNOFF · {RunoffBand(telemetry.BodyRunoff01)}",
                    "Persistent contamination is active elsewhere; inspect brown/purple cells.",
                    RunoffColour(telemetry.BodyRunoff01));
            if (airForecast)
                AddEnvironmentAlert("BODY AIR RISING",
                    $"Projected to reach {nextAir} in {airEta} at the current trend.", T.AccentAmber);
            if (runoffForecast)
                AddEnvironmentAlert("BODY RUNOFF SPREADING",
                    $"Projected to reach {nextRunoff} in {runoffEta} at the current trend.", T.AccentAmber);
        }

        private static bool TryForecastBand(float current, float trendPerMinute, bool air,
            out string nextBand, out string eta)
        {
            nextBand = string.Empty;
            eta = string.Empty;
            if (trendPerMinute <= 0.00005f) return false;
            float[] thresholds = air ? AirAlertThresholds : RunoffAlertThresholds;
            float next = -1f;
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (thresholds[i] <= current + 0.0001f) continue;
                next = thresholds[i];
                break;
            }
            if (next < 0f) return false;
            float minutes = (next - current) / trendPerMinute;
            if (minutes <= 0f || minutes > 120f) return false;
            nextBand = air ? AirBand(next) : RunoffBand(next);
            eta = minutes < 1f ? "under 1 min" : $"about {Mathf.CeilToInt(minutes)} min";
            return true;
        }

        private static void AddEnvironmentAlert(string title, string detail, Color colour)
        {
            var card = new VisualElement();
            card.style.marginBottom = 4;
            card.style.paddingLeft = 8;
            card.style.paddingRight = 8;
            card.style.paddingTop = 5;
            card.style.paddingBottom = 5;
            card.style.backgroundColor = new StyleColor(new Color(colour.r, colour.g, colour.b, 0.10f));
            card.style.borderLeftWidth = 2;
            card.style.borderLeftColor = new StyleColor(colour);
            T.Radius(card, 4);

            var heading = new Label(title);
            heading.style.fontSize = 9;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.letterSpacing = 0.8f;
            heading.style.color = new StyleColor(colour);
            card.Add(heading);

            var body = new Label(detail);
            body.style.fontSize = 9;
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.color = new StyleColor(T.TextMuted);
            card.Add(body);
            _list.Add(card);
        }

        private static void AddSection(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 9;
            label.style.letterSpacing = 1.4f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(new Color(0.42f, 0.50f, 0.60f));
            label.style.marginTop = 8;
            label.style.marginBottom = 3;
            _list.Add(label);
        }

        private static void AddEmpty(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 9;
            label.style.color = new StyleColor(new Color(0.34f, 0.38f, 0.44f));
            label.style.marginBottom = 2;
            _list.Add(label);
        }

        private static VisualElement BuildRow(MapMarker marker)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;

            var dot = new Label("\u25cf");
            dot.style.fontSize = 10;
            dot.style.width = 14;
            dot.style.color = new StyleColor(marker.Alert ? AlertInk : InkFor(marker.Kind));
            row.Add(dot);

            var name = new Label(marker.Label);
            name.style.flexGrow = 1;
            name.style.fontSize = 10;
            name.style.color = new StyleColor(marker.Alert ? AlertInk : Color.white);
            row.Add(name);

            var detail = new Label(marker.Detail);
            detail.style.fontSize = 9;
            detail.style.color = new StyleColor(marker.Alert ? AlertInk : T.TextMuted);
            row.Add(detail);

            // Clicking a contact centres it: on a large network, finding a named station
            // by dragging is hopeless.
            row.RegisterCallback<PointerDownEvent>(_ =>
            {
                _anchor = marker.World;
                _pan = Vector2.zero;
                InvalidateView();
            });

            return row;
        }

        private static Color InkFor(MapOverlayKind kind) => kind switch
        {
            MapOverlayKind.RailStation => StationInk,
            MapOverlayKind.Train => TrainInk,
            MapOverlayKind.DronePort => PortInk,
            MapOverlayKind.BaseZone => ZoneInk,
            MapOverlayKind.Player => PlayerInk,
            MapOverlayKind.Deposit => DepositInk,
            _ => RailInk,
        };

        // ── Painting ─────────────────────────────────────────────────────────────
        private static MapFrame BuildMapFrame(Vector3 anchor)
        {
            CelestialBody body = GravityProvider.ActiveBody;
            if (body == null)
                return new MapFrame(null, null, anchor, Vector3.up, Vector3.right, Vector3.forward);

            Vector3 up = anchor - body.transform.position;
            if (up.sqrMagnitude < 0.0001f) up = body.transform.up;
            up.Normalize();

            // Project the body's authored forward axis into the local tangent plane. This
            // gives every surface map a stable north rather than tying it to global X/Z.
            Vector3 north = Vector3.ProjectOnPlane(body.transform.forward, up);
            if (north.sqrMagnitude < 0.0001f)
                north = Vector3.ProjectOnPlane(body.transform.up, up);
            if (north.sqrMagnitude < 0.0001f)
                north = Vector3.ProjectOnPlane(body.transform.right, up);
            north.Normalize();
            Vector3 east = Vector3.Cross(up, north).normalized;
            north = Vector3.Cross(east, up).normalized;

            SphereWorld sphere = VoxelEngine.Core.ActiveWorld.Current as SphereWorld;
            if (sphere != null && sphere.body != body) sphere = null;
            return new MapFrame(body, sphere, anchor, up, east, north);
        }

        private static Vector2 Project(Vector3 world, Vector2 centre,
            float metresPerPixel, in MapFrame frame)
        {
            Vector3 delta = world - frame.Anchor;
            float east = Vector3.Dot(delta, frame.East) / metresPerPixel;
            float north = Vector3.Dot(delta, frame.North) / metresPerPixel;
            return new Vector2(centre.x + east, centre.y - north);
        }

        private static Vector2 Project(Vector3 world, Vector2 centre, float metresPerPixel)
            => Project(world, centre, metresPerPixel, _projectionFrame);

        // ── Hover inspection ─────────────────────────────────────────────────────
        private static void UpdateHover()
        {
            if (!_open || !_pointerInside || _dragging || _canvas == null || _hoverCard == null)
            {
                HideHover();
                return;
            }

            Rect r = _canvas.contentRect;
            if (r.width < 10f || r.height < 10f || !r.Contains(_pointerCanvas))
            {
                HideHover();
                return;
            }

            Vector2 centre = new(r.width * 0.5f + _pan.x, r.height * 0.5f + _pan.y);
            float metresPerPixel = _baseScale / Mathf.Max(0.0001f, _zoom);
            _projectionFrame = BuildMapFrame(_anchor);

            if (_showBuildings && TryHoveredBuilding(_pointerCanvas, centre, metresPerPixel,
                out MapFootprint building))
            {
                string team = string.IsNullOrEmpty(building.OwnerId)
                    ? "UNKNOWN (LEGACY / UNOWNED)"
                    : string.IsNullOrEmpty(building.TeamName) ? "NO TEAM" : building.TeamName;
                string detail = "TEAM  ·  " + team;
                var owner = !string.IsNullOrEmpty(building.OwnerId)
                    ? VoxelEngine.Networking.NetworkSession.GetPlayer(building.OwnerId)
                    : null;
                if (owner != null && !string.IsNullOrWhiteSpace(owner.displayName))
                    detail += "\nOWNER  ·  " + owner.displayName;
                ShowHover(building.Name, detail, building.Ink, r);
                return;
            }

            EnsureTerrainRaster(r, metresPerPixel, _projectionFrame);
            bool overWater = IsOceanAt(_pointerCanvas, centre, metresPerPixel);
            bool hasPollution = TryHoveredPollution(_pointerCanvas, centre, metresPerPixel,
                out PollutionMapCell pollution);

            if (overWater)
            {
                float runoff = hasPollution ? pollution.Runoff01 : 0f;
                string detail = $"{runoff * 100f:0.0}% POLLUTED";
                if (hasPollution && pollution.RunoffUnits > 0f)
                    detail += "  ·  " + PollutionUnits.FormatContaminantMass(pollution.RunoffUnits);
                if (_showPollution && hasPollution && pollution.Intensity01 >= 0.01f)
                    detail += $"\nAIR ABOVE  ·  {AirBand(pollution.Intensity01)}  ·  "
                        + $"{pollution.Intensity01 * 100f:0.0}%  ·  "
                        + PollutionUnits.FormatMass(pollution.AirborneUnits);
                if (hasPollution && runoff >= 0.01f)
                    detail = AppendSourceGuidance(detail, pollution);
                Color waterInk = runoff >= 0.01f ? RunoffColour(runoff) : new Color(0.38f, 0.76f, 0.94f);
                string title = runoff >= 0.01f
                    ? $"POLLUTED WATER · {RunoffBand(runoff)}"
                    : "CLEAN WATER";
                ShowHover(title, detail, waterInk, r);
                return;
            }

            if (_showPollution && hasPollution)
            {
                bool air = pollution.Intensity01 >= 0.01f;
                bool runoff = pollution.Runoff01 >= 0.01f;
                string title = air && runoff ? "MIXED POLLUTION"
                    : air ? $"AIR POLLUTION · {AirBand(pollution.Intensity01)}"
                    : $"SOIL / WATER · {RunoffBand(pollution.Runoff01)}";
                string detail = string.Empty;
                if (air)
                    detail = $"AIR  ·  {AirBand(pollution.Intensity01)}  ·  "
                        + $"{pollution.Intensity01 * 100f:0.0}%  ·  "
                        + PollutionUnits.FormatMass(pollution.AirborneUnits);
                if (runoff)
                {
                    if (!string.IsNullOrEmpty(detail)) detail += "\n";
                    detail += $"RUNOFF  ·  {RunoffBand(pollution.Runoff01)}  ·  "
                        + $"{pollution.Runoff01 * 100f:0.0}%  ·  "
                        + PollutionUnits.FormatContaminantMass(pollution.RunoffUnits);
                }
                detail = AppendSourceGuidance(detail, pollution);
                Color ink = runoff && !air ? RunoffColour(pollution.Runoff01)
                    : PollutionColour(pollution.Intensity01);
                ShowHover(title, detail, ink, r);
                return;
            }

            HideHover();
        }

        private static bool TryHoveredBuilding(Vector2 pointer, Vector2 centre,
            float metresPerPixel, out MapFootprint result)
        {
            result = default;
            float best = float.MaxValue;
            bool found = false;
            var buildings = LogisticsMapData.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                MapFootprint candidate = buildings[i];
                Vector2 p = Project(candidate.World, centre, metresPerPixel);
                float half = Mathf.Max(6f,
                    Mathf.Clamp(candidate.RadiusMetres / metresPerPixel, 1.5f, 80f));
                float distance = Mathf.Abs(pointer.x - p.x) + Mathf.Abs(pointer.y - p.y);
                float normalized = distance / half;
                if (normalized > 1f || normalized >= best) continue;
                best = normalized;
                result = candidate;
                found = true;
            }
            return found;
        }

        private static bool TryHoveredPollution(Vector2 pointer, Vector2 centre,
            float metresPerPixel, out PollutionMapCell result)
        {
            result = default;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < _pollutionCells.Count; i++)
            {
                PollutionMapCell candidate = _pollutionCells[i];
                Vector2 p = Project(candidate.World, centre, metresPerPixel);
                float half = Mathf.Max(3f, candidate.SizeMetres / metresPerPixel * 0.5f);
                float dx = Mathf.Abs(pointer.x - p.x);
                float dy = Mathf.Abs(pointer.y - p.y);
                if (dx > half || dy > half) continue;
                float normalized = Mathf.Max(dx, dy) / half;
                if (normalized >= best) continue;
                best = normalized;
                result = candidate;
                found = true;
            }
            return found;
        }

        private static string AppendSourceGuidance(string detail, PollutionMapCell cell)
        {
            EcologyReading ecology = EcologyPressure.Sample(cell.World);
            if (ecology.SupportsNativeEcology)
                detail += $"\nECOLOGY  ·  {ecology.Status}  ·  "
                    + $"{ecology.Vitality01 * 100f:0}% VITALITY  ·  "
                    + $"{ecology.HostilePressureMultiplier:0.00}x HOSTILES";
            else if (ecology.Status != "NO BIOSPHERE")
                detail += $"\nBIOSPHERE  ·  BARREN  ·  "
                    + $"{ecology.HostilePressureMultiplier:0.00}x HOSTILES";

            PollutionEmitter.CollectActiveNear(cell.World, cell.SizeMetres * 1.75f, _sourceReadings);
            if (_sourceReadings.Count == 0)
            {
                detail += "\nNO ACTIVE SOURCE NEARBY · RESIDUAL LOAD";
            }
            else
            {
                detail += "\nACTIVE CONTRIBUTORS";
                int shown = Mathf.Min(3, _sourceReadings.Count);
                for (int i = 0; i < shown; i++)
                {
                    PollutionSourceReading source = _sourceReadings[i];
                    detail += $"\n{i + 1}. {source.Name}\n   ";
                    bool wroteRate = false;
                    if (source.AirbornePerSecond > 0.0001f)
                    {
                        detail += PollutionUnits.FormatRate(source.AirbornePerSecond);
                        wroteRate = true;
                    }
                    if (source.RunoffPerSecond > 0.0001f)
                    {
                        if (wroteRate) detail += " · ";
                        detail += PollutionUnits.FormatContaminantRate(source.RunoffPerSecond);
                    }
                }
            }

            if (cell.Runoff01 >= 0.18f && cell.Intensity01 >= 0.20f)
                detail += "\nACTION · POWER AN ATMOSPHERIC CARBON HARVESTER NEARBY";
            else if (cell.Runoff01 >= 0.18f)
                detail += "\nACTION · REMEDIATE WITHIN 72 m";
            else if (cell.Intensity01 >= 0.20f)
                detail += "\nACTION · CAPTURE WITHIN 96 m";
            return detail;
        }

        private static string AirBand(float intensity) => intensity switch
        {
            < 0.05f => "CLEAR",
            < 0.20f => "TRACE",
            < 0.45f => "HAZE",
            < 0.70f => "SMOG",
            _ => "SEVERE",
        };

        private static string RunoffBand(float intensity) => intensity switch
        {
            < 0.03f => "CLEAN",
            < 0.18f => "TRACE",
            < 0.42f => "TAINTED",
            < 0.70f => "TOXIC",
            _ => "SEVERE",
        };

        private static bool IsOceanAt(Vector2 pointer, Vector2 centre, float metresPerPixel)
        {
            float east = (pointer.x - centre.x) * metresPerPixel;
            float north = (centre.y - pointer.y) * metresPerPixel;
            for (int i = 0; i < _terrainTiles.Count; i++)
            {
                TerrainTile tile = _terrainTiles[i];
                if (Mathf.Abs(east - tile.EastMetres) > tile.HalfEastMetres
                    || Mathf.Abs(north - tile.NorthMetres) > tile.HalfNorthMetres) continue;
                return tile.Ocean;
            }
            return false;
        }

        private static void ShowHover(string title, string detail, Color accent, Rect viewport)
        {
            if (_hoverCard == null) return;
            _hoverTitle.text = title ?? string.Empty;
            _hoverTitle.style.color = new StyleColor(accent);
            _hoverDetail.text = detail ?? string.Empty;
            _hoverCard.style.borderLeftColor = new StyleColor(accent);
            _hoverCard.style.display = DisplayStyle.Flex;

            int lines = 1;
            if (!string.IsNullOrEmpty(detail))
                for (int i = 0; i < detail.Length; i++) if (detail[i] == '\n') lines++;
            float estimatedHeight = 39f + lines * 14f;
            float x = Mathf.Clamp(_pointerCanvas.x + 16f, 6f,
                Mathf.Max(6f, viewport.width - 366f));
            float y = _pointerCanvas.y + 17f;
            if (y + estimatedHeight > viewport.height - 8f)
                y = _pointerCanvas.y - estimatedHeight - 12f;
            _hoverCard.style.left = x;
            _hoverCard.style.top = Mathf.Max(6f, y);
        }

        private static void HideHover()
        {
            if (_hoverCard != null) _hoverCard.style.display = DisplayStyle.None;
        }

        private static void Paint(MeshGenerationContext ctx)
        {
            var painter = ctx.painter2D;
            Rect r = _canvas.contentRect;
            if (r.width < 10 || r.height < 10) return;

            Vector2 centre = new Vector2(r.width * 0.5f, r.height * 0.5f) + _pan;
            float metresPerPixel = _baseScale / Mathf.Max(0.0001f, _zoom);
            _projectionFrame = BuildMapFrame(_anchor);

            DrawTerrain(painter, r, centre, metresPerPixel, _projectionFrame);
            if (_showBuildings) DrawBuildings(painter, r, centre, metresPerPixel);
            DrawGrid(painter, r, centre, metresPerPixel);
            if (_showPollution) DrawPollution(painter, r, centre, metresPerPixel);

            var links = LogisticsMapData.Links;

            // Roads underneath everything: they are context, not the subject.
            if (_showRoads)
            {
                painter.strokeColor = RoadInk;
                painter.lineWidth = 1f;
                for (int i = 0; i < links.Count; i++)
                {
                    if (links[i].Kind != MapOverlayKind.Road) continue;
                    Vector2 p = Project(links[i].A, centre, metresPerPixel);
                    if (!OnScreen(p, r, 8f)) continue;
                    painter.BeginPath();
                    painter.MoveTo(p);
                    painter.LineTo(p + new Vector2(1.4f, 0f));
                    painter.Stroke();
                }
            }

            if (_showZones)
            {
                var zones = LogisticsMapData.Zones;
                for (int i = 0; i < zones.Count; i++)
                {
                    Vector2 c = Project(zones[i].Centre, centre, metresPerPixel);
                    float radius = zones[i].Radius / metresPerPixel;
                    if (radius < 2f || !OnScreen(c, r, radius + 40f)) continue;

                    painter.strokeColor = ZoneInk;
                    painter.lineWidth = 1.5f;
                    painter.BeginPath();
                    painter.Arc(c, radius, 0f, 360f);
                    painter.Stroke();
                }
            }

            if (_showRail)
            {
                painter.strokeColor = RailInk;
                painter.lineWidth = 2.2f;
                for (int i = 0; i < links.Count; i++)
                {
                    if (links[i].Kind != MapOverlayKind.RailLine) continue;
                    Vector2 a = Project(links[i].A, centre, metresPerPixel);
                    Vector2 b = Project(links[i].B, centre, metresPerPixel);
                    if (!OnScreen(a, r, 40f) && !OnScreen(b, r, 40f)) continue;
                    painter.BeginPath();
                    painter.MoveTo(a);
                    painter.LineTo(b);
                    painter.Stroke();
                }
            }

            if (_showDrones)
            {
                for (int i = 0; i < links.Count; i++)
                {
                    if (links[i].Kind != MapOverlayKind.DroneRoute) continue;
                    Vector2 a = Project(links[i].A, centre, metresPerPixel);
                    Vector2 b = Project(links[i].B, centre, metresPerPixel);
                    if (!OnScreen(a, r, 60f) && !OnScreen(b, r, 60f)) continue;

                    // An active route is drawn solid, an idle pairing faint: the map should
                    // show what is MOVING, not merely what is connected.
                    painter.strokeColor = links[i].Active
                        ? DroneInk
                        : new Color(DroneInk.r, DroneInk.g, DroneInk.b, 0.28f);
                    painter.lineWidth = links[i].Active ? 1.8f : 1f;
                    painter.BeginPath();
                    painter.MoveTo(a);
                    painter.LineTo(b);
                    painter.Stroke();
                }
            }

            DrawMarkers(painter, r, centre, metresPerPixel);
        }

        private static void DrawBuildings(Painter2D painter, Rect r, Vector2 centre,
            float metresPerPixel)
        {
            var buildings = LogisticsMapData.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                MapFootprint building = buildings[i];
                Vector2 p = Project(building.World, centre, metresPerPixel);
                float half = Mathf.Clamp(building.RadiusMetres / metresPerPixel, 1.5f, 80f);
                if (!OnScreen(p, r, half)) continue;

                painter.fillColor = building.Ink;
                painter.BeginPath();
                painter.MoveTo(p + new Vector2(0f, -half));
                painter.LineTo(p + new Vector2(half, 0f));
                painter.LineTo(p + new Vector2(0f, half));
                painter.LineTo(p + new Vector2(-half, 0f));
                painter.ClosePath();
                painter.Fill();
            }
        }

        private static void DrawTerrain(Painter2D painter, Rect r, Vector2 centre,
            float metresPerPixel, in MapFrame frame)
        {
            if (frame.Body == null || frame.Sphere == null) return;
            EnsureTerrainRaster(r, metresPerPixel, frame);

            for (int i = 0; i < _terrainTiles.Count; i++)
            {
                TerrainTile tile = _terrainTiles[i];
                Vector2 p = new(
                    centre.x + tile.EastMetres / metresPerPixel,
                    centre.y - tile.NorthMetres / metresPerPixel);
                float halfX = tile.HalfEastMetres / metresPerPixel + 0.7f;
                float halfY = tile.HalfNorthMetres / metresPerPixel + 0.7f;
                if (!OnScreen(p, r, Mathf.Max(halfX, halfY))) continue;

                painter.fillColor = tile.Colour;
                painter.BeginPath();
                painter.MoveTo(p + new Vector2(-halfX, -halfY));
                painter.LineTo(p + new Vector2(halfX, -halfY));
                painter.LineTo(p + new Vector2(halfX, halfY));
                painter.LineTo(p + new Vector2(-halfX, halfY));
                painter.ClosePath();
                painter.Fill();
            }
        }

        private static void EnsureTerrainRaster(Rect viewport, float metresPerPixel, in MapFrame frame)
        {
            Vector2 sizeMetres = new(viewport.width * metresPerPixel, viewport.height * metresPerPixel);
            Vector2 viewCentre = new(-_pan.x * metresPerPixel, _pan.y * metresPerPixel);
            float cellEast = Mathf.Max(1f, sizeMetres.x * 1.25f / TerrainRasterCells);
            float cellNorth = Mathf.Max(1f, sizeMetres.y * 1.25f / TerrainRasterCells);
            float centreThreshold = Mathf.Max(cellEast, cellNorth) * 0.45f;
            bool stale = _terrainTiles.Count == 0
                || _terrainBody != frame.Body
                || (_terrainAnchor - frame.Anchor).sqrMagnitude > centreThreshold * centreThreshold
                || (_terrainViewCentre - viewCentre).sqrMagnitude > centreThreshold * centreThreshold
                || Mathf.Abs(_terrainMetresPerPixel - metresPerPixel) > metresPerPixel * 0.12f
                || Mathf.Abs(_terrainViewportSize.x - viewport.width) > 24f
                || Mathf.Abs(_terrainViewportSize.y - viewport.height) > 24f;
            if (!stale) return;

            _terrainTiles.Clear();
            _terrainBody = frame.Body;
            _terrainAnchor = frame.Anchor;
            _terrainViewCentre = viewCentre;
            _terrainViewportSize = viewport.size;
            _terrainMetresPerPixel = metresPerPixel;

            float radius = Mathf.Max(100f, Vector3.Distance(frame.Anchor, frame.Body.transform.position));
            float halfEast = cellEast * 0.5f;
            float halfNorth = cellNorth * 0.5f;
            float startEast = viewCentre.x - cellEast * (TerrainRasterCells - 1) * 0.5f;
            float startNorth = viewCentre.y - cellNorth * (TerrainRasterCells - 1) * 0.5f;

            for (int y = 0; y < TerrainRasterCells; y++)
            {
                float northMetres = startNorth + y * cellNorth;
                for (int x = 0; x < TerrainRasterCells; x++)
                {
                    float eastMetres = startEast + x * cellEast;
                    Vector3 tangent = frame.East * eastMetres + frame.North * northMetres;
                    float distance = tangent.magnitude;
                    Vector3 worldDirection = frame.Up;
                    if (distance > 0.001f)
                    {
                        float angle = distance / radius;
                        worldDirection = frame.Up * Mathf.Cos(angle)
                            + tangent / distance * Mathf.Sin(angle);
                        worldDirection.Normalize();
                    }

                    Vector3 localDirection = frame.Body.transform.InverseTransformDirection(worldDirection).normalized;
                    if (!frame.Sphere.TrySampleAnalyticMapSurface(localDirection,
                        out float surfaceRadius, out byte surfaceMaterial, out bool ocean)) continue;

                    Color colour = frame.Sphere.materialRegistry != null
                        ? frame.Sphere.materialRegistry.GetColor(surfaceMaterial)
                        : MaterialRegistry.DefaultColor((MaterialId)surfaceMaterial);
                    float seaRadius = frame.Body.genParams.seaRadius;
                    float shade = ocean
                        ? 0.68f
                        : Mathf.Lerp(0.70f, 1.08f, Mathf.InverseLerp(-4f, 160f, surfaceRadius - seaRadius));
                    colour = new Color(
                        Mathf.Clamp01(colour.r * shade),
                        Mathf.Clamp01(colour.g * shade),
                        Mathf.Clamp01(colour.b * shade),
                        0.82f);
                    _terrainTiles.Add(new TerrainTile(eastMetres, northMetres,
                        halfEast, halfNorth, colour, ocean));
                }
            }
        }

        private static void DrawPollution(Painter2D painter, Rect r, Vector2 centre,
            float metresPerPixel)
        {
            for (int i = 0; i < _pollutionCells.Count; i++)
            {
                PollutionMapCell cell = _pollutionCells[i];
                Vector2 p = Project(cell.World, centre, metresPerPixel);
                float half = Mathf.Max(2f, cell.SizeMetres / metresPerPixel * 0.5f);
                if (!OnScreen(p, r, half)) continue;

                if (cell.Runoff01 >= 0.01f)
                {
                    Color runoff = RunoffColour(cell.Runoff01);
                    runoff.a = Mathf.Lerp(0.12f, 0.58f, cell.Runoff01);
                    FillPollutionCell(painter, p, half, runoff);
                }
                if (cell.Intensity01 >= 0.01f)
                {
                    Color air = PollutionColour(cell.Intensity01);
                    air.a = Mathf.Lerp(0.08f, 0.46f, cell.Intensity01);
                    FillPollutionCell(painter, p, half * 0.88f, air);
                }
            }
        }

        private static void FillPollutionCell(Painter2D painter, Vector2 p, float half, Color colour)
        {
            painter.fillColor = colour;
            painter.BeginPath();
            painter.MoveTo(p + new Vector2(-half, -half));
            painter.LineTo(p + new Vector2(half, -half));
            painter.LineTo(p + new Vector2(half, half));
            painter.LineTo(p + new Vector2(-half, half));
            painter.ClosePath();
            painter.Fill();
        }

        private static Color PollutionColour(float intensity)
        {
            intensity = Mathf.Clamp01(intensity);
            if (intensity < 0.35f)
                return Color.Lerp(new Color(0.38f, 0.72f, 0.48f), new Color(0.95f, 0.78f, 0.22f), intensity / 0.35f);
            if (intensity < 0.70f)
                return Color.Lerp(new Color(0.95f, 0.78f, 0.22f), new Color(0.88f, 0.38f, 0.12f), (intensity - 0.35f) / 0.35f);
            return Color.Lerp(new Color(0.88f, 0.38f, 0.12f), new Color(0.70f, 0.12f, 0.12f), (intensity - 0.70f) / 0.30f);
        }

        private static Color RunoffColour(float intensity)
        {
            intensity = Mathf.Clamp01(intensity);
            if (intensity < 0.40f)
                return Color.Lerp(new Color(0.54f, 0.61f, 0.30f), new Color(0.62f, 0.44f, 0.16f), intensity / 0.40f);
            if (intensity < 0.75f)
                return Color.Lerp(new Color(0.62f, 0.44f, 0.16f), new Color(0.46f, 0.20f, 0.48f), (intensity - 0.40f) / 0.35f);
            return Color.Lerp(new Color(0.46f, 0.20f, 0.48f), new Color(0.22f, 0.07f, 0.20f), (intensity - 0.75f) / 0.25f);
        }

        private static Color EcologyColour(float pressure)
        {
            pressure = Mathf.Clamp01(pressure);
            if (pressure < 0.50f)
                return Color.Lerp(new Color(0.36f, 0.78f, 0.45f), new Color(0.94f, 0.72f, 0.20f), pressure / 0.50f);
            return Color.Lerp(new Color(0.94f, 0.72f, 0.20f), new Color(0.72f, 0.14f, 0.12f),
                (pressure - 0.50f) / 0.50f);
        }

        private static bool OnScreen(Vector2 p, Rect r, float margin)
            => p.x > -margin && p.y > -margin && p.x < r.width + margin && p.y < r.height + margin;

        /// <summary>Round grid spacing for the current zoom, in metres. Shared by
        /// the painter and the label pass so the drawn grid and the printed scale
        /// can never disagree.</summary>
        private static float GridStep(float metresPerPixel)
        {
            float target = 120f * metresPerPixel;
            float step = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(1f, target))));
            if (target / step > 5f) step *= 5f;
            else if (target / step > 2f) step *= 2f;
            return step;
        }

        private static void DrawGrid(Painter2D painter, Rect r, Vector2 centre, float metresPerPixel)
        {
            // A scale grid that picks a round spacing for the current zoom, so the player
            // can always read distance off the map without a legend.
            float step = GridStep(metresPerPixel);
            float pixelStep = step / metresPerPixel;
            if (pixelStep < 8f) return;

            painter.strokeColor = GridInk;
            painter.lineWidth = 1f;

            float startX = centre.x % pixelStep;
            for (float x = startX; x < r.width; x += pixelStep)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, r.height));
                painter.Stroke();
            }
            float startY = centre.y % pixelStep;
            for (float y = startY; y < r.height; y += pixelStep)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(0f, y));
                painter.LineTo(new Vector2(r.width, y));
                painter.Stroke();
            }
        }

        private static void DrawMarkers(Painter2D painter, Rect r, Vector2 centre, float metresPerPixel)
        {
            var markers = LogisticsMapData.Markers;

            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];

                if (!_showRail && (m.Kind == MapOverlayKind.RailStation || m.Kind == MapOverlayKind.Train)) continue;
                if (!_showDrones && m.Kind == MapOverlayKind.DronePort) continue;
                if (!_showDeposits && m.Kind == MapOverlayKind.Deposit) continue;

                Vector2 p = Project(m.World, centre, metresPerPixel);
                if (!OnScreen(p, r, 30f)) continue;

                Color ink = m.Alert ? AlertInk : InkFor(m.Kind);

                switch (m.Kind)
                {
                    case MapOverlayKind.Player:
                        painter.strokeColor = PlayerInk;
                        painter.lineWidth = 2f;
                        painter.BeginPath();
                        painter.Arc(p, 6f, 0f, 360f);
                        painter.Stroke();
                        painter.BeginPath();
                        painter.MoveTo(p + new Vector2(0f, -9f));
                        painter.LineTo(p + new Vector2(0f, 9f));
                        painter.Stroke();
                        painter.BeginPath();
                        painter.MoveTo(p + new Vector2(-9f, 0f));
                        painter.LineTo(p + new Vector2(9f, 0f));
                        painter.Stroke();
                        break;

                    case MapOverlayKind.Train:
                        // A filled diamond: the only thing on the map that MOVES should be
                        // the easiest shape to pick out at a glance.
                        painter.fillColor = ink;
                        painter.BeginPath();
                        painter.MoveTo(p + new Vector2(0f, -6f));
                        painter.LineTo(p + new Vector2(6f, 0f));
                        painter.LineTo(p + new Vector2(0f, 6f));
                        painter.LineTo(p + new Vector2(-6f, 0f));
                        painter.ClosePath();
                        painter.Fill();
                        break;

                    case MapOverlayKind.Deposit:
                        // A triangle: distinct from the station square, the train diamond
                        // and the port circle, so four marker types stay tellable apart.
                        painter.fillColor = ink;
                        painter.BeginPath();
                        painter.MoveTo(p + new Vector2(0f, -6f));
                        painter.LineTo(p + new Vector2(5.5f, 4f));
                        painter.LineTo(p + new Vector2(-5.5f, 4f));
                        painter.ClosePath();
                        painter.Fill();
                        break;

                    case MapOverlayKind.RailStation:
                        painter.fillColor = ink;
                        painter.BeginPath();
                        painter.MoveTo(p + new Vector2(-5f, -5f));
                        painter.LineTo(p + new Vector2(5f, -5f));
                        painter.LineTo(p + new Vector2(5f, 5f));
                        painter.LineTo(p + new Vector2(-5f, 5f));
                        painter.ClosePath();
                        painter.Fill();
                        break;

                    default:
                        painter.fillColor = ink;
                        painter.BeginPath();
                        painter.Arc(p, 5f, 0f, 360f);
                        painter.Fill();
                        break;
                }

            }
        }

        // ── Pooled labels ────────────────────────────────────────────────────────
        // Pooled rather than drawn, for the reason recorded on the orbital map:
        // MeshGenerationContext.DrawText needs a font resolved at paint time and is not
        // dependable across Unity versions.
        //
        // The pass runs OUTSIDE the painter (14.19.1): UI Toolkit forbids changing an
        // element's render data while the visual tree is being generated, and setting
        // Label.text from inside generateVisualContent threw every single repaint.
        private static readonly List<Label> _labelPool = new();

        /// <summary>Places every map label for the current view. Mirrors the
        /// painter's projection exactly, and runs on the same triggers.</summary>
        private static void LayoutLabels()
        {
            if (_canvas == null || _labelLayer == null) return;
            Rect r = _canvas.contentRect;
            if (!_open || r.width < 10f || r.height < 10f) { HideLabelsFrom(0); return; }

            Vector2 centre = new Vector2(r.width * 0.5f, r.height * 0.5f) + _pan;
            float metresPerPixel = _baseScale / Mathf.Max(0.0001f, _zoom);
            _projectionFrame = BuildMapFrame(_anchor);

            int used = 0;
            SetLabel(used++, new Vector2(r.width * 0.5f - 12f, 12f),
                "N  ↑", new Color(0.62f, 0.72f, 0.78f), 9);

            // The scale readout, only while the grid itself is actually drawn.
            float step = GridStep(metresPerPixel);
            if (step / metresPerPixel >= 8f)
                SetLabel(used++, new Vector2(r.width - 120f, r.height - 34f),
                    $"GRID {step:0} m", new Color(0.40f, 0.48f, 0.58f), 9);

            var markers = LogisticsMapData.Markers;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (metresPerPixel >= 3f && !m.Alert && m.Kind != MapOverlayKind.Player) continue;
                if (!_showRail && (m.Kind == MapOverlayKind.RailStation || m.Kind == MapOverlayKind.Train)) continue;
                if (!_showDrones && m.Kind == MapOverlayKind.DronePort) continue;
                if (!_showDeposits && m.Kind == MapOverlayKind.Deposit) continue;

                Vector2 p = Project(m.World, centre, metresPerPixel);
                if (!OnScreen(p, r, 30f)) continue;

                string text = string.IsNullOrEmpty(m.Detail) ? m.Label : $"{m.Label}  {m.Detail}";
                SetLabel(used++, p + new Vector2(9f, -7f), text,
                    m.Alert ? AlertInk : InkFor(m.Kind), 9);
            }

            HideLabelsFrom(used);
        }

        private static void SetLabel(int index, Vector2 position, string text, Color colour, int size)
        {
            while (_labelPool.Count <= index)
            {
                var fresh = new Label();
                fresh.style.position = Position.Absolute;
                fresh.pickingMode = PickingMode.Ignore;
                fresh.style.unityFontStyleAndWeight = FontStyle.Bold;
                _labelLayer.Add(fresh);
                _labelPool.Add(fresh);
            }

            var label = _labelPool[index];
            label.style.display = DisplayStyle.Flex;
            label.style.left = position.x;
            label.style.top = position.y;
            label.style.fontSize = size;
            label.style.color = new StyleColor(colour);
            label.text = text;
        }

        private static void HideLabelsFrom(int index)
        {
            for (int i = index; i < _labelPool.Count; i++)
                _labelPool[i].style.display = DisplayStyle.None;
        }
    }
}
