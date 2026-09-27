#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Pressure;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 90 (11.23.0-dev): the Station Life Support unit.
    ///
    /// The oxygen source that makes a sealed station compartment habitable. Without it
    /// every room a player seals would read VACUUM forever and the pressurisation
    /// feature would look broken.
    ///
    /// Non-destructive: an existing prefab or block item keeps every authored value and
    /// only missing links are repaired. Safe to re-run. Logs with the [Setup 90] prefix.
    /// </summary>
    public static class StationLifeSupportSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string BlocksFolder = Root + "/Blocks";
        private const string RecipesFolder = Root + "/Recipes";
        private const string PrefabsFolder = Root + "/StationPrefabs";

        private static readonly Color UnitTint = new(0.62f, 0.78f, 0.84f);

        public static void RunStep90()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Station Life Support",
                    "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(
                    Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Station Life Support",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.",
                        "OK");
                    return;
                }

                var steel = FindItem("Item_SteelIngot");
                var wire = FindItem("Item_CopperWire");
                if (steel == null || wire == null)
                {
                    EditorUtility.DisplayDialog("Station Life Support",
                        "An ingredient is missing.\n\nRun the earlier crafting-content steps first.",
                        "OK");
                    return;
                }

                var prefab = EnsurePrefab(out bool prefabChanged);
                var block = EnsureBlockItem(prefab, out bool itemChanged);
                bool recipeChanged = EnsureRecipe(registry, block, steel, wire);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("Step 90 - Station Life Support",
                    "Station Life Support authored.\n\n" +
                    "  STATION LIFE SUPPORT   Steel x25 + Wire x30\n\n" +
                    "Place one INSIDE a sealed station compartment and give\n" +
                    "it power. It fills the room with breathable air and keeps\n" +
                    "replacing what leaks away.\n\n" +
                    "A compartment is sealed when station pieces fully enclose\n" +
                    "it. A DOCK collar is deliberately open, so a room with one\n" +
                    "in the wall will not hold pressure until a ship mates in.\n\n" +
                    "Cut the power and the compartment slowly goes stale.\n\n" +
                    ((prefabChanged || itemChanged || recipeChanged)
                        ? "Changes were written. See the Console."
                        : "Everything was already in place."),
                    "OK");

                Debug.Log("[Setup 90] Station Life Support setup complete.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 90] Aborted: " + ex);
                EditorUtility.DisplayDialog("Station Life Support",
                    "Setup stopped: " + ex.Message + "\n\nNothing further was written.", "OK");
            }
        }

        private static GameObject EnsurePrefab(out bool changed)
        {
            string path = PrefabsFolder + "/StationLifeSupport.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(PrefabsFolder);
                var root = new GameObject("StationLifeSupport");
                var body = MakeMat("Mat_StationLifeSupport", UnitTint);
                var trim = MakeMat("Mat_StationLifeSupportTrim", new Color(0.26f, 0.34f, 0.40f));

                var cabinet = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cabinet.name = "Cabinet";
                cabinet.transform.SetParent(root.transform, false);
                cabinet.transform.localScale = new Vector3(1.1f, 1.7f, 0.7f);
                cabinet.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                Paint(cabinet, body);

                // Two scrubber cylinders: reads as an air plant rather than a crate.
                for (int i = 0; i < 2; i++)
                {
                    var tank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    tank.name = "Scrubber" + i;
                    tank.transform.SetParent(root.transform, false);
                    tank.transform.localPosition = new Vector3(i == 0 ? -0.3f : 0.3f, 1.05f, 0.42f);
                    tank.transform.localScale = new Vector3(0.28f, 0.5f, 0.28f);
                    Paint(tank, trim);
                }

                var grille = GameObject.CreatePrimitive(PrimitiveType.Cube);
                grille.name = "Grille";
                grille.transform.SetParent(root.transform, false);
                grille.transform.localScale = new Vector3(0.9f, 0.28f, 0.1f);
                grille.transform.localPosition = new Vector3(0f, 0.32f, 0.38f);
                Paint(grille, trim);

                root.AddComponent<PowerConsumer>();
                root.AddComponent<StationLifeSupport>();

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 90] Created " + path + ".");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;

            if (contents.GetComponent<PowerConsumer>() == null)
            {
                contents.AddComponent<PowerConsumer>();
                dirty = true;
                Debug.Log("[Setup 90] Prefab had no PowerConsumer; added one.");
            }
            if (contents.GetComponent<StationLifeSupport>() == null)
            {
                contents.AddComponent<StationLifeSupport>();
                dirty = true;
                Debug.Log("[Setup 90] Prefab had no StationLifeSupport; added one.");
            }

            GameObject result = existing;
            if (dirty) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            changed = dirty;
            return result;
        }

        private static BlockItem EnsureBlockItem(GameObject prefab, out bool changed)
        {
            string path = BlocksFolder + "/Block_StationLifeSupport.asset";
            var block = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool created = false;

            if (block == null)
            {
                EnsureFolder(BlocksFolder);
                block = ScriptableObject.CreateInstance<BlockItem>();
                created = true;
            }
            bool dirty = created;

            if (block.itemId != "stationlifesupport") { block.itemId = "stationlifesupport"; dirty = true; }
            if (block.displayName != "Station Life Support") { block.displayName = "Station Life Support"; dirty = true; }
            if (block.maxStack <= 0) { block.maxStack = 20; dirty = true; }
            if (block.massPerUnit <= 0f) { block.massPerUnit = 180f; dirty = true; }
            if (block.blockHealth <= 0) { block.blockHealth = 400; dirty = true; }
            if (block.miningTier <= 0) { block.miningTier = 1; dirty = true; }
            if (block.category != "Machines") { block.category = "Machines"; dirty = true; }
            if (string.IsNullOrEmpty(block.description))
            {
                block.description =
                    "Fills a sealed station compartment with breathable air and replaces what " +
                    "leaks away. Needs power and a fully enclosed room - a dock collar is open " +
                    "by design and will not hold pressure.";
                dirty = true;
            }
            if (block.icon == null) block.iconTint = UnitTint;

            if (block.placedPrefab == null || block.placedPrefab != prefab)
            {
                block.placedPrefab = prefab;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(block)) AssetDatabase.CreateAsset(block, path);
                EditorUtility.SetDirty(block);
                Debug.Log("[Setup 90] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            changed = dirty;
            return block;
        }

        private static bool EnsureRecipe(RecipeRegistry registry, BlockItem block,
            ItemDefinition steel, ItemDefinition wire)
        {
            const string stem = "Recipe_StationLifeSupport";
            var recipe = FindRecipe(stem);
            bool changed = false;

            RecipeIngredient[] Inputs() => new[]
            {
                new RecipeIngredient { item = steel, count = 25 },
                new RecipeIngredient { item = wire, count = 30 },
            };

            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = "Station Life Support";
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 20f;
                recipe.unlockedByDefault = false;
                recipe.outputCount = 1;
                recipe.outputItem = block;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, RecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 90] Created " + stem + ".");
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
                Debug.Log("[Setup 90] Added " + stem + " to RecipeRegistry.");
                changed = true;
            }

            // Gated behind the same node that unlocks the station family, so the unit and
            // the pieces it serves arrive together rather than one without the other.
            AttachToOrbitalConstruction(recipe);
            return changed;
        }

        private static void AttachToOrbitalConstruction(RecipeDefinition recipe)
        {
            var guids = AssetDatabase.FindAssets("t:ResearchNode");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var node = AssetDatabase.LoadAssetAtPath<VoxelEngine.Research.ResearchNode>(p);
                if (node == null || node.nodeId != "orbital_construction") continue;

                var unlocks = node.unlocksRecipes ?? new RecipeDefinition[0];
                foreach (var r in unlocks) if (r == recipe) return;

                var grown = new RecipeDefinition[unlocks.Length + 1];
                unlocks.CopyTo(grown, 0);
                grown[unlocks.Length] = recipe;
                node.unlocksRecipes = grown;
                EditorUtility.SetDirty(node);
                Debug.Log("[Setup 90] Attached the recipe to Orbital Construction.");
                return;
            }

            Debug.LogWarning("[Setup 90] Orbital Construction node not found; run step 89 first. " +
                             "The recipe was created but is not unlocked by research yet.");
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

        private static Material MakeMat(string name, Color c)
        {
            string path = PrefabsFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                Debug.LogError("[Setup 90] Preserved conflicting asset at '" + path + "'.");
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
