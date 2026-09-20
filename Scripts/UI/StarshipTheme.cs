// Assets/Scripts/VoxelEngine/UI/StarshipTheme.cs
//
// The starship instrument library: the UI matches the block, and ship systems
// wear deep-space naval chrome - bulkhead side-rails, a hull divider with a
// centred diamond, and vector meters that read a value as a needle position
// on a track instead of a bar or a row of cells. Static styling only (no
// scheduled anims), so live panels that rebuild on the machine cadence can
// call it safely.

using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    public static class StarshipTheme
    {
        public static readonly Color Starlight = new(0.75f, 0.92f, 1.00f, 1f);
        public static readonly Color SteelDim = new(0.35f, 0.45f, 0.55f, 1f);
        public static readonly Color VoidDeep = new(0.05f, 0.07f, 0.11f, 1f);

        /// <summary>Faint tint of an accent for hairlines behind bright chrome.</summary>
        public static Color Ghost(Color accent, float alpha = 0.35f)
        {
            return new Color(accent.r, accent.g, accent.b, alpha);
        }

        /// <summary>Bulkhead frame: two vertical side-rails with docking ticks
        /// top and bottom. The accent should follow block status, the way the
        /// high-tech holo frame does.</summary>
        public static void Frame(VisualElement panel, Color accent)
        {
            if (panel == null) return;
            // MachinePanel is already a positioned ancestor, so the rails
            // anchor to its edges.
            Rail(panel, accent, left: true);
            Rail(panel, accent, left: false);
            Tick(panel, accent, top: true);
            Tick(panel, accent, top: false);
        }

        private static void Rail(VisualElement panel, Color accent, bool left)
        {
            var rail = new VisualElement { name = "ThemeFrame" };
            rail.style.position = Position.Absolute;
            if (left) rail.style.left = 5f;
            else rail.style.right = 5f;
            rail.style.top = Length.Percent(18f);
            rail.style.height = Length.Percent(64f);
            rail.style.width = 2f;
            rail.style.backgroundColor = new StyleColor(Ghost(accent, 0.6f));
            rail.pickingMode = PickingMode.Ignore;
            panel.Add(rail);
        }

        private static void Tick(VisualElement panel, Color accent, bool top)
        {
            var tick = new VisualElement { name = "ThemeFrame" };
            tick.style.position = Position.Absolute;
            if (top) tick.style.top = 5f;
            else tick.style.bottom = 5f;
            tick.style.left = Length.Percent(50f);
            tick.style.marginLeft = -12f;
            tick.style.width = 24f;
            tick.style.height = 2f;
            tick.style.backgroundColor = new StyleColor(accent);
            tick.pickingMode = PickingMode.Ignore;
            panel.Add(tick);
        }

        /// <summary>A hull divider: hairlines parted by a centred diamond -
        /// the starship section break.</summary>
        public static VisualElement HullDivider(Color accent)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 2;
            row.style.marginBottom = 8;
            row.pickingMode = PickingMode.Ignore;

            var left = new VisualElement();
            left.style.flexGrow = 1;
            left.style.height = 1;
            left.style.backgroundColor = new StyleColor(Ghost(accent));
            left.pickingMode = PickingMode.Ignore;
            row.Add(left);

            var gem = new VisualElement();
            gem.style.width = 7;
            gem.style.height = 7;
            gem.style.marginLeft = 8;
            gem.style.marginRight = 8;
            gem.style.rotate = new Rotate(new Angle(45, AngleUnit.Degree));
            gem.style.backgroundColor = new StyleColor(accent);
            gem.pickingMode = PickingMode.Ignore;
            row.Add(gem);

            var right = new VisualElement();
            right.style.flexGrow = 1;
            right.style.height = 1;
            right.style.backgroundColor = new StyleColor(Ghost(accent));
            right.pickingMode = PickingMode.Ignore;
            row.Add(right);

            return row;
        }

        /// <summary>A vector meter: the value as a bright needle position on a
        /// track with a ghost trail - the starship readout for buffers,
        /// efficiency and fuel.</summary>
        public static VisualElement VectorMeter(string label, float fill01, Color color,
            string valueText)
        {
            float fill = Mathf.Clamp01(fill01);

            var col = new VisualElement();
            col.style.marginBottom = 6;
            col.pickingMode = PickingMode.Ignore;

            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;
            top.pickingMode = PickingMode.Ignore;

            var lab = new Label(label.ToUpper());
            lab.style.flexGrow = 1;
            lab.style.fontSize = 8;
            lab.style.letterSpacing = 1.2f;
            lab.style.unityFontStyleAndWeight = FontStyle.Bold;
            lab.style.color = new StyleColor(SteelDim);
            lab.pickingMode = PickingMode.Ignore;
            top.Add(lab);

            var val = new Label(valueText ?? string.Empty);
            val.style.fontSize = 10;
            val.style.unityFontStyleAndWeight = FontStyle.Bold;
            val.style.color = new StyleColor(color);
            val.pickingMode = PickingMode.Ignore;
            top.Add(val);
            col.Add(top);

            var track = new VisualElement();
            track.style.position = Position.Relative;
            track.style.height = 8;
            track.style.marginTop = 3;
            track.style.backgroundColor = new StyleColor(VoidDeep);
            track.style.overflow = Overflow.Hidden;
            track.pickingMode = PickingMode.Ignore;

            var trail = new VisualElement();
            trail.style.position = Position.Absolute;
            trail.style.left = 0;
            trail.style.top = 0;
            trail.style.bottom = 0;
            trail.style.width = Length.Percent(fill * 100f);
            trail.style.backgroundColor = new StyleColor(Ghost(color, 0.4f));
            trail.pickingMode = PickingMode.Ignore;
            track.Add(trail);

            var needle = new VisualElement();
            needle.style.position = Position.Absolute;
            needle.style.top = 0;
            needle.style.bottom = 0;
            needle.style.left = Length.Percent(fill * 100f);
            needle.style.width = 3;
            needle.style.marginLeft = -1.5f;
            needle.style.backgroundColor = new StyleColor(Starlight);
            needle.pickingMode = PickingMode.Ignore;
            track.Add(needle);

            col.Add(track);
            return col;
        }
    }
}
