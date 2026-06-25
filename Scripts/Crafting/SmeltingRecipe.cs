// Assets/Scripts/VoxelEngine/Crafting/SmeltingRecipe.cs
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Crafting
{
    [CreateAssetMenu(menuName = "Voxel Engine/Crafting/Smelting Recipe", fileName = "Smelt_New")]
    public class SmeltingRecipe : ScriptableObject
    {
        public ItemDefinition input;
        public int            inputCount  = 1;
        public ItemDefinition output;
        public int            outputCount = 1;
        [Tooltip("Seconds to smelt one batch.")]
        public float          smeltSeconds = 5f;
    }
}
