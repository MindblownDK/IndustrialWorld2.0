// Assets/Scripts/VoxelEngine/UI/UIThemeDefinition.cs
// Expanded theme definition — 10 built-in themes + custom properties + reactive manager.

using System;
using UnityEngine;

namespace VoxelEngine.UI
{
    public enum BuiltInUITheme
    {
        IndustrialSteel,
        MidnightOperator,
        HazardAmber,
        ArcticFrost,
        BioLuminescent,
        MilitaryOlive,
        NeonCyber,
        CorporateClean,
        RustBelt,
        VoidBlack
    }

    [CreateAssetMenu(menuName = "Voxel Engine/UI/Theme Definition", fileName = "ThemeDefinition_New")]
    public class UIThemeDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Industrial Steel";
        public BuiltInUITheme builtInTheme = BuiltInUITheme.IndustrialSteel;

        [Header("Core Colors")]
        public Color accent = new(0.18f, 0.72f, 0.88f);
        public Color panel = new(0.08f, 0.088f, 0.12f, 0.97f);
        public Color text = new(0.92f, 0.94f, 0.97f);
        public Color border = new(0.18f, 0.21f, 0.28f, 0.85f);
        public Color background = new(0.04f, 0.045f, 0.06f, 1f);

        [Header("Shape")]
        [Range(0f, 1f)] public float panelOpacity = 0.97f;
        [Range(0f, 24f)] public float cornerRadius = 12f;
        [Range(0f, 8f)] public float borderThickness = 1f;

        [Header("Effects")]
        [Range(0f, 1f)] public float accentGlow = 0.28f;
        [Range(0f, 1f)] public float backgroundDim = 0.55f;
        [Tooltip("Animation speed multiplier for theme transitions (1 = normal).")]
        [Range(0.2f, 3f)] public float animationSpeed = 1f;
        public AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Typography (Optional)")]
        [Tooltip("Optional custom font reference — if null, default UI font is used.")]
        public Font customFont;
        [Tooltip("USS font asset name for UI Toolkit (e.g., 'Inter-Regular'). Leave empty for default.")]
        public string fontAssetName = "";
        [Range(8f, 18f)] public float baseFontSize = 12f;

        [Header("Preview")]
        [Tooltip("Short description shown in theme selector.")]
        [TextArea] public string description = "";

        public void ApplyToManager()
        {
            UIThemeManager.ApplyDefinition(this);
        }

        public static UIThemeDefinition CreateDefault(BuiltInUITheme theme)
        {
            var def = ScriptableObject.CreateInstance<UIThemeDefinition>();
            def.builtInTheme = theme;
            def.displayName = UIThemeManager.Label(theme);
            def.accent = UIThemeManager.AccentFor(theme);
            def.panel = UIThemeManager.PanelFor(theme);
            def.text = UIThemeManager.TextFor(theme);
            def.border = new Color(def.accent.r, def.accent.g, def.accent.b, 0.55f);
            def.background = UITheme.BgBase;
            def.panelOpacity = def.panel.a;
            def.cornerRadius = UITheme.PanelRadius;
            def.borderThickness = 1f;
            def.accentGlow = 0.28f;
            def.backgroundDim = 0.55f;
            def.animationSpeed = 1f;
            def.transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
            def.baseFontSize = 12f;
            def.description = $"{def.displayName} — premium industrial theme.";
            return def;
        }
    }

    public static class UIThemeManager
    {
        private const string ThemeKey = "IndustrialWorld.UITheme";
        private const string CustomAccentEnabledKey = "IndustrialWorld.UITheme.CustomAccentEnabled";
        private const string CustomAccentRKey = "IndustrialWorld.UITheme.CustomAccentR";
        private const string CustomAccentGKey = "IndustrialWorld.UITheme.CustomAccentG";
        private const string CustomAccentBKey = "IndustrialWorld.UITheme.CustomAccentB";
        private const string PanelOpacityKey = "IndustrialWorld.UITheme.PanelOpacity";
        private const string CornerRadiusKey = "IndustrialWorld.UITheme.CornerRadius";
        private const string AccentGlowKey = "IndustrialWorld.UITheme.AccentGlow";
        private const string AnimationSpeedKey = "IndustrialWorld.UITheme.AnimationSpeed";

        private static bool _loaded;
        private static BuiltInUITheme _current = BuiltInUITheme.IndustrialSteel;
        private static bool _customAccentEnabled;
        private static Color _customAccent = UITheme.AccentCyan;
        private static float _panelOpacity = 0.97f;
        private static float _cornerRadius = UITheme.PanelRadius;
        private static float _accentGlow = 0.28f;
        private static float _animationSpeed = 1f;

        public static event Action OnThemeChanged;
        public static event Action<UIThemeDefinition> OnDefinitionApplied;

        // Cache of loaded definitions for fast lookup
        private static UIThemeDatabase _database;

        public static BuiltInUITheme Current
        {
            get { EnsureLoaded(); return _current; }
            set
            {
                EnsureLoaded();
                if (_current == value) return;
                _current = value;
                PlayerPrefs.SetInt(ThemeKey, (int)_current);
                PlayerPrefs.Save();
                NotifyChanged();
            }
        }

        public static string CurrentLabel => Label(Current);
        public static bool CustomAccentEnabled
        {
            get { EnsureLoaded(); return _customAccentEnabled; }
            set { EnsureLoaded(); _customAccentEnabled = value; SaveCustomAccent(); NotifyChanged(); }
        }
        public static Color CustomAccent
        {
            get { EnsureLoaded(); return _customAccent; }
            set { EnsureLoaded(); _customAccent = value; SaveCustomAccent(); NotifyChanged(); }
        }
        public static Color Accent => CustomAccentEnabled ? CustomAccent : AccentFor(Current);

        public static float PanelOpacity
        {
            get { EnsureLoaded(); return _panelOpacity; }
            set { EnsureLoaded(); _panelOpacity = Mathf.Clamp(value, 0.45f, 1f); SaveAdvancedTheme(); NotifyChanged(); }
        }
        public static float CornerRadius
        {
            get { EnsureLoaded(); return _cornerRadius; }
            set { EnsureLoaded(); _cornerRadius = Mathf.Clamp(value, 2f, 24f); SaveAdvancedTheme(); NotifyChanged(); }
        }

        public static float AccentGlow
        {
            get { EnsureLoaded(); return _accentGlow; }
            set { EnsureLoaded(); _accentGlow = Mathf.Clamp(value, 0f, 1f); SaveAdvancedTheme(); NotifyChanged(); }
        }

        public static float AnimationSpeed
        {
            get { EnsureLoaded(); return _animationSpeed; }
            set { EnsureLoaded(); _animationSpeed = Mathf.Clamp(value, 0.2f, 3f); SaveAdvancedTheme(); NotifyChanged(); }
        }

        public static Color PanelColor
        {
            get
            {
                Color c = PanelFor(Current);
                c.a = PanelOpacity;
                return c;
            }
        }
        public static Color TextColor => TextFor(Current);
        public static Color BorderColor => new Color(Accent.r, Accent.g, Accent.b, 0.55f);

        public static void Next()
        {
            int count = Enum.GetValues(typeof(BuiltInUITheme)).Length;
            Current = (BuiltInUITheme)(((int)Current + 1) % count);
        }

        public static string Label(BuiltInUITheme theme) => theme switch
        {
            BuiltInUITheme.MidnightOperator => "Midnight Operator",
            BuiltInUITheme.HazardAmber => "Hazard Amber",
            BuiltInUITheme.ArcticFrost => "Arctic Frost",
            BuiltInUITheme.BioLuminescent => "Bio-Luminescent",
            BuiltInUITheme.MilitaryOlive => "Military Olive",
            BuiltInUITheme.NeonCyber => "Neon Cyber",
            BuiltInUITheme.CorporateClean => "Corporate Clean",
            BuiltInUITheme.RustBelt => "Rust Belt",
            BuiltInUITheme.VoidBlack => "Void Black",
            _ => "Industrial Steel"
        };

        public static Color PanelFor(BuiltInUITheme theme) => theme switch
        {
            BuiltInUITheme.CorporateClean => new Color(0.86f, 0.88f, 0.92f, 0.96f),
            BuiltInUITheme.VoidBlack => new Color(0.015f, 0.016f, 0.024f, 0.98f),
            BuiltInUITheme.ArcticFrost => new Color(0.07f, 0.10f, 0.13f, 0.96f),
            BuiltInUITheme.RustBelt => new Color(0.11f, 0.075f, 0.055f, 0.97f),
            BuiltInUITheme.MilitaryOlive => new Color(0.07f, 0.085f, 0.065f, 0.97f),
            _ => UITheme.BgPanel
        };

        public static Color TextFor(BuiltInUITheme theme) => theme switch
        {
            BuiltInUITheme.CorporateClean => new Color(0.08f, 0.10f, 0.14f, 1f),
            _ => UITheme.TextPrimary
        };

        public static Color AccentFor(BuiltInUITheme theme) => theme switch
        {
            BuiltInUITheme.MidnightOperator => new Color(0.40f, 0.52f, 0.92f),
            BuiltInUITheme.HazardAmber => UITheme.AccentGold,
            BuiltInUITheme.ArcticFrost => new Color(0.50f, 0.82f, 1.00f),
            BuiltInUITheme.BioLuminescent => new Color(0.22f, 0.95f, 0.62f),
            BuiltInUITheme.MilitaryOlive => new Color(0.58f, 0.68f, 0.36f),
            BuiltInUITheme.NeonCyber => new Color(0.95f, 0.24f, 0.85f),
            BuiltInUITheme.CorporateClean => new Color(0.28f, 0.52f, 0.88f),
            BuiltInUITheme.RustBelt => new Color(0.88f, 0.42f, 0.18f),
            BuiltInUITheme.VoidBlack => new Color(0.62f, 0.42f, 1.00f),
            _ => UITheme.AccentCyan
        };

        public static string DescriptionFor(BuiltInUITheme theme) => theme switch
        {
            BuiltInUITheme.IndustrialSteel => "Default premium steel — cool cyan, dark panels, high contrast.",
            BuiltInUITheme.MidnightOperator => "Deep void navy with soft periwinkle accents for night ops.",
            BuiltInUITheme.HazardAmber => "High-visibility amber warning theme for hazardous zones.",
            BuiltInUITheme.ArcticFrost => "Icy cyan-blue light on near-black for cold-world outposts.",
            BuiltInUITheme.BioLuminescent => "Vivid bio-green glow inspired by alien flora.",
            BuiltInUITheme.MilitaryOlive => "Tactical olive drab with muted lime accents.",
            BuiltInUITheme.NeonCyber => "Hot magenta cyberpunk with electric purple haze.",
            BuiltInUITheme.CorporateClean => "Light corporate panels with cobalt accent for bright labs.",
            BuiltInUITheme.RustBelt => "Weathered rust orange over deep iron-brown.",
            BuiltInUITheme.VoidBlack => "Pure void black with violet-white highlights for orbital stations.",
            _ => "Premium industrial theme."
        };

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            int saved = PlayerPrefs.GetInt(ThemeKey, (int)BuiltInUITheme.IndustrialSteel);
            _current = Enum.IsDefined(typeof(BuiltInUITheme), saved)
                ? (BuiltInUITheme)saved
                : BuiltInUITheme.IndustrialSteel;
            _customAccentEnabled = PlayerPrefs.GetInt(CustomAccentEnabledKey, 0) != 0;
            _customAccent = new Color(
                PlayerPrefs.GetFloat(CustomAccentRKey, UITheme.AccentCyan.r),
                PlayerPrefs.GetFloat(CustomAccentGKey, UITheme.AccentCyan.g),
                PlayerPrefs.GetFloat(CustomAccentBKey, UITheme.AccentCyan.b),
                1f);
            _panelOpacity = Mathf.Clamp(PlayerPrefs.GetFloat(PanelOpacityKey, _panelOpacity), 0.45f, 1f);
            _cornerRadius = Mathf.Clamp(PlayerPrefs.GetFloat(CornerRadiusKey, _cornerRadius), 2f, 24f);
            _accentGlow = Mathf.Clamp(PlayerPrefs.GetFloat(AccentGlowKey, _accentGlow), 0f, 1f);
            _animationSpeed = Mathf.Clamp(PlayerPrefs.GetFloat(AnimationSpeedKey, _animationSpeed), 0.2f, 3f);

            // Load database attempt (graceful if missing)
            _database = UIThemeDatabase.Load();
        }

        private static void SaveCustomAccent()
        {
            PlayerPrefs.SetInt(CustomAccentEnabledKey, _customAccentEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(CustomAccentRKey, _customAccent.r);
            PlayerPrefs.SetFloat(CustomAccentGKey, _customAccent.g);
            PlayerPrefs.SetFloat(CustomAccentBKey, _customAccent.b);
            SaveAdvancedTheme();
        }

        private static void SaveAdvancedTheme()
        {
            PlayerPrefs.SetFloat(PanelOpacityKey, _panelOpacity);
            PlayerPrefs.SetFloat(CornerRadiusKey, _cornerRadius);
            PlayerPrefs.SetFloat(AccentGlowKey, _accentGlow);
            PlayerPrefs.SetFloat(AnimationSpeedKey, _animationSpeed);
            PlayerPrefs.Save();
        }

        public static void ApplyDefinition(UIThemeDefinition def)
        {
            if (def == null) return;
            EnsureLoaded();
            _current = def.builtInTheme;
            PlayerPrefs.SetInt(ThemeKey, (int)_current);
            _customAccent = def.accent;
            _panelOpacity = def.panelOpacity;
            _cornerRadius = def.cornerRadius;
            _accentGlow = def.accentGlow;
            _animationSpeed = def.animationSpeed;
            PlayerPrefs.Save();
            NotifyChanged(def);
        }

        private static void NotifyChanged(UIThemeDefinition def = null)
        {
            try
            {
                OnDefinitionApplied?.Invoke(def ?? GetCurrentDefinition());
            }
            catch (Exception ex) { Debug.LogWarning($"[UIThemeManager] OnDefinitionApplied failed: {ex.Message}"); }

            try
            {
                OnThemeChanged?.Invoke();
            }
            catch (Exception ex) { Debug.LogWarning($"[UIThemeManager] OnThemeChanged failed: {ex.Message}"); }
        }

        public static UIThemeDefinition GetCurrentDefinition()
        {
            EnsureLoaded();
            if (_database != null)
            {
                var d = _database.Get(_current);
                if (d != null) return d;
            }
#if UNITY_EDITOR
            // Editor fallback: find asset
            var guids = UnityEditor.AssetDatabase.FindAssets($"t:UIThemeDefinition");
            foreach (var g in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UIThemeDefinition>(path);
                if (asset != null && asset.builtInTheme == _current) return asset;
            }
#endif
            return null;
        }

        public static UIThemeDefinition[] GetAllDefinitions()
        {
            EnsureLoaded();
            if (_database != null && _database.themes != null && _database.themes.Count > 0)
                return _database.themes.ToArray();

#if UNITY_EDITOR
            var list = new System.Collections.Generic.List<UIThemeDefinition>();
            var guids = UnityEditor.AssetDatabase.FindAssets($"t:UIThemeDefinition");
            foreach (var g in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UIThemeDefinition>(path);
                if (asset != null) list.Add(asset);
            }
            return list.ToArray();
#else
            return System.Array.Empty<UIThemeDefinition>();
#endif
        }

        public static string ExportThemeCode()
        {
            EnsureLoaded();
            return string.Join("|", (int)Current, CustomAccentEnabled ? 1 : 0,
                CustomAccent.r.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                CustomAccent.g.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                CustomAccent.b.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                PanelOpacity.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                CornerRadius.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                AccentGlow.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                AnimationSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        public static bool TryImportThemeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            var parts = code.Split('|');
            if (parts.Length < 7) return false;
            if (!int.TryParse(parts[0], out int theme) || !Enum.IsDefined(typeof(BuiltInUITheme), theme)) return false;
            bool enabled = parts[1] == "1" || parts[1].Equals("true", StringComparison.OrdinalIgnoreCase);
            bool okR = float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r);
            bool okG = float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float g);
            bool okB = float.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float b);
            bool okO = float.TryParse(parts[5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float opacity);
            bool okC = float.TryParse(parts[6], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float radius);
            if (!okR || !okG || !okB || !okO || !okC) return false;

            Current = (BuiltInUITheme)theme;
            CustomAccentEnabled = enabled;
            CustomAccent = new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
            PanelOpacity = opacity;
            CornerRadius = radius;

            if (parts.Length >= 8 && float.TryParse(parts[7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float glow))
                AccentGlow = glow;
            if (parts.Length >= 9 && float.TryParse(parts[8], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float speed))
                AnimationSpeed = speed;

            return true;
        }

        public static void ResetToDefault()
        {
            Current = BuiltInUITheme.IndustrialSteel;
            CustomAccentEnabled = false;
            CustomAccent = UITheme.AccentCyan;
            PanelOpacity = 0.97f;
            CornerRadius = UITheme.PanelRadius;
            AccentGlow = 0.28f;
            AnimationSpeed = 1f;
        }
    }
}
