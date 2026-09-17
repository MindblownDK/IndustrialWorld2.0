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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Settings;
using Cursor = UnityEngine.Cursor;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class LogisticsMapScreen
    {
        private static VisualElement _root, _screen, _canvas, _sidebar, _labelLayer;
        private static Label _headerLabel, _statusLabel;
        private static ScrollView _list;
        private static bool _open, _blocking;

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
        private static bool _showDeposits = true;

        private static float _refreshTimer;

        private static readonly Color Backdrop = new(0.030f, 0.038f, 0.052f, 0.985f);
        private static readonly Color GridInk = new(0.12f, 0.17f, 0.22f, 0.55f);
        private static readonly Color RailInk = new(0.72f, 0.76f, 0.84f, 0.95f);
        private static readonly Color DroneInk = new(0.42f, 0.74f, 0.96f, 0.80f);
        private static readonly Color RoadInk = new(0.30f, 0.32f, 0.36f, 0.85f);
        private static readonly Color ZoneInk = new(0.95f, 0.72f, 0.28f, 0.55f);
        private static readonly Color StationInk = new(1.00f, 0.78f, 0.32f);
        private static readonly Color TrainInk = new(0.40f, 0.92f, 0.58f);
        private static readonly Color PortInk = new(0.45f, 0.80f, 1.00f);
        private static readonly Color AlertInk = new(1.00f, 0.48f, 0.34f);
        private static readonly Color PlayerInk = new(1.00f, 1.00f, 1.00f);
        private static readonly Color DepositInk = new(0.80f, 0.55f, 0.95f);

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
                _canvas.MarkDirtyRepaint();
                e.StopPropagation();
            });
            _canvas.RegisterCallback<PointerDownEvent>(e =>
            {
                _dragging = true;
                _dragStart = e.position;
                _panStart = _pan;
                _canvas.CapturePointer(e.pointerId);
            });
            _canvas.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_dragging) return;
                _pan = _panStart + ((Vector2)e.position - _dragStart);
                _canvas.MarkDirtyRepaint();
            });
            _canvas.RegisterCallback<PointerUpEvent>(e =>
            {
                _dragging = false;
                _canvas.ReleasePointer(e.pointerId);
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

            var hint = new Label("DRAG TO PAN   ·   SCROLL TO ZOOM   ·   L / ESC TO CLOSE");
            hint.style.position = Position.Absolute;
            hint.style.bottom = 12;
            hint.style.width = Length.Percent(100);
            hint.style.unityTextAlign = TextAnchor.LowerCenter;
            hint.style.fontSize = 8;
            hint.style.letterSpacing = 1.4f;
            hint.style.color = new StyleColor(new Color(0.38f, 0.44f, 0.55f));
            hint.pickingMode = PickingMode.Ignore;
            _canvas.Add(hint);

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
            AddLayerToggle("DEPOSITS", DepositInk, () => _showDeposits, v => _showDeposits = v);

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
            var row = new Button(() => { set(!get()); RefreshLayerButtons(); _canvas.MarkDirtyRepaint(); })
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
                _refreshTimer = 0.5f;
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
        }

        public static void Close()
        {
            if (!_open) return;
            _open = false;
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
            BuildList();

            _statusLabel.text =
                $"{LogisticsMapData.RailCells} RAIL CELLS   ·   {LogisticsMapData.StationCount} STATIONS   ·   " +
                $"{LogisticsMapData.TrainCount} TRAINS   ·   {LogisticsMapData.PortCount} PORTS   ·   " +
                $"{LogisticsMapData.ZoneCount} BASES   ·   {LogisticsMapData.DepositCount} DEPOSITS";

            _canvas?.MarkDirtyRepaint();
        }

        /// <summary>Fits the whole network in view, so the map never opens on empty space.</summary>
        private static void FrameAll()
        {
            var bounds = LogisticsMapData.Extent;
            _anchor = bounds.center;

            Rect r = _canvas.contentRect;
            float w = r.width > 10 ? r.width : 900f;
            float h = r.height > 10 ? r.height : 700f;

            float spanX = Mathf.Max(16f, bounds.size.x);
            float spanZ = Mathf.Max(16f, bounds.size.z);

            // Metres per pixel needed to fit each axis, with a margin so markers near the
            // edge are not clipped by their own labels.
            float need = Mathf.Max(spanX / (w * 0.82f), spanZ / (h * 0.82f));
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
                _canvas.MarkDirtyRepaint();
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
        private static Vector2 Project(Vector3 world, Vector2 centre, float metresPerPixel)
        {
            // Top-down onto XZ, +Z up the screen — the same projection the orbital map
            // uses, so the two read consistently despite their different scales.
            float dx = (world.x - _anchor.x) / metresPerPixel;
            float dz = (world.z - _anchor.z) / metresPerPixel;
            return new Vector2(centre.x + dx, centre.y - dz);
        }

        private static void Paint(MeshGenerationContext ctx)
        {
            var painter = ctx.painter2D;
            Rect r = _canvas.contentRect;
            if (r.width < 10 || r.height < 10) return;

            Vector2 centre = new Vector2(r.width * 0.5f, r.height * 0.5f) + _pan;
            float metresPerPixel = _baseScale / Mathf.Max(0.0001f, _zoom);

            DrawGrid(painter, r, centre, metresPerPixel);

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

        private static bool OnScreen(Vector2 p, Rect r, float margin)
            => p.x > -margin && p.y > -margin && p.x < r.width + margin && p.y < r.height + margin;

        private static void DrawGrid(Painter2D painter, Rect r, Vector2 centre, float metresPerPixel)
        {
            // A scale grid that picks a round spacing for the current zoom, so the player
            // can always read distance off the map without a legend.
            float target = 120f * metresPerPixel;
            float step = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(1f, target))));
            if (target / step > 5f) step *= 5f;
            else if (target / step > 2f) step *= 2f;

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

            SetLabel(0, new Vector2(r.width - 120f, r.height - 34f),
                $"GRID {step:0} m", new Color(0.40f, 0.48f, 0.58f), 9);
        }

        private static void DrawMarkers(Painter2D painter, Rect r, Vector2 centre, float metresPerPixel)
        {
            var markers = LogisticsMapData.Markers;
            int labelIndex = 1;   // 0 is the grid scale label

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

                if (metresPerPixel < 3f || m.Alert || m.Kind == MapOverlayKind.Player)
                {
                    string text = string.IsNullOrEmpty(m.Detail) ? m.Label : $"{m.Label}  {m.Detail}";
                    SetLabel(labelIndex++, p + new Vector2(9f, -7f), text, ink, 9);
                }
            }

            HideLabelsFrom(labelIndex);
        }

        // ── Pooled labels ────────────────────────────────────────────────────────
        // Pooled rather than drawn, for the reason recorded on the orbital map:
        // MeshGenerationContext.DrawText needs a font resolved at paint time and is not
        // dependable across Unity versions.
        private static readonly List<Label> _labelPool = new();

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
