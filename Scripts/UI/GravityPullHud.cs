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
        // Muted phosphor palette: old practical instrumentation, not a neon hologram.
        private static readonly Color LcdGlass = new(0.105f, 0.125f, 0.075f, 0.98f);
        private static readonly Color LcdFrame = new(0.31f, 0.37f, 0.21f, 0.88f);
        private static readonly Color LcdInk = new(0.72f, 0.84f, 0.42f, 1f);

        // Motion/dampener accents (14.62.1): blue = referenced to a grid, amber = drifting.
        private static readonly Color InkRelative = new(0.45f, 0.74f, 0.90f, 1f);
        private static readonly Color InkWarning = new(0.98f, 0.71f, 0.24f, 1f);

        private static VisualElement _root;
        private static VisualElement _card;
        private static VisualElement _lcdScreen;
        private static VisualElement _lcdBorder;
        private static Label _bodyLabel;
        private static Label _lcdGLabel;
        private static Label _lcdAccelerationLabel;
        private static Label _speedLabel;
        private static Label _relativeLabel;
        private static Label _dampenerLabel;
        private static Label _referenceLabel;
        private static Label _tempLabel;
        private static Label _climateLabel;

        private static PlayerController _player;
        private static float _nextPlayerSearchAt;
        private static float _smoothedGees;
        private static bool _visible;

        // Motion telemetry (14.62.1): measured from the transform itself, so walking,
        // jetpack flight and the magnetic-boot carry all read equally truthfully.
        private static bool _hasLastPos;
        private static Vector3 _lastPos;
        private static Vector3 _measuredVelocity;
        private static float _smoothedSpeed;

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
            _card.style.width = 184;   // 14.62.1 - one compact instrument, more screen
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
            _hasLastPos = false;
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
            BuildClimateRow();
        }

        private static void BuildLcd(VisualElement parent)
        {
            _lcdBorder = new VisualElement { name = "GravityLcdBezel" };
            _lcdBorder.style.width = 84;
            _lcdBorder.style.height = 76;   // 14.62.1 - speed/REL lines under the pull
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
            for (int i = 0; i < 7; i++)
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

            // ── motion, under the local pull — same glass (14.62.1) ──
            _speedLabel = new Label("0.0 m/s");
            _speedLabel.style.marginLeft = 3;
            _speedLabel.style.marginTop = 3;
            _speedLabel.style.fontSize = 10;
            _speedLabel.style.letterSpacing = 0.6f;
            _speedLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _speedLabel.style.color = new StyleColor(LcdInk);
            _speedLabel.pickingMode = PickingMode.Ignore;
            _lcdScreen.Add(_speedLabel);

            _relativeLabel = new Label("");   // "REL x.x m/s" only while referenced
            _relativeLabel.style.marginLeft = 3;
            _relativeLabel.style.marginTop = -1;
            _relativeLabel.style.fontSize = 7;
            _relativeLabel.style.letterSpacing = 0.6f;
            _relativeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _relativeLabel.style.color = new StyleColor(InkRelative);
            _relativeLabel.pickingMode = PickingMode.Ignore;
            _lcdScreen.Add(_relativeLabel);
        }

        private static void BuildReferenceColumn(VisualElement parent)
        {
            // 14.62.1 — the surface-reference meter is gone; this column now carries
            // the body name, the personal DAMPENER state and the dampener REFERENCE
            // (world rest, the nearby grid, or an explicit Ctrl+Z lock).
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

            var dampCaption = SmallCaption("DAMPENERS");
            dampCaption.style.marginTop = 3;
            column.Add(dampCaption);

            _dampenerLabel = new Label("ON");
            _dampenerLabel.style.fontSize = 9;
            _dampenerLabel.style.letterSpacing = 0.8f;
            _dampenerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _dampenerLabel.style.color = new StyleColor(LcdInk);
            _dampenerLabel.pickingMode = PickingMode.Ignore;
            column.Add(_dampenerLabel);

            var refCaption = SmallCaption("REF");
            refCaption.style.marginTop = 3;
            column.Add(refCaption);

            _referenceLabel = new Label("WORLD REST");
            _referenceLabel.style.fontSize = 8;
            _referenceLabel.style.letterSpacing = 0.6f;
            _referenceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _referenceLabel.style.color = new StyleColor(T.TextMuted);
            _referenceLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _referenceLabel.style.overflow = Overflow.Hidden;
            _referenceLabel.style.textOverflow = TextOverflow.Ellipsis;
            _referenceLabel.pickingMode = PickingMode.Ignore;
            column.Add(_referenceLabel);
        }

        private static void BuildClimateRow()
        {
            var row = new VisualElement { name = "ClimateInstrumentRow" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 4;
            row.style.paddingTop = 3;
            row.style.borderTopWidth = 1;
            row.style.borderTopColor = new StyleColor(new Color(LcdFrame.r, LcdFrame.g, LcdFrame.b, 0.40f));
            row.pickingMode = PickingMode.Ignore;
            _card.Add(row);

            _tempLabel = new Label("+15.0°C");
            _tempLabel.style.fontSize = 8;
            _tempLabel.style.letterSpacing = 0.6f;
            _tempLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _tempLabel.style.color = new StyleColor(LcdInk);
            _tempLabel.pickingMode = PickingMode.Ignore;
            row.Add(_tempLabel);

            _climateLabel = new Label("🌱 SPRING");
            _climateLabel.style.fontSize = 7;
            _climateLabel.style.letterSpacing = 0.8f;
            _climateLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _climateLabel.style.color = new StyleColor(new Color(LcdInk.r, LcdInk.g, LcdInk.b, 0.80f));
            _climateLabel.pickingMode = PickingMode.Ignore;
            row.Add(_climateLabel);
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
                _hasLastPos = false;     // never measure speed across a seat/menu gap
                return;
            }

            var player = ResolvePlayer();
            if (player == null)
            {
                SetVisible(false);
                _hasLastPos = false;
                return;
            }

            GravityFieldSample gravity = GravityProvider.Sample(player.transform.position);
            float smooth = 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime);
            _smoothedGees = Mathf.Lerp(_smoothedGees, gravity.Gees, smooth);

            // ── measure true world velocity from the transform itself (14.62.1) ──
            float dt = Time.deltaTime;
            Vector3 pos = player.transform.position;
            if (dt > 1e-5f && _hasLastPos)
            {
                Vector3 delta = pos - _lastPos;
                // A teleport / respawn / floating-origin shift is not motion.
                _measuredVelocity = delta.sqrMagnitude < 80f * 80f ? delta / dt : Vector3.zero;
            }
            _lastPos = pos;
            _hasLastPos = true;
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, _measuredVelocity.magnitude,
                1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));

            if (!_visible) SetVisible(true);
            ApplyReadout(player, gravity, _smoothedGees);
        }

        private static PlayerController ResolvePlayer()
        {
            if (_player != null) return _player;
            if (Time.unscaledTime < _nextPlayerSearchAt) return null;
            _nextPlayerSearchAt = Time.unscaledTime + 0.5f;
            _player = Object.FindAnyObjectByType<PlayerController>();
            return _player;
        }

        private static void ApplyReadout(PlayerController player, GravityFieldSample gravity, float gees)
        {
            Color ink = ResolveLcdInk(gravity);
            var body = GravityProvider.ActiveBody;
            string bodyName = body != null ? body.DisplayName.ToUpperInvariant() : "LOCAL FIELD";

            _bodyLabel.text = bodyName;
            _lcdGLabel.text = $"{gees:0.00}G";
            _lcdAccelerationLabel.text = $"{gravity.Magnitude:00.00} m/s²";

            // ── motion + dampener state (14.62.1) ──
            var boots = player.GetComponent<MagneticBoots>();
            GridEntity reference = boots != null ? boots.ActiveReferenceGrid : null;
            bool locked = boots != null && boots.LockedReference != null && reference == boots.LockedReference;
            bool dampeners = player.DampenersOn;

            _speedLabel.text = _smoothedSpeed >= 100f ? $"{_smoothedSpeed:0} m/s" : $"{_smoothedSpeed:0.0} m/s";

            if (reference != null && reference.Body != null)
            {
                Vector3 refVel = locked
                    ? reference.Body.linearVelocity
                    : reference.Body.GetPointVelocity(player.transform.position);
                float rel = (_measuredVelocity - refVel).magnitude;
                _relativeLabel.text = rel >= 100f ? $"REL {rel:0} m/s" : $"REL {rel:0.0} m/s";
                string refName = reference.name.ToUpperInvariant();
                _referenceLabel.text = locked ? "LOCK " + refName : refName;
                _referenceLabel.style.color = new StyleColor(InkRelative);
            }
            else
            {
                _relativeLabel.text = "";
                _referenceLabel.text = "WORLD REST";
                _referenceLabel.style.color = new StyleColor(T.TextMuted);
            }

            Color dampInk = dampeners ? (reference != null ? InkRelative : ink) : InkWarning;
            _dampenerLabel.text = dampeners ? (locked ? "REL LOCK" : "ON") : "OFF · DRIFT";
            _dampenerLabel.style.color = new StyleColor(dampInk);

            _lcdGLabel.style.color = new StyleColor(ink);
            _lcdAccelerationLabel.style.color = new StyleColor(new Color(ink.r, ink.g, ink.b, 0.84f));
            _speedLabel.style.color = new StyleColor(dampeners ? ink : InkWarning);
            T.Border(_lcdBorder, 1f, new Color(ink.r, ink.g, ink.b, 0.70f));
            T.Border(_card, 1f, new Color(ink.r, ink.g, ink.b, 0.35f));

            var season = VoxelEngine.Weather.PlanetarySeasons.GetCurrentSeasonInfo();
            string tempSign = season.effectiveTemperature >= 0f ? "+" : "";
            if (_tempLabel != null)
            {
                _tempLabel.text = $"{tempSign}{season.effectiveTemperature:F1}°C";
                Color tempColor = season.isFreezing ? new Color(0.45f, 0.85f, 1f) : ink;
                _tempLabel.style.color = new StyleColor(tempColor);
            }
            if (_climateLabel != null)
            {
                string weatherStr = VoxelEngine.Weather.WeatherManager.Instance != null && VoxelEngine.Weather.WeatherManager.Instance.IsWeatherActive
                    ? VoxelEngine.Weather.WeatherManager.Instance.CurrentState.ToString().ToUpperInvariant()
                    : season.forecastPrecipitation.ToUpperInvariant();
                _climateLabel.text = $"{season.SeasonIcon} {season.SeasonName.ToUpperInvariant()} · {weatherStr}";
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
