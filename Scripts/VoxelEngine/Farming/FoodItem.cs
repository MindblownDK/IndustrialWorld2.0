// Assets/Scripts/VoxelEngine/Farming/FoodItem.cs
//
// An edible item. When consumed (RMB while holding), restores hunger.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Farming
{
    /// <summary>
    /// Consumable food item. RMB while held to eat and restore hunger.
    /// Create via: Right-click > Create > Voxel Engine > Farming > Food Item.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Farming/Food Item", fileName = "Food_New")]
    public class FoodItem : ItemDefinition
    {
        [Header("Nutrition")]
        [Tooltip("Hunger points restored when eaten.")]
        public float hungerRestore = 20f;

        [Tooltip("Health points restored when eaten (0 = none).")]
        public float healthRestore = 0f;

        [Tooltip("Stamina restored when eaten (0 = none).")]
        public float staminaRestore = 0f;
    }
}
