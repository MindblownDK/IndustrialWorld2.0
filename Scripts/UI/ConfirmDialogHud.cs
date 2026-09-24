// Assets/Scripts/VoxelEngine/UI/ConfirmDialogHud.cs
//
// Player-facing binary choice as a two-wedge radial, the same language as the
// Hammer build wheel: unlock the cursor, point at a wedge, click. Enter/Space
// takes the accept wedge, Esc / right-click aborts. Used for partial warp jumps
// (and anything else that cannot decide alone). One dialog at a time.
//
// 12.28.2-dev: the previous card sat under a locked cockpit cursor, so neither
// button could be reached. The wheel PushBlock's the UI so look frees, and the
// wedges read from mouse position the same way the hammer ring does.

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
        private static Label _titleLabel;
        private static Label _detailLabel;
        private static Label _acceptLabel;
        private static Label _abortLabel;
        private static Texture2D _ringTexture;
        private static Action _onAccept;
        private static bool _pushedBlock;
        private static int _hovered = 0; // 0 accept, 1 abort, -1 none
        private static IVisualElementScheduledItem _tick;

        public static bool IsOpen { get; private set; }

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
            disc.style.backgroundColor = new StyleColor(new Color(0.025f, 0.035f, 0.055f, 0.99f));
            disc.style.paddingLeft = 18;
            disc.style.paddingRight = 18;
            disc.pickingMode = PickingMode.Ignore;
            UITheme.Radius(disc, 145f);
            UITheme.Border(disc, 2f, UITheme.BorderBright);
            _wheel.Add(disc);

            _titleLabel = new Label("CONFIRM");
            _titleLabel.style.fontSize = 16;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.letterSpacing = 1.6f;
            _titleLabel.style.color = new StyleColor(UITheme.AccentCyan);
            _titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _titleLabel.style.whiteSpace = WhiteSpace.Normal;
            _titleLabel.pickingMode = PickingMode.Ignore;
            disc.Add(_titleLabel);

            _detailLabel = new Label();
            _detailLabel.style.marginTop = 8;
            _detailLabel.style.fontSize = 11;
            _detailLabel.style.color = new StyleColor(UITheme.TextPrimary);
            _detailLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _detailLabel.style.whiteSpace = WhiteSpace.Normal;
            _detailLabel.pickingMode = PickingMode.Ignore;
            disc.Add(_detailLabel);

            var hint = new Label("point a wedge  ·  click / Enter  ·  Esc abort");
            hint.style.marginTop = 10;
            hint.style.fontSize = 9;
            hint.style.color = new StyleColor(UITheme.TextMuted);
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            hint.pickingMode = PickingMode.Ignore;
            disc.Add(hint);

            _acceptLabel = MakeWedgeLabel("JUMP", true);
            _abortLabel = MakeWedgeLabel("ABORT", false);
            _wheel.Add(_acceptLabel);
            _wheel.Add(_abortLabel);

            _overlay.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!IsOpen) return;
                SetHovered(SegmentAt(_wheel.WorldToLocal(evt.position)));
            });
            _overlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (!IsOpen) return;
                evt.StopPropagation();
                if (evt.button == 1) { Hide(); return; }
                if (evt.button != 0) return;
                int seg = SegmentAt(_wheel.WorldToLocal(evt.position));
                if (seg == 1) Hide();
                else if (seg == 0 || seg < 0) Accept();
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
        {
            if (_overlay == null) return;
            if (_titleLabel != null) _titleLabel.text = string.IsNullOrEmpty(title) ? "CONFIRM" : title.ToUpperInvariant();
            if (_detailLabel != null) _detailLabel.text = detail ?? "";
            if (_acceptLabel != null) _acceptLabel.text = string.IsNullOrEmpty(acceptText) ? "JUMP" : acceptText.ToUpperInvariant();
            if (_abortLabel != null) _abortLabel.text = string.IsNullOrEmpty(abortText) ? "ABORT" : abortText.ToUpperInvariant();
            _onAccept = onAccept;
            _hovered = 0;
            if (!IsOpen && !_pushedBlock)
            {
                UIState.PushBlock();
                _pushedBlock = true;
            }
            IsOpen = true;
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.BringToFront();
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
            _tick?.Pause();
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
            if (_pushedBlock)
            {
                UIState.PopBlock();
                _pushedBlock = false;
            }
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

            if (GameSettings.WasPressed(InputAction.Pause))
            {
                UIState.PauseConsumedFrame = Time.frameCount;
                Hide();
                return;
            }
            if (ConfirmKeyPressed())
            {
                if (_hovered == 1) Hide();
                else Accept();
            }
        }

        private static bool ConfirmKeyPressed()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                return true;
#endif
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
        }

        private static void SetHovered(int segment)
        {
            if (segment == _hovered) return;
            _hovered = segment;
            PaintRing();
            PaintLabels();
        }

        private static int SegmentAt(Vector2 local)
        {
            Vector2 delta = local - new Vector2(WheelPx * 0.5f, WheelPx * 0.5f);
            float radius = delta.magnitude;
            float innerPx = InnerR / OuterR * ((WheelPx - RingInset * 2f) * 0.5f);
            float outerPx = (WheelPx - RingInset * 2f) * 0.5f;
            if (radius < innerPx * 0.85f) return 0; // centre disc = accept
            if (radius > outerPx + 12f) return -1;
            // UI y grows down; flip so +x is right, +y is up.
            float angle = Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.Abs(angle) - 90f) < GapDeg) return -1;
            return angle >= -90f && angle <= 90f ? 0 : 1;
        }

        private static void PaintLabels()
        {
            if (_acceptLabel != null)
            {
                bool on = _hovered == 0;
                _acceptLabel.style.color = new StyleColor(on ? UITheme.AccentGreen : Color.white);
                _acceptLabel.style.scale = new StyleScale(new Scale(on ? new Vector3(1.06f, 1.06f, 1f) : Vector3.one));
            }
            if (_abortLabel != null)
            {
                bool on = _hovered == 1;
                _abortLabel.style.color = new StyleColor(on ? UITheme.AccentRed : Color.white);
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
            Color32 cream = new Color32(245, 242, 232, 245);
            Color32 acceptHot = new Color32(55, 190, 105, 255);
            Color32 abortHot = new Color32(215, 55, 45, 255);
            Color32 rim = new Color32(180, 175, 160, 180);

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
                        : cream;
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
