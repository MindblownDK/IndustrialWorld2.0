// Assets/Scripts/VoxelEngine/Items/ResourceItem.cs
using UnityEngine;

namespace VoxelEngine.Items
{
    public enum ResourceCategory { Raw, Ingot, Component, Fuel, Misc }

    /// <summary>
    /// Raw materials, ingots, intermediates. Always stackable.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Resource", fileName = "Res_New")]
    public class ResourceItem : ItemDefinition
    {
        public ResourceCategory subcategory = ResourceCategory.Raw;
        [Tooltip("If used as fuel, how many seconds it burns in a furnace.")]
        public float fuelSeconds = 0f;
    }
}
