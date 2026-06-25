// Assets/Scripts/VoxelEngine/UI/RustStyleHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║            PLAYER VITALS HUD — Bottom-Right Corner             ║
// ║   Premium segmented bar design: HP · Stamina · Hunger · Oxy   ║
// ║   Dark pill containers, glowing fill, icon + numeric label.    ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Player;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class RustStyleHud
    {
        // ── State ──────────────────────────────────────────────────────
        private static VisualElement _root, _container;

        private static VisualElement _hpFill,     _staFill,    _hungerFill,    _oxyFill;
        private static Label         _hpVal,       _staVal,     _hungerVal,     _oxyVal;
        private static Label         _hpIcon,      _staIcon,    _hungerIcon,    _oxyIcon;

        // Tracks previous values to avoid unnecessary label updates.
        private static float _prevHp, _prevSta, _prevHunger, _prevOxy;

        public const float TOTAL_HEIGHT = 132f; // reserved space for feedback HUD offset

        // ── Mount ──────────────────────────────────────────────────────
        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _container != null && _container.parent == uiRoot) return;
            _root = uiRoot;
            if (_container != null) _container.RemoveFromHierarchy();

            // Outer column — right-aligned, 4 bars stacked vertically.
            _container = new VisualElement { name = "VitalsHud" };
            _container.style.position      = Position.Absolute;
            _container.style.bottom        = 16;
            _container.style.right         = 18;
            _container.style.width         = 170;
            _container.style.flexDirection = FlexDirection.Column;
            _container.style.alignItems    = Align.Stretch;
            _container.pickingMode         = PickingMode.Ignore;
            uiRoot.Add(_container);

            // Build all 4 bars in order.
            (_hpFill,     _hpVal,     _hpIcon)     = AddVitalBar("✚",  T.AccentRed,    "HP");
            AddGap(4);
            (_staFill,    _staVal,    _staIcon)    = AddVitalBar("⚡", T.AccentGreen,  "STA");
            AddGap(4);
            (_hungerFill, _hungerVal, _hungerIcon) = AddVitalBar("◈",  T.AccentAmber,  "HNG");
            AddGap(4);
            (_oxyFill,    _oxyVal,    _oxyIcon)    = AddVitalBar("◉",  new Color(0.18f, 0.68f, 0.94f), "OXY");

            // Force first update so bars don't flash empty.
            _prevHp = _prevSta = _prevHunger = _prevOxy = -1f;
        }

        // ── Tick ───────────────────────────────────────────────────────
        public static void Tick()
        {
            var st = PlayerStats.Instance;
            if (st == null || _hpFill == null) return;

            UpdateBar(_hpFill,     _hpVal,     st.Health,  st.MaxHealth,  ref _prevHp,     T.AccentRed);
            UpdateBar(_staFill,    _staVal,    st.Stamina, st.MaxStamina, ref _prevSta,    T.AccentGreen);
            UpdateBar(_hungerFill, _hungerVal, st.Hunger,  st.MaxHunger,  ref _prevHunger, T.AccentAmber);
            UpdateBar(_oxyFill,    _oxyVal,    st.Oxygen,  st.MaxOxygen,  ref _prevOxy,
                new Color(0.18f, 0.68f, 0.94f));
        }

        // ── Private Helpers ────────────────────────────────────────────
        private static (VisualElement fill, Label val, Label icon) AddVitalBar(
            string iconText, Color fillColor, string abbrev)
        {
            // Track: pill-shaped container.
            var track = new VisualElement();
            track.style.height          = 26;
            track.style.backgroundColor = new StyleColor(new Color(0.05f, 0.055f, 0.075f, 0.94f));
            track.style.overflow        = Overflow.Hidden;
            T.Radius(track, 13f);
            T.Border(track, 1, new Color(fillColor.r, fillColor.g, fillColor.b, 0.22f));
            track.pickingMode = PickingMode.Ignore;

            // Fill layer.
            var fill = new VisualElement();
            fill.style.position         = Position.Absolute;
            fill.style.left             = 0;
            fill.style.top              = 0;
            fill.style.bottom           = 0;
            fill.style.width            = new StyleLength(new Length(100f, LengthUnit.Percent));
            fill.style.backgroundColor  = new StyleColor(new Color(fillColor.r, fillColor.g, fillColor.b, 0.28f));
            T.Radius(fill, 13f);
            fill.pickingMode = PickingMode.Ignore;
            track.Add(fill);

            // Bright leading edge shimmer.
            var shimmer = new VisualElement();
            shimmer.style.position       = Position.Absolute;
            shimmer.style.top            = 3;
            shimmer.style.bottom         = 3;
            shimmer.style.right          = 0;
            shimmer.style.width          = 2;
            shimmer.style.backgroundColor = new StyleColor(new Color(fillColor.r, fillColor.g, fillColor.b, 0.60f));
            Radius(shimmer, 1);
            shimmer.pickingMode = PickingMode.Ignore;
            fill.Add(shimmer);

            // Icon label — left side.
            var ico = new Label(iconText);
            ico.style.position        = Position.Absolute;
            ico.style.left            = 8;
            ico.style.top             = 0;
            ico.style.bottom          = 0;
            ico.style.fontSize        = 13;
            ico.style.color           = new StyleColor(new Color(fillColor.r, fillColor.g, fillColor.b, 0.90f));
            ico.style.unityTextAlign  = TextAnchor.MiddleLeft;
            ico.pickingMode           = PickingMode.Ignore;
            track.Add(ico);

            // Abbrev label — next to icon.
            var abbrevLbl = new Label(abbrev);
            abbrevLbl.style.position        = Position.Absolute;
            abbrevLbl.style.left            = 28;
            abbrevLbl.style.top             = 0;
            abbrevLbl.style.bottom          = 0;
            abbrevLbl.style.fontSize        = 8;
            abbrevLbl.style.letterSpacing   = 1.0f;
            abbrevLbl.style.color           = new StyleColor(new Color(fillColor.r, fillColor.g, fillColor.b, 0.55f));
            abbrevLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            abbrevLbl.style.unityTextAlign  = TextAnchor.MiddleLeft;
            abbrevLbl.pickingMode           = PickingMode.Ignore;
            track.Add(abbrevLbl);

            // Value label — right-aligned.
            var val = new Label("100");
            val.style.position        = Position.Absolute;
            val.style.right           = 8;
            val.style.top             = 0;
            val.style.bottom          = 0;
            val.style.fontSize        = 11;
            val.style.color           = Color.white;
            val.style.unityTextAlign  = TextAnchor.MiddleRight;
            val.style.unityFontStyleAndWeight = FontStyle.Bold;
            val.pickingMode = PickingMode.Ignore;
            track.Add(val);

            _container.Add(track);
            return (fill, val, ico);
        }

        private static void UpdateBar(
            VisualElement fill, Label label,
            float cur, float max, ref float prev, Color fillColor)
        {
            if (max <= 0) return;
            float t = Mathf.Clamp01(cur / max);
            T.SetFillPercent(fill, t);

            // Colour shift: green→amber→red as resource depletes.
            Color displayColor = t > 0.5f ? fillColor :
                                 t > 0.25f ? Color.Lerp(T.AccentAmber, fillColor, (t - 0.25f) / 0.25f) :
                                 Color.Lerp(T.AccentRed, T.AccentAmber, t / 0.25f);
            fill.style.backgroundColor = new StyleColor(new Color(displayColor.r, displayColor.g, displayColor.b, 0.28f));

            if (!Mathf.Approximately(cur, prev))
            {
                label.text = $"{Mathf.RoundToInt(cur)}";
                prev = cur;
            }
        }

        private static void AddGap(float h)
        {
            var s = new VisualElement();
            s.style.height    = h;
            s.pickingMode     = PickingMode.Ignore;
            _container.Add(s);
        }

        private static void Radius(VisualElement v, float r)
        {
            v.style.borderTopLeftRadius = v.style.borderTopRightRadius =
            v.style.borderBottomLeftRadius = v.style.borderBottomRightRadius = r;
        }
    }
}
