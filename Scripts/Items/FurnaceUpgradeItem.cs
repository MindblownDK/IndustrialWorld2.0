// Assets/Scripts/VoxelEngine/Items/FurnaceUpgradeItem.cs
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// Upgrade module that can be inserted into an ElectricFurnace's upgrade slots.
    /// Multipliers stack multiplicatively per stack count (1.5 with 2 stacks = 2.25x).
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Furnace Upgrade", fileName = "Upgrade_New")]
    public class FurnaceUpgradeItem : ItemDefinition
    {
        [Tooltip("Multiplier applied per upgrade module to smelt SPEED. Use values like 1.25 (25% faster).")]
        public float speedMultiplier = 1.25f;
        [Tooltip("Multiplier applied per module to power DRAW. Use values like 0.8 (uses 20% less power).")]
        public float efficiencyMultiplier = 1.0f;
    }
}
