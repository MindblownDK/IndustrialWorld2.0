// Assets/Scripts/Editor/GridSystem/WelderSetup.cs
//
// Step 113 - Welder Tool (14.61.0). Non-destructive: creates the Welder tool
// asset and its recipe if missing, reconnects references if they exist, and
// never reverts tweaks the team has made to numbers on existing assets.

using UnityEditor;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class WelderSetup
    {
        private const string ASSET_ROOT    = "Assets/VoxelEngineAssets";
        private const string ToolsFolder   = ASSET_ROOT + "/Tools";
        private const string RecipesFolder = ASSET_ROOT + "/Recipes";

        public static void Run()
        {
            Debug.Log("[VoxelEngineSetup] Step 113 - Welder Tool setup started.");
            EnsureFolder(ToolsFolder);
            EnsureFolder(RecipesFolder);

            int created = 0, preserved = 0;

            // ── 1. Welder tool asset ──────────────────────────────────
            string toolPath = ToolsFolder + "/Tool_Welder.asset";
            var welder = AssetDatabase.LoadAssetAtPath<VoxelEngine.GridSystem.WelderTool>(toolPath);
            bool isNew = welder == null;
            if (isNew)
            {
                welder = ScriptableObject.CreateInstance<VoxelEngine.GridSystem.WelderTool>();
                AssetDatabase.CreateAsset(welder, toolPath);
                created++;
            }
            else preserved++;

            if (isNew)
            {
                welder.itemId        = "welder_tool";
                welder.displayName   = "Welder";
                welder.description   = "Repairs grid (ship/vehicle) blocks. Hold LMB on a damaged block to " +
                                       "restore hit points - repairs consume material as they go, and the " +
                                       "look-at card shows the full cost of the block under the crosshair.";
                welder.maxStack      = 1;
                welder.toolType      = ToolType.Other;
                welder.miningTier    = 3;
                welder.maxDurability = 800;
                welder.iconTint      = new Color(0.25f, 0.85f, 1f);
                welder.category      = "Tools";
                welder.repairHPPerSecond = 45f;
                welder.hpPerMaterialUnit = 60f;
            }
            // Reconnect-if-missing (never override a deliberate material choice).
            if (welder.repairMaterial == null)
                welder.repairMaterial = FindItemByName("Iron Ingot") ?? FindItemByName("Iron Plate");
            EditorUtility.SetDirty(welder);

            // ── 2. Recipe (Assembler tier, like the grinder) ──────────
            string recipePath = RecipesFolder + "/Recipe_WelderTool.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(recipePath);
            bool recipeNew = recipe == null;
            if (recipeNew)
            {
                recipe = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, recipePath);
                created++;
            }
            else preserved++;

            if (string.IsNullOrWhiteSpace(recipe.displayName)) recipe.displayName = "Welder";
            recipe.outputItem = welder;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            if (recipeNew)
            {
                recipe.requiredStation = VoxelEngine.Crafting.StationTier.Assembler;
                recipe.craftSeconds = 4f;
                recipe.unlockedByDefault = true;
            }
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var iron   = FindItemByName("Iron Ingot");
                var copper = FindItemByName("Copper Ingot");
                var list = new System.Collections.Generic.List<VoxelEngine.Crafting.RecipeIngredient>();
                if (iron != null)   list.Add(new VoxelEngine.Crafting.RecipeIngredient { item = iron,   count = 4 });
                if (copper != null) list.Add(new VoxelEngine.Crafting.RecipeIngredient { item = copper, count = 2 });
                recipe.inputs = list.ToArray();
            }
            EditorUtility.SetDirty(recipe);

            // ── 3. Register the recipe ────────────────────────────────
            var registry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>(
                ASSET_ROOT + "/RecipeRegistry.asset");
            if (registry != null && !registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[VoxelEngineSetup] Step 113 - Welder Tool complete: {created} created, {preserved} preserved. " +
                      (welder.repairMaterial != null
                          ? $"Repair material: {welder.repairMaterial.displayName}."
                          : "WARNING: no Iron Ingot/Plate found - assign repairMaterial on Tool_Welder manually."));
        }

        private static ItemDefinition FindItemByName(string displayName)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item != null && string.Equals(item.displayName, displayName,
                        System.StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
