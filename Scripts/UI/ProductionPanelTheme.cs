// Assets/Scripts/VoxelEngine/UI/ProductionPanelTheme.cs

using UnityEngine;

namespace VoxelEngine.UI
{
    public enum ProductionPanelTheme
    {
        IndustrialSteel,
        AmberFactory,
        CyanLogistics,
        VioletResearch
    }

    public static class ProductionPanelThemeState
    {
        private const string PrefKey = "IndustrialWorld.ProductionPanelTheme";
        private static bool _loaded;
        private static ProductionPanelTheme _current = ProductionPanelTheme.IndustrialSteel;

        public static ProductionPanelTheme Current
        {
            get
            {
                EnsureLoaded();
                return _current;
            }
            private set
            {
                _current = value;
                PlayerPrefs.SetInt(PrefKey, (int)_current);
                PlayerPrefs.Save();
            }
        }

        public static Color Accent => Current switch
        {
            ProductionPanelTheme.AmberFactory => UITheme.AccentGold,
            ProductionPanelTheme.CyanLogistics => UITheme.AccentCyan,
            ProductionPanelTheme.VioletResearch => UITheme.AccentPurple,
            _ => UITheme.AccentTeal
        };

        public static string Label => Current switch
        {
            ProductionPanelTheme.AmberFactory => "Amber",
            ProductionPanelTheme.CyanLogistics => "Cyan",
            ProductionPanelTheme.VioletResearch => "Violet",
            _ => "Steel"
        };

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            int saved = PlayerPrefs.GetInt(PrefKey, (int)ProductionPanelTheme.IndustrialSteel);
            _current = System.Enum.IsDefined(typeof(ProductionPanelTheme), saved)
                ? (ProductionPanelTheme)saved
                : ProductionPanelTheme.IndustrialSteel;
        }

        public static void Next()
        {
            Current = Current switch
            {
                ProductionPanelTheme.IndustrialSteel => ProductionPanelTheme.AmberFactory,
                ProductionPanelTheme.AmberFactory => ProductionPanelTheme.CyanLogistics,
                ProductionPanelTheme.CyanLogistics => ProductionPanelTheme.VioletResearch,
                _ => ProductionPanelTheme.IndustrialSteel
            };
        }
    }
}
