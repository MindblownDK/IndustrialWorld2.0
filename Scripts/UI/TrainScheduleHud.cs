// Assets/Scripts/VoxelEngine/UI/TrainScheduleHud.cs
//
// The conductor's console: edit the stop list of a Train Schedule block.
//
// Same panel language as RailConfigHud - scrim, brass-tinted card, uppercase section
// heads - because a train's schedule is a rail console, and rail consoles look one way
// in this game. Stations are offered by NAME from the live registry, which is the whole
// point of name-matching: the board, the schedule and a rebuilt station all agree.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.GridSystem;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class TrainScheduleHud
    {
        private static VisualElement _root, _scrim, _body;
        private static bool _open, _blocking;
        private static GridTrainScheduleBlock _block;
        private static ScheduleWait _pickerWait = ScheduleWait.Seconds;
        private static string _pickerStation = "";
        private static float _pickerSeconds = 15f;
        private static float _nextRebuild;

        public static bool IsOpen => _open;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _scrim != null && _scrim.parent == uiRoot) return;
            _root = uiRoot;
            if (_scrim != null) _scrim.RemoveFromHierarchy();

            _scrim = new VisualElement { name = "TrainScheduleHud" };
            _scrim.style.position = Position.Absolute;
            _scrim.style.left = 0; _scrim.style.top = 0;
            _scrim.style.right = 0; _scrim.style.bottom = 0;
            _scrim.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
            _scrim.style.alignItems = Align.Center;
            _scrim.style.justifyContent = Justify.Center;
            _scrim.style.display = DisplayStyle.None;
            _root.Add(_scrim);

            _body = new VisualElement();
            _body.style.width = 480;
            _body.style.maxHeight = Length.Percent(88);
            _body.style.paddingLeft = 16; _body.style.paddingRight = 16;
            _body.style.paddingTop = 14; _body.style.paddingBottom = 14;
            _body.style.backgroundColor = new StyleColor(new Color(0.055f, 0.070f, 0.095f, 0.99f));
            T.Radius(_body, 5f);
            T.Border(_body, 1f, new Color(0.22f, 0.34f, 0.44f, 0.95f));
            _scrim.Add(_body);
        }

        public static void Open(GridTrainScheduleBlock block)
        {
            if (block == null) return;
            _block = block;
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
            _block = null;
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
            if (_block == null) { Close(); return; }
            if (UIState.TextInputActive) return;
            if (Time.time < _nextRebuild) return;
            _nextRebuild = Time.time + 0.5f;
            Rebuild();
        }

        private static void Rebuild()
        {
            if (_body == null || _block == null) return;
            _body.Clear();

            Title("TRAIN SCHEDULE", _block.TrainName);
            Info(_block.StatusLabel, T.AccentAmber);

            Section("STOPS");
            var entries = _block.schedule.entries;
            if (entries.Count == 0)
                Info("No stops yet. Add one below - a train with no schedule is a train " +
                     "somebody drives by hand.", T.TextMuted);

            for (int i = 0; i < entries.Count; i++)
            {
                int index = i;
                var entry = entries[i];

                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };
                row.style.alignItems = Align.Center;

                var label = new Label($"{i + 1}.  {ScheduleConditions.Describe(entry)}");
                label.style.flexGrow = 1;
                label.style.fontSize = 11;
                label.style.color = new StyleColor(i == _block.CurrentIndex ? T.AccentCyan : T.TextSecondary);
                row.Add(label);

                row.Add(Mini("UP", () => Move(index, -1)));
                row.Add(Mini("DN", () => Move(index, +1)));
                row.Add(Mini("DEL", () => { entries.RemoveAt(index); Rebuild(); }));
                _body.Add(row);
            }

            Section("ADD A STOP");

            if (string.IsNullOrEmpty(_pickerStation))
            {
                Info("Pick the station this stop serves:", T.TextMuted);
                var stations = RailStation.All;
                int shown = 0;
                for (int i = 0; i < stations.Count && shown < 10; i++)
                {
                    var st = stations[i];
                    if (st == null) continue;
                    shown++;
                    string name = st.StationName;
                    var b = new Button(() => { _pickerStation = name; Rebuild(); }) { text = name };
                    b.style.height = 22;
                    b.style.fontSize = 10;
                    b.style.marginBottom = 2;
                    _body.Add(b);
                }
                if (shown == 0) Info("No stations exist yet. Build a Rail Station first.", T.TextMuted);
            }
            else
            {
                Info("Stop at: " + _pickerStation, T.TextPrimary);

                var waitRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };
                foreach (ScheduleWait wait in System.Enum.GetValues(typeof(ScheduleWait)))
                {
                    var w = wait;
                    var b = new Button(() => { _pickerWait = w; Rebuild(); })
                    {
                        text = w switch
                        {
                            ScheduleWait.HoldFull => "HOLD FULL",
                            ScheduleWait.HoldEmpty => "HOLD EMPTY",
                            ScheduleWait.HoldHasSpace => "HOLD HAS SPACE",
                            _ => "DWELL",
                        },
                    };
                    b.style.fontSize = 9;
                    b.style.height = 22;
                    b.style.marginRight = 4;
                    b.style.backgroundColor = new StyleColor(_pickerWait == w
                        ? new Color(0.16f, 0.38f, 0.50f) : new Color(0.10f, 0.13f, 0.17f));
                    b.style.color = new StyleColor(_pickerWait == w ? Color.white : T.TextSecondary);
                    waitRow.Add(b);
                }
                _body.Add(waitRow);

                if (_pickerWait == ScheduleWait.Seconds)
                {
                    var field = new TextField { value = _pickerSeconds.ToString("0") };
                    field.style.height = 24;
                    field.style.marginBottom = 4;
                    field.RegisterValueChangedCallback(evt =>
                        float.TryParse(evt.newValue, out _pickerSeconds));
                    _body.Add(field);
                }

                var addRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                var add = new Button(() =>
                {
                    _block.schedule.entries.Add(new ScheduleEntry(_pickerStation, _pickerWait, _pickerSeconds));
                    _pickerStation = "";
                    Rebuild();
                })
                { text = "ADD STOP" };
                add.style.flexGrow = 1;
                add.style.height = 26;
                add.style.fontSize = 10;
                addRow.Add(add);

                var back = new Button(() => { _pickerStation = ""; Rebuild(); }) { text = "CHANGE STATION" };
                back.style.height = 26;
                back.style.fontSize = 10;
                back.style.marginLeft = 4;
                addRow.Add(back);
                _body.Add(addRow);
            }

            var close = new Button(Close) { text = "CLOSE" };
            close.style.marginTop = 12;
            close.style.height = 26;
            close.style.fontSize = 9;
            _body.Add(close);
        }

        private static void Move(int index, int delta)
        {
            var entries = _block.schedule.entries;
            int target = index + delta;
            if (target < 0 || target >= entries.Count) return;
            (entries[index], entries[target]) = (entries[target], entries[index]);
            Rebuild();
        }

        private static Button Mini(string text, System.Action onClick)
        {
            var b = new Button(() => onClick()) { text = text };
            b.style.width = 34;
            b.style.height = 20;
            b.style.fontSize = 9;
            b.style.marginLeft = 3;
            return b;
        }

        private static void Title(string text, string sub)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 10 } };
            row.style.alignItems = Align.Center;
            var title = new Label(text);
            title.style.flexGrow = 1;
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.4f;
            title.style.color = new StyleColor(new Color(0.45f, 0.85f, 1f));
            row.Add(title);
            var pill = new Label(sub);
            pill.style.fontSize = 10;
            pill.style.color = new StyleColor(T.AccentAmber);
            row.Add(pill);
            _body.Add(row);
        }

        private static void Section(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 10;
            l.style.letterSpacing = 1.2f;
            l.style.color = new StyleColor(T.TextMuted);
            l.style.marginTop = 10;
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
