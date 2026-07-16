// Assets/Scripts/VoxelEngine/UI/ThemedPanel.cs
// Premium ThemedPanel base class — all premium UI panels should derive from this.
// Implements reactive theme application without scene reload.

using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    /// <summary>
    /// Base MonoBehaviour for any UI Toolkit panel that wants to react to global theme changes.
    /// Assigns shared PanelSettings, applies USS variables (--theme-accent, --theme-panel, etc.)
    /// and re-applies whenever UIThemeManager fires OnThemeChanged.
    /// </summary>
    public abstract class ThemedPanel : MonoBehaviour
    {
        [Header("Theming")]
        [Tooltip("If empty, the global theme (UIThemeManager.Current) is used.")]
        public bool usePerBlockOverride;
        public BuiltInUITheme overrideTheme = BuiltInUITheme.IndustrialSteel;
        public bool overrideAccent;
        public Color accentOverride = UITheme.AccentCyan;

        protected UIDocument Document { get; private set; }
        protected VisualElement Root => Document != null ? Document.rootVisualElement : null;

        protected virtual void Awake()
        {
            Document = GetComponent<UIDocument>();
            if (Document == null)
                Document = gameObject.AddComponent<UIDocument>();

            if (Document.panelSettings == null)
            {
                var ps = Resources.Load<PanelSettings>("MenuPanelSettings");
                if (ps != null) Document.panelSettings = ps;
            }
        }

        protected virtual void OnEnable()
        {
            UIThemeManager.OnThemeChanged += ApplyTheme;
            // Delay one frame so UIDocument has created its panel
            Invoke(nameof(ApplyTheme), 0.05f);
        }

        protected virtual void OnDisable()
        {
            UIThemeManager.OnThemeChanged -= ApplyTheme;
        }

        protected virtual void ApplyTheme()
        {
            if (Document == null) Document = GetComponent<UIDocument>();
            var root = Root;
            if (root == null) return;

            // Resolve effective theme
            BuiltInUITheme effective = usePerBlockOverride ? overrideTheme : UIThemeManager.Current;
            Color effectiveAccent = overrideAccent ? accentOverride : UIThemeManager.Accent;
            if (!usePerBlockOverride && UIThemeManager.CustomAccentEnabled)
                effectiveAccent = UIThemeManager.CustomAccent;

            Color panel = UIThemeManager.PanelFor(effective);
            panel.a = UIThemeManager.PanelOpacity;
            Color text = UIThemeManager.TextFor(effective);

            UIThemeApplier.ApplyThemeToRoot(root, effective, effectiveAccent, panel, text,
                UIThemeManager.CornerRadius, UIThemeManager.PanelOpacity);

            OnThemeApplied(effective, effectiveAccent, panel, text);
        }

        /// <summary>Called after theme has been applied to root. Override to re-style custom elements.</summary>
        protected virtual void OnThemeApplied(BuiltInUITheme theme, Color accent, Color panel, Color text) { }

        /// <summary>Helper: mark a visual element to use theme tokens instead of hard-coded colors.</summary>
        protected void MarkThemed(VisualElement ve, string semantic = "panel")
        {
            if (ve == null) return;
            ve.AddToClassList($"themed-{semantic}");
        }
    }

    /// <summary>
    /// Lightweight MonoBehaviour that only applies theme to an existing UIDocument without being a full panel.
    /// Useful for GameUIController, MainMenuController, PauseMenu etc. that already exist.
    /// </summary>
    public sealed class ThemedDocument : MonoBehaviour
    {
        private UIDocument _doc;
        private void Awake() => _doc = GetComponent<UIDocument>();
        private void OnEnable()
        {
            UIThemeManager.OnThemeChanged += Apply;
            Invoke(nameof(Apply), 0.05f);
        }
        private void OnDisable() => UIThemeManager.OnThemeChanged -= Apply;

        private void Apply()
        {
            if (_doc == null) _doc = GetComponent<UIDocument>();
            var root = _doc != null ? _doc.rootVisualElement : null;
            if (root == null) return;
            UIThemeApplier.ApplyThemeToRoot(root, UIThemeManager.Current, UIThemeManager.Accent,
                UIThemeManager.PanelColor, UIThemeManager.TextColor,
                UIThemeManager.CornerRadius, UIThemeManager.PanelOpacity);
        }
    }
}
