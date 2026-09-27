#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Farming;
using VoxelEngine.Items;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 87 (11.19.0-dev): Livestock Husbandry.
    ///
    /// Two jobs:
    ///   1. Attach <see cref="LivestockHusbandry"/> to the Cow / Sheep / Pig prefabs that
    ///      the existing fauna step already authored, so those animals become farmable
    ///      without becoming a different kind of object.
    ///   2. Author the Livestock Pen block that feeds, waters, breeds and harvests them.
    ///
    /// Non-destructive: existing prefabs keep every authored value; only the missing
    /// component is added. Safe to re-run. Logs with the [Setup 87] prefix.
    /// </summary>
    public static class LivestockHusbandrySetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string BlocksFolder = Root + "/Blocks";
        private const string RecipesFolder = Root + "/Recipes";
        private const string PrefabsFolder = Root + "/StationPrefabs";
        private const string LivestockFolder = "Assets/Resources/Livestock";

        private static readonly Color PenTint = new(0.52f, 0.40f, 0.26f);

        private static readonly string[] AnimalPrefabs =
        {
            LivestockFolder + "/Cow.prefab",
            LivestockFolder + "/Sheep.prefab",
            LivestockFolder + "/Pig.prefab",
        };

        public static void RunStep87()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Livestock Husbandry", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Livestock Husbandry",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.", "OK");
                    return;
                }

                var milk = EnsureMilkItem();
                var wool = FindItemByItemId("item_wool") ?? FindItem("Item_Wool");
                if (wool == null)
                    Debug.LogWarning("[Setup 87] Wool item not found; sheep will produce nothing " +
                                     "until the fauna content step has been run.");

                int upgraded = UpgradeAnimals(out int missing);
                bool penChanged = BuildPen(registry, milk, wool);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                string animalNote = missing > 0
                    ? $"\n\nNOTE: {missing} animal prefab(s) were not found.\nRun the fauna/livestock content step first, then re-run this step."
                    : "";

                EditorUtility.DisplayDialog("Step 87 - Livestock Husbandry",
                    "Livestock husbandry authored.\n\n" +
                    $"  {upgraded} animal prefab(s) can now be farmed\n" +
                    "    Cow produces milk, Sheep produces wool.\n" +
                    "    Pigs stay meat-and-hide only.\n\n" +
                    "  LIVESTOCK PEN   Wood x40 + Steel x8\n" +
                    "    Feeds, waters, shelters, breeds and harvests\n" +
                    "    every animal within 12 m, up to its population cap.\n\n" +
                    "Feed accepts wheat, grain, hay, biomass and root crops.\n" +
                    "Water accepts any water item. Both can be belted or piped in.\n\n" +
                    "Animals are only farmed once a pen is near them - wild\n" +
                    "herds behave exactly as they did before." + animalNote,
                    "OK");

                Debug.Log($"[Setup 87] Husbandry setup complete. {upgraded} animal(s) upgraded, " +
                          (penChanged ? "pen written." : "pen already in place."));
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 87] Aborted: " + ex);
                EditorUtility.DisplayDialog("Livestock Husbandry",
                    "Setup stopped: " + ex.Message + "\n\nNothing further was written.", "OK");
            }
        }

        // ============================================================
        //                      Milk item
        // ============================================================
        // Wool and Hide already exist from the fauna step, but Milk was never authored.
        // Without it every cow would fill up and stall with nothing to hand over, which
        // looks exactly like a broken pen. Authored beside the other animal products.
        private static ItemDefinition EnsureMilkItem()
        {
            const string folder = Root + "/Fauna/Items";
            const string path = folder + "/Item_Milk.asset";

            var existing = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (existing != null) return existing;

            // Another Milk may already exist elsewhere in the project; never make a second.
            var guids = AssetDatabase.FindAssets("t:ItemDefinition");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var candidate = AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
                if (candidate != null && !string.IsNullOrEmpty(candidate.itemId)
                    && candidate.itemId.Contains("milk"))
                {
                    Debug.Log("[Setup 87] Milk already exists at " + p + "; left untouched.");
                    return candidate;
                }
            }

            EnsureFolder(folder);
            var milk = ScriptableObject.CreateInstance<ResourceItem>();
            milk.itemId = "item_milk";
            milk.displayName = "Milk";
            milk.description = "Fresh milk from a well-kept cow. Renewable - the animal is unharmed.";
            milk.iconTint = new Color(0.95f, 0.95f, 0.92f);
            milk.maxStack = 40;
            milk.massPerUnit = 0.4f;
            milk.category = "Food";
            AssetDatabase.CreateAsset(milk, path);
            EditorUtility.SetDirty(milk);
            Debug.Log("[Setup 87] Created " + path + ".");
            return milk;
        }

        // ============================================================
        //                    Animal prefabs
        // ============================================================
        private static int UpgradeAnimals(out int missing)
        {
            int upgraded = 0;
            missing = 0;

            foreach (var path in AnimalPrefabs)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                {
                    missing++;
                    Debug.LogWarning("[Setup 87] Animal prefab not found: " + path +
                                     ". Run the fauna content step first.");
                    continue;
                }

                var contents = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;

                if (contents.GetComponent<LivestockHusbandry>() == null)
                {
                    contents.AddComponent<LivestockHusbandry>();
                    dirty = true;
                    upgraded++;
                    Debug.Log("[Setup 87] Added LivestockHusbandry to " + path + ".");
                }

                if (dirty) PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return upgraded;
        }

        // ============================================================
        //                         Pen
        // ============================================================
        private static bool BuildPen(RecipeRegistry registry, ItemDefinition milk, ItemDefinition wool)
        {
            var prefab = EnsurePenPrefab(milk, wool, out bool prefabChanged);

            string path = BlocksFolder + "/Block_LivestockPen.asset";
            var block = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool created = false;

            if (block == null)
            {
                EnsureFolder(BlocksFolder);
                block = ScriptableObject.CreateInstance<BlockItem>();
                created = true;
            }
            bool dirty = created;

            if (block.itemId != "livestockpen") { block.itemId = "livestockpen"; dirty = true; }
            if (block.displayName != "Livestock Pen") { block.displayName = "Livestock Pen"; dirty = true; }
            if (block.maxStack <= 0) { block.maxStack = 20; dirty = true; }
            if (block.massPerUnit <= 0f) { block.massPerUnit = 120f; dirty = true; }
            if (block.blockHealth <= 0) { block.blockHealth = 320; dirty = true; }
            if (block.miningTier <= 0) { block.miningTier = 1; dirty = true; }
            if (block.category != "Machines") { block.category = "Machines"; dirty = true; }
            if (string.IsNullOrEmpty(block.description))
            {
                block.description =
                    "Manages every animal within 12 m: feeds and waters them from its supply, " +
                    "shelters them so they consume less, breeds them up to a population cap, " +
                    "and collects milk and wool without harming them.";
                dirty = true;
            }
            if (block.icon == null) block.iconTint = PenTint;

            if (block.placedPrefab == null || block.placedPrefab != prefab)
            {
                block.placedPrefab = prefab;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(block)) AssetDatabase.CreateAsset(block, path);
                EditorUtility.SetDirty(block);
                Debug.Log("[Setup 87] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            bool recipeChanged = EnsureRecipe(registry, block);
            return prefabChanged || dirty || recipeChanged;
        }

        private static GameObject EnsurePenPrefab(ItemDefinition milk, ItemDefinition wool, out bool changed)
        {
            string path = PrefabsFolder + "/LivestockPen.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(PrefabsFolder);
                var root = new GameObject("LivestockPen");
                var wood = MakeColoredMat(PrefabsFolder, "Mat_LivestockPen", PenTint);
                var trough = MakeColoredMat(PrefabsFolder, "Mat_PenTrough", new Color(0.36f, 0.30f, 0.22f));

                // A trough with fence posts: reads as a feeding station rather than a crate.
                var basin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                basin.name = "Trough";
                basin.transform.SetParent(root.transform, false);
                basin.transform.localScale = new Vector3(2.2f, 0.34f, 0.8f);
                basin.transform.localPosition = new Vector3(0f, 0.17f, 0f);
                Paint(basin, trough);

                for (int i = 0; i < 4; i++)
                {
                    var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    post.name = "Post" + i;
                    post.transform.SetParent(root.transform, false);
                    float x = (i % 2 == 0) ? -1.05f : 1.05f;
                    float z = (i < 2) ? -0.42f : 0.42f;
                    post.transform.localScale = new Vector3(0.16f, 1.1f, 0.16f);
                    post.transform.localPosition = new Vector3(x, 0.55f, z);
                    Paint(post, wood);
                }

                var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.name = "Rail";
                rail.transform.SetParent(root.transform, false);
                rail.transform.localScale = new Vector3(2.3f, 0.12f, 0.12f);
                rail.transform.localPosition = new Vector3(0f, 0.95f, -0.42f);
                Paint(rail, wood);

                var pen = root.AddComponent<LivestockPen>();
                pen.milkItem = milk;
                pen.woolItem = wool;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 87] Created " + path + ".");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;
            var existingPen = contents.GetComponent<LivestockPen>();
            if (existingPen == null)
            {
                existingPen = contents.AddComponent<LivestockPen>();
                dirty = true;
                Debug.Log("[Setup 87] Pen prefab had no LivestockPen component; added one.");
            }

            // Only fill in a MISSING reference. An authored override is never replaced.
            if (existingPen.milkItem == null && milk != null) { existingPen.milkItem = milk; dirty = true; }
            if (existingPen.woolItem == null && wool != null) { existingPen.woolItem = wool; dirty = true; }

            GameObject result = existing;
            if (dirty) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            changed = dirty;
            return result;
        }

        private static bool EnsureRecipe(RecipeRegistry registry, BlockItem block)
        {
            const string stem = "Recipe_LivestockPen";
            var recipe = FindRecipe(stem);
            bool changed = false;

            var wood = FindItem("Item_WoodenPlank") ?? FindItem("Item_WoodLog");
            var steel = FindItem("Item_SteelIngot");

            RecipeIngredient[] Inputs()
            {
                var list = new System.Collections.Generic.List<RecipeIngredient>();
                if (wood != null) list.Add(new RecipeIngredient { item = wood, count = 40 });
                if (steel != null) list.Add(new RecipeIngredient { item = steel, count = 8 });
                return list.ToArray();
            }

            if (recipe == null)
            {
                if (wood == null && steel == null)
                {
                    Debug.LogWarning("[Setup 87] No wood or steel item resolved; pen recipe skipped.");
                    return false;
                }

                EnsureFolder(RecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = "Livestock Pen";
                recipe.requiredStation = StationTier.CraftingBench;
                recipe.craftSeconds = 10f;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = block;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, RecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 87] Created " + stem + ".");
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
                Debug.Log("[Setup 87] Added " + stem + " to RecipeRegistry.");
                changed = true;
            }
            return changed;
        }

        // ============================================================
        //                        Helpers
        // ============================================================
        private static ItemDefinition FindItemByItemId(string itemId)
        {
            var guids = AssetDatabase.FindAssets("t:ItemDefinition");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var candidate = AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
                if (candidate != null && candidate.itemId == itemId) return candidate;
            }
            return null;
        }

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

        private static Material MakeColoredMat(string folder, string name, Color c)
        {
            string path = folder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                Debug.LogError("[Setup 87] Preserved conflicting asset at '" + path + "'.");
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
