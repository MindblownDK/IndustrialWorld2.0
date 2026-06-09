// Assets/Scripts/VoxelEngine/Items/WaterBucket.cs
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// LMB scoops a water voxel into the bucket (becomes "filled"); RMB places water into
    /// an empty voxel. Single-stack item; durability is reused as a 1=filled / 0=empty flag.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Water Bucket", fileName = "Bucket_New")]
    public class WaterBucket : ToolItem
    {
        public WaterBucket() { toolType = ToolType.Other; maxDurability = 1; maxStack = 1; }
    }
}
