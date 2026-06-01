// Assets/Scripts/VoxelEngine/Farming/FarmingSetupGuide.cs
//
// Editor-only wizard that creates a complete farming content set in one click:
// crops, seeds, foods, cooking recipes, and smelting recipes.
//
// Menu: Tools > Voxel Engine > Create Farming Content

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.Farming
{
    public static class FarmingSetupGuide
    {
        [MenuItem("Tools/Voxel Engine/Create Farming Content")]
        public static void CreateAll()
        {
            string basePath = "Assets/VoxelEngineAssets/Farming";
            if (!AssetDatabase.IsValidFolder(basePath))
            {
                AssetDatabase.CreateFolder("Assets/VoxelEngineAssets", "Farming");
            }
            if (!AssetDatabase.IsValidFolder(basePath + "/Crops"))
                AssetDatabase.CreateFolder(basePath, "Crops");
            if (!AssetDatabase.IsValidFolder(basePath + "/Seeds"))
                AssetDatabase.CreateFolder(basePath, "Seeds");
            if (!AssetDatabase.IsValidFolder(basePath + "/Foods"))
                AssetDatabase.CreateFolder(basePath, "Foods");
            if (!AssetDatabase.IsValidFolder(basePath + "/Recipes"))
                AssetDatabase.CreateFolder(basePath, "Recipes");

            // ── WHEAT ──────────────────────────────────────────────
            var wheatFood = CreateFood("Food_Wheat", "Wheat", 5f, 0f,
                new Color(0.9f, 0.85f, 0.4f), basePath + "/Foods");
            var wheatSeed = CreateSeed("Seed_Wheat", "Wheat Seeds",
                new Color(0.7f, 0.65f, 0.3f), basePath + "/Seeds");
            var wheatCrop = CreateCrop("Crop_Wheat", "Wheat", 90f, wheatFood, 3, wheatSeed, 2, 10f,
                new[] { new Color(0.3f, 0.5f, 0.2f), new Color(0.5f, 0.6f, 0.25f),
                        new Color(0.7f, 0.75f, 0.3f), new Color(0.9f, 0.85f, 0.4f) },
                basePath + "/Crops");
            wheatSeed.crop = wheatCrop;
            EditorUtility.SetDirty(wheatSeed);

            // ── CORN ───────────────────────────────────────────────
            var cornFood = CreateFood("Food_Corn", "Corn", 8f, 0f,
                new Color(0.95f, 0.85f, 0.2f), basePath + "/Foods");
            var cornSeed = CreateSeed("Seed_Corn", "Corn Seeds",
                new Color(0.8f, 0.7f, 0.15f), basePath + "/Seeds");
            var cornCrop = CreateCrop("Crop_Corn", "Corn", 120f, cornFood, 2, cornSeed, 1, 12f,
                new[] { new Color(0.25f, 0.45f, 0.15f), new Color(0.4f, 0.55f, 0.2f),
                        new Color(0.6f, 0.7f, 0.25f), new Color(0.95f, 0.85f, 0.2f) },
                basePath + "/Crops");
            cornSeed.crop = cornCrop;
            EditorUtility.SetDirty(cornSeed);

            // ── CARROT ─────────────────────────────────────────────
            var carrotFood = CreateFood("Food_Carrot", "Carrot", 10f, 2f,
                new Color(0.95f, 0.5f, 0.1f), basePath + "/Foods");
            var carrotSeed = CreateSeed("Seed_Carrot", "Carrot Seeds",
                new Color(0.6f, 0.4f, 0.15f), basePath + "/Seeds");
            var carrotCrop = CreateCrop("Crop_Carrot", "Carrot", 80f, carrotFood, 4, carrotSeed, 2, 15f,
                new[] { new Color(0.2f, 0.5f, 0.15f), new Color(0.35f, 0.55f, 0.2f),
                        new Color(0.5f, 0.6f, 0.2f), new Color(0.3f, 0.65f, 0.2f) },
                basePath + "/Crops");
            carrotSeed.crop = carrotCrop;
            EditorUtility.SetDirty(carrotSeed);

            // ── POTATO ─────────────────────────────────────────────
            var potatoFood = CreateFood("Food_Potato", "Potato", 12f, 0f,
                new Color(0.7f, 0.55f, 0.3f), basePath + "/Foods");
            var potatoSeed = CreateSeed("Seed_Potato", "Potato Seed",
                new Color(0.6f, 0.5f, 0.25f), basePath + "/Seeds");
            var potatoCrop = CreateCrop("Crop_Potato", "Potato", 100f, potatoFood, 5, potatoSeed, 2, 18f,
                new[] { new Color(0.2f, 0.4f, 0.15f), new Color(0.3f, 0.45f, 0.18f),
                        new Color(0.4f, 0.5f, 0.2f), new Color(0.35f, 0.55f, 0.22f) },
                basePath + "/Crops");
            potatoSeed.crop = potatoCrop;
            EditorUtility.SetDirty(potatoSeed);

            // ── BERRIES ────────────────────────────────────────────
            var berryFood = CreateFood("Food_Berries", "Berries", 6f, 1f,
                new Color(0.6f, 0.15f, 0.3f), basePath + "/Foods");
            var berrySeed = CreateSeed("Seed_Berry", "Berry Seeds",
                new Color(0.5f, 0.2f, 0.3f), basePath + "/Seeds");
            var berryCrop = CreateCrop("Crop_Berry", "Berry Bush", 70f, berryFood, 4, berrySeed, 3, 8f,
                new[] { new Color(0.15f, 0.4f, 0.15f), new Color(0.2f, 0.5f, 0.2f),
                        new Color(0.25f, 0.55f, 0.25f), new Color(0.6f, 0.15f, 0.3f) },
                basePath + "/Crops");
            berrySeed.crop = berryCrop;
            EditorUtility.SetDirty(berrySeed);

            // ── PUMPKIN ────────────────────────────────────────────
            var pumpkinFood = CreateFood("Food_Pumpkin", "Pumpkin", 20f, 5f,
                new Color(0.9f, 0.55f, 0.1f), basePath + "/Foods");
            var pumpkinSeed = CreateSeed("Seed_Pumpkin", "Pumpkin Seeds",
                new Color(0.7f, 0.5f, 0.15f), basePath + "/Seeds");
            var pumpkinCrop = CreateCrop("Crop_Pumpkin", "Pumpkin", 180f, pumpkinFood, 2, pumpkinSeed, 3, 25f,
                new[] { new Color(0.2f, 0.45f, 0.15f), new Color(0.35f, 0.55f, 0.2f),
                        new Color(0.5f, 0.6f, 0.15f), new Color(0.9f, 0.55f, 0.1f) },
                basePath + "/Crops");
            pumpkinSeed.crop = pumpkinCrop;
            EditorUtility.SetDirty(pumpkinSeed);

            // ── COOKED FOODS (crafted at furnace) ──────────────────
            var bread = CreateFood("Food_Bread", "Bread", 35f, 5f,
                new Color(0.85f, 0.7f, 0.35f), basePath + "/Foods");
            var stew = CreateFood("Food_Stew", "Vegetable Stew", 45f, 10f,
                new Color(0.6f, 0.4f, 0.2f), basePath + "/Foods");
            var roastPotato = CreateFood("Food_RoastPotato", "Roast Potato", 30f, 8f,
                new Color(0.75f, 0.6f, 0.3f), basePath + "/Foods");
            var cornBread = CreateFood("Food_CornBread", "Corn Bread", 40f, 5f,
                new Color(0.9f, 0.8f, 0.3f), basePath + "/Foods");
            var berryPie = CreateFood("Food_BerryPie", "Berry Pie", 50f, 15f,
                new Color(0.7f, 0.25f, 0.35f), basePath + "/Foods");
            var pumpkinSoup = CreateFood("Food_PumpkinSoup", "Pumpkin Soup", 55f, 12f,
                new Color(0.9f, 0.6f, 0.15f), basePath + "/Foods");

            // ── CRAFTING RECIPES ────────────────────────────────────
            // Bread = 3 wheat @ Furnace
            CreateCookingRecipe("Cook_Bread", "Bread", bread, 1,
                new[] { (wheatFood as ItemDefinition, 3) },
                StationTier.Furnace, basePath + "/Recipes");

            // Vegetable Stew = 2 carrot + 1 potato @ Furnace
            CreateCookingRecipe("Cook_Stew", "Vegetable Stew", stew, 1,
                new[] { (carrotFood as ItemDefinition, 2), (potatoFood as ItemDefinition, 1) },
                StationTier.Furnace, basePath + "/Recipes");

            // Roast Potato = 2 potato @ Furnace
            CreateCookingRecipe("Cook_RoastPotato", "Roast Potato", roastPotato, 2,
                new[] { (potatoFood as ItemDefinition, 2) },
                StationTier.Furnace, basePath + "/Recipes");

            // Corn Bread = 2 corn + 1 wheat @ Furnace
            CreateCookingRecipe("Cook_CornBread", "Corn Bread", cornBread, 1,
                new[] { (cornFood as ItemDefinition, 2), (wheatFood as ItemDefinition, 1) },
                StationTier.Furnace, basePath + "/Recipes");

            // Berry Pie = 3 berries + 2 wheat @ Furnace
            CreateCookingRecipe("Cook_BerryPie", "Berry Pie", berryPie, 1,
                new[] { (berryFood as ItemDefinition, 3), (wheatFood as ItemDefinition, 2) },
                StationTier.Furnace, basePath + "/Recipes");

            // Pumpkin Soup = 1 pumpkin + 1 carrot @ Furnace
            CreateCookingRecipe("Cook_PumpkinSoup", "Pumpkin Soup", pumpkinSoup, 1,
                new[] { (pumpkinFood as ItemDefinition, 1), (carrotFood as ItemDefinition, 1) },
                StationTier.Furnace, basePath + "/Recipes");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Farming] Created 6 crops, 6 seeds, 12 foods, 6 cooking recipes in " + basePath);
            EditorUtility.DisplayDialog("Farming Content Created",
                "Created:\n• 6 Crops (Wheat, Corn, Carrot, Potato, Berry, Pumpkin)\n" +
                "• 6 Seed Items\n• 6 Raw Foods + 6 Cooked Foods\n" +
                "• 6 Cooking Recipes (Bread, Stew, Roast Potato, Corn Bread, Berry Pie, Pumpkin Soup)\n\n" +
                "Add the cooking recipes to your RecipeRegistry asset.\n" +
                "Add wild crop prefabs to biome scatter lists.", "OK");
        }

        private static FoodItem CreateFood(string fileName, string displayName, float hunger, float health,
            Color tint, string folder)
        {
            var food = ScriptableObject.CreateInstance<FoodItem>();
            food.itemId = fileName.ToLower();
            food.displayName = displayName;
            food.iconTint = tint;
            food.category = "Food";
            food.maxStack = 64;
            food.hungerRestore = hunger;
            food.healthRestore = health;
            AssetDatabase.CreateAsset(food, $"{folder}/{fileName}.asset");
            return food;
        }

        private static SeedItem CreateSeed(string fileName, string displayName, Color tint, string folder)
        {
            var seed = ScriptableObject.CreateInstance<SeedItem>();
            seed.itemId = fileName.ToLower();
            seed.displayName = displayName;
            seed.iconTint = tint;
            seed.category = "Farming";
            seed.maxStack = 64;
            AssetDatabase.CreateAsset(seed, $"{folder}/{fileName}.asset");
            return seed;
        }

        private static CropDefinition CreateCrop(string fileName, string cropName, float growthTime,
            ItemDefinition harvestItem, int harvestAmt, ItemDefinition seedItem, int seedReturn, float foodValue,
            Color[] stageColors, string folder)
        {
            var crop = ScriptableObject.CreateInstance<CropDefinition>();
            crop.cropName = cropName;
            crop.growthTime = growthTime;
            crop.growthStages = 4;
            crop.requiresWater = true;
            crop.irrigatedSpeedMultiplier = 2f;
            crop.droughtToleranceSec = 30f;
            crop.harvestItem = harvestItem;
            crop.harvestAmount = harvestAmt;
            crop.seedItem = seedItem;
            crop.seedReturnAmount = seedReturn;
            crop.foodValue = foodValue;
            crop.stageColors = stageColors;
            crop.stageScales = new[] { 0.15f, 0.35f, 0.65f, 1.0f };
            AssetDatabase.CreateAsset(crop, $"{folder}/{fileName}.asset");
            return crop;
        }

        private static void CreateCookingRecipe(string fileName, string displayName,
            ItemDefinition output, int outputCount,
            (ItemDefinition item, int count)[] inputs,
            StationTier station, string folder)
        {
            var recipe = ScriptableObject.CreateInstance<CookingRecipe>();
            recipe.displayName = displayName;
            recipe.outputItem = output;
            recipe.outputCount = outputCount;
            recipe.requiredStation = station;
            recipe.craftSeconds = 0f; // instant at station
            recipe.unlockedByDefault = true;
            recipe.inputs = new RecipeIngredient[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
                recipe.inputs[i] = new RecipeIngredient { item = inputs[i].item, count = inputs[i].count };
            AssetDatabase.CreateAsset(recipe, $"{folder}/{fileName}.asset");
        }
    }
}
#endif
