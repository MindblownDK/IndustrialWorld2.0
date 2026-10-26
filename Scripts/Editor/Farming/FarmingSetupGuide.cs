// Assets/Scripts/Editor/Farming/FarmingSetupGuide.cs
//
// Editor-only setup utility invoked from Tools -> Voxel Engine -> Voxel Engine Setup.
// Non-destructively authors crops, seeds, raw/cooked foods, cooking recipes in
// RecipeRegistry.asset, 3D WildCrop scatter prefabs, and habitable biome scatter entries.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Biomes;
using VoxelEngine.Crafting;
using VoxelEngine.EditorTools;
using VoxelEngine.Items;

namespace VoxelEngine.Farming
{
    public static class FarmingSetupGuide
    {
        private const string AssetRoot = "Assets/VoxelEngineAssets";
        private const string BasePath = AssetRoot + "/Farming";
        private const string WildCropDir = BasePath + "/Prefabs/WildCrops";
        private const string WildMatDir = WildCropDir + "/Materials";
        private const string BiomeRoot = AssetRoot + "/Biomes";
        private const string RegistryPath = AssetRoot + "/RecipeRegistry.asset";

        public static void CreateAll()
        {
            EnsureFolder(BasePath + "/Crops");
            EnsureFolder(BasePath + "/Seeds");
            EnsureFolder(BasePath + "/Foods");
            EnsureFolder(BasePath + "/Recipes");
            EnsureFolder(WildMatDir);

            var registry = GetOrCreateRecipeRegistry();

            // ── WHEAT ──────────────────────────────────────────────
            var wheatFood = CreateFood("Food_Wheat", "Wheat",
                "Harvested golden grain stalks. Mill and bake into Bread or Corn Bread at a Furnace, or feed livestock.",
                5f, 0f, new Color(0.9f, 0.85f, 0.4f), BasePath + "/Foods");
            var wheatSeed = CreateSeed("Seed_Wheat", "Wheat Seeds",
                "Hardy cereal grain seeds. Plant on irrigated Tilled Soil or a Seed Planter to cultivate Wheat.",
                new Color(0.7f, 0.65f, 0.3f), BasePath + "/Seeds");
            var wheatCrop = CreateCrop("Crop_Wheat", "Wheat", 90f, wheatFood, 3, wheatSeed, 2, 10f,
                new[] { new Color(0.3f, 0.5f, 0.2f), new Color(0.5f, 0.6f, 0.25f),
                        new Color(0.7f, 0.75f, 0.3f), new Color(0.9f, 0.85f, 0.4f) },
                BasePath + "/Crops");
            LinkSeedToCrop(wheatSeed, wheatCrop);

            // ── CORN ───────────────────────────────────────────────
            var cornFood = CreateFood("Food_Corn", "Corn",
                "Sweet yellow maize ears harvested from tall stalks. Eat raw or bake with Wheat into Corn Bread.",
                8f, 0f, new Color(0.95f, 0.85f, 0.2f), BasePath + "/Foods");
            var cornSeed = CreateSeed("Seed_Corn", "Corn Seeds",
                "Dried maize kernels ready for sowing on Tilled Soil or automated Seed Planters.",
                new Color(0.8f, 0.7f, 0.15f), BasePath + "/Seeds");
            var cornCrop = CreateCrop("Crop_Corn", "Corn", 120f, cornFood, 2, cornSeed, 1, 12f,
                new[] { new Color(0.25f, 0.45f, 0.15f), new Color(0.4f, 0.55f, 0.2f),
                        new Color(0.6f, 0.7f, 0.25f), new Color(0.95f, 0.85f, 0.2f) },
                BasePath + "/Crops");
            LinkSeedToCrop(cornSeed, cornCrop);

            // ── CARROT ─────────────────────────────────────────────
            var carrotFood = CreateFood("Food_Carrot", "Carrot",
                "Crisp orange taproot rich in nutrients. Eat fresh or simmer into Vegetable Stew and Pumpkin Soup.",
                10f, 2f, new Color(0.95f, 0.5f, 0.1f), BasePath + "/Foods");
            var carrotSeed = CreateSeed("Seed_Carrot", "Carrot Seeds",
                "Fine root-crop seeds that thrive in hydrated soil beds and enclosed greenhouses.",
                new Color(0.6f, 0.4f, 0.15f), BasePath + "/Seeds");
            var carrotCrop = CreateCrop("Crop_Carrot", "Carrot", 80f, carrotFood, 4, carrotSeed, 2, 15f,
                new[] { new Color(0.2f, 0.5f, 0.15f), new Color(0.35f, 0.55f, 0.2f),
                        new Color(0.5f, 0.6f, 0.2f), new Color(0.3f, 0.65f, 0.2f) },
                BasePath + "/Crops");
            LinkSeedToCrop(carrotSeed, carrotCrop);

            // ── POTATO ─────────────────────────────────────────────
            var potatoFood = CreateFood("Food_Potato", "Potato",
                "Starchy earth tuber with high yield per plot. Roast over heat or cook into hearty Vegetable Stew.",
                12f, 0f, new Color(0.7f, 0.55f, 0.3f), BasePath + "/Foods");
            var potatoSeed = CreateSeed("Seed_Potato", "Potato Seed",
                "Sprouted seed-tuber eyes ready for planting in Tilled Soil or Seed Planters.",
                new Color(0.6f, 0.5f, 0.25f), BasePath + "/Seeds");
            var potatoCrop = CreateCrop("Crop_Potato", "Potato", 100f, potatoFood, 5, potatoSeed, 2, 18f,
                new[] { new Color(0.2f, 0.4f, 0.15f), new Color(0.3f, 0.45f, 0.18f),
                        new Color(0.4f, 0.5f, 0.2f), new Color(0.35f, 0.55f, 0.22f) },
                BasePath + "/Crops");
            LinkSeedToCrop(potatoSeed, potatoCrop);

            // ── BERRIES ────────────────────────────────────────────
            var berryFood = CreateFood("Food_Berries", "Berries",
                "Tart woodland berries gathered from wild bushes or cultivated hedges. Bake with Wheat into Berry Pie.",
                6f, 1f, new Color(0.6f, 0.15f, 0.3f), BasePath + "/Foods");
            var berrySeed = CreateSeed("Seed_Berry", "Berry Seeds",
                "Fast-growing bush seeds yielding clusters of sweet berries and surplus seeds.",
                new Color(0.5f, 0.2f, 0.3f), BasePath + "/Seeds");
            var berryCrop = CreateCrop("Crop_Berry", "Berry Bush", 70f, berryFood, 4, berrySeed, 3, 8f,
                new[] { new Color(0.15f, 0.4f, 0.15f), new Color(0.2f, 0.5f, 0.2f),
                        new Color(0.25f, 0.55f, 0.25f), new Color(0.6f, 0.15f, 0.3f) },
                BasePath + "/Crops");
            LinkSeedToCrop(berrySeed, berryCrop);

            // ── PUMPKIN ────────────────────────────────────────────
            var pumpkinFood = CreateFood("Food_Pumpkin", "Pumpkin",
                "Heavy ribbed autumn gourd packed with calories. Simmer with Carrot into restorative Pumpkin Soup.",
                20f, 5f, new Color(0.9f, 0.55f, 0.1f), BasePath + "/Foods");
            var pumpkinSeed = CreateSeed("Seed_Pumpkin", "Pumpkin Seeds",
                "Broad-leaf gourd seeds with longer maturation and substantial nutrition yield.",
                new Color(0.7f, 0.5f, 0.15f), BasePath + "/Seeds");
            var pumpkinCrop = CreateCrop("Crop_Pumpkin", "Pumpkin", 180f, pumpkinFood, 2, pumpkinSeed, 3, 25f,
                new[] { new Color(0.2f, 0.45f, 0.15f), new Color(0.35f, 0.55f, 0.2f),
                        new Color(0.5f, 0.6f, 0.15f), new Color(0.9f, 0.55f, 0.1f) },
                BasePath + "/Crops");
            LinkSeedToCrop(pumpkinSeed, pumpkinCrop);

            // ── COOKED FOODS (crafted at Furnace) ──────────────────
            var bread = CreateFood("Food_Bread", "Bread",
                "Crusty golden loaf baked from three bundles of Wheat. Reliable staple ration for field expeditions.",
                35f, 5f, new Color(0.85f, 0.7f, 0.35f), BasePath + "/Foods");
            var stew = CreateFood("Food_Stew", "Vegetable Stew",
                "Hot root-vegetable stew simmered from Carrots and Potato. Restores substantial hunger and health.",
                45f, 10f, new Color(0.6f, 0.4f, 0.2f), BasePath + "/Foods");
            var roastPotato = CreateFood("Food_RoastPotato", "Roast Potato",
                "Char-crisped roasted potatoes cooked in pairs for efficient everyday sustenance.",
                30f, 8f, new Color(0.75f, 0.6f, 0.3f), BasePath + "/Foods");
            var cornBread = CreateFood("Food_CornBread", "Corn Bread",
                "Dense golden skillet bread baked from Corn and Wheat.",
                40f, 5f, new Color(0.9f, 0.8f, 0.3f), BasePath + "/Foods");
            var berryPie = CreateFood("Food_BerryPie", "Berry Pie",
                "Flaky wheat-crust pastry filled with rich cooked berries. High hunger and health restoration.",
                50f, 15f, new Color(0.7f, 0.25f, 0.35f), BasePath + "/Foods");
            var pumpkinSoup = CreateFood("Food_PumpkinSoup", "Pumpkin Soup",
                "Velvety roasted Pumpkin and Carrot soup providing top-tier expedition nutrition.",
                55f, 12f, new Color(0.9f, 0.6f, 0.15f), BasePath + "/Foods");

            // ── COOKING RECIPES (automatically registered in RecipeRegistry.asset) ──
            CreateCookingRecipe(registry, "Cook_Bread", "Bread", bread, 1,
                new[] { (wheatFood as ItemDefinition, 3) },
                StationTier.Furnace, BasePath + "/Recipes");

            CreateCookingRecipe(registry, "Cook_Stew", "Vegetable Stew", stew, 1,
                new[] { (carrotFood as ItemDefinition, 2), (potatoFood as ItemDefinition, 1) },
                StationTier.Furnace, BasePath + "/Recipes");

            CreateCookingRecipe(registry, "Cook_RoastPotato", "Roast Potato", roastPotato, 2,
                new[] { (potatoFood as ItemDefinition, 2) },
                StationTier.Furnace, BasePath + "/Recipes");

            CreateCookingRecipe(registry, "Cook_CornBread", "Corn Bread", cornBread, 1,
                new[] { (cornFood as ItemDefinition, 2), (wheatFood as ItemDefinition, 1) },
                StationTier.Furnace, BasePath + "/Recipes");

            CreateCookingRecipe(registry, "Cook_BerryPie", "Berry Pie", berryPie, 1,
                new[] { (berryFood as ItemDefinition, 3), (wheatFood as ItemDefinition, 2) },
                StationTier.Furnace, BasePath + "/Recipes");

            CreateCookingRecipe(registry, "Cook_PumpkinSoup", "Pumpkin Soup", pumpkinSoup, 1,
                new[] { (pumpkinFood as ItemDefinition, 1), (carrotFood as ItemDefinition, 1) },
                StationTier.Furnace, BasePath + "/Recipes");

            EnsureAllCookingRecipesRegistered(registry);

            // ── WILD CROP SCATTER PREFABS & BIOME SCATTER WIRING ────
            var wildWheat = EnsureWildCropPrefab("WildPlant_Wheat", wheatCrop, "stalks",
                new Color(0.86f, 0.78f, 0.30f), new Color(0.95f, 0.86f, 0.42f), seedYield: 2, foodYield: 1, hp: 20);
            var wildCorn = EnsureWildCropPrefab("WildPlant_Corn", cornCrop, "corn",
                new Color(0.28f, 0.56f, 0.22f), new Color(0.96f, 0.84f, 0.20f), seedYield: 2, foodYield: 1, hp: 25);
            var wildCarrot = EnsureWildCropPrefab("WildPlant_Carrot", carrotCrop, "root",
                new Color(0.26f, 0.62f, 0.24f), new Color(0.95f, 0.48f, 0.12f), seedYield: 2, foodYield: 2, hp: 20);
            var wildPotato = EnsureWildCropPrefab("WildPlant_Potato", potatoCrop, "mound",
                new Color(0.28f, 0.54f, 0.22f), new Color(0.72f, 0.56f, 0.32f), seedYield: 2, foodYield: 2, hp: 20);
            var wildBerry = EnsureWildCropPrefab("WildBush_Berry", berryCrop, "bush",
                new Color(0.18f, 0.48f, 0.20f), new Color(0.76f, 0.16f, 0.30f), seedYield: 2, foodYield: 2, hp: 25);
            var wildPumpkin = EnsureWildCropPrefab("WildPlant_Pumpkin", pumpkinCrop, "gourd",
                new Color(0.24f, 0.52f, 0.20f), new Color(0.92f, 0.52f, 0.10f), seedYield: 2, foodYield: 1, hp: 30);

            int biomeLinksAdded = WireWildCropsToBiomes(
                wildWheat, wildCorn, wildCarrot, wildPotato, wildBerry, wildPumpkin);

            SyncCropIcons();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Farming] Verified 6 crops, 6 seeds, 12 foods, 6 cooking recipes in RecipeRegistry, and 6 wild crop scatter prefabs (+{biomeLinksAdded} new biome scatter links).");
        }

        public static void SyncCropIcons()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:CropDefinition", new[] { BasePath }))
            {
                var crop = AssetDatabase.LoadAssetAtPath<CropDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (crop == null || crop.harvestItem == null) continue;
                if (crop.icon == null && crop.harvestItem.icon != null)
                {
                    crop.icon = crop.harvestItem.icon;
                    EditorUtility.SetDirty(crop);
                }
            }
        }

        private static void LinkSeedToCrop(SeedItem seed, CropDefinition crop)
        {
            if (seed == null || crop == null || seed.crop == crop) return;
            seed.crop = crop;
            EditorUtility.SetDirty(seed);
        }

        private static RecipeRegistry GetOrCreateRecipeRegistry()
        {
            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(RegistryPath);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<RecipeRegistry>();
                AssetDatabase.CreateAsset(registry, RegistryPath);
            }
            if (registry.recipes == null)
            {
                registry.recipes = new List<RecipeDefinition>();
                EditorUtility.SetDirty(registry);
            }
            return registry;
        }

        private static void EnsureAllCookingRecipesRegistered(RecipeRegistry registry)
        {
            if (registry == null) return;
            registry.recipes ??= new List<RecipeDefinition>();
            bool dirty = registry.recipes.RemoveAll(r => r == null) > 0;
            foreach (string guid in AssetDatabase.FindAssets("t:CookingRecipe", new[] { BasePath }))
            {
                var cook = AssetDatabase.LoadAssetAtPath<CookingRecipe>(AssetDatabase.GUIDToAssetPath(guid));
                if (cook != null && !registry.recipes.Contains(cook))
                {
                    registry.recipes.Add(cook);
                    dirty = true;
                }
            }
            if (dirty) EditorUtility.SetDirty(registry);
        }

        private static GameObject EnsureWildCropPrefab(string prefabName, CropDefinition crop, string style,
            Color foliageColor, Color accentColor, int seedYield, int foodYield, int hp)
        {
            string path = $"{WildCropDir}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var wc = existing.GetComponent<WildCrop>();
                if (wc != null && wc.crop == null && crop != null)
                {
                    wc.crop = crop;
                    PrefabUtility.SavePrefabAsset(existing);
                }
                return existing;
            }
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                Debug.LogWarning($"[Farming] Preserved conflicting asset at '{path}'.");
                return null;
            }

            Material foliageMat = MakeColoredMat(WildMatDir, "Mat_" + prefabName + "_Foliage", foliageColor);
            Material accentMat = MakeColoredMat(WildMatDir, "Mat_" + prefabName + "_Accent", accentColor);

            var root = new GameObject(prefabName);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.45f, 0f);
            collider.size = new Vector3(0.90f, 0.90f, 0.90f);

            var wild = root.AddComponent<WildCrop>();
            wild.crop = crop;
            wild.seedYield = seedYield;
            wild.foodYield = foodYield;
            wild.hp = hp;

            void AddPart(string name, PrimitiveType primitive, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
            {
                var part = GameObject.CreatePrimitive(primitive);
                part.name = name;
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = pos;
                part.transform.localScale = scale;
                part.transform.localRotation = Quaternion.Euler(euler);
                UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
                if (mat != null) part.GetComponent<Renderer>().sharedMaterial = mat;
            }

            switch (style)
            {
                case "stalks": // Wheat: clustered golden stalks + plump grain heads
                    for (int i = 0; i < 6; i++)
                    {
                        float ang = i * 60f * Mathf.Deg2Rad;
                        float rx = Mathf.Cos(ang) * 0.16f;
                        float rz = Mathf.Sin(ang) * 0.16f;
                        float tilt = 6f + (i % 2) * 4f;
                        AddPart("Stalk" + i, PrimitiveType.Cylinder, new Vector3(rx, 0.36f, rz),
                            new Vector3(0.035f, 0.36f, 0.035f), new Vector3(Mathf.Sin(ang) * tilt, 0f, -Mathf.Cos(ang) * tilt), foliageMat);
                        AddPart("Ear" + i, PrimitiveType.Capsule, new Vector3(rx * 1.25f, 0.76f, rz * 1.25f),
                            new Vector3(0.075f, 0.14f, 0.075f), new Vector3(Mathf.Sin(ang) * tilt, 0f, -Mathf.Cos(ang) * tilt), accentMat);
                    }
                    break;

                case "corn": // Corn: tall leafy green stalks + bright yellow cobs
                    for (int i = 0; i < 3; i++)
                    {
                        float ox = (i - 1) * 0.18f;
                        float oz = (i % 2 == 0) ? 0.08f : -0.10f;
                        AddPart("Stalk" + i, PrimitiveType.Cylinder, new Vector3(ox, 0.58f, oz),
                            new Vector3(0.06f, 0.58f, 0.06f), new Vector3(0f, i * 35f, (i - 1) * 3f), foliageMat);
                        AddPart("Leaf" + i, PrimitiveType.Cube, new Vector3(ox + 0.14f, 0.52f, oz),
                            new Vector3(0.32f, 0.03f, 0.10f), new Vector3(0f, i * 55f, -28f), foliageMat);
                        AddPart("Cob" + i, PrimitiveType.Capsule, new Vector3(ox - 0.07f, 0.62f, oz + 0.05f),
                            new Vector3(0.09f, 0.14f, 0.09f), new Vector3(14f, i * 40f, 18f), accentMat);
                    }
                    break;

                case "root": // Carrot: visible orange taproot crowns + feathery green tops
                    for (int i = 0; i < 3; i++)
                    {
                        float ox = (i - 1) * 0.16f;
                        float oz = (i == 1) ? 0.12f : -0.08f;
                        AddPart("Crown" + i, PrimitiveType.Sphere, new Vector3(ox, 0.08f, oz),
                            new Vector3(0.14f, 0.18f, 0.14f), Vector3.zero, accentMat);
                        for (int f = 0; f < 3; f++)
                        {
                            float yaw = f * 120f + i * 25f;
                            AddPart($"Frond_{i}_{f}", PrimitiveType.Cube, new Vector3(ox, 0.30f, oz),
                                new Vector3(0.05f, 0.34f, 0.12f), new Vector3(18f, yaw, 0f), foliageMat);
                        }
                    }
                    break;

                case "mound": // Potato: leafy low bush + peeking tan tubers at base
                    AddPart("CanopyMain", PrimitiveType.Sphere, new Vector3(0f, 0.28f, 0f),
                        new Vector3(0.58f, 0.36f, 0.54f), Vector3.zero, foliageMat);
                    AddPart("CanopySide", PrimitiveType.Sphere, new Vector3(0.18f, 0.22f, 0.14f),
                        new Vector3(0.38f, 0.28f, 0.36f), Vector3.zero, foliageMat);
                    AddPart("TuberA", PrimitiveType.Sphere, new Vector3(-0.20f, 0.07f, 0.20f),
                        new Vector3(0.18f, 0.12f, 0.14f), new Vector3(0f, 24f, 8f), accentMat);
                    AddPart("TuberB", PrimitiveType.Sphere, new Vector3(0.22f, 0.06f, -0.16f),
                        new Vector3(0.16f, 0.11f, 0.13f), new Vector3(0f, -32f, -6f), accentMat);
                    break;

                case "bush": // Berry bush: lush rounded shrub + 6 crimson berry clusters
                    AddPart("BushCore", PrimitiveType.Sphere, new Vector3(0f, 0.38f, 0f),
                        new Vector3(0.74f, 0.56f, 0.72f), Vector3.zero, foliageMat);
                    AddPart("BushLobeA", PrimitiveType.Sphere, new Vector3(-0.20f, 0.30f, 0.16f),
                        new Vector3(0.48f, 0.42f, 0.48f), Vector3.zero, foliageMat);
                    AddPart("BushLobeB", PrimitiveType.Sphere, new Vector3(0.22f, 0.32f, -0.14f),
                        new Vector3(0.46f, 0.44f, 0.46f), Vector3.zero, foliageMat);
                    for (int i = 0; i < 6; i++)
                    {
                        float ang = i * 60f * Mathf.Deg2Rad;
                        AddPart("Berry" + i, PrimitiveType.Sphere,
                            new Vector3(Mathf.Cos(ang) * 0.31f, 0.36f + (i % 2) * 0.12f, Mathf.Sin(ang) * 0.31f),
                            new Vector3(0.11f, 0.11f, 0.11f), Vector3.zero, accentMat);
                    }
                    break;

                default: // Pumpkin gourd + green stem + broad vine leaves
                    AddPart("GourdCore", PrimitiveType.Sphere, new Vector3(0f, 0.24f, 0f),
                        new Vector3(0.56f, 0.42f, 0.56f), Vector3.zero, accentMat);
                    AddPart("GourdRibA", PrimitiveType.Sphere, new Vector3(0f, 0.24f, 0f),
                        new Vector3(0.62f, 0.40f, 0.42f), new Vector3(0f, 45f, 0f), accentMat);
                    AddPart("GourdRibB", PrimitiveType.Sphere, new Vector3(0f, 0.24f, 0f),
                        new Vector3(0.62f, 0.40f, 0.42f), new Vector3(0f, -45f, 0f), accentMat);
                    AddPart("Stem", PrimitiveType.Cylinder, new Vector3(0f, 0.48f, 0f),
                        new Vector3(0.06f, 0.08f, 0.06f), new Vector3(0f, 0f, 14f), foliageMat);
                    AddPart("VineLeafA", PrimitiveType.Cube, new Vector3(-0.34f, 0.08f, 0.18f),
                        new Vector3(0.32f, 0.04f, 0.28f), new Vector3(8f, 22f, -10f), foliageMat);
                    AddPart("VineLeafB", PrimitiveType.Cube, new Vector3(0.32f, 0.08f, -0.20f),
                        new Vector3(0.30f, 0.04f, 0.26f), new Vector3(-8f, -30f, 10f), foliageMat);
                    break;
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        private static int WireWildCropsToBiomes(
            GameObject wildWheat, GameObject wildCorn, GameObject wildCarrot,
            GameObject wildPotato, GameObject wildBerry, GameObject wildPumpkin)
        {
            var assignments = new (string biomeAsset, GameObject prefab, float density, float minScale, float maxScale)[]
            {
                ("Biome_Plains",      wildWheat,   0.0080f, 0.85f, 1.25f),
                ("Biome_Plains",      wildCorn,    0.0060f, 0.85f, 1.25f),
                ("Biome_Plains",      wildCarrot,  0.0060f, 0.85f, 1.20f),
                ("Biome_Plains",      wildPumpkin, 0.0035f, 0.85f, 1.25f),

                ("Biome_Steppes",     wildWheat,   0.0070f, 0.85f, 1.20f),
                ("Biome_Steppes",     wildCorn,    0.0050f, 0.85f, 1.20f),
                ("Biome_Steppes",     wildPotato,  0.0050f, 0.85f, 1.20f),

                ("Biome_Forest",      wildBerry,   0.0100f, 0.85f, 1.30f),
                ("Biome_Forest",      wildCarrot,  0.0050f, 0.85f, 1.20f),
                ("Biome_Forest",      wildPumpkin, 0.0040f, 0.85f, 1.25f),
                ("Biome_Forest",      wildPotato,  0.0040f, 0.85f, 1.20f),

                ("Biome_Taiga",       wildBerry,   0.0080f, 0.85f, 1.25f),
                ("Biome_Taiga",       wildPotato,  0.0050f, 0.85f, 1.15f),

                ("Biome_GreekMarble", wildWheat,   0.0060f, 0.85f, 1.20f),
                ("Biome_GreekMarble", wildBerry,   0.0060f, 0.85f, 1.25f),
            };

            int added = 0;
            foreach (var entry in assignments)
            {
                if (entry.prefab == null) continue;
                var biome = AssetDatabase.LoadAssetAtPath<BiomeDefinition>($"{BiomeRoot}/{entry.biomeAsset}.asset");
                if (biome == null) continue;
                if (CustomBiomeScatterSetup.EnsureEntry(
                        biome, BiomeScatterCategory.Environment,
                        entry.prefab, entry.density, entry.minScale, entry.maxScale))
                {
                    added++;
                }
            }
            return added;
        }

        private static Material MakeColoredMat(string folder, string name, Color c)
        {
            EnsureFolder(folder);
            string path = $"{folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) return null;

            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(sh) { name = name, color = c };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static FoodItem CreateFood(string fileName, string displayName, string description,
            float hunger, float health, Color tint, string folder)
        {
            string path = $"{folder}/{fileName}.asset";
            var food = AssetDatabase.LoadAssetAtPath<FoodItem>(path);
            bool created = food == null;
            if (created)
            {
                food = ScriptableObject.CreateInstance<FoodItem>();
                food.itemId = fileName.ToLowerInvariant();
                food.displayName = displayName;
                food.description = description;
                food.iconTint = tint;
                food.category = "Food";
                food.maxStack = 64;
                food.hungerRestore = hunger;
                food.healthRestore = health;
                AssetDatabase.CreateAsset(food, path);
                return food;
            }

            bool dirty = false;
            if (string.IsNullOrWhiteSpace(food.displayName)) { food.displayName = displayName; dirty = true; }
            if (string.IsNullOrWhiteSpace(food.description)) { food.description = description; dirty = true; }
            if (dirty) EditorUtility.SetDirty(food);
            return food;
        }

        private static SeedItem CreateSeed(string fileName, string displayName, string description,
            Color tint, string folder)
        {
            string path = $"{folder}/{fileName}.asset";
            var seed = AssetDatabase.LoadAssetAtPath<SeedItem>(path);
            bool created = seed == null;
            if (created)
            {
                seed = ScriptableObject.CreateInstance<SeedItem>();
                seed.itemId = fileName.ToLowerInvariant();
                seed.displayName = displayName;
                seed.description = description;
                seed.iconTint = tint;
                seed.category = "Farming";
                seed.maxStack = 64;
                AssetDatabase.CreateAsset(seed, path);
                return seed;
            }

            bool dirty = false;
            if (string.IsNullOrWhiteSpace(seed.displayName)) { seed.displayName = displayName; dirty = true; }
            if (string.IsNullOrWhiteSpace(seed.description)) { seed.description = description; dirty = true; }
            if (dirty) EditorUtility.SetDirty(seed);
            return seed;
        }

        private static CropDefinition CreateCrop(string fileName, string cropName, float growthTime,
            ItemDefinition harvestItem, int harvestAmt, ItemDefinition seedItem, int seedReturn, float foodValue,
            Color[] stageColors, string folder)
        {
            string path = $"{folder}/{fileName}.asset";
            var crop = AssetDatabase.LoadAssetAtPath<CropDefinition>(path);
            if (crop != null)
            {
                if (crop.icon == null && harvestItem != null && harvestItem.icon != null)
                {
                    crop.icon = harvestItem.icon;
                    EditorUtility.SetDirty(crop);
                }
                return crop;
            }

            crop = ScriptableObject.CreateInstance<CropDefinition>();
            crop.cropName = cropName;
            crop.icon = harvestItem != null ? harvestItem.icon : null;
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
            AssetDatabase.CreateAsset(crop, path);
            return crop;
        }

        private static void CreateCookingRecipe(RecipeRegistry registry, string fileName, string displayName,
            ItemDefinition output, int outputCount,
            (ItemDefinition item, int count)[] inputs,
            StationTier station, string folder)
        {
            string path = $"{folder}/{fileName}.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<CookingRecipe>(path);
            bool created = recipe == null;
            if (created)
            {
                recipe = ScriptableObject.CreateInstance<CookingRecipe>();
                AssetDatabase.CreateAsset(recipe, path);
            }
            if (created || string.IsNullOrWhiteSpace(recipe.displayName))
                recipe.displayName = displayName;
            if (created || recipe.outputItem == null)
                recipe.outputItem = output;
            if (created || recipe.outputCount <= 0)
                recipe.outputCount = outputCount;
            if (created)
                recipe.requiredStation = station;
            recipe.unlockedByDefault = false;
            if (created || recipe.inputs == null || recipe.inputs.Length == 0)
            {
                recipe.inputs = new RecipeIngredient[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                    recipe.inputs[i] = new RecipeIngredient { item = inputs[i].item, count = inputs[i].count };
                recipe.craftSeconds = CraftTimeDefaults.Suggest(station, recipe.inputs);
            }
            EditorUtility.SetDirty(recipe);

            if (registry != null)
            {
                registry.recipes ??= new List<RecipeDefinition>();
                if (!registry.recipes.Contains(recipe))
                {
                    registry.recipes.Add(recipe);
                    EditorUtility.SetDirty(registry);
                }
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(leaf))
                AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
