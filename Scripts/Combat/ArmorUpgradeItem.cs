// Assets/Scripts/VoxelEngine/Combat/ArmorUpgradeItem.cs
//
// A crafted module installed by the Armor Upgrade Station. Modules are single-use,
// but the installed result is retained on the specific armor ItemStack.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    [CreateAssetMenu(menuName = "Voxel Engine/Combat/Armor Upgrade Module", fileName = "ArmorUpgrade_New")]
    public sealed class ArmorUpgradeItem : ItemDefinition
    {
        [Header("Upgrade Module")]
        public ArmorUpgradeKind kind = ArmorUpgradeKind.HeatTolerance;

        [Range(1, ArmorUpgradeKindInfo.MaxTier)]
        public int tier = 1;

        [Tooltip("Special one-time seal that grants full radiation immunity to the upgraded armor piece.")]
        public bool isHazmat;

        public ArmorUpgradeItem()
        {
            maxStack = 1;
            massPerUnit = 1.5f;
        }

        public override bool IsStackable => false;

        /// <summary>
        /// Installation time scales from the station's 30-second base. The Hazmat
        /// seal is treated as the highest-grade installation.
        /// </summary>
        public int InstallationTier => isHazmat
            ? ArmorUpgradeKindInfo.MaxTier
            : Mathf.Clamp(tier, 1, ArmorUpgradeKindInfo.MaxTier);

        public string DefaultDisplayName => isHazmat
            ? "Hazmat Module"
            : $"{ArmorUpgradeKindInfo.DisplayName(kind)} Module (T{InstallationTier})";

        public static bool IsModule(ItemDefinition item)
        {
            return item is ArmorUpgradeItem;
        }
    }
}
