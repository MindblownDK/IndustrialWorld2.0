// Assets/Scripts/VoxelEngine/Items/BlueprintDataCoreItem.cs
//
// Damaged Blueprint Data Core — found in ruins.
// Right-click (or use from hotbar) restores the blueprint and unlocks its recipe.

using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Exploration;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Blueprint Data Core", fileName = "Item_BlueprintCore")]
    public class BlueprintDataCoreItem : ResourceItem
    {
        [Header("Blueprint")]
        [Tooltip("Human-readable name of what this core unlocks (e.g. Wind Turbine Nacelle)")]
        public string targetDisplayName = "Unknown Blueprint";

        [Tooltip("Exact RecipeDefinition asset name to unlock (e.g. Recipe_t90_Nacelle)")]
        public string targetRecipeAssetName;

        [Tooltip("If set, unlocks this recipe asset directly (overrides asset name lookup)")]
        public RecipeDefinition targetRecipe;

        public bool TryUnlock()
        {
            BlueprintUnlockManager.EnsureInstance();
            var mgr = BlueprintUnlockManager.Instance;
            if (mgr == null) return false;

            string assetName = targetRecipe != null ? targetRecipe.name : targetRecipeAssetName;
            if (string.IsNullOrEmpty(assetName))
            {
                Debug.LogWarning($"[Blueprint] {displayName} has no targetRecipe set");
                return false;
            }

            // Try to find recipe by name if only asset name provided
            if (targetRecipe == null && !string.IsNullOrEmpty(targetRecipeAssetName))
            {
                // The manager stores by asset name, so we can unlock even if RecipeDefinition not loaded yet
                return mgr.Unlock(targetRecipeAssetName);
            }

            return mgr.Unlock(targetRecipe);
        }
    }
}
