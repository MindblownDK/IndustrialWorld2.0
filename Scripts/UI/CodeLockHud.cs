// Assets/Scripts/VoxelEngine/UI/CodeLockHud.cs
//
// 13.17.0-dev: the code lock keypad and its owner menu.
//
// Three faces, one panel: SET CODE (green header) when a fresh lock gets its
// combination, ENTER CODE (red header) when a stranger works the keypad, and a
// small owner menu (lock / unlock / change code / remove) for anyone already
// authorized. Four digits, auto-submit on the last one. A wrong code stings for
// 5 HP. Built entirely in UI Toolkit like every other HUD in the game.

using System;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace VoxelEngine.UI
{
    public static class CodeLockHud
    {
        private static readonly Color PanelBg   = new(0.075f, 0.085f, 0.075f, 0.97f);
        private static readonly Color KeyBg     = new(0.155f, 0.175f, 0.155f, 1f);
        private static readonly Color KeyHover  = new(0.235f, 0.265f, 0.225f, 1f);
        private static readonly Color KeyText   = new(0.90f, 0.93f, 0.88f, 1f);
        private static readonly Color HeaderSet = new(0.42f, 0.60f, 0.20f, 1f);
        private static readonly Color HeaderAsk = new(0.70f, 0.22f, 0.15f, 1f);
        private static readonly Color DeniedRed = new(0.85f, 0.25f, 0.18f, 1f);

        private static VisualElement _root;
        private static VisualElement _overlay;
        private static VisualElement _keypadPanel;
        private static VisualElement _menuPanel;
        private static Label _header;
        private static Label _display;
        private static Label _menuStatus;
        private static Label _menuLockButton;

        private static VoxelEngine.Building.Tiered.CodeLock _target;
        private static VoxelEngine.Player.PlayerStats _stats;
        private static VoxelEngine.Items.Inventory _inventory;
        private static Action _onSuccess;
        private static Action _onSetDone;
        private static string _digits = "";
        private static bool _setMode;
        private static bool _pushedBlock;

        public static bool IsOpen { get; private set; }

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _overlay != null && _overlay.parent == uiRoot) return;
            if (_pushedBlock) { UIState.PopBlock(); _pushedBlock = false; }
            _root = uiRoot;
            if (_overlay != null) _overlay.RemoveFromHierarchy();
            IsOpen = false;
            _target = null;

            _overlay = new VisualElement { name = "CodeLockHud" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0; _overlay.style.top = 0;
            _overlay.style.right = 0; _overlay.style.bottom = 0;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.backgroundColor = new StyleColor(new Color(0.01f, 0.012f, 0.018f, 0.72f));
            _overlay.style.display = DisplayStyle.None;
            _overlay.pickingMode = PickingMode.Position;
            _overlay.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == _overlay) Hide();   // click beside the panel closes
            });
            uiRoot.Add(_overlay);

            BuildKeypad();
            BuildMenu();

            // Physical keys work alongside the on-screen pad: digits on the
            // top row and the numpad, backspace erases, escape closes.
            _overlay.schedule.Execute(PollKeyboard).Every(30);
        }

        // ─────────────────────────── public faces ───────────────────────────

        /// <summary>Green keypad: author a fresh (or replacement) combination.
        /// onDone fires after the code is set - e.g. to open the door behind it.</summary>
        public static void ShowSet(VoxelEngine.Building.Tiered.CodeLock target, Action onDone = null)
        {
            if (_overlay == null || target == null) return;
            _target = target; _setMode = true; _onSuccess = null; _onSetDone = onDone;
            _header.text = "SET NEW CODE";
            _header.style.backgroundColor = HeaderSet;
            OpenKeypad();
        }

        /// <summary>Red keypad: prove yourself. Success authorizes and fires onSuccess.</summary>
        public static void ShowEnter(VoxelEngine.Building.Tiered.CodeLock target,
            VoxelEngine.Player.PlayerStats stats, Action onSuccess)
        {
            if (_overlay == null || target == null) return;
            _target = target; _setMode = false; _stats = stats; _onSuccess = onSuccess;
            _header.text = "ENTER CODE";
            _header.style.backgroundColor = HeaderAsk;
            OpenKeypad();
        }

        /// <summary>Owner menu for an already-authorized player.</summary>
        public static void ShowMenu(VoxelEngine.Building.Tiered.CodeLock target,
            VoxelEngine.Items.Inventory inventory)
        {
            if (_overlay == null || target == null) return;
            _target = target; _inventory = inventory;
            RefreshMenu();
            _keypadPanel.style.display = DisplayStyle.None;
            _menuPanel.style.display = DisplayStyle.Flex;
            Open();
        }

        public static void Hide()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _overlay.style.display = DisplayStyle.None;
            if (_pushedBlock) { UIState.PopBlock(); _pushedBlock = false; }
            _target = null; _stats = null; _inventory = null;
            _onSuccess = null; _onSetDone = null;
        }

        // ─────────────────────────── internals ───────────────────────────

        private static void OpenKeypad()
        {
            _digits = "";
            UpdateDisplay(false);
            _menuPanel.style.display = DisplayStyle.None;
            _keypadPanel.style.display = DisplayStyle.Flex;
            Open();
        }

        private static void Open()
        {
            _overlay.style.display = DisplayStyle.Flex;
            if (!IsOpen)
            {
                IsOpen = true;
                if (!_pushedBlock) { UIState.PushBlock(); _pushedBlock = true; }
            }
        }

        private static void Press(char digit)
        {
            if (_digits.Length >= 4) return;
            _digits += digit;
            UpdateDisplay(false);
            if (_digits.Length == 4) Submit();
        }

        private static void Submit()
        {
            if (_target == null) { Hide(); return; }
            if (_setMode)
            {
                _target.ApplyCode(_digits, VoxelEngine.Networking.PlayerIdentity.LocalId);
                BuildFeedbackHud.Show("Code Lock", "Code set - locked", null,
                    new Color(0.55f, 0.80f, 0.35f));
                var done = _onSetDone;
                Hide();
                done?.Invoke();
                return;
            }
            if (_target.TryEnter(_digits, VoxelEngine.Networking.PlayerIdentity.LocalId))
            {
                BuildFeedbackHud.Show("Code Lock", "Access granted", null,
                    new Color(0.55f, 0.80f, 0.35f));
                var success = _onSuccess;
                Hide();
                success?.Invoke();
                return;
            }
            // Wrong code: the pad bites back, Rust-style.
            if (_stats != null) _stats.TakeDamage(5f);
            BuildFeedbackHud.Show("Code Lock", "Wrong code", null,
                new Color(0.95f, 0.35f, 0.25f));
            _digits = "";
            UpdateDisplay(true);
        }

        private static void PollKeyboard()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            if (!IsOpen) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                UIState.PauseConsumedFrame = Time.frameCount;
                Hide();
                return;
            }
            if (_keypadPanel == null || _keypadPanel.resolvedStyle.display == DisplayStyle.None) return;
            if (keyboard.backspaceKey.wasPressedThisFrame && _digits.Length > 0)
            {
                _digits = _digits.Substring(0, _digits.Length - 1);
                UpdateDisplay(false);
            }
            if (Tapped(keyboard.digit0Key, keyboard.numpad0Key)) Press('0');
            if (Tapped(keyboard.digit1Key, keyboard.numpad1Key)) Press('1');
            if (Tapped(keyboard.digit2Key, keyboard.numpad2Key)) Press('2');
            if (Tapped(keyboard.digit3Key, keyboard.numpad3Key)) Press('3');
            if (Tapped(keyboard.digit4Key, keyboard.numpad4Key)) Press('4');
            if (Tapped(keyboard.digit5Key, keyboard.numpad5Key)) Press('5');
            if (Tapped(keyboard.digit6Key, keyboard.numpad6Key)) Press('6');
            if (Tapped(keyboard.digit7Key, keyboard.numpad7Key)) Press('7');
            if (Tapped(keyboard.digit8Key, keyboard.numpad8Key)) Press('8');
            if (Tapped(keyboard.digit9Key, keyboard.numpad9Key)) Press('9');
#endif
        }

#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
        private static bool Tapped(KeyControl main, KeyControl pad)
            => (main != null && main.wasPressedThisFrame)
            || (pad != null && pad.wasPressedThisFrame);
#endif

        private static void UpdateDisplay(bool denied)
        {
            string shown = "";
            for (int i = 0; i < 4; i++)
                shown += i < _digits.Length ? _digits[i].ToString() : "_";
            _display.text = $"{shown[0]} {shown[1]} {shown[2]} {shown[3]}";
            _display.style.color = denied ? DeniedRed : KeyText;
        }

        private static void RefreshMenu()
        {
            bool locked = _target != null && _target.isLocked;
            _menuStatus.text = locked ? "STATUS: LOCKED" : "STATUS: UNLOCKED";
            _menuStatus.style.color = locked
                ? new Color(0.95f, 0.45f, 0.35f) : new Color(0.60f, 0.85f, 0.45f);
            _menuLockButton.text = locked ? "UNLOCK" : "LOCK";
        }

        // ─────────────────────────── construction ───────────────────────────

        private static void BuildKeypad()
        {
            _keypadPanel = MakePanel(300f);

            _header = new Label("ENTER CODE");
            _header.style.unityTextAlign = TextAnchor.MiddleCenter;
            _header.style.unityFontStyleAndWeight = FontStyle.Bold;
            _header.style.fontSize = 19;
            _header.style.color = Color.white;
            _header.style.height = 42;
            _header.style.backgroundColor = HeaderAsk;
            _header.style.marginBottom = 10;
            _keypadPanel.Add(_header);

            _display = new Label("_ _ _ _");
            _display.style.unityTextAlign = TextAnchor.MiddleCenter;
            _display.style.unityFontStyleAndWeight = FontStyle.Bold;
            _display.style.fontSize = 30;
            _display.style.color = KeyText;
            _display.style.height = 52;
            _display.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
            _display.style.marginLeft = 14; _display.style.marginRight = 14;
            _display.style.marginBottom = 10;
            _keypadPanel.Add(_display);

            string[][] rows =
            {
                new[] { "1", "2", "3" },
                new[] { "4", "5", "6" },
                new[] { "7", "8", "9" },
                new[] { "C", "0", "X" },
            };
            foreach (var rowKeys in rows)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.Center;
                _keypadPanel.Add(row);
                foreach (var keyLabel in rowKeys)
                {
                    string captured = keyLabel;
                    var key = MakeButton(keyLabel, 82f, 52f, () =>
                    {
                        switch (captured)
                        {
                            case "C": _digits = ""; UpdateDisplay(false); break;
                            case "X": Hide(); break;
                            default: Press(captured[0]); break;
                        }
                    });
                    row.Add(key);
                }
            }
        }

        private static void BuildMenu()
        {
            _menuPanel = MakePanel(260f);

            var title = new Label("CODE LOCK");
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 19;
            title.style.color = Color.white;
            title.style.height = 42;
            title.style.backgroundColor = new StyleColor(new Color(0.22f, 0.26f, 0.22f, 1f));
            title.style.marginBottom = 8;
            _menuPanel.Add(title);

            _menuStatus = new Label("STATUS: LOCKED");
            _menuStatus.style.unityTextAlign = TextAnchor.MiddleCenter;
            _menuStatus.style.fontSize = 13;
            _menuStatus.style.marginBottom = 8;
            _menuPanel.Add(_menuStatus);

            _menuLockButton = MakeButton("UNLOCK", 220f, 42f, () =>
            {
                if (_target == null) { Hide(); return; }
                _target.SetLockedState(!_target.isLocked);
                RefreshMenu();
            });
            _menuPanel.Add(Center(_menuLockButton));

            _menuPanel.Add(Center(MakeButton("CHANGE CODE", 220f, 42f, () =>
            {
                var target = _target;
                Hide();
                ShowSet(target);
            })));

            _menuPanel.Add(Center(MakeButton("REMOVE LOCK", 220f, 42f, () =>
            {
                var target = _target;
                var inventory = _inventory;
                Hide();
                if (target != null)
                {
                    target.RemoveAndRefund(inventory);
                    BuildFeedbackHud.Show("Code Lock", "Lock removed", null,
                        new Color(0.85f, 0.75f, 0.35f));
                }
            })));

            _menuPanel.Add(Center(MakeButton("CLOSE", 220f, 42f, Hide)));
        }

        private static VisualElement MakePanel(float width)
        {
            var panel = new VisualElement();
            panel.style.width = width;
            panel.style.backgroundColor = PanelBg;
            panel.style.paddingBottom = 14;
            panel.style.borderTopLeftRadius = 8; panel.style.borderTopRightRadius = 8;
            panel.style.borderBottomLeftRadius = 8; panel.style.borderBottomRightRadius = 8;
            panel.style.borderTopWidth = 1; panel.style.borderBottomWidth = 1;
            panel.style.borderLeftWidth = 1; panel.style.borderRightWidth = 1;
            var edge = new Color(0.35f, 0.40f, 0.33f, 0.9f);
            panel.style.borderTopColor = edge; panel.style.borderBottomColor = edge;
            panel.style.borderLeftColor = edge; panel.style.borderRightColor = edge;
            panel.style.display = DisplayStyle.None;
            _overlay.Add(panel);
            return panel;
        }

        private static Label MakeButton(string text, float width, float height, Action onClick)
        {
            var button = new Label(text);
            button.style.width = width;
            button.style.height = height;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.fontSize = 20;
            button.style.color = KeyText;
            button.style.backgroundColor = KeyBg;
            button.style.marginLeft = 4; button.style.marginRight = 4;
            button.style.marginTop = 4; button.style.marginBottom = 4;
            button.style.borderTopLeftRadius = 5; button.style.borderTopRightRadius = 5;
            button.style.borderBottomLeftRadius = 5; button.style.borderBottomRightRadius = 5;
            button.pickingMode = PickingMode.Position;
            button.RegisterCallback<MouseEnterEvent>(_ => button.style.backgroundColor = KeyHover);
            button.RegisterCallback<MouseLeaveEvent>(_ => button.style.backgroundColor = KeyBg);
            button.RegisterCallback<ClickEvent>(_ => onClick());
            return button;
        }

        private static VisualElement Center(VisualElement child)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.Center;
            row.Add(child);
            return row;
        }
    }
}
