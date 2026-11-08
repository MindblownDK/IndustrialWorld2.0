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

        [Tooltip("SOFT CLEARANCE (17.44.0-dev). Off = hard clearance: nothing may overlap " +
                 "this block and this block may not overlap another structure. On = " +
                 "decorative clearance: it may brush past other soft blocks so railings, " +
                 "trim, signage and lamps can share an edge with the thing they decorate. " +
                 "Machines, foundations and anything with a function stay off.")]
        public bool   softClearance = false;
        /// <summary>18.14.0-dev: this item's own default placement pattern while held
        /// (-1 = none, the player-wide choice applies). A decorative strip sets SPAN so
        /// holding it and dragging lays a run by itself, like a grid drag, without
        /// opening the pattern wheel. An explicit wheel pick clears it for the session.</summary>
        [Tooltip("Default placement pattern while this item is held (-1 = player-wide choice). SPAN = a drag lays a run by itself.")]
        public int    defaultPlacementPattern = -1;
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
