#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Research;
using VoxelEngine.Transport;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 91 (11.24.0-dev): the Interplanetary Cargo Pad.
    ///
    /// One placed block plus the `interplanetary_logistics` research node that unlocks
    /// it. Pads ship full loads to each other across bodies; flights are simulated
    /// centrally so they complete whether or not either world is loaded.
    ///
    /// Non-destructive: an existing prefab, block item or node keeps every authored
    /// value and only missing links are repaired. Safe to re-run. Logs with [Setup 91].
    /// </summary>
    public static class CargoPadSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string BlocksFolder = Root + "/Blocks";
        private const string RecipesFolder = Root + "/Recipes";
        private const string PrefabsFolder = Root + "/StationPrefabs";
        private const string NodeFolder = Root + "/Research/Nodes";

        private static readonly Color PadTint = new(0.46f, 0.54f, 0.64f);
        private static readonly Color MarkTint = new(0.95f, 0.72f, 0.24f);

        public static void RunStep91()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Cargo Pad", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Cargo Pad",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.",
                        "OK");
                    return;
                }

                var steel = FindItem("Item_SteelIngot");
                var wire = FindItem("Item_CopperWire");
                if (steel == null || wire == null)
                {
                    EditorUtility.DisplayDialog("Cargo Pad",
                        "An ingredient is missing.\n\nRun the earlier crafting-content steps first.", "OK");
                    return;
                }

                var prefab = EnsurePrefab(out bool prefabChanged);
                var block = EnsureBlockItem(prefab, out bool itemChanged);
                var recipe = EnsureRecipe(registry, block, steel, wire, out bool recipeChanged);
                bool nodeCreated = EnsureResearchNode(recipe);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("Step 91 - Interplanetary Cargo Pad",
                    "Cargo pad authored.\n\n" +
                    "  CARGO LAUNCH PAD   Steel x45 + Wire x35\n" +
                    (nodeCreated ? "  Research: Interplanetary Logistics\n" : "") +
                    "\nTo run a freight route:\n" +
                    "  1. Build a pad on each body and name them.\n" +
                    "  2. Set one to SEND and pick the other as its destination.\n" +
                    "  3. Set the far one to RECEIVE.\n" +
                    "  4. Power the sender and fill its hold.\n\n" +
                    "A launch needs a FULL load of one item, so a pad waits\n" +
                    "rather than burning a whole flight on a handful of ingots.\n\n" +
                    "Flights keep running while you are on another world.\n\n" +
                    ((prefabChanged || itemChanged || recipeChanged)
                        ? "Changes were written. See the Console."
                        : "Everything was already in place."),
                    "OK");

                Debug.Log("[Setup 91] Cargo pad setup complete.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 91] Aborted: " + ex);
                EditorUtility.DisplayDialog("Cargo Pad",
                    "Setup stopped: " + ex.Message + "\n\nNothing further was written.", "OK");
            }
        }

        private static GameObject EnsurePrefab(out bool changed)
        {
            string path = PrefabsFolder + "/CargoLaunchPad.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(PrefabsFolder);
                var root = new GameObject("CargoLaunchPad");
                var body = MakeMat("Mat_CargoPad", PadTint);
                var mark = MakeMat("Mat_CargoPadMark", MarkTint);

                var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
                deck.name = "Deck";
                deck.transform.SetParent(root.transform, false);
                deck.transform.localScale = new Vector3(4f, 0.3f, 4f);
                deck.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                Paint(deck, body);

                // Corner masts read as a launch site rather than a plain slab.
                for (int i = 0; i < 4; i++)
                {
                    var mast = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    mast.name = "Mast" + i;
                    mast.transform.SetParent(root.transform, false);
                    float x = (i % 2 == 0) ? -1.7f : 1.7f;
                    float z = (i < 2) ? -1.7f : 1.7f;
                    mast.transform.localScale = new Vector3(0.22f, 2.2f, 0.22f);
                    mast.transform.localPosition = new Vector3(x, 1.2f, z);
                    Paint(mast, body);
                }

                // Hazard cross on the deck: the visual that says "something lands here".
                var barA = GameObject.CreatePrimitive(PrimitiveType.Cube);
                barA.name = "MarkA";
                barA.transform.SetParent(root.transform, false);
                barA.transform.localScale = new Vector3(2.6f, 0.04f, 0.3f);
                barA.transform.localPosition = new Vector3(0f, 0.32f, 0f);
                Paint(barA, mark);

                var barB = GameObject.CreatePrimitive(PrimitiveType.Cube);
                barB.name = "MarkB";
                barB.transform.SetParent(root.transform, false);
                barB.transform.localScale = new Vector3(0.3f, 0.04f, 2.6f);
                barB.transform.localPosition = new Vector3(0f, 0.32f, 0f);
                Paint(barB, mark);

                root.AddComponent<PowerConsumer>();
                root.AddComponent<CargoLaunchPad>();

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 91] Created " + path + ".");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;

            if (contents.GetComponent<PowerConsumer>() == null)
            {
                contents.AddComponent<PowerConsumer>();
                dirty = true;
                Debug.Log("[Setup 91] Prefab had no PowerConsumer; added one.");
            }
            if (contents.GetComponent<CargoLaunchPad>() == null)
            {
                contents.AddComponent<CargoLaunchPad>();
                dirty = true;
                Debug.Log("[Setup 91] Prefab had no CargoLaunchPad; added one.");
            }

            GameObject result = existing;
            if (dirty) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            changed = dirty;
            return result;
        }

        private static BlockItem EnsureBlockItem(GameObject prefab, out bool changed)
        {
            string path = BlocksFolder + "/Block_CargoLaunchPad.asset";
            var block = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool created = false;

            if (block == null)
            {
                EnsureFolder(BlocksFolder);
                block = ScriptableObject.CreateInstance<BlockItem>();
                created = true;
            }
            bool dirty = created;

            if (block.itemId != "cargolaunchpad") { block.itemId = "cargolaunchpad"; dirty = true; }
            if (block.displayName != "Cargo Launch Pad") { block.displayName = "Cargo Launch Pad"; dirty = true; }
            if (block.maxStack <= 0) { block.maxStack = 10; dirty = true; }
            if (block.massPerUnit <= 0f) { block.massPerUnit = 640f; dirty = true; }
            if (block.blockHealth <= 0) { block.blockHealth = 800; dirty = true; }
            if (block.miningTier <= 0) { block.miningTier = 1; dirty = true; }
            if (block.category != "Machines") { block.category = "Machines"; dirty = true; }
            if (string.IsNullOrEmpty(block.description))
            {
                block.description =
                    "Ships bulk cargo between worlds. Name a pad on each body, point one at " +
                    "the other, and it launches full loads automatically. Flights continue " +
                    "while you are elsewhere.";
                dirty = true;
            }
            if (block.icon == null) block.iconTint = PadTint;

            if (block.placedPrefab == null || block.placedPrefab != prefab)
            {
                block.placedPrefab = prefab;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(block)) AssetDatabase.CreateAsset(block, path);
                EditorUtility.SetDirty(block);
                Debug.Log("[Setup 91] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            changed = dirty;
            return block;
        }

        private static RecipeDefinition EnsureRecipe(RecipeRegistry registry, BlockItem block,
            ItemDefinition steel, ItemDefinition wire, out bool changed)
        {
            const string stem = "Recipe_CargoLaunchPad";
            var recipe = FindRecipe(stem);
            changed = false;

            RecipeIngredient[] Inputs() => new[]
            {
                new RecipeIngredient { item = steel, count = 45 },
                new RecipeIngredient { item = wire, count = 35 },
            };

            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = "Cargo Launch Pad";
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 30f;
                recipe.unlockedByDefault = false;
                recipe.outputCount = 1;
                recipe.outputItem = block;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, RecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 91] Created " + stem + ".");
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
                Debug.Log("[Setup 91] Added " + stem + " to RecipeRegistry.");
                changed = true;
            }

            return recipe;
        }

        private static bool EnsureResearchNode(RecipeDefinition recipe)
        {
            var tree = FindTree();
            if (tree == null)
            {
                Debug.LogWarning("[Setup 91] No ResearchTree found; the node was skipped.");
                return false;
            }

            const string id = "interplanetary_logistics";
            string path = NodeFolder + "/Research_" + id + ".asset";
            var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(path);
            bool created = false;

            if (node == null)
            {
                EnsureFolder(NodeFolder);
                node = ScriptableObject.CreateInstance<ResearchNode>();
                created = true;

                node.nodeId = id;
                node.displayName = "Interplanetary Logistics";
                node.description =
                    "Automated freight between worlds. Unlocks the Cargo Launch Pad, which " +
                    "ships bulk loads to a named pad on another body and keeps running while " +
                    "you are somewhere else.";
                node.category = ResearchCategory.Environment;
                node.tier = 7;
                node.column = 1;
                node.researchSeconds = 260f;
                node.maxRanks = 1;
                node.costScalesWithRank = false;

                var sci3 = FindScience("Item_ScienceT3");
                if (sci3 != null)
                    node.cost = new[] { new ResearchNode.ScienceCost { pack = sci3, count = 26 } };

                AssetDatabase.CreateAsset(node, path);
                EditorUtility.SetDirty(node);
                Debug.Log("[Setup 91] Created " + path + ".");
            }

            // Repair the unlock link even on an existing node: a node that does not unlock
            // the recipe leaves the pad uncraftable with no visible reason.
            if (recipe != null)
            {
                var unlocks = node.unlocksRecipes ?? new RecipeDefinition[0];
                bool present = false;
                foreach (var r in unlocks) if (r == recipe) { present = true; break; }

                if (!present)
                {
                    var grown = new RecipeDefinition[unlocks.Length + 1];
                    unlocks.CopyTo(grown, 0);
                    grown[unlocks.Length] = recipe;
                    node.unlocksRecipes = grown;
                    EditorUtility.SetDirty(node);
                    Debug.Log("[Setup 91] Linked the pad recipe to " + id + ".");
                }
            }

            if (!tree.nodes.Contains(node))
            {
                tree.nodes.Add(node);
                EditorUtility.SetDirty(tree);
                Debug.Log("[Setup 91] Added " + id + " to the research tree.");
            }

            return created;
        }

        private static ResearchTree FindTree()
        {
            var guids = AssetDatabase.FindAssets("t:ResearchTree");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<ResearchTree>(AssetDatabase.GUIDToAssetPath(guids[0]));
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

        private static ScienceItem FindScience(string stem)
        {
            var guids = AssetDatabase.FindAssets(stem + " t:ScienceItem");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == stem)
                    return AssetDatabase.LoadAssetAtPath<ScienceItem>(p);
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
            string path = PrefabsFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                Debug.LogError("[Setup 91] Preserved conflicting asset at '" + path + "'.");
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
