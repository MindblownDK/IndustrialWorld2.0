// Assets/Scripts/Editor/Nuclear/NuclearSetupGuide.cs
//
// Ensures the unique Control Rod Assembly item and recipe under Nuclear/ while
// preserving the canonical reactor fuel/waste items under Survival/NuclearItems/.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.Nuclear
{
    public static class NuclearSetupGuide
    {
        public static void CreateAll()
        {
            const string assetRoot = "Assets/VoxelEngineAssets";
            const string basePath = assetRoot + "/Nuclear";
            if (!AssetDatabase.IsValidFolder(basePath))
                AssetDatabase.CreateFolder(assetRoot, "Nuclear");
            if (!AssetDatabase.IsValidFolder(basePath + "/Items"))
                AssetDatabase.CreateFolder(basePath, "Items");
            if (!AssetDatabase.IsValidFolder(basePath + "/Recipes"))
                AssetDatabase.CreateFolder(basePath, "Recipes");

            string controlRodPath = basePath + "/Items/Item_ControlRod.asset";
            var controlRod = AssetDatabase.LoadAssetAtPath<ResourceItem>(controlRodPath);
            bool createdRod = controlRod == null;
            if (createdRod)
            {
                controlRod = ScriptableObject.CreateInstance<ResourceItem>();
                AssetDatabase.CreateAsset(controlRod, controlRodPath);
            }
            if (createdRod || string.IsNullOrWhiteSpace(controlRod.itemId))
                controlRod.itemId = "item_controlrod";
            if (createdRod || string.IsNullOrWhiteSpace(controlRod.displayName))
                controlRod.displayName = "Control Rod Assembly";
            if (createdRod || string.IsNullOrWhiteSpace(controlRod.description))
                controlRod.description = "Boron-cobalt neutron-absorbing control rods for regulating fission reactor cores.";
            if (createdRod)
            {
                controlRod.iconTint = new Color(0.30f, 0.32f, 0.38f);
                controlRod.category = "Nuclear";
                controlRod.maxStack = 64;
                controlRod.massPerUnit = 2.5f;
            }
            EditorUtility.SetDirty(controlRod);

            string recipePath = basePath + "/Recipes/Recipe_ControlRod.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recipePath);
            if (recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, recipePath);
            }
            recipe.displayName = "Control Rod Assembly";
            recipe.outputItem = controlRod;
            recipe.outputCount = 1;
            recipe.requiredStation = StationTier.Assembler;
            if (recipe.craftSeconds <= 0f) recipe.craftSeconds = 5f;
            recipe.unlockedByDefault = false;

            var steel = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetRoot + "/Items/Item_SteelIngot.asset");
            var cobalt = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetRoot + "/Items/Item_Cobalt.asset");
            if (recipe.inputs == null || recipe.inputs.Length == 0 || recipe.inputs[0].item == null)
            {
                var inputs = new List<RecipeIngredient>();
                if (steel != null) inputs.Add(new RecipeIngredient { item = steel, count = 5 });
                if (cobalt != null) inputs.Add(new RecipeIngredient { item = cobalt, count = 3 });
                recipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(recipe);

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(assetRoot + "/RecipeRegistry.asset");
            if (registry != null && registry.recipes != null && !registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }

            AssetDatabase.SaveAssets();
        }
    }
}
#endif
