// Assets/Scripts/VoxelEngine/Crafting/RecipeDefinition.cs
using System;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Crafting
{
    /// <summary>
    /// Workstation tiers. A recipe with stationTier == None can be crafted in the player
    /// inventory (no station required). Higher tiers require being near a matching station.
    /// </summary>
    public enum StationTier { None, CraftingBench, Furnace, Assembler }

    [Serializable]
    public struct RecipeIngredient
    {
        public ItemDefinition item;
        public int            count;
    }

    [CreateAssetMenu(menuName = "Voxel Engine/Crafting/Recipe", fileName = "Recipe_New")]
    public class RecipeDefinition : ScriptableObject
    {
        public string displayName;
        public Sprite icon; // optional — falls back to output item's icon

        [Header("Inputs")]
        public RecipeIngredient[] inputs;

        [Header("Output")]
        public ItemDefinition outputItem;
        public int            outputCount = 1;

        [Header("Station / Time")]
        public StationTier requiredStation = StationTier.None;
        [Tooltip("Crafting time in seconds. 0 = instant.")]
        public float craftSeconds = 0f;

        [Header("Discovery")]
        [Tooltip("If true, this recipe is in the player's known list from the start.")]
        public bool unlockedByDefault = true;

        public Sprite GetIcon() => icon != null ? icon : (outputItem ? outputItem.icon : null);
        public string GetName()
        {
            if (!string.IsNullOrEmpty(displayName)) return displayName;
            if (outputItem != null && !string.IsNullOrEmpty(outputItem.displayName))
                return outputItem.displayName;
            // Never leak the raw asset name ("Recipe_IronPlate") into the UI —
            // prettify it ("Iron Plate") so placeholder recipes still read clean.
            return PrettifyAssetName(name);
        }

        private static string PrettifyAssetName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Recipe";
            var s = raw;
            if (s.StartsWith("MachineRecipe_")) s = s.Substring("MachineRecipe_".Length);
            else if (s.StartsWith("Recipe_")) s = s.Substring("Recipe_".Length);
            else if (s.StartsWith("Smelt_")) s = s.Substring("Smelt_".Length);
            return s.Replace('_', ' ');
        }
    }
}
