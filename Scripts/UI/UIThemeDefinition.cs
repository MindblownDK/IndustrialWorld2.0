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
        private static bool _loaded;
        private static BuiltInUITheme _current = BuiltInUITheme.IndustrialSteel;

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
        public static Color Accent => AccentFor(Current);

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
        }
    }
}
