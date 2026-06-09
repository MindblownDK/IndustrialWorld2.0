// Assets/Scripts/VoxelEngine/Research/ResearchRecipeLinker.cs
//
// Links research nodes to recipe unlocks.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;

namespace VoxelEngine.Research
{
    public static class ResearchRecipeLinker
    {
        private static Dictionary<string, RecipeDefinition[]> _researchToRecipes 
            = new Dictionary<string, RecipeDefinition[]>();

        public static void Register(string researchId, params RecipeDefinition[] recipes)
        {
            _researchToRecipes[researchId] = recipes;
        }

        public static RecipeDefinition[] GetUnlockedRecipes(string researchId)
        {
            return _researchToRecipes.TryGetValue(researchId, out var recipes) 
                ? recipes 
                : new RecipeDefinition[0];
        }
    }
}