// Assets/Scripts/VoxelEngine/Simulation/MachineRecipe.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — MACHINE RECIPE (ScriptableObject)           ║
// ║  Extended recipe type for machine-processed items. Supports     ║
// ║  multiple inputs, multiple outputs, and byproduct yields.       ║
// ║  Distinct from SmeltingRecipe (single-in/single-out) and the    ║
// ║  hand-crafting RecipeDefinition.                                ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    /// <summary>Which machine category can execute this recipe.</summary>
    [Serializable]
    public enum MachineRecipeType
    {
        Smelting,
        Crushing,
        Assembling,
        Chemical,
        Washing,
        Custom
    }

    [Serializable]
    public struct MachineRecipeSlot
    {
        public ItemDefinition item;
        public int count;
    }

    [CreateAssetMenu(menuName = "Voxel Engine/Simulation/Machine Recipe", fileName = "MachineRecipe_New")]
    public class MachineRecipe : ScriptableObject
    {
        [Header("Identity")]
        public string displayName;
        public Sprite icon;

        [Header("Type")]
        public MachineRecipeType recipeType = MachineRecipeType.Assembling;

        [Header("Inputs (up to 6)")]
        public MachineRecipeSlot[] inputs = Array.Empty<MachineRecipeSlot>();

        [Header("Primary Output")]
        public ItemDefinition outputItem;
        public int outputCount = 1;

        [Header("Byproduct (optional)")]
        [Tooltip("Secondary output — e.g. slag from crushing, tailings from washing.")]
        public ItemDefinition byproductItem;
        public int byproductCount;
        [Tooltip("Chance (0-1) that the byproduct is produced per batch.")]
        [Range(0f, 1f)]
        public float byproductChance = 1f;

        [Header("Timing")]
        [Tooltip("Base processing time in seconds. Modified by machine speed multiplier.")]
        public float processSeconds = 4f;

        [Header("Discovery")]
        [Tooltip("Available from game start, or gated behind research/blueprint.")]
        public bool unlockedByDefault = true;

        // ── Helpers ───────────────────────────────────────────────────

        public Sprite GetIcon() => icon != null ? icon : (outputItem ? outputItem.icon : null);

        public string GetName()
        {
            if (!string.IsNullOrEmpty(displayName)) return displayName;
            if (outputItem != null && !string.IsNullOrEmpty(outputItem.displayName))
                return outputItem.displayName;
            // Prettified asset-name fallback — never show raw "MachineRecipe_X" in UI.
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

        /// <summary>
        /// Check if the given set of input items matches this recipe.
        /// </summary>
        public bool MatchesInputs(ItemDefinition[] items, int[] counts)
        {
            if (inputs == null || inputs.Length == 0) return false;
            if (items == null || counts == null) return false;

            for (int i = 0; i < inputs.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < items.Length; j++)
                {
                    if (items[j] == inputs[i].item && counts[j] >= inputs[i].count)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }
    }
}
