// Assets/Scripts/VoxelEngine/Crafting/CraftTimeDefaults.cs
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Crafting
{
    /// <summary>
    /// Shared authoring defaults for workstation recipes. Setup callers apply these only
    /// when a recipe has no valid authored duration, so a positive tuned time always wins.
    /// Inventory recipes remain instant because they have no workstation queue.
    /// </summary>
    public static class CraftTimeDefaults
    {
        public static float Suggest(RecipeDefinition recipe)
        {
            return recipe == null ? 0f : Suggest(recipe.requiredStation, recipe.inputs);
        }

        public static float Suggest(StationTier station)
        {
            return Suggest(station, 0, 0);
        }

        public static float Suggest(StationTier station, RecipeIngredient[] ingredients)
        {
            int kinds = 0;
            int totalUnits = 0;
            if (ingredients != null)
            {
                foreach (var ingredient in ingredients)
                    AddInput(ingredient.item, ingredient.count, ref kinds, ref totalUnits);
            }
            return Suggest(station, kinds, totalUnits);
        }

        public static float Suggest(StationTier station, (ItemDefinition item, int count)[] ingredients)
        {
            int kinds = 0;
            int totalUnits = 0;
            if (ingredients != null)
            {
                foreach (var ingredient in ingredients)
                    AddInput(ingredient.item, ingredient.count, ref kinds, ref totalUnits);
            }
            return Suggest(station, kinds, totalUnits);
        }

        private static void AddInput(ItemDefinition item, int count, ref int kinds, ref int totalUnits)
        {
            if (item == null || count <= 0) return;
            kinds++;
            totalUnits = totalUnits > int.MaxValue - count
                ? int.MaxValue
                : totalUnits + count;
        }

        private static float Suggest(StationTier station, int ingredientKinds, int totalUnits)
        {
            float baseSeconds;
            float secondsPerExtraIngredient;
            float secondsPerExtraUnit;
            float maximumSeconds;

            switch (station)
            {
                case StationTier.CraftingBench:
                    baseSeconds = 2.5f;
                    secondsPerExtraIngredient = 0.5f;
                    secondsPerExtraUnit = 0.12f;
                    maximumSeconds = 12f;
                    break;
                case StationTier.Furnace:
                    baseSeconds = 4f;
                    secondsPerExtraIngredient = 0.6f;
                    secondsPerExtraUnit = 0.14f;
                    maximumSeconds = 14f;
                    break;
                case StationTier.Assembler:
                    baseSeconds = 6f;
                    secondsPerExtraIngredient = 0.8f;
                    secondsPerExtraUnit = 0.16f;
                    maximumSeconds = 18f;
                    break;
                case StationTier.ArmorStation:
                    baseSeconds = 7f;
                    secondsPerExtraIngredient = 0.9f;
                    secondsPerExtraUnit = 0.18f;
                    maximumSeconds = 20f;
                    break;
                default:
                    return 0f;
            }

            int extraKinds = Mathf.Max(0, ingredientKinds - 1);
            int extraUnits = Mathf.Max(0, totalUnits - ingredientKinds);
            float suggested = baseSeconds
                + extraKinds * secondsPerExtraIngredient
                + Mathf.Min(extraUnits, 50) * secondsPerExtraUnit;
            return Mathf.Clamp(suggested, baseSeconds, maximumSeconds);
        }
    }
}
