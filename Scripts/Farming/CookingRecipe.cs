// Assets/Scripts/VoxelEngine/Farming/CookingRecipe.cs
//
// A cooking recipe: combine raw ingredients (from farming) at a furnace or
// cooking station to produce cooked food with better nutrition.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Farming
{
    /// <summary>
    /// Cooking recipe — transforms raw farm ingredients into cooked food.
    /// Works as a regular RecipeDefinition but this helper class makes it
    /// easy to create them via the asset menu.
    ///
    /// Use: Right-click > Create > Voxel Engine > Farming > Cooking Recipe
    /// Then add the output FoodItem to recipeRegistry.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Farming/Cooking Recipe", fileName = "Cook_New")]
    public class CookingRecipe : Crafting.RecipeDefinition
    {
        // Inherits everything from RecipeDefinition.
        // Convention: set requiredStation = Furnace for cooking.
        // The output should be a FoodItem.

        public CookingRecipe()
        {
            requiredStation = Crafting.StationTier.Furnace;
            craftSeconds = 0f; // instant at furnace — use smelting recipes for timed cooking
        }
    }
}
