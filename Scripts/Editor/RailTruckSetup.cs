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
    /// Step 92 (11.31.0-dev): the Rail Truck — Train System v2, phase 1.
    ///
    /// One grid block. Put it on any construct you have built and that construct can run
    /// on rails: no locomotive entity, no special vehicle type. A wagon carries containers
    /// and tanks because it is a grid and those are grid blocks.
    ///
    /// Non-destructive: an existing prefab, item or recipe keeps every authored value and
    /// only missing links are repaired. Safe to re-run. Logs with the [Setup 92] prefix.
    /// </summary>
    public static class RailTruckSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string GridPrefabsFolder = Root + "/GridSystem/Prefabs";
        private const string GridItemsFolder = Root + "/GridSystem/Items";
        private const string GridRecipesFolder = Root + "/GridSystem/Recipes";

        private static readonly Color TruckTint = new(0.42f, 0.45f, 0.50f);
        private static readonly Color WheelTint = new(0.17f, 0.18f, 0.21f);

        public static void RunStep92()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Rail Truck", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Rail Truck",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.",
                        "OK");
                    return;
                }

                var steel = FindItem("Item_SteelIngot");
                var wire = FindItem("Item_CopperWire");
                if (steel == null)
                {
                    EditorUtility.DisplayDialog("Rail Truck",
                        "Steel Ingot not found. Run the earlier crafting-content steps first.", "OK");
                    return;
                }

                var prefab = EnsurePrefab(out bool prefabChanged);
                var item = EnsureItem(prefab, out bool itemChanged);
                bool recipeChanged = EnsureRecipe(registry, item, steel, wire);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("Step 92 - Rail Truck",
                    "Rail Truck authored.\n\n" +
                    "  RAIL TRUCK   Steel x18" + (wire != null ? " + Wire x10" : "") + "\n\n" +
                    "Train System v2: a train is now an ordinary construct.\n\n" +
                    "  1. Build any grid you like.\n" +
                    "  2. Place a Rail Truck on it.\n" +
                    "  3. Park it within a few metres of track.\n" +
                    "  4. Open the truck and press SNAP TO RAIL, then DRIVE.\n\n" +
                    "Because it is a grid, it can carry containers, tanks,\n" +
                    "refineries or turrets, and takes damage, paint, power and\n" +
                    "pressurisation exactly like anything else you build.\n\n" +
                    "The 11.15.0 rail network (track, switches, buffers) is\n" +
                    "unchanged and is what these run on.\n\n" +
                    ((prefabChanged || itemChanged || recipeChanged)
                        ? "Changes were written. See the Console."
                        : "Everything was already in place."),
                    "OK");

                Debug.Log("[Setup 92] Rail Truck setup complete.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 92] Aborted: " + ex);
                EditorUtility.DisplayDialog("Rail Truck",
                    "Setup stopped: " + ex.Message + "\n\nNothing further was written.", "OK");
            }
        }

        private static GameObject EnsurePrefab(out bool changed)
        {
            string path = GridPrefabsFolder + "/RailTruck.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(GridPrefabsFolder);
                var root = new GameObject("RailTruck");
                var body = MakeMat("Mat_RailTruck", TruckTint);
                var wheel = MakeMat("Mat_RailTruckWheel", WheelTint);

                var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frame.name = "Frame";
                frame.transform.SetParent(root.transform, false);
                frame.transform.localScale = new Vector3(2.2f, 0.5f, 2.2f);
                frame.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                Paint(frame, body);

                // Four flanged wheels, so the block reads as a bogie at a glance.
                for (int i = 0; i < 4; i++)
                {
                    var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    w.name = "Wheel" + i;
                    w.transform.SetParent(root.transform, false);
                    float x = (i % 2 == 0) ? -0.85f : 0.85f;
                    float z = (i < 2) ? -0.7f : 0.7f;
                    w.transform.localPosition = new Vector3(x, 0.05f, z);
                    w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    w.transform.localScale = new Vector3(0.45f, 0.07f, 0.45f);
                    Paint(w, wheel);
                }

                var truck = root.AddComponent<GridRailTruck>();
                truck.blockName = "Rail Truck";
                truck.BlockMass = 260f;
                truck.maxHP = 420f;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 92] Created " + path + ".");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;

            if (contents.GetComponent<GridRailTruck>() == null)
            {
                var truck = contents.AddComponent<GridRailTruck>();
                truck.blockName = "Rail Truck";
                dirty = true;
                Debug.Log("[Setup 92] Prefab had no GridRailTruck; added one.");
            }

            GameObject result = existing;
            if (dirty) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            changed = dirty;
            return result;
        }

        private static GridBlockItem EnsureItem(GameObject prefab, out bool changed)
        {
            string path = GridItemsFolder + "/GItem_RailTruck.asset";
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(path);
            bool created = false;

            if (item == null)
            {
                EnsureFolder(GridItemsFolder);
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                created = true;
            }
            bool dirty = created;

            if (item.itemId != "railtruck") { item.itemId = "railtruck"; dirty = true; }
            if (item.displayName != "Rail Truck") { item.displayName = "Rail Truck"; dirty = true; }
            if (item.maxStack <= 0) { item.maxStack = 20; dirty = true; }
            if (item.massPerUnit <= 0f) { item.massPerUnit = 260f; dirty = true; }
            if (item.category != "Grid Blocks") { item.category = "Grid Blocks"; dirty = true; }
            if (string.IsNullOrEmpty(item.description))
            {
                item.description =
                    "Puts a construct on rails. Any grid with a Rail Truck can run on track, " +
                    "so a train is just something you built - it carries whatever grid blocks " +
                    "you put on it. The slowest truck aboard sets the speed.";
                dirty = true;
            }
            if (item.icon == null) item.iconTint = TruckTint;
            if (created) { item.blockMass = 260f; item.blockHP = 420f; }

            if (item.blockPrefab == null || item.blockPrefab != prefab)
            {
                item.blockPrefab = prefab;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(item)) AssetDatabase.CreateAsset(item, path);
                EditorUtility.SetDirty(item);
                Debug.Log("[Setup 92] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            changed = dirty;
            return item;
        }

        private static bool EnsureRecipe(RecipeRegistry registry, GridBlockItem item,
            ItemDefinition steel, ItemDefinition wire)
        {
            const string stem = "Recipe_RailTruck";
            var recipe = FindRecipe(stem);
            bool changed = false;

            RecipeIngredient[] Inputs()
            {
                if (wire == null)
                    return new[] { new RecipeIngredient { item = steel, count = 18 } };
                return new[]
                {
                    new RecipeIngredient { item = steel, count = 18 },
                    new RecipeIngredient { item = wire, count = 10 },
                };
            }

            if (recipe == null)
            {
                EnsureFolder(GridRecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = "Rail Truck";
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 14f;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = item;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, GridRecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 92] Created " + stem + ".");
            }
            else
            {
                if (recipe.outputItem == null && item != null)
                {
                    recipe.outputItem = item;
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
                Debug.Log("[Setup 92] Added " + stem + " to RecipeRegistry.");
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

        private static Material MakeMat(string name, Color c)
        {
            EnsureFolder(GridPrefabsFolder);
            string path = GridPrefabsFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                Debug.LogError("[Setup 92] Preserved conflicting asset at '" + path + "'.");
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
