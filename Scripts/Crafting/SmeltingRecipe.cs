// Assets/Scripts/VoxelEngine/Crafting/SmeltingRecipe.cs
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Crafting
{
    [CreateAssetMenu(menuName = "Voxel Engine/Crafting/Smelting Recipe", fileName = "Smelt_New")]
    public class SmeltingRecipe : ScriptableObject
    {
        public ItemDefinition input;
        public int            inputCount  = 1;
        public ItemDefinition output;
        public int            outputCount = 1;
        [Tooltip("Seconds to smelt one batch.")]
        public float          smeltSeconds = 5f;

        [Header("Progression (17.26.0-dev)")]
        [Tooltip("Research that must be finished before a furnace will run this recipe. Empty = available from the start. " +
                 "Alloying metals (steel, brass) was reachable the moment a Furnace existed, which is why alloy research " +
                 "unlocked nothing that mattered.")]
        public VoxelEngine.Research.ResearchNode requiresResearch;

        /// <summary>
        /// True when this recipe may be offered and run right now: either it is ungated
        /// or its research node has been completed. Fails OPEN when there is no research
        /// manager in the scene, so a test scene or a sliced-off world never loses its
        /// smelting entirely.
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                if (requiresResearch == null) return true;
                var rm = VoxelEngine.Research.ResearchManager.Instance;
                if (rm == null) return true;
                return rm.IsUnlocked(requiresResearch);
            }
        }

        /// <summary>Name of the blocking research, or null when the recipe is available.</summary>
        public string LockedByResearchName
        {
            get
            {
                if (IsAvailable) return null;
                return string.IsNullOrEmpty(requiresResearch.displayName)
                    ? requiresResearch.nodeId
                    : requiresResearch.displayName;
            }
        }
    }
}
