// Assets/Scripts/VoxelEngine/UI/RadialWheelView.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║            INDUSTRIAL WORLD — RADIAL WHEEL PRESENTER              ║
// ║                                                                  ║
// ║  The single visual definition of every radial selector in the    ║
// ║  game: the build dial, the conveyor shapes, the grid shapes, the ║
// ║  energy pipes, the road surfaces and the jump drive all draw     ║
// ║  through this one class. One palette, one geometry table, one    ║
// ║  animation curve — change it here and every wheel follows.       ║
// ║                                                                  ║
// ║  It renders and nothing else: no input, no game state. The owner ║
// ║  hands it a list of options and an index, and it paints them.    ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    /// <summary>One wedge's worth of presentable state.</summary>
    public struct RadialOption
    {
        public string Title;
        public string Description;
        public Texture2D Icon;
        /// <summary>Requirement lines for the hub, for example "50 x Wood  (985,703)".</summary>
        public string[] Details;
        /// <summary>Per-line satisfaction; drives the green/red colour of each detail line.</summary>
        public bool[] DetailsOk;
        /// <summary>False paints a dark locked wedge that refuses selection.</summary>
        public bool Available;
        /// <summary>False dims the wedge — offerable, but the player cannot pay for it.</summary>
        public bool Affordable;
        /// <summary>True marks the wedge the owner currently has armed.</summary>
        public bool Selected;

        public static RadialOption Simple(string title, string description, Texture2D icon)
            => new() { Title = title, Description = description, Icon = icon, Available = true, Affordable = true };
    }

    /// <summary>
    /// Builds and updates the dial. Lives inside a host root supplied by the owner,
    /// so several wheels can share one UIDocument without clearing each other away.
    /// </summary>
    public sealed class RadialWheelView
    {
        // ── Geometry, in design pixels. One table for the whole game. ──────
        public const float WheelSize    = 620f;
        private const float RingInner   = 206f;
        private const float RingOuter   = 286f;
        private const float HairOuter   = 302f;
        private const float HairInner   = 192f;
        private const float IconRadius  = 246f;
        private const float IconBox     = 58f;
        private const float HubSize     = 360f;
        private const float PointerReach = 194f;
        private const float LeanPixels   = 17f;

        // ── Palette: warm bone dial, hot iron-oxide highlight ─────────────
        public static readonly Color DialBone     = new(0.914f, 0.898f, 0.855f, 0.97f);
        public static readonly Color DialBoneWeak = new(0.914f, 0.898f, 0.855f, 0.34f);
        public static readonly Color DialLocked   = new(0.20f, 0.21f, 0.23f, 0.60f);
        public static readonly Color DialHot      = new(0.804f, 0.259f, 0.184f, 1.00f);
        public static readonly Color DialChosen   = new(0.478f, 0.161f, 0.122f, 0.96f);
        private static readonly Color DialHotGlow = new(0.804f, 0.259f, 0.184f, 0.20f);
        private static readonly Color IconIdle    = new(0.757f, 0.243f, 0.173f, 1.00f);
        private static readonly Color IconHot     = new(1.000f, 0.976f, 0.949f, 1.00f);
        private static readonly Color IconChosen  = new(0.980f, 0.882f, 0.855f, 1.00f);
        private static readonly Color IconBroke   = new(0.36f, 0.35f, 0.34f, 0.85f);
        private static readonly Color HubFace     = new(0.035f, 0.040f, 0.052f, 0.93f);
        private static readonly Color Backdrop    = new(0.010f, 0.012f, 0.018f, 0.58f);

        private readonly VisualElement _host;
        private readonly List<RadialOption> _options = new(16);
        private readonly List<VisualElement> _iconSlots = new(16);
        private readonly List<Label> _detailLines = new(4);

        private VisualElement _layer;
        private VisualElement _dial;
        private RadialRing _ring;
        private VisualElement _pointer;
        private VisualElement _hubIcon;
        private Label _hubGroup;
        private Label _hubTitle;
        private Label _hubDesc;
        private VisualElement _hubDetails;
        private Label _hubHint;
        private Label _hubSwapHint;
        private VisualElement _prompt;
        private Label _promptLabel;

        private int _hovered = -1;
        private float _fit = 1f;

        /// <summary>The root this view was created against. Used to detect a stale HUD.</summary>
        public VisualElement Host => _host;

        public bool IsBuilt => _layer != null && _layer.parent != null;
        public int Count => _options.Count;

        /// <summary>Caption above the hub icon, for example the family group.</summary>
        public string GroupLabel = string.Empty;
        /// <summary>Bottom line of the hub: the control contract.</summary>
        public string HintLine = string.Empty;
        /// <summary>Second bottom line, for a group swap or a secondary control.</summary>
        public string SwapHint = string.Empty;
        /// <summary>Hub content when the pointer sits in the deadzone and nothing is armed.</summary>
        public string IdleTitle = "CANCEL";
        public string IdleDescription = "Release here to keep the current selection";
        public Color IdleTitleColor = UITheme.AccentGold;
        public Texture2D IdleIcon;

        public RadialWheelView(VisualElement host) => _host = host;

        // ══════════════════════════════════════════════════════════════════
        //  BUILD / TEARDOWN
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Creates the dial for the supplied options and animates it in.</summary>
        public void Build(IReadOnlyList<RadialOption> options)
        {
            if (_host == null) return;
            Destroy();

            _options.Clear();
            for (int i = 0; i < options.Count; i++) _options.Add(options[i]);
            _iconSlots.Clear();
            _detailLines.Clear();

            _fit = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 760f, 0.52f, 1f);

            _layer = new VisualElement { name = "RadialWheelLayer" };
            _layer.style.position = Position.Absolute;
            _layer.style.left = 0; _layer.style.top = 0;
            _layer.style.right = 0; _layer.style.bottom = 0;
            _layer.style.alignItems = Align.Center;
            _layer.style.justifyContent = Justify.Center;
            _layer.style.backgroundColor = new StyleColor(Backdrop);
            // Nothing here is clickable: the cursor is locked while a wheel is up,
            // so every interaction runs through the virtual pointer.
            _layer.pickingMode = PickingMode.Ignore;
            _host.Add(_layer);

            _dial = new VisualElement { name = "RadialDial" };
            _dial.style.width = WheelSize;
            _dial.style.height = WheelSize;
            _dial.pickingMode = PickingMode.Ignore;
            _layer.Add(_dial);

            BuildRing();
            BuildHub();
            for (int i = 0; i < _options.Count; i++) BuildIconSlot(i);
            BuildPointer();

            // Fast, confident entry. The selection is already live on frame one;
            // this is decoration and must finish before a human can react.
            _dial.style.transitionProperty = new List<StylePropertyName> { "scale", "opacity" };
            _dial.style.transitionDuration = new List<TimeValue>
                { new(0.085f, TimeUnit.Second), new(0.085f, TimeUnit.Second) };
            _dial.style.transitionTimingFunction = new List<EasingFunction>
                { new(EasingMode.EaseOutCubic), new(EasingMode.EaseOutCubic) };
            _dial.style.scale = new StyleScale(new Scale(new Vector3(_fit * 0.9f, _fit * 0.9f, 1f)));
            _dial.style.opacity = 0f;
            _dial.schedule.Execute(() =>
            {
                if (_dial == null) return;
                _dial.style.scale = new StyleScale(new Scale(new Vector3(_fit, _fit, 1f)));
                _dial.style.opacity = 1f;
            }).ExecuteLater(1);

            Refresh(_hovered);
        }

        /// <summary>Removes the dial and the prompt and forgets every element.</summary>
        public void Dispose()
        {
            Destroy();
            RemovePrompt();
        }

        public void Destroy()
        {
            _layer?.RemoveFromHierarchy();
            _layer = null;
            _dial = null;
            _ring = null;
            _pointer = null;
            _hubIcon = null;
            _hubGroup = null;
            _hubTitle = null;
            _hubDesc = null;
            _hubDetails = null;
            _hubHint = null;
            _hubSwapHint = null;
            _iconSlots.Clear();
            _detailLines.Clear();
            _options.Clear();
        }

        private void BuildRing()
        {
            _ring = new RadialRing
            {
                name = "RadialDialRing",
                SegmentCount = _options.Count,
                InnerRadius = RingInner,
                OuterRadius = RingOuter,
                HairlineOuterRadius = HairOuter,
                HairlineInnerRadius = HairInner,
                HairlineColor = new Color(DialBone.r, DialBone.g, DialBone.b, 0.22f),
                BackdropColor = new Color(0.015f, 0.018f, 0.024f, 0.72f),
                GapPixels = 7f,
                ExpandOuter = 15f,
                ExpandInner = 9f
            };
            _ring.style.position = Position.Absolute;
            _ring.style.left = 0; _ring.style.top = 0;
            _ring.style.width = WheelSize;
            _ring.style.height = WheelSize;
            _ring.WedgeProvider = DescribeWedge;
            _dial.Add(_ring);
        }

        private RadialWedge DescribeWedge(int index)
        {
            var wedge = new RadialWedge { RimWidth = 0f };
            if (index < 0 || index >= _options.Count) return wedge;
            var option = _options[index];

            if (!option.Available)
            {
                wedge.Fill = DialLocked;
                return wedge;
            }

            if (index == _hovered)
            {
                wedge.Fill = DialHot;
                wedge.Rim = new Color(1f, 0.92f, 0.88f, 0.55f);
                wedge.RimWidth = 1.8f;
                wedge.Expanded = true;
                wedge.Glow = DialHotGlow;
            }
            else if (option.Selected)
            {
                wedge.Fill = DialChosen;
                wedge.Rim = new Color(DialHot.r, DialHot.g, DialHot.b, 0.8f);
                wedge.RimWidth = 1.6f;
            }
            else
            {
                wedge.Fill = option.Affordable ? DialBone : DialBoneWeak;
            }
            return wedge;
        }

        private void BuildIconSlot(int index)
        {
            float angle = RadialWheelInput.SegmentAngle(index, _options.Count) * Mathf.Deg2Rad;
            // UI space is Y-down, so 12 o'clock is -cos.
            float x = WheelSize * 0.5f + Mathf.Sin(angle) * IconRadius;
            float y = WheelSize * 0.5f - Mathf.Cos(angle) * IconRadius;

            var slot = new VisualElement();
            slot.style.position = Position.Absolute;
            slot.style.left = x - IconBox * 0.5f;
            slot.style.top = y - IconBox * 0.5f;
            slot.style.width = IconBox;
            slot.style.height = IconBox;
            slot.pickingMode = PickingMode.Ignore;
            if (_options[index].Icon != null)
                slot.style.backgroundImage = new StyleBackground(_options[index].Icon);
            slot.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            slot.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            slot.style.transitionProperty = new List<StylePropertyName> { "scale" };
            slot.style.transitionDuration = new List<TimeValue> { new(0.07f, TimeUnit.Second) };
            slot.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseOutCubic) };
            _dial.Add(slot);
            _iconSlots.Add(slot);
        }

        private void BuildPointer()
        {
            _pointer = new VisualElement { name = "RadialPointer" };
            _pointer.style.position = Position.Absolute;
            _pointer.style.width = 13;
            _pointer.style.height = 13;
            _pointer.style.left = WheelSize * 0.5f - 6.5f;
            _pointer.style.top = WheelSize * 0.5f - 6.5f;
            _pointer.style.backgroundColor = new StyleColor(new Color(DialHot.r, DialHot.g, DialHot.b, 0.9f));
            _pointer.pickingMode = PickingMode.Ignore;
            UITheme.Radius(_pointer, 6.5f);
            UITheme.Border(_pointer, 1.5f, new Color(1f, 0.95f, 0.92f, 0.55f));
            _pointer.style.opacity = 0f;
            _dial.Add(_pointer);
        }

        private void BuildHub()
        {
            var hub = new VisualElement { name = "RadialHub" };
            hub.style.position = Position.Absolute;
            hub.style.left = (WheelSize - HubSize) * 0.5f;
            hub.style.top = (WheelSize - HubSize) * 0.5f;
            hub.style.width = HubSize;
            hub.style.height = HubSize;
            hub.style.alignItems = Align.Center;
            hub.style.justifyContent = Justify.Center;
            hub.style.paddingLeft = hub.style.paddingRight = 34;
            hub.style.backgroundColor = new StyleColor(HubFace);
            hub.pickingMode = PickingMode.Ignore;
            UITheme.Radius(hub, HubSize * 0.5f);
            UITheme.Border(hub, 1.5f, new Color(DialBone.r, DialBone.g, DialBone.b, 0.14f));
            _dial.Add(hub);

            _hubGroup = Caption(hub, 9, new Color(DialBone.r, DialBone.g, DialBone.b, 0.45f));
            _hubGroup.style.marginBottom = 10;

            _hubIcon = new VisualElement();
            _hubIcon.style.width = 74;
            _hubIcon.style.height = 74;
            _hubIcon.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            _hubIcon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            _hubIcon.pickingMode = PickingMode.Ignore;
            hub.Add(_hubIcon);

            _hubTitle = new Label();
            _hubTitle.style.fontSize = 23;
            _hubTitle.style.marginTop = 8;
            _hubTitle.style.letterSpacing = 1.5f;
            _hubTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _hubTitle.style.color = new StyleColor(new Color(0.98f, 0.97f, 0.95f));
            _hubTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _hubTitle.style.whiteSpace = WhiteSpace.NoWrap;
            _hubTitle.pickingMode = PickingMode.Ignore;
            hub.Add(_hubTitle);

            _hubDesc = new Label();
            _hubDesc.style.fontSize = 11;
            _hubDesc.style.marginTop = 4;
            _hubDesc.style.maxWidth = HubSize - 90f;
            _hubDesc.style.color = new StyleColor(new Color(0.62f, 0.62f, 0.60f));
            _hubDesc.style.unityTextAlign = TextAnchor.MiddleCenter;
            _hubDesc.style.whiteSpace = WhiteSpace.Normal;
            _hubDesc.pickingMode = PickingMode.Ignore;
            hub.Add(_hubDesc);

            _hubDetails = new VisualElement();
            _hubDetails.style.marginTop = 12;
            _hubDetails.style.alignItems = Align.Center;
            _hubDetails.pickingMode = PickingMode.Ignore;
            hub.Add(_hubDetails);

            for (int i = 0; i < 4; i++)
            {
                var line = new Label();
                line.style.fontSize = 12;
                line.style.unityFontStyleAndWeight = FontStyle.Bold;
                line.style.unityTextAlign = TextAnchor.MiddleCenter;
                line.style.whiteSpace = WhiteSpace.NoWrap;
                line.style.display = DisplayStyle.None;
                line.pickingMode = PickingMode.Ignore;
                _hubDetails.Add(line);
                _detailLines.Add(line);
            }

            _hubHint = Caption(hub, 9, new Color(DialBone.r, DialBone.g, DialBone.b, 0.32f));
            _hubHint.style.marginTop = 14;

            _hubSwapHint = Caption(hub, 9, new Color(DialBone.r, DialBone.g, DialBone.b, 0.22f));
            _hubSwapHint.style.marginTop = 3;
        }

        private static Label Caption(VisualElement parent, int size, Color color)
        {
            var label = new Label();
            label.style.fontSize = size;
            label.style.letterSpacing = 1.4f;
            label.style.color = new StyleColor(color);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.pickingMode = PickingMode.Ignore;
            parent.Add(label);
            return label;
        }

        // ══════════════════════════════════════════════════════════════════
        //  LIVE UPDATE
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Replaces the option data in place. Rebuilds only if the count changed.</summary>
        public void SetOptions(IReadOnlyList<RadialOption> options)
        {
            if (!IsBuilt) { Build(options); return; }
            if (options.Count != _options.Count) { Build(options); return; }

            for (int i = 0; i < options.Count; i++)
            {
                _options[i] = options[i];
                if (i < _iconSlots.Count && options[i].Icon != null)
                    _iconSlots[i].style.backgroundImage = new StyleBackground(options[i].Icon);
            }
            Refresh(_hovered);
        }

        /// <summary>Repaints wedges, icon tints and the hub for the given hovered index.</summary>
        public void Refresh(int hovered)
        {
            _hovered = hovered;
            if (!IsBuilt) return;

            _ring?.Repaint();

            for (int i = 0; i < _iconSlots.Count && i < _options.Count; i++)
            {
                var option = _options[i];
                bool hot = i == _hovered;
                Color tint = !option.Available ? IconBroke
                    : hot ? IconHot
                    : option.Selected ? IconChosen
                    : option.Affordable ? IconIdle
                    : new Color(IconIdle.r, IconIdle.g, IconIdle.b, 0.35f);

                _iconSlots[i].style.unityBackgroundImageTintColor = new StyleColor(tint);
                _iconSlots[i].style.scale = new StyleScale(new Scale(
                    hot ? new Vector3(1.22f, 1.22f, 1f) : Vector3.one));
            }

            RefreshHub();
        }

        private void RefreshHub()
        {
            if (_hubTitle == null) return;

            _hubGroup.text = GroupLabel ?? string.Empty;
            _hubHint.text = HintLine ?? string.Empty;
            _hubSwapHint.text = SwapHint ?? string.Empty;

            bool hasHover = _hovered >= 0 && _hovered < _options.Count;
            if (!hasHover)
            {
                SetHubIcon(IdleIcon, IdleTitleColor);
                _hubTitle.text = IdleTitle ?? string.Empty;
                _hubTitle.style.color = new StyleColor(IdleTitleColor);
                _hubDesc.text = IdleDescription ?? string.Empty;
                SetDetails(null, null);
                return;
            }

            var option = _options[_hovered];
            SetHubIcon(option.Icon, option.Available ? DialHot : IconBroke);
            _hubTitle.text = option.Title ?? string.Empty;
            _hubTitle.style.color = new StyleColor(new Color(0.98f, 0.97f, 0.95f));
            _hubDesc.text = option.Description ?? string.Empty;
            SetDetails(option.Details, option.DetailsOk);
        }

        private void SetHubIcon(Texture2D icon, Color tint)
        {
            if (_hubIcon == null) return;
            _hubIcon.style.backgroundImage = icon != null
                ? new StyleBackground(icon)
                : new StyleBackground(StyleKeyword.None);
            _hubIcon.style.unityBackgroundImageTintColor = new StyleColor(tint);
        }

        private void SetDetails(string[] lines, bool[] ok)
        {
            for (int i = 0; i < _detailLines.Count; i++)
            {
                bool show = lines != null && i < lines.Length && !string.IsNullOrEmpty(lines[i]);
                _detailLines[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                _detailLines[i].text = lines[i];
                bool good = ok == null || i >= ok.Length || ok[i];
                _detailLines[i].style.color = new StyleColor(good ? UITheme.AccentGreen : UITheme.AccentRed);
            }
        }

        /// <summary>
        /// Leans the whole dial toward the aim and rides the pointer bead out to the
        /// groove, reaching it exactly as the deadzone is left. The lean is small and
        /// undamped: it must read as the dial acknowledging the hand, never as drift
        /// the hand then has to chase.
        /// </summary>
        public void SetAim(Vector2 normalized, float engage)
        {
            if (!IsBuilt) return;

            _dial.style.translate = new StyleTranslate(new Translate(
                new Length(normalized.x * LeanPixels, LengthUnit.Pixel),
                new Length(-normalized.y * LeanPixels, LengthUnit.Pixel), 0f));

            Vector2 dir = normalized.sqrMagnitude > 1e-8f ? normalized.normalized : Vector2.up;
            float radius = PointerReach * engage;
            _pointer.style.translate = new StyleTranslate(new Translate(
                new Length(dir.x * radius, LengthUnit.Pixel),
                new Length(-dir.y * radius, LengthUnit.Pixel), 0f));
            _pointer.style.opacity = engage * 0.9f;
        }

        // ══════════════════════════════════════════════════════════════════
        //  PROMPT — the "hold this key" pill shown before the wheel opens
        // ══════════════════════════════════════════════════════════════════

        public void ShowPrompt(string text)
        {
            if (_host == null) return;
            if (_prompt == null)
            {
                _prompt = new VisualElement { name = "RadialWheelPrompt" };
                _prompt.style.position = Position.Absolute;
                _prompt.style.left = 0;
                _prompt.style.right = 0;
                _prompt.style.bottom = 82;
                _prompt.style.alignItems = Align.Center;
                _prompt.pickingMode = PickingMode.Ignore;

                var pill = new VisualElement();
                pill.style.height = 30;
                pill.style.paddingLeft = 14;
                pill.style.paddingRight = 14;
                pill.style.flexDirection = FlexDirection.Row;
                pill.style.alignItems = Align.Center;
                pill.style.justifyContent = Justify.Center;
                pill.style.backgroundColor = new StyleColor(new Color(0.035f, 0.045f, 0.065f, 0.94f));
                pill.pickingMode = PickingMode.Ignore;
                UITheme.Radius(pill, 15f);
                UITheme.Border(pill, 1f, new Color(DialBone.r, DialBone.g, DialBone.b, 0.28f));
                _prompt.Add(pill);

                _promptLabel = new Label();
                _promptLabel.style.fontSize = 11;
                _promptLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                _promptLabel.style.color = new StyleColor(UITheme.TextPrimary);
                _promptLabel.style.letterSpacing = 0.8f;
                _promptLabel.pickingMode = PickingMode.Ignore;
                pill.Add(_promptLabel);
            }

            if (_prompt.parent == null) _host.Add(_prompt);
            _prompt.style.display = DisplayStyle.Flex;
            _promptLabel.text = text;
        }

        public void HidePrompt()
        {
            if (_prompt != null) _prompt.style.display = DisplayStyle.None;
        }

        public void RemovePrompt()
        {
            _prompt?.RemoveFromHierarchy();
            _prompt = null;
            _promptLabel = null;
        }
    }
}
