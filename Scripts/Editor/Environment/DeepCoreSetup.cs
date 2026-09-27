#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 86 (11.18.0-dev): author the Deep Core programme.
    ///
    ///   Deep Survey Scanner  - carried tool. While it is in the inventory, the survey
    ///                          readout shows the nearest deep deposit and its distance.
    ///   Deep Core Extractor  - placed machine. The only way to tap a deep node.
    ///
    /// Non-destructive: missing assets are created, existing ones only have unresolvable
    /// links repaired. Authored tuning, craft times and icons are never reset. Safe to
    /// re-run. Logs with the [Setup 86] prefix.
    /// </summary>
    public static class DeepCoreSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string ItemsFolder = Root + "/Items";
        private const string BlocksFolder = Root + "/Blocks";
        private const string RecipesFolder = Root + "/Recipes";
        private const string PrefabsFolder = Root + "/StationPrefabs";

        private static readonly Color ScannerTint = new(0.38f, 0.72f, 0.86f);
        private static readonly Color ExtractorTint = new(0.52f, 0.46f, 0.38f);

        private const string SteelPath = Root + "/Items/Item_SteelIngot.asset";
        private const string WirePath = Root + "/Industrial/Items/Item_CopperWire.asset";
        private const string CircuitPath = Root + "/Industrial/Items/Item_Circuit.asset";

        public static void RunStep86()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Deep Core", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Deep Core",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.", "OK");
                    return;
                }

                var steel = AssetDatabase.LoadAssetAtPath<ItemDefinition>(SteelPath);
                var wire = AssetDatabase.LoadAssetAtPath<ItemDefinition>(WirePath);
                var circuit = AssetDatabase.LoadAssetAtPath<ItemDefinition>(CircuitPath);

                if (steel == null || wire == null)
                {
                    Debug.LogError("[Setup 86] An ingredient did not resolve (steel: " + (steel != null) +
                                   ", copper wire: " + (wire != null) + ").");
                    EditorUtility.DisplayDialog("Deep Core",
                        "An ingredient is missing.\n\nRun the earlier crafting-content steps first.", "OK");
                    return;
                }

                bool any = false;
                any |= BuildScanner(steel, wire, circuit, registry);
                any |= BuildExtractor(steel, wire, circuit, registry);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[Setup 86] Deep Core setup complete. " +
                          (any ? "Changes were written." : "Everything was already in place."));

                EditorUtility.DisplayDialog("Step 86 - Deep Core",
                    "Deep Core programme authored.\n\n" +
                    "  DEEP SURVEY SCANNER   Steel x8 + Wire x12" +
                    (circuit != null ? " + Circuit x2" : "") + "\n" +
                    "    Carry it and a readout shows the nearest deep deposit\n" +
                    "    and how far away it is. Walk until the number drops.\n\n" +
                    "  DEEP CORE EXTRACTOR   Steel x60 + Wire x40" +
                    (circuit != null ? " + Circuit x8" : "") + "\n" +
                    "    Place it ON a deposit. Needs power. Runs unattended\n" +
                    "    until the deposit is exhausted.\n\n" +
                    "Deep deposits already exist in every world, including saves\n" +
                    "made before this version - they are derived from the world\n" +
                    "seed, not spawned, so nothing needs regenerating.\n\n" +
                    (any ? "Changes were written. See the Console." : "Everything was already in place."),
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 86] Aborted: " + ex);
                EditorUtility.DisplayDialog("Deep Core",
                    "Setup stopped: " + ex.Message + "\n\nNothing was written.", "OK");
            }
        }

        // ============================================================
        //                      Survey scanner
        // ============================================================
        private static bool BuildScanner(ItemDefinition steel, ItemDefinition wire,
            ItemDefinition circuit, RecipeRegistry registry)
        {
            string path = ItemsFolder + "/Item_DeepSurveyScanner.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            bool created = false;

            if (item == null)
            {
                EnsureFolder(ItemsFolder);
                item = ScriptableObject.CreateInstance<ResourceItem>();
                created = true;
            }
            bool dirty = created;

            // The id is load-bearing: DeepSurveyHud matches on it to decide whether the
            // player is carrying a scanner.
            if (item.itemId != VoxelEngine.Generation.DeepOreField.ScannerItemId)
            {
                item.itemId = VoxelEngine.Generation.DeepOreField.ScannerItemId;
                dirty = true;
            }
            if (item.displayName != "Deep Survey Scanner") { item.displayName = "Deep Survey Scanner"; dirty = true; }
            if (item.maxStack <= 0) { item.maxStack = 1; dirty = true; }
            if (item.massPerUnit <= 0f) { item.massPerUnit = 4f; dirty = true; }
            if (item.category != "Tools") { item.category = "Tools"; dirty = true; }
            if (string.IsNullOrEmpty(item.description))
            {
                item.description =
                    "Reads dense mass far below the crust. Carry it and the survey strip " +
                    "reports the nearest deep deposit and the distance to it. Deep deposits " +
                    "cannot be mined by hand - they need a Deep Core Extractor.";
                dirty = true;
            }
            if (item.icon == null) item.iconTint = ScannerTint;

            if (dirty)
            {
                if (!AssetDatabase.Contains(item)) AssetDatabase.CreateAsset(item, path);
                EditorUtility.SetDirty(item);
                Debug.Log("[Setup 86] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            bool recipeChanged = EnsureRecipe(registry, "Recipe_DeepSurveyScanner", "Deep Survey Scanner",
                item, RecipesFolder, StationTier.Assembler, 15f,
                steel, 8, wire, 12, circuit, 2);

            return dirty || recipeChanged;
        }

        // ============================================================
        //                        Extractor
        // ============================================================
        private static bool BuildExtractor(ItemDefinition steel, ItemDefinition wire,
            ItemDefinition circuit, RecipeRegistry registry)
        {
            var prefab = EnsureExtractorPrefab(out bool prefabChanged);

            string path = BlocksFolder + "/Block_DeepCoreExtractor.asset";
            var block = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool created = false;

            if (block == null)
            {
                EnsureFolder(BlocksFolder);
                block = ScriptableObject.CreateInstance<BlockItem>();
                created = true;
            }
            bool dirty = created;

            if (block.itemId != "deepcoreextractor") { block.itemId = "deepcoreextractor"; dirty = true; }
            if (block.displayName != "Deep Core Extractor") { block.displayName = "Deep Core Extractor"; dirty = true; }
            if (block.maxStack <= 0) { block.maxStack = 20; dirty = true; }
            if (block.massPerUnit <= 0f) { block.massPerUnit = 480f; dirty = true; }
            if (block.blockHealth <= 0) { block.blockHealth = 700; dirty = true; }
            if (block.miningTier <= 0) { block.miningTier = 1; dirty = true; }
            if (block.category != "Machines") { block.category = "Machines"; dirty = true; }
            if (string.IsNullOrEmpty(block.description))
            {
                block.description =
                    "Taps a deep ore deposit. Place it on a deposit found with a Deep Survey " +
                    "Scanner, give it power, and it produces ore unattended until the deposit " +
                    "is exhausted. Deposits are finite - plan to move on.";
                dirty = true;
            }
            if (block.icon == null) block.iconTint = ExtractorTint;

            if (block.placedPrefab == null || block.placedPrefab != prefab)
            {
                block.placedPrefab = prefab;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(block)) AssetDatabase.CreateAsset(block, path);
                EditorUtility.SetDirty(block);
                Debug.Log("[Setup 86] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            bool recipeChanged = EnsureRecipe(registry, "Recipe_DeepCoreExtractor", "Deep Core Extractor",
                block, RecipesFolder, StationTier.Assembler, 40f,
                steel, 60, wire, 40, circuit, 8);

            return prefabChanged || dirty || recipeChanged;
        }

        private static GameObject EnsureExtractorPrefab(out bool changed)
        {
            string path = PrefabsFolder + "/DeepCoreExtractor.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(PrefabsFolder);
                var root = new GameObject("DeepCoreExtractor");
                var body = MakeColoredMat(PrefabsFolder, "Mat_DeepCoreExtractor", ExtractorTint);
                var metal = MakeColoredMat(PrefabsFolder, "Mat_DeepCoreHead", new Color(0.30f, 0.33f, 0.38f));

                var baseSlab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                baseSlab.name = "Base";
                baseSlab.transform.SetParent(root.transform, false);
                baseSlab.transform.localScale = new Vector3(2.4f, 0.4f, 2.4f);
                baseSlab.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                Paint(baseSlab, body);

                // A tall derrick reads as "this goes down a long way", which is the whole
                // idea the block needs to communicate at a glance.
                var derrick = GameObject.CreatePrimitive(PrimitiveType.Cube);
                derrick.name = "Derrick";
                derrick.transform.SetParent(root.transform, false);
                derrick.transform.localScale = new Vector3(0.9f, 3.0f, 0.9f);
                derrick.transform.localPosition = new Vector3(0f, 1.9f, 0f);
                Paint(derrick, metal);

                var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cap.name = "Head";
                cap.transform.SetParent(root.transform, false);
                cap.transform.localScale = new Vector3(1.5f, 0.42f, 1.5f);
                cap.transform.localPosition = new Vector3(0f, 3.5f, 0f);
                Paint(cap, body);

                for (int i = 0; i < 4; i++)
                {
                    var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    leg.name = "Leg" + i;
                    leg.transform.SetParent(root.transform, false);
                    float x = (i % 2 == 0) ? -0.95f : 0.95f;
                    float z = (i < 2) ? -0.95f : 0.95f;
                    leg.transform.localScale = new Vector3(0.18f, 1.8f, 0.18f);
                    leg.transform.localPosition = new Vector3(x, 1.2f, z);
                    Paint(leg, metal);
                }

                root.AddComponent<PowerConsumer>();
                root.AddComponent<DeepCoreExtractor>();

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 86] Created " + path + ".");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;

            if (contents.GetComponent<PowerConsumer>() == null)
            {
                contents.AddComponent<PowerConsumer>();
                dirty = true;
                Debug.Log("[Setup 86] Extractor prefab had no PowerConsumer; added one.");
            }
            if (contents.GetComponent<DeepCoreExtractor>() == null)
            {
                contents.AddComponent<DeepCoreExtractor>();
                dirty = true;
                Debug.Log("[Setup 86] Extractor prefab had no DeepCoreExtractor; added one.");
            }

            GameObject result = existing;
            if (dirty) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            changed = dirty;
            return result;
        }

        // ============================================================
        //                        Helpers
        // ============================================================
        private static bool EnsureRecipe(RecipeRegistry registry, string stem, string display,
            ItemDefinition outputItem, string folder, StationTier station, float seconds,
            ItemDefinition steel, int steelCount,
            ItemDefinition wire, int wireCount,
            ItemDefinition circuit, int circuitCount)
        {
            var recipe = FindRecipe(stem);
            bool changed = false;

            RecipeIngredient[] Inputs()
            {
                // The circuit is optional: not every project has that item authored, and a
                // missing ingredient must degrade to a simpler recipe rather than abort.
                if (circuit != null && circuitCount > 0)
                {
                    return new[]
                    {
                        new RecipeIngredient { item = steel, count = steelCount },
                        new RecipeIngredient { item = wire, count = wireCount },
                        new RecipeIngredient { item = circuit, count = circuitCount },
                    };
                }
                return new[]
                {
                    new RecipeIngredient { item = steel, count = steelCount },
                    new RecipeIngredient { item = wire, count = wireCount },
                };
            }

            if (recipe == null)
            {
                EnsureFolder(folder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = display;
                recipe.requiredStation = station;
                recipe.craftSeconds = seconds;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = outputItem;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, folder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 86] Created " + stem + ".");
            }
            else
            {
                if (recipe.outputItem == null && outputItem != null)
                {
                    recipe.outputItem = outputItem;
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
                Debug.Log("[Setup 86] Added " + stem + " to RecipeRegistry.");
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

        private static void Paint(GameObject go, Material mat)
        {
            if (mat == null) return;
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
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

        private static Material MakeColoredMat(string folder, string name, Color c)
        {
            string path = folder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                Debug.LogError("[Setup 86] Preserved conflicting asset at '" + path + "'.");
                return null;
            }
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(sh) { name = name, color = c };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
#endif
