#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 84 (11.13.0-dev): author the Orbital Programme.
    ///
    /// Three linked assets, because the feature does not work unless all three exist:
    ///
    ///   1. Orbital Map  - personal equipment. Gates the M map screen. Expensive and
    ///                     research-gated, so the first one is a real milestone.
    ///   2. Satellite Research Station - a grid block. Nodes flagged requiresOrbitalLab
    ///                     can only be researched at one of these, aboard a construct
    ///                     classified as a SATELLITE and committed to orbit.
    ///   3. Two research nodes - "Orbital Telemetry" unlocks the map, and
    ///                     "Orbital Science" unlocks the station.
    ///
    /// Non-destructive: missing assets are created, existing ones only have unresolvable
    /// links repaired (null prefab, null output item, missing registry/tree membership).
    /// Authored costs, craft times, tuning and icons are never reset. Safe to re-run.
    /// Logs with the [Setup 84] prefix.
    /// </summary>
    public static class OrbitalProgrammeSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string ItemsFolder = Root + "/Items";
        private const string RecipesFolder = Root + "/Recipes";
        private const string ResearchFolder = Root + "/Research";
        private const string NodesFolder = ResearchFolder + "/Nodes";
        private const string GridItemsFolder = Root + "/GridSystem/Items";
        private const string GridPrefabsFolder = Root + "/GridSystem/Prefabs";
        private const string GridRecipesFolder = Root + "/GridSystem/Recipes";

        private static readonly Color MapTint = new(0.35f, 0.80f, 1.00f);
        private static readonly Color LabTint = new(0.72f, 0.78f, 0.86f);

        // Ingredients. All verified to exist before anything is written.
        private const string SteelPath = Root + "/Items/Item_SteelIngot.asset";
        private const string WirePath = Root + "/Industrial/Items/Item_CopperWire.asset";
        private const string ScienceT2Path = Root + "/Items/Item_ScienceT2.asset";
        private const string ScienceT3Path = Root + "/Items/Item_ScienceT3.asset";

        public static void RunStep84()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Orbital Programme", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Orbital Programme",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.", "OK");
                    return;
                }

                var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(ResearchFolder + "/ResearchTree.asset");
                if (tree == null)
                {
                    EditorUtility.DisplayDialog("Orbital Programme",
                        "ResearchTree.asset doesn't exist yet. Run the research setup step first.", "OK");
                    return;
                }

                var steel = AssetDatabase.LoadAssetAtPath<ItemDefinition>(SteelPath);
                var wire = AssetDatabase.LoadAssetAtPath<ItemDefinition>(WirePath);
                var sci2 = AssetDatabase.LoadAssetAtPath<ScienceItem>(ScienceT2Path);
                var sci3 = AssetDatabase.LoadAssetAtPath<ScienceItem>(ScienceT3Path);

                if (steel == null || wire == null)
                {
                    Debug.LogError("[Setup 84] An ingredient did not resolve (steel: " + (steel != null) +
                                   ", copper wire: " + (wire != null) + "). Run the earlier content steps first.");
                    EditorUtility.DisplayDialog("Orbital Programme",
                        "An ingredient is missing.\n\nRun the earlier crafting-content steps first.", "OK");
                    return;
                }

                var mapItem = EnsureOrbitalMapItem(out bool mapChanged);
                var mapRecipe = EnsureMapRecipe(registry, mapItem, steel, wire, out bool mapRecipeChanged);

                var labPrefab = EnsureLabPrefab(out bool labPrefabChanged);
                var labItem = EnsureLabItem(labPrefab, out bool labItemChanged);
                var labRecipe = EnsureLabRecipe(registry, labItem, steel, wire, out bool labRecipeChanged);

                EnsureResearchNodes(tree, mapRecipe, labRecipe, sci2, sci3, out bool researchChanged);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                bool any = mapChanged || mapRecipeChanged || labPrefabChanged
                           || labItemChanged || labRecipeChanged || researchChanged;

                Debug.Log("[Setup 84] Orbital Programme setup complete. " +
                          (any ? "Changes were written." : "Everything was already in place."));

                EditorUtility.DisplayDialog("Step 84 - Orbital Programme",
                    "Orbital Programme authored.\n\n" +
                    "  ORBITAL MAP (equipment)\n" +
                    "    Life Support instrument slot. Press M to open the map.\n" +
                    "    Research: Orbital Telemetry\n\n" +
                    "  SATELLITE RESEARCH STATION (grid block)\n" +
                    "    Hosts research flagged 'requiresOrbitalLab'.\n" +
                    "    Only works aboard a SATELLITE committed to orbit.\n" +
                    "    Research: Orbital Science\n\n" +
                    "To gate a research node behind orbit, tick 'Requires Orbital Lab' on that node.\n\n" +
                    (any ? "Changes were written. See the Console." : "Everything was already in place."),
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 84] Aborted: " + ex);
                EditorUtility.DisplayDialog("Orbital Programme",
                    "Setup stopped: " + ex.Message + "\n\nNothing was written.", "OK");
            }
        }

        // ============================================================
        //                      Orbital Map item
        // ============================================================
        private static OrbitalMapItem EnsureOrbitalMapItem(out bool changed)
        {
            string path = ItemsFolder + "/Equip_OrbitalMap.asset";
            var item = AssetDatabase.LoadAssetAtPath<OrbitalMapItem>(path);
            bool created = false;

            if (item == null)
            {
                EnsureFolder(ItemsFolder);
                item = ScriptableObject.CreateInstance<OrbitalMapItem>();
                created = true;
            }
            bool dirty = created;

            if (item.itemId != "orbital_map") { item.itemId = "orbital_map"; dirty = true; }
            if (item.displayName != "Orbital Map") { item.displayName = "Orbital Map"; dirty = true; }
            if (item.maxStack != 1) { item.maxStack = 1; dirty = true; }
            if (item.massPerUnit <= 0f) { item.massPerUnit = 4f; dirty = true; }
            if (item.category != "Equipment") { item.category = "Equipment"; dirty = true; }
            if (string.IsNullOrEmpty(item.description))
            {
                item.description = "Personal orbital tracking instrument. Equip in a Life Support " +
                                   "instrument slot and press M to view every named construct, planet " +
                                   "and moon in the system with live orbital telemetry.";
                dirty = true;
            }
            if (item.icon == null) item.iconTint = MapTint;

            // Tuning is only seeded on creation, never reset, so a designer can retune it.
            if (created)
            {
                item.trackingRangeKm = 250000d;
                item.showFullTelemetry = true;
                item.showOrbitPaths = true;
                item.allowFocusSwitching = true;
                item.requiresPower = false;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(item)) AssetDatabase.CreateAsset(item, path);
                EditorUtility.SetDirty(item);
                Debug.Log("[Setup 84] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }
            changed = dirty;
            return item;
        }

        private static RecipeDefinition EnsureMapRecipe(RecipeRegistry registry, OrbitalMapItem map,
            ItemDefinition steel, ItemDefinition wire, out bool changed)
        {
            const string stem = "Recipe_OrbitalMap";
            var recipe = FindRecipe(stem);

            // Deliberately expensive: this is a milestone instrument, not a starter tool.
            RecipeIngredient[] Inputs() => new[]
            {
                new RecipeIngredient { item = steel, count = 12 },
                new RecipeIngredient { item = wire,  count = 24 },
            };

            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = "Orbital Map";
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 20f;
                // NOT unlocked by default: the research node is the gate.
                recipe.unlockedByDefault = false;
                recipe.outputCount = 1;
                recipe.outputItem = map;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, RecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 84] Created " + stem + " (Steel x12 + Copper Wire x24 -> Orbital Map).");
            }
            else
            {
                bool dirty = false;
                if (recipe.outputItem == null && map != null) { recipe.outputItem = map; recipe.outputCount = 1; dirty = true; }
                if (recipe.inputs == null || recipe.inputs.Length == 0) { recipe.inputs = Inputs(); dirty = true; }
                if (dirty) EditorUtility.SetDirty(recipe);
                changed = dirty;
                Debug.Log("[Setup 84] " + stem + (dirty ? " had broken links repaired." : " kept as authored."));
            }

            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                Debug.Log("[Setup 84] Added " + stem + " to RecipeRegistry.");
            }
            return recipe;
        }

        // ============================================================
        //                 Satellite Research Station
        // ============================================================
        private static GameObject EnsureLabPrefab(out bool changed)
        {
            string path = GridPrefabsFolder + "/SatelliteResearchStation.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(GridPrefabsFolder);
                var root = new GameObject("SatelliteResearchStation");
                var mat = MakeColoredMat(GridPrefabsFolder, "Mat_SatelliteResearchStation", LabTint);

                // A sensor drum with a dish and two instrument booms.
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body";
                body.transform.SetParent(root.transform, false);
                body.transform.localScale = new Vector3(2.3f, 2.3f, 2.3f);
                Paint(body, mat);

                var dish = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                dish.name = "Dish";
                dish.transform.SetParent(root.transform, false);
                dish.transform.localPosition = new Vector3(0f, 1.35f, 0f);
                dish.transform.localScale = new Vector3(1.5f, 0.08f, 1.5f);
                Paint(dish, mat);

                for (int i = 0; i < 2; i++)
                {
                    var boom = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    boom.name = "Boom" + i;
                    boom.transform.SetParent(root.transform, false);
                    boom.transform.localPosition = new Vector3(i == 0 ? 1.5f : -1.5f, 0.2f, 0f);
                    boom.transform.localScale = new Vector3(0.9f, 0.10f, 0.10f);
                    Paint(boom, mat);
                }

                root.AddComponent<GridSatelliteLab>();

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 84] Created " + path + ".");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool repaired = false;
            if (contents.GetComponent<GridSatelliteLab>() == null)
            {
                contents.AddComponent<GridSatelliteLab>();
                repaired = true;
                Debug.Log("[Setup 84] Prefab had no GridSatelliteLab component; added one.");
            }

            GameObject result = existing;
            if (repaired) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);

            changed = repaired;
            Debug.Log("[Setup 84] Satellite Research Station prefab " + (repaired ? "corrected." : "kept as authored."));
            return result;
        }

        private static GridBlockItem EnsureLabItem(GameObject prefab, out bool changed)
        {
            string path = GridItemsFolder + "/GItem_SatelliteResearchStation.asset";
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(path);
            bool created = false;

            if (item == null)
            {
                EnsureFolder(GridItemsFolder);
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                created = true;
            }
            bool dirty = created;

            if (item.itemId != "satellite_research_station") { item.itemId = "satellite_research_station"; dirty = true; }
            if (item.displayName != "Satellite Research Station") { item.displayName = "Satellite Research Station"; dirty = true; }
            if (item.maxStack <= 0) { item.maxStack = 20; dirty = true; }
            if (item.massPerUnit <= 0f) { item.massPerUnit = 120f; dirty = true; }
            if (item.category != "Grid Blocks") { item.category = "Grid Blocks"; dirty = true; }
            if (string.IsNullOrEmpty(item.description))
            {
                item.description = "Orbital laboratory. Hosts research that cannot be performed on the " +
                                   "ground. Requires a powered construct classified as a SATELLITE and " +
                                   "committed to a stable orbit.";
                dirty = true;
            }
            if (item.icon == null) item.iconTint = LabTint;
            if (created) { item.blockMass = 120f; item.blockHP = 350f; }

            if (item.blockPrefab == null || item.blockPrefab != prefab)
            {
                item.blockPrefab = prefab;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(item)) AssetDatabase.CreateAsset(item, path);
                EditorUtility.SetDirty(item);
                Debug.Log("[Setup 84] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }
            changed = dirty;
            return item;
        }

        private static RecipeDefinition EnsureLabRecipe(RecipeRegistry registry, GridBlockItem lab,
            ItemDefinition steel, ItemDefinition wire, out bool changed)
        {
            const string stem = "Recipe_SatelliteResearchStation";
            var recipe = FindRecipe(stem);

            RecipeIngredient[] Inputs() => new[]
            {
                new RecipeIngredient { item = steel, count = 30 },
                new RecipeIngredient { item = wire,  count = 40 },
            };

            if (recipe == null)
            {
                EnsureFolder(GridRecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = "Satellite Research Station";
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 30f;
                recipe.unlockedByDefault = false;
                recipe.outputCount = 1;
                recipe.outputItem = lab;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, GridRecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 84] Created " + stem + ".");
            }
            else
            {
                bool dirty = false;
                if (recipe.outputItem == null && lab != null) { recipe.outputItem = lab; recipe.outputCount = 1; dirty = true; }
                if (recipe.inputs == null || recipe.inputs.Length == 0) { recipe.inputs = Inputs(); dirty = true; }
                if (dirty) EditorUtility.SetDirty(recipe);
                changed = dirty;
                Debug.Log("[Setup 84] " + stem + (dirty ? " had broken links repaired." : " kept as authored."));
            }

            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                Debug.Log("[Setup 84] Added " + stem + " to RecipeRegistry.");
            }
            return recipe;
        }

        // ============================================================
        //                      Research nodes
        // ============================================================
        private static void EnsureResearchNodes(ResearchTree tree, RecipeDefinition mapRecipe,
            RecipeDefinition labRecipe, ScienceItem sci2, ScienceItem sci3, out bool changed)
        {
            bool dirty = false;

            var telemetry = EnsureNode(tree, "orbital_telemetry", "Orbital Telemetry",
                "Tracks every named construct, planet and moon in the system. Unlocks the " +
                "Orbital Map, a personal instrument worn in a Life Support slot.",
                tier: 4, column: 0, seconds: 90f, mapRecipe, sci2, 12, ref dirty);

            var science = EnsureNode(tree, "orbital_science", "Orbital Science",
                "Research performed in orbit. Unlocks the Satellite Research Station, which " +
                "hosts experiments that cannot be run on the ground.",
                tier: 5, column: 0, seconds: 150f, labRecipe, sci3, 10, ref dirty);

            // Orbital Science follows Orbital Telemetry: you need to be able to see your
            // satellite before it makes sense to do science aboard one.
            if (science != null && telemetry != null
                && (science.prerequisites == null || science.prerequisites.Length == 0))
            {
                science.prerequisites = new[] { telemetry };
                EditorUtility.SetDirty(science);
                dirty = true;
                Debug.Log("[Setup 84] Linked Orbital Science behind Orbital Telemetry.");
            }

            changed = dirty;
        }

        private static ResearchNode EnsureNode(ResearchTree tree, string id, string name,
            string description, int tier, int column, float seconds, RecipeDefinition unlock,
            ScienceItem pack, int packCount, ref bool dirty)
        {
            string path = NodesFolder + "/Research_" + id + ".asset";
            var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(path);
            bool created = false;

            if (node == null)
            {
                EnsureFolder(NodesFolder);
                node = ScriptableObject.CreateInstance<ResearchNode>();
                created = true;
            }
            bool local = created;

            if (node.nodeId != id) { node.nodeId = id; local = true; }
            if (node.displayName != name) { node.displayName = name; local = true; }
            if (string.IsNullOrEmpty(node.description)) { node.description = description; local = true; }

            if (created)
            {
                node.category = ResearchCategory.Environment;
                node.subCategory = ResearchSubCategory.General;
                node.tier = tier;
                node.column = column;
                node.researchSeconds = seconds;
                node.maxRanks = 1;
                node.costScalesWithRank = false;
                if (pack != null)
                    node.cost = new[] { new ResearchNode.ScienceCost { pack = pack, count = packCount } };
            }

            // Only repair a MISSING unlock link. An authored list is never overwritten.
            if (unlock != null && (node.unlocksRecipes == null || node.unlocksRecipes.Length == 0))
            {
                node.unlocksRecipes = new[] { unlock };
                local = true;
            }

            if (local)
            {
                if (!AssetDatabase.Contains(node)) AssetDatabase.CreateAsset(node, path);
                EditorUtility.SetDirty(node);
                Debug.Log("[Setup 84] " + (created ? "Created" : "Repaired") + " research node '" + name + "'.");
            }

            if (!tree.nodes.Contains(node))
            {
                tree.nodes.Add(node);
                EditorUtility.SetDirty(tree);
                local = true;
                Debug.Log("[Setup 84] Added '" + name + "' to the ResearchTree.");
            }

            if (local) dirty = true;
            return node;
        }

        // ============================================================
        //                        Helpers
        // ============================================================
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
                Debug.LogError("[Setup 84] Preserved conflicting asset at '" + path + "'.");
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
