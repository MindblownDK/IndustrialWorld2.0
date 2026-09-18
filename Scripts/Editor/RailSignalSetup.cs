#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 94 (11.34.0-dev): the Rail Signal — Train System v2, phase 4.
    ///
    /// Block occupancy is automatic and needs nothing placed; a signal is the VISIBLE half
    /// of it. Put one where a line is contended and it shows red while a train holds the
    /// section, so a train stopping has a visible cause rather than looking broken.
    ///
    /// Non-destructive: an existing prefab, item or recipe keeps every authored value and
    /// only missing links are repaired. Safe to re-run. Logs with the [Setup 94] prefix.
    /// </summary>
    public static class RailSignalSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string BlocksFolder = Root + "/Blocks";
        private const string RecipesFolder = Root + "/Recipes";
        private const string PrefabsFolder = Root + "/StationPrefabs";

        private static readonly Color PostTint = new(0.30f, 0.32f, 0.36f);
        private static readonly Color LampTint = new(0.25f, 0.90f, 0.35f);

        public static void RunStep94()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Rail Signal", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Rail Signal",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.",
                        "OK");
                    return;
                }

                var steel = FindItem("Item_SteelIngot");
                var wire = FindItem("Item_CopperWire");
                if (steel == null)
                {
                    EditorUtility.DisplayDialog("Rail Signal",
                        "Steel Ingot not found. Run the earlier crafting-content steps first.", "OK");
                    return;
                }

                var prefab = EnsurePrefab(out bool prefabChanged);
                var block = EnsureBlockItem(prefab, out bool itemChanged);
                bool recipeChanged = EnsureRecipe(registry, block, steel, wire);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("Step 94 - Rail Signal",
                    "Rail Signal authored.\n\n" +
                    "  RAIL SIGNAL   Steel x8" + (wire != null ? " + Wire x6" : "") + "\n\n" +
                    "Block occupancy is ALREADY ACTIVE without this - trains\n" +
                    "stop for occupied track automatically, and sections are\n" +
                    "derived from the track graph (the line between two\n" +
                    "junctions is one section).\n\n" +
                    "A signal is the visible half: place one beside a contended\n" +
                    "stretch and it shows red while a train holds that section,\n" +
                    "so a waiting train has a visible reason.\n\n" +
                    "Right-click a signal to read its state.\n\n" +
                    ((prefabChanged || itemChanged || recipeChanged)
                        ? "Changes were written. See the Console."
                        : "Everything was already in place."),
                    "OK");

                Debug.Log("[Setup 94] Rail Signal setup complete.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 94] Aborted: " + ex);
                EditorUtility.DisplayDialog("Rail Signal",
                    "Setup stopped: " + ex.Message + "\n\nNothing further was written.", "OK");
            }
        }

        private static GameObject EnsurePrefab(out bool changed)
        {
            string path = PrefabsFolder + "/RailSignal.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(PrefabsFolder);
                var root = new GameObject("RailSignal");
                var postMat = MakeMat("Mat_RailSignalPost", PostTint);
                var lampMat = MakeMat("Mat_RailSignalLamp", LampTint);

                var baseBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                baseBlock.name = "Base";
                baseBlock.transform.SetParent(root.transform, false);
                baseBlock.transform.localScale = new Vector3(0.5f, 0.12f, 0.5f);
                baseBlock.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                Paint(baseBlock, postMat);

                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = "Post";
                post.transform.SetParent(root.transform, false);
                post.transform.localScale = new Vector3(0.12f, 1.9f, 0.12f);
                post.transform.localPosition = new Vector3(0f, 1.0f, 0f);
                Paint(post, postMat);

                var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
                head.name = "Head";
                head.transform.SetParent(root.transform, false);
                head.transform.localScale = new Vector3(0.34f, 0.5f, 0.2f);
                head.transform.localPosition = new Vector3(0f, 2.0f, 0f);
                Paint(head, postMat);

                // The lamp is a separate renderer so the aspect can be tinted without
                // touching the rest of the post.
                var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lamp.name = "Lamp";
                lamp.transform.SetParent(root.transform, false);
                lamp.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);
                lamp.transform.localPosition = new Vector3(0f, 2.05f, 0.12f);
                Paint(lamp, lampMat);

                var signal = root.AddComponent<RailSignal>();
                signal.lamp = lamp.GetComponent<Renderer>();

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 94] Created " + path + ".");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;

            var existingSignal = contents.GetComponent<RailSignal>();
            if (existingSignal == null)
            {
                existingSignal = contents.AddComponent<RailSignal>();
                dirty = true;
                Debug.Log("[Setup 94] Prefab had no RailSignal; added one.");
            }

            // Repair a missing lamp reference: a signal with no lamp silently shows nothing,
            // which is indistinguishable from a signal that never goes red.
            if (existingSignal.lamp == null)
            {
                var lampTransform = contents.transform.Find("Lamp");
                if (lampTransform != null)
                {
                    existingSignal.lamp = lampTransform.GetComponent<Renderer>();
                    dirty = true;
                    Debug.Log("[Setup 94] Reconnected the signal lamp renderer.");
                }
            }

            GameObject result = existing;
            if (dirty) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            changed = dirty;
            return result;
        }

        private static BlockItem EnsureBlockItem(GameObject prefab, out bool changed)
        {
            string path = BlocksFolder + "/Block_RailSignal.asset";
            var block = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool created = false;

            if (block == null)
            {
                EnsureFolder(BlocksFolder);
                block = ScriptableObject.CreateInstance<BlockItem>();
                created = true;
            }
            bool dirty = created;

            if (block.itemId != "railsignal") { block.itemId = "railsignal"; dirty = true; }
            if (block.displayName != "Rail Signal") { block.displayName = "Rail Signal"; dirty = true; }
            if (block.maxStack <= 0) { block.maxStack = 40; dirty = true; }
            if (block.massPerUnit <= 0f) { block.massPerUnit = 25f; dirty = true; }
            if (block.blockHealth <= 0) { block.blockHealth = 140; dirty = true; }
            if (block.miningTier <= 0) { block.miningTier = 1; dirty = true; }
            if (block.category != "Rail") { block.category = "Rail"; dirty = true; }
            if (string.IsNullOrEmpty(block.description))
            {
                block.description =
                    "Shows whether the stretch of line beside it is held by a train. Trains " +
                    "already stop for occupied track on their own - a signal makes that " +
                    "visible so a waiting train has an obvious reason.";
                dirty = true;
            }
            if (block.icon == null) block.iconTint = LampTint;

            if (block.placedPrefab == null || block.placedPrefab != prefab)
            {
                block.placedPrefab = prefab;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(block)) AssetDatabase.CreateAsset(block, path);
                EditorUtility.SetDirty(block);
                Debug.Log("[Setup 94] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            changed = dirty;
            return block;
        }

        private static bool EnsureRecipe(RecipeRegistry registry, BlockItem block,
            ItemDefinition steel, ItemDefinition wire)
        {
            const string stem = "Recipe_RailSignal";
            var recipe = FindRecipe(stem);
            bool changed = false;

            RecipeIngredient[] Inputs()
            {
                if (wire == null)
                    return new[] { new RecipeIngredient { item = steel, count = 8 } };
                return new[]
                {
                    new RecipeIngredient { item = steel, count = 8 },
                    new RecipeIngredient { item = wire, count = 6 },
                };
            }

            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = "Rail Signal";
                recipe.requiredStation = StationTier.CraftingBench;
                recipe.craftSeconds = 6f;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = block;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, RecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 94] Created " + stem + ".");
            }
            else
            {
                if (recipe.outputItem == null && block != null)
                {
                    recipe.outputItem = block;
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
                Debug.Log("[Setup 94] Added " + stem + " to RecipeRegistry.");
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
            EnsureFolder(PrefabsFolder);
            string path = PrefabsFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                Debug.LogError("[Setup 94] Preserved conflicting asset at '" + path + "'.");
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
