// Assets/Scripts/VoxelEngine/UI/RadialWheelController.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║             INDUSTRIAL WORLD — RADIAL WHEEL BEHAVIOUR             ║
// ║                                                                  ║
// ║  The one place the feel of every radial selector is defined.     ║
// ║                                                                  ║
// ║   • Hold the key, flick, release. No confirming click.           ║
// ║   • The hardware cursor is locked and re-centred on every open,  ║
// ║     so a direction always means the same option.                 ║
// ║   • Selection is pure angle with no smoothing — overshoot the    ║
// ║     ring by a mile and the wedge you aimed at still wins.        ║
// ║   • A tap under the threshold pins the dial open to be read.     ║
// ║   • The centre is always "cancel", or whatever the owner binds.  ║
// ║                                                                  ║
// ║  Owners supply options and handle confirmation. They never touch ║
// ║  input, cursor state, UIState or layout.                         ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.FX;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.UI
{
    public sealed class RadialWheelController
    {
        /// <summary>Hold shorter than this and the dial stays pinned instead of closing.</summary>
        private const float TapSeconds = 0.20f;
        /// <summary>How often the option list is re-read while the dial is open.</summary>
        private const float RefreshSeconds = 0.2f;

        // ── Owner-supplied wiring ─────────────────────────────────────────

        /// <summary>Fills the supplied list with the current options. Called on open and periodically.</summary>
        public Action<List<RadialOption>> BuildOptions;
        /// <summary>A wedge was confirmed. Return false to refuse and keep the dial open.</summary>
        public Func<int, bool> ConfirmOption;
        /// <summary>The centre was confirmed. Optional — the default is a silent cancel.</summary>
        public Action ConfirmCentre;
        /// <summary>Tab or the scroll wheel was used while open. Optional.</summary>
        public Action SwapGroup;
        /// <summary>Resolves the root the dial is parented to. Called lazily until it returns non-null.</summary>
        public Func<VisualElement> ResolveHost;

        public string GroupLabel = string.Empty;
        public string SwapHint = string.Empty;
        public string IdleTitle = "CANCEL";
        public string IdleDescription = "Release here to keep the current selection";
        public Color IdleTitleColor = UITheme.AccentGold;
        public Texture2D IdleIcon;

        public bool IsOpen { get; private set; }
        public bool IsPinned { get; private set; }
        public int Hovered { get; private set; } = -1;

        private readonly RadialWheelInput _input = new();
        private readonly List<RadialOption> _options = new(16);
        private RadialWheelView _view;
        private InputAction _openAction = InputAction.BuildWheel;
        private bool _wasHeld;
        private float _pressTime;
        private float _nextSwap;
        private float _nextRefresh;

        // ══════════════════════════════════════════════════════════════════
        //  FRAME
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Drives the whole wheel. Call once per frame from the owner's Update.
        /// <paramref name="context"/> false closes the dial and suppresses opening —
        /// that is how "only while holding a conveyor" is expressed.
        /// </summary>
        public void Tick(InputAction openAction, bool context)
        {
            _openAction = openAction;

            if (!context)
            {
                if (IsOpen) Close(false);
                _wasHeld = false;
                return;
            }

            bool pressed = GameSettings.WasPressed(openAction);
            bool held = GameSettings.IsHeld(openAction);

            // The edge, not the level: a key already down when the context appears
            // must not pop the dial in the player's face. The second clause catches
            // a press edge eaten by a panel closing on the same frame.
            if (pressed || (held && !_wasHeld))
            {
                _pressTime = Time.unscaledTime;
                if (!IsOpen) { if (!UIState.IsBlocking) Open(); }
                else Unpin();
            }

            if (!held && _wasHeld && IsOpen && !IsPinned)
            {
                if (Hovered >= 0) Close(true);
                else if (Time.unscaledTime - _pressTime < TapSeconds) Pin();
                else Close(false);
            }
            _wasHeld = held;

            if (!IsOpen) return;

            // A blocking panel hands the cursor back to the OS on push, so the
            // lock is re-asserted every frame the dial is up.
            RadialWheelInput.HoldCursorCentred();

            _input.Sample();
            int hovered = _input.SegmentAt(_options.Count);
            if (hovered != Hovered)
            {
                Hovered = hovered;
                _view?.Refresh(Hovered);
                if (Hovered >= 0) PlayTick(UnityEngine.Random.Range(1.02f, 1.10f));
            }

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + RefreshSeconds;
                RefreshOptions();
            }

            _view?.SetAim(_input.Normalized,
                Mathf.Clamp01(_input.Deflection / Mathf.Max(0.001f, _input.Deadzone)));

            HandleSwap();
            HandleClick();
        }

        private void HandleSwap()
        {
            if (SwapGroup == null) return;
            if (Time.unscaledTime < _nextSwap) return;
            if (!TabPressed() && Mathf.Abs(ReadScroll()) <= 0.01f) return;
            _nextSwap = Time.unscaledTime + 0.18f;
            SwapGroup.Invoke();
            PlayTick(0.92f);
        }

        private void HandleClick()
        {
            if (!LeftMousePressed()) return;
            if (Hovered >= 0) { Close(true); return; }
            ConfirmCentre?.Invoke();
            Close(false);
        }

        // ══════════════════════════════════════════════════════════════════
        //  OPEN / CLOSE
        // ══════════════════════════════════════════════════════════════════

        public void Open()
        {
            if (IsOpen) return;
            var host = ResolveHost?.Invoke();
            if (host == null) return;

            IsOpen = true;
            IsPinned = false;
            Hovered = -1;
            _input.Begin();
            UIState.PushBlock();
            RadialWheelInput.HoldCursorCentred();

            // A scene reload gives the HUD a new root; the old view would then paint
            // into a panel that is no longer on screen.
            if (_view != null && _view.Host != host) { _view.Dispose(); _view = null; }
            _view ??= new RadialWheelView(host);
            _view.HidePrompt();
            ApplyCaptions();
            RefreshOptions(forceBuild: true);
            _nextRefresh = Time.unscaledTime + RefreshSeconds;

            AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiClick), 0.45f, 1.18f);
        }

        /// <summary>Closes the dial, optionally confirming whatever is lit.</summary>
        public void Close(bool confirmHovered)
        {
            if (!IsOpen) return;
            if (confirmHovered && Hovered >= 0 && ConfirmOption != null)
            {
                if (!ConfirmOption(Hovered)) return;   // refused — stay open
            }

            IsOpen = false;
            IsPinned = false;
            Hovered = -1;
            UIState.PopBlock();
            _view?.Destroy();
        }

        /// <summary>Tears everything down without touching UIState — for OnDestroy paths.</summary>
        public void Dispose()
        {
            if (IsOpen) Close(false);
            _view?.Dispose();
            _view = null;
        }

        private void Pin()
        {
            IsPinned = true;
            ApplyCaptions();
            _view?.Refresh(Hovered);
            PlayTick(0.86f);
        }

        private void Unpin()
        {
            IsPinned = false;
            ApplyCaptions();
            _view?.Refresh(Hovered);
        }

        // ══════════════════════════════════════════════════════════════════
        //  PROMPT
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Shows the "hold this key" pill. Safe to call every frame.</summary>
        public void ShowPrompt(string text)
        {
            if (IsOpen) return;
            var host = ResolveHost?.Invoke();
            if (host == null) return;
            if (_view != null && _view.Host != host) { _view.Dispose(); _view = null; }
            _view ??= new RadialWheelView(host);
            _view.ShowPrompt(text);
        }

        public void HidePrompt() => _view?.HidePrompt();

        // ══════════════════════════════════════════════════════════════════
        //  INTERNALS
        // ══════════════════════════════════════════════════════════════════

        private void RefreshOptions(bool forceBuild = false)
        {
            if (BuildOptions == null || _view == null) return;
            _options.Clear();
            BuildOptions(_options);

            if (forceBuild) _view.Build(_options);
            else _view.SetOptions(_options);

            if (Hovered >= _options.Count)
            {
                Hovered = _input.SegmentAt(_options.Count);
                _view.Refresh(Hovered);
            }
        }

        private void ApplyCaptions()
        {
            if (_view == null) return;
            _view.GroupLabel = GroupLabel;
            _view.IdleTitle = IdleTitle;
            _view.IdleDescription = IdleDescription;
            _view.IdleTitleColor = IdleTitleColor;
            _view.IdleIcon = IdleIcon;
            _view.HintLine = IsPinned
                ? "PINNED  ·  LEFT CLICK TO CONFIRM  ·  ESC TO CLOSE"
                : $"HOLD {GameSettings.GetKey(_openAction)}  ·  FLICK  ·  RELEASE";
            _view.SwapHint = SwapHint;
        }

        /// <summary>Re-reads the option list right now, for example after the owner's state changed.</summary>
        public void Invalidate()
        {
            if (!IsOpen) return;
            ApplyCaptions();
            RefreshOptions();
        }

        private static void PlayTick(float pitch)
            => AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiHover), 0.42f, pitch);

        private static bool TabPressed()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard.tabKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Tab);
#endif
        }

        private static bool LeftMousePressed()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private static float ReadScroll()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null ? mouse.scroll.ReadValue().y : 0f;
#else
            return Input.mouseScrollDelta.y;
#endif
        }
    }
}
