#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 82 (11.7.0-dev): author the Drone Port.
    ///
    /// The logistic chests solved supply inside a 48 m bubble and no further, which left the
    /// roadmap's out-of-range delivery item open: an outpost past that radius simply could not
    /// be supplied. A pair of Drone Ports bridges the gap by flying items over real time, so
    /// long distance costs power and a round trip rather than being free.
    ///
    /// The block is a <see cref="DronePort"/> plus a <see cref="PowerConsumer"/>. It holds no
    /// inventory of its own on purpose: it serves the logistic chests already within its own
    /// service radius, so the player keeps using the one storage concept they already learned.
    ///
    /// Non-destructive by construction: a missing prefab, block item or recipe is created; an
    /// existing one is only corrected where it cannot be right (missing component, null recipe
    /// link, missing registry membership). Authored craft times, quantities, tuning values
    /// (link range, payload, speed, watts), health and icons are never reset. Safe to re-run.
    /// Every decision logs with the [Setup 82] prefix.
    /// </summary>
    public static class DronePortSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string StationsFolder = Root + "/StationPrefabs";
        private const string BlocksFolder   = Root + "/Blocks";
        private const string RecipesFolder  = Root + "/Recipes";

        private const string AssetName   = "DronePort";
        private const string DisplayName = "Drone Port";

        private static readonly Color Tint = new(0.30f, 0.62f, 0.72f);

        private const string SteelPath   = Root + "/Items/Item_SteelIngot.asset";
        private const string CircuitPath = Root + "/Industrial/Items/Item_Circuit.asset";
        private const string WirePath    = Root + "/Industrial/Items/Item_CopperWire.asset";

        public static void RunStep82()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Drone Port", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Drone Port",
                        "Run step 4 (Build Crafting Content) first — RecipeRegistry.asset doesn't exist yet.", "OK");
                    return;
                }

                var steel   = AssetDatabase.LoadAssetAtPath<ItemDefinition>(SteelPath);
                var circuit = AssetDatabase.LoadAssetAtPath<ItemDefinition>(CircuitPath);
                var wire    = AssetDatabase.LoadAssetAtPath<ItemDefinition>(WirePath);

                if (steel == null || circuit == null || wire == null)
                {
                    Debug.LogError("[Setup 82] An ingredient did not resolve (steel: " + (steel != null) +
                                   ", circuit: " + (circuit != null) + ", copper wire: " + (wire != null) +
                                   "). Run the earlier content steps first, then run this step again.");
                    EditorUtility.DisplayDialog("Drone Port",
                        "An ingredient is missing.\n\nRun the earlier crafting-content steps first, then run this step again.", "OK");
                    return;
                }

                var prefab = GetOrCreatePrefab(out bool prefabChanged);
                EnsureBlockItem(prefab, out bool blockChanged);
                EnsureRecipe(registry, steel, circuit, wire, out bool recipeChanged);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                bool any = prefabChanged || blockChanged || recipeChanged;
                Debug.Log("[Setup 82] Drone Port setup complete. " +
                          (any ? "Changes were written." : "Everything was already in place."));

                EditorUtility.DisplayDialog("Step 82 - Drone Port",
                    "Drone Port authored.\n\n" +
                    "  Link range      400 m  (chest network is 48 m)\n" +
                    "  Payload         64 items per round trip\n" +
                    "  Power           20 W idle, +140 W in flight\n\n" +
                    "Build TWO ports - one at each site - and put logistic chests within 48 m of each. " +
                    "A drone flies only what the local network cannot already supply.\n\n" +
                    (any ? "Changes were written. See the Console for details." : "Everything was already in place."),
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 82] Aborted: " + ex);
                EditorUtility.DisplayDialog("Drone Port",
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

                // A low pad with a raised mast reads as a landing site at a glance.
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = "Pad";
                pad.transform.SetParent(root.transform, false);
                pad.transform.localScale = new Vector3(1f, 0.18f, 1f);
                pad.transform.localPosition = new Vector3(0f, -0.41f, 0f);

                var mast = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mast.name = "Mast";
                mast.transform.SetParent(root.transform, false);
                mast.transform.localScale = new Vector3(0.14f, 0.62f, 0.14f);
                mast.transform.localPosition = new Vector3(0.35f, 0f, 0.35f);

                var mat = MakeColoredMat(StationsFolder, $"Mat_{AssetName}", Tint);
                if (mat != null)
                {
                    pad.GetComponent<Renderer>().sharedMaterial = mat;
                    mast.GetComponent<Renderer>().sharedMaterial = mat;
                }

                root.AddComponent<PowerConsumer>();
                var port = root.AddComponent<DronePort>();
                port.portName = DisplayName;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 82] Created " + path + ".");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;

            if (contents.GetComponent<PowerConsumer>() == null)
            {
                contents.AddComponent<PowerConsumer>();
                dirty = true;
                Debug.Log("[Setup 82] Prefab had no PowerConsumer; added one.");
            }

            var existingPort = contents.GetComponent<DronePort>();
            if (existingPort == null)
            {
                existingPort = contents.AddComponent<DronePort>();
                existingPort.portName = DisplayName;
                dirty = true;
                Debug.Log("[Setup 82] Prefab had no DronePort component; added one.");
            }
            else if (string.IsNullOrWhiteSpace(existingPort.portName))
            {
                // Only fill a BLANK name. A player-renamed port is left exactly as authored.
                existingPort.portName = DisplayName;
                dirty = true;
                Debug.Log("[Setup 82] Prefab port name was blank; set to \"" + DisplayName + "\".");
            }

            GameObject result = existing;
            if (dirty) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);

            changed = dirty;
            Debug.Log("[Setup 82] " + AssetName + " prefab " + (dirty ? "corrected." : "kept as authored."));
            return result;
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
            if (b.massPerUnit <= 0f) { b.massPerUnit = 12f; dirty = true; }
            if (b.blockHealth <= 0) { b.blockHealth = 260; dirty = true; }
            if (b.miningTier <= 0) { b.miningTier = 1; dirty = true; }
            if (b.category != "Logistics") { b.category = "Logistics"; dirty = true; }
            if (string.IsNullOrEmpty(b.description))
            {
                b.description = "Long-range logistics relay. Paired with a second port, a drone flies items " +
                                "between the logistic chests around each one — far beyond wireless range.";
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
                Debug.Log("[Setup 82] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }
            changed = dirty;
        }

        // ============================================================
        //                        Recipe
        // ============================================================
        private static void EnsureRecipe(RecipeRegistry registry, ItemDefinition steel,
                                         ItemDefinition circuit, ItemDefinition wire, out bool changed)
        {
            const string stem = "Recipe_" + AssetName;
            var recipe = FindRecipe(stem);
            var block = AssetDatabase.LoadAssetAtPath<BlockItem>($"{BlocksFolder}/Block_{AssetName}.asset");

            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                string path = $"{RecipesFolder}/{stem}.asset";
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = DisplayName;
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 8f;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = block;
                recipe.inputs = new[]
                {
                    new RecipeIngredient { item = steel,   count = 10 },
                    new RecipeIngredient { item = circuit, count = 4  },
                    new RecipeIngredient { item = wire,    count = 6  },
                };
                AssetDatabase.CreateAsset(recipe, path);
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 82] Created " + path + " (Steel x10 + Circuit x4 + Copper Wire x6 -> " +
                          DisplayName + ", Assembler).");
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
                    recipe.inputs = new[]
                    {
                        new RecipeIngredient { item = steel,   count = 10 },
                        new RecipeIngredient { item = circuit, count = 4  },
                        new RecipeIngredient { item = wire,    count = 6  },
                    };
                    dirty = true;
                }
                if (dirty) EditorUtility.SetDirty(recipe);
                changed = dirty;
                Debug.Log("[Setup 82] " + stem + (dirty ? " had broken links repaired." : " kept as authored."));
            }

            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                Debug.Log("[Setup 82] Added " + stem + " to RecipeRegistry.");
            }
            if (recipe.outputItem == null)
            {
                Debug.LogWarning("[Setup 82] " + stem + " has no resolvable output item — the block item it " +
                                 "points at is missing. The recipe is registered but will not appear in the UI.");
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
                Debug.LogError("[Setup 82] Preserved conflicting asset at '" + path + "'.");
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
