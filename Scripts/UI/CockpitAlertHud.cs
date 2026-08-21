// Assets/Scripts/VoxelEngine/UI/CockpitAlertHud.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                 COCKPIT ALERT BANNER (Phase 5)                        ║
// ║                                                                       ║
// ║  Persistent warning strip shown WHILE PILOTING when a ship system     ║
// ║  reports an emergency (containment vault pressure loss, etc.).        ║
// ║  It is the cockpit's own alarm channel:                              ║
// ║                                                                       ║
// ║   • Appears only when a pilot seat is active.                        ║
// ║   • Holds for ~2.5 s after the last report, then fades out.          ║
// ║   • Amber = warning, red = critical (pulsing).                        ║
// ║   • Mounted into the persistent HUD layer like the other LCD HUDs.   ║
// ╚══════════════════════════════════════════════════════════════════════╝
using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    public static class CockpitAlertHud
    {
        private const float HoldSeconds = 2.5f;

        private static VisualElement _root;
        private static VisualElement _banner;
        private static Label _titleLabel;
        private static Label _detailLabel;
        private static float _expiry = -999f;
        private static bool _critical;
        private static bool _pulseHigh;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _banner != null && _banner.parent == uiRoot) return;
            _root = uiRoot;
            if (_banner != null) _banner.RemoveFromHierarchy();

            _banner = new VisualElement { name = "CockpitAlertHud" };
            _banner.style.position = Position.Absolute;
            _banner.style.top = Length.Percent(16f);
            _banner.style.left = Length.Percent(50f);
            _banner.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f, 0f);
            _banner.style.width = new StyleLength(new Length(64f, LengthUnit.Percent));
            _banner.style.maxWidth = 720;
            _banner.style.flexDirection = FlexDirection.Column;
            _banner.style.alignItems = Align.Center;
            _banner.style.paddingTop = 8;
            _banner.style.paddingBottom = 8;
            _banner.style.paddingLeft = 14;
            _banner.style.paddingRight = 14;
            _banner.style.backgroundColor = new StyleColor(new Color(0.10f, 0.02f, 0.02f, 0.88f));
            UITheme.Radius(_banner, 10f);
            UITheme.Border(_banner, 1, new Color(1f, 0.3f, 0.2f, 0.8f));
            _banner.style.display = DisplayStyle.None;
            _banner.pickingMode = PickingMode.Ignore;
            uiRoot.Add(_banner);

            _titleLabel = new Label("ALERT");
            _titleLabel.style.fontSize = 13;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.letterSpacing = 2.2f;
            _titleLabel.style.color = new Color(1f, 0.35f, 0.25f);
            _titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _titleLabel.pickingMode = PickingMode.Ignore;
            _banner.Add(_titleLabel);

            _detailLabel = new Label();
            _detailLabel.style.marginTop = 2;
            _detailLabel.style.fontSize = 10;
            _detailLabel.style.letterSpacing = 1.0f;
            _detailLabel.style.color = new Color(0.92f, 0.88f, 0.86f);
            _detailLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _detailLabel.style.whiteSpace = WhiteSpace.Normal;
            _detailLabel.pickingMode = PickingMode.Ignore;
            _banner.Add(_detailLabel);

            // Self-ticking pulse/fade (runs while the banner is attached).
            _banner.schedule.Execute(Tick).Every(120);
        }

        /// <summary>Report an emergency to the cockpit. Holds the banner for ~2.5 s.</summary>
        public static void Report(string title, string detail, bool critical = false)
        {
            if (_banner == null) return;
            if (_titleLabel != null) _titleLabel.text = title;
            if (_detailLabel != null) _detailLabel.text = detail;
            _critical = critical;
            _expiry = Time.unscaledTime + HoldSeconds;
            _banner.style.display = DisplayStyle.Flex;
        }

        private static void Tick(TimerState state)
        {
            if (_banner == null) return;

            // Only the cockpit channel — invisible on foot.
            bool piloting = VoxelEngine.GridSystem.GridCockpit.AnyPilotSeatActive;
            bool active = piloting && Time.unscaledTime < _expiry;
            if (!active)
            {
                _banner.style.display = DisplayStyle.None;
                return;
            }

            // Critical alerts pulse; warnings hold steady.
            if (_critical)
            {
                _pulseHigh = !_pulseHigh;
                float a = _pulseHigh ? 1f : 0.55f;
                _banner.style.backgroundColor = new StyleColor(new Color(0.10f, 0.02f, 0.02f, 0.88f * a));
                if (_titleLabel != null)
                    _titleLabel.style.color = new Color(1f, 0.25f, 0.18f, a);
            }
        }
    }
}
