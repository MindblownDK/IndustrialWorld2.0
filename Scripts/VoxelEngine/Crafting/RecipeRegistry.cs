// Assets/Scripts/VoxelEngine/Crafting/RecipeRegistry.cs
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Crafting
{
    [CreateAssetMenu(menuName = "Voxel Engine/Crafting/Recipe Registry", fileName = "RecipeRegistry")]
    public class RecipeRegistry : ScriptableObject
    {
        public List<RecipeDefinition> recipes = new();

        public IEnumerable<RecipeDefinition> RecipesForStation(StationTier station)
        {
            foreach (var r in recipes)
                if (r != null && r.requiredStation == station)
                    yield return r;
        }
    }
}
