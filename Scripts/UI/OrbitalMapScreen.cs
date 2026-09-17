// Assets/Scripts/VoxelEngine/UI/OrbitalMapScreen.cs
//
// The orbital map: a full-screen system view of every body and every named
// construct, with live orbital telemetry.
//
// Rendering approach: the orbit ellipses and body discs are drawn with a single
// UIElements `generateVisualContent` painter rather than a pile of VisualElements.
// One mesh per frame keeps a system with dozens of tracked objects cheap, and the
// painter can draw true ellipses, which is what makes the view read like a real
// map instead of a list with icons.
//
// The map is gated on an equipped OrbitalMapItem, and the device's own stats decide
// how much of this screen actually populates.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class OrbitalMapScreen
    {
        private static VisualElement _root;
        private static VisualElement _screen;
        private static VisualElement _canvas;
        private static VisualElement _sidebar;
        private static VisualElement _labelLayer;
        private static Label _headerLabel, _statusLabel, _focusLabel;
        private static ScrollView _list;
        private static bool _open;
        private static bool _blocking;

        // View state
        private static double _zoom = 1d;
        private static Vector2 _pan;
        private static string _focusName = "";
        private static bool _dragging;
        private static Vector2 _dragStart;
        private static Vector2 _panStart;

        private const double MinZoom = 0.02d;
        private const double MaxZoom = 4000d;

        private static readonly Color Space = new(0.016f, 0.020f, 0.035f, 0.985f);
        private static readonly Color OrbitLine = new(0.32f, 0.85f, 0.42f, 0.55f);
        private static readonly Color SatelliteInk = new(0.35f, 0.80f, 1.00f);
        private static readonly Color StationInk = new(1.00f, 0.72f, 0.24f);
        private static readonly Color VesselInk = new(0.92f, 0.94f, 1.00f);

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
            _screen = new VisualElement { name = "OrbitalMapScreen" };
            _screen.style.position = Position.Absolute;
            _screen.style.left = 0; _screen.style.top = 0;
            _screen.style.right = 0; _screen.style.bottom = 0;
            _screen.style.backgroundColor = new StyleColor(Space);
            _screen.style.display = DisplayStyle.None;
            _screen.style.flexDirection = FlexDirection.Row;
            _root.Add(_screen);

            // ── Map canvas ──
            _canvas = new VisualElement { name = "OrbitalMapCanvas" };
            _canvas.style.flexGrow = 1;
            _canvas.generateVisualContent += Paint;
            _screen.Add(_canvas);

            // Scroll zooms about the centre; drag pans. Registered on the canvas so the
            // sidebar list keeps its own normal scrolling behaviour.
            _canvas.RegisterCallback<WheelEvent>(e =>
            {
                _zoom = System.Math.Clamp(_zoom * (e.delta.y < 0 ? 1.18d : 1d / 1.18d), MinZoom, MaxZoom);
                LayoutLabels();
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
                Vector2 delta = (Vector2)e.position - _dragStart;
                _pan = _panStart + delta;
                LayoutLabels();
                _canvas.MarkDirtyRepaint();
            });
            _canvas.RegisterCallback<PointerUpEvent>(e =>
            {
                _dragging = false;
                _canvas.ReleasePointer(e.pointerId);
            });

            // Label overlay sits above the painted mesh but ignores the pointer so panning
            // and zooming still work when the cursor is over a name.
            _labelLayer = new VisualElement { name = "OrbitalMapLabels" };
            _labelLayer.style.position = Position.Absolute;
            _labelLayer.style.left = 0; _labelLayer.style.top = 0;
            _labelLayer.style.right = 0; _labelLayer.style.bottom = 0;
            _labelLayer.pickingMode = PickingMode.Ignore;
            _canvas.Add(_labelLayer);

            // ── Header ──
            var header = new VisualElement();
            header.style.position = Position.Absolute;
            header.style.left = 18; header.style.top = 14;
            header.pickingMode = PickingMode.Ignore;
            _canvas.Add(header);

            _headerLabel = new Label("ORBITAL MAP");
            _headerLabel.style.fontSize = 15;
            _headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _headerLabel.style.letterSpacing = 2.4f;
            _headerLabel.style.color = new StyleColor(new Color(0.55f, 0.95f, 0.65f));
            header.Add(_headerLabel);

            _statusLabel = new Label("");
            _statusLabel.style.fontSize = 9;
            _statusLabel.style.color = new StyleColor(T.TextMuted);
            _statusLabel.style.marginTop = 2;
            header.Add(_statusLabel);

            _focusLabel = new Label("");
            _focusLabel.style.position = Position.Absolute;
            _focusLabel.style.top = 14;
            _focusLabel.style.fontSize = 13;
            _focusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _focusLabel.style.color = new StyleColor(new Color(0.95f, 0.85f, 0.35f));
            _focusLabel.style.alignSelf = Align.Center;
            _focusLabel.style.width = Length.Percent(100);
            _focusLabel.style.unityTextAlign = TextAnchor.UpperCenter;
            _focusLabel.pickingMode = PickingMode.Ignore;
            _canvas.Add(_focusLabel);

            var hint = new Label("DRAG TO PAN   ·   SCROLL TO ZOOM   ·   CLICK A CONTACT TO FOCUS   ·   M / ESC TO CLOSE");
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
            _sidebar = new VisualElement { name = "OrbitalMapSidebar" };
            _sidebar.style.width = 310;
            _sidebar.style.flexShrink = 0;
            _sidebar.style.backgroundColor = new StyleColor(new Color(0.045f, 0.055f, 0.075f, 0.97f));
            _sidebar.style.paddingLeft = 10; _sidebar.style.paddingRight = 10;
            _sidebar.style.paddingTop = 12; _sidebar.style.paddingBottom = 12;
            _sidebar.style.borderLeftWidth = 1;
            _sidebar.style.borderLeftColor = new StyleColor(new Color(0.16f, 0.22f, 0.28f));
            _screen.Add(_sidebar);

            var listTitle = new Label("TRACKED CONTACTS");
            listTitle.style.fontSize = 10;
            listTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            listTitle.style.letterSpacing = 1.6f;
            listTitle.style.color = new StyleColor(new Color(0.55f, 0.95f, 0.65f));
            listTitle.style.marginBottom = 8;
            _sidebar.Add(listTitle);

            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.style.flexGrow = 1;
            _sidebar.Add(_list);
        }

        // ── Open / close ─────────────────────────────────────────────────────────
        public static void Tick()
        {
            bool textInput = UIState.TextInputActive;

            if (!textInput && GameSettings.WasPressed(InputAction.OrbitalMap))
            {
                if (_open) Close();
                else TryOpen();
            }

            if (_open && !textInput && GameSettings.WasPressed(InputAction.Pause))
            {
                Close();
                UIState.PauseConsumedFrame = Time.frameCount;
            }

            if (_open) RefreshData();
        }

        private static void TryOpen()
        {
            // The gate: the map is a device, not a menu. No device, no map.
            var equipment = Object.FindAnyObjectByType<VoxelEngine.Player.PlayerEquipment>();
            var device = equipment != null ? equipment.EquippedOrbitalMap : null;
            if (device == null)
            {
                BuildFeedbackHud.Show("No orbital map equipped",
                    "Equip an Orbital Map in your Life Support slots to track constructs.");
                return;
            }

            if (_screen == null) return;
            _open = true;
            _screen.style.display = DisplayStyle.Flex;
            if (!_blocking) { UIState.PushBlock(); _blocking = true; }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RefreshData();
        }

        public static void Close()
        {
            if (!_open) return;
            _open = false;
            if (_screen != null) _screen.style.display = DisplayStyle.None;
            if (_blocking) { UIState.PopBlock(); _blocking = false; }
        }

        // ── Data ─────────────────────────────────────────────────────────────────
        private static OrbitalMapItem _device;

        private static void RefreshData()
        {
            var equipment = Object.FindAnyObjectByType<VoxelEngine.Player.PlayerEquipment>();
            _device = equipment != null ? equipment.EquippedOrbitalMap : null;
            if (_device == null) { Close(); return; }

            OrbitalTrackingService.Refresh(_device.trackingRangeKm);

            int tracked = 0, contacts = 0;
            var entries = OrbitalTrackingService.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].IsCraft) { contacts++; if (entries[i].InRange) tracked++; }
            }

            _statusLabel.text = $"{_device.displayName.ToUpperInvariant()}  ·  {_device.CapabilityLabel}  ·  " +
                                $"{tracked}/{contacts} CONTACTS IN RANGE  ·  RANGE {OrbitalTrackingService.FormatKm(_device.trackingRangeKm)}";
            _focusLabel.text = string.IsNullOrEmpty(_focusName) ? "" : "Focus: " + _focusName;

            BuildList(entries);
            LayoutLabels();
            _canvas.MarkDirtyRepaint();
        }

        private static void BuildList(IReadOnlyList<MapEntry> entries)
        {
            _list.Clear();

            AddSection("CONSTRUCTS");
            bool any = false;
            for (int i = 0; i < entries.Count; i++)
            {
                if (!entries[i].IsCraft) continue;
                _list.Add(BuildRow(entries[i]));
                any = true;
            }
            if (!any)
            {
                var none = new Label("No named constructs. Name a grid to track it.");
                none.style.fontSize = 9;
                none.style.whiteSpace = WhiteSpace.Normal;
                none.style.color = new StyleColor(T.TextMuted);
                none.style.marginBottom = 8;
                _list.Add(none);
            }

            AddSection("BODIES");
            for (int i = 0; i < entries.Count; i++)
            {
                if (!entries[i].IsBody) continue;
                _list.Add(BuildRow(entries[i]));
            }
        }

        private static void AddSection(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 8;
            label.style.letterSpacing = 1.5f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(new Color(0.40f, 0.50f, 0.62f));
            label.style.marginTop = 8;
            label.style.marginBottom = 4;
            _list.Add(label);
        }

        private static VisualElement BuildRow(MapEntry entry)
        {
            var row = new VisualElement();
            row.style.marginBottom = 4;
            row.style.paddingLeft = 7; row.style.paddingRight = 7;
            row.style.paddingTop = 5; row.style.paddingBottom = 5;
            row.style.backgroundColor = new StyleColor(new Color(0.075f, 0.09f, 0.12f, 0.95f));
            T.Radius(row, 3f);

            bool focused = entry.Name == _focusName;
            T.Border(row, 1f, focused
                ? new Color(0.95f, 0.85f, 0.35f, 0.85f)
                : new Color(0.16f, 0.22f, 0.28f, 0.9f));

            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.justifyContent = Justify.SpaceBetween;

            var name = new Label(entry.Name);
            name.style.fontSize = 10;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.color = new StyleColor(entry.InRange ? InkFor(entry) : T.TextMuted);
            top.Add(name);

            var kind = new Label(entry.KindLabel);
            kind.style.fontSize = 7;
            kind.style.letterSpacing = 1f;
            kind.style.color = new StyleColor(new Color(0.45f, 0.53f, 0.64f));
            top.Add(kind);
            row.Add(top);

            if (!entry.InRange)
            {
                var oor = new Label("OUT OF TRACKING RANGE");
                oor.style.fontSize = 8;
                oor.style.color = new StyleColor(T.AccentAmber);
                row.Add(oor);
                return row;
            }

            var state = new Label(entry.MotionLabel
                + (string.IsNullOrEmpty(entry.ParentName) ? "" : "  ·  " + entry.ParentName));
            state.style.fontSize = 8;
            state.style.color = new StyleColor(MotionColor(entry.Motion));
            row.Add(state);

            // Full telemetry is what an expensive device buys you. A basic unit stops here.
            if (_device != null && _device.showFullTelemetry && entry.Motion == MapMotionState.Orbiting)
            {
                var detail = new Label(
                    $"AP {OrbitalTrackingService.FormatKm(entry.ApoapsisKm)}   " +
                    $"PE {OrbitalTrackingService.FormatKm(entry.PeriapsisKm)}\n" +
                    $"T {OrbitalTrackingService.FormatPeriod(entry.PeriodSeconds)}   " +
                    $"INC {(double.IsNaN(entry.InclinationDeg) ? "—" : entry.InclinationDeg.ToString("0.0") + "\u00b0")}");
                detail.style.fontSize = 8;
                detail.style.whiteSpace = WhiteSpace.Normal;
                detail.style.color = new StyleColor(new Color(0.58f, 0.66f, 0.76f));
                detail.style.marginTop = 2;
                row.Add(detail);
            }

            if (_device != null && _device.allowFocusSwitching)
            {
                row.RegisterCallback<PointerDownEvent>(_ =>
                {
                    _focusName = _focusName == entry.Name ? "" : entry.Name;
                    _pan = Vector2.zero;
                    RefreshData();
                });
            }
            return row;
        }

        private static Color InkFor(MapEntry e) => e.Kind switch
        {
            MapEntryKind.Satellite => SatelliteInk,
            MapEntryKind.Station => StationInk,
            MapEntryKind.Sun => new Color(1.00f, 0.88f, 0.42f),
            MapEntryKind.Planet => new Color(0.55f, 0.78f, 0.95f),
            MapEntryKind.Moon => new Color(0.72f, 0.75f, 0.80f),
            _ => VesselInk,
        };

        private static Color MotionColor(MapMotionState m) => m switch
        {
            MapMotionState.Orbiting => new Color(0.35f, 0.88f, 0.52f),
            MapMotionState.Escaping => new Color(0.72f, 0.56f, 1.00f),
            MapMotionState.Suborbital => T.AccentAmber,
            MapMotionState.Drifting => new Color(0.55f, 0.62f, 0.72f),
            MapMotionState.Flying => new Color(0.42f, 0.74f, 0.96f),
            _ => new Color(0.50f, 0.56f, 0.64f),
        };

        // ── Painting ─────────────────────────────────────────────────────────────
        private static void Paint(MeshGenerationContext ctx)
        {
            var painter = ctx.painter2D;
            Rect r = _canvas.contentRect;
            if (r.width < 10 || r.height < 10) return;

            var entries = OrbitalTrackingService.Entries;
            if (entries.Count == 0) return;

            Vector2 centre = new(r.width * 0.5f, r.height * 0.5f) ;
            centre += _pan;

            // Anchor the view on the focused contact, else on the dominant body, so the
            // map always opens on something meaningful instead of the system barycentre.
            double3 anchor = default;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Name == _focusName) { anchor = entries[i].PositionKm; break; }
            }

            // Scale: pixels per km. Chosen so a default zoom frames a planet and its moons.
            double pxPerKm = 0.00035d * _zoom;

            // Orbit ellipses first, so contact markers draw on top of them.
            if (_device == null || _device.showOrbitPaths)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (!e.InRange) continue;
                    if (e.Motion != MapMotionState.Orbiting) continue;
                    if (double.IsNaN(e.ApoapsisKm) || double.IsNaN(e.PeriapsisKm)) continue;
                    if (e.Parent == null) continue;

                    double3 parentPos = ParentPosition(entries, e);
                    Vector2 pc = Project(parentPos, anchor, centre, pxPerKm);

                    // Radii include the parent's surface radius, because AP/PE are altitudes
                    // for craft but the ellipse has to be drawn about the body's centre.
                    double surface = ParentRadiusKm(entries, e);
                    double apoR = (e.ApoapsisKm + surface) * pxPerKm;
                    double periR = (e.PeriapsisKm + surface) * pxPerKm;
                    if (apoR < 3d || apoR > 40000d) continue;

                    DrawEllipse(painter, pc, (float)apoR, (float)periR,
                        e.IsCraft ? new Color(InkFor(e).r, InkFor(e).g, InkFor(e).b, 0.5f) : OrbitLine);
                }
            }

            // Bodies and contacts.
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!e.InRange) continue;

                Vector2 p = Project(e.PositionKm, anchor, centre, pxPerKm);
                if (p.x < -200 || p.y < -200 || p.x > r.width + 200 || p.y > r.height + 200) continue;

                Color ink = InkFor(e);

                if (e.IsBody)
                {
                    // Bodies draw to scale where possible, with a floor so a distant moon
                    // is still clickable rather than a sub-pixel dot.
                    float radius = Mathf.Clamp((float)(e.RadiusKm * pxPerKm), 3f, 900f);
                    painter.fillColor = new Color(ink.r * 0.35f, ink.g * 0.35f, ink.b * 0.40f, 0.95f);
                    painter.BeginPath();
                    painter.Arc(p, radius, 0f, 360f);
                    painter.Fill();

                    painter.strokeColor = ink;
                    painter.lineWidth = 1.2f;
                    painter.BeginPath();
                    painter.Arc(p, radius, 0f, 360f);
                    painter.Stroke();
                }
                else
                {
                    // Craft are drawn as a fixed-size marker: a station is never to scale
                    // against a planet, and pretending otherwise makes it invisible.
                    float s = e.Kind == MapEntryKind.Station ? 5f : 4f;
                    painter.fillColor = ink;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(p.x, p.y - s));
                    painter.LineTo(new Vector2(p.x + s, p.y));
                    painter.LineTo(new Vector2(p.x, p.y + s));
                    painter.LineTo(new Vector2(p.x - s, p.y));
                    painter.ClosePath();
                    painter.Fill();
                }

            }
        }

        // ── Name labels ──────────────────────────────────────────────────────────
        // Drawn as pooled Labels in an overlay rather than via MeshGenerationContext.DrawText,
        // which needs a font resolved at paint time and is not dependable across Unity
        // versions. Pooling keeps a busy system from churning elements every refresh.
        private static readonly List<Label> _labelPool = new();

        private static void LayoutLabels()
        {
            Rect r = _canvas.contentRect;
            var entries = OrbitalTrackingService.Entries;
            if (r.width < 10 || r.height < 10) { HideLabelsFrom(0); return; }

            Vector2 centre = new Vector2(r.width * 0.5f, r.height * 0.5f) + _pan;
            double3 anchor = default;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Name == _focusName) { anchor = entries[i].PositionKm; break; }

            double pxPerKm = 0.00035d * _zoom;

            int used = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!e.InRange) continue;

                Vector2 p = Project(e.PositionKm, anchor, centre, pxPerKm);
                if (p.x < -100 || p.y < -100 || p.x > r.width + 100 || p.y > r.height + 100) continue;

                Label label = used < _labelPool.Count ? _labelPool[used] : NewLabel();
                used++;

                label.text = e.Name;
                label.style.display = DisplayStyle.Flex;
                label.style.left = p.x + 9f;
                label.style.top = p.y - 8f;
                label.style.color = new StyleColor(e.Name == _focusName
                    ? new Color(0.95f, 0.85f, 0.35f)
                    : InkFor(e));
                label.style.fontSize = e.IsBody ? 10 : 9;
                label.style.unityFontStyleAndWeight = e.Name == _focusName ? FontStyle.Bold : FontStyle.Normal;
            }
            HideLabelsFrom(used);
        }

        private static Label NewLabel()
        {
            var label = new Label();
            label.style.position = Position.Absolute;
            label.pickingMode = PickingMode.Ignore;
            label.style.letterSpacing = 0.6f;
            _labelLayer.Add(label);
            _labelPool.Add(label);
            return label;
        }

        private static void HideLabelsFrom(int index)
        {
            for (int i = index; i < _labelPool.Count; i++)
                _labelPool[i].style.display = DisplayStyle.None;
        }

        private static double3 ParentPosition(IReadOnlyList<MapEntry> entries, MapEntry child)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].IsBody && entries[i].Name == child.ParentName) return entries[i].PositionKm;
            }
            return child.PositionKm;
        }

        private static double ParentRadiusKm(IReadOnlyList<MapEntry> entries, MapEntry child)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].IsBody && entries[i].Name == child.ParentName) return entries[i].RadiusKm;
            }
            return 0d;
        }

        private static Vector2 Project(double3 posKm, double3 anchorKm, Vector2 centre, double pxPerKm)
        {
            // Top-down projection onto the system's XZ plane: the orbital plane most bodies
            // share, so this reads as a map rather than an arbitrary slice.
            double dx = (posKm.x - anchorKm.x) * pxPerKm;
            double dz = (posKm.z - anchorKm.z) * pxPerKm;
            return new Vector2(centre.x + (float)dx, centre.y - (float)dz);
        }

        /// <summary>Draws an ellipse from its apoapsis/periapsis radii, offset so a focus sits at one focus.</summary>
        private static void DrawEllipse(Painter2D painter, Vector2 centre, float apoR, float periR, Color color)
        {
            float a = (apoR + periR) * 0.5f;          // semi-major
            float c = a - periR;                       // focal distance
            float b = Mathf.Sqrt(Mathf.Max(0.01f, a * a - c * c));   // semi-minor

            painter.strokeColor = color;
            painter.lineWidth = 1.1f;
            painter.BeginPath();

            const int Segments = 96;
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments * Mathf.PI * 2f;
                // Offset by the focal distance so the parent body sits at a focus of the
                // ellipse, not its centre — the visual signature of a real orbit.
                var p = new Vector2(centre.x + Mathf.Cos(t) * a - c, centre.y + Mathf.Sin(t) * b);
                if (i == 0) painter.MoveTo(p); else painter.LineTo(p);
            }
            painter.Stroke();
        }
    }
}
