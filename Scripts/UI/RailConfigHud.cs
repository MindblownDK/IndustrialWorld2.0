// Assets/Scripts/VoxelEngine/UI/RailConfigHud.cs
//
// Console for the rail system: stations, trains and switches.
//
// One panel for all three because they are one workflow. The player names a station,
// then builds a schedule out of those names, then watches the train run it. Splitting
// that across three UIs would make the player hold the connection in their head.
//
// The schedule editor is the important half: a train is only as good as the standing
// order it runs, and an order made of station NAMES keeps working when the player
// rebuilds the station it refers to.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using Cursor = UnityEngine.Cursor;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class RailConfigHud
    {
        private static VisualElement _root, _scrim, _body;
        private static bool _open, _blocking;

        private static RailStation _station;
        private static RailTrack _switchTrack;
        private static GridRailTruck _truck;

        private static readonly List<string> _nameScratch = new();

        public static bool IsOpen => _open;

        // ── Mounting ─────────────────────────────────────────────────────────────
        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _scrim != null && _scrim.parent == uiRoot) return;
            _root = uiRoot;
            if (_scrim != null) _scrim.RemoveFromHierarchy();

            _scrim = new VisualElement { name = "RailConfigHud" };
            _scrim.style.position = Position.Absolute;
            _scrim.style.left = 0; _scrim.style.top = 0;
            _scrim.style.right = 0; _scrim.style.bottom = 0;
            _scrim.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
            _scrim.style.alignItems = Align.Center;
            _scrim.style.justifyContent = Justify.Center;
            _scrim.style.display = DisplayStyle.None;
            _root.Add(_scrim);

            _body = new VisualElement();
            _body.style.width = 460;
            _body.style.maxHeight = Length.Percent(88);
            _body.style.paddingLeft = 16; _body.style.paddingRight = 16;
            _body.style.paddingTop = 14; _body.style.paddingBottom = 14;
            _body.style.backgroundColor = new StyleColor(new Color(0.055f, 0.070f, 0.095f, 0.99f));
            T.Radius(_body, 5f);
            T.Border(_body, 1f, new Color(0.22f, 0.34f, 0.44f, 0.95f));
            _scrim.Add(_body);
        }

        // ── Open / close ─────────────────────────────────────────────────────────
        public static void OpenStation(RailStation station)
        {
            if (station == null) return;
            _station = station; _switchTrack = null; _truck = null;
            Show();
        }

        public static void OpenSwitch(RailTrack track)
        {
            if (track == null) return;
            _switchTrack = track; _station = null; _truck = null;
            Show();
        }

        /// <summary>The bogie console: snapping policy and rail state for one truck.
        /// Opened with E on the truck itself (12.2.0).</summary>
        public static void OpenBogie(GridRailTruck truck)
        {
            if (truck == null) return;
            _truck = truck; _station = null; _switchTrack = null;
            Show();
        }

        private static void Show()
        {
            if (_scrim == null) return;
            _open = true;
            _scrim.style.display = DisplayStyle.Flex;
            if (!_blocking) { UIState.PushBlock(); _blocking = true; }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Rebuild();
        }

        public static void Close()
        {
            if (!_open) return;
            _open = false;
            UIState.TextInputActive = false;
            if (_scrim != null) _scrim.style.display = DisplayStyle.None;
            if (_blocking) { UIState.PopBlock(); _blocking = false; }
            _station = null; _switchTrack = null; _truck = null;
        }

        public static void Tick()
        {
            if (!_open) return;

            if (!UIState.TextInputActive
                && VoxelEngine.Settings.GameSettings.WasPressed(VoxelEngine.Settings.InputAction.Pause))
            {
                Close();
                UIState.PauseConsumedFrame = Time.frameCount;
                return;
            }

            // A live panel: a station's hold is filled by machines the player is not
            // looking at. (The v1 train console lived here until its retirement in
            // 12.0.0-dev; a train is a grid now and configures itself through the grid
            // terminal like every other buildable.)
            if (_station != null || _truck != null) Rebuild();
        }

        // ── Build ────────────────────────────────────────────────────────────────
        private static void Rebuild()
        {
            if (_body == null) return;

            // Preserve text focus across a rebuild, or typing a name would be impossible
            // on a panel that refreshes every frame.
            if (UIState.TextInputActive) return;

            _body.Clear();

            if (_station != null) BuildStation();
            else if (_switchTrack != null) BuildSwitch();
            else if (_truck != null) BuildBogie();
            else { Close(); return; }

            var close = new Button(Close) { text = "CLOSE" };
            close.style.marginTop = 12;
            close.style.height = 26;
            close.style.fontSize = 9;
            _body.Add(close);
        }

        private static void Title(string text, string status, Color statusColor)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 10;

            var title = new Label(text);
            title.style.flexGrow = 1;
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.4f;
            title.style.color = new StyleColor(new Color(0.45f, 0.85f, 1f));
            row.Add(title);

            var pill = new Label(status);
            pill.style.fontSize = 9;
            pill.style.unityFontStyleAndWeight = FontStyle.Bold;
            pill.style.color = new StyleColor(statusColor);
            pill.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.35f));
            pill.style.paddingLeft = 8; pill.style.paddingRight = 8;
            pill.style.paddingTop = 3; pill.style.paddingBottom = 3;
            T.Radius(pill, 9f);
            row.Add(pill);

            _body.Add(row);
        }

        // ── Bogie panel ──────────────────────────────────────────────────────────
        private static void BuildBogie()
        {
            if (_truck == null) return;
            var bogie = _truck.Grid != null ? _truck.Grid.GetComponent<GridRailBogie>() : null;
            bool onRails = bogie != null && bogie.IsOnRails;

            var head = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 8 } };
            head.style.alignItems = Align.Center;
            var title = new Label("BOGIE");
            title.style.flexGrow = 1;
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.4f;
            title.style.color = new StyleColor(new Color(0.45f, 0.85f, 1f));
            head.Add(title);
            var pill = new Label(onRails ? "ON RAILS" : "OFF RAILS");
            pill.style.fontSize = 10;
            pill.style.color = new StyleColor(onRails ? T.AccentGreen : T.AccentAmber);
            head.Add(pill);
            _body.Add(head);

            if (bogie != null)
            {
                if (onRails)
                    Info($"Speed {(bogie.Speed * 3.6f):0.0} km/h.  {bogie.StatusLabel}", T.TextSecondary);
                else
                    Info(string.IsNullOrEmpty(bogie.BlockedReason)
                        ? "Not on rails." : bogie.BlockedReason, T.TextSecondary);
            }
            else
            {
                Info("This truck's grid has no bogie yet.", T.TextMuted);
            }

            Section("SNAPPING");
            Info("Auto-snap re-latches this train onto rail under it by itself - after " +
                 "building, after a reload, or when the line grows into the yard. Turn " +
                 "it off to keep a parked wagon parked.", T.TextMuted);

            var snapRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6 } };

            var toggle = new Button(() => { _truck.autoSnap = !_truck.autoSnap; Rebuild(); })
            {
                text = "AUTO-SNAP: " + (_truck.autoSnap ? "ON" : "OFF"),
            };
            toggle.style.flexGrow = 1;
            toggle.style.height = 26;
            toggle.style.fontSize = 10;
            toggle.style.backgroundColor = new StyleColor(_truck.autoSnap
                ? new Color(0.16f, 0.38f, 0.50f) : new Color(0.10f, 0.13f, 0.17f));
            toggle.style.color = new StyleColor(_truck.autoSnap ? Color.white : T.TextSecondary);
            snapRow.Add(toggle);

            if (bogie != null)
            {
                if (onRails)
                {
                    var lift = new Button(() => { bogie.Detach(); Rebuild(); }) { text = "LIFT OFF" };
                    lift.style.width = 90;
                    lift.style.height = 26;
                    lift.style.fontSize = 10;
                    lift.style.marginLeft = 4;
                    snapRow.Add(lift);
                }
                else
                {
                    var snap = new Button(() => { bogie.TrySnapToTrack(); Rebuild(); }) { text = "SNAP NOW" };
                    snap.style.width = 90;
                    snap.style.height = 26;
                    snap.style.fontSize = 10;
                    snap.style.marginLeft = 4;
                    snapRow.Add(snap);
                }
            }
            _body.Add(snapRow);
        }

        private static Label Section(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 9;
            label.style.letterSpacing = 1.3f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(new Color(0.50f, 0.60f, 0.72f));
            label.style.marginTop = 8;
            label.style.marginBottom = 4;
            _body.Add(label);
            return label;
        }

        private static Label Info(string text, Color? color = null)
        {
            var label = new Label(text);
            label.style.fontSize = 10;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = new StyleColor(color ?? T.TextSecondary);
            _body.Add(label);
            return label;
        }

        private static TextField NameField(string label, string value, System.Action<string> apply)
        {
            var field = new TextField(label) { value = value };
            field.style.marginBottom = 6;
            field.RegisterCallback<FocusInEvent>(_ => UIState.TextInputActive = true);
            field.RegisterCallback<FocusOutEvent>(_ => { UIState.TextInputActive = false; Rebuild(); });
            field.RegisterValueChangedCallback(e => apply(e.newValue));
            _body.Add(field);
            return field;
        }

        // ── Station ──────────────────────────────────────────────────────────────
        private static void BuildStation()
        {
            var platform = _station.ServedTrack;
            bool ok = platform != null;

            Title("RAIL STATION", ok ? "ON LINE" : "NO TRACK",
                ok ? new Color(0.35f, 0.88f, 0.52f) : T.AccentAmber);

            NameField("Station name", _station.HasCustomName ? _station.StationName : "",
                v => _station.StationName = v);
            Info("Schedules refer to this name. Renaming a station re-points every train that " +
                 "already calls here.", T.TextMuted);

            Section("ROLE");
            var roleRow = new VisualElement();
            roleRow.style.flexDirection = FlexDirection.Row;
            roleRow.style.marginBottom = 6;
            foreach (StationRole role in System.Enum.GetValues(typeof(StationRole)))
            {
                var captured = role;
                var b = new Button(() => { _station.role = captured; Rebuild(); })
                { text = captured.ToString().ToUpperInvariant() };
                b.style.flexGrow = 1;
                b.style.height = 24;
                b.style.fontSize = 9;
                bool active = _station.role == captured;
                b.style.backgroundColor = new StyleColor(active
                    ? new Color(0.16f, 0.38f, 0.50f) : new Color(0.10f, 0.13f, 0.17f));
                b.style.color = new StyleColor(active ? Color.white : T.TextSecondary);
                roleRow.Add(b);
            }
            _body.Add(roleRow);

            Info(_station.role switch
            {
                StationRole.Load => "Moves cargo from this station's hold INTO a docked train.",
                StationRole.Unload => "Moves cargo from a docked train INTO this station's hold.",
                _ => "A timing point. Trains stop here but no cargo moves.",
            }, T.TextMuted);

            Section("STATUS");
            if (!ok)
            {
                Info($"No rail track within {RailStation.ServiceRadius:0} m. Lay track beside the " +
                     "station so trains can reach it.", T.AccentAmber);
            }
            else
            {
                int used = 0;
                for (int i = 0; i < _station.Hold.Size; i++)
                    if (!_station.Hold.GetSlot(i).IsEmpty) used++;

                Info($"Platform connected.  Hold {used}/{_station.Hold.Size} slots used.  " +
                     $"Transfer {_station.transferPerSecond:0}/s  ·  Dwell {_station.dwellSeconds:0}s");
            }

            var openHold = new Button(() =>
            {
                Close();
                GameUIController.Instance?.OpenContainer(_station.Hold);
            })
            { text = "OPEN STATION HOLD" };
            openHold.style.marginTop = 8;
            openHold.style.height = 28;
            openHold.style.fontSize = 10;
            _body.Add(openHold);
        }

        // ── Switch ───────────────────────────────────────────────────────────────
        private static void BuildSwitch()
        {
            Title("RAIL SWITCH",
                _switchTrack.IsActiveJunction ? "JUNCTION" : "PLAIN LINE",
                _switchTrack.IsActiveJunction ? new Color(0.35f, 0.88f, 0.52f) : T.TextMuted);

            var links = _switchTrack.Links;
            if (!_switchTrack.IsActiveJunction)
            {
                Info("This switch has fewer than three connections, so it behaves as plain " +
                     "track. Lay a branch off it to make it a working junction.", T.TextMuted);
                return;
            }

            Section("ROUTE SET TO");
            for (int i = 0; i < links.Count; i++)
            {
                int index = i;
                var link = links[i];
                if (link == null) continue;

                Vector3 dir = link.transform.position - _switchTrack.transform.position;
                string compass = Describe(dir);

                var b = new Button(() => { _switchTrack.SetSwitchSelection(index); Rebuild(); })
                { text = (index == _switchTrack.SwitchSelection ? "\u25c9  " : "\u25cb  ") + compass };
                b.style.height = 24;
                b.style.fontSize = 10;
                b.style.marginBottom = 3;
                bool active = index == _switchTrack.SwitchSelection;
                b.style.backgroundColor = new StyleColor(active
                    ? new Color(0.16f, 0.38f, 0.50f) : new Color(0.10f, 0.13f, 0.17f));
                b.style.color = new StyleColor(active ? Color.white : T.TextSecondary);
                _body.Add(b);
            }

            Info("A train arriving from the selected leg takes the next available route " +
                 "instead, so the points can never bounce a train straight back.", T.TextMuted);
        }

        private static string Describe(Vector3 direction)
        {
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z))
                return direction.x > 0f ? "EAST" : "WEST";
            return direction.z > 0f ? "NORTH" : "SOUTH";
        }
    }
}
