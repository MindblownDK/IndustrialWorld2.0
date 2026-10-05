// Assets/Scripts/VoxelEngine/UI/PlayerMotionHud.cs
//
// 14.62.0 - MON-02: on-foot motion telemetry.
//
// The pilot always knew his speed; the crewman never did. This is the second
// instrument in the bottom-left rack, next to the gravity monitor (MON-01),
// built in the same restrained LCD language: true world speed, the current
// dampener REFERENCE (world rest, a nearby grid, or an explicit Ctrl+Z lock)
// with the speed RELATIVE to it, and the state of the player's personal
// inertia dampeners. Hidden while seated - the cockpit has its own gauges.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.GridSystem;
using VoxelEngine.Player;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class PlayerMotionHud
    {
        // Same muted phosphor palette as the gravity instrument.
        private static readonly Color LcdGlass = new(0.105f, 0.125f, 0.075f, 0.98f);
        private static readonly Color LcdFrame = new(0.31f, 0.37f, 0.21f, 0.88f);
        private static readonly Color LcdInk = new(0.72f, 0.84f, 0.42f, 1f);
        private static readonly Color InkRelative = new(0.45f, 0.74f, 0.90f, 1f);
        private static readonly Color InkWarning = new(0.98f, 0.71f, 0.24f, 1f);

        private static VisualElement _root;
        private static VisualElement _card;
        private static VisualElement _lcdBorder;
        private static Label _speedLabel;
        private static Label _speedUnitLabel;
        private static Label _referenceLabel;
        private static Label _relativeLabel;
        private static Label _dampenerLabel;

        private static PlayerController _player;
        private static float _nextPlayerSearchAt;
        private static bool _visible;

        // Speed is measured, not asked for: transform delta per frame captures
        // walking, jetpack flight AND the magnetic-boot carry equally truthfully.
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

            _card = new VisualElement { name = "PlayerMotionHud" };
            _card.style.position = Position.Absolute;
            _card.style.left = 222;   // 18 + gravity card (196) + 8 gap: a rack, not a pile
            _card.style.bottom = 18;
            _card.style.width = 172;
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
            BuildLcd();
            BuildStatusRows();
            _visible = false;
            _hasLastPos = false;
        }

        private static void BuildHeader()
        {
            var row = new VisualElement { name = "MotionInstrumentHeader" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            row.pickingMode = PickingMode.Ignore;
            _card.Add(row);

            var title = new Label("MOTION");
            title.style.flexGrow = 1;
            title.style.fontSize = 8;
            title.style.letterSpacing = 1.15f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(T.TextSecondary);
            title.pickingMode = PickingMode.Ignore;
            row.Add(title);

            var module = new Label("MON-02");
            module.style.fontSize = 7;
            module.style.letterSpacing = 0.8f;
            module.style.unityFontStyleAndWeight = FontStyle.Bold;
            module.style.color = new StyleColor(T.TextMuted);
            module.pickingMode = PickingMode.Ignore;
            row.Add(module);
        }

        private static void BuildLcd()
        {
            _lcdBorder = new VisualElement { name = "MotionLcd" };
            _lcdBorder.style.backgroundColor = new StyleColor(LcdGlass);
            _lcdBorder.style.paddingLeft = 8;
            _lcdBorder.style.paddingRight = 8;
            _lcdBorder.style.paddingTop = 5;
            _lcdBorder.style.paddingBottom = 5;
            _lcdBorder.pickingMode = PickingMode.Ignore;
            T.Radius(_lcdBorder, 2f);
            T.Border(_lcdBorder, 1f, new Color(LcdFrame.r, LcdFrame.g, LcdFrame.b, 0.70f));
            _card.Add(_lcdBorder);

            var speedRow = new VisualElement();
            speedRow.style.flexDirection = FlexDirection.Row;
            speedRow.style.alignItems = Align.FlexEnd;
            speedRow.pickingMode = PickingMode.Ignore;
            _lcdBorder.Add(speedRow);

            _speedLabel = new Label("0.0");
            _speedLabel.style.fontSize = 22;
            _speedLabel.style.letterSpacing = 0.6f;
            _speedLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _speedLabel.style.color = new StyleColor(LcdInk);
            _speedLabel.pickingMode = PickingMode.Ignore;
            speedRow.Add(_speedLabel);

            _speedUnitLabel = new Label("m/s");
            _speedUnitLabel.style.fontSize = 9;
            _speedUnitLabel.style.marginLeft = 4;
            _speedUnitLabel.style.marginBottom = 3;
            _speedUnitLabel.style.letterSpacing = 0.6f;
            _speedUnitLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _speedUnitLabel.style.color = new StyleColor(new Color(LcdInk.r, LcdInk.g, LcdInk.b, 0.80f));
            _speedUnitLabel.pickingMode = PickingMode.Ignore;
            speedRow.Add(_speedUnitLabel);

            _relativeLabel = new Label("");
            _relativeLabel.style.fontSize = 8;
            _relativeLabel.style.marginTop = 1;
            _relativeLabel.style.letterSpacing = 0.7f;
            _relativeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _relativeLabel.style.color = new StyleColor(InkRelative);
            _relativeLabel.pickingMode = PickingMode.Ignore;
            _lcdBorder.Add(_relativeLabel);
        }

        private static void BuildStatusRows()
        {
            var row = new VisualElement { name = "MotionStatusRow" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 4;
            row.style.paddingTop = 3;
            row.style.borderTopWidth = 1;
            row.style.borderTopColor = new StyleColor(new Color(LcdFrame.r, LcdFrame.g, LcdFrame.b, 0.40f));
            row.pickingMode = PickingMode.Ignore;
            _card.Add(row);

            var caption = new Label("DAMPENERS");
            caption.style.fontSize = 7;
            caption.style.letterSpacing = 1.1f;
            caption.style.unityFontStyleAndWeight = FontStyle.Bold;
            caption.style.color = new StyleColor(T.TextMuted);
            caption.pickingMode = PickingMode.Ignore;
            row.Add(caption);

            _dampenerLabel = new Label("ON");
            _dampenerLabel.style.fontSize = 8;
            _dampenerLabel.style.letterSpacing = 0.9f;
            _dampenerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _dampenerLabel.style.color = new StyleColor(LcdInk);
            _dampenerLabel.pickingMode = PickingMode.Ignore;
            row.Add(_dampenerLabel);

            _referenceLabel = new Label("REF · WORLD REST");
            _referenceLabel.style.fontSize = 7;
            _referenceLabel.style.marginTop = 2;
            _referenceLabel.style.letterSpacing = 0.8f;
            _referenceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _referenceLabel.style.color = new StyleColor(T.TextMuted);
            _referenceLabel.pickingMode = PickingMode.Ignore;
            _card.Add(_referenceLabel);
        }

        public static void Tick()
        {
            if (_card == null) return;
            if (UIState.IsBlocking || GridCockpit.AnyPilotSeatActive)
            {
                SetVisible(false);
                _hasLastPos = false;     // never measure across a seat/menu gap
                return;
            }

            var player = ResolvePlayer();
            if (player == null)
            {
                SetVisible(false);
                _hasLastPos = false;
                return;
            }

            // ── measure true world velocity from the transform itself ──
            float dt = Time.deltaTime;
            Vector3 pos = player.transform.position;
            if (dt > 1e-5f && _hasLastPos)
            {
                Vector3 delta = pos - _lastPos;
                // A teleport / respawn / floating-origin shift is not motion.
                if (delta.sqrMagnitude < 80f * 80f)
                    _measuredVelocity = delta / dt;
                else
                    _measuredVelocity = Vector3.zero;
            }
            _lastPos = pos;
            _hasLastPos = true;

            float speed = _measuredVelocity.magnitude;
            float smooth = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, speed, smooth);

            if (!_visible) SetVisible(true);
            ApplyReadout(player);
        }

        private static void ApplyReadout(PlayerController player)
        {
            var boots = player.GetComponent<MagneticBoots>();
            GridEntity reference = boots != null ? boots.ActiveReferenceGrid : null;
            bool locked = boots != null && boots.LockedReference != null && reference == boots.LockedReference;
            bool dampeners = player.DampenersOn;

            _speedLabel.text = _smoothedSpeed >= 100f ? $"{_smoothedSpeed:0}" : $"{_smoothedSpeed:0.0}";

            if (reference != null && reference.Body != null)
            {
                Vector3 refVel = locked
                    ? reference.Body.linearVelocity
                    : reference.Body.GetPointVelocity(player.transform.position);
                float rel = (_measuredVelocity - refVel).magnitude;
                _relativeLabel.text = $"REL {(rel >= 100f ? $"{rel:0}" : $"{rel:0.0}")} m/s";
                _relativeLabel.style.display = DisplayStyle.Flex;
                string name = reference.name.ToUpperInvariant();
                if (name.Length > 16) name = name.Substring(0, 16);
                _referenceLabel.text = locked ? $"REF · LOCK {name}" : $"REF · {name}";
            }
            else
            {
                _relativeLabel.style.display = DisplayStyle.None;
                _referenceLabel.text = "REF · WORLD REST";
            }

            Color ink = dampeners ? (reference != null ? InkRelative : LcdInk) : InkWarning;
            _dampenerLabel.text = dampeners ? (locked ? "REL LOCK" : "ON") : "OFF · DRIFT";
            _dampenerLabel.style.color = new StyleColor(ink);
            _speedLabel.style.color = new StyleColor(ink);
            _speedUnitLabel.style.color = new StyleColor(new Color(ink.r, ink.g, ink.b, 0.80f));
            T.Border(_lcdBorder, 1f, new Color(ink.r, ink.g, ink.b, 0.70f));
            T.Border(_card, 1f, new Color(ink.r, ink.g, ink.b, 0.35f));
        }

        private static PlayerController ResolvePlayer()
        {
            if (_player != null) return _player;
            if (Time.unscaledTime < _nextPlayerSearchAt) return null;
            _nextPlayerSearchAt = Time.unscaledTime + 0.5f;
            _player = Object.FindAnyObjectByType<PlayerController>();
            return _player;
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
