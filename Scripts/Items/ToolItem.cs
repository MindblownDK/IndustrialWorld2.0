// Assets/Scripts/VoxelEngine/Items/ToolItem.cs
using UnityEngine;

namespace VoxelEngine.Items
{
    public enum ToolType { Pickaxe, Axe, Shovel, Sword, Other }

    /// <summary>
    /// A pickaxe / axe / shovel / etc. Carries a mining tier and durability.
    /// Tool items don't stack — each instance has its own remaining durability.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Tool", fileName = "Tool_New")]
    public class ToolItem : ItemDefinition
    {
        [Header("Tool")]
        public ToolType toolType = ToolType.Pickaxe;
        [Tooltip("0=hands, 1=wood, 2=stone, 3=iron, 4=steel/diamond. Materials with miningTier > this can't be mined.")]
        [Range(0,4)] public int miningTier = 1;
        [Tooltip("Damage applied per swing — affects mining speed (faster = more density removed).")]
        public float strength = 60f;
        [Tooltip("Brush radius in voxels — larger pickaxes carve a bigger area.")]
        public float brushRadius = 1.4f;
        [Tooltip("Hits per second when held.")]
        public float fireRate = 5f;
        [Tooltip("Total durability — each swing costs 1.")]
        public int   maxDurability = 150;

        public override bool IsStackable => false;
    }
}
