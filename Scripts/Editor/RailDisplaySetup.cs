#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 95 (12.1.0-dev): author the Steampunk Display Family and the Train Schedule.
    ///
    ///   Train Schedule            - grid block; owns a service pattern and drives the bogie.
    ///   Brass Display Screen      - grid block; shaft-fed, modular kind and source.
    ///   Display Cabinet           - stationary; mains-fed (a small electric engine inside).
    ///   Hanging Departure Board   - stationary; rods up, split-flap departures by default.
    ///   Nixie Readout             - stationary small; glowing digits in a brass cage.
    ///
    /// Every screen is the SAME component with different authored defaults - the modularity
    /// lives in the console, not in five parallel implementations.
    ///
    /// Non-destructive: missing assets are created, existing ones only have unresolvable
    /// links repaired (null prefab, null output item, missing registry membership).
    /// Authored tuning, craft times, health and icons are never reset. Safe to re-run.
    /// Logs with the [Setup 95] prefix.
    /// </summary>
    public static class RailDisplaySetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string BlocksFolder = Root + "/Blocks";
        private const string RecipesFolder = Root + "/Recipes";
        private const string PrefabsFolder = Root + "/StationPrefabs";
        private const string GridItemsFolder = Root + "/GridSystem/Items";
        private const string GridPrefabsFolder = Root + "/GridSystem/Prefabs";

        private static readonly Color Brass = new(0.72f, 0.51f, 0.22f);
        private static readonly Color DarkIron = new(0.13f, 0.12f, 0.11f);
        private static readonly Color Mahogany = new(0.28f, 0.16f, 0.10f);

        private const string SteelPath = Root + "/Items/Item_SteelIngot.asset";
        private const string WirePath = Root + "/Industrial/Items/Item_CopperWire.asset";

        public static void RunStep95()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Steampunk Displays", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Steampunk Displays",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.", "OK");
                    return;
                }

                var steel = AssetDatabase.LoadAssetAtPath<ItemDefinition>(SteelPath);
                var wire = AssetDatabase.LoadAssetAtPath<ItemDefinition>(WirePath);
                if (steel == null || wire == null)
                {
                    EditorUtility.DisplayDialog("Steampunk Displays",
                        "An ingredient is missing.\n\nRun the earlier crafting-content steps first.", "OK");
                    return;
                }

                bool any = false;

                any |= BuildScheduleBlock(steel, wire, registry);
                any |= BuildGridScreen(steel, wire, registry);
                any |= BuildCabinet(steel, wire, registry);
                any |= BuildHangingBoard(steel, wire, registry);
                any |= BuildNixie(steel, wire, registry);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[Setup 95] Steampunk display family complete. " +
                          (any ? "Changes were written." : "Everything was already in place."));

                EditorUtility.DisplayDialog("Step 95 - Steampunk Displays & Schedules",
                    "Authored:\n\n" +
                    "  TRAIN SCHEDULE        grid block  - steel x4 + wire x2\n" +
                    "  BRASS DISPLAY SCREEN  grid block  - steel x6 + wire x4 (shaft-fed)\n" +
                    "  DISPLAY CABINET       stationary  - steel x8 + wire x4 (15 W)\n" +
                    "  HANGING DEPARTURE BD  stationary  - steel x10 + wire x6 (25 W)\n" +
                    "  NIXIE READOUT         stationary  - steel x3 + wire x2 (8 W)\n\n" +
                    "Every screen opens a console on E: kind (split-flap / nixie /\n" +
                    "analog), source (speed, load, departures, station, custom).\n" +
                    "Grid screens live while any shaft on the grid turns;\n" +
                    "stationary ones draw mains through a small electric engine.\n\n" +
                    (any ? "Changes were written. See the Console." : "Everything was already in place."),
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 95] Aborted: " + ex);
                EditorUtility.DisplayDialog("Steampunk Displays",
                    "Setup stopped: " + ex.Message + "\n\nNothing was written.", "OK");
            }
        }

        // ============================================================
        //  Train Schedule - grid block
        // ============================================================
        private static bool BuildScheduleBlock(ItemDefinition steel, ItemDefinition wire, RecipeRegistry registry)
        {
            const string path = GridPrefabsFolder + "/TrainSchedule.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool changed = false;

            if (prefab == null)
            {
                EnsureFolder(GridPrefabsFolder);
                var root = new GameObject("TrainSchedule");

                Cube(root, "Case", new Vector3(0f, 0f, 0f), new Vector3(0.70f, 0.50f, 0.30f),
                    Mat("Mat_ScheduleBrass", Brass));
                var face = Cube(root, "ClockFace", new Vector3(0f, 0.10f, -0.16f), new Vector3(0.30f, 0.30f, 0.02f),
                    Mat("Mat_ScheduleFace", new Color(0.90f, 0.86f, 0.74f)));
                face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var lever = Cube(root, "Lever", new Vector3(0.28f, 0.30f, 0f), new Vector3(0.05f, 0.22f, 0.05f),
                    Mat("Mat_ScheduleIron", DarkIron));
                lever.transform.localRotation = Quaternion.Euler(0f, 0f, -20f);

                var block = root.AddComponent<VoxelEngine.GridSystem.GridTrainScheduleBlock>();
                block.blockName = "Train Schedule";
                block.BlockMass = 40f;
                block.maxHP = 150f;

                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 95] Created " + path + ".");
            }
            else
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                bool repaired = false;
                if (contents.GetComponent<VoxelEngine.GridSystem.GridTrainScheduleBlock>() == null)
                {
                    var b = contents.AddComponent<VoxelEngine.GridSystem.GridTrainScheduleBlock>();
                    b.blockName = "Train Schedule";
                    repaired = true;
                }
                if (repaired) prefab = PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
                changed = repaired;
            }

            changed |= EnsureGridItem("GItem_TrainSchedule", "trainschedule", "Train Schedule",
                "A conductor's desk for a train. Build it onto a grid with a Rail Truck and " +
                "open it with E to write the service: an ordered list of stations, each with " +
                "the condition that releases the train again - dwell time, hold full, hold " +
                "empty, or hold with space left. The train routes itself between stops.",
                prefab, Brass, 40f, 150f);

            changed |= EnsureRecipe(registry, "Recipe_TrainSchedule", "Train Schedule",
                steel, 4, wire, 2, StationTier.CraftingBench, 3f,
                AssetDatabase.LoadAssetAtPath<GridBlockItem>(GridItemsFolder + "/GItem_TrainSchedule.asset"));

            return changed;
        }

        // ============================================================
        //  Brass Display Screen - grid block, shaft-fed
        // ============================================================
        private static bool BuildGridScreen(ItemDefinition steel, ItemDefinition wire, RecipeRegistry registry)
        {
            const string path = GridPrefabsFolder + "/BrassDisplayScreen.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool changed = false;

            if (prefab == null)
            {
                EnsureFolder(GridPrefabsFolder);
                var root = new GameObject("BrassDisplayScreen");

                Cube(root, "Housing", new Vector3(0f, 0f, 0.05f), new Vector3(1.05f, 0.75f, 0.10f),
                    Mat("Mat_ScreenBrass", Brass));
                var collar = Cube(root, "ShaftCollar", new Vector3(0.58f, 0f, 0.05f), new Vector3(0.12f, 0.12f, 0.16f),
                    Mat("Mat_ScreenIron", DarkIron));
                collar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

                var screen = root.AddComponent<RailDisplayScreen>();
                screen.kind = ScreenKind.SplitFlap;
                screen.source = ScreenSource.TrainSpeed;
                screen.rows = 2;

                var block = root.AddComponent<VoxelEngine.GridSystem.GridBlock>();
                block.blockName = "Brass Display Screen";
                block.BlockMass = 30f;
                block.maxHP = 120f;

                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 95] Created " + path + ".");
            }
            else
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                bool repaired = false;
                if (contents.GetComponent<RailDisplayScreen>() == null)
                {
                    contents.AddComponent<RailDisplayScreen>();
                    repaired = true;
                }
                if (repaired) prefab = PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
                changed = repaired;
            }

            changed |= EnsureGridItem("GItem_BrassDisplayScreen", "brassdisplayscreen", "Brass Display Screen",
                "A split-flap board for the train itself. Takes ROTATIONAL power: while any " +
                "shaft, gearbox or engine on the grid turns above idle the drums keep " +
                "clicking; park the engine and the board goes dark mid-word. Open with E to " +
                "pick kind and source.",
                prefab, Brass, 30f, 120f);

            changed |= EnsureRecipe(registry, "Recipe_BrassDisplayScreen", "Brass Display Screen",
                steel, 6, wire, 4, StationTier.CraftingBench, 4f,
                AssetDatabase.LoadAssetAtPath<GridBlockItem>(GridItemsFolder + "/GItem_BrassDisplayScreen.asset"));

            return changed;
        }

        // ============================================================
        //  Stationary housings
        // ============================================================
        private static bool BuildCabinet(ItemDefinition steel, ItemDefinition wire, RecipeRegistry registry)
        {
            var prefab = EnsureStationaryPrefab("DisplayCabinet", ScreenKind.SplitFlap,
                ScreenSource.Departures, 4, 15f, build =>
                {
                    Cube(build, "Cabinet", new Vector3(0f, 0.60f, 0f), new Vector3(0.95f, 1.20f, 0.30f),
                        Mat("Mat_CabinetWood", Mahogany));
                    Cube(build, "Trim", new Vector3(0f, 1.22f, 0f), new Vector3(1.02f, 0.06f, 0.34f),
                        Mat("Mat_CabinetBrass", Brass));
                });

            bool changed = false;
            changed |= EnsureBlockItem("DisplayCabinet", "Display Cabinet",
                "A station display cabinet on a mahogany body with brass trim. Draws mains " +
                "power - a small electric engine inside turns the flap drums - and shows " +
                "whatever its console is set to: departures by default. Open with E.",
                prefab, Brass, 25f, 160, registry, "Recipe_DisplayCabinet", steel, 8, wire, 4,
                StationTier.Assembler, 8f);
            return changed;
        }

        private static bool BuildHangingBoard(ItemDefinition steel, ItemDefinition wire, RecipeRegistry registry)
        {
            var prefab = EnsureStationaryPrefab("HangingDepartureBoard", ScreenKind.SplitFlap,
                ScreenSource.Departures, 4, 25f, build =>
                {
                    Cube(build, "Board", new Vector3(0f, 0f, 0f), new Vector3(1.70f, 0.85f, 0.16f),
                        Mat("Mat_BoardIron", DarkIron));
                    for (int i = 0; i < 2; i++)
                    {
                        var rod = Cube(build, "HangRod" + i, new Vector3(i == 0 ? -0.65f : 0.65f, 0.75f, 0f),
                            new Vector3(0.04f, 1.10f, 0.04f), Mat("Mat_BoardBrass", Brass));
                        rod.transform.localRotation = Quaternion.identity;
                    }
                });

            bool changed = false;
            changed |= EnsureBlockItem("HangingDepartureBoard", "Hanging Departure Board",
                "A departure board hung from its rods under a platform canopy. Split-flap " +
                "rows list every scheduled service calling at the station beside it, with " +
                "the iconic clack-slap as cards turn. Mains-fed; 25 W of small electric " +
                "engine. Open with E.",
                prefab, Brass, 35f, 160, registry, "Recipe_HangingDepartureBoard", steel, 10, wire, 6,
                StationTier.Assembler, 10f);
            return changed;
        }

        private static bool BuildNixie(ItemDefinition steel, ItemDefinition wire, RecipeRegistry registry)
        {
            var prefab = EnsureStationaryPrefab("NixieReadout", ScreenKind.Nixie,
                ScreenSource.StationStatus, 1, 8f, build =>
                {
                    Cube(build, "Case", new Vector3(0f, 0f, 0f), new Vector3(0.55f, 0.38f, 0.16f),
                        Mat("Mat_NixieBrass", Brass));
                });

            bool changed = false;
            changed |= EnsureBlockItem("NixieReadout", "Nixie Readout",
                "A small readout of glowing gas-discharge digits behind a brass cage. " +
                "Quiet, warm and a little dangerous-looking, exactly as a nixie should be. " +
                "Mains-fed at 8 W. Open with E to point it at something else.",
                prefab, Brass, 8f, 90, registry, "Recipe_NixieReadout", steel, 3, wire, 2,
                StationTier.CraftingBench, 3f);
            return changed;
        }

        private static GameObject EnsureStationaryPrefab(string asset, ScreenKind kind,
            ScreenSource source, int rows, float watts, System.Action<GameObject> decorate)
        {
            string path = PrefabsFolder + "/" + asset + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                bool repaired = false;
                if (contents.GetComponent<RailDisplayScreen>() == null)
                {
                    contents.AddComponent<RailDisplayScreen>();
                    repaired = true;
                }
                if (contents.GetComponent<VoxelEngine.Power.PowerConsumer>() == null)
                {
                    var restored = contents.AddComponent<VoxelEngine.Power.PowerConsumer>();
                    restored.wattsPerSecond = watts;
                    repaired = true;
                }
                GameObject result = existing;
                if (repaired) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
                return result;
            }

            EnsureFolder(PrefabsFolder);
            var root = new GameObject(asset);
            decorate?.Invoke(root);

            var screen = root.AddComponent<RailDisplayScreen>();
            screen.kind = kind;
            screen.source = source;
            screen.rows = rows;

            var consumer = root.AddComponent<VoxelEngine.Power.PowerConsumer>();
            consumer.wattsPerSecond = watts;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log("[Setup 95] Created " + path + ".");
            return prefab;
        }

        // ============================================================
        //  Shared authoring
        // ============================================================
        private static GameObject Cube(GameObject parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            return go;
        }

        private static bool EnsureGridItem(string asset, string id, string display, string description,
            GameObject prefab, Color tint, float mass, float hp)
        {
            string path = GridItemsFolder + "/" + asset + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(path);
            bool created = false;
            if (item == null)
            {
                EnsureFolder(GridItemsFolder);
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                created = true;
            }
            bool dirty = created;

            if (item.itemId != id) { item.itemId = id; dirty = true; }
            if (item.displayName != display) { item.displayName = display; dirty = true; }
            if (item.maxStack <= 0) { item.maxStack = 20; dirty = true; }
            if (item.massPerUnit <= 0f) { item.massPerUnit = mass; dirty = true; }
            if (item.category != "Grid Blocks") { item.category = "Grid Blocks"; dirty = true; }
            if (string.IsNullOrEmpty(item.description)) { item.description = description; dirty = true; }
            if (item.icon == null) item.iconTint = tint;
            if (created) { item.blockMass = mass; item.blockHP = hp; }
            if (item.blockPrefab == null || item.blockPrefab != prefab) { item.blockPrefab = prefab; dirty = true; }

            if (dirty)
            {
                if (!AssetDatabase.Contains(item)) AssetDatabase.CreateAsset(item, path);
                EditorUtility.SetDirty(item);
                Debug.Log("[Setup 95] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }
            return dirty;
        }

        private static bool EnsureBlockItem(string asset, string display, string description,
            GameObject prefab, Color tint, float mass, int health, RecipeRegistry registry,
            string recipeStem, ItemDefinition steel, int steelCount, ItemDefinition wire, int wireCount,
            StationTier tier, float seconds)
        {
            string path = BlocksFolder + "/Block_" + asset + ".asset";
            var b = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool created = false;
            if (b == null)
            {
                EnsureFolder(BlocksFolder);
                b = ScriptableObject.CreateInstance<BlockItem>();
                created = true;
            }
            bool dirty = created;

            string id = asset.ToLowerInvariant();
            if (b.itemId != id) { b.itemId = id; dirty = true; }
            if (b.displayName != display) { b.displayName = display; dirty = true; }
            if (b.maxStack <= 0) { b.maxStack = 99; dirty = true; }
            if (b.massPerUnit <= 0f) { b.massPerUnit = mass; dirty = true; }
            if (b.blockHealth <= 0) { b.blockHealth = health; dirty = true; }
            if (b.miningTier <= 0) { b.miningTier = 1; dirty = true; }
            if (b.category != "Rail") { b.category = "Rail"; dirty = true; }
            if (string.IsNullOrEmpty(b.description)) { b.description = description; dirty = true; }
            if (b.icon == null) b.iconTint = tint;
            if (b.placedPrefab == null || b.placedPrefab != prefab) { b.placedPrefab = prefab; dirty = true; }

            if (dirty)
            {
                if (!AssetDatabase.Contains(b)) AssetDatabase.CreateAsset(b, path);
                EditorUtility.SetDirty(b);
                Debug.Log("[Setup 95] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            var recipe = FindRecipe(recipeStem);
            bool recipeChanged = false;
            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = display;
                recipe.requiredStation = tier;
                recipe.craftSeconds = seconds;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = b;
                recipe.inputs = new[]
                {
                    new RecipeIngredient { item = steel, count = steelCount },
                    new RecipeIngredient { item = wire, count = wireCount },
                };
                AssetDatabase.CreateAsset(recipe, RecipesFolder + "/" + recipeStem + ".asset");
                EditorUtility.SetDirty(recipe);
                recipeChanged = true;
            }
            else if (recipe.outputItem == null)
            {
                recipe.outputItem = b;
                EditorUtility.SetDirty(recipe);
                recipeChanged = true;
            }
            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                recipeChanged = true;
            }
            return dirty || recipeChanged;
        }

        private static bool EnsureRecipe(RecipeRegistry registry, string stem, string display,
            ItemDefinition steel, int steelCount, ItemDefinition wire, int wireCount,
            StationTier tier, float seconds, ItemDefinition output)
        {
            var recipe = FindRecipe(stem);
            bool changed = false;
            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = display;
                recipe.requiredStation = tier;
                recipe.craftSeconds = seconds;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = output;
                recipe.inputs = new[]
                {
                    new RecipeIngredient { item = steel, count = steelCount },
                    new RecipeIngredient { item = wire, count = wireCount },
                };
                AssetDatabase.CreateAsset(recipe, RecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
            }
            else if (recipe.outputItem == null && output != null)
            {
                recipe.outputItem = output;
                EditorUtility.SetDirty(recipe);
                changed = true;
            }
            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                changed = true;
            }
            return changed;
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

        private static Material Mat(string name, Color c)
        {
            string path = PrefabsFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) return null;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(sh) { name = name, color = c };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
#endif
