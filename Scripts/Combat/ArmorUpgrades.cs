// Assets/Scripts/VoxelEngine/Combat/ArmorUpgrades.cs
//
// Bit-packed storage of armour upgrades on an armour ItemStack.
//
// Layout (single int, stored in ItemStack.durability — a field the save system
// already serializes, so upgrades survive save/load and equip/unequip with zero
// schema changes):
//
//     bits  0- 2 : Heat Tolerance tier      (0-5)
//     bits  3- 5 : Radiation Shielding tier (0-5)
//     bits  6- 8 : Oxygen Efficiency tier   (0-5)
//     bits  9-11 : Fall Impact tier         (0-5)
//     bits 12-14 : Mobility tier            (0-5)
//     bit  15    : Hazmat flag
//
// All reads are defensive (armour with durability == 0 means "no upgrades").

using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    public static class ArmorUpgrades
    {
        public const int HazmatBit = 15;
        public const int MaxTier   = 5;

        // Per-kind bit shift (each occupies 3 bits).
        private static int Shift(ArmorUpgradeKind kind)
        {
            switch (kind)
            {
                case ArmorUpgradeKind.HeatTolerance:      return 0;
                case ArmorUpgradeKind.RadiationShielding: return 3;
                case ArmorUpgradeKind.OxygenEfficiency:   return 6;
                case ArmorUpgradeKind.FallImpact:         return 9;
                case ArmorUpgradeKind.Mobility:           return 12;
                default:                                  return 0;
            }
        }

        private static bool IsArmorStack(ItemStack s)
            => s != null && !s.IsEmpty && s.item is ArmorItem;

        /// <summary>Packed upgrade value for an armour stack (0 when none/none worn).</summary>
        public static int GetPacked(ItemStack armor)
        {
            if (!IsArmorStack(armor)) return 0;
            return armor.durability;
        }

        /// <summary>Current tier of one branch on an armour stack (0..5).</summary>
        public static int GetTier(ItemStack armor, ArmorUpgradeKind kind)
        {
            if (!IsArmorStack(armor)) return 0;
            int packed = armor.durability;
            int raw = (packed >> Shift(kind)) & 0x7;
            return raw < 0 ? 0 : (raw > MaxTier ? MaxTier : raw);
        }

        /// <summary>Current tier of one branch on the armour definition-level packed value.</summary>
        public static int GetTier(int packed, ArmorUpgradeKind kind)
        {
            int raw = (packed >> Shift(kind)) & 0x7;
            return raw < 0 ? 0 : (raw > MaxTier ? MaxTier : raw);
        }

        /// <summary>True when this armour carries the Hazmat seal (radiation immunity).</summary>
        public static bool HasHazmat(ItemStack armor)
        {
            if (!IsArmorStack(armor)) return false;
            return (armor.durability & (1 << HazmatBit)) != 0;
        }

        /// <summary>
        /// Try to apply a module to an armour stack. Returns true and mutates the stack
        /// when the module can be applied (armour present, and it actually raises the
        /// branch / is a hazmat seal). Never reduces an existing upgrade.
        /// </summary>
        public static bool TryApply(ItemStack armor, ArmorUpgradeItem module)
        {
            if (module == null || !IsArmorStack(armor)) return false;

            if (module.isHazmat)
            {
                if (HasHazmat(armor)) return false;   // already sealed
                armor.durability |= (1 << HazmatBit);
                return true;
            }

            int shift = Shift(module.kind);
            int current = (armor.durability >> shift) & 0x7;
            int target = System.Math.Min(MaxTier, System.Math.Max(current, module.tier));
            if (target <= current) return false;       // no improvement — don't consume the module
            int mask = 0x7 << shift;
            armor.durability = (armor.durability & ~mask) | (target << shift);
            return true;
        }
    }
}
