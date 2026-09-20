#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 96 (12.4.0-dev): author the steam railway - locomotive and water tower.
    ///
    ///   Steam Engine      - grid block; piston gear, flywheel, ROTATIONAL POWER only.
    ///   Water Tower       - stationary; platform-side water for berthed locomotives.
    ///
    /// Non-destructive: missing assets are created, existing ones only have
    /// unresolvable links repaired. Authored tuning is never reset. Safe to re-run.
    /// Logs with the [Setup 96] prefix.
    /// </summary>
    public static class RailSteamSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string BlocksFolder = Root + "/Blocks";
        private const string RecipesFolder = Root + "/Recipes";
        private const string PrefabsFolder = Root + "/StationPrefabs";
        private const string GridItemsFolder = Root + "/GridSystem/Items";
        private const string GridPrefabsFolder = Root + "/GridSystem/Prefabs";

        private static readonly Color BoilerBlack = new(0.09f, 0.09f, 0.10f);
        private static readonly Color Brass = new(0.72f, 0.51f, 0.22f);
        private static readonly Color Oxide = new(0.35f, 0.16f, 0.10f);
        private static readonly Color TankGreen = new(0.16f, 0.28f, 0.22f);

        private const string SteelPath = Root + "/Items/Item_SteelIngot.asset";
        private const string BrassPath = Root + "/Items/Item_BrassIngot.asset";
        private const string WirePath = Root + "/Industrial/Items/Item_CopperWire.asset";

        public static void RunStep96()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Steam Railway", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Steam Railway",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.", "OK");
                    return;
                }

                var steel = AssetDatabase.LoadAssetAtPath<ItemDefinition>(SteelPath);
                var brass = AssetDatabase.LoadAssetAtPath<ItemDefinition>(BrassPath);
                var wire = AssetDatabase.LoadAssetAtPath<ItemDefinition>(WirePath);
                if (steel == null)
                {
                    EditorUtility.DisplayDialog("Steam Railway",
                        "Steel ingot is missing.\n\nRun the earlier crafting-content steps first.", "OK");
                    return;
                }
                if (brass == null)
                {
                    EditorUtility.DisplayDialog("Steam Railway",
                        "Brass ingot is missing.\n\nRun step 95 first - it authors the brass smelt.", "OK");
                    return;
                }

                bool any = false;
                any |= BuildSteamEngine(steel, brass, wire, registry);
                any |= BuildWaterTower(steel, brass, registry);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[Setup 96] Steam railway complete. " +
                          (any ? "Changes were written." : "Everything was already in place."));

                EditorUtility.DisplayDialog("Step 96 - Steam Railway",
                    "Authored:\n\n" +
                    "  STEAM ENGINE      grid block  - steel x24 + brass x6 + wire x4\n" +
                    "  WATER TOWER       stationary  - steel x10 + brass x2\n\n" +
                    "The engine produces ROTATIONAL POWER only: a turning\n" +
                    "flywheel drives the train mechanically when the grid has\n" +
                    "no electric power, and feeds the brass screens' shaft tap.\n" +
                    "It shovels coal (or wood) from any cargo container aboard,\n" +
                    "drinks from tank wagons moving and water towers berthed.\n" +
                    "E opens the footplate: pressure, water, fire, whistle.\n\n" +
                    (any ? "Changes were written. See the Console." : "Everything was already in place."),
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 96] Aborted: " + ex);
                EditorUtility.DisplayDialog("Steam Railway",
                    "Setup stopped: " + ex.Message + "\n\nNothing was written.", "OK");
            }
        }

        // ============================================================
        //  Steam Engine - grid block, piston gear and all
        // ============================================================
        private static bool BuildSteamEngine(ItemDefinition steel, ItemDefinition brass,
            ItemDefinition wire, RecipeRegistry registry)
        {
            const string path = GridPrefabsFolder + "/SteamEngine.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool changed = false;

            if (prefab == null)
            {
                EnsureFolder(GridPrefabsFolder);
                var root = new GameObject("SteamEngine");

                var black = Mat("Mat_EngineBlack", BoilerBlack);
                var brassM = Mat("Mat_EngineBrass", Brass);
                var oxide = Mat("Mat_EngineOxide", Oxide);

                // Bed and frames: everything else bolts to these.
                Cube(root, "Bed", new Vector3(0f, 0.06f, 0f), new Vector3(1.10f, 0.12f, 2.40f), black);
                for (int i = 0; i < 2; i++)
                    Cube(root, "Frame" + i, new Vector3(i == 0 ? -0.42f : 0.42f, 0.21f, 0f),
                        new Vector3(0.08f, 0.30f, 2.40f), oxide);

                // Steam cylinder forward, with a brass gland the piston rod runs through.
                var cyl = Cylinder(root, "SteamCylinder", new Vector3(0f, 0.35f, 0.95f),
                    new Vector3(0.34f, 0.45f, 0.34f), black);
                cyl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var gland = Cylinder(root, "Gland", new Vector3(0f, 0.35f, 0.50f),
                    new Vector3(0.16f, 0.06f, 0.16f), brassM);
                gland.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                // Slide bars the crosshead rides on.
                for (int i = 0; i < 2; i++)
                    Cube(root, "SlideBar" + i, new Vector3(i == 0 ? -0.10f : 0.10f, 0.35f, 0.35f),
                        new Vector3(0.04f, 0.05f, 1.00f), black);

                // Crosshead + piston rod. The rod is a child so it slides with it and
                // disappears into the gland exactly like the real thing.
                var cross = Cube(root, "Crosshead", new Vector3(0f, 0.35f, 0.40f),
                    new Vector3(0.16f, 0.16f, 0.24f), brassM);
                var prod = Cylinder(cross, "PistonRod", new Vector3(0f, 0f, 0.45f),
                    new Vector3(0.05f, 0.45f, 0.05f), black);
                prod.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                // Connecting rod: an empty pivot at the crank-pin end, body stretched
                // along its local Y. GridSteamEngine lays it between pin and crosshead
                // at its true angle every frame.
                var conRod = new GameObject("ConRod");
                conRod.transform.SetParent(root.transform, false);
                conRod.transform.localPosition = new Vector3(0f, 0.35f, -0.15f);
                var conBody = Cube(conRod, "ConRodBody", new Vector3(0f, 0.425f, 0f),
                    new Vector3(0.05f, 0.85f, 0.05f), oxide);

                // Flywheel: mount carries the placement rotation, spin carries the
                // animation and the crank pin, so the two never fight.
                var mount = new GameObject("FlywheelMount");
                mount.transform.SetParent(root.transform, false);
                mount.transform.localPosition = new Vector3(0f, 0.35f, -0.45f);
                mount.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                var spin = new GameObject("FlywheelSpin");
                spin.transform.SetParent(mount.transform, false);
                var rim = Cylinder(spin, "Flywheel", new Vector3(0f, 0f, 0f),
                    new Vector3(0.90f, 0.35f, 0.90f), black);
                var hub = Cylinder(spin, "Hub", new Vector3(0f, 0f, 0f),
                    new Vector3(0.14f, 0.40f, 0.14f), brassM);
                for (int i = 0; i < 4; i++)
                {
                    var spoke = Cube(spin, "Spoke" + i, new Vector3(0f, 0f, 0f),
                        new Vector3(0.80f, 0.30f, 0.06f), oxide);
                    spoke.transform.localRotation = Quaternion.Euler(0f, i * 45f, 0f);
                }
                var pin = Cylinder(spin, "CrankPin", new Vector3(0.30f, 0f, 0f),
                    new Vector3(0.05f, 0.42f, 0.05f), brassM);

                // Vertical boiler behind, brass-banded, with the chimney the white
                // smoke comes out of.
                var boiler = Cylinder(root, "Boiler", new Vector3(0f, 0.62f, -1.15f),
                    new Vector3(0.55f, 0.60f, 0.55f), oxide);
                for (int i = 0; i < 2; i++)
                {
                    var band = Cylinder(root, "BoilerBand" + i, new Vector3(0f, 0.45f + i * 0.40f, -1.15f),
                        new Vector3(0.58f, 0.03f, 0.58f), brassM);
                }
                Cylinder(root, "Chimney", new Vector3(0f, 1.42f, -1.15f),
                    new Vector3(0.14f, 0.22f, 0.14f), black);
                var gauge = Cylinder(root, "Gauge", new Vector3(0f, 0.95f, -0.86f),
                    new Vector3(0.12f, 0.02f, 0.12f), brassM);
                gauge.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                var engine = root.AddComponent<GridSteamEngine>();
                engine.blockName = "Steam Engine";
                engine.BlockMass = 900f;
                engine.maxHP = 600f;

                ScrubMissingScripts(root);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 96] Created " + path + ".");
            }
            else
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                ScrubMissingScripts(contents);
                bool repaired = false;
                if (contents.GetComponent<GridSteamEngine>() == null)
                {
                    var e = contents.AddComponent<GridSteamEngine>();
                    e.blockName = "Steam Engine";
                    repaired = true;
                }
                if (repaired) prefab = PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
                changed = repaired;
            }

            changed |= EnsureGridItem("GItem_SteamEngine", "steamengine", "Steam Engine",
                "A piston steam engine: vertical boiler, horizontal cylinder, crosshead, " +
                "connecting rod and a flywheel across the frames. It produces ROTATIONAL " +
                "POWER and nothing else - no electricity, no traction of its own. The " +
                "mechanical drive takes the flywheel's turn to move a train, and the " +
                "brass screens tap the same shaft. Shovels coal (or wood) from any cargo " +
                "container aboard; drinks from tank wagons moving and water towers " +
                "berthed. Open it with E for the footplate.",
                prefab, Brass, 900f, 600f);

            changed |= EnsureRecipe(registry, "Recipe_SteamEngine", "Steam Engine",
                new[] { (steel, 24), (brass, 6), (wire, 4) }, StationTier.Assembler, 12f,
                AssetDatabase.LoadAssetAtPath<GridBlockItem>(GridItemsFolder + "/GItem_SteamEngine.asset"));

            return changed;
        }

        // ============================================================
        //  Water Tower - stationary
        // ============================================================
        private static bool BuildWaterTower(ItemDefinition steel, ItemDefinition brass, RecipeRegistry registry)
        {
            string path = PrefabsFolder + "/WaterTower.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool changed = false;

            if (prefab == null)
            {
                EnsureFolder(PrefabsFolder);
                var root = new GameObject("WaterTower");

                for (int i = 0; i < 4; i++)
                {
                    float x = (i % 2 == 0 ? -1f : 1f) * 0.70f;
                    float z = (i < 2 ? -1f : 1f) * 0.70f;
                    Cube(root, "Leg" + i, new Vector3(x, 1.10f, z), new Vector3(0.12f, 2.20f, 0.12f),
                        Mat("Mat_TowerWood", Oxide));
                }

                var tank = Cylinder(root, "Tank", new Vector3(0f, 2.70f, 0f),
                    new Vector3(1.70f, 0.55f, 1.70f), Mat("Mat_TowerGreen", TankGreen));
                tank.transform.localRotation = Quaternion.identity;
                for (int i = 0; i < 2; i++)
                {
                    var band = Cylinder(root, "TankBand" + i, new Vector3(0f, 2.40f + i * 0.60f, 0f),
                        new Vector3(1.74f, 0.04f, 1.74f), Mat("Mat_TowerBrass", Brass));
                    band.transform.localRotation = Quaternion.identity;
                }
                var roof = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                roof.name = "Roof";
                roof.transform.SetParent(root.transform, false);
                roof.transform.localPosition = new Vector3(0f, 3.30f, 0f);
                roof.transform.localScale = new Vector3(1.80f, 0.60f, 1.80f);
                Paint(roof, Mat("Mat_TowerBrass", Brass));

                // The standpipe: a brass arm out over the platform side.
                var spout = Cylinder(root, "Standpipe", new Vector3(0.95f, 2.10f, 0f),
                    new Vector3(0.10f, 0.45f, 0.10f), Mat("Mat_TowerBrass", Brass));
                spout.transform.localRotation = Quaternion.Euler(0f, 0f, -20f);

                root.AddComponent<VoxelEngine.Building.WaterTower>();

                ScrubMissingScripts(root);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 96] Created " + path + ".");
            }
            else
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                ScrubMissingScripts(contents);
                bool repaired = false;
                if (contents.GetComponent<VoxelEngine.Building.WaterTower>() == null)
                {
                    contents.AddComponent<VoxelEngine.Building.WaterTower>();
                    repaired = true;
                }
                if (repaired) prefab = PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
                changed = repaired;
            }

            changed |= EnsureBlockItem("WaterTower", "Water Tower",
                "A tank on legs with a standpipe over the platform side. Fills itself " +
                "from a water network it stands beside, or slowly from open water - a " +
                "tower by a pond seeps full the way real ones were pumped. Berthed " +
                "locomotives take their water from here.",
                prefab, TankGreen, 60f, 220, registry, "Recipe_WaterTower",
                new[] { (steel, 10), (brass, 2) }, StationTier.CraftingBench, 6f);

            return changed;
        }

        // ============================================================
        //  Shared authoring
        // ============================================================
        private static void ScrubMissingScripts(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        }

        private static GameObject Cube(GameObject parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Paint(go, mat);
            return go;
        }

        private static GameObject Cylinder(GameObject parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Paint(go, mat);
            return go;
        }

        private static void Paint(GameObject go, Material mat)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
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
            if (item.maxStack <= 0) { item.maxStack = 5; dirty = true; }
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
                Debug.Log("[Setup 96] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }
            return dirty;
        }

        private static bool EnsureBlockItem(string asset, string display, string description,
            GameObject prefab, Color tint, float mass, int health, RecipeRegistry registry,
            string recipeStem, (ItemDefinition item, int count)[] inputs, StationTier tier, float seconds)
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
                Debug.Log("[Setup 96] " + (created ? "Created" : "Repaired") + " " + path + ".");
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
                recipe.inputs = new RecipeIngredient[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                    recipe.inputs[i] = new RecipeIngredient { item = inputs[i].item, count = inputs[i].count };
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
            (ItemDefinition item, int count)[] inputs, StationTier tier, float seconds, ItemDefinition output)
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
                recipe.inputs = new RecipeIngredient[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                    recipe.inputs[i] = new RecipeIngredient { item = inputs[i].item, count = inputs[i].count };
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
