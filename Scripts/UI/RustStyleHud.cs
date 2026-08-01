// Assets/Scripts/VoxelEngine/UI/RustStyleHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║            PLAYER VITALS HUD — Bottom-Right Corner             ║
// ║   Premium segmented bar design: HP · H₂ · Hunger · Oxy         ║
// ║   Dark pill containers, glowing fill, icon + numeric label.    ║
// ║   Stamina removed — H₂ shows total Portable Hydrogen Tank ml.  ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.Player;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class RustStyleHud
    {
        private static VisualElement _root, _container;

        private static VisualElement _hpFill, _h2Fill, _hungerFill, _oxyFill;
        private static Label _hpVal, _h2Val, _hungerVal, _oxyVal;
        private static Label _hpIcon, _h2Icon, _hungerIcon, _oxyIcon;

        private static float _prevHp, _prevH2, _prevHunger, _prevOxy;

        public const float TOTAL_HEIGHT = 132f;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _container != null && _container.parent == uiRoot) return;
            _root = uiRoot;
            if (_container != null) _container.RemoveFromHierarchy();

            _container = new VisualElement { name = "VitalsHud" };
            _container.style.position = Position.Absolute;
            _container.style.bottom = 16;
            _container.style.right = 18;
            _container.style.width = 170;
            _container.style.flexDirection = FlexDirection.Column;
            _container.style.alignItems = Align.Stretch;
            _container.pickingMode = PickingMode.Ignore;
            uiRoot.Add(_container);

            var h2Color = new Color(0.35f, 0.85f, 1.0f);
            (_hpFill, _hpVal, _hpIcon) = AddVitalBar("✚", T.AccentRed, "HP");
            AddGap(4);
            (_h2Fill, _h2Val, _h2Icon) = AddVitalBar("◆", h2Color, "H₂");
            AddGap(4);
            (_hungerFill, _hungerVal, _hungerIcon) = AddVitalBar("◈", T.AccentAmber, "HNG");
            AddGap(4);
            (_oxyFill, _oxyVal, _oxyIcon) = AddVitalBar("◉", new Color(0.18f, 0.68f, 0.94f), "OXY");

            _prevHp = _prevH2 = _prevHunger = _prevOxy = -1f;
        }

        public static void Tick()
        {
            var st = PlayerStats.Instance;
            if (st == null || _hpFill == null) return;

            UpdateBar(_hpFill, _hpVal, st.Health, st.MaxHealth, ref _prevHp, T.AccentRed);

            // Total portable hydrogen across inventory tanks (metric ml).
            GetInventoryHydrogenMl(out float h2Cur, out float h2Max);
            UpdateBarMl(_h2Fill, _h2Val, h2Cur, h2Max, ref _prevH2, new Color(0.35f, 0.85f, 1.0f));

            UpdateBar(_hungerFill, _hungerVal, st.Hunger, st.MaxHunger, ref _prevHunger, T.AccentAmber);
            UpdateBar(_oxyFill, _oxyVal, st.Oxygen, st.MaxOxygen, ref _prevOxy,
                new Color(0.18f, 0.68f, 0.94f));
        }

        /// <summary>Sum fill / capacity of every Portable Hydrogen Tank in the player inventory.</summary>
        public static void GetInventoryHydrogenMl(out float currentMl, out float capacityMl)
        {
            currentMl = 0f;
            capacityMl = 0f;
            var inv = Object.FindAnyObjectByType<Inventory>();
            if (inv == null || inv.container == null) return;
            inv.container.EnsureValid();
            for (int i = 0; i < inv.container.Size; i++)
            {
                var s = inv.container.GetSlot(i);
                if (s == null || s.IsEmpty || s.item == null) continue;
                if (!HydrogenCanisterItem.IsPortableHydrogenTank(s.item)) continue;
                currentMl += HydrogenCanisterItem.GetStoredMl(s);
                capacityMl += HydrogenCanisterItem.GetCapacityMl(s);
            }
        }

        private static (VisualElement fill, Label val, Label icon) AddVitalBar(
            string iconText, Color fillColor, string abbrev)
        {
            var track = new VisualElement();
            track.style.height = 26;
            track.style.backgroundColor = new StyleColor(new Color(0.05f, 0.055f, 0.075f, 0.94f));
            track.style.overflow = Overflow.Hidden;
            T.Radius(track, 13f);
            T.Border(track, 1, new Color(fillColor.r, fillColor.g, fillColor.b, 0.22f));
            track.pickingMode = PickingMode.Ignore;

            var fill = new VisualElement();
            fill.style.position = Position.Absolute;
            fill.style.left = 0;
            fill.style.top = 0;
            fill.style.bottom = 0;
            fill.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
            fill.style.backgroundColor = new StyleColor(new Color(fillColor.r, fillColor.g, fillColor.b, 0.28f));
            T.Radius(fill, 13f);
            fill.pickingMode = PickingMode.Ignore;
            track.Add(fill);

            var shimmer = new VisualElement();
            shimmer.style.position = Position.Absolute;
            shimmer.style.top = 3;
            shimmer.style.bottom = 3;
            shimmer.style.right = 0;
            shimmer.style.width = 2;
            shimmer.style.backgroundColor = new StyleColor(new Color(fillColor.r, fillColor.g, fillColor.b, 0.60f));
            Radius(shimmer, 1);
            shimmer.pickingMode = PickingMode.Ignore;
            fill.Add(shimmer);

            var ico = new Label(iconText);
            ico.style.position = Position.Absolute;
            ico.style.left = 8;
            ico.style.top = 0;
            ico.style.bottom = 0;
            ico.style.fontSize = 13;
            ico.style.color = new StyleColor(new Color(fillColor.r, fillColor.g, fillColor.b, 0.90f));
            ico.style.unityTextAlign = TextAnchor.MiddleLeft;
            ico.pickingMode = PickingMode.Ignore;
            track.Add(ico);

            var abbrevLbl = new Label(abbrev);
            abbrevLbl.style.position = Position.Absolute;
            abbrevLbl.style.left = 28;
            abbrevLbl.style.top = 0;
            abbrevLbl.style.bottom = 0;
            abbrevLbl.style.fontSize = 8;
            abbrevLbl.style.letterSpacing = 1.0f;
            abbrevLbl.style.color = new StyleColor(new Color(fillColor.r, fillColor.g, fillColor.b, 0.55f));
            abbrevLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            abbrevLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            abbrevLbl.pickingMode = PickingMode.Ignore;
            track.Add(abbrevLbl);

            var val = new Label("0");
            val.style.position = Position.Absolute;
            val.style.right = 8;
            val.style.top = 0;
            val.style.bottom = 0;
            val.style.fontSize = 11;
            val.style.color = Color.white;
            val.style.unityTextAlign = TextAnchor.MiddleRight;
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
            if (max <= 0f) max = 1f;
            float t = Mathf.Clamp01(cur / max);
            T.SetFillPercent(fill, t);

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

        private static void UpdateBarMl(
            VisualElement fill, Label label,
            float curMl, float maxMl, ref float prev, Color fillColor)
        {
            if (maxMl <= 0f)
            {
                T.SetFillPercent(fill, 0f);
                fill.style.backgroundColor = new StyleColor(new Color(fillColor.r, fillColor.g, fillColor.b, 0.12f));
                if (prev != 0f) { label.text = "0 ml"; prev = 0f; }
                return;
            }

            float t = Mathf.Clamp01(curMl / maxMl);
            T.SetFillPercent(fill, t);

            Color displayColor = t > 0.5f ? fillColor :
                                 t > 0.25f ? Color.Lerp(T.AccentAmber, fillColor, (t - 0.25f) / 0.25f) :
                                 Color.Lerp(T.AccentRed, T.AccentAmber, t / 0.25f);
            fill.style.backgroundColor = new StyleColor(new Color(displayColor.r, displayColor.g, displayColor.b, 0.28f));

            bool shouldUpdate = !Mathf.Approximately(curMl, prev);
            // Prevent flash to 0 when fuel briefly drains during recharge cycle.
            if (curMl < 10f && prev > 100f && (prev - curMl) > 50f) shouldUpdate = false;
            if (shouldUpdate)
            {
                label.text = FormatMl(curMl);
                prev = curMl;
            }
        }

        private static string FormatMl(float ml)
        {
            if (ml >= 1000f) return $"{ml / 1000f:0.0} L";
            return $"{Mathf.RoundToInt(ml)} ml";
        }

        private static void AddGap(float h)
        {
            var s = new VisualElement();
            s.style.height = h;
            s.pickingMode = PickingMode.Ignore;
            _container.Add(s);
        }

        private static void Radius(VisualElement v, float r)
        {
            v.style.borderTopLeftRadius = v.style.borderTopRightRadius =
            v.style.borderBottomLeftRadius = v.style.borderBottomRightRadius = r;
        }
    }
}
