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
        public static ProductionPanelTheme Current { get; private set; } = ProductionPanelTheme.IndustrialSteel;

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
