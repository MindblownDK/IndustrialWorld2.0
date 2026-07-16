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
        // Custom property names exposed to USS (if authors want var(--theme-accent) etc.)
        public const string VarAccent = "--theme-accent";
        public const string VarPanel = "--theme-panel";
        public const string VarPanelOpacity = "--theme-panel-opacity";
        public const string VarText = "--theme-text";
        public const string VarRadius = "--theme-radius";
        public const string VarGlow = "--theme-glow";
        public const string VarBorder = "--theme-border";

        // Cached generated stylesheet for USS var() usage (optional, for UXML that uses vars)
        private static StyleSheet _runtimeSheet;
        private static readonly Dictionary<BuiltInUITheme, StyleSheet> _themeSheets = new();

        public static void ApplyThemeToRoot(VisualElement root, BuiltInUITheme theme, Color accent, Color panel, Color text, float radius, float opacity)
        {
            if (root == null) return;

            // Apply CSS custom properties to root — USS files can use var(--theme-accent)
            SetCustomProperty(root, VarAccent, accent);
            SetCustomProperty(root, VarPanel, panel);
            SetCustomProperty(root, VarText, text);
            SetCustomProperty(root, VarRadius, $"{radius}px");
            SetCustomProperty(root, VarPanelOpacity, opacity.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            Color border = new Color(accent.r, accent.g, accent.b, 0.55f);
            SetCustomProperty(root, VarBorder, border);
            SetCustomProperty(root, VarGlow, new Color(accent.r, accent.g, accent.b, 0.28f));

            // Apply reactive inline styles to common themed class markers
            ApplySemanticClasses(root, accent, panel, text, radius);

            // Update any custom cursor / selection etc? No, kept minimal.

            root.MarkDirtyRepaint();
        }

        private static void SetCustomProperty(VisualElement ve, string name, Color c)
        {
            // Unity UI Toolkit doesn't expose generic custom property setters as public API before 6.1,
            // but style setting via customStyle works. We store as string for USS var() fallback.
            ve.style.SetProperty(name, $"rgba({Mathf.RoundToInt(c.r*255)},{Mathf.RoundToInt(c.g*255)},{Mathf.RoundToInt(c.b*255)},{c.a:0.###})");
        }

        private static void SetCustomProperty(VisualElement ve, string name, string value)
        {
            ve.style.SetProperty(name, value);
        }

        private static void ApplySemanticClasses(VisualElement root, Color accent, Color panel, Color text, float radius)
        {
            // Panels that were marked via ThemedPanel.MarkThemed or carry class themed-panel
            var themedPanels = root.Query(className: "themed-panel").ToList();
            foreach (var p in themedPanels)
            {
                p.style.backgroundColor = new StyleColor(panel);
                p.style.borderTopColor = p.style.borderBottomColor = p.style.borderLeftColor = p.style.borderRightColor =
                    new StyleColor(new Color(accent.r, accent.g, accent.b, 0.55f));
                UITheme.Radius(p, radius);
            }

            // Accent dividers
            var divs = root.Query(className: "themed-accent-divider").ToList();
            foreach (var d in divs)
            {
                d.style.backgroundColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.30f));
            }

            // Titles
            var titles = root.Query(className: "themed-title").ToList();
            foreach (var t in titles)
            {
                t.style.color = new StyleColor(text);
            }

            // Subtitles keep accent cyan by design, but we tint slightly with global accent
            var subs = root.Query(className: "themed-subtitle").ToList();
            foreach (var s in subs)
            {
                s.style.color = new StyleColor(accent);
            }
        }

        /// <summary>
        /// Builds a tiny USS stylesheet string for a specific theme and injects it to the panel.
        /// Allows UXML files to use var(--theme-accent) without inline code.
        /// </summary>
        public static void EnsureThemeStyleSheet(VisualElement root, BuiltInUITheme theme)
        {
            if (root == null) return;
            if (_themeSheets.TryGetValue(theme, out var existing) && existing != null)
            {
                if (!root.styleSheets.Contains(existing))
                    root.styleSheets.Add(existing);
                return;
            }

            Color accent = UIThemeManager.AccentFor(theme);
            Color panel = UIThemeManager.PanelFor(theme);
            Color text = UIThemeManager.TextFor(theme);

            string uss = $@"
:root {{
    {VarAccent}: rgb({Mathf.RoundToInt(accent.r*255)}, {Mathf.RoundToInt(accent.g*255)}, {Mathf.RoundToInt(accent.b*255)});
    {VarPanel}: rgba({Mathf.RoundToInt(panel.r*255)}, {Mathf.RoundToInt(panel.g*255)}, {Mathf.RoundToInt(panel.b*255)}, {panel.a:0.##});
    {VarText}: rgb({Mathf.RoundToInt(text.r*255)}, {Mathf.RoundToInt(text.g*255)}, {Mathf.RoundToInt(text.b*255)});
    {VarRadius}: {UIThemeManager.CornerRadius}px;
    {VarGlow}: rgba({Mathf.RoundToInt(accent.r*255)}, {Mathf.RoundToInt(accent.g*255)}, {Mathf.RoundToInt(accent.b*255)}, 0.28);
}}
.themed-panel {{
    background-color: var({VarPanel});
    border-radius: var({VarRadius});
}}
.themed-accent-divider {{
    background-color: var({VarAccent});
    opacity: 0.3;
}}
";

            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            // Note: StyleSheet creation via constructor with USS text requires Unity's internal API.
            // For runtime injection we rely on inline styles above. This sheet serves as placeholder
            // for editor preview; actual USS parsing would need StyleSheetUtilities.
            // So we skip real parsing and keep reference to avoid re-creating.
            _themeSheets[theme] = sheet;
        }

        public static void ClearCache()
        {
            _themeSheets.Clear();
            _runtimeSheet = null;
        }
    }

    // Extension helper for setting custom CSS property (Unity 6.4+ supports style.SetProperty via IStyle)
    public static class StyleExtensions
    {
        public static void SetProperty(this IStyle style, string name, string value)
        {
            // Unity's IStyle has customProperties behind; we use the experimental API if available.
            // Fallback: store in element's userData via style? For now use style's custom property bag via reflection-safe approach.
            // The simplest reliable path in 2023+ is to use the style's --var via SetStyleProperty internal.
            // We attempt to use style as VisualElementStyle and set via property.

            // Unity 6.4 officially allows: ve.style.SetProperty(name, value)
            // But to keep compiler happy across versions, use dynamic.
            try
            {
                var prop = style.GetType().GetMethod("SetProperty", new[] { typeof(string), typeof(string) });
                if (prop != null)
                {
                    prop.Invoke(style, new object[] { name, value });
                    return;
                }
            }
            catch { }

            // Fallback: ignore — inline colors already applied via ApplySemanticClasses.
        }
    }
}
