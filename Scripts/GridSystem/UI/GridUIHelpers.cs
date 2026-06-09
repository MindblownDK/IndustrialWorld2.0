// Assets/Scripts/VoxelEngine/GridSystem/UI/GridUIHelpers.cs
//
// Small shared UI Toolkit helpers used by every grid block panel — so the
// Chemical Plant, tanks, cargo, battery, docking port and the master terminal
// all look and behave consistently. Built on the existing UITheme palette.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem.UI
{
    public static class GridUIHelpers
    {
        /// <summary>A right-aligned "⚖ 12.5 t" weight badge for an inventory section.
        /// Returns the Label so callers can refresh its text each frame.</summary>
        public static Label WeightHeader(float kg, string prefix = "Weight")
        {
            var lbl = new Label($"⚖ {prefix}: {MassFormat.Format(kg)}");
            lbl.style.unityTextAlign          = TextAnchor.MiddleRight;
            lbl.style.fontSize                = 11;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.color                   = new StyleColor(new Color(0.75f, 0.82f, 0.9f));
            lbl.style.marginBottom            = 4;
            return lbl;
        }

        public static void SetWeight(Label lbl, float kg, string prefix = "Weight")
        {
            if (lbl != null) lbl.text = $"⚖ {prefix}: {MassFormat.Format(kg)}";
        }

        /// <summary>A "current / max" weight readout against a cap (turns amber/red as it fills).</summary>
        public static Label WeightCapHeader(float kg, float maxKg)
        {
            var lbl = new Label();
            SetWeightCap(lbl, kg, maxKg);
            lbl.style.unityTextAlign          = TextAnchor.MiddleRight;
            lbl.style.fontSize                = 11;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.marginBottom            = 4;
            return lbl;
        }

        public static void SetWeightCap(Label lbl, float kg, float maxKg)
        {
            if (lbl == null) return;
            lbl.text = $"⚖ {MassFormat.FormatRatio(kg, maxKg)}";
            float f = maxKg <= 0 ? 0 : kg / maxKg;
            Color c = f >= 0.99f ? new Color(0.95f, 0.35f, 0.3f)
                    : f >= 0.8f  ? new Color(0.95f, 0.75f, 0.3f)
                                 : new Color(0.75f, 0.82f, 0.9f);
            lbl.style.color = new StyleColor(c);
        }

        /// <summary>Section title row, e.g. "INVENTORY".</summary>
        public static Label SectionTitle(string text)
        {
            var lbl = new Label(text.ToUpper());
            lbl.style.fontSize                = 10;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.letterSpacing           = 1.5f;
            lbl.style.color                   = new StyleColor(new Color(0.55f, 0.6f, 0.68f));
            lbl.style.marginTop               = 6;
            lbl.style.marginBottom            = 3;
            return lbl;
        }
    }
}
