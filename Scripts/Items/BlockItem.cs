// Assets/Scripts/VoxelEngine/Items/BlockItem.cs
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// A placeable building block. The 'placedPrefab' is what gets instantiated in the world.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Block", fileName = "Block_New")]
    public class BlockItem : ItemDefinition
    {
        [Header("Placement")]
        public GameObject placedPrefab;
        [Tooltip("Footprint in voxels (used by grid snapping). 1 = single voxel cube.")]
        public Vector3Int gridSize = Vector3Int.one;
        [Tooltip("Allow placing on top of itself / other blocks (foundations true; trees false).")]
        public bool   allowStacking = true;
        [Tooltip("Hit-points before this placed block breaks when mined.")]
        public int    blockHealth   = 100;
        [Tooltip("Required pickaxe tier to break this block once placed.")]
        [Range(0,4)] public int miningTier = 0;

        [Header("Visuals (optional overrides)")]
        [Tooltip("If set, this material is applied to every renderer on the placed prefab " +
                 "at instantiate time. Drop in a Material with your own texture to make the " +
                 "block look like real wood / stone / steel etc.")]
        public Material placedMaterial;

        [Tooltip("Optional override texture. If both placedMaterial AND texture are set, " +
                 "the texture is assigned to the material's main texture slot.")]
        public Texture2D texture;
    }
}
