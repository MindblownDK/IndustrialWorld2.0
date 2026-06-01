// Assets/Scripts/VoxelEngine/Crafting/ProcessingRecipe.cs
//
// Multi-input / multi-output recipe used by industrial processors
// (Oil Refinery, Plastic Press, Chemical Plant, etc.).
//
// Differs from RecipeDefinition (player crafting) and SmeltingRecipe (1 in / 1 out):
//   * Supports up to N inputs and M outputs (each with a count)
//   * Has its own seconds-per-batch + power-draw multiplier
//   * Outputs go to a different output container by index, so machines can
//     route byproducts to dedicated slots.

using System;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Crafting
{
    [Serializable]
    public struct ProcessingIO
    {
        public ItemDefinition item;
        public int            count;
    }

    [CreateAssetMenu(menuName = "Voxel Engine/Crafting/Processing Recipe", fileName = "Proc_New")]
    public class ProcessingRecipe : ScriptableObject
    {
        [Header("Identity")]
        public string displayName;
        [Tooltip("Free-form category, e.g. 'Refinery', 'Plastics', 'Chemistry'. Used by machines to filter their recipe list.")]
        public string category = "Refinery";

        [Header("I/O")]
        public ProcessingIO[] inputs  = Array.Empty<ProcessingIO>();
        public ProcessingIO[] outputs = Array.Empty<ProcessingIO>();

        [Header("Tuning")]
        [Tooltip("Seconds per batch at 1x speed multiplier.")]
        public float secondsPerBatch = 8f;
        [Tooltip("Extra power draw multiplier applied on top of the machine's base watts.")]
        public float powerDrawMultiplier = 1f;

        public string GetDisplayName()
        {
            if (!string.IsNullOrEmpty(displayName)) return displayName;
            if (outputs != null && outputs.Length > 0 && outputs[0].item != null)
                return outputs[0].item.displayName;
            return name;
        }
    }
}
