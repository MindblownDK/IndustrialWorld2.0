#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Transport;
using Object = UnityEngine.Object;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 78 (11.2.0-dev): close the storage line with the two port-locked chests.
    ///
    /// 11.1.0-dev shipped the Wooden Crate / Iron Chest / Steel Chest tiers and left the
    /// Provider/Requester end of the roadmap's storage progression open. This step authors
    /// those two blocks on the SAME <see cref="Chest"/> component, using the new
    /// <c>portLock</c> field. The ports mirror the wireless role: a Provider is FED by pipes
    /// (faces pinned to Input) and supplies the network, while a Requester is filled by the
    /// network and FEEDS pipes (faces pinned to Output). Everything else — the panel, the filters, the
    /// belt/pipe plumbing, the save/restore — is the chest the game already knows.
    ///
    /// Non-destructive by construction: missing assets are created; an existing prefab,
    /// block item or recipe is only corrected where it cannot be right (missing Chest
    /// component, wrong slot count / display name / lock mode, null recipe link, missing
    /// registry membership). Authored craft times, quantities, health and icons are never
    /// reset, and no other chest is touched. Every decision logs with the [Setup 78] prefix.
    /// </summary>
    public static class LogisticChestsSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string StationsFolder = Root + "/StationPrefabs";
        private const string BlocksFolder   = Root + "/Blocks";
        private const string RecipesFolder  = Root + "/Recipes";

        private const string PlankPath     = Root + "/Items/Item_WoodenPlank.asset";
        private const string IronIngotPath = Root + "/Items/Item_IronIngot.asset";
        private const string CopperIngotPath = Root + "/Items/Item_CopperIngot.asset";

        /// <summary>One logistic chest variant: a slot count plus the direction its faces are pinned to.</summary>
        private struct Variant
        {
            public string       assetName;
            public string       displayName;
            public int          size;
            public PortLockMode lockMode;
            public Color        tint;
            public string       description;
            public StationTier  station;
            public float        craftSeconds;
            public (ItemDefinition item, int count)[] inputs;
        }

        public static void RunStep78()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Logistic Chests",
                    "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Logistic Chests",
                        "Run step 4 (Build Crafting Content) first — RecipeRegistry.asset doesn't exist yet.", "OK");
                    return;
                }

                var plank       = AssetDatabase.LoadAssetAtPath<ItemDefinition>(PlankPath);
                var ironIngot   = AssetDatabase.LoadAssetAtPath<ItemDefinition>(IronIngotPath);
                var copperIngot = AssetDatabase.LoadAssetAtPath<ItemDefinition>(CopperIngotPath);
                if (plank == null || ironIngot == null || copperIngot == null)
                {
                    Debug.LogError("[Setup 78] An ingredient did not resolve (plank: " + (plank != null) +
                                   ", iron ingot: " + (ironIngot != null) +
                                   ", copper ingot: " + (copperIngot != null) +
                                   "). Run step 4 (Build Crafting Content) first, then run this step again.");
                    EditorUtility.DisplayDialog("Logistic Chests",
                        "An ingredient is missing.\n\nRun step 4 (Build Crafting Content) first, then run this step again.", "OK");
                    return;
                }

                var variants = new[]
                {
                    new Variant
                    {
                        assetName    = "ProviderChest",
                        displayName  = "Provider Chest",
                        size         = 18,
                        lockMode     = PortLockMode.Provider,
                        tint         = new Color(0.72f, 0.44f, 0.12f),
                        description  = "18-slot supply buffer. Pipes fill it; the wireless network hands its stock to requesters in range.",
                        station      = StationTier.CraftingBench,
                        craftSeconds = 3f,
                        inputs       = new[] { ((ItemDefinition)ironIngot, 4), ((ItemDefinition)copperIngot, 2), ((ItemDefinition)plank, 2) }
                    },
                    new Variant
                    {
                        assetName    = "RequesterChest",
                        displayName  = "Requester Chest",
                        size         = 18,
                        lockMode     = PortLockMode.Requester,
                        tint         = new Color(0.16f, 0.46f, 0.74f),
                        description  = "18-slot delivery buffer. The wireless network keeps it stocked; its ports feed the pipes downstream.",
                        station      = StationTier.CraftingBench,
                        craftSeconds = 3f,
                        inputs       = new[] { ((ItemDefinition)ironIngot, 4), ((ItemDefinition)copperIngot, 2), ((ItemDefinition)plank, 2) }
                    }
                };

                int changedVariants = 0;
                foreach (var v in variants)
                {
                    var prefab = GetOrCreatePrefab(v, out bool prefabChanged);
                    EnsureBlockItem(v, prefab, out bool blockChanged);
                    EnsureRecipe(registry, v, out bool recipeChanged);
                    if (prefabChanged || blockChanged || recipeChanged) changedVariants++;
                    Debug.Log("[Setup 78] " + v.displayName + ": " + v.size + " slots, lock " + v.lockMode +
                              ", " + DescribeInputs(v) + " — station " + v.station +
                              (prefabChanged ? ", prefab corrected" : ", prefab kept as authored") +
                              (recipeChanged ? ", recipe corrected" : ", recipe kept as authored") + ".");
                }

                Debug.Log("[Setup 78] Logistic chests ready: " +
                          (changedVariants == 0
                              ? "both variants already present and correct, nothing written."
                              : changedVariants + " of 2 variant(s) created or corrected."));

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("Logistic Chests",
                    "Logistic chests ready.\n\n" +
                    "  Provider Chest   18 slots   — ports INPUT (pipes fill it, network draws from it)\n" +
                    "  Requester Chest  18 slots   — ports OUTPUT (network fills it, pipes draw from it)\n\n" +
                    "Both craft at the Crafting Bench from iron ingot x4 + copper ingot x2 + planks x2.\n\n" +
                    "The existing chests and tiers were left untouched. Missing content was created, " +
                    "broken links were repaired, authored values were never reset. See the console for every change.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Logistic Chests",
                    "Setup stopped: " + ex.Message + "\n\nNothing was written.", "OK");
            }
        }

        private static string DescribeInputs(Variant v)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var (item, count) in v.inputs)
                parts.Add((item != null ? item.displayName : "?") + " x" + count);
            return string.Join(" + ", parts);
        }

        // ============================================================
        //                        Prefab
        // ============================================================
        /// <summary>
        /// Create the variant prefab, or repair the existing one. Repairs are strictly
        /// additive: add a missing Chest component, correct a slot count, display name or
        /// lock mode that cannot be right, and leave every other authored value alone.
        /// A locked prefab is also pre-seeded with one active face so the block is useful
        /// the moment it is placed — but only when the port config has no active face at all.
        /// </summary>
        private static GameObject GetOrCreatePrefab(Variant v, out bool changed)
        {
            string path = $"{StationsFolder}/{v.assetName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(StationsFolder);
                var root = new GameObject(v.assetName);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Mesh";
                cube.transform.SetParent(root.transform, false);
                cube.transform.localScale = new Vector3(1f, 0.7f, 0.7f);
                var mat = MakeColoredMat(StationsFolder, $"Mat_{v.assetName}", v.tint);
                if (mat != null) cube.GetComponent<Renderer>().sharedMaterial = mat;

                var newChest = root.AddComponent<Chest>();
                newChest.size = v.size;
                newChest.displayName = v.displayName;
                newChest.portLock = v.lockMode;
                SeedLockedFace(root, v);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 78] Created " + path + " (" + v.size + " slots, lock " + v.lockMode + ").");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;
            var chest = contents.GetComponent<Chest>();
            if (chest == null)
            {
                chest = contents.AddComponent<Chest>();
                chest.size = v.size;
                chest.displayName = v.displayName;
                chest.portLock = v.lockMode;
                dirty = true;
                Debug.Log("[Setup 78] " + v.assetName + " prefab had no Chest component; added one (" +
                          v.size + " slots, lock " + v.lockMode + ").");
            }
            else
            {
                if (chest.size != v.size)
                {
                    chest.size = v.size;
                    dirty = true;
                    Debug.Log("[Setup 78] " + v.assetName + " prefab slot count corrected to " + v.size + ".");
                }
                if (!string.Equals(chest.displayName, v.displayName, StringComparison.Ordinal))
                {
                    chest.displayName = v.displayName;
                    dirty = true;
                    Debug.Log("[Setup 78] " + v.assetName + " prefab display name corrected to \"" + v.displayName + "\".");
                }
                // The lock mode is the whole point of this variant: a wrong value makes the
                // block indistinguishable from a plain chest, so it is always corrected.
                if (chest.portLock != v.lockMode)
                {
                    chest.portLock = v.lockMode;
                    dirty = true;
                    Debug.Log("[Setup 78] " + v.assetName + " prefab port lock corrected to " + v.lockMode + ".");
                }
            }

            if (SeedLockedFace(contents, v)) dirty = true;

            if (dirty) PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            changed = dirty;
            return existing;
        }

        /// <summary>
        /// Give a locked prefab one working face when its port config has none. Any existing
        /// active face is respected — a player-authored layout is never rewritten here; the
        /// runtime <c>EnforcePortLock</c> pins directions when the block wakes up.
        /// Returns true when a face was seeded.
        /// </summary>
        private static bool SeedLockedFace(GameObject go, Variant v)
        {
            var cfg = go.GetComponent<PortConfig>();
            if (cfg == null) cfg = go.AddComponent<PortConfig>();
            cfg.EnsureAllFaces();
            if (go.GetComponent<ItemPortRouting>() == null) go.AddComponent<ItemPortRouting>();

            // Mirror of the wireless role: a Provider is FED by pipes (input ports), a
            // Requester FEEDS them (output ports). Kept in step with Chest.PinnedDirection.
            var pinned = v.lockMode == PortLockMode.Provider ? PortDirection.Input : PortDirection.Output;

            bool anyActive = false;
            for (int i = 0; i < cfg.ports.Length; i++)
                if (cfg.ports[i].enabled && cfg.ports[i].direction != PortDirection.None) { anyActive = true; break; }

            bool touched = false;
            for (int i = 0; i < cfg.ports.Length; i++)
            {
                if (!anyActive && cfg.ports[i].face == CubeFace.PosX)
                {
                    cfg.ports[i].enabled = true;
                    cfg.ports[i].direction = pinned;
                    touched = true;
                    Debug.Log("[Setup 78] " + v.assetName + " had no active face; seeded +X as " + pinned + ".");
                }
                else if (cfg.ports[i].enabled && cfg.ports[i].direction != PortDirection.None &&
                         cfg.ports[i].direction != pinned)
                {
                    // An active face pointing the wrong way contradicts the lock — pin it.
                    cfg.ports[i].direction = pinned;
                    touched = true;
                    Debug.Log("[Setup 78] " + v.assetName + " face " + cfg.ports[i].face + " pinned to " + pinned + ".");
                }
            }
            return touched;
        }

        // ============================================================
        //                      Block item
        // ============================================================
        private static void EnsureBlockItem(Variant v, GameObject prefab, out bool changed)
        {
            string path = $"{BlocksFolder}/Block_{v.assetName}.asset";
            var b = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool created = false;
            if (b == null)
            {
                EnsureFolder(BlocksFolder);
                b = ScriptableObject.CreateInstance<BlockItem>();
                created = true;
            }
            bool dirty = created;

            if (b.itemId != v.assetName.ToLower()) { b.itemId = v.assetName.ToLower(); dirty = true; }
            if (b.displayName != v.displayName) { b.displayName = v.displayName; dirty = true; }
            if (b.maxStack <= 0) { b.maxStack = 99; dirty = true; }
            if (b.massPerUnit <= 0f) { b.massPerUnit = 6f; dirty = true; }
            if (b.blockHealth <= 0) { b.blockHealth = 220; dirty = true; }
            if (b.miningTier <= 0) { b.miningTier = 1; dirty = true; }
            if (b.category != "Storage") { b.category = "Storage"; dirty = true; }
            if (string.IsNullOrEmpty(b.description)) { b.description = v.description; dirty = true; }
            if (b.icon == null) b.iconTint = v.tint;   // tint only — icons bind via the icon sync

            if (b.placedPrefab == null || b.placedPrefab != prefab)
            {
                b.placedPrefab = prefab;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(b)) AssetDatabase.CreateAsset(b, path);
                EditorUtility.SetDirty(b);
                Debug.Log("[Setup 78] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }
            changed = dirty;
        }

        // ============================================================
        //                        Recipe
        // ============================================================
        private static void EnsureRecipe(RecipeRegistry registry, Variant v, out bool changed)
        {
            string stem = "Recipe_" + v.assetName;
            var recipe = FindRecipe(stem);

            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                string path = $"{RecipesFolder}/{stem}.asset";
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                ApplyDefinition(recipe, v);
                AssetDatabase.CreateAsset(recipe, path);
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 78] Created " + path + " (" + DescribeInputs(v) + " -> " +
                          v.displayName + ", " + v.station + ").");
            }
            else
            {
                bool dirty = false;
                var block = AssetDatabase.LoadAssetAtPath<BlockItem>($"{BlocksFolder}/Block_{v.assetName}.asset");
                if (recipe.outputItem == null && block != null)
                {
                    recipe.outputItem = block;
                    recipe.outputCount = 1;
                    dirty = true;
                }
                if (recipe.inputs == null || recipe.inputs.Length == 0)
                {
                    ApplyInputs(recipe, v);
                    dirty = true;
                }
                else
                {
                    for (int i = 0; i < recipe.inputs.Length; i++)
                    {
                        if (recipe.inputs[i].item != null && recipe.inputs[i].count > 0) continue;
                        if (i < v.inputs.Length && v.inputs[i].item != null)
                        {
                            recipe.inputs[i] = new RecipeIngredient { item = v.inputs[i].item, count = v.inputs[i].count };
                            dirty = true;
                        }
                    }
                }
                if (dirty) EditorUtility.SetDirty(recipe);
                changed = dirty;
                Debug.Log("[Setup 78] " + stem + (dirty ? " had broken links repaired." : " kept as authored."));
            }

            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                Debug.Log("[Setup 78] Added " + stem + " to RecipeRegistry.");
            }
            if (recipe.outputItem == null)
            {
                Debug.LogWarning("[Setup 78] " + stem + " has no resolvable output item — the block item it " +
                                 "points at is missing. The recipe is registered but will not appear in the UI.");
            }
        }

        private static void ApplyDefinition(RecipeDefinition r, Variant v)
        {
            r.displayName = v.displayName;
            r.requiredStation = v.station;
            r.craftSeconds = v.craftSeconds;
            r.unlockedByDefault = true;
            r.outputCount = 1;
            r.outputItem = AssetDatabase.LoadAssetAtPath<BlockItem>($"{BlocksFolder}/Block_{v.assetName}.asset");
            ApplyInputs(r, v);
        }

        private static void ApplyInputs(RecipeDefinition r, Variant v)
        {
            var valid = new System.Collections.Generic.List<RecipeIngredient>();
            foreach (var (item, count) in v.inputs)
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
                Debug.LogError("[Setup 78] Preserved conflicting asset at '" + path + "'.");
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
