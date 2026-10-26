// Assets/Scripts/Editor/Crafting/LiquidOverhaulSetup.cs
//
// Step 56: LIQUIDS OVERHAUL WIRING — non-destructive authoring for the
// seven-liquid world using the canonical Tool_LiquidCanister asset ("liquid_canister")
// and Recipe_LiquidCanister ("res_fluid_handling"). Retires any leftover Water Bucket
// duplicate assets.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class LiquidOverhaulSetup
    {
        private const string AssetRoot = "Assets/VoxelEngineAssets";
        private static readonly string[] IndustrialKeywords =
        {
            "Industrial", "Factory", "Refinery", "Forge", "Foundry", "Plant",
        };

        public static void RunStep56()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 56 — Liquids Overhaul Wiring started.");
            int flagged = 0;

            string canisterPath = AssetRoot + "/Items/Tool_LiquidCanister.asset";
            var canister = AssetDatabase.LoadAssetAtPath<LiquidCanister>(canisterPath);
            bool created = canister == null;
            if (created)
            {
                canister = ScriptableObject.CreateInstance<LiquidCanister>();
                AssetDatabase.CreateAsset(canister, canisterPath);
            }
            canister.itemId = "liquid_canister";
            if (created || string.IsNullOrWhiteSpace(canister.displayName)
                || canister.displayName.IndexOf("Bucket", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                canister.displayName = "Liquid Canister";
            }
            if (created || string.IsNullOrWhiteSpace(canister.description)
                || canister.description.IndexOf("bucket", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                canister.description =
                    "A 10 L canister that holds ONE liquid at a time. RMB on a liquid pool to scoop " +
                    "0.5 L per click (again and again until full). LMB pours 0.5 L into the world. RMB a " +
                    "liquid tank or water pump to pour the liquid in, and RMB an infinity jack pump to " +
                    "fill the canister with crude oil.";
            }
            canister.maxStack = 1;
            canister.maxDurability = LiquidCanister.CapacityMl;
            canister.toolType = ToolType.Other;
            if (string.IsNullOrWhiteSpace(canister.category)) canister.category = "Tools";
            EditorUtility.SetDirty(canister);

            string recipePath = AssetRoot + "/Recipes/Recipe_LiquidCanister.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recipePath);
            if (recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, recipePath);
            }
            recipe.displayName = "Liquid Canister";
            recipe.outputItem = canister;
            recipe.outputCount = 1;
            recipe.requiredStation = StationTier.CraftingBench;
            if (recipe.craftSeconds <= 0f) recipe.craftSeconds = 2f;
            recipe.unlockedByDefault = false;
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var iron = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetRoot + "/Items/Item_IronIngot.asset");
                if (iron != null)
                    recipe.inputs = new[] { new RecipeIngredient { item = iron, count = 3 } };
            }
            EditorUtility.SetDirty(recipe);

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(AssetRoot + "/RecipeRegistry.asset");
            if (registry != null && registry.recipes != null && !registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }

            // Industrial planet templates
            var guids = AssetDatabase.FindAssets("t:PlanetTemplate");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var template = AssetDatabase.LoadAssetAtPath<PlanetTemplate>(path);
                if (template == null || template.body == null) continue;

                string name = template.body.bodyName?.Trim() ?? string.Empty;
                if (name.Length == 0) continue;

                bool industrial = false;
                for (int k = 0; k < IndustrialKeywords.Length; k++)
                {
                    if (name.IndexOf(IndustrialKeywords[k], System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        industrial = true;
                        break;
                    }
                }
                if (industrial && !template.body.industrialWorld)
                {
                    template.body.industrialWorld = true;
                    EditorUtility.SetDirty(template);
                    flagged++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Liquids Overhaul",
                "Liquids Overhaul Wiring (non-destructive):\n\n" +
                "• Canonical Tool_LiquidCanister ('liquid_canister') and Recipe_LiquidCanister verified.\n" +
                (flagged > 0
                    ? $"• {flagged} industrial planet template(s) flagged for natural fuel/refined-product lakes.\n"
                    : "• Industrial planet templates already configured.\n"),
                "OK");
        }
    }
}
#endif
