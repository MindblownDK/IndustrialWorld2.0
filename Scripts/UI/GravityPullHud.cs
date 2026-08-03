// Assets/Scripts/VoxelEngine/UI/GravityPullHud.cs
//
// Premium local-gravity telemetry for on-foot exploration. The cockpit gets an
// integrated companion module in GridPilotHud, while this card owns the bottom-
// left anchor when the player is on foot.

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
        private static VisualElement _root;
        private static VisualElement _card;
        private static VisualElement _accentLine;
        private static VisualElement _pulseDot;
        private static VisualElement _orb;
        private static VisualElement _meterFill;
        private static Label _bodyLabel;
        private static Label _pullArrow;
        private static Label _gLabel;
        private static Label _accelerationLabel;
        private static Label _directionLabel;
        private static Label _surfaceLabel;

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
            _card.style.width = 254;
            _card.style.paddingLeft = 13;
            _card.style.paddingRight = 13;
            _card.style.paddingTop = 10;
            _card.style.paddingBottom = 10;
            _card.style.backgroundColor = new StyleColor(new Color(0.025f, 0.032f, 0.052f, 0.94f));
            _card.style.opacity = 0f;
            _card.style.display = DisplayStyle.None;
            _card.style.overflow = Overflow.Hidden;
            _card.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "opacity", "translate" };
            _card.style.transitionDuration = new System.Collections.Generic.List<TimeValue>
            {
                new(0.18f, TimeUnit.Second),
                new(0.18f, TimeUnit.Second)
            };
            _card.pickingMode = PickingMode.Ignore;
            T.Radius(_card, 10f);
            T.Border(_card, 1f, new Color(T.AccentPurple.r, T.AccentPurple.g, T.AccentPurple.b, 0.58f));
            uiRoot.Add(_card);

            _accentLine = new VisualElement { name = "GravityAccent" };
            _accentLine.style.position = Position.Absolute;
            _accentLine.style.left = 0;
            _accentLine.style.top = 0;
            _accentLine.style.bottom = 0;
            _accentLine.style.width = 3;
            _accentLine.style.backgroundColor = new StyleColor(T.AccentPurple);
            _accentLine.pickingMode = PickingMode.Ignore;
            _card.Add(_accentLine);

            BuildHeader();
            BuildReadout();
            BuildSurfaceMeter();
            _visible = false;
        }

        private static void BuildHeader()
        {
            var row = new VisualElement { name = "GravityHeader" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 7;
            row.pickingMode = PickingMode.Ignore;
            _card.Add(row);

            _pulseDot = new VisualElement { name = "GravityPulse" };
            _pulseDot.style.width = 7;
            _pulseDot.style.height = 7;
            _pulseDot.style.marginRight = 7;
            _pulseDot.style.backgroundColor = new StyleColor(T.AccentPurple);
            _pulseDot.pickingMode = PickingMode.Ignore;
            T.Radius(_pulseDot, 4f);
            row.Add(_pulseDot);

            var title = new Label("GRAVITY PULL");
            title.style.flexGrow = 1;
            title.style.fontSize = 10;
            title.style.letterSpacing = 1.45f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(T.TextSecondary);
            title.pickingMode = PickingMode.Ignore;
            row.Add(title);

            _bodyLabel = new Label("LOCAL FIELD");
            _bodyLabel.style.fontSize = 8;
            _bodyLabel.style.letterSpacing = 0.75f;
            _bodyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _bodyLabel.style.color = new StyleColor(T.TextMuted);
            _bodyLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _bodyLabel.pickingMode = PickingMode.Ignore;
            row.Add(_bodyLabel);
        }

        private static void BuildReadout()
        {
            var row = new VisualElement { name = "GravityReadout" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.pickingMode = PickingMode.Ignore;
            _card.Add(row);

            _orb = new VisualElement { name = "GravityOrb" };
            _orb.style.width = 46;
            _orb.style.height = 46;
            _orb.style.marginRight = 11;
            _orb.style.backgroundColor = new StyleColor(new Color(T.AccentPurple.r, T.AccentPurple.g, T.AccentPurple.b, 0.16f));
            _orb.pickingMode = PickingMode.Ignore;
            T.Radius(_orb, 23f);
            T.Border(_orb, 1f, new Color(T.AccentPurple.r, T.AccentPurple.g, T.AccentPurple.b, 0.75f));
            row.Add(_orb);

            var innerRing = new VisualElement();
            innerRing.style.position = Position.Absolute;
            innerRing.style.left = 7;
            innerRing.style.top = 7;
            innerRing.style.width = 30;
            innerRing.style.height = 30;
            innerRing.style.borderLeftWidth = innerRing.style.borderRightWidth = 1;
            innerRing.style.borderTopWidth = innerRing.style.borderBottomWidth = 1;
            var ringColor = new StyleColor(new Color(T.AccentPurple.r, T.AccentPurple.g, T.AccentPurple.b, 0.32f));
            innerRing.style.borderLeftColor = ringColor;
            innerRing.style.borderRightColor = ringColor;
            innerRing.style.borderTopColor = ringColor;
            innerRing.style.borderBottomColor = ringColor;
            innerRing.pickingMode = PickingMode.Ignore;
            T.Radius(innerRing, 15f);
            _orb.Add(innerRing);

            _pullArrow = new Label("↓");
            _pullArrow.style.position = Position.Absolute;
            _pullArrow.style.left = 0;
            _pullArrow.style.right = 0;
            _pullArrow.style.top = 0;
            _pullArrow.style.bottom = 1;
            _pullArrow.style.fontSize = 25;
            _pullArrow.style.unityFontStyleAndWeight = FontStyle.Bold;
            _pullArrow.style.unityTextAlign = TextAnchor.MiddleCenter;
            _pullArrow.style.color = new StyleColor(T.AccentPurple);
            _pullArrow.pickingMode = PickingMode.Ignore;
            _orb.Add(_pullArrow);

            var column = new VisualElement();
            column.style.flexGrow = 1;
            column.pickingMode = PickingMode.Ignore;
            row.Add(column);

            _gLabel = new Label("1.00 G");
            _gLabel.style.fontSize = 24;
            _gLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _gLabel.style.letterSpacing = 0.5f;
            _gLabel.style.color = new StyleColor(T.AccentPurple);
            _gLabel.pickingMode = PickingMode.Ignore;
            column.Add(_gLabel);

            _accelerationLabel = new Label("9.81 m/s²");
            _accelerationLabel.style.fontSize = 10;
            _accelerationLabel.style.marginTop = -2;
            _accelerationLabel.style.color = new StyleColor(T.TextSecondary);
            _accelerationLabel.pickingMode = PickingMode.Ignore;
            column.Add(_accelerationLabel);

            _directionLabel = new Label("PULL VECTOR · COREWARD");
            _directionLabel.style.fontSize = 8;
            _directionLabel.style.marginTop = 3;
            _directionLabel.style.letterSpacing = 0.85f;
            _directionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _directionLabel.style.color = new StyleColor(T.TextMuted);
            _directionLabel.pickingMode = PickingMode.Ignore;
            column.Add(_directionLabel);
        }

        private static void BuildSurfaceMeter()
        {
            var header = new VisualElement { name = "GravitySurfaceHeader" };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginTop = 9;
            header.pickingMode = PickingMode.Ignore;
            _card.Add(header);

            var label = new Label("SURFACE PULL");
            label.style.flexGrow = 1;
            label.style.fontSize = 8;
            label.style.letterSpacing = 1.1f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(T.TextMuted);
            label.pickingMode = PickingMode.Ignore;
            header.Add(label);

            _surfaceLabel = new Label("100%");
            _surfaceLabel.style.fontSize = 8;
            _surfaceLabel.style.letterSpacing = 0.7f;
            _surfaceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _surfaceLabel.style.color = new StyleColor(T.AccentPurple);
            _surfaceLabel.pickingMode = PickingMode.Ignore;
            header.Add(_surfaceLabel);

            var track = new VisualElement { name = "GravitySurfaceTrack" };
            track.style.height = 5;
            track.style.marginTop = 4;
            track.style.backgroundColor = new StyleColor(new Color(0.14f, 0.16f, 0.22f, 0.92f));
            track.style.overflow = Overflow.Hidden;
            track.pickingMode = PickingMode.Ignore;
            T.Radius(track, 3f);
            _card.Add(track);

            _meterFill = new VisualElement { name = "GravitySurfaceFill" };
            _meterFill.style.position = Position.Absolute;
            _meterFill.style.left = 0;
            _meterFill.style.top = 0;
            _meterFill.style.bottom = 0;
            _meterFill.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
            _meterFill.style.backgroundColor = new StyleColor(T.AccentPurple);
            _meterFill.pickingMode = PickingMode.Ignore;
            T.Radius(_meterFill, 3f);
            track.Add(_meterFill);
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
            Color accent = ResolveAccent(gravity);
            var body = GravityProvider.ActiveBody;
            string bodyName = body != null ? body.DisplayName.ToUpperInvariant() : "LOCAL FIELD";
            string direction = gravity.IsRadial ? "PULL VECTOR · COREWARD" : "PULL VECTOR · DOWNWARD";

            _bodyLabel.text = bodyName;
            _gLabel.text = $"{gees:0.00} G";
            _accelerationLabel.text = $"{gravity.Magnitude:0.00} m/s²";
            _directionLabel.text = direction;
            _surfaceLabel.text = gravity.IsRadial ? $"{surfacePull * 100f:0}%" : "NOMINAL";
            T.SetFillPercent(_meterFill, surfacePull);

            _gLabel.style.color = new StyleColor(accent);
            _pullArrow.style.color = new StyleColor(accent);
            _surfaceLabel.style.color = new StyleColor(accent);
            _pulseDot.style.backgroundColor = new StyleColor(accent);
            _accentLine.style.backgroundColor = new StyleColor(accent);
            _orb.style.backgroundColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.16f));
            T.Border(_orb, 1f, new Color(accent.r, accent.g, accent.b, 0.75f));
            _meterFill.style.backgroundColor = new StyleColor(accent);
            T.Border(_card, 1f, new Color(accent.r, accent.g, accent.b, 0.58f));

            float pulse = 0.64f + Mathf.Sin(Time.unscaledTime * 3.2f) * 0.28f;
            _pulseDot.style.opacity = pulse;
        }

        private static Color ResolveAccent(GravityFieldSample gravity)
        {
            if (gravity.Gees >= 1.75f) return T.AccentAmber;
            if (gravity.Gees <= 0.20f || gravity.SurfaceFraction <= 0.15f) return T.AccentBlue;
            if (gravity.Gees <= 0.70f || gravity.SurfaceFraction <= 0.50f) return T.AccentCyan;
            return T.AccentPurple;
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
