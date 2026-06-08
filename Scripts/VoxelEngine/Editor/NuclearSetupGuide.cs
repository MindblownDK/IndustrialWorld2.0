// Assets/Scripts/VoxelEngine/Nuclear/NuclearSetupGuide.cs
//
// Editor wizard that creates all nuclear items and recipes.
// Menu: Tools > Voxel Engine > Create Nuclear Content

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.Nuclear
{
    public static class NuclearSetupGuide
    {
        [MenuItem("Tools/Voxel Engine/Create Nuclear Content")]
        public static void CreateAll()
        {
            string basePath = "Assets/VoxelEngineAssets/Nuclear";
            if (!AssetDatabase.IsValidFolder(basePath))
                AssetDatabase.CreateFolder("Assets/VoxelEngineAssets", "Nuclear");
            if (!AssetDatabase.IsValidFolder(basePath + "/Items"))
                AssetDatabase.CreateFolder(basePath, "Items");
            if (!AssetDatabase.IsValidFolder(basePath + "/Recipes"))
                AssetDatabase.CreateFolder(basePath, "Recipes");

            // ── ITEMS ────────────────────────────────────────────
            var enrichedRod = CreateItem("Item_EnrichedFuelRod", "Enriched Fuel Rod",
                "Enriched uranium fuel rod for the nuclear reactor core.",
                new Color(0.2f, 0.9f, 0.3f), "Nuclear", basePath + "/Items");

            var leuPellet = CreateItem("Item_LEUPellet", "LEU Pellet",
                "Low-Enriched Uranium pellet for the portable reactor.",
                new Color(0.3f, 0.7f, 0.2f), "Nuclear", basePath + "/Items");

            var spentRod = CreateItem("Item_SpentFuelRod", "Spent Fuel Rod",
                "Exhausted fuel rod. Reprocess to recover uranium.",
                new Color(0.5f, 0.4f, 0.2f), "Nuclear", basePath + "/Items");

            var depletedU = CreateItem("Item_DepletedUranium", "Depleted Uranium",
                "Byproduct of enrichment. Can be reprocessed into LEU pellets.",
                new Color(0.4f, 0.5f, 0.3f), "Nuclear", basePath + "/Items");

            var hlWaste = CreateItem("Item_HighLevelWaste", "High-Level Waste",
                "Vitrified nuclear waste. Must be stored safely. Cannot be reprocessed further.",
                new Color(0.6f, 0.2f, 0.1f), "Nuclear", basePath + "/Items");

            var controlRod = CreateItem("Item_ControlRod", "Control Rod Assembly",
                "Boron carbide control rods for regulating reactor power.",
                new Color(0.3f, 0.3f, 0.35f), "Nuclear", basePath + "/Items");

            // ── RECIPES ──────────────────────────────────────────
            // Control Rod = 5 Steel + 3 Cobalt @ Assembler
            CreateRecipe("Recipe_ControlRod", "Control Rod Assembly", controlRod, 1,
                new[] { ("Item_SteelIngot", 5), ("Item_Cobalt", 3) },
                StationTier.Assembler, basePath + "/Recipes");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Nuclear] Created 6 items + recipes in " + basePath);
            EditorUtility.DisplayDialog("Nuclear Content Created",
                "Created:\n" +
                "• Enriched Fuel Rod (for big reactor)\n" +
                "• LEU Pellet (for portable reactor)\n" +
                "• Spent Fuel Rod (waste, reprocessable)\n" +
                "• Depleted Uranium (waste, reprocessable)\n" +
                "• High-Level Waste (final waste)\n" +
                "• Control Rod Assembly\n\n" +
                "Next steps:\n" +
                "1. Create prefabs for: ReactorCore, GasPipe, SteamTurbine,\n" +
                "   PortableReactor, UraniumProcessor, WasteReprocessor\n" +
                "2. Add BlockItem assets pointing to the prefabs\n" +
                "3. Add recipes to RecipeRegistry\n" +
                "4. Set the item references on each component\n" +
                "5. Add GasNetwork to your scene", "OK");
        }

        private static ItemDefinition CreateItem(string id, string displayName,
            string desc, Color tint, string category, string folder)
        {
            var item = ScriptableObject.CreateInstance<ResourceItem>();
            item.itemId = id.ToLower();
            item.displayName = displayName;
            item.description = desc;
            item.iconTint = tint;
            item.category = category;
            item.maxStack = 64;
            AssetDatabase.CreateAsset(item, $"{folder}/{id}.asset");
            return item;
        }

        private static void CreateRecipe(string id, string displayName,
            ItemDefinition output, int outputCount,
            (string itemId, int count)[] inputs, StationTier station, string folder)
        {
            var recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            recipe.displayName = displayName;
            recipe.outputItem = output;
            recipe.outputCount = outputCount;
            recipe.requiredStation = station;
            recipe.unlockedByDefault = false; // must research nuclear tech first
            // Note: input items must be linked manually in the inspector
            // since we can't easily find them by ID in a wizard.
            recipe.inputs = new RecipeIngredient[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
                recipe.inputs[i] = new RecipeIngredient { count = inputs[i].count };
            AssetDatabase.CreateAsset(recipe, $"{folder}/{id}.asset");
        }
    }
}
#endif
