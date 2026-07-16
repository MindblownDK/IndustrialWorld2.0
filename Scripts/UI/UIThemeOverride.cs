// Assets/Scripts/VoxelEngine/UI/UIThemeOverride.cs
// Enhanced per-block UI override — supports accent override, built-in theme override, and icon style.
// Non-destructive setup wizard can add this to any machine/block prefab.

using UnityEngine;

namespace VoxelEngine.UI
{
    /// <summary>
    /// Optional per-block UI accent/theme override. Add to a machine/block prefab when
    /// a specific production zone or block type should use a distinct panel accent or theme.
    /// Used by crusher, assemblers, electric furnace, and future factory blocks via Stage 17 setup.
    /// </summary>
    public sealed class UIThemeOverride : MonoBehaviour
    {
        [Header("Theme Override")]
        [Tooltip("When true, the block uses overrideTheme instead of global theme.")]
        public bool overrideTheme;
        public BuiltInUITheme themeOverride = BuiltInUITheme.IndustrialSteel;

        [Header("Accent Override")]
        [Tooltip("When true, accentColor overrides the theme accent for this block's UI.")]
        public bool overrideAccent;
        public Color accentColor = UITheme.AccentCyan;

        [Header("Icon & Presentation")]
        [Tooltip("Optional icon style identifier (e.g., 'steel', 'amber', 'neon'). Used by themed panels.")]
        public string iconStyleOverride = "";
        [Tooltip("Optional display name suffix for production zone labeling.")]
        public string zoneLabel = "";

        [Tooltip("When true, this override also tints world-space status lights to match the accent.")]
        public bool tintStatusLights = false;

        public static Color ResolveAccent(Component owner, Color fallback)
        {
            if (owner == null) return fallback;
            var ov = owner.GetComponent<UIThemeOverride>();
            if (ov != null && ov.overrideAccent) return ov.accentColor;
            return fallback;
        }

        public static BuiltInUITheme ResolveTheme(Component owner, BuiltInUITheme fallback)
        {
            if (owner == null) return fallback;
            var ov = owner.GetComponent<UIThemeOverride>();
            if (ov != null && ov.overrideTheme) return ov.themeOverride;
            return fallback;
        }

        public static string ResolveIconStyle(Component owner)
        {
            if (owner == null) return null;
            var ov = owner.GetComponent<UIThemeOverride>();
            return string.IsNullOrWhiteSpace(ov?.iconStyleOverride) ? null : ov.iconStyleOverride;
        }

        public static UIThemeOverride Ensure(GameObject go)
        {
            if (go == null) return null;
            var existing = go.GetComponent<UIThemeOverride>();
            if (existing != null) return existing;
            var added = go.AddComponent<UIThemeOverride>();
            return added;
        }

        // Called by editor setup to configure without overwriting user custom colors unless requested.
        public void ConfigureIfDefault(BuiltInUITheme theme, Color accent, bool preserveUserEdits = true)
        {
            if (preserveUserEdits && (overrideAccent || overrideTheme))
                return; // keep user edits

            themeOverride = theme;
            accentColor = accent;
        }
    }
}
