// Assets/Scripts/VoxelEngine/UI/GridIdentityHud.cs
//
// Name a construct, declare what it is, and commit it to orbit.
//
// This is the player's entry point into the whole orbital programme: a grid is
// just a pile of blocks until it is named, and only a grid declared a SATELLITE
// or STATION can be committed to a stable orbit. Keeping naming, classification
// and orbit commitment on one panel makes that progression obvious instead of
// scattering it across three different blocks.
//
// Opened with U while seated in a cockpit (rebindable).

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class GridIdentityHud
    {
        private static VisualElement _root, _panel;
        private static TextField _nameField;
        private static Label _statusLabel, _orbitLabel;
        private static Button _orbitButton;
        private static VisualElement _classRow;
        private static bool _open, _blocking;
        private static GridEntity _target;

        public static bool IsOpen => _open;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _panel != null && _panel.parent == uiRoot) return;
            _root = uiRoot;
            if (_panel != null) _panel.RemoveFromHierarchy();
            Build();
        }

        private static void Build()
        {
            var scrim = new VisualElement { name = "GridIdentityHud" };
            scrim.style.position = Position.Absolute;
            scrim.style.left = 0; scrim.style.top = 0; scrim.style.right = 0; scrim.style.bottom = 0;
            scrim.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
            scrim.style.alignItems = Align.Center;
            scrim.style.justifyContent = Justify.Center;
            scrim.style.display = DisplayStyle.None;
            _root.Add(scrim);
            _panel = scrim;

            var card = new VisualElement();
            card.style.width = 430;
            card.style.paddingLeft = 18; card.style.paddingRight = 18;
            card.style.paddingTop = 16; card.style.paddingBottom = 16;
            card.style.backgroundColor = new StyleColor(new Color(0.055f, 0.070f, 0.095f, 0.99f));
            T.Radius(card, 5f);
            T.Border(card, 1f, new Color(0.22f, 0.34f, 0.44f, 0.95f));
            scrim.Add(card);

            var title = new Label("CONSTRUCT REGISTRY");
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.8f;
            title.style.color = new StyleColor(new Color(0.45f, 0.85f, 1f));
            title.style.marginBottom = 10;
            card.Add(title);

            _nameField = new TextField("Designation");
            _nameField.maxLength = GridIdentity.MaxNameLength;
            _nameField.style.marginBottom = 4;
            // While a text field has focus the game's hotkeys must stand down, or typing
            // an "m" in a station name would throw open the orbital map.
            _nameField.RegisterCallback<FocusInEvent>(_ => UIState.TextInputActive = true);
            _nameField.RegisterCallback<FocusOutEvent>(_ => UIState.TextInputActive = false);
            _nameField.RegisterValueChangedCallback(e =>
            {
                if (_target == null) return;
                GridIdentity.Ensure(_target).SetDisplayName(e.newValue);
            });
            card.Add(_nameField);

            var hint = new Label("Named constructs appear on the orbital map.");
            hint.style.fontSize = 8;
            hint.style.color = new StyleColor(T.TextMuted);
            hint.style.marginBottom = 12;
            card.Add(hint);

            var classTitle = new Label("CLASSIFICATION");
            classTitle.style.fontSize = 9;
            classTitle.style.letterSpacing = 1.3f;
            classTitle.style.color = new StyleColor(new Color(0.50f, 0.60f, 0.72f));
            classTitle.style.marginBottom = 5;
            card.Add(classTitle);

            _classRow = new VisualElement();
            _classRow.style.flexDirection = FlexDirection.Row;
            _classRow.style.marginBottom = 12;
            card.Add(_classRow);
            AddClassButton("VESSEL", GridClass.Vessel);
            AddClassButton("SATELLITE", GridClass.Satellite);
            AddClassButton("STATION", GridClass.Station);

            _orbitLabel = new Label("");
            _orbitLabel.style.fontSize = 9;
            _orbitLabel.style.whiteSpace = WhiteSpace.Normal;
            _orbitLabel.style.marginBottom = 8;
            _orbitLabel.style.color = new StyleColor(new Color(0.60f, 0.68f, 0.78f));
            card.Add(_orbitLabel);

            _orbitButton = new Button(ToggleOrbit) { text = "COMMIT TO ORBIT" };
            _orbitButton.style.height = 30;
            _orbitButton.style.fontSize = 10;
            _orbitButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _orbitButton.style.letterSpacing = 1.2f;
            card.Add(_orbitButton);

            _statusLabel = new Label("");
            _statusLabel.style.fontSize = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop = 8;
            _statusLabel.style.color = new StyleColor(T.AccentAmber);
            card.Add(_statusLabel);

            var close = new Button(Close) { text = "CLOSE  (U)" };
            close.style.marginTop = 12;
            close.style.height = 24;
            close.style.fontSize = 9;
            card.Add(close);
        }

        private static void AddClassButton(string label, GridClass cls)
        {
            var b = new Button(() =>
            {
                if (_target == null) return;
                GridIdentity.Ensure(_target).SetGridClass(cls);
                Refresh();
            })
            { text = label };
            b.userData = cls;
            b.style.flexGrow = 1;
            b.style.height = 26;
            b.style.fontSize = 9;
            b.style.letterSpacing = 0.8f;
            _classRow.Add(b);
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────
        public static void Tick()
        {
            if (UIState.TextInputActive)
            {
                if (_open) Refresh();
                return;
            }

            // N is the registry key while seated. It is only meaningful in a cockpit, so it
            // does not need a global binding.
            var seat = GridCockpit.ActiveControlSeat;
            bool seated = seat != null && GridCockpit.ActiveControlPilot != null;

            bool pressed = GameSettings.WasPressed(InputAction.ConstructRegistry);
            if (_open && pressed) Close();
            else if (pressed && seated && !UIState.IsBlocking) Open(seat.Grid);

            if (_open && GameSettings.WasPressed(InputAction.Pause))
            {
                Close();
                UIState.PauseConsumedFrame = Time.frameCount;
            }

            if (_open) Refresh();
        }

        public static void Open(GridEntity grid)
        {
            if (grid == null || _panel == null) return;
            _target = grid;
            _open = true;
            _panel.style.display = DisplayStyle.Flex;
            if (!_blocking) { UIState.PushBlock(); _blocking = true; }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            var identity = GridIdentity.Ensure(grid);
            _nameField.SetValueWithoutNotify(identity.HasCustomName ? identity.DisplayName : "");
            Refresh();
        }

        public static void Close()
        {
            if (!_open) return;
            _open = false;
            UIState.TextInputActive = false;
            if (_panel != null) _panel.style.display = DisplayStyle.None;
            if (_blocking) { UIState.PopBlock(); _blocking = false; }
            _target = null;
        }

        // ── Refresh ──────────────────────────────────────────────────────────────
        private static void Refresh()
        {
            if (_target == null) { Close(); return; }

            var identity = GridIdentity.Ensure(_target);

            // Highlight the active classification.
            foreach (var child in _classRow.Children())
            {
                if (child is not Button b || b.userData is not GridClass cls) continue;
                bool active = identity.Class == cls;
                b.style.backgroundColor = new StyleColor(active
                    ? new Color(0.16f, 0.38f, 0.50f)
                    : new Color(0.10f, 0.13f, 0.17f));
                b.style.color = new StyleColor(active ? Color.white : T.TextSecondary);
            }

            var rails = _target.GetComponent<OrbitalRails>();
            bool onRails = rails != null && rails.IsOnRails;

            // Only a satellite or a station may be parked in orbit. A vessel is something
            // the player flies; freezing one would just be a way to lose a ship.
            bool eligible = identity.Class == GridClass.Satellite || identity.Class == GridClass.Station;

            if (onRails)
            {
                rails.Describe(out _, out string parentName, out double alt, out double apo,
                    out double peri, out double period, out double incl, out double speed);
                _orbitLabel.text =
                    $"ON RAILS around {parentName}\n" +
                    $"ALT {OrbitalTrackingService.FormatKm(alt)}   " +
                    $"AP {OrbitalTrackingService.FormatKm(apo)}   PE {OrbitalTrackingService.FormatKm(peri)}\n" +
                    $"PERIOD {OrbitalTrackingService.FormatPeriod(period)}   INC {incl:0.0}\u00b0   {speed:0} m/s";
                _orbitLabel.style.color = new StyleColor(new Color(0.35f, 0.88f, 0.52f));
                _orbitButton.text = "RELEASE FROM ORBIT";
                _orbitButton.SetEnabled(true);
                _statusLabel.text = "";
                return;
            }

            _orbitButton.text = "COMMIT TO ORBIT";

            if (!eligible)
            {
                _orbitLabel.text = "Classify this construct as a SATELLITE or STATION to commit it to orbit.";
                _orbitLabel.style.color = new StyleColor(T.TextMuted);
                _orbitButton.SetEnabled(false);
                _statusLabel.text = "";
                return;
            }

            string reason = OrbitalRails.ValidateCommit(_target);
            _orbitButton.SetEnabled(reason == null);
            if (reason == null)
            {
                _orbitLabel.text = "Ready to commit. The construct will hold this orbit permanently, " +
                                   "including while you are away.";
                _orbitLabel.style.color = new StyleColor(new Color(0.35f, 0.88f, 0.52f));
                _statusLabel.text = "";
            }
            else
            {
                _orbitLabel.text = "Cannot commit to orbit yet.";
                _orbitLabel.style.color = new StyleColor(T.TextMuted);
                _statusLabel.text = reason;
            }
        }

        private static void ToggleOrbit()
        {
            if (_target == null) return;

            var rails = _target.GetComponent<OrbitalRails>();
            if (rails != null && rails.IsOnRails)
            {
                rails.Release();
                BuildFeedbackHud.Show("Released from orbit", GridIdentity.NameOf(_target));
                Refresh();
                return;
            }

            if (rails == null) rails = _target.gameObject.AddComponent<OrbitalRails>();
            if (rails.Commit(out string reason))
            {
                BuildFeedbackHud.Show("Orbit committed", GridIdentity.NameOf(_target) + " is now on rails.");
            }
            else
            {
                _statusLabel.text = reason;
            }
            Refresh();
        }
    }
}
