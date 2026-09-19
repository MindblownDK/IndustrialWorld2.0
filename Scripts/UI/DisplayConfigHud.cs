// Assets/Scripts/VoxelEngine/UI/DisplayConfigHud.cs
//
// The screen console: pick what a display shows and how it shows it.
//
// "Modular" means the hardware is one family and the configuration is the player's -
// so this panel is deliberately tiny: three kinds, five sources, one text field. A
// configuration panel bigger than the device it configures is a design failure.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class DisplayConfigHud
    {
        private static VisualElement _root, _scrim, _body;
        private static bool _open, _blocking;
        private static RailDisplayScreen _screen;

        public static bool IsOpen => _open;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _scrim != null && _scrim.parent == uiRoot) return;
            _root = uiRoot;
            if (_scrim != null) _scrim.RemoveFromHierarchy();

            _scrim = new VisualElement { name = "DisplayConfigHud" };
            _scrim.style.position = Position.Absolute;
            _scrim.style.left = 0; _scrim.style.top = 0;
            _scrim.style.right = 0; _scrim.style.bottom = 0;
            _scrim.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
            _scrim.style.alignItems = Align.Center;
            _scrim.style.justifyContent = Justify.Center;
            _scrim.style.display = DisplayStyle.None;
            _root.Add(_scrim);

            _body = new VisualElement();
            _body.style.width = 420;
            _body.style.maxHeight = Length.Percent(88);
            _body.style.paddingLeft = 16; _body.style.paddingRight = 16;
            _body.style.paddingTop = 14; _body.style.paddingBottom = 14;
            _body.style.backgroundColor = new StyleColor(new Color(0.055f, 0.070f, 0.095f, 0.99f));
            T.Radius(_body, 5f);
            T.Border(_body, 1f, new Color(0.22f, 0.34f, 0.44f, 0.95f));
            _scrim.Add(_body);
        }

        public static void Open(RailDisplayScreen screen)
        {
            if (screen == null) return;
            _screen = screen;
            _open = true;
            _scrim.style.display = DisplayStyle.Flex;
            if (!_blocking) { UIState.PushBlock(); _blocking = true; }
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            Rebuild();
        }

        public static void Close()
        {
            if (!_open) return;
            _open = false;
            UIState.TextInputActive = false;
            if (_scrim != null) _scrim.style.display = DisplayStyle.None;
            if (_blocking) { UIState.PopBlock(); _blocking = false; }
            _screen = null;
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
            if (_screen == null) { Close(); return; }
            if (UIState.TextInputActive) return;
        }

        private static void Rebuild()
        {
            if (_body == null || _screen == null) return;
            _body.Clear();

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 8 } };
            row.style.alignItems = Align.Center;
            var title = new Label("DISPLAY CONSOLE");
            title.style.flexGrow = 1;
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.4f;
            title.style.color = new StyleColor(new Color(0.45f, 0.85f, 1f));
            row.Add(title);

            bool mains = _screen.GetComponent<VoxelEngine.Power.PowerConsumer>() != null;
            var pill = new Label(_screen.IsPowered
                ? (mains ? "ENGINE RUNNING" : "SHAFT TURNING")
                : (mains ? "NO POWER" : "SHAFT STOPPED"));
            pill.style.fontSize = 10;
            pill.style.color = new StyleColor(_screen.IsPowered ? T.AccentGreen : T.AccentAmber);
            row.Add(pill);
            _body.Add(row);

            Info(mains
                ? "Mains-fed: a small electric engine inside turns the drums."
                : "Shaft-fed: this screen lives while anything on the grid turns.", T.TextMuted);

            Section("DISPLAY KIND");
            var kindRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6 } };
            foreach (ScreenKind k in System.Enum.GetValues(typeof(ScreenKind)))
            {
                var kind = k;
                var b = new Button(() => { _screen.Configure(kind, _screen.Source, _screen.customText); Rebuild(); })
                {
                    text = kind switch
                    {
                        ScreenKind.Nixie => "NIXIE",
                        ScreenKind.Analog => "ANALOG DIAL",
                        _ => "SPLIT-FLAP",
                    },
                };
                b.style.fontSize = 9;
                b.style.height = 24;
                b.style.marginRight = 4;
                b.style.backgroundColor = new StyleColor(_screen.Kind == kind
                    ? new Color(0.16f, 0.38f, 0.50f) : new Color(0.10f, 0.13f, 0.17f));
                b.style.color = new StyleColor(_screen.Kind == kind ? Color.white : T.TextSecondary);
                kindRow.Add(b);
            }
            _body.Add(kindRow);

            Section("DATA SOURCE");
            foreach (ScreenSource src in System.Enum.GetValues(typeof(ScreenSource)))
            {
                var source = src;
                var b = new Button(() => { _screen.Configure(_screen.Kind, source, _screen.customText); Rebuild(); })
                {
                    text = source switch
                    {
                        ScreenSource.TrainSpeed => "TRAIN SPEED",
                        ScreenSource.ConsistLoad => "CONSIST LOAD",
                        ScreenSource.Departures => "DEPARTURES",
                        ScreenSource.StationStatus => "STATION STATUS",
                        _ => "CUSTOM TEXT",
                    },
                };
                b.style.fontSize = 9;
                b.style.height = 22;
                b.style.marginBottom = 3;
                b.style.backgroundColor = new StyleColor(_screen.Source == source
                    ? new Color(0.16f, 0.38f, 0.50f) : new Color(0.10f, 0.13f, 0.17f));
                b.style.color = new StyleColor(_screen.Source == source ? Color.white : T.TextSecondary);
                _body.Add(b);
            }

            if (_screen.Source == ScreenSource.CustomText)
            {
                Section("TEXT");
                var field = new TextField { value = _screen.customText, multiline = true };
                field.style.marginBottom = 6;
                field.RegisterValueChangedCallback(evt =>
                {
                    _screen.customText = evt.newValue ?? "";
                    _screen.Configure(_screen.Kind, _screen.Source, _screen.customText);
                });
                _body.Add(field);
            }

            var close = new Button(Close) { text = "CLOSE" };
            close.style.marginTop = 12;
            close.style.height = 26;
            close.style.fontSize = 9;
            _body.Add(close);
        }

        private static void Section(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 10;
            l.style.letterSpacing = 1.2f;
            l.style.color = new StyleColor(T.TextMuted);
            l.style.marginTop = 8;
            l.style.marginBottom = 4;
            _body.Add(l);
        }

        private static void Info(string text, Color color)
        {
            var l = new Label(text);
            l.style.fontSize = 10;
            l.style.color = new StyleColor(color);
            l.style.marginBottom = 3;
            l.style.whiteSpace = WhiteSpace.Normal;
            _body.Add(l);
        }
    }
}
