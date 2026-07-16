// Assets/Scripts/VoxelEngine/UI/UIThemeDefinition.cs

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
        public string displayName = "Industrial Steel";
        public BuiltInUITheme builtInTheme = BuiltInUITheme.IndustrialSteel;
        public Color accent = new(0.18f, 0.72f, 0.88f);
        public Color panel = new(0.08f, 0.088f, 0.12f, 0.97f);
        public Color text = new(0.92f, 0.94f, 0.97f);
        [Range(0f, 1f)] public float panelOpacity = 0.97f;
        [Range(0f, 24f)] public float cornerRadius = 12f;
    }

    public static class UIThemeManager
    {
        private const string ThemeKey = "IndustrialWorld.UITheme";
        private const string CustomAccentEnabledKey = "IndustrialWorld.UITheme.CustomAccentEnabled";
        private const string CustomAccentRKey = "IndustrialWorld.UITheme.CustomAccentR";
        private const string CustomAccentGKey = "IndustrialWorld.UITheme.CustomAccentG";
        private const string CustomAccentBKey = "IndustrialWorld.UITheme.CustomAccentB";
        private static bool _loaded;
        private static BuiltInUITheme _current = BuiltInUITheme.IndustrialSteel;
        private static bool _customAccentEnabled;
        private static Color _customAccent = UITheme.AccentCyan;

        public static BuiltInUITheme Current
        {
            get { EnsureLoaded(); return _current; }
            set
            {
                _current = value;
                PlayerPrefs.SetInt(ThemeKey, (int)_current);
                PlayerPrefs.Save();
            }
        }

        public static string CurrentLabel => Label(Current);
        public static bool CustomAccentEnabled
        {
            get { EnsureLoaded(); return _customAccentEnabled; }
            set { EnsureLoaded(); _customAccentEnabled = value; SaveCustomAccent(); }
        }
        public static Color CustomAccent
        {
            get { EnsureLoaded(); return _customAccent; }
            set { EnsureLoaded(); _customAccent = value; SaveCustomAccent(); }
        }
        public static Color Accent => CustomAccentEnabled ? CustomAccent : AccentFor(Current);
        public static Color PanelColor => PanelFor(Current);
        public static Color TextColor => TextFor(Current);

        public static void Next()
        {
            int count = System.Enum.GetValues(typeof(BuiltInUITheme)).Length;
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

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            int saved = PlayerPrefs.GetInt(ThemeKey, (int)BuiltInUITheme.IndustrialSteel);
            _current = System.Enum.IsDefined(typeof(BuiltInUITheme), saved)
                ? (BuiltInUITheme)saved
                : BuiltInUITheme.IndustrialSteel;
            _customAccentEnabled = PlayerPrefs.GetInt(CustomAccentEnabledKey, 0) != 0;
            _customAccent = new Color(
                PlayerPrefs.GetFloat(CustomAccentRKey, UITheme.AccentCyan.r),
                PlayerPrefs.GetFloat(CustomAccentGKey, UITheme.AccentCyan.g),
                PlayerPrefs.GetFloat(CustomAccentBKey, UITheme.AccentCyan.b),
                1f);
        }

        private static void SaveCustomAccent()
        {
            PlayerPrefs.SetInt(CustomAccentEnabledKey, _customAccentEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(CustomAccentRKey, _customAccent.r);
            PlayerPrefs.SetFloat(CustomAccentGKey, _customAccent.g);
            PlayerPrefs.SetFloat(CustomAccentBKey, _customAccent.b);
            PlayerPrefs.Save();
        }
    }
}
