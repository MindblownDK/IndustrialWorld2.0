// Assets/Scripts/VoxelEngine/Combat/ArmorUpgradeKind.cs
//
// The five armour-upgrade families that the Armor Station applies to a worn
// Crusader armour piece. Each is a discrete stat branch with five tiers (1-5).
// Upgrades are stored per-instance on the armour ItemStack (bit-packed in
// ItemStack.durability) so they survive save/load and equip/unequip exactly
// like jetpack fuel does.

namespace VoxelEngine.Combat
{
    public enum ArmorUpgradeKind
    {
        HeatTolerance       = 0,
        RadiationShielding  = 1,
        OxygenEfficiency    = 2,
        FallImpact          = 3,
        Mobility            = 4,   // jetpack speed + fuel efficiency
    }

    /// <summary>Static metadata for the armour-upgrade branches.</summary>
    public static class ArmorUpgradeKindInfo
    {
        public const int MaxTier = 5;

        public static string DisplayName(ArmorUpgradeKind k)
            => k switch
            {
                ArmorUpgradeKind.HeatTolerance      => "Heat Tolerance",
                ArmorUpgradeKind.RadiationShielding => "Radiation Shielding",
                ArmorUpgradeKind.OxygenEfficiency   => "Oxygen Efficiency",
                ArmorUpgradeKind.FallImpact         => "Impact Padding",
                ArmorUpgradeKind.Mobility           => "Mobility Servos",
                _                                   => "Upgrade",
            };

        public static string Description(ArmorUpgradeKind k)
            => k switch
            {
                ArmorUpgradeKind.HeatTolerance      => "Reduces burn and environmental heat damage by 5% per tier.",
                ArmorUpgradeKind.RadiationShielding => "Reduces radiation damage by 8% per tier. Tier 5 approaches full immunity.",
                ArmorUpgradeKind.OxygenEfficiency   => "Reduces oxygen drain by 10% per tier, underwater or in vacuum.",
                ArmorUpgradeKind.FallImpact         => "Reduces fall damage by 12% per tier.",
                ArmorUpgradeKind.Mobility           => "Increases jetpack speed by 6% and reduces fuel drain by 6% per tier.",
                _                                   => "",
            };

        /// <summary>Damage-reduction fraction applied per tier (0..1).</summary>
        public static float DamageReductionPerTier(ArmorUpgradeKind k)
            => k switch
            {
                ArmorUpgradeKind.HeatTolerance      => 0.05f,
                ArmorUpgradeKind.RadiationShielding => 0.08f,
                ArmorUpgradeKind.OxygenEfficiency   => 0.10f,
                ArmorUpgradeKind.FallImpact         => 0.12f,
                ArmorUpgradeKind.Mobility           => 0.06f,
                _                                   => 0f,
            };
    }
}
