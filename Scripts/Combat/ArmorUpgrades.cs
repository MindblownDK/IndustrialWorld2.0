// Assets/Scripts/VoxelEngine/Combat/ArmorUpgrades.cs
//
// Save-safe per-instance armor upgrade state. ArmorItem definitions remain shared
// assets, while each equipped/crafted ItemStack stores its own installed modules in
// the already-persisted durability integer.
//
// Layout:
//   bits  0- 2 : Heat Tolerance tier
//   bits  3- 5 : Radiation Shielding tier
//   bits  6- 8 : Oxygen Efficiency tier
//   bits  9-11 : Impact Padding tier
//   bits 12-14 : Mobility Servos tier
//   bit      15: Hazmat seal

using System;
using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    public static class ArmorUpgrades
    {
        private const int BitsPerBranch = 3;
        private const int BranchMask = 0x7;
        private const int HazmatBit = 15;

        private static bool IsArmorStack(ItemStack stack)
        {
            return stack != null && !stack.IsEmpty && stack.item is ArmorItem;
        }

        private static int Shift(ArmorUpgradeKind kind)
        {
            return kind switch
            {
                ArmorUpgradeKind.HeatTolerance => 0,
                ArmorUpgradeKind.RadiationShielding => BitsPerBranch,
                ArmorUpgradeKind.OxygenEfficiency => BitsPerBranch * 2,
                ArmorUpgradeKind.FallImpact => BitsPerBranch * 3,
                ArmorUpgradeKind.Mobility => BitsPerBranch * 4,
                _ => -1,
            };
        }

        /// <summary>Returns the packed state or zero for an unupgraded/invalid armor stack.</summary>
        public static int GetPacked(ItemStack armor)
        {
            return IsArmorStack(armor) ? Math.Max(0, armor.durability) : 0;
        }

        public static int GetTier(ItemStack armor, ArmorUpgradeKind kind)
        {
            return GetTier(GetPacked(armor), kind);
        }

        public static int GetTier(int packed, ArmorUpgradeKind kind)
        {
            int shift = Shift(kind);
            if (shift < 0 || packed < 0) return 0;
            return ArmorUpgradeKindInfo.ClampTier((packed >> shift) & BranchMask);
        }

        public static bool HasHazmat(ItemStack armor)
        {
            return IsArmorStack(armor) && (GetPacked(armor) & (1 << HazmatBit)) != 0;
        }

        /// <summary>
        /// Tests whether a module would improve the selected armor piece. This never
        /// changes the item and is safe for UI previews and process validation.
        /// </summary>
        public static bool CanApply(ItemStack armor, ArmorUpgradeItem module, out string reason)
        {
            if (!IsArmorStack(armor))
            {
                reason = "Insert an armor piece.";
                return false;
            }

            if (module == null)
            {
                reason = "Insert an upgrade module.";
                return false;
            }

            if (module.isHazmat)
            {
                if (HasHazmat(armor))
                {
                    reason = "This armor already has a Hazmat seal.";
                    return false;
                }

                reason = string.Empty;
                return true;
            }

            if (!ArmorUpgradeKindInfo.IsDefined(module.kind))
            {
                reason = "This module has an invalid upgrade branch.";
                return false;
            }

            int targetTier = ArmorUpgradeKindInfo.ClampTier(module.tier);
            if (targetTier <= 0)
            {
                reason = "This module has no valid tier.";
                return false;
            }

            int currentTier = GetTier(armor, module.kind);
            if (targetTier <= currentTier)
            {
                reason = currentTier >= ArmorUpgradeKindInfo.MaxTier
                    ? $"{ArmorUpgradeKindInfo.DisplayName(module.kind)} is already at maximum tier."
                    : $"This armor already has {ArmorUpgradeKindInfo.DisplayName(module.kind)} T{currentTier} or better.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>Applies one module to one armor ItemStack without consuming the module.</summary>
        public static bool TryApply(ItemStack armor, ArmorUpgradeItem module, out string reason)
        {
            if (!CanApply(armor, module, out reason)) return false;

            int packed = GetPacked(armor);
            if (module.isHazmat)
            {
                armor.durability = packed | (1 << HazmatBit);
                return true;
            }

            int shift = Shift(module.kind);
            int targetTier = ArmorUpgradeKindInfo.ClampTier(module.tier);
            int mask = BranchMask << shift;
            armor.durability = (packed & ~mask) | (targetTier << shift);
            return true;
        }

        public static bool TryApply(ItemStack armor, ArmorUpgradeItem module)
        {
            return TryApply(armor, module, out _);
        }
    }
}
