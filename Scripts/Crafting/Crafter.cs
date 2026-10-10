// Assets/Scripts/VoxelEngine/Crafting/Crafter.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Storage;

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
            bool matchId = OutputsSciencePack(recipe);
            foreach (var ing in recipe.inputs)
            {
                if (ing.item == null || ing.count <= 0) continue;
                if (CountCraftIngredient(source, ing.item, matchId) < ing.count) return false;
            }
            return true;
        }

        /// <summary>17.4.1 — building families are chosen on the hammer wheel.
        /// A recipe that outputs a build token is not craftable.</summary>
        public static bool OutputsBuildToken(RecipeDefinition recipe)
        {
            return recipe != null && recipe.outputItem is VoxelEngine.Building.Tiered.BuildToken;
        }

        /// <summary>
        /// Removes ingredients from 'source' and inserts the output into 'destination'.
        /// Returns true if the craft succeeded (ingredients were available AND output fit).
        /// </summary>
        public static bool TryCraft(IItemContainer source, IItemContainer destination, RecipeDefinition recipe, CraftQueue queue = null)
        {
            if (OutputsBuildToken(recipe)) return false;
            if (!HasIngredients(source, recipe)) return false;
            if (destination is ItemContainer ic)
            {
                if (!ic.HasSpace(recipe.outputItem, recipe.outputCount)) return false;
            }

            // Consume ingredients up-front (refunded if canceled while in the queue).
            bool matchId = OutputsSciencePack(recipe);
            foreach (var ing in recipe.inputs)
                RemoveCraftIngredient(source, ing.item, ing.count, matchId);

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
        /// Mirrors <see cref="TryCraft"/>'s refusal gates and names the reason.
        /// Returns null when the craft would succeed, otherwise a short
        /// player-facing explanation ("Missing ingredients", "Inventory full",
        /// "Overweight 449/450 kg", ...).
        /// </summary>
        public static string CraftFailReason(IItemContainer source, IItemContainer destination, RecipeDefinition recipe)
        {
            if (!HasIngredients(source, recipe)) return "Missing ingredients";
            if (destination is ItemContainer ic)
            {
                if (ic.HasSpace(recipe.outputItem, recipe.outputCount)) return null;
                // The output does not fit: explain WHICH gate refused it.
                var item = recipe.outputItem;
                if (item != null)
                {
                    if (item.requiresContainment && !ic.allowContainment) return "Needs a containment vault";
                    if (item.cannotBeCarried && !ic.allowPlayerCarry) return "Cannot carry by hand";
                    float need = Mathf.Max(0.0001f, item.massPerUnit) * recipe.outputCount;
                    if (ic.MaxWeightKg > 0f && ic.RemainingWeightKg < need)
                        return $"Overweight {MassFormat.Format(ic.CurrentWeightKg)} / {MassFormat.Format(ic.MaxWeightKg)}";
                }
                return "Inventory full";
            }
            return null;
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
            return CollectAvailableRecipes(registry, recipe => (int)recipe.requiredStation <= (int)maxStation, scienceCap: maxStation);
        }

        /// <summary>
        /// Gets the recipe list for one placed station. Exclusive stations use an
        /// exact-tier filter so dedicated stations stay focused rather than exposing
        /// the entire lower-tier catalogue.
        /// </summary>
        public static List<RecipeDefinition> AvailableRecipesForStation(RecipeRegistry registry, CraftingStation station)
        {
            if (station == null) return AvailableRecipes(registry, StationTier.None);

            // Owning an Armor Station is the progression gate. Once it is placed,
            // its focused armory catalogue must remain usable even when a scene's
            // research-tree reference has not yet refreshed its unlock cache.
            // This also recognizes legacy armor recipes that were authored before
            // the dedicated ArmorStation tier existed.
            if (station is VoxelEngine.Combat.ArmorStation)
                return CollectAvailableRecipes(registry, IsArmorStationRecipe, ignoreResearchLock: true);

            return station.exclusiveRecipes
                ? CollectAvailableRecipes(registry, recipe => recipe.requiredStation == station.tier)
                : AvailableRecipes(registry, station.tier);
        }

        private static bool IsArmorStationRecipe(RecipeDefinition recipe)
        {
            if (recipe == null || recipe.outputItem == null) return false;
            if (recipe.requiredStation == StationTier.ArmorStation) return true;
            if (recipe.outputItem is VoxelEngine.Combat.ArmorItem) return true;
            if (recipe.outputItem is VoxelEngine.Combat.ArmorUpgradeItem) return true;
            return string.Equals(recipe.outputItem.itemId, "block_armorupgradestation", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Science packs are research currency, never a research reward.</summary>
        public static bool OutputsSciencePack(RecipeDefinition recipe)
        {
            return recipe != null && recipe.outputItem is ScienceItem;
        }

        private static List<RecipeDefinition> CollectAvailableRecipes(
            RecipeRegistry registry,
            System.Func<RecipeDefinition, bool> stationFilter,
            bool ignoreResearchLock = false,
            StationTier? scienceCap = null)
        {
            var list = new List<RecipeDefinition>();
            if (registry == null || stationFilter == null) return list;

            var researchManager = VoxelEngine.Research.ResearchManager.Instance;
            var bestScience = new Dictionary<int, RecipeDefinition>();
            foreach (var recipe in registry.recipes)
            {
                if (recipe == null || recipe.outputItem == null) continue;
                if (OutputsBuildToken(recipe)) continue;
                // Never surface hollow placeholders. They cannot be crafted safely
                // and should not leak raw asset names into player-facing UIs.
                if (recipe.inputs == null || recipe.inputs.Length == 0) continue;
                var sciPack = recipe.outputItem as ScienceItem;
                bool science = sciPack != null;
                if (science && scienceCap.HasValue)
                {
                    if ((int)ScienceStation(sciPack) > (int)scienceCap.Value) continue;
                }
                else if (!stationFilter(recipe)) continue;

                if (!science && !ignoreResearchLock)
                {
                    if (researchManager != null)
                    {
                        if (!researchManager.IsRecipeUnlocked(recipe)) continue;
                    }
                    else if (!recipe.unlockedByDefault)
                    {
                        continue;
                    }
                }

                if (science)
                {
                    int tier = ((ScienceItem)recipe.outputItem).tier;
                    if (!bestScience.TryGetValue(tier, out var current) || ScienceRecipeScore(recipe) > ScienceRecipeScore(current))
                        bestScience[tier] = recipe;
                    continue;
                }

                list.Add(recipe);
            }
            foreach (var pair in bestScience)
                list.Add(pair.Value);
            return list;
        }

        private static StationTier ScienceStation(ScienceItem pack)
        {
            if (pack == null || pack.tier <= 1) return StationTier.None;
            if (pack.tier == 2) return StationTier.CraftingBench;
            return StationTier.Assembler;
        }

        private static int ScienceRecipeScore(RecipeDefinition recipe)
        {
            int score = 0;
            if (recipe.unlockedByDefault) score += 10;
            if (recipe.inputs != null)
            {
                foreach (var input in recipe.inputs)
                    if (input.item != null && input.count > 0) score += 5;
            }
            if (recipe.outputItem != null && !string.IsNullOrEmpty(recipe.outputItem.itemId)
                && recipe.outputItem.itemId.StartsWith("item_sciencet", System.StringComparison.OrdinalIgnoreCase))
                score += 3;
            return score;
        }

        private static int CountCraftIngredient(IItemContainer source, ItemDefinition item, bool matchId)
        {
            if (source == null || item == null) return 0;
            if (!matchId || string.IsNullOrEmpty(item.itemId)) return source.CountOf(item);
            if (source is ItemContainer box) return box.CountOfId(item.itemId);
            if (source is NetworkItemSource net) return net.CountOfId(item.itemId);
            return source.CountOf(item);
        }

        private static void RemoveCraftIngredient(IItemContainer source, ItemDefinition item, int count, bool matchId)
        {
            if (source == null || item == null || count <= 0) return;
            if (!matchId || string.IsNullOrEmpty(item.itemId))
            {
                source.Remove(item, count);
                return;
            }
            if (source is ItemContainer box) box.RemoveId(item.itemId, count);
            else if (source is NetworkItemSource net) net.RemoveId(item.itemId, count);
            else source.Remove(item, count);
        }
    }
}
