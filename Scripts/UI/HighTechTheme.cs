// Assets/Scripts/VoxelEngine/UI/HighTechTheme.cs
//
// The high-tech instrument library: the UI matches the block, and advanced
// machines wear targeting-computer chrome - corner-bracket holo frames,
// scanline dividers, segmented cell meters and glowing numeric readouts.
// Static styling only (no scheduled anims), so live panels that rebuild on
// the machine cadence can call it safely.

using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    public static class HighTechTheme
    {
        public static readonly Color HoloCyan = new(0.30f, 0.90f, 1.00f, 1f);
        public static readonly Color HoloDim = new(0.16f, 0.45f, 0.55f, 1f);
        public static readonly Color CellEmpty = new(0.08f, 0.11f, 0.14f, 1f);

        /// <summary>Faint tint of an accent for hairlines behind bright chrome.</summary>
        public static Color Ghost(Color accent, float alpha = 0.35f)
        {
            return new Color(accent.r, accent.g, accent.b, alpha);
        }

        /// <summary>Corner-bracket holo frame: four L-brackets pinned to the
        /// panel corners, the targeting-computer silhouette. The accent should
        /// follow machine status (green running, red fault, dim idle).</summary>
        public static void Frame(VisualElement panel, Color accent)
        {
            if (panel == null) return;
            // MachinePanel is already a positioned ancestor, so the brackets
            // anchor to its corners. Inset slightly to sit on the card edge.
            Corner(panel, accent, left: 5f, top: 5f, right: null, bottom: null);
            Corner(panel, accent, left: null, top: 5f, right: 5f, bottom: null);
            Corner(panel, accent, left: 5f, top: null, right: null, bottom: 5f);
            Corner(panel, accent, left: null, top: null, right: 5f, bottom: 5f);
        }

        private static void Corner(VisualElement panel, Color accent,
            float? left, float? top, float? right, float? bottom)
        {
            const float len = 15f;
            const float thick = 2f;

            var h = new VisualElement();
            h.style.position = Position.Absolute;
            if (left.HasValue) h.style.left = left.Value;
            if (top.HasValue) h.style.top = top.Value;
            if (right.HasValue) h.style.right = right.Value;
            if (bottom.HasValue) h.style.bottom = bottom.Value;
            h.style.width = len;
            h.style.height = thick;
            h.style.backgroundColor = new StyleColor(accent);
            h.pickingMode = PickingMode.Ignore;
            panel.Add(h);

            var v = new VisualElement();
            v.style.position = Position.Absolute;
            if (left.HasValue) v.style.left = left.Value;
            if (top.HasValue) v.style.top = top.Value;
            if (right.HasValue) v.style.right = right.Value;
            if (bottom.HasValue) v.style.bottom = bottom.Value;
            v.style.width = thick;
            v.style.height = len;
            v.style.backgroundColor = new StyleColor(accent);
            v.pickingMode = PickingMode.Ignore;
            panel.Add(v);
        }

        /// <summary>A scanline divider: dim hairlines with one bright segment,
        /// the high-tech section break.</summary>
        public static VisualElement ScanDivider(Color accent)
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

            var scan = new VisualElement();
            scan.style.width = 76;
            scan.style.height = 2;
            scan.style.marginLeft = 6;
            scan.style.marginRight = 6;
            scan.style.backgroundColor = new StyleColor(accent);
            scan.pickingMode = PickingMode.Ignore;
            row.Add(scan);

            var right = new VisualElement();
            right.style.flexGrow = 1;
            right.style.height = 1;
            right.style.backgroundColor = new StyleColor(Ghost(accent));
            right.pickingMode = PickingMode.Ignore;
            row.Add(right);

            return row;
        }

        /// <summary>A segmented cell meter: discrete glowing cells instead of a
        /// continuous bar - the reactor-control readout for fuel, rods and
        /// buffers.</summary>
        public static VisualElement SegmentMeter(string label, float fill01, Color color,
            string valueText, int segments = 12)
        {
            float fill = Mathf.Clamp01(fill01);
            int segs = Mathf.Max(4, segments);

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
            lab.style.color = new StyleColor(HoloDim);
            lab.pickingMode = PickingMode.Ignore;
            top.Add(lab);

            var val = new Label(valueText ?? string.Empty);
            val.style.fontSize = 10;
            val.style.unityFontStyleAndWeight = FontStyle.Bold;
            val.style.color = new StyleColor(color);
            val.pickingMode = PickingMode.Ignore;
            top.Add(val);
            col.Add(top);

            var cells = new VisualElement();
            cells.style.flexDirection = FlexDirection.Row;
            cells.style.marginTop = 3;
            cells.pickingMode = PickingMode.Ignore;
            int on = Mathf.RoundToInt(fill * segs);
            for (int i = 0; i < segs; i++)
            {
                var cell = new VisualElement();
                cell.style.flexGrow = 1;
                cell.style.height = 10;
                if (i < segs - 1) cell.style.marginRight = 2;
                cell.style.backgroundColor = new StyleColor(i < on ? color : CellEmpty);
                cell.pickingMode = PickingMode.Ignore;
                cells.Add(cell);
            }
            col.Add(cells);

            return col;
        }

        /// <summary>A hero numeric readout: letter-spaced caption over large
        /// glowing digits with an accent underline. For the one number the
        /// operator watches - core temp, power output.</summary>
        public static VisualElement Readout(string label, string value, Color accent)
        {
            var col = new VisualElement();
            col.style.marginBottom = 6;
            col.pickingMode = PickingMode.Ignore;

            var lab = new Label(label.ToUpper());
            lab.style.fontSize = 8;
            lab.style.letterSpacing = 1.4f;
            lab.style.unityFontStyleAndWeight = FontStyle.Bold;
            lab.style.color = new StyleColor(Ghost(accent, 0.8f));
            lab.pickingMode = PickingMode.Ignore;
            col.Add(lab);

            var val = new Label(value ?? string.Empty);
            val.style.fontSize = 24;
            val.style.unityFontStyleAndWeight = FontStyle.Bold;
            val.style.color = new StyleColor(accent);
            val.pickingMode = PickingMode.Ignore;
            col.Add(val);

            var under = new VisualElement();
            under.style.width = 46;
            under.style.height = 2;
            under.style.marginTop = 2;
            under.style.backgroundColor = new StyleColor(accent);
            under.pickingMode = PickingMode.Ignore;
            col.Add(under);

            return col;
        }
    }
}
