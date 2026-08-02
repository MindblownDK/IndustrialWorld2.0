// Assets/Scripts/VoxelEngine/Combat/ArmorUpgradeKind.cs
//
// Shared identity and balance metadata for upgrades installed on a Crusader armor
// piece. Each branch is deliberately data-light: the upgrade item carries only its
// branch and tier, while the installed state lives on the individual ItemStack.

namespace VoxelEngine.Combat
{
    public enum ArmorUpgradeKind
    {
        HeatTolerance = 0,
        RadiationShielding = 1,
        OxygenEfficiency = 2,
        FallImpact = 3,
        Mobility = 4,
    }

    /// <summary>Central, player-facing metadata for every armor-upgrade branch.</summary>
    public static class ArmorUpgradeKindInfo
    {
        public const int MaxTier = 5;

        public static bool IsDefined(ArmorUpgradeKind kind)
        {
            return kind >= ArmorUpgradeKind.HeatTolerance && kind <= ArmorUpgradeKind.Mobility;
        }

        public static int ClampTier(int tier)
        {
            return tier < 0 ? 0 : tier > MaxTier ? MaxTier : tier;
        }

        public static string DisplayName(ArmorUpgradeKind kind)
        {
            return kind switch
            {
                ArmorUpgradeKind.HeatTolerance => "Heat Tolerance",
                ArmorUpgradeKind.RadiationShielding => "Radiation Shielding",
                ArmorUpgradeKind.OxygenEfficiency => "Oxygen Efficiency",
                ArmorUpgradeKind.FallImpact => "Impact Padding",
                ArmorUpgradeKind.Mobility => "Mobility Servos",
                _ => "Upgrade",
            };
        }

        public static string Description(ArmorUpgradeKind kind)
        {
            return kind switch
            {
                ArmorUpgradeKind.HeatTolerance => "Reduces burn and environmental heat damage by 5% per tier.",
                ArmorUpgradeKind.RadiationShielding => "Reduces radiation damage by 8% per tier.",
                ArmorUpgradeKind.OxygenEfficiency => "Reduces oxygen drain by 10% per tier.",
                ArmorUpgradeKind.FallImpact => "Reduces hard-landing damage by 12% per tier.",
                ArmorUpgradeKind.Mobility => "Increases jetpack speed by 6% and reduces fuel drain by 6% per tier.",
                _ => string.Empty,
            };
        }

        /// <summary>Effect magnitude per installed tier, expressed as a fraction.</summary>
        public static float EffectPerTier(ArmorUpgradeKind kind)
        {
            return kind switch
            {
                ArmorUpgradeKind.HeatTolerance => 0.05f,
                ArmorUpgradeKind.RadiationShielding => 0.08f,
                ArmorUpgradeKind.OxygenEfficiency => 0.10f,
                ArmorUpgradeKind.FallImpact => 0.12f,
                ArmorUpgradeKind.Mobility => 0.06f,
                _ => 0f,
            };
        }
    }
}
