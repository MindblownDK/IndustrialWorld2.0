// Assets/Scripts/VoxelEngine/UI/IndustrialTheme.cs
//
// The industrial instrument library: the UI matches the block, and workhorse
// machines wear factory chrome - steel girder frames with bolt dots, hazard
// dividers and a tri-lamp status stack like a control cabinet. Static styling
// only (no scheduled anims), so live panels that rebuild on the machine
// cadence can call it safely.

using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    public static class IndustrialTheme
    {
        public static readonly Color Steel = new(0.34f, 0.37f, 0.42f, 1f);
        public static readonly Color SteelDark = new(0.16f, 0.18f, 0.21f, 1f);
        public static readonly Color HazardYellow = new(0.95f, 0.72f, 0.10f, 1f);

        /// <summary>Faint tint of a color for hairlines behind bright chrome.</summary>
        public static Color Ghost(Color color, float alpha = 0.35f)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        /// <summary>Steel girder frame: heavy top/bottom beams and side posts
        /// with bolt dots along the beams. Named so scroll-wrapping dispatchers
        /// leave it on the panel instead of moving it into the scroller.</summary>
        public static void Frame(VisualElement panel)
        {
            if (panel == null) return;
            // MachinePanel is already a positioned ancestor, so the beams
            // anchor to its edges, inside the card border.
            Beam(panel, top: true);
            Beam(panel, top: false);
            Post(panel, left: true);
            Post(panel, left: false);
            foreach (float pct in new[] { 14f, 32f, 50f, 68f, 86f })
            {
                Bolt(panel, pct, top: true);
                Bolt(panel, pct, top: false);
            }
        }

        private static void Beam(VisualElement panel, bool top)
        {
            var beam = new VisualElement { name = "ThemeFrame" };
            beam.style.position = Position.Absolute;
            if (top) beam.style.top = 3f;
            else beam.style.bottom = 3f;
            beam.style.left = 3f;
            beam.style.right = 3f;
            beam.style.height = 5f;
            beam.style.backgroundColor = new StyleColor(SteelDark);
            beam.pickingMode = PickingMode.Ignore;
            panel.Add(beam);
        }

        private static void Post(VisualElement panel, bool left)
        {
            var post = new VisualElement { name = "ThemeFrame" };
            post.style.position = Position.Absolute;
            if (left) post.style.left = 3f;
            else post.style.right = 3f;
            post.style.top = 3f;
            post.style.bottom = 3f;
            post.style.width = 5f;
            post.style.backgroundColor = new StyleColor(SteelDark);
            post.pickingMode = PickingMode.Ignore;
            panel.Add(post);
        }

        private static void Bolt(VisualElement panel, float percentFromLeft, bool top)
        {
            var bolt = new VisualElement { name = "ThemeFrame" };
            bolt.style.position = Position.Absolute;
            if (top) bolt.style.top = 3.5f;
            else bolt.style.bottom = 3.5f;
            bolt.style.left = Length.Percent(percentFromLeft);
            bolt.style.marginLeft = -2f;
            bolt.style.width = 4f;
            bolt.style.height = 4f;
            bolt.style.backgroundColor = new StyleColor(Steel);
            UITheme.Radius(bolt, 2f);
            bolt.pickingMode = PickingMode.Ignore;
            panel.Add(bolt);
        }

        /// <summary>A hazard divider: steel hairlines parted by a caution
        /// block cluster - the industrial section break.</summary>
        public static VisualElement HazardDivider()
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
            left.style.backgroundColor = new StyleColor(Ghost(Steel, 0.6f));
            left.pickingMode = PickingMode.Ignore;
            row.Add(left);

            var cluster = new VisualElement();
            cluster.style.flexDirection = FlexDirection.Row;
            cluster.style.marginLeft = 8;
            cluster.style.marginRight = 8;
            cluster.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < 4; i++)
            {
                var block = new VisualElement();
                block.style.width = 8;
                block.style.height = 8;
                if (i < 3) block.style.marginRight = 2;
                block.style.backgroundColor = new StyleColor(
                    i % 2 == 0 ? HazardYellow : SteelDark);
                UITheme.Border(block, 1f, Ghost(Steel, 0.5f));
                block.pickingMode = PickingMode.Ignore;
                cluster.Add(block);
            }
            row.Add(cluster);

            var right = new VisualElement();
            right.style.flexGrow = 1;
            right.style.height = 1;
            right.style.backgroundColor = new StyleColor(Ghost(Steel, 0.6f));
            right.pickingMode = PickingMode.Ignore;
            row.Add(right);

            return row;
        }

        /// <summary>A tri-lamp status stack: red / amber / green cabinet lamps
        /// with one lit. lit: 0 fault, 1 idle, 2 running.</summary>
        public static VisualElement Lamps(int lit)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 2;
            row.style.marginBottom = 6;
            row.pickingMode = PickingMode.Ignore;

            Lamp(row, UITheme.AccentRed, lit == 0);
            Lamp(row, UITheme.AccentAmber, lit == 1);
            Lamp(row, UITheme.AccentGreen, lit == 2);

            return row;
        }

        private static void Lamp(VisualElement row, Color color, bool on)
        {
            var lamp = new VisualElement();
            lamp.style.width = 12;
            lamp.style.height = 12;
            lamp.style.marginRight = 6;
            lamp.style.backgroundColor = new StyleColor(
                on ? color : Ghost(color, 0.22f));
            UITheme.Radius(lamp, 6f);
            if (on) UITheme.Border(lamp, 1f, color);
            lamp.pickingMode = PickingMode.Ignore;
            row.Add(lamp);
        }
    }
}
