// Assets/Scripts/Editor/Items/CodeLockSetup.cs
//
// 13.17.0-dev - Step 104: Code Lock content.
//
// Authors the Code Lock item, its crafting recipe and its entry in the runtime
// item persistence catalog. Non-destructive and re-runnable: existing assets
// are connected, never replaced; only blank fields are repaired.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class CodeLockSetup
    {
        private const string AssetRoot     = "Assets/VoxelEngineAssets";
        private const string ItemsFolder   = AssetRoot + "/Items";
        private const string RecipesFolder = AssetRoot + "/Recipes";
        private const string ItemPath      = ItemsFolder + "/Item_CodeLock.asset";
        private const string RecipePath    = RecipesFolder + "/Recipe_CodeLock.asset";
        private const string RegistryPath  = AssetRoot + "/RecipeRegistry.asset";
        private const string CatalogPath   = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        public static void RunStep104()
        {
            EnsureFolder(ItemsFolder);
            EnsureFolder(RecipesFolder);

            // ── Item ──
            var conflict = AssetDatabase.LoadMainAssetAtPath(ItemPath);
            var item = AssetDatabase.LoadAssetAtPath<CodeLockItem>(ItemPath);
            if (item == null && conflict != null)
            {
                EditorUtility.DisplayDialog("Voxel Engine - Step 104",
                    $"An asset of a different type already sits at {ItemPath}. Nothing was changed.", "OK");
                return;
            }
            bool created = item == null;
            if (created)
            {
                item = ScriptableObject.CreateInstance<CodeLockItem>();
                AssetDatabase.CreateAsset(item, ItemPath);
            }
            if (string.IsNullOrEmpty(item.itemId)) item.itemId = "code_lock";
            if (string.IsNullOrEmpty(item.displayName)) item.displayName = "Code Lock";
            if (created)
            {
                item.description = "Keypad lock for doors, gates, garage doors and floor hatches. " +
                                   "Fit it with a right-click, then set a four-digit code. " +
                                   "Wrong guesses sting.";
                item.iconTint = new Color(0.30f, 0.42f, 0.30f);
                item.maxStack = 20;
                item.massPerUnit = 4f;
                item.category = "Tools";
            }
            EditorUtility.SetDirty(item);

            // ── Recipe ──
            var ironIngot = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/Item_IronIngot.asset");
            var copperIngot = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/Item_CopperIngot.asset");
            var recipe = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(RecipePath);
            if (recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, RecipePath);
            }
            recipe.displayName = "Code Lock";
            recipe.outputItem = item;
            recipe.outputCount = 1;
            recipe.requiredStation = VoxelEngine.Crafting.StationTier.CraftingBench;
            recipe.craftSeconds = 0f;
            recipe.unlockedByDefault = true;
            var inputs = new List<VoxelEngine.Crafting.RecipeIngredient>();
            if (ironIngot != null) inputs.Add(new VoxelEngine.Crafting.RecipeIngredient { item = ironIngot, count = 6 });
            if (copperIngot != null) inputs.Add(new VoxelEngine.Crafting.RecipeIngredient { item = copperIngot, count = 4 });
            recipe.inputs = inputs.ToArray();
            EditorUtility.SetDirty(recipe);

            var registry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>(RegistryPath);
            if (registry != null && !registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }

            // ── Persistence catalog (runtime-safe id lookup for saves + refunds) ──
            var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(CatalogPath);
            if (catalog != null && !catalog.items.Contains(item))
            {
                catalog.items.Add(item);
                EditorUtility.SetDirty(catalog);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string ingredientNote = ironIngot == null || copperIngot == null
                ? "\nNOTE: Iron/Copper Ingot item assets were not all found - run the base " +
                  "crafting steps first, then re-run Step 104 to complete the recipe."
                : "";
            EditorUtility.DisplayDialog("Voxel Engine - Step 104",
                "Code Lock ready:\n" +
                "  - Item_CodeLock (id: code_lock)\n" +
                "  - Recipe_CodeLock (Crafting Bench: 6 Iron Ingot + 4 Copper Ingot)\n" +
                "  - Registered in RecipeRegistry + ItemPersistenceCatalog" + ingredientNote, "OK");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash > 0) EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
#endif
