// Assets/Scripts/VoxelEngine/Editor/ProspectingSetup.cs
//
// Step 59: GEOLOGICAL PROSPECTING & ORE DETECTION — non-destructive authoring
// for prospecting tools, sub-surface acoustic radar, and spherical ore detection:
//
//   • Creates the GEOLOGICAL PROSPECTING SCANNER tool (handheld acoustic radar for
//     field prospectors that probes sub-surface voxel layers along the planetary radial down vector).
//   • Creates its recipe (Iron Ingot + Copper Ingot + Silicon/Circuit) at the Crafting Bench / Assembler.
//   • Registers the recipe in RecipeRegistry.
//   • Non-destructive: preserves existing balance values, custom modifications, and authored tuning.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class ProspectingSetup
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";
        private const string ITEMS      = ASSET_ROOT + "/Items";
        private const string RECIPES    = ASSET_ROOT + "/Recipes";

        public static void RunStep59()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 59 — Geological Prospecting Tools setup started.");

            EnsureFolder(ITEMS);
            EnsureFolder(RECIPES);

            int created = 0, preserved = 0;

            // ── 1) Geological Prospecting Scanner Item ──
            string scannerPath = ITEMS + "/Tool_ProspectingScanner.asset";
            var scanner = AssetDatabase.LoadAssetAtPath<ProspectingScanner>(scannerPath);
            if (scanner == null)
            {
                scanner = ScriptableObject.CreateInstance<ProspectingScanner>();
                AssetDatabase.CreateAsset(scanner, scannerPath);
                scanner.itemId = "tool_prospecting_scanner";
                scanner.displayName = "Geological Prospecting Scanner";
                scanner.description =
                    "Handheld acoustic radar scanner. Right-click the ground to ping for subterranean " +
                    "ore deposits up to 24 meters deep along the planetary radial down vector. 120 pings.";
                scanner.iconTint = new Color(0.35f, 0.85f, 1.0f);
                scanner.maxStack = 1;
                scanner.maxDurability = 120;
                scanner.scanDepthMeters = 24f;
                scanner.scanRadiusMeters = 6f;
                scanner.toolType = ToolType.Other;
                scanner.category = "Tools";
                created++;
            }
            else
            {
                preserved++;
            }
            EditorUtility.SetDirty(scanner);

            // ── 2) Recipe for Geological Prospecting Scanner ──
            var iron   = LoadItem(ITEMS + "/Item_IronIngot.asset") ?? LoadItem(ITEMS + "/Item_IronPlate.asset");
            var copper = LoadItem(ITEMS + "/Item_CopperIngot.asset") ?? LoadItem(ITEMS + "/Item_CopperLVWire.asset");
            var silicon = LoadItem(ITEMS + "/Item_Silicon.asset") ?? LoadItem(ITEMS + "/Item_CircuitBoard.asset");

            string recipePath = RECIPES + "/Recipe_ProspectingScanner.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recipePath);
            if (recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, recipePath);
                recipe.displayName = "Geological Prospecting Scanner";
                recipe.outputItem = scanner;
                recipe.outputCount = 1;
                recipe.requiredStation = StationTier.CraftingBench;
                recipe.craftSeconds = 3f;
                recipe.unlockedByDefault = true;

                var inputs = new List<RecipeIngredient>();
                if (iron != null) inputs.Add(new RecipeIngredient { item = iron, count = 2 });
                if (copper != null) inputs.Add(new RecipeIngredient { item = copper, count = 2 });
                if (silicon != null) inputs.Add(new RecipeIngredient { item = silicon, count = 1 });
                recipe.inputs = inputs.ToArray();
                created++;
            }
            else
            {
                preserved++;
            }
            EditorUtility.SetDirty(recipe);

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            if (registry != null && !registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Prospecting & Ore Detection",
                "Geological Prospecting & Ore Detection (non-destructive):\n\n" +
                "• Geological Prospecting Scanner: " + (created > 0 ? "Created tool & recipe." : "Preserved existing assets.") + "\n" +
                "• Right-click the ground with the scanner to ping subterranean voxel layers (up to 24m depth).\n" +
                "• Grid Ore Detector now supports spherical-safe radial coreward scanning and IGridDataProvider telemetry for GridScreenBlock LCDs.\n" +
                "• Recipes and balance tuning preserved.",
                "OK");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        private static ItemDefinition LoadItem(string path)
        {
            return AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
        }
    }
}
#endif
