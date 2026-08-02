// Assets/Scripts/VoxelEngine/Combat/ArmorUpgradeItem.cs
//
// An armour-upgrade module crafted at the Armor Station. Holding one while
// interacting with an Armor Station (while wearing armour) applies it to the
// worn piece, permanently raising that branch's tier. The module is consumed.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    [CreateAssetMenu(menuName = "Voxel Engine/Combat/Armor Upgrade Module", fileName = "ArmorUpgrade_New")]
    public class ArmorUpgradeItem : ItemDefinition
    {
        [Header("Upgrade Module")]
        public ArmorUpgradeKind kind = ArmorUpgradeKind.HeatTolerance;
        [Range(1, 5)] public int tier = 1;
        [Tooltip("True for the special Hazmat Module — grants full radiation immunity on any armour piece.")]
        public bool isHazmat = false;

        public ArmorUpgradeItem() { maxStack = 1; massPerUnit = 1.5f; }

        public override bool IsStackable => false;

        /// <summary>Default display name if none was authored.</summary>
        public string DefaultDisplayName
            => isHazmat ? "Hazmat Module"
                        : $"{ArmorUpgradeKindInfo.DisplayName(kind)} Module (T{tier})";

        public static bool IsModule(ItemDefinition item) => item is ArmorUpgradeItem;
    }
}
