// Assets/Scripts/VoxelEngine/Items/QuarryUpgradeItem.cs
//
// Upgrade modules for the Quarry machine. Each upgrade type stacks
// independently (up to its max level). Drop onto a Quarry to install.

using UnityEngine;

namespace VoxelEngine.Items
{
    public enum QuarryUpgradeKind { Range, Speed, Efficiency }

    [CreateAssetMenu(menuName = "Voxel Engine/Items/Quarry Upgrade", fileName = "Upgrade_Q_New")]
    public class QuarryUpgradeItem : ItemDefinition
    {
        [Tooltip("Which quarry stat this module improves.")]
        public QuarryUpgradeKind upgradeKind = QuarryUpgradeKind.Range;

        [Tooltip("Maximum installed count of this upgrade type per quarry.")]
        [Range(1, 20)] public int maxInstalled = 10;

        [Tooltip("Effect per installed module. Range=+1 size, Speed=+0.08 interval reduction, Efficiency=+1 extra voxel/tick.")]
        [Range(1, 10)] public int level = 1;

        [Tooltip("UI tint for the upgrade icon badge.")]
        public Color badgeTint = Color.white;
    }
}
