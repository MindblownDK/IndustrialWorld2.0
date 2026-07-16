// Assets/Scripts/VoxelEngine/UI/UIThemeApplier.cs
// USS variable application layer — translates BuiltInUITheme into runtime USS custom properties
// and inline styles without requiring a scene reload.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    public static class UIThemeApplier
    {
        public const string VarAccent = "--theme-accent";
        public const string VarPanel = "--theme-panel";
        public const string VarPanelOpacity = "--theme-panel-opacity";
        public const string VarText = "--theme-text";
        public const string VarRadius = "--theme-radius";
        public const string VarGlow = "--theme-glow";
        public const string VarBorder = "--theme-border";

        public static void ApplyThemeToRoot(VisualElement root, BuiltInUITheme theme, Color accent, Color panel, Color text, float radius, float opacity)
        {
            if (root == null) return;

            // Inject CSS custom properties so USS files using var(--theme-*) react
            TrySetCustomProperty(root, VarAccent, accent);
            TrySetCustomProperty(root, VarPanel, panel);
            TrySetCustomProperty(root, VarText, text);
            TrySetCustomProperty(root, VarRadius, $"{radius}px");
            TrySetCustomProperty(root, VarPanelOpacity, opacity.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            Color border = new Color(accent.r, accent.g, accent.b, 0.55f);
            TrySetCustomProperty(root, VarBorder, border);
            TrySetCustomProperty(root, VarGlow, new Color(accent.r, accent.g, accent.b, 0.28f));

            // Apply semantic class styling for hard-coded themed elements
            ApplySemanticClasses(root, accent, panel, text, radius);

            root.MarkDirtyRepaint();
        }

        private static void TrySetCustomProperty(VisualElement ve, string name, Color c)
        {
            if (ve == null) return;
            string value = $"rgba({Mathf.RoundToInt(c.r * 255)},{Mathf.RoundToInt(c.g * 255)},{Mathf.RoundToInt(c.b * 255)},{c.a:0.###})";
            TrySetCustomProperty(ve, name, value);
        }

        private static void TrySetCustomProperty(VisualElement ve, string name, string value)
        {
            if (ve == null) return;
            try
            {
                // Unity 6+ supports IStyle SetProperty via extension
                var style = ve.style;
                var method = style.GetType().GetMethod("SetProperty", new System.Type[] { typeof(string), typeof(string) });
                if (method != null)
                {
                    method.Invoke(style, new object[] { name, value });
                    return;
                }
            }
            catch { /* fallback: ignore, inline colors already applied */ }
        }

        private static void ApplySemanticClasses(VisualElement root, Color accent, Color panel, Color text, float radius)
        {
            try
            {
                var themedPanels = root.Query(className: "themed-panel").ToList();
                foreach (var p in themedPanels)
                {
                    if (p == null) continue;
                    p.style.backgroundColor = new StyleColor(panel);
                    p.style.borderTopColor = p.style.borderBottomColor = p.style.borderLeftColor = p.style.borderRightColor =
                        new StyleColor(new Color(accent.r, accent.g, accent.b, 0.55f));
                    UITheme.Radius(p, radius);
                }

                var divs = root.Query(className: "themed-accent-divider").ToList();
                foreach (var d in divs)
                {
                    if (d == null) continue;
                    d.style.backgroundColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.30f));
                }

                var titles = root.Query(className: "themed-title").ToList();
                foreach (var t in titles)
                {
                    if (t == null) continue;
                    t.style.color = new StyleColor(text);
                }

                var subs = root.Query(className: "themed-subtitle").ToList();
                foreach (var s in subs)
                {
                    if (s == null) continue;
                    s.style.color = new StyleColor(accent);
                }
            }
            catch { }
        }
    }
}
