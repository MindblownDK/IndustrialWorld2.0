// Assets/Scripts/VoxelEngine/Farming/SeedItem.cs
//
// A plantable seed item. When used on a FarmPlot (RMB), plants the associated crop.
// Also edible (restores a small amount of hunger).

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Farming
{
    /// <summary>
    /// Seed item. Plant on tilled soil (FarmPlot) with RMB.
    /// Create via: Right-click > Create > Voxel Engine > Farming > Seed Item.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Farming/Seed Item", fileName = "Seed_New")]
    public class SeedItem : ItemDefinition
    {
        [Header("Farming")]
        [Tooltip("The crop this seed grows into.")]
        public CropDefinition crop;
    }
}
