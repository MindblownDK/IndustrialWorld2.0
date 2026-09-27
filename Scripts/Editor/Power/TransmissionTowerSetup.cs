#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 83 (11.11.0-dev): author the High-Voltage Transmission Tower.
    ///
    /// Cables only link one grid step at a time, which keeps base wiring readable but made a
    /// remote site impossible to power without dragging hundreds of blocks across the world.
    /// This is the same gap the drone ports closed for items, and it matters more now that the
    /// ports themselves need power at both ends of a 400 m link.
    ///
    /// A tower is a <see cref="PowerNode"/> that taps its own base like a relay and spans to
    /// another tower up to 128 m away. The span is a capacity-rated manual link, so the
    /// existing network merge, bottleneck rule and UI all keep working with no changes.
    ///
    /// Non-destructive: a missing prefab, block item or recipe is created; an existing one is
    /// only corrected where it cannot be right (missing component, null recipe link, missing
    /// registry membership). Authored tuning (span range, capacity, max spans), craft times,
    /// health and icons are never reset. Safe to re-run. Logs with the [Setup 83] prefix.
    /// </summary>
    public static class TransmissionTowerSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string StationsFolder = Root + "/StationPrefabs";
        private const string BlocksFolder   = Root + "/Blocks";
        private const string RecipesFolder  = Root + "/Recipes";

        private const string AssetName   = "TransmissionTower";
        private const string DisplayName = "Transmission Tower";

        private static readonly Color Tint = new(0.62f, 0.64f, 0.68f);

        private const string SteelPath = Root + "/Items/Item_SteelIngot.asset";
        private const string WirePath  = Root + "/Industrial/Items/Item_CopperWire.asset";

        public static void RunStep83()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Transmission Tower", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Transmission Tower",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.", "OK");
                    return;
                }

                var steel = AssetDatabase.LoadAssetAtPath<ItemDefinition>(SteelPath);
                var wire  = AssetDatabase.LoadAssetAtPath<ItemDefinition>(WirePath);
                if (steel == null || wire == null)
                {
                    Debug.LogError("[Setup 83] An ingredient did not resolve (steel: " + (steel != null) +
                                   ", copper wire: " + (wire != null) + "). Run the earlier content steps first.");
                    EditorUtility.DisplayDialog("Transmission Tower",
                        "An ingredient is missing.\n\nRun the earlier crafting-content steps first, then run this step again.", "OK");
                    return;
                }

                var prefab = GetOrCreatePrefab(out bool prefabChanged);
                EnsureBlockItem(prefab, out bool blockChanged);
                EnsureRecipe(registry, steel, wire, out bool recipeChanged);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                bool any = prefabChanged || blockChanged || recipeChanged;
                Debug.Log("[Setup 83] Transmission Tower setup complete. " +
                          (any ? "Changes were written." : "Everything was already in place."));

                EditorUtility.DisplayDialog("Step 83 - Transmission Tower",
                    "Transmission Tower authored.\n\n" +
                    "  Span range     128 m tower to tower\n" +
                    "  Span capacity  20 kW\n" +
                    "  Local tap      4 m (picks up cables and machines at its base)\n" +
                    "  Max spans      3 (two makes a line, three makes a junction)\n\n" +
                    "Build a tower at each site. Two within 128 m link automatically and merge " +
                    "their grids, so a remote outpost joins the home network without a cable run.\n\n" +
                    (any ? "Changes were written. See the Console." : "Everything was already in place."),
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 83] Aborted: " + ex);
                EditorUtility.DisplayDialog("Transmission Tower",
                    "Setup stopped: " + ex.Message + "\n\nNothing was written.", "OK");
            }
        }

        // ============================================================
        //                        Prefab
        // ============================================================
        private static GameObject GetOrCreatePrefab(out bool changed)
        {
            string path = $"{StationsFolder}/{AssetName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(StationsFolder);
                var root = new GameObject(AssetName);

                var mat = MakeColoredMat(StationsFolder, $"Mat_{AssetName}", Tint);

                // A lattice pylon: four splayed legs, a tall mast and two cross-arms.
                for (int i = 0; i < 4; i++)
                {
                    float sx = (i is 0 or 1) ? 1f : -1f;
                    float sz = (i is 0 or 2) ? 1f : -1f;

                    var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    leg.name = "Leg" + i;
                    leg.transform.SetParent(root.transform, false);
                    leg.transform.localPosition = new Vector3(0.22f * sx, 0.6f, 0.22f * sz);
                    leg.transform.localScale = new Vector3(0.09f, 2.2f, 0.09f);
                    leg.transform.localRotation = Quaternion.Euler(4f * sz, 0f, -4f * sx);
                    Paint(leg, mat);
                }

                var mast = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mast.name = "Mast";
                mast.transform.SetParent(root.transform, false);
                mast.transform.localPosition = new Vector3(0f, 2.3f, 0f);
                mast.transform.localScale = new Vector3(0.20f, 1.6f, 0.20f);
                Paint(mast, mat);

                for (int a = 0; a < 2; a++)
                {
                    var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    arm.name = "CrossArm" + a;
                    arm.transform.SetParent(root.transform, false);
                    arm.transform.localPosition = new Vector3(0f, 2.55f + a * 0.55f, 0f);
                    arm.transform.localScale = new Vector3(1.5f - a * 0.45f, 0.08f, 0.10f);
                    Paint(arm, mat);
                }

                var tower = root.AddComponent<TransmissionTower>();
                tower.connectRadius = tower.localTapRadius;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 83] Created " + path + ".");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;

            if (contents.GetComponent<TransmissionTower>() == null)
            {
                contents.AddComponent<TransmissionTower>();
                dirty = true;
                Debug.Log("[Setup 83] Prefab had no TransmissionTower component; added one.");
            }

            GameObject result = existing;
            if (dirty) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);

            changed = dirty;
            Debug.Log("[Setup 83] " + AssetName + " prefab " + (dirty ? "corrected." : "kept as authored."));
            return result;
        }

        private static void Paint(GameObject go, Material mat)
        {
            if (mat == null) return;
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
        }

        // ============================================================
        //                      Block item
        // ============================================================
        private static void EnsureBlockItem(GameObject prefab, out bool changed)
        {
            string path = $"{BlocksFolder}/Block_{AssetName}.asset";
            var b = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool created = false;
            if (b == null)
            {
                EnsureFolder(BlocksFolder);
                b = ScriptableObject.CreateInstance<BlockItem>();
                created = true;
            }
            bool dirty = created;

            if (b.itemId != AssetName.ToLower()) { b.itemId = AssetName.ToLower(); dirty = true; }
            if (b.displayName != DisplayName) { b.displayName = DisplayName; dirty = true; }
            if (b.maxStack <= 0) { b.maxStack = 99; dirty = true; }
            if (b.massPerUnit <= 0f) { b.massPerUnit = 18f; dirty = true; }
            if (b.blockHealth <= 0) { b.blockHealth = 300; dirty = true; }
            if (b.miningTier <= 0) { b.miningTier = 1; dirty = true; }
            if (b.category != "Power") { b.category = "Power"; dirty = true; }
            if (string.IsNullOrEmpty(b.description))
            {
                b.description = "High-voltage pylon. Two towers within 128 m link automatically and " +
                                "carry power between them, joining a remote site to the home grid.";
                dirty = true;
            }
            if (b.icon == null) b.iconTint = Tint;

            if (b.placedPrefab == null || b.placedPrefab != prefab)
            {
                b.placedPrefab = prefab;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(b)) AssetDatabase.CreateAsset(b, path);
                EditorUtility.SetDirty(b);
                Debug.Log("[Setup 83] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }
            changed = dirty;
        }

        // ============================================================
        //                        Recipe
        // ============================================================
        private static void EnsureRecipe(RecipeRegistry registry, ItemDefinition steel,
                                         ItemDefinition wire, out bool changed)
        {
            const string stem = "Recipe_" + AssetName;
            var recipe = FindRecipe(stem);
            var block = AssetDatabase.LoadAssetAtPath<BlockItem>($"{BlocksFolder}/Block_{AssetName}.asset");

            RecipeIngredient[] Inputs() => new[]
            {
                new RecipeIngredient { item = steel, count = 8 },
                new RecipeIngredient { item = wire,  count = 4 },
            };

            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                string path = $"{RecipesFolder}/{stem}.asset";
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = DisplayName;
                recipe.requiredStation = StationTier.CraftingBench;
                recipe.craftSeconds = 4f;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = block;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, path);
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 83] Created " + path + " (Steel x8 + Copper Wire x4 -> " + DisplayName + ").");
            }
            else
            {
                bool dirty = false;
                if (recipe.outputItem == null && block != null)
                {
                    recipe.outputItem = block;
                    recipe.outputCount = 1;
                    dirty = true;
                }
                if (recipe.inputs == null || recipe.inputs.Length == 0)
                {
                    recipe.inputs = Inputs();
                    dirty = true;
                }
                if (dirty) EditorUtility.SetDirty(recipe);
                changed = dirty;
                Debug.Log("[Setup 83] " + stem + (dirty ? " had broken links repaired." : " kept as authored."));
            }

            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                Debug.Log("[Setup 83] Added " + stem + " to RecipeRegistry.");
            }
            if (recipe.outputItem == null)
            {
                Debug.LogWarning("[Setup 83] " + stem + " has no resolvable output item. The recipe is " +
                                 "registered but will not appear in the UI.");
            }
        }

        private static RecipeDefinition FindRecipe(string stem)
        {
            var guids = AssetDatabase.FindAssets($"{stem} t:RecipeDefinition");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == stem)
                    return AssetDatabase.LoadAssetAtPath<RecipeDefinition>(p);
            }
            return null;
        }

        // ============================================================
        //                        Helpers
        // ============================================================
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            var leaf   = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static Material MakeColoredMat(string folder, string name, Color c)
        {
            string path = $"{folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                Debug.LogError("[Setup 83] Preserved conflicting asset at '" + path + "'.");
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
