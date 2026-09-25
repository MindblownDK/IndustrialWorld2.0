// Assets/Scripts/VoxelEngine/UI/ConfirmDialogHud.cs
//
// Player-facing binary choice as a two-wedge radial, the same language as the
// Hammer build wheel. Used for warp jumps (and anything else that cannot decide
// alone). One dialog at a time.
//
// 12.30.0-dev: Input System only (never UnityEngine.Input). Ctrl frees the mouse
// for the drive-count slider; A/D still pick wedges while the look is locked.

using System;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VoxelEngine.UI
{
    public static class ConfirmDialogHud
    {
        private const int RingSize = 256;
        private const float WheelPx = 520f;
        private const float RingInset = 20f;
        private const float InnerR = 78f;
        private const float OuterR = 128f;
        private const float GapDeg = 3.5f;

        private static VisualElement _root;
        private static VisualElement _overlay;
        private static VisualElement _wheel;
        private static VisualElement _ringElement;
        private static VisualElement _driveRow;
        private static Label _titleLabel;
        private static Label _detailLabel;
        private static Label _hintLabel;
        private static Label _driveLabel;
        private static Label _acceptLabel;
        private static Label _abortLabel;
        private static SliderInt _driveSlider;
        private static Texture2D _ringTexture;
        private static Action _onAccept;
        private static Action<int> _onDriveUseChanged;
        private static Func<int, string> _detailForDriveUse;
        private static bool _pushedBlock;
        private static bool _mouseFree;
        private static int _hovered = -1; // 0 accept, 1 abort, -1 none
        private static IVisualElementScheduledItem _tick;

        public static bool IsOpen { get; private set; }
        public static bool MouseFree => IsOpen && _mouseFree;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _overlay != null && _overlay.parent == uiRoot) return;
            if (_pushedBlock)
            {
                UIState.PopBlock();
                _pushedBlock = false;
            }
            _root = uiRoot;
            if (_overlay != null) _overlay.RemoveFromHierarchy();
            ReleaseRingTexture();
            IsOpen = false;
            _onAccept = null;

            _overlay = new VisualElement { name = "ConfirmDialogHud" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.top = 0;
            _overlay.style.right = 0;
            _overlay.style.bottom = 0;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.backgroundColor = new StyleColor(new Color(0.01f, 0.012f, 0.018f, 0.82f));
            _overlay.style.display = DisplayStyle.None;
            _overlay.pickingMode = PickingMode.Position;
            uiRoot.Add(_overlay);

            _wheel = new VisualElement { name = "ConfirmWheel" };
            _wheel.style.width = WheelPx;
            _wheel.style.height = WheelPx;
            _wheel.style.position = Position.Relative;
            float safeScale = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 640f, 0.55f, 1f);
            _wheel.style.scale = new StyleScale(new Scale(new Vector3(safeScale, safeScale, 1f)));
            _overlay.Add(_wheel);

            _ringElement = new VisualElement { name = "ConfirmRing" };
            _ringElement.style.position = Position.Absolute;
            _ringElement.style.left = RingInset;
            _ringElement.style.top = RingInset;
            _ringElement.style.width = WheelPx - RingInset * 2f;
            _ringElement.style.height = WheelPx - RingInset * 2f;
            _ringElement.pickingMode = PickingMode.Ignore;
            _wheel.Add(_ringElement);

            var disc = new VisualElement();
            disc.style.position = Position.Absolute;
            disc.style.left = 115;
            disc.style.top = 115;
            disc.style.width = 290;
            disc.style.height = 290;
            disc.style.alignItems = Align.Center;
            disc.style.justifyContent = Justify.Center;
            disc.style.backgroundColor = new StyleColor(new Color(0.04f, 0.05f, 0.07f, 1f));
            disc.style.paddingLeft = 18;
            disc.style.paddingRight = 18;
            disc.pickingMode = PickingMode.Position;
            UITheme.Radius(disc, 145f);
            UITheme.Border(disc, 2f, UITheme.BorderBright);
            disc.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            _wheel.Add(disc);

            _titleLabel = new Label("CONFIRM");
            _titleLabel.style.fontSize = 16;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.letterSpacing = 1.6f;
            _titleLabel.style.color = new StyleColor(Color.white);
            _titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _titleLabel.style.whiteSpace = WhiteSpace.Normal;
            _titleLabel.pickingMode = PickingMode.Ignore;
            disc.Add(_titleLabel);

            _detailLabel = new Label();
            _detailLabel.style.marginTop = 8;
            _detailLabel.style.fontSize = 11;
            _detailLabel.style.color = new StyleColor(new Color(0.85f, 0.88f, 0.92f));
            _detailLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _detailLabel.style.whiteSpace = WhiteSpace.Normal;
            _detailLabel.pickingMode = PickingMode.Ignore;
            disc.Add(_detailLabel);

            _driveRow = new VisualElement();
            _driveRow.style.marginTop = 10;
            _driveRow.style.width = 230;
            _driveRow.style.flexDirection = FlexDirection.Column;
            _driveRow.style.alignItems = Align.Stretch;
            _driveRow.pickingMode = PickingMode.Position;
            _driveRow.RegisterCallback<PointerDownEvent>(evt => evt.StopImmediatePropagation());
            _driveRow.RegisterCallback<PointerMoveEvent>(evt => evt.StopPropagation());

            _driveLabel = new Label("DRIVES");
            _driveLabel.style.fontSize = 10;
            _driveLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _driveLabel.style.letterSpacing = 1.1f;
            _driveLabel.style.color = new StyleColor(Color.white);
            _driveLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _driveLabel.pickingMode = PickingMode.Ignore;
            _driveRow.Add(_driveLabel);

            _driveSlider = new SliderInt(1, 1);
            _driveSlider.style.marginTop = 4;
            _driveSlider.style.height = 18;
            _driveSlider.RegisterValueChangedCallback(evt =>
            {
                if (!IsOpen) return;
                int v = evt.newValue;
                _onDriveUseChanged?.Invoke(v);
                RefreshDriveCaption(v, _driveSlider.highValue);
                if (_detailForDriveUse != null && _detailLabel != null)
                    _detailLabel.text = _detailForDriveUse(v) ?? "";
            });
            _driveRow.Add(_driveSlider);
            disc.Add(_driveRow);

            _hintLabel = new Label("point a wedge and click  ·  Ctrl locks look  ·  Esc abort");
            _hintLabel.style.marginTop = 10;
            _hintLabel.style.fontSize = 9;
            _hintLabel.style.color = new StyleColor(UITheme.TextMuted);
            _hintLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _hintLabel.style.whiteSpace = WhiteSpace.Normal;
            _hintLabel.pickingMode = PickingMode.Ignore;
            disc.Add(_hintLabel);

            _acceptLabel = MakeWedgeLabel("JUMP", true);
            _abortLabel = MakeWedgeLabel("ABORT", false);
            _wheel.Add(_acceptLabel);
            _wheel.Add(_abortLabel);

            _overlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (!IsOpen) return;
                evt.StopPropagation();
                if (evt.button == 1) { Hide(); return; }
                if (evt.button != 0) return;
                if (_hovered == 1) Hide();
                else if (_hovered == 0) Accept();
            });

            _tick = _overlay.schedule.Execute(Tick).Every(16);
            _tick.Pause();
        }

        private static Label MakeWedgeLabel(string text, bool acceptSide)
        {
            var lab = new Label(text);
            lab.style.position = Position.Absolute;
            lab.style.top = 236;
            if (acceptSide) { lab.style.right = 36; lab.style.left = StyleKeyword.Auto; }
            else { lab.style.left = 28; lab.style.right = StyleKeyword.Auto; }
            lab.style.fontSize = 13;
            lab.style.unityFontStyleAndWeight = FontStyle.Bold;
            lab.style.letterSpacing = 1.4f;
            lab.style.color = new StyleColor(Color.white);
            lab.pickingMode = PickingMode.Ignore;
            return lab;
        }

        public static void Show(string title, string detail, string acceptText, string abortText, Action onAccept)
            => Show(title, detail, acceptText, abortText, onAccept, 0, 0, null, null);

        public static void Show(string title, string detail, string acceptText, string abortText, Action onAccept,
            int driveMax, int driveUse, Action<int> onDriveUseChanged, Func<int, string> detailForDriveUse)
        {
            if (_overlay == null) return;
            if (_titleLabel != null) _titleLabel.text = string.IsNullOrEmpty(title) ? "CONFIRM" : title.ToUpperInvariant();
            if (_detailLabel != null) _detailLabel.text = detail ?? "";
            if (_acceptLabel != null) _acceptLabel.text = string.IsNullOrEmpty(acceptText) ? "JUMP" : acceptText.ToUpperInvariant();
            if (_abortLabel != null) _abortLabel.text = string.IsNullOrEmpty(abortText) ? "ABORT" : abortText.ToUpperInvariant();
            _onAccept = onAccept;
            _onDriveUseChanged = onDriveUseChanged;
            _detailForDriveUse = detailForDriveUse;
            _hovered = -1;
            _mouseFree = true;
            ConfigureDriveSlider(driveMax, driveUse);
            if (!IsOpen && !_pushedBlock)
            {
                UIState.PushBlock();
                _pushedBlock = true;
            }
            IsOpen = true;
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.BringToFront();
            ApplyCursor();
            PaintRing();
            PaintLabels();
            _tick?.Resume();
        }

        public static void Hide()
        {
            if (!IsOpen && !_pushedBlock)
            {
                if (_overlay != null) _overlay.style.display = DisplayStyle.None;
                return;
            }
            IsOpen = false;
            _onAccept = null;
            _onDriveUseChanged = null;
            _detailForDriveUse = null;
            _mouseFree = false;
            _tick?.Pause();
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
            if (_pushedBlock)
            {
                UIState.PopBlock();
                _pushedBlock = false;
            }
        }

        private static void ConfigureDriveSlider(int driveMax, int driveUse)
        {
            bool show = driveMax >= 2 && _driveSlider != null;
            if (_driveRow != null)
                _driveRow.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            _driveSlider.lowValue = 1;
            _driveSlider.highValue = driveMax;
            int v = Mathf.Clamp(driveUse > 0 ? driveUse : driveMax, 1, driveMax);
            _driveSlider.SetValueWithoutNotify(v);
            RefreshDriveCaption(v, driveMax);
        }

        private static void RefreshDriveCaption(int use, int max)
        {
            if (_driveLabel != null)
                _driveLabel.text = $"DRIVES  {use} / {max}";
        }

        private static void Accept()
        {
            var cb = _onAccept;
            Hide();
            try { cb?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void Tick()
        {
            if (!IsOpen) return;
            if (CtrlPressedThisFrame())
            {
                _mouseFree = !_mouseFree;
                ApplyCursor();
                if (_hintLabel != null)
                    _hintLabel.text = _mouseFree
                        ? "point a wedge and click  ·  drag the slider  ·  Ctrl locks look  ·  Esc"
                        : "A / D pick a wedge  ·  Ctrl frees mouse  ·  Enter  ·  Esc abort";
            }
            ApplyCursor();
            if (_mouseFree) PollMouseHover();
            PollSteerKeys();

            if (GameSettings.WasPressed(InputAction.Pause))
            {
                UIState.PauseConsumedFrame = Time.frameCount;
                Hide();
                return;
            }
            if (ConfirmKeyPressed())
            {
                if (_hovered == 1) Hide();
                else if (_hovered == 0) Accept();
            }
        }

        private static void ApplyCursor()
        {
            if (!IsOpen) return;
            if (_mouseFree)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
            }
            else
            {
                UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                UnityEngine.Cursor.visible = false;
            }
        }

        private static void PollMouseHover()
        {
            Vector2 mouse;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var m = Mouse.current;
            mouse = m != null ? m.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#else
            mouse = Input.mousePosition;
#endif
            float dx = mouse.x - Screen.width * 0.5f;
            if (dx > 28f) SetHovered(0);
            else if (dx < -28f) SetHovered(1);
        }

        private static void PollSteerKeys()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) SetHovered(1);
            if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) SetHovered(0);
#else
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) SetHovered(1);
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) SetHovered(0);
#endif
        }

        private static bool ConfirmKeyPressed()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            return kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
#endif
        }

        private static bool CtrlPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            return kb.leftCtrlKey.wasPressedThisFrame || kb.rightCtrlKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl);
#endif
        }

        private static void SetHovered(int segment)
        {
            if (segment == _hovered) return;
            _hovered = segment;
            PaintRing();
            PaintLabels();
        }

        private static void PaintLabels()
        {
            if (_acceptLabel != null)
            {
                bool on = _hovered == 0;
                _acceptLabel.style.color = new StyleColor(Color.white);
                _acceptLabel.style.scale = new StyleScale(new Scale(on ? new Vector3(1.06f, 1.06f, 1f) : Vector3.one));
            }
            if (_abortLabel != null)
            {
                bool on = _hovered == 1;
                _abortLabel.style.color = new StyleColor(Color.white);
                _abortLabel.style.scale = new StyleScale(new Scale(on ? new Vector3(1.06f, 1.06f, 1f) : Vector3.one));
            }
        }

        private static void PaintRing()
        {
            if (_ringTexture == null)
            {
                _ringTexture = new Texture2D(RingSize, RingSize, TextureFormat.RGBA32, false)
                {
                    name = "ConfirmDialogRing",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            var pixels = new Color32[RingSize * RingSize];
            float center = (RingSize - 1) * 0.5f;
            Color32 idle = new Color32(36, 42, 52, 255);
            Color32 acceptHot = new Color32(32, 110, 62, 255);
            Color32 abortHot = new Color32(140, 38, 34, 255);
            Color32 rim = new Color32(210, 214, 220, 220);

            for (int y = 0; y < RingSize; y++)
            {
                for (int x = 0; x < RingSize; x++)
                {
                    float dx = x - center;
                    float dy = center - y;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    if (radius < InnerR || radius > OuterR) continue;
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    if (Mathf.Abs(Mathf.Abs(angle) - 90f) < GapDeg) continue;
                    int seg = angle >= -90f && angle <= 90f ? 0 : 1;
                    bool hovered = seg == _hovered;
                    Color32 color = hovered
                        ? (seg == 0 ? acceptHot : abortHot)
                        : idle;
                    float edge = Mathf.Min(radius - InnerR, OuterR - radius);
                    float alphaFade = Mathf.Clamp01(edge / 8f);
                    color.a = (byte)Mathf.RoundToInt(255 * alphaFade * (hovered ? 1f : 0.96f));
                    if (edge < 4f) color = new Color32(rim.r, rim.g, rim.b, (byte)(color.a * 0.7f));
                    pixels[y * RingSize + x] = color;
                }
            }

            _ringTexture.SetPixels32(pixels);
            _ringTexture.Apply(false, false);
            if (_ringElement != null)
                _ringElement.style.backgroundImage = new StyleBackground(_ringTexture);
        }

        private static void ReleaseRingTexture()
        {
            if (_ringTexture == null) return;
            UnityEngine.Object.Destroy(_ringTexture);
            _ringTexture = null;
        }
    }
}
