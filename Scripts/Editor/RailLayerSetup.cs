#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 93 (11.33.0-dev): the Rail Layer — Train System v2, phase 3.
    ///
    /// A carried tool that lays whole rail runs by dragging, at a chosen gauge, instead of
    /// placing hundreds of cells by hand. Reuses the road system's corridor solver, so
    /// curves and multi-track widths come from code that is already proven.
    ///
    /// Non-destructive: an existing tool asset or recipe keeps every authored value and
    /// only missing links are repaired. Safe to re-run. Logs with the [Setup 93] prefix.
    /// </summary>
    public static class RailLayerSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string ItemsFolder = Root + "/Items";
        private const string RecipesFolder = Root + "/Recipes";

        private static readonly Color ToolTint = new(0.58f, 0.62f, 0.68f);

        public static void RunStep93()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Rail Layer", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Rail Layer",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.",
                        "OK");
                    return;
                }

                var steel = FindItem("Item_SteelIngot");
                var wire = FindItem("Item_CopperWire");
                if (steel == null)
                {
                    EditorUtility.DisplayDialog("Rail Layer",
                        "Steel Ingot not found. Run the earlier crafting-content steps first.", "OK");
                    return;
                }

                // The tool must lay the SAME block the player places by hand, or a run and a
                // hand-laid cell would be two different things that happen to look alike.
                var trackBlock = FindBlock("Block_RailTrack");
                if (trackBlock == null)
                {
                    EditorUtility.DisplayDialog("Rail Layer",
                        "Rail Track block not found.\n\nRun step 85 (Build the Rail System) first, " +
                        "then re-run this step.", "OK");
                    return;
                }

                var tool = EnsureTool(trackBlock, steel, out bool toolChanged);
                bool recipeChanged = EnsureRecipe(registry, tool, steel, wire);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("Step 93 - Rail Layer",
                    "Rail Layer authored.\n\n" +
                    "  RAIL LAYER   Steel x25" + (wire != null ? " + Wire x15" : "") + "\n\n" +
                    "How to use it:\n" +
                    "  1. Hold the Rail Layer.\n" +
                    "  2. Click once where the run should start.\n" +
                    "  3. Aim at the far end and click again to lay it.\n" +
                    "  Right-click cancels a run in progress.\n" +
                    "  Ctrl + scroll picks the gauge (1-3 parallel tracks).\n\n" +
                    "Curves are solved automatically. A run that is too steep,\n" +
                    "or a corner too tight for the chosen gauge, is REFUSED with\n" +
                    "the reason rather than laid as track no train can use.\n\n" +
                    "It lays the same Rail Track block you place by hand, so runs\n" +
                    "and hand-laid cells join up normally.\n\n" +
                    ((toolChanged || recipeChanged)
                        ? "Changes were written. See the Console."
                        : "Everything was already in place."),
                    "OK");

                Debug.Log("[Setup 93] Rail Layer setup complete.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 93] Aborted: " + ex);
                EditorUtility.DisplayDialog("Rail Layer",
                    "Setup stopped: " + ex.Message + "\n\nNothing further was written.", "OK");
            }
        }

        private static RailLayerTool EnsureTool(BlockItem trackBlock, ItemDefinition steel, out bool changed)
        {
            string path = ItemsFolder + "/Item_RailLayer.asset";
            var tool = AssetDatabase.LoadAssetAtPath<RailLayerTool>(path);
            bool created = false;

            if (tool == null)
            {
                EnsureFolder(ItemsFolder);
                tool = ScriptableObject.CreateInstance<RailLayerTool>();
                created = true;
            }
            bool dirty = created;

            if (tool.itemId != "raillayer") { tool.itemId = "raillayer"; dirty = true; }
            if (tool.displayName != "Rail Layer") { tool.displayName = "Rail Layer"; dirty = true; }
            if (tool.maxDurability <= 0) { tool.maxDurability = 400; dirty = true; }
            if (tool.massPerUnit <= 0f) { tool.massPerUnit = 6f; dirty = true; }
            if (tool.category != "Tools") { tool.category = "Tools"; dirty = true; }
            if (string.IsNullOrEmpty(tool.description))
            {
                tool.description =
                    "Lays whole rail runs by dragging. Click a start, aim at the far end, click " +
                    "again. Curves are solved for you; a slope a train could not pull is refused " +
                    "rather than laid. Ctrl + scroll sets the gauge, up to three parallel tracks.";
                dirty = true;
            }
            if (tool.icon == null) tool.iconTint = ToolTint;
            if (created) tool.defaultGauge = 1;

            // Repair the links even on an existing asset: a tool with no track block is a
            // tool that silently does nothing.
            if (tool.trackBlock == null) { tool.trackBlock = trackBlock; dirty = true; }
            if (tool.railMaterial == null) { tool.railMaterial = steel; dirty = true; }
            if (tool.materialPerCell <= 0) { tool.materialPerCell = 1; dirty = true; }

            if (dirty)
            {
                if (!AssetDatabase.Contains(tool)) AssetDatabase.CreateAsset(tool, path);
                EditorUtility.SetDirty(tool);
                Debug.Log("[Setup 93] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            changed = dirty;
            return tool;
        }

        private static bool EnsureRecipe(RecipeRegistry registry, RailLayerTool tool,
            ItemDefinition steel, ItemDefinition wire)
        {
            const string stem = "Recipe_RailLayer";
            var recipe = FindRecipe(stem);
            bool changed = false;

            RecipeIngredient[] Inputs()
            {
                if (wire == null)
                    return new[] { new RecipeIngredient { item = steel, count = 25 } };
                return new[]
                {
                    new RecipeIngredient { item = steel, count = 25 },
                    new RecipeIngredient { item = wire, count = 15 },
                };
            }

            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = "Rail Layer";
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 16f;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = tool;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, RecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 93] Created " + stem + ".");
            }
            else
            {
                if (recipe.outputItem == null && tool != null)
                {
                    recipe.outputItem = tool;
                    recipe.outputCount = 1;
                    EditorUtility.SetDirty(recipe);
                    changed = true;
                }
                if (recipe.inputs == null || recipe.inputs.Length == 0)
                {
                    recipe.inputs = Inputs();
                    EditorUtility.SetDirty(recipe);
                    changed = true;
                }
            }

            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                Debug.Log("[Setup 93] Added " + stem + " to RecipeRegistry.");
                changed = true;
            }

            return changed;
        }

        // ============================================================
        //                        Helpers
        // ============================================================
        private static ItemDefinition FindItem(string stem)
        {
            var guids = AssetDatabase.FindAssets(stem + " t:ItemDefinition");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == stem)
                    return AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
            }
            return null;
        }

        private static BlockItem FindBlock(string stem)
        {
            var guids = AssetDatabase.FindAssets(stem + " t:BlockItem");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == stem)
                    return AssetDatabase.LoadAssetAtPath<BlockItem>(p);
            }
            return null;
        }

        private static RecipeDefinition FindRecipe(string stem)
        {
            var guids = AssetDatabase.FindAssets(stem + " t:RecipeDefinition");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == stem)
                    return AssetDatabase.LoadAssetAtPath<RecipeDefinition>(p);
            }
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            var leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
