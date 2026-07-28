// Assets/Scripts/VoxelEngine/GridSystem/GridBlockItem.cs
//
// Item definition for placeable grid blocks. Held in inventory, placed by GridBuilder.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    [CreateAssetMenu(menuName = "Voxel Engine/Grid/Block Item", fileName = "GBlock_New")]
    public class GridBlockItem : ItemDefinition
    {
        [Header("Grid Block")]
        public GridSize gridSize = GridSize.Large;

        [Tooltip("Prefab to instantiate. Must have a GridBlock (or subclass) component.")]
        public GameObject blockPrefab;

        [Tooltip("Mass of this block in kg.")]
        public float blockMass = 100f;

        [Tooltip("Hit points.")]
        public float blockHP = 200f;

        [Header("Shape Variants")]
        [Tooltip("Allows this structural block to use the Grid Shape wheel. Step 18 enables this non-destructively for supported armor items.")]
        public bool supportsShapeVariants;

        public bool SupportsShapeVariants => supportsShapeVariants
            || (blockPrefab != null && blockPrefab.GetComponent<GridArmorBlock>() != null);
    }
}
