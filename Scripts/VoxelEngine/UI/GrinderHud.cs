// Assets/Scripts/VoxelEngine/UI/GrinderHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          GRINDER PROGRESS HUD — Centre-bottom overlay          ║
// ║   Displayed while the player grinds a grid block.              ║
// ║   Animated fill bar + percentage label, fades when done.       ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Player;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class GrinderHud
    {
        // ── State ──────────────────────────────────────────────────────
        private static VisualElement        _root, _container;
        private static VisualElement        _barFill;
        private static Label                _label, _pctLabel;
        private static PlayerInteractionTool _cachedTool;
        private static float                _displayedT;   // smooth lerp target

        // ── Mount ──────────────────────────────────────────────────────
        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _container != null && _container.parent == uiRoot) return;
            _root = uiRoot;
            if (_container != null) _container.RemoveFromHierarchy();

            _container = new VisualElement { name = "GrinderHud" };
            _container.style.position    = Position.Absolute;
            _container.style.bottom      = 190;
            _container.style.left        = 0;
            _container.style.right       = 0;
            _container.style.alignItems  = Align.Center;
            _container.pickingMode       = PickingMode.Ignore;
            _container.style.display     = DisplayStyle.None;
            uiRoot.Add(_container);

            // Card.
            var box = new VisualElement();
            box.style.width           = 260;
            box.style.paddingTop      = 10;
            box.style.paddingBottom   = 10;
            box.style.paddingLeft     = 14;
            box.style.paddingRight    = 14;
            box.style.backgroundColor = new StyleColor(new Color(T.BgPanel.r, T.BgPanel.g, T.BgPanel.b, 0.94f));
            T.Radius(box, 8f);
            T.Border(box, 1, new Color(T.AccentOrange.r, T.AccentOrange.g, T.AccentOrange.b, 0.50f));
            box.pickingMode = PickingMode.Ignore;
            _container.Add(box);

            // Header row: icon + label + pct.
            var hdr = new VisualElement();
            hdr.style.flexDirection = FlexDirection.Row;
            hdr.style.alignItems    = Align.Center;
            hdr.style.marginBottom  = 7;
            hdr.pickingMode         = PickingMode.Ignore;

            var ico = new Label("⚙");
            ico.style.fontSize   = 14;
            ico.style.color      = new StyleColor(T.AccentOrange);
            ico.style.marginRight = 7;
            ico.pickingMode = PickingMode.Ignore;
            hdr.Add(ico);

            _label = T.StatLabel("Dismantling...", T.TextPrimary);
            _label.style.flexGrow = 1;
            hdr.Add(_label);

            _pctLabel = T.StatLabel("0%", T.AccentOrange);
            hdr.Add(_pctLabel);

            box.Add(hdr);

            // Progress bar track.
            var (bar, fill) = T.ProgressBar(0f, T.AccentOrange, 8, true);
            _barFill = fill;
            box.Add(bar);
            _displayedT = 0f;
        }

        // ── Tick ───────────────────────────────────────────────────────
        public static void Tick()
        {
            if (_container == null) return;

            if (_cachedTool == null)
                _cachedTool = UnityEngine.Object.FindAnyObjectByType<PlayerInteractionTool>();

            var tool = _cachedTool;
            if (tool == null || !tool.IsGrinding)
            {
                _container.style.display = DisplayStyle.None;
                _displayedT = 0f;
                return;
            }

            _container.style.display = DisplayStyle.Flex;

            float target    = tool.GrindProgress01;
            _displayedT     = Mathf.Lerp(_displayedT, target, Time.unscaledDeltaTime * 8f);
            T.SetFillPercent(_barFill, _displayedT);

            int pct     = Mathf.RoundToInt(target * 100f);
            _pctLabel.text  = $"{pct}%";
            _label.text     = pct >= 100 ? "Complete!" : "Dismantling...";
        }
    }
}
