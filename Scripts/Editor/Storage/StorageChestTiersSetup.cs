#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using Object = UnityEngine.Object;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 77 (11.1.0-dev): build the storage chest tier progression.
    ///
    /// The game ships a single 30-slot Chest (planks x8, Crafting Bench). This step adds
    /// the tiers around it that the roadmap planned — a cheap early-game Wooden Crate,
    /// a mid-game Iron Chest, and an end-game Steel Chest — as three more authored
    /// blocks on the SAME <see cref="Chest"/> component, so each tier gets the port
    /// configuration, the belt/pipe plumbing and the save/restore the chest already has.
    ///
    /// Non-destructive by construction: an existing prefab, block item or recipe of the
    /// step's own is only created when missing; when it exists, a missing Chest component
    /// is added, a null or wrong slot count / display name is corrected, and a broken
    /// recipe link (null output or input item) is re-linked — but authored craft times,
    /// quantities, health and icons are never reset. The pre-existing Chest is never
    /// touched. Every change is logged with the [Setup 77] prefix.
    /// </summary>
    public static class StorageChestTiersSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string StationsFolder = Root + "/StationPrefabs";
        private const string BlocksFolder   = Root + "/Blocks";
        private const string RecipesFolder  = Root + "/Recipes";

        private const string PlankPath     = Root + "/Items/Item_WoodenPlank.asset";
        private const string IronIngotPath = Root + "/Items/Item_IronIngot.asset";
        private const string SteelIngotPath = Root + "/Items/Item_SteelIngot.asset";

        /// <summary>One tier of the progression. Inputs reference the resolved items, so a
        /// null ingredient (a world where step 4 never ran) is caught before anything is written.</summary>
        private struct Tier
        {
            public string              assetName;    // asset/prefab stem, e.g. "WoodenCrate"
            public string              displayName;  // player-facing, e.g. "Wooden Crate"
            public int                 size;
            public Color               tint;
            public string              description;
            public StationTier         station;
            public float               craftSeconds;
            public (ItemDefinition item, int count)[] inputs;
        }

        public static void RunStep77()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Storage Chest Tiers",
                    "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Storage Chest Tiers",
                        "Run step 4 (Build Crafting Content) first — RecipeRegistry.asset doesn't exist yet.", "OK");
                    return;
                }

                var plank     = AssetDatabase.LoadAssetAtPath<ItemDefinition>(PlankPath);
                var ironIngot = AssetDatabase.LoadAssetAtPath<ItemDefinition>(IronIngotPath);
                var steelIngot = AssetDatabase.LoadAssetAtPath<ItemDefinition>(SteelIngotPath);
                if (plank == null || ironIngot == null || steelIngot == null)
                {
                    Debug.LogError("[Setup 77] A tier ingredient did not resolve (plank: " + (plank != null) +
                                   ", iron ingot: " + (ironIngot != null) +
                                   ", steel ingot: " + (steelIngot != null) +
                                   "). Run step 4 (Build Crafting Content) first, then run this step again.");
                    EditorUtility.DisplayDialog("Storage Chest Tiers",
                        "A tier ingredient is missing.\n\n" +
                        "Run step 4 (Build Crafting Content) first, then run this step again.", "OK");
                    return;
                }

                var tiers = new[]
                {
                    new Tier
                    {
                        assetName    = "WoodenCrate",
                        displayName  = "Wooden Crate",
                        size         = 9,
                        tint         = new Color(0.45f, 0.32f, 0.18f),
                        description  = "9-slot storage. Crafted from planks straight in your inventory.",
                        station      = StationTier.None,
                        craftSeconds = 0f,
                        inputs       = new[] { ((ItemDefinition)plank, 4) }
                    },
                    new Tier
                    {
                        assetName    = "IronChest",
                        displayName  = "Iron Chest",
                        size         = 18,
                        tint         = new Color(0.45f, 0.47f, 0.52f),
                        description  = "18-slot storage. Crafted at the Crafting Bench from iron ingots and planks.",
                        station      = StationTier.CraftingBench,
                        craftSeconds = 2f,
                        inputs       = new[] { ((ItemDefinition)ironIngot, 4), ((ItemDefinition)plank, 2) }
                    },
                    new Tier
                    {
                        assetName    = "SteelChest",
                        displayName  = "Steel Chest",
                        size         = 36,
                        tint         = new Color(0.30f, 0.38f, 0.50f),
                        description  = "36-slot storage. Crafted at the Assembler from steel and iron ingots.",
                        station      = StationTier.Assembler,
                        craftSeconds = 4f,
                        inputs       = new[] { ((ItemDefinition)steelIngot, 4), ((ItemDefinition)ironIngot, 4) }
                    }
                };

                int changedTiers = 0;
                foreach (var t in tiers)
                {
                    var prefab = GetOrCreateChestPrefab(t, out bool prefabChanged);
                    EnsureBlockItem(t, prefab, out bool blockChanged);
                    EnsureRecipe(registry, t, out bool recipeChanged);
                    if (prefabChanged || blockChanged || recipeChanged) changedTiers++;
                    Debug.Log("[Setup 77] " + t.displayName + ": " + t.size + " slots, " +
                              DescribeInputs(t) + " — station " + t.station +
                              (prefabChanged ? ", prefab corrected" : ", prefab kept as authored") +
                              (recipeChanged ? ", recipe corrected" : ", recipe kept as authored") + ".");
                }
                Debug.Log("[Setup 77] Storage chest tiers ready: " +
                          (changedTiers == 0
                              ? "all three tiers already present and correct, nothing written."
                              : changedTiers + " of 3 tier(s) created or corrected."));

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("Storage Chest Tiers",
                    "Storage chest tiers ready.\n\n" +
                    "  Wooden Crate   9 slots   — planks x4, in your inventory\n" +
                    "  Iron Chest    18 slots   — iron ingot x4 + planks x2, Crafting Bench\n" +
                    "  Steel Chest   36 slots   — steel ingot x4 + iron ingot x4, Assembler\n\n" +
                    "The pre-existing 30-slot Chest was left untouched. Missing content was created, " +
                    "broken links were repaired, authored values were never reset. " +
                    "See the console for every change.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Storage Chest Tiers",
                    "Setup stopped: " + ex.Message + "\n\nNothing was written.", "OK");
            }
        }

        private static string DescribeInputs(Tier t)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var (item, count) in t.inputs)
                parts.Add((item != null ? item.displayName : "?") + " x" + count);
            return string.Join(" + ", parts);
        }

        // ============================================================
        //                       Prefab
        // ============================================================
        /// <summary>
        /// Create the tier prefab, or repair the existing one. Repairs are strictly
        /// additive: add a missing Chest component, correct a slot count or display
        /// name that cannot be right, and leave every other authored property alone.
        /// Returns true when the prefab on disk changed.
        /// </summary>
        private static GameObject GetOrCreateChestPrefab(Tier t, out bool changed)
        {
            string path = $"{StationsFolder}/{t.assetName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(StationsFolder);
                var root = new GameObject(t.assetName);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Mesh";
                cube.transform.SetParent(root.transform, false);
                cube.transform.localScale = new Vector3(1f, 0.7f, 0.7f);
                var mat = MakeColoredMat(StationsFolder, $"Mat_{t.assetName}", t.tint);
                if (mat != null) cube.GetComponent<Renderer>().sharedMaterial = mat;
                var newChest = root.AddComponent<Chest>();
                newChest.size = t.size;
                newChest.displayName = t.displayName;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 77] Created " + path + " (" + t.size + " slots).");
                return prefab;
            }

            // Existing prefab: repair in place, save only when something changed.
            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;
            var chest = contents.GetComponent<Chest>();
            if (chest == null)
            {
                chest = contents.AddComponent<Chest>();
                chest.size = t.size;
                chest.displayName = t.displayName;
                dirty = true;
                Debug.Log("[Setup 77] " + t.assetName + " prefab had no Chest component; added one (" + t.size + " slots).");
            }
            else
            {
                if (chest.size != t.size)
                {
                    chest.size = t.size;
                    dirty = true;
                    Debug.Log("[Setup 77] " + t.assetName + " prefab slot count corrected to " + t.size + ".");
                }
                if (!string.Equals(chest.displayName, t.displayName, StringComparison.Ordinal))
                {
                    chest.displayName = t.displayName;
                    dirty = true;
                    Debug.Log("[Setup 77] " + t.assetName + " prefab display name corrected to \"" + t.displayName + "\".");
                }
            }
            if (dirty) PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            changed = dirty;
            return existing;
        }

        // ============================================================
        //                     Block item
        // ============================================================
        /// <summary>Create or repair the placeable block item. Authored values are kept;
        /// only a missing placed-prefab reference or display metadata is filled in.</summary>
        private static void EnsureBlockItem(Tier t, GameObject prefab, out bool changed)
        {
            string path = $"{BlocksFolder}/Block_{t.assetName}.asset";
            var b = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool created = false;
            if (b == null)
            {
                EnsureFolder(BlocksFolder);
                b = ScriptableObject.CreateInstance<BlockItem>();
                created = true;
            }
            bool dirty = created;

            if (b.itemId != t.assetName.ToLower()) { b.itemId = t.assetName.ToLower(); dirty = true; }
            if (b.displayName != t.displayName) { b.displayName = t.displayName; dirty = true; }
            if (b.maxStack <= 0) { b.maxStack = 99; dirty = true; }
            if (b.massPerUnit <= 0f) { b.massPerUnit = 4f; dirty = true; }
            if (b.blockHealth <= 0) { b.blockHealth = 200; dirty = true; }
            if (b.miningTier <= 0) { b.miningTier = 1; dirty = true; }
            if (b.category != "Storage") { b.category = "Storage"; dirty = true; }
            if (string.IsNullOrEmpty(b.description)) { b.description = t.description; dirty = true; }
            if (b.icon == null) b.iconTint = t.tint;   // tint only — icons bind via the icon sync

            // The placed-prefab reference is what the game instantiates. It is the one
            // link that is safe to overwrite: a null or stale reference makes the block
            // unplaceable, so point it at the prefab this step just verified.
            if (b.placedPrefab == null || b.placedPrefab != prefab)
            {
                b.placedPrefab = prefab;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(b)) AssetDatabase.CreateAsset(b, path);
                EditorUtility.SetDirty(b);
                Debug.Log("[Setup 77] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }
            changed = dirty;
        }

        // ============================================================
        //                       Recipe
        // ============================================================
        /// <summary>
        /// Find the tier's recipe in the registry by asset name. When it exists, its
        /// authored quantities and craft time are kept — only a missing registry
        /// membership or a null link (the failure mode that made the smelting recipes
        /// in 11.0.0-dev) is repaired. When it doesn't, it is authored from the tier.
        /// </summary>
        private static void EnsureRecipe(RecipeRegistry registry, Tier t, out bool changed)
        {
            string stem = "Recipe_" + t.assetName;
            var recipe = FindRecipe(stem);

            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                string path = $"{RecipesFolder}/{stem}.asset";
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                ApplyDefinition(recipe, t);
                AssetDatabase.CreateAsset(recipe, path);
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 77] Created " + path + " (" + DescribeInputs(t) + " -> " +
                          t.displayName + ", " + t.station + ").");
            }
            else
            {
                bool dirty = false;
                // Re-link only what is broken: a null output or a null ingredient.
                var block = AssetDatabase.LoadAssetAtPath<BlockItem>($"{BlocksFolder}/Block_{t.assetName}.asset");
                if (recipe.outputItem == null && block != null)
                {
                    recipe.outputItem = block;
                    recipe.outputCount = 1;
                    dirty = true;
                }
                if (recipe.inputs == null || recipe.inputs.Length == 0)
                {
                    ApplyInputs(recipe, t);
                    dirty = true;
                }
                else
                {
                    for (int i = 0; i < recipe.inputs.Length; i++)
                    {
                        if (recipe.inputs[i].item == null || recipe.inputs[i].count <= 0)
                        {
                            if (i < t.inputs.Length && t.inputs[i].item != null)
                            {
                                recipe.inputs[i] = new RecipeIngredient { item = t.inputs[i].item, count = t.inputs[i].count };
                                dirty = true;
                            }
                        }
                    }
                }
                if (dirty) EditorUtility.SetDirty(recipe);
                changed = dirty;
                if (dirty) Debug.Log("[Setup 77] Repaired broken links on " + stem + ".");
                if (!dirty) Debug.Log("[Setup 77] " + stem + " kept as authored.");
            }

            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                Debug.Log("[Setup 77] Added " + stem + " to RecipeRegistry.");
            }
            if (recipe.outputItem == null)
            {
                Debug.LogWarning("[Setup 77] " + stem + " has no resolvable output item — the block item " +
                                 "it points at is missing. The recipe is registered but will not appear in the UI.");
            }
        }

        private static void ApplyDefinition(RecipeDefinition r, Tier t)
        {
            r.displayName = t.displayName;
            r.requiredStation = t.station;
            r.craftSeconds = t.craftSeconds;
            r.unlockedByDefault = true;
            r.outputCount = 1;
            // The output is the block item that was just authored — resolve by path so
            // the reference survives across the create/repair order above.
            r.outputItem = AssetDatabase.LoadAssetAtPath<BlockItem>($"{BlocksFolder}/Block_{t.assetName}.asset");
            ApplyInputs(r, t);
        }

        private static void ApplyInputs(RecipeDefinition r, Tier t)
        {
            var valid = new System.Collections.Generic.List<RecipeIngredient>();
            foreach (var (item, count) in t.inputs)
                if (item != null && count > 0)
                    valid.Add(new RecipeIngredient { item = item, count = count });
            r.inputs = valid.ToArray();
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
        //                      Helpers
        // ============================================================
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            var leaf   = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent); // recurse up — ensure parent exists first
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>Idempotent colored material for the tier's box mesh.</summary>
        private static Material MakeColoredMat(string folder, string name, Color c)
        {
            string path = $"{folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                Debug.LogError("[Setup 77] Preserved conflicting asset at '" + path + "'.");
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
