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
            var stations = Object.FindObjectsByType<CraftingStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var st in stations)
            {
                if ((st.transform.position - origin).sqrMagnitude > radius * radius) continue;
                if ((int)st.tier > (int)best) best = st.tier;
            }
            return best;
        }

        public static List<RecipeDefinition> AvailableRecipes(RecipeRegistry registry, StationTier maxStation)
        {
            var list = new List<RecipeDefinition>();
            if (registry == null) return list;
            var rm = VoxelEngine.Research.ResearchManager.Instance;
            foreach (var r in registry.recipes)
            {
                if (r == null) continue;
                // Recipe is craftable if it's unlocked-by-default OR research has unlocked it.
                if (rm != null)
                {
                    if (!rm.IsRecipeUnlocked(r)) continue;
                }
                else if (!r.unlockedByDefault) continue;
                if ((int)r.requiredStation <= (int)maxStation) list.Add(r);
            }
            return list;
        }
    }
}
