// Assets/Scripts/VoxelEngine/Crafting/Crafter.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Crafting
{
    /// <summary>
    /// Static helpers for testing recipe requirements and performing crafts.
    /// </summary>
    public static class Crafter
    {
        public static bool HasIngredients(IItemContainer source, RecipeDefinition recipe)
        {
            if (recipe == null || recipe.inputs == null) return false;
            foreach (var ing in recipe.inputs)
            {
                if (ing.item == null || ing.count <= 0) continue;
                if (source.CountOf(ing.item) < ing.count) return false;
            }
            return true;
        }

        /// <summary>
        /// Removes ingredients from 'source' and inserts the output into 'destination'.
        /// Returns true if the craft succeeded (ingredients were available AND output fit).
        /// </summary>
        public static bool TryCraft(IItemContainer source, IItemContainer destination, RecipeDefinition recipe, CraftQueue queue = null)
        {
            if (!HasIngredients(source, recipe)) return false;
            if (destination is ItemContainer ic)
            {
                if (!ic.HasSpace(recipe.outputItem, recipe.outputCount)) return false;
            }

            // Consume ingredients up-front (refunded if canceled while in the queue).
            foreach (var ing in recipe.inputs)
                source.Remove(ing.item, ing.count);

            // If a queue is provided AND the recipe has a craft time, queue it instead of inserting immediately.
            if (queue != null && recipe.craftSeconds > 0f)
            {
                queue.Enqueue(recipe, destination);
                return true;
            }

            // Otherwise: instant craft.
            destination.Insert(new ItemStack(recipe.outputItem, recipe.outputCount));
            return true;
        }

        /// <summary>
        /// Returns the highest station tier currently accessible from 'origin' within 'radius'.
        /// Always includes StationTier.None (recipes craftable bare-handed).
        /// </summary>
        public static StationTier MaxAccessibleStation(Vector3 origin, float radius)
        {
            StationTier best = StationTier.None;
            var stations = Object.FindObjectsByType<CraftingStation>(FindObjectsInactive.Exclude);
            foreach (var st in stations)
            {
                if ((st.transform.position - origin).sqrMagnitude > radius * radius) continue;
                if ((int)st.tier > (int)best) best = st.tier;
            }
            return best;
        }

        public static List<RecipeDefinition> AvailableRecipes(RecipeRegistry registry, StationTier maxStation)
        {
            return CollectAvailableRecipes(registry, recipe => (int)recipe.requiredStation <= (int)maxStation);
        }

        /// <summary>
        /// Gets the recipe list for one placed station. Exclusive stations use an
        /// exact-tier filter so dedicated stations stay focused rather than exposing
        /// the entire lower-tier catalogue.
        /// </summary>
        public static List<RecipeDefinition> AvailableRecipesForStation(RecipeRegistry registry, CraftingStation station)
        {
            if (station == null) return AvailableRecipes(registry, StationTier.None);
            return station.exclusiveRecipes
                ? CollectAvailableRecipes(registry, recipe => recipe.requiredStation == station.tier)
                : AvailableRecipes(registry, station.tier);
        }

        private static List<RecipeDefinition> CollectAvailableRecipes(
            RecipeRegistry registry,
            System.Func<RecipeDefinition, bool> stationFilter)
        {
            var list = new List<RecipeDefinition>();
            if (registry == null || stationFilter == null) return list;

            var researchManager = VoxelEngine.Research.ResearchManager.Instance;
            foreach (var recipe in registry.recipes)
            {
                if (recipe == null || recipe.outputItem == null) continue;
                // Never surface hollow placeholders. They cannot be crafted safely
                // and should not leak raw asset names into player-facing UIs.
                if (recipe.inputs == null || recipe.inputs.Length == 0) continue;
                if (!stationFilter(recipe)) continue;

                if (researchManager != null)
                {
                    if (!researchManager.IsRecipeUnlocked(recipe)) continue;
                }
                else if (!recipe.unlockedByDefault)
                {
                    continue;
                }

                list.Add(recipe);
            }
            return list;
        }
    }
}
