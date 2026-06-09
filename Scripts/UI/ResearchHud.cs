// Assets/Scripts/VoxelEngine/UI/ResearchHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        RESEARCH PROGRESS HUD — Top-right corner widget         ║
// ║   Visible only while ResearchManager.ActiveResearch != null.  ║
// ║   Shows research name, ETA, and an animated progress bar.      ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Research;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class ResearchHud
    {
        // ── State ──────────────────────────────────────────────────────
        private static VisualElement _root, _box;
        private static Label         _eyebrow, _title, _eta;
        private static VisualElement _fill;
        private static float         _smoothPct;

        // ── Mount ──────────────────────────────────────────────────────
        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _box != null && _box.parent == uiRoot) return;
            _root = uiRoot;
            if (_box != null) _box.RemoveFromHierarchy();

            _box = new VisualElement { name = "ResearchHud" };
            _box.style.position         = Position.Absolute;
            _box.style.top              = 18;
            _box.style.right            = 18;
            _box.style.width            = 270;
            _box.style.paddingTop       = 10;
            _box.style.paddingBottom    = 10;
            _box.style.paddingLeft      = 12;
            _box.style.paddingRight     = 12;
            _box.style.backgroundColor  = new StyleColor(new Color(T.BgPanel.r, T.BgPanel.g, T.BgPanel.b, 0.94f));
            T.Radius(_box, T.PanelRadius);
            T.Border(_box, 1, new Color(T.AccentBlue.r, T.AccentBlue.g, T.AccentBlue.b, 0.45f));
            _box.pickingMode            = PickingMode.Ignore;
            _box.style.display          = DisplayStyle.None;
            uiRoot.Add(_box);

            // Top: eyebrow label "RESEARCHING"
            _eyebrow = new Label("RESEARCHING");
            _eyebrow.style.color                   = new StyleColor(new Color(T.AccentBlue.r, T.AccentBlue.g, T.AccentBlue.b, 0.75f));
            _eyebrow.style.fontSize                = 8;
            _eyebrow.style.unityFontStyleAndWeight = FontStyle.Bold;
            _eyebrow.style.letterSpacing           = 2.0f;
            _eyebrow.style.marginBottom            = 3;
            _eyebrow.pickingMode = PickingMode.Ignore;
            _box.Add(_eyebrow);

            // Research name.
            _title = new Label("");
            _title.style.color                   = new StyleColor(T.TextPrimary);
            _title.style.fontSize                = 13;
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.whiteSpace              = WhiteSpace.NoWrap;
            _title.style.overflow                = Overflow.Hidden;
            _title.pickingMode = PickingMode.Ignore;
            _box.Add(_title);

            // Progress bar.
            _box.Add(T.Spacer(6));
            var barTrack = new VisualElement();
            barTrack.style.height          = 6;
            barTrack.style.backgroundColor = new StyleColor(new Color(0.05f, 0.055f, 0.075f));
            T.Radius(barTrack, 3f);
            barTrack.style.overflow = Overflow.Hidden;
            barTrack.pickingMode    = PickingMode.Ignore;

            _fill = new VisualElement();
            _fill.style.height          = 6;
            _fill.style.backgroundColor = new StyleColor(T.AccentBlue);
            T.Radius(_fill, 3f);
            _fill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));
            _fill.pickingMode = PickingMode.Ignore;
            barTrack.Add(_fill);
            _box.Add(barTrack);

            _box.Add(T.Spacer(5));

            // ETA / detail line.
            _eta = new Label("");
            _eta.style.color    = new StyleColor(T.TextMuted);
            _eta.style.fontSize = 10;
            _eta.pickingMode    = PickingMode.Ignore;
            _box.Add(_eta);
        }

        // ── Tick ───────────────────────────────────────────────────────
        public static void Tick()
        {
            if (_box == null) return;

            var rm = ResearchManager.Instance;
            if (rm == null || rm.ActiveResearch == null)
            {
                _box.style.display = DisplayStyle.None;
                _smoothPct = 0f;
                return;
            }

            _box.style.display = DisplayStyle.Flex;
            var n = rm.ActiveResearch;

            _title.text = n.displayName;

            // Smooth the bar fill for a polished animation.
            float target  = Mathf.Clamp01(rm.ActiveProgress01);
            _smoothPct    = Mathf.Lerp(_smoothPct, target, Time.unscaledDeltaTime * 3f);
            T.SetFillPercent(_fill, _smoothPct);

            if (rm.ActiveHasCost)
            {
                float secsLeft = Mathf.Max(0f, n.researchSeconds * (1f - rm.ActiveProgress01));
                _eta.text = secsLeft > 60f
                    ? $"{_smoothPct * 100f:0}%  ·  {Mathf.CeilToInt(secsLeft / 60f)}m {Mathf.RoundToInt(secsLeft % 60f)}s left"
                    : $"{_smoothPct * 100f:0}%  ·  {Mathf.CeilToInt(secsLeft)}s left";
            }
            else
            {
                _eta.text = "Awaiting science packs at a Research Lab...";
            }
        }
    }
}
