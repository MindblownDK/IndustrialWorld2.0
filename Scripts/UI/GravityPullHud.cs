// Assets/Scripts/VoxelEngine/UI/GravityPullHud.cs
//
// Deliberately restrained, instrument-style local-gravity telemetry for on-foot
// exploration. It reads as a fitted ship/field monitor rather than a decorative
// hologram: recessed LCD glass, practical labels, and a discrete reference meter.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;
using VoxelEngine.Player;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class GravityPullHud
    {
        private const int SurfaceSegmentCount = 8;

        // Muted phosphor palette: old practical instrumentation, not a neon hologram.
        private static readonly Color LcdGlass = new(0.105f, 0.125f, 0.075f, 0.98f);
        private static readonly Color LcdFrame = new(0.31f, 0.37f, 0.21f, 0.88f);
        private static readonly Color LcdInk = new(0.72f, 0.84f, 0.42f, 1f);
        private static readonly Color LcdOff = new(0.085f, 0.10f, 0.07f, 0.98f);

        private static VisualElement _root;
        private static VisualElement _card;
        private static VisualElement _lcdScreen;
        private static VisualElement _lcdBorder;
        private static VisualElement[] _surfaceSegments;
        private static Label _bodyLabel;
        private static Label _lcdGLabel;
        private static Label _lcdAccelerationLabel;
        private static Label _vectorLabel;
        private static Label _surfaceValueLabel;
        private static Label _surfaceStateLabel;

        private static PlayerController _player;
        private static float _nextPlayerSearchAt;
        private static float _smoothedGees;
        private static float _smoothedSurfacePull;
        private static bool _visible;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _card != null && _card.parent == uiRoot) return;

            _root = uiRoot;
            if (_card != null) _card.RemoveFromHierarchy();

            _card = new VisualElement { name = "GravityPullHud" };
            _card.style.position = Position.Absolute;
            _card.style.left = 18;
            _card.style.bottom = 18;
            _card.style.width = 196;
            _card.style.paddingLeft = 6;
            _card.style.paddingRight = 6;
            _card.style.paddingTop = 6;
            _card.style.paddingBottom = 6;
            _card.style.backgroundColor = new StyleColor(new Color(0.035f, 0.042f, 0.052f, 0.96f));
            _card.style.opacity = 0f;
            _card.style.display = DisplayStyle.None;
            _card.style.overflow = Overflow.Hidden;
            _card.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "opacity" };
            _card.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(0.16f, TimeUnit.Second) };
            _card.pickingMode = PickingMode.Ignore;
            T.Radius(_card, 3f);
            T.Border(_card, 1f, new Color(0.25f, 0.29f, 0.34f, 0.92f));
            uiRoot.Add(_card);
            LcdHudTheme.YieldWhileBlocking(_card);

            BuildHeader();
            BuildInstrumentFace();
            _visible = false;
        }

        private static void BuildHeader()
        {
            var row = new VisualElement { name = "GravityInstrumentHeader" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            row.pickingMode = PickingMode.Ignore;
            _card.Add(row);

            var title = new Label("GRAVITY FIELD");
            title.style.flexGrow = 1;
            title.style.fontSize = 8;
            title.style.letterSpacing = 1.15f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(T.TextSecondary);
            title.pickingMode = PickingMode.Ignore;
            row.Add(title);

            var module = new Label("MON-01");
            module.style.fontSize = 7;
            module.style.letterSpacing = 0.8f;
            module.style.unityFontStyleAndWeight = FontStyle.Bold;
            module.style.color = new StyleColor(T.TextMuted);
            module.pickingMode = PickingMode.Ignore;
            row.Add(module);
        }

        private static void BuildInstrumentFace()
        {
            var main = new VisualElement { name = "GravityInstrumentFace" };
            main.style.flexDirection = FlexDirection.Row;
            main.style.alignItems = Align.Stretch;
            main.pickingMode = PickingMode.Ignore;
            _card.Add(main);

            BuildLcd(main);
            BuildReferenceColumn(main);
        }

        private static void BuildLcd(VisualElement parent)
        {
            _lcdBorder = new VisualElement { name = "GravityLcdBezel" };
            _lcdBorder.style.width = 84;
            _lcdBorder.style.height = 50;
            _lcdBorder.style.paddingLeft = 4;
            _lcdBorder.style.paddingRight = 4;
            _lcdBorder.style.paddingTop = 3;
            _lcdBorder.style.paddingBottom = 3;
            _lcdBorder.style.backgroundColor = new StyleColor(new Color(0.018f, 0.022f, 0.019f, 0.98f));
            _lcdBorder.pickingMode = PickingMode.Ignore;
            T.Radius(_lcdBorder, 2f);
            T.Border(_lcdBorder, 1f, LcdFrame);
            parent.Add(_lcdBorder);

            _lcdScreen = new VisualElement { name = "GravityLcdScreen" };
            _lcdScreen.style.flexGrow = 1;
            _lcdScreen.style.backgroundColor = new StyleColor(LcdGlass);
            _lcdScreen.style.overflow = Overflow.Hidden;
            _lcdScreen.pickingMode = PickingMode.Ignore;
            T.Radius(_lcdScreen, 1f);
            _lcdBorder.Add(_lcdScreen);

            // Subtle horizontal scan lines make this look like a physical LCD without
            // relying on an external texture or a generic glow effect.
            for (int i = 0; i < 4; i++)
            {
                var line = new VisualElement();
                line.style.position = Position.Absolute;
                line.style.left = 2;
                line.style.right = 2;
                line.style.top = 5 + i * 9;
                line.style.height = 1;
                line.style.backgroundColor = new StyleColor(new Color(0.77f, 0.88f, 0.48f, 0.055f));
                line.pickingMode = PickingMode.Ignore;
                _lcdScreen.Add(line);
            }

            var caption = new Label("LOCAL PULL");
            caption.style.marginLeft = 3;
            caption.style.marginTop = 2;
            caption.style.fontSize = 6;
            caption.style.letterSpacing = 1.1f;
            caption.style.unityFontStyleAndWeight = FontStyle.Bold;
            caption.style.color = new StyleColor(new Color(LcdInk.r, LcdInk.g, LcdInk.b, 0.72f));
            caption.pickingMode = PickingMode.Ignore;
            _lcdScreen.Add(caption);

            _lcdGLabel = new Label("1.00G");
            _lcdGLabel.style.marginLeft = 3;
            _lcdGLabel.style.marginTop = -1;
            _lcdGLabel.style.fontSize = 16;
            _lcdGLabel.style.letterSpacing = 0.7f;
            _lcdGLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _lcdGLabel.style.color = new StyleColor(LcdInk);
            _lcdGLabel.pickingMode = PickingMode.Ignore;
            _lcdScreen.Add(_lcdGLabel);

            _lcdAccelerationLabel = new Label("09.81 m/s²");
            _lcdAccelerationLabel.style.marginLeft = 3;
            _lcdAccelerationLabel.style.marginTop = -2;
            _lcdAccelerationLabel.style.fontSize = 7;
            _lcdAccelerationLabel.style.letterSpacing = 0.6f;
            _lcdAccelerationLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _lcdAccelerationLabel.style.color = new StyleColor(new Color(LcdInk.r, LcdInk.g, LcdInk.b, 0.82f));
            _lcdAccelerationLabel.pickingMode = PickingMode.Ignore;
            _lcdScreen.Add(_lcdAccelerationLabel);
        }

        private static void BuildReferenceColumn(VisualElement parent)
        {
            var column = new VisualElement { name = "GravityReferenceColumn" };
            column.style.flexGrow = 1;
            column.style.marginLeft = 6;
            column.pickingMode = PickingMode.Ignore;
            parent.Add(column);

            var bodyCaption = SmallCaption("BODY");
            column.Add(bodyCaption);

            _bodyLabel = new Label("LOCAL FIELD");
            _bodyLabel.style.fontSize = 9;
            _bodyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _bodyLabel.style.color = new StyleColor(T.TextPrimary);
            _bodyLabel.style.marginTop = 0;
            _bodyLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _bodyLabel.style.overflow = Overflow.Hidden;
            _bodyLabel.style.textOverflow = TextOverflow.Ellipsis;
            _bodyLabel.pickingMode = PickingMode.Ignore;
            column.Add(_bodyLabel);

            // VECTOR row removed in 7.13.5 — wasted vertical space on screen; the
            // surface-reference segments below carry the useful information.
            var vectorCaption = SmallCaption("VECTOR");
            vectorCaption.style.marginTop = 3;
            vectorCaption.style.display = DisplayStyle.None;
            column.Add(vectorCaption);

            _vectorLabel = new Label("COREWARD");
            _vectorLabel.style.fontSize = 9;
            _vectorLabel.style.letterSpacing = 0.7f;
            _vectorLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _vectorLabel.style.color = new StyleColor(T.TextSecondary);
            _vectorLabel.style.display = DisplayStyle.None;
            _vectorLabel.pickingMode = PickingMode.Ignore;
            column.Add(_vectorLabel);

            var surfaceRow = new VisualElement { name = "GravitySurfaceReference" };
            surfaceRow.style.flexDirection = FlexDirection.Row;
            surfaceRow.style.alignItems = Align.Center;
            surfaceRow.style.marginTop = 3;
            surfaceRow.pickingMode = PickingMode.Ignore;
            column.Add(surfaceRow);

            var surfaceCaption = SmallCaption("SFC REF");
            surfaceCaption.style.flexGrow = 1;
            surfaceRow.Add(surfaceCaption);

            _surfaceValueLabel = new Label("100%");
            _surfaceValueLabel.style.fontSize = 10;
            _surfaceValueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _surfaceValueLabel.style.color = new StyleColor(LcdInk);
            _surfaceValueLabel.pickingMode = PickingMode.Ignore;
            surfaceRow.Add(_surfaceValueLabel);

            var segmentTrack = new VisualElement { name = "GravitySurfaceSegments" };
            segmentTrack.style.flexDirection = FlexDirection.Row;
            segmentTrack.style.height = 8;
            segmentTrack.style.marginTop = 2;
            segmentTrack.style.paddingLeft = 1;
            segmentTrack.style.paddingRight = 1;
            segmentTrack.style.paddingTop = 1;
            segmentTrack.style.paddingBottom = 1;
            segmentTrack.style.backgroundColor = new StyleColor(new Color(0.018f, 0.022f, 0.019f, 0.98f));
            segmentTrack.pickingMode = PickingMode.Ignore;
            T.Radius(segmentTrack, 1f);
            T.Border(segmentTrack, 1f, new Color(LcdFrame.r, LcdFrame.g, LcdFrame.b, 0.72f));
            column.Add(segmentTrack);

            _surfaceSegments = new VisualElement[SurfaceSegmentCount];
            for (int i = 0; i < SurfaceSegmentCount; i++)
            {
                var segment = new VisualElement { name = "GravitySegment" + i };
                segment.style.flexGrow = 1;
                segment.style.marginRight = i < SurfaceSegmentCount - 1 ? 1 : 0;
                segment.style.backgroundColor = new StyleColor(LcdOff);
                segment.pickingMode = PickingMode.Ignore;
                T.Radius(segment, 1f);
                _surfaceSegments[i] = segment;
                segmentTrack.Add(segment);
            }

            _surfaceStateLabel = new Label("AT SURFACE REFERENCE");
            _surfaceStateLabel.style.marginTop = 2;
            _surfaceStateLabel.style.fontSize = 6;
            _surfaceStateLabel.style.letterSpacing = 0.65f;
            _surfaceStateLabel.style.color = new StyleColor(T.TextMuted);
            _surfaceStateLabel.pickingMode = PickingMode.Ignore;
            column.Add(_surfaceStateLabel);
        }

        private static Label SmallCaption(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 7;
            label.style.letterSpacing = 1.1f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(T.TextMuted);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        public static void Tick()
        {
            if (_card == null) return;
            if (UIState.IsBlocking || GridCockpit.AnyPilotSeatActive)
            {
                SetVisible(false);
                return;
            }

            var player = ResolvePlayer();
            if (player == null)
            {
                SetVisible(false);
                return;
            }

            GravityFieldSample gravity = GravityProvider.Sample(player.transform.position);
            float smooth = 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime);
            _smoothedGees = Mathf.Lerp(_smoothedGees, gravity.Gees, smooth);
            _smoothedSurfacePull = Mathf.Lerp(_smoothedSurfacePull, gravity.SurfaceFraction, smooth);

            if (!_visible) SetVisible(true);
            ApplyReadout(gravity, _smoothedGees, _smoothedSurfacePull);
        }

        private static PlayerController ResolvePlayer()
        {
            if (_player != null) return _player;
            if (Time.unscaledTime < _nextPlayerSearchAt) return null;
            _nextPlayerSearchAt = Time.unscaledTime + 0.5f;
            _player = Object.FindAnyObjectByType<PlayerController>();
            return _player;
        }

        private static void ApplyReadout(GravityFieldSample gravity, float gees, float surfacePull)
        {
            Color ink = ResolveLcdInk(gravity);
            var body = GravityProvider.ActiveBody;
            string bodyName = body != null ? body.DisplayName.ToUpperInvariant() : "LOCAL FIELD";
            string direction = gravity.IsRadial ? "COREWARD" : "DOWNWARD";
            int litSegments = Mathf.Clamp(Mathf.RoundToInt(surfacePull * SurfaceSegmentCount), 0, SurfaceSegmentCount);

            _bodyLabel.text = bodyName;
            _lcdGLabel.text = $"{gees:0.00}G";
            _lcdAccelerationLabel.text = $"{gravity.Magnitude:00.00} m/s²";
            _vectorLabel.text = direction;
            _surfaceValueLabel.text = gravity.IsRadial ? $"{surfacePull * 100f:0}%" : "100%";
            _surfaceStateLabel.text = gravity.IsRadial
                ? (surfacePull >= 0.995f ? "AT SURFACE REFERENCE" : "RELATIVE SURFACE PULL")
                : "FLAT FIELD REFERENCE";

            _lcdGLabel.style.color = new StyleColor(ink);
            _lcdAccelerationLabel.style.color = new StyleColor(new Color(ink.r, ink.g, ink.b, 0.84f));
            _surfaceValueLabel.style.color = new StyleColor(ink);
            T.Border(_lcdBorder, 1f, new Color(ink.r, ink.g, ink.b, 0.70f));
            T.Border(_card, 1f, new Color(ink.r, ink.g, ink.b, 0.35f));

            for (int i = 0; i < _surfaceSegments.Length; i++)
            {
                var segment = _surfaceSegments[i];
                if (segment == null) continue;
                segment.style.backgroundColor = new StyleColor(i < litSegments
                    ? new Color(ink.r, ink.g, ink.b, 0.90f)
                    : LcdOff);
            }
        }

        private static Color ResolveLcdInk(GravityFieldSample gravity)
        {
            if (gravity.Gees >= 1.75f) return new Color(0.98f, 0.71f, 0.24f);
            if (gravity.Gees <= 0.20f || gravity.SurfaceFraction <= 0.15f) return new Color(0.45f, 0.74f, 0.90f);
            if (gravity.Gees <= 0.70f || gravity.SurfaceFraction <= 0.50f) return new Color(0.56f, 0.82f, 0.72f);
            return LcdInk;
        }

        private static void SetVisible(bool visible)
        {
            if (_card == null || _visible == visible) return;
            _visible = visible;
            if (visible)
            {
                _card.style.display = DisplayStyle.Flex;
                _card.style.opacity = 1f;
            }
            else
            {
                _card.style.opacity = 0f;
                _card.style.display = DisplayStyle.None;
            }
        }
    }
}
