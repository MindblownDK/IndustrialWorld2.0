// Assets/Scripts/VoxelEngine/UI/UIThemeOverride.cs

using UnityEngine;

namespace VoxelEngine.UI
{
    /// <summary>
    /// Optional per-block UI accent override. Add to a machine/block prefab when
    /// a specific production zone or block type should use a distinct panel accent.
    /// </summary>
    public sealed class UIThemeOverride : MonoBehaviour
    {
        public bool overrideAccent;
        public Color accentColor = UITheme.AccentCyan;
        public string iconStyleOverride;

        public static Color ResolveAccent(Component owner, Color fallback)
        {
            if (owner == null) return fallback;
            var theme = owner.GetComponent<UIThemeOverride>();
            if (theme != null && theme.overrideAccent) return theme.accentColor;
            return fallback;
        }
    }
}
