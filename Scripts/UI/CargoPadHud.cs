// Assets/Scripts/VoxelEngine/UI/CargoPadHud.cs
//
// Console for the interplanetary cargo pad.
//
// The important half of this panel is the FLIGHT BOARD. A pad that only showed its own
// hold would leave the player with no way to tell "nothing has been sent yet" apart from
// "three shipments are two minutes out" - and the whole appeal of unattended freight is
// knowing it is happening while you are elsewhere.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Transport;
using Cursor = UnityEngine.Cursor;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class CargoPadHud
    {
        private static VisualElement _root, _scrim, _body;
        private static bool _open, _blocking;
        private static CargoLaunchPad _pad;
        private static float _refresh;

        private static readonly List<string> _nameScratch = new();

        public static bool IsOpen => _open;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _scrim != null && _scrim.parent == uiRoot) return;
            _root = uiRoot;
            if (_scrim != null) _scrim.RemoveFromHierarchy();

            _scrim = new VisualElement { name = "CargoPadHud" };
            _scrim.style.position = Position.Absolute;
            _scrim.style.left = 0; _scrim.style.top = 0;
            _scrim.style.right = 0; _scrim.style.bottom = 0;
            _scrim.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
            _scrim.style.alignItems = Align.Center;
            _scrim.style.justifyContent = Justify.Center;
            _scrim.style.display = DisplayStyle.None;
            _root.Add(_scrim);

            _body = new VisualElement();
            _body.style.width = 470;
            _body.style.maxHeight = Length.Percent(88);
            _body.style.paddingLeft = 16; _body.style.paddingRight = 16;
            _body.style.paddingTop = 14; _body.style.paddingBottom = 14;
            _body.style.backgroundColor = new StyleColor(new Color(0.050f, 0.060f, 0.085f, 0.99f));
            T.Radius(_body, 5f);
            T.Border(_body, 1f, new Color(0.26f, 0.38f, 0.52f, 0.95f));
            _scrim.Add(_body);
        }

        public static void Open(CargoLaunchPad pad)
        {
            if (pad == null || _scrim == null) return;
            _pad = pad;
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
            _pad = null;
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

            if (_pad == null) { Close(); return; }

            _refresh -= Time.unscaledDeltaTime;
            if (_refresh <= 0f)
            {
                _refresh = 0.5f;
                Rebuild();
            }
        }

        private static void Rebuild()
        {
            if (_body == null || _pad == null) return;

            // Never rebuild mid-typing, or the name field loses focus every refresh.
            if (UIState.TextInputActive) return;

            _body.Clear();

            // ── Header ──
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            head.style.marginBottom = 8;

            var title = new Label("CARGO LAUNCH PAD");
            title.style.flexGrow = 1;
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.4f;
            title.style.color = new StyleColor(new Color(0.52f, 0.78f, 0.98f));
            head.Add(title);

            var pill = new Label(_pad.role == PadRole.Send ? "SEND" : "RECEIVE");
            pill.style.fontSize = 9;
            pill.style.unityFontStyleAndWeight = FontStyle.Bold;
            pill.style.color = new StyleColor(new Color(0.45f, 0.88f, 0.60f));
            pill.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.35f));
            pill.style.paddingLeft = 8; pill.style.paddingRight = 8;
            pill.style.paddingTop = 3; pill.style.paddingBottom = 3;
            T.Radius(pill, 9f);
            head.Add(pill);
            _body.Add(head);

            Info($"On {_pad.BodyName}", T.TextMuted);

            var field = new TextField("Pad name") { value = _pad.HasCustomName ? _pad.PadName : "" };
            field.style.marginTop = 6;
            field.style.marginBottom = 4;
            field.RegisterCallback<FocusInEvent>(_ => UIState.TextInputActive = true);
            field.RegisterCallback<FocusOutEvent>(_ => { UIState.TextInputActive = false; Rebuild(); });
            field.RegisterValueChangedCallback(e => _pad.PadName = e.newValue);
            _body.Add(field);

            // ── Role ──
            Section("ROLE");
            var roleRow = new VisualElement();
            roleRow.style.flexDirection = FlexDirection.Row;
            roleRow.style.marginBottom = 4;
            foreach (PadRole r in System.Enum.GetValues(typeof(PadRole)))
            {
                var captured = r;
                // Through the property, not the field: setting the role has to invalidate
                // the cached port descriptor or belts keep facing the old direction.
                var b = new Button(() => { _pad.Role = captured; Rebuild(); })
                { text = captured.ToString().ToUpperInvariant() };
                b.style.flexGrow = 1;
                b.style.height = 24;
                b.style.fontSize = 9;
                bool active = _pad.role == captured;
                b.style.backgroundColor = new StyleColor(active
                    ? new Color(0.16f, 0.34f, 0.50f) : new Color(0.10f, 0.13f, 0.17f));
                b.style.color = new StyleColor(active ? Color.white : T.TextSecondary);
                roleRow.Add(b);
            }
            _body.Add(roleRow);

            Info(_pad.role == PadRole.Send
                ? $"Ships a full load of {_pad.launchSize} identical items to the chosen pad."
                : "Receives incoming shipments into this pad's hold.", T.TextMuted);

            // ── Destination ──
            if (_pad.role == PadRole.Send)
            {
                Section("DESTINATION");
                CargoLaunchPad.CollectNames(_nameScratch, _pad);

                if (_nameScratch.Count == 0)
                {
                    Info("No other pads exist yet. Build one on another body and name it.",
                        T.TextMuted);
                }
                else
                {
                    var wrap = new VisualElement();
                    wrap.style.flexDirection = FlexDirection.Row;
                    wrap.style.flexWrap = Wrap.Wrap;
                    foreach (var name in _nameScratch)
                    {
                        string captured = name;
                        var target = CargoLaunchPad.Find(captured);
                        bool selected = _pad.destinationPad == captured;

                        var b = new Button(() => { _pad.destinationPad = captured; Rebuild(); })
                        { text = captured + (target != null ? $"  ({target.BodyName})" : "") };
                        b.style.height = 22;
                        b.style.fontSize = 9;
                        b.style.marginRight = 4;
                        b.style.marginBottom = 4;
                        b.style.backgroundColor = new StyleColor(selected
                            ? new Color(0.16f, 0.34f, 0.50f) : new Color(0.10f, 0.13f, 0.17f));
                        b.style.color = new StyleColor(selected ? Color.white : T.TextSecondary);
                        wrap.Add(b);
                    }
                    _body.Add(wrap);

                    if (!string.IsNullOrEmpty(_pad.destinationPad))
                    {
                        var dest = CargoLaunchPad.Find(_pad.destinationPad);
                        if (dest != null)
                        {
                            float seconds = CargoFlightRegistry.EstimateFlightSeconds(
                                _pad.BodyName, dest.BodyName);
                            Info($"Transit time about {seconds:0} s each way.", T.TextSecondary);
                        }
                    }
                }
            }

            // ── Status ──
            Section("STATUS");
            Info(_pad.Status, Color.white);

            // ── Flight board ──
            Section("FLIGHTS IN TRANSIT");
            var flights = CargoFlightRegistry.Flights;
            bool any = false;
            for (int i = 0; i < flights.Count; i++)
            {
                var flight = flights[i];
                if (flight == null) continue;
                if (flight.OriginPad != _pad.PadName && flight.DestinationPad != _pad.PadName) continue;

                bool outbound = flight.OriginPad == _pad.PadName;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 2;

                var arrow = new Label(outbound ? "\u2191" : "\u2193");
                arrow.style.width = 14;
                arrow.style.fontSize = 11;
                arrow.style.color = new StyleColor(outbound
                    ? new Color(0.95f, 0.72f, 0.35f) : new Color(0.45f, 0.88f, 0.60f));
                row.Add(arrow);

                var text = new Label($"{flight.Count} x {flight.Item.displayName}  " +
                                     $"{(outbound ? "to " + flight.DestinationPad : "from " + flight.OriginPad)}");
                text.style.flexGrow = 1;
                text.style.fontSize = 10;
                text.style.color = new StyleColor(Color.white);
                row.Add(text);

                var eta = new Label($"{flight.Remaining:0} s");
                eta.style.fontSize = 9;
                eta.style.color = new StyleColor(T.TextSecondary);
                row.Add(eta);

                _body.Add(row);
                any = true;
            }
            if (!any) Info("Nothing in transit.", T.TextMuted);

            // ── Buttons ──
            var openHold = new Button(() =>
            {
                var pad = _pad;
                Close();
                GameUIController.Instance?.OpenContainer(pad.Hold);
            })
            { text = "OPEN CARGO HOLD" };
            openHold.style.marginTop = 10;
            openHold.style.height = 28;
            openHold.style.fontSize = 10;
            _body.Add(openHold);

            var close = new Button(Close) { text = "CLOSE" };
            close.style.marginTop = 4;
            close.style.height = 26;
            close.style.fontSize = 9;
            _body.Add(close);
        }

        private static void Section(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 9;
            label.style.letterSpacing = 1.3f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(new Color(0.48f, 0.58f, 0.70f));
            label.style.marginTop = 9;
            label.style.marginBottom = 3;
            _body.Add(label);
        }

        private static void Info(string text, Color? color = null)
        {
            var label = new Label(text);
            label.style.fontSize = 10;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = new StyleColor(color ?? T.TextSecondary);
            _body.Add(label);
        }
    }
}
