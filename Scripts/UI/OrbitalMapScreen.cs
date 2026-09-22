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
// UnityEngine.UIElements also declares a Cursor type, so the bare name is
// ambiguous in this file. Alias the engine one we actually mean.
using Cursor = UnityEngine.Cursor;
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
        private static Label _headerLabel, _statusLabel, _focusLabel, _navLabel;
        private static Button _apButton;
        private static Label _apStatus;

        // Trajectory layer visibility, persisted per player. The device's orbit-path
        // gate stays the master switch; these filter within what the device allows.
        private const string PrefsTrajPlanets = "IW_MapTrajPlanets";
        private const string PrefsTrajGrids = "IW_MapTrajGrids";
        private const string PrefsTrajSats = "IW_MapTrajSats";
        private static bool _showPlanetTraj = true;
        private static bool _showGridTraj = true;
        private static bool _showSatTraj = true;
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
                // A press-and-release without a drag is a click: acquire the contact
                // under the cursor as the navigation target, or clear on empty space.
                if (((Vector2)e.position - _dragStart).magnitude <= 6f)
                    HandleCanvasClick(e.localPosition);
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
            _headerLabel.style.fontSize = 16;
            _headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _headerLabel.style.letterSpacing = 2.4f;
            _headerLabel.style.color = new StyleColor(new Color(0.55f, 0.95f, 0.65f));
            header.Add(_headerLabel);

            _statusLabel = new Label("");
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color = new StyleColor(T.TextMuted);
            _statusLabel.style.marginTop = 2;
            header.Add(_statusLabel);

            _focusLabel = new Label("");
            _focusLabel.style.position = Position.Absolute;
            _focusLabel.style.top = 14;
            _focusLabel.style.fontSize = 14;
            _focusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _focusLabel.style.color = new StyleColor(new Color(0.95f, 0.85f, 0.35f));
            _focusLabel.style.alignSelf = Align.Center;
            _focusLabel.style.width = Length.Percent(100);
            _focusLabel.style.unityTextAlign = TextAnchor.UpperCenter;
            _focusLabel.pickingMode = PickingMode.Ignore;
            _canvas.Add(_focusLabel);

            _navLabel = new Label("");
            _navLabel.style.position = Position.Absolute;
            _navLabel.style.top = 34;
            _navLabel.style.fontSize = 11;
            _navLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _navLabel.style.letterSpacing = 1.6f;
            _navLabel.style.color = new StyleColor(new Color(0.95f, 0.78f, 0.30f));
            _navLabel.style.alignSelf = Align.Center;
            _navLabel.style.width = Length.Percent(100);
            _navLabel.style.unityTextAlign = TextAnchor.UpperCenter;
            _navLabel.pickingMode = PickingMode.Ignore;
            _canvas.Add(_navLabel);

            var hint = new Label("DRAG TO PAN   ·   SCROLL TO ZOOM   ·   CLICK CONTACT = NAV TARGET + FOLLOW   ·   CLICK SPACE = CLEAR   ·   P = AUTOPILOT   ·   M / ESC TO CLOSE");
            hint.style.position = Position.Absolute;
            hint.style.bottom = 12;
            hint.style.width = Length.Percent(100);
            hint.style.unityTextAlign = TextAnchor.LowerCenter;
            hint.style.fontSize = 9;
            hint.style.letterSpacing = 1.4f;
            hint.style.color = new StyleColor(new Color(0.38f, 0.44f, 0.55f));
            hint.pickingMode = PickingMode.Ignore;
            _canvas.Add(hint);

            // ── Trajectory toggles ──
            // A small overlay card, top-right. It eats pointer down/up so flipping a
            // checkbox never pans the map or clears the nav target underneath it.
            var trajPanel = new VisualElement { name = "OrbitalMapTraj" };
            trajPanel.style.position = Position.Absolute;
            trajPanel.style.right = 12;
            trajPanel.style.top = 12;
            trajPanel.style.paddingLeft = 10; trajPanel.style.paddingRight = 10;
            trajPanel.style.paddingTop = 7; trajPanel.style.paddingBottom = 7;
            trajPanel.style.backgroundColor = new StyleColor(new Color(0.075f, 0.09f, 0.12f, 0.95f));
            T.Border(trajPanel, 1f, new Color(0.16f, 0.22f, 0.28f, 0.9f));
            T.Radius(trajPanel, 4f);
            trajPanel.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            trajPanel.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            _canvas.Add(trajPanel);

            var trajTitle = new Label("TRAJECTORIES");
            trajTitle.style.fontSize = 9;
            trajTitle.style.letterSpacing = 1.5f;
            trajTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            trajTitle.style.color = new StyleColor(new Color(0.40f, 0.50f, 0.62f));
            trajTitle.style.marginBottom = 4;
            trajPanel.Add(trajTitle);

            trajPanel.Add(MakeTrajToggle("Planets", PrefsTrajPlanets,
                "Planet and moon orbit paths", v => _showPlanetTraj = v));
            trajPanel.Add(MakeTrajToggle("Grids", PrefsTrajGrids,
                "Construct (ship and station) trajectories", v => _showGridTraj = v));
            trajPanel.Add(MakeTrajToggle("Satellites", PrefsTrajSats,
                "Satellite trajectories", v => _showSatTraj = v));

            // ── Autopilot ──
            // Fly-to control for the ship in reach: engages the cruise to the nav
            // target and reports the live leg underneath. Eats the pointer like the
            // trajectory card, so engaging never pans the map or clears the target.
            var apPanel = new VisualElement { name = "OrbitalMapAutopilot" };
            apPanel.style.position = Position.Absolute;
            apPanel.style.left = 18;
            apPanel.style.top = 76;
            apPanel.style.width = 245;
            apPanel.style.paddingLeft = 10; apPanel.style.paddingRight = 10;
            apPanel.style.paddingTop = 7; apPanel.style.paddingBottom = 7;
            apPanel.style.backgroundColor = new StyleColor(new Color(0.075f, 0.09f, 0.12f, 0.95f));
            T.Border(apPanel, 1f, new Color(0.16f, 0.22f, 0.28f, 0.9f));
            T.Radius(apPanel, 4f);
            apPanel.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            apPanel.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            _canvas.Add(apPanel);

            var apTitle = new Label("AUTOPILOT");
            apTitle.style.fontSize = 9;
            apTitle.style.letterSpacing = 1.5f;
            apTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            apTitle.style.color = new StyleColor(new Color(0.40f, 0.50f, 0.62f));
            apTitle.style.marginBottom = 4;
            apPanel.Add(apTitle);

            _apButton = new Button(() => VoxelEngine.Navigation.NavFlightAutopilot.ToggleFromMap()) { text = "ENGAGE AUTOPILOT [P]" };
            _apButton.style.fontSize = 10;
            _apButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _apButton.style.color = new StyleColor(new Color(0.55f, 0.95f, 0.65f));
            _apButton.style.backgroundColor = new StyleColor(new Color(0.06f, 0.10f, 0.08f, 1f));
            T.Border(_apButton, 1f, new Color(0.25f, 0.45f, 0.30f, 0.9f));
            T.Radius(_apButton, 3f);
            _apButton.style.paddingTop = 4; _apButton.style.paddingBottom = 4;
            _apButton.style.marginTop = 2;
            apPanel.Add(_apButton);

            _apStatus = new Label("");
            _apStatus.style.fontSize = 9;
            _apStatus.style.whiteSpace = WhiteSpace.Normal;
            _apStatus.style.color = new StyleColor(T.TextMuted);
            _apStatus.style.marginTop = 4;
            apPanel.Add(_apStatus);

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
            listTitle.style.fontSize = 11;
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
            NavigationTarget.EnsureRestored();
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
            _navLabel.text = NavigationTarget.HasTarget ? "NAV TARGET: " + NavigationTarget.TargetName : "";
            var apFlight = VoxelEngine.Navigation.NavFlightAutopilot.Active;
            bool apOn = apFlight != null && apFlight.Engaged;
            _apButton.text = apOn ? "DISENGAGE [P]" : (NavigationTarget.HasTarget ? "ENGAGE AUTOPILOT [P]" : "NO NAV TARGET");
            _apStatus.text = VoxelEngine.Navigation.NavFlightAutopilot.StatusLine;

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
                none.style.fontSize = 10;
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
            label.style.fontSize = 9;
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

            bool nav = NavigationTarget.HasTarget && entry.Name == NavigationTarget.TargetName;
            var name = new Label(nav ? entry.Name + "  [NAV]" : entry.Name);
            name.style.fontSize = 11;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.color = new StyleColor(!entry.InRange ? T.TextMuted
                : nav ? new Color(0.95f, 0.85f, 0.35f) : InkFor(entry));
            top.Add(name);

            var kind = new Label(entry.KindLabel);
            kind.style.fontSize = 8;
            kind.style.letterSpacing = 1f;
            kind.style.color = new StyleColor(new Color(0.45f, 0.53f, 0.64f));
            top.Add(kind);
            row.Add(top);

            if (!entry.InRange)
            {
                var oor = new Label("OUT OF TRACKING RANGE");
                oor.style.fontSize = 9;
                oor.style.color = new StyleColor(T.AccentAmber);
                row.Add(oor);
                return row;
            }

            var state = new Label(entry.MotionLabel
                + (string.IsNullOrEmpty(entry.ParentName) ? "" : "  ·  " + entry.ParentName));
            state.style.fontSize = 9;
            state.style.color = new StyleColor(MotionColor(entry.Motion));
            row.Add(state);

            // Pollution burden. Reads 0% everywhere until the pollution simulation
            // lands and feeds the tracking snapshot — the readout is already wired.
            if (entry.Kind == MapEntryKind.Planet || entry.Kind == MapEntryKind.Moon)
            {
                var pol = new Label($"POLLUTION {(entry.Pollution01 * 100d):0}%");
                pol.style.fontSize = 9;
                pol.style.color = new StyleColor(new Color(0.62f, 0.64f, 0.52f));
                row.Add(pol);
            }

            // Full telemetry is what an expensive device buys you. A basic unit stops here.
            if (_device != null && _device.showFullTelemetry && entry.Motion == MapMotionState.Orbiting)
            {
                var detail = new Label(
                    $"AP {OrbitalTrackingService.FormatKm(entry.ApoapsisKm)}   " +
                    $"PE {OrbitalTrackingService.FormatKm(entry.PeriapsisKm)}\n" +
                    $"T {OrbitalTrackingService.FormatPeriod(entry.PeriodSeconds)}   " +
                    $"INC {(double.IsNaN(entry.InclinationDeg) ? "—" : entry.InclinationDeg.ToString("0.0") + "\u00b0")}");
                detail.style.fontSize = 9;
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

        private static Toggle MakeTrajToggle(string label, string prefsKey, string tooltip,
            System.Action<bool> apply)
        {
            bool value = PlayerPrefs.GetInt(prefsKey, 1) == 1;
            apply(value);
            var toggle = new Toggle(label) { value = value, tooltip = tooltip };
            toggle.style.fontSize = 10;
            toggle.style.color = new StyleColor(new Color(0.75f, 0.82f, 0.90f));
            toggle.style.marginBottom = 2;
            toggle.RegisterValueChangedCallback(e =>
            {
                apply(e.newValue);
                PlayerPrefs.SetInt(prefsKey, e.newValue ? 1 : 0);
                PlayerPrefs.Save();
                _canvas.MarkDirtyRepaint();
            });
            return toggle;
        }

        private static bool TrajVisible(MapEntryKind kind)
        {
            switch (kind)
            {
                case MapEntryKind.Planet:
                case MapEntryKind.Moon:
                    return _showPlanetTraj;
                case MapEntryKind.Vessel:
                case MapEntryKind.Station:
                    return _showGridTraj;
                case MapEntryKind.Satellite:
                    return _showSatTraj;
                default:
                    return true;
            }
        }

        private static Color InkFor(MapEntry e)
        {
            // Planets and moons wear their authored hue (the same displayColor the
            // beacons use), so Mars reads red and ice reads pale. Unauthored bodies
            // keep the classic kind colours below.
            if ((e.Kind == MapEntryKind.Planet || e.Kind == MapEntryKind.Moon)
                && e.BodyColor.a > 0.01f)
                return e.BodyColor;
            return e.Kind switch
            {
                MapEntryKind.Satellite => SatelliteInk,
                MapEntryKind.Station => StationInk,
                MapEntryKind.Sun => new Color(1.00f, 0.88f, 0.42f),
                MapEntryKind.Planet => new Color(0.55f, 0.78f, 0.95f),
                MapEntryKind.Moon => new Color(0.72f, 0.75f, 0.80f),
                MapEntryKind.Asteroid => new Color(0.78f, 0.70f, 0.55f),
                _ => VesselInk,
            };
        }

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

            Frame(r, entries, out Vector2 centre, out double3 anchor, out double pxPerKm);

            // Orbit ellipses first, so contact markers draw on top of them.
            if (_device == null || _device.showOrbitPaths)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (!e.InRange) continue;
                    if (e.Motion != MapMotionState.Orbiting) continue;
                    if (!TrajVisible(e.Kind)) continue;
                    if (double.IsNaN(e.ApoapsisKm) || double.IsNaN(e.PeriapsisKm)) continue;
                    // Planets orbit the sun, which is not a BodyInstance — they carry a
                    // null parent with the sun's name and resolve it below. Only entries
                    // with no parent at all are skipped.
                    if (e.Parent == null && string.IsNullOrEmpty(e.ParentName)) continue;

                    double3 parentPos = ParentPosition(entries, e);
                    Vector2 pc = Project(parentPos, anchor, centre, pxPerKm);

                    // Craft AP/PE are altitudes, so the parent's surface is added back to
                    // draw about the body's centre. Body AP/PE are already centre-based
                    // Kepler radii and stay exact.
                    double surface = e.IsBody ? 0d : ParentRadiusKm(entries, e);
                    double apoR = (e.ApoapsisKm + surface) * pxPerKm;
                    double periR = (e.PeriapsisKm + surface) * pxPerKm;
                    if (apoR < 3d || apoR > 40000d) continue;

                    Color lineColor = e.IsCraft
                        ? new Color(InkFor(e).r, InkFor(e).g, InkFor(e).b, 0.5f)
                        : OrbitLine;
                    if (e.IsBody && !double.IsNaN(e.TrueAnomalyRad))
                        PaintBodyOrbit(painter, e, pc, pxPerKm, lineColor);
                    else
                    {
                        DrawEllipse(painter, pc, (float)apoR, (float)periR, lineColor);
                        PaintTrail(painter, pc, Project(e.PositionKm, anchor, centre, pxPerKm),
                            (float)apoR, (float)periR, lineColor);
                    }
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
                    // The asteroid shell draws as a region — a ring plus its rocks —
                    // never as a solid disc, which would swallow the inner system.
                    if (e.Kind == MapEntryKind.Asteroid)
                    {
                        PaintAsteroidBelt(painter, e, p, anchor, centre, pxPerKm, ink, r);
                        continue;
                    }

                    // Bodies draw to scale where possible, with a floor so a distant moon
                    // is still clickable rather than a sub-pixel dot.
                    float floorPx = Mathf.Clamp(3f * Mathf.Sqrt((float)_zoom), 3f, 20f);
                    float radius = Mathf.Clamp((float)(e.RadiusKm * pxPerKm), floorPx, 900f);
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

            // Navigation target reticle, resolved live so it tracks moving craft.
            if (NavigationTarget.HasTarget && NavigationTarget.TryResolve(out double3 navKm))
            {
                Vector2 np = Project(navKm, anchor, centre, pxPerKm);
                if (np.x > -60 && np.y > -60 && np.x < r.width + 60 && np.y < r.height + 60)
                    PaintReticle(painter, np);
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

            Frame(r, entries, out Vector2 centre, out double3 anchor, out double pxPerKm);

            int used = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!e.InRange) continue;

                Vector2 p = Project(e.PositionKm, anchor, centre, pxPerKm);
                if (p.x < -100 || p.y < -100 || p.x > r.width + 100 || p.y > r.height + 100) continue;

                // A moon hugging its planet keeps its name to itself until zoomed in —
                // this alone clears the label pile-up at the system's heart.
                if (e.Kind == MapEntryKind.Moon)
                {
                    Vector2 pp = Project(ParentPosition(entries, e), anchor, centre, pxPerKm);
                    if ((p - pp).magnitude < 36f) continue;
                }

                Label label = used < _labelPool.Count ? _labelPool[used] : NewLabel();
                used++;

                label.text = e.Name;
                label.style.display = DisplayStyle.Flex;
                if (e.Kind == MapEntryKind.Asteroid)
                {
                    float ringR = Mathf.Clamp((float)(e.RadiusKm * pxPerKm), 10f, 4000f);
                    if (ringR > 50f)
                    {
                        label.style.left = p.x + 10f;
                        label.style.top = p.y + ringR - 10f;
                    }
                    else
                    {
                        label.style.left = p.x + 9f;
                        label.style.top = p.y - 8f;
                    }
                }
                else
                {
                    label.style.left = p.x + 9f;
                    label.style.top = p.y - 8f;
                }
                bool marked = e.Name == _focusName
                    || (NavigationTarget.HasTarget && e.Name == NavigationTarget.TargetName);
                label.style.color = new StyleColor(marked
                    ? new Color(0.95f, 0.85f, 0.35f)
                    : InkFor(e));
                label.style.fontSize = e.IsBody ? 12 : 11;
                label.style.unityFontStyleAndWeight = marked ? FontStyle.Bold : FontStyle.Normal;
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

        /// <summary>
        /// The shared view frame: paint, labels, and click hit-testing all project
        /// through this, so what you click is always what you see.
        /// </summary>
        private static void Frame(Rect r, IReadOnlyList<MapEntry> entries,
            out Vector2 centre, out double3 anchor, out double pxPerKm)
        {
            centre = new Vector2(r.width * 0.5f, r.height * 0.5f) + _pan;

            // Anchor the view on the focused contact, else on the dominant body, so the
            // map always opens on something meaningful instead of the system barycentre.
            anchor = default;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Name == _focusName) { anchor = entries[i].PositionKm; break; }
            }

            // Scale: pixels per km. Chosen so a default zoom frames a planet and its moons.
            pxPerKm = 0.00035d * _zoom;
        }

        /// <summary>
        /// Click-to-acquire: the nearest contact inside its grab radius becomes the
        /// navigation target (and the follow focus, when the device allows it).
        /// Empty space clears both, which also hands pan control back to the player.
        /// </summary>
        private static void HandleCanvasClick(Vector2 localPos)
        {
            Rect r = _canvas.contentRect;
            var entries = OrbitalTrackingService.Entries;
            if (r.width < 10 || r.height < 10 || entries.Count == 0) return;
            Frame(r, entries, out Vector2 centre, out double3 anchor, out double pxPerKm);

            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!e.InRange) continue;
                Vector2 p = Project(e.PositionKm, anchor, centre, pxPerKm);

                float d;
                float grab;
                if (e.Kind == MapEntryKind.Asteroid)
                {
                    // The belt is a ring: grab it by its edge. Its centre is usually
                    // the sun, which keeps its own grab radius.
                    float radius = Mathf.Clamp((float)(e.RadiusKm * pxPerKm), 10f, 4000f);
                    d = Mathf.Abs((localPos - p).magnitude - radius);
                    grab = 12f;
                }
                else if (e.IsBody)
                {
                    float floorPx = Mathf.Clamp(3f * Mathf.Sqrt((float)_zoom), 3f, 20f);
                    float radius = Mathf.Clamp((float)(e.RadiusKm * pxPerKm), floorPx, 900f);
                    d = (localPos - p).magnitude;
                    grab = Mathf.Max(radius, 14f);
                }
                else
                {
                    d = (localPos - p).magnitude;
                    grab = 14f;
                }

                if (d <= grab && d < bestD) { best = i; bestD = d; }
            }

            if (best >= 0)
            {
                var e = entries[best];
                if (_device != null && _device.allowFocusSwitching)
                {
                    _focusName = e.Name;
                    _pan = Vector2.zero;
                }
                NavigationTarget.Set(e.Name, e.Kind);
            }
            else
            {
                _focusName = "";
                NavigationTarget.Clear();
            }
            RefreshData();
        }

        /// <summary>
        /// An analytic trajectory trail: an arc of the entry's already-drawn orbit
        /// ellipse, trailing behind the body's current position. No history buffer —
        /// the solved elements are the trail. Same (focus, a, c, b) convention as
        /// DrawEllipse, so the arc always lies exactly on the ellipse.
        /// </summary>
        private static void PaintTrail(Painter2D painter, Vector2 focus, Vector2 bodyPos,
            float apoR, float periR, Color color)
        {
            float a = (apoR + periR) * 0.5f;
            float c = a - periR;
            float b = Mathf.Sqrt(Mathf.Max(0.01f, a * a - c * c));
            if (a < 12f || a > 20000f || b <= 0f) return;

            float tBody = Mathf.Atan2((bodyPos.y - focus.y) / b, (bodyPos.x - focus.x + c) / a);

            const float Sweep = 1.1f;
            const int Chunks = 3;
            for (int k = 0; k < Chunks; k++)
            {
                float t0 = tBody - Sweep + Sweep * k / Chunks;
                float t1 = tBody - Sweep + Sweep * (k + 1) / Chunks;
                float alpha = 0.14f + 0.18f * k;
                painter.strokeColor = new Color(color.r, color.g, color.b, color.a * alpha * 2f);
                painter.lineWidth = 2f;
                painter.BeginPath();
                for (int sgm = 0; sgm <= 14; sgm++)
                {
                    float t = Mathf.Lerp(t0, t1, sgm / 14f);
                    var p = new Vector2(focus.x + Mathf.Cos(t) * a - c, focus.y + Mathf.Sin(t) * b);
                    if (sgm == 0) painter.MoveTo(p); else painter.LineTo(p);
                }
                painter.Stroke();
            }
        }

        /// <summary>Corner-tick reticle marking the live navigation target.</summary>
        private static void PaintReticle(Painter2D painter, Vector2 p)
        {
            painter.strokeColor = new Color(0.95f, 0.78f, 0.30f, 0.95f);
            painter.lineWidth = 2f;
            const float g = 7f;
            const float L = 17f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(p.x - L, p.y - g));
            painter.LineTo(new Vector2(p.x - L, p.y - L));
            painter.LineTo(new Vector2(p.x - g, p.y - L));
            painter.MoveTo(new Vector2(p.x + g, p.y - L));
            painter.LineTo(new Vector2(p.x + L, p.y - L));
            painter.LineTo(new Vector2(p.x + L, p.y - g));
            painter.MoveTo(new Vector2(p.x + L, p.y + g));
            painter.LineTo(new Vector2(p.x + L, p.y + L));
            painter.LineTo(new Vector2(p.x + g, p.y + L));
            painter.MoveTo(new Vector2(p.x - g, p.y + L));
            painter.LineTo(new Vector2(p.x - L, p.y + L));
            painter.LineTo(new Vector2(p.x - L, p.y + g));
            painter.Stroke();
        }

        /// <summary>
        /// The asteroid shell as a region: a boundary ring at the shell radius plus
        /// its rocks as stride-sampled dust motes. Dots share the orbit-path device
        /// gate — a basic unit still sees the ring.
        /// </summary>
        private static void PaintAsteroidBelt(Painter2D painter, MapEntry e, Vector2 p,
            double3 anchor, Vector2 centre, double pxPerKm, Color ink, Rect r)
        {
            float radius = Mathf.Clamp((float)(e.RadiusKm * pxPerKm), 10f, 4000f);
            painter.strokeColor = new Color(ink.r, ink.g, ink.b, 0.65f);
            painter.lineWidth = 1.2f;
            painter.BeginPath();
            painter.Arc(p, radius, 0f, 360f);
            painter.Stroke();

            var registry = CosmicRegistry.Instance;
            var rocks = registry != null ? registry.Asteroids : null;
            if (rocks == null || rocks.Count == 0) return;

            // Inner edge: the shell reads as a band between two radii, not a disc.
            double innerKm = NavigationTarget.BeltInnerRadiusKm(registry, e.PositionKm);
            float innerR = (float)(innerKm * pxPerKm);
            if (innerR >= 4f && innerR < radius)
            {
                painter.strokeColor = new Color(ink.r, ink.g, ink.b, 0.4f);
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.Arc(p, innerR, 0f, 360f);
                painter.Stroke();
            }

            if (_device != null && !_device.showOrbitPaths) return;

            double3 sun = registry.Sun != null ? registry.Sun.positionKmD : default;
            int stride = Mathf.Max(1, (rocks.Count + 219) / 220);
            painter.fillColor = new Color(ink.r, ink.g, ink.b, 0.8f);
            for (int i = 0; i < rocks.Count; i += stride)
            {
                var rock = rocks[i];
                if (rock == null) continue;
                double3 rp = sun + new double3(rock.positionKm.x, rock.positionKm.y, rock.positionKm.z);
                Vector2 sp = Project(rp, anchor, centre, pxPerKm);
                if (sp.x < 0 || sp.y < 0 || sp.x > r.width || sp.y > r.height) continue;
                painter.BeginPath();
                painter.Arc(sp, 1.3f, 0f, 360f);
                painter.Fill();
            }
        }

        private static Vector2 Project(double3 posKm, double3 anchorKm, Vector2 centre, double pxPerKm)
        {
            // Projection onto the system's XY plane: the elements are seeded about the
            // reference plane, so this is the plane the orbits actually share. (An XZ
            // top-down showed the whole system edge-on — every planet in a row.)
            double dx = (posKm.x - anchorKm.x) * pxPerKm;
            double dy = (posKm.y - anchorKm.y) * pxPerKm;
            return new Vector2(centre.x + (float)dx, centre.y - (float)dy);
        }

        /// <summary>
        /// A body's orbit drawn from its live elements: the ring is sampled through
        /// the same elements-to-position path as propagation (full Ω/ω/i), and the
        /// trail is the true-anomaly arc behind the body's live position. The body
        /// always sits exactly on its ring, at whatever orientation and inclination
        /// the designer seeded — no axis-aligned fiction.
        /// </summary>
        private static void PaintBodyOrbit(Painter2D painter, MapEntry e, Vector2 focus,
            double pxPerKm, Color color)
        {
            double aKm = (e.ApoapsisKm + e.PeriapsisKm) * 0.5d;
            if (!(aKm > 0d)) return;
            double ecc = (e.ApoapsisKm - e.PeriapsisKm) / (e.ApoapsisKm + e.PeriapsisKm);
            if (!(ecc >= 0d) || ecc >= 0.95d) return;
            double incl = e.InclinationDeg * Mathf.Deg2Rad;

            painter.strokeColor = color;
            painter.lineWidth = 1.1f;
            painter.BeginPath();
            const int Segments = 96;
            for (int i = 0; i <= Segments; i++)
            {
                double nu = i / (double)Segments * Mathf.PI * 2d;
                double3 off = OrbitMath.ReferencePositionAtTrueAnomaly(
                    aKm, ecc, incl, e.RaanRad, e.ArgPeriapsisRad, nu);
                var p = new Vector2(focus.x + (float)(off.x * pxPerKm),
                    focus.y - (float)(off.y * pxPerKm));
                if (i == 0) painter.MoveTo(p); else painter.LineTo(p);
            }
            painter.Stroke();

            double nuBody = e.TrueAnomalyRad;
            const double Sweep = 1.1d;
            for (int k = 0; k < 3; k++)
            {
                double t0 = nuBody - Sweep + Sweep * k / 3d;
                double t1 = nuBody - Sweep + Sweep * (k + 1) / 3d;
                float alpha = 0.14f + 0.18f * k;
                painter.strokeColor = new Color(color.r, color.g, color.b, color.a * alpha * 2f);
                painter.lineWidth = 2f;
                painter.BeginPath();
                for (int sgm = 0; sgm <= 14; sgm++)
                {
                    double nu = t0 + (t1 - t0) * sgm / 14d;
                    double3 off = OrbitMath.ReferencePositionAtTrueAnomaly(
                        aKm, ecc, incl, e.RaanRad, e.ArgPeriapsisRad, nu);
                    var p = new Vector2(focus.x + (float)(off.x * pxPerKm),
                        focus.y - (float)(off.y * pxPerKm));
                    if (sgm == 0) painter.MoveTo(p); else painter.LineTo(p);
                }
                painter.Stroke();
            }
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
