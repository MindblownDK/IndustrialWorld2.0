// Assets/Scripts/VoxelEngine/Items/ItemDefinition.cs
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// Base item asset. Use one of the subclasses (ResourceItem, ToolItem, BlockItem)
    /// for specialised behaviour, or this class directly for plain stackables.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Item Definition", fileName = "Item_New")]
    public class ItemDefinition : ScriptableObject
    {
        public string itemId = "iron_ore";
        public string displayName = "Iron Ore";
        [TextArea] public string description;
        public Sprite icon;
        [Tooltip("Color used for icon fallback when no sprite is assigned.")]
        public Color iconTint = Color.white;
        public int   maxStack    = 900;
        public float massPerUnit = 1f;
        [Tooltip("Free-form category for filtering in the crafting list. Examples: Resources, Tools, Building, Power, Stations.")]
        public string category    = "Misc";

        [Header("Visuals")]
        [Tooltip("Optional 3D prefab to show when this item is held in hand (viewmodel).")]
        public GameObject viewmodelPrefab;

        /// <summary>Tools and blocks override this to be unique-per-instance (no stacking).</summary>
        public virtual bool IsStackable => maxStack > 1;
    }
}
