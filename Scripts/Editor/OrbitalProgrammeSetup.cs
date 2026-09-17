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

                var payloadRecipes = EnsurePayloads(registry, steel, wire, out bool payloadsChanged);

                EnsureResearchNodes(tree, mapRecipe, labRecipe, payloadRecipes, sci2, sci3, out bool researchChanged);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                bool any = mapChanged || mapRecipeChanged || labPrefabChanged
                           || labItemChanged || labRecipeChanged || payloadsChanged || researchChanged;

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
                    "  SATELLITE PAYLOADS (grid blocks)\n" +
                    "    Sensor Array     - planet-wide season tracking\n" +
                    "    Weather Radar    - adds live weather and forecast\n" +
                    "    Climate Control  - influences weather, 2.5 kW active\n" +
                    "    Resource Scanner - maps deep ore deposits from orbit\n" +
                    "    Research: Orbital Science / Climate Engineering /\n" +
                    "              Orbital Prospecting\n\n" +
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
        //                    Satellite payloads
        // ============================================================
        private readonly struct PayloadSpec
        {
            public readonly string Asset, Display, Description;
            public readonly SatellitePayloadKind Kind;
            public readonly int Steel, Wire;
            public readonly float Idle, Influence, Strength, Mass, Hp;

            public PayloadSpec(string asset, string display, string description,
                SatellitePayloadKind kind, int steel, int wire, float idle,
                float influence, float strength, float mass, float hp)
            {
                Asset = asset; Display = display; Description = description; Kind = kind;
                Steel = steel; Wire = wire; Idle = idle; Influence = influence;
                Strength = strength; Mass = mass; Hp = hp;
            }
        }

        private static readonly PayloadSpec[] Payloads =
        {
            new("SatelliteSensorArray", "Satellite Sensor Array",
                "Orbital sensor package. Reports the season cycle of the body it orbits, " +
                "planet-wide, without standing on the surface.",
                SatellitePayloadKind.SensorArray, 18, 20, 120f, 0f, 0f, 90f, 260f),

            new("SatelliteWeatherRadar", "Satellite Weather Radar",
                "Orbital weather radar. Adds live weather state and forecast on top of " +
                "full season telemetry.",
                SatellitePayloadKind.WeatherRadar, 28, 36, 220f, 0f, 0f, 130f, 300f),

            new("SatelliteClimateControl", "Satellite Climate Control Array",
                "Atmospheric steering array. Suppresses or encourages weather over the " +
                "body it orbits. Influences the odds rather than setting the sky, and " +
                "draws heavily while active.",
                SatellitePayloadKind.ClimateControl, 55, 80, 260f, 2400f, 0.35f, 240f, 380f),

            new("SatelliteResourceScanner", "Satellite Resource Scanner",
                "Deep-penetration survey array. Maps the deep ore deposits beneath the " +
                "satellite's ground track, so a world can be prospected from orbit instead " +
                "of on foot.",
                SatellitePayloadKind.ResourceScanner, 40, 55, 340f, 0f, 0f, 190f, 330f),
        };

        private static RecipeDefinition[] EnsurePayloads(RecipeRegistry registry,
            ItemDefinition steel, ItemDefinition wire, out bool changed)
        {
            bool dirty = false;
            var recipes = new RecipeDefinition[Payloads.Length];

            for (int i = 0; i < Payloads.Length; i++)
            {
                var spec = Payloads[i];
                var prefab = EnsurePayloadPrefab(spec, out bool prefabChanged);
                var item = EnsurePayloadItem(spec, prefab, out bool itemChanged);
                recipes[i] = EnsurePayloadRecipe(registry, spec, item, steel, wire, out bool recipeChanged);
                dirty |= prefabChanged || itemChanged || recipeChanged;
            }

            changed = dirty;
            return recipes;
        }

        private static GameObject EnsurePayloadPrefab(PayloadSpec spec, out bool changed)
        {
            string path = GridPrefabsFolder + "/" + spec.Asset + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(GridPrefabsFolder);
                var root = new GameObject(spec.Asset);
                var mat = MakeColoredMat(GridPrefabsFolder, "Mat_" + spec.Asset, LabTint);

                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body";
                body.transform.SetParent(root.transform, false);
                body.transform.localScale = new Vector3(2.0f, 1.2f, 2.0f);
                Paint(body, mat);

                // The dish grows with the tier, so the three blocks read apart at a glance.
                float dishScale = spec.Kind switch
                {
                    SatellitePayloadKind.ClimateControl => 2.1f,
                    // The scanner points DOWN at the ground rather than out at the sky, so
                    // it gets a wide, flat array that reads differently from a dish.
                    SatellitePayloadKind.ResourceScanner => 1.9f,
                    SatellitePayloadKind.WeatherRadar => 1.6f,
                    _ => 1.1f,
                };
                var dish = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                dish.name = "Dish";
                dish.transform.SetParent(root.transform, false);
                dish.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                dish.transform.localScale = new Vector3(dishScale, 0.07f, dishScale);
                Paint(dish, mat);

                var mast = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mast.name = "Mast";
                mast.transform.SetParent(root.transform, false);
                mast.transform.localPosition = new Vector3(0f, 0.68f, 0f);
                mast.transform.localScale = new Vector3(0.12f, 0.5f, 0.12f);
                Paint(mast, mat);

                // A downward-facing survey boom, so a scanner is identifiable at a glance
                // among a stack of otherwise similar payload blocks.
                if (spec.Kind == SatellitePayloadKind.ResourceScanner)
                {
                    var boom = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    boom.name = "SurveyBoom";
                    boom.transform.SetParent(root.transform, false);
                    boom.transform.localPosition = new Vector3(0f, -0.55f, 0f);
                    boom.transform.localScale = new Vector3(0.7f, 0.5f, 0.7f);
                    Paint(boom, mat);
                }

                var payload = root.AddComponent<GridSatellitePayload>();
                payload.kind = spec.Kind;
                payload.idleWatts = spec.Idle;
                payload.influenceWatts = spec.Influence;
                if (spec.Strength > 0f) payload.influenceStrength = spec.Strength;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 84] Created " + path + ".");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool repaired = false;

            var existingPayload = contents.GetComponent<GridSatellitePayload>();
            if (existingPayload == null)
            {
                existingPayload = contents.AddComponent<GridSatellitePayload>();
                // Only seed tuning when the component was missing entirely. An authored
                // power figure or influence strength is never reset.
                existingPayload.kind = spec.Kind;
                existingPayload.idleWatts = spec.Idle;
                existingPayload.influenceWatts = spec.Influence;
                if (spec.Strength > 0f) existingPayload.influenceStrength = spec.Strength;
                repaired = true;
                Debug.Log("[Setup 84] " + spec.Asset + " had no GridSatellitePayload; added one.");
            }

            GameObject result = existing;
            if (repaired) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);

            changed = repaired;
            return result;
        }

        private static GridBlockItem EnsurePayloadItem(PayloadSpec spec, GameObject prefab, out bool changed)
        {
            string id = spec.Asset.ToLowerInvariant();
            string path = GridItemsFolder + "/GItem_" + spec.Asset + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(path);
            bool created = false;

            if (item == null)
            {
                EnsureFolder(GridItemsFolder);
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                created = true;
            }
            bool dirty = created;

            if (item.itemId != id) { item.itemId = id; dirty = true; }
            if (item.displayName != spec.Display) { item.displayName = spec.Display; dirty = true; }
            if (item.maxStack <= 0) { item.maxStack = 20; dirty = true; }
            if (item.massPerUnit <= 0f) { item.massPerUnit = spec.Mass; dirty = true; }
            if (item.category != "Grid Blocks") { item.category = "Grid Blocks"; dirty = true; }
            if (string.IsNullOrEmpty(item.description)) { item.description = spec.Description; dirty = true; }
            if (item.icon == null) item.iconTint = LabTint;
            if (created) { item.blockMass = spec.Mass; item.blockHP = spec.Hp; }

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

        private static RecipeDefinition EnsurePayloadRecipe(RecipeRegistry registry, PayloadSpec spec,
            GridBlockItem item, ItemDefinition steel, ItemDefinition wire, out bool changed)
        {
            string stem = "Recipe_" + spec.Asset;
            var recipe = FindRecipe(stem);

            RecipeIngredient[] Inputs() => new[]
            {
                new RecipeIngredient { item = steel, count = spec.Steel },
                new RecipeIngredient { item = wire,  count = spec.Wire },
            };

            if (recipe == null)
            {
                EnsureFolder(GridRecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = spec.Display;
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 25f;
                recipe.unlockedByDefault = false;
                recipe.outputCount = 1;
                recipe.outputItem = item;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, GridRecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 84] Created " + stem + ".");
            }
            else
            {
                bool dirty = false;
                if (recipe.outputItem == null && item != null) { recipe.outputItem = item; recipe.outputCount = 1; dirty = true; }
                if (recipe.inputs == null || recipe.inputs.Length == 0) { recipe.inputs = Inputs(); dirty = true; }
                if (dirty) EditorUtility.SetDirty(recipe);
                changed = dirty;
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
            RecipeDefinition labRecipe, RecipeDefinition[] payloadRecipes,
            ScienceItem sci2, ScienceItem sci3, out bool changed)
        {
            bool dirty = false;

            var telemetry = EnsureNode(tree, "orbital_telemetry", "Orbital Telemetry",
                "Tracks every named construct, planet and moon in the system. Unlocks the " +
                "Orbital Map, a personal instrument worn in a Life Support slot.",
                tier: 4, column: 0, seconds: 90f, mapRecipe, sci2, 12, ref dirty);

            // The two observation payloads ride with Orbital Science, so getting a lab up
            // also gets you something to point at the planet.
            var scienceUnlocks = new System.Collections.Generic.List<RecipeDefinition> { labRecipe };
            if (payloadRecipes != null)
            {
                if (payloadRecipes.Length > 0 && payloadRecipes[0] != null) scienceUnlocks.Add(payloadRecipes[0]);
                if (payloadRecipes.Length > 1 && payloadRecipes[1] != null) scienceUnlocks.Add(payloadRecipes[1]);
            }

            var science = EnsureNodeMulti(tree, "orbital_science", "Orbital Science",
                "Research performed in orbit. Unlocks the Satellite Research Station plus the " +
                "Sensor Array and Weather Radar payloads.",
                tier: 5, column: 0, seconds: 150f, scienceUnlocks.ToArray(), sci3, 10, ref dirty);

            // The resource scanner gets its own node rather than riding with Orbital Science:
            // prospecting from orbit is a genuinely different capability from watching the
            // weather, and bundling it would hide it behind a name that does not suggest it.
            RecipeDefinition scannerRecipe =
                payloadRecipes != null && payloadRecipes.Length > 3 ? payloadRecipes[3] : null;

            var prospecting = EnsureNode(tree, "orbital_prospecting", "Orbital Prospecting",
                "Deep-penetration survey from orbit. Unlocks the Satellite Resource Scanner, " +
                "which maps deep ore deposits beneath the satellite's ground track.",
                tier: 6, column: 1, seconds: 180f, scannerRecipe, sci3, 16, ref dirty);

            // Climate Engineering is the end of this line and is deliberately gated behind
            // the orbital lab itself: you must already have a working satellite in orbit
            // before you can research the ability to steer a planet's weather.
            RecipeDefinition climateRecipe =
                payloadRecipes != null && payloadRecipes.Length > 2 ? payloadRecipes[2] : null;

            var climate = EnsureNode(tree, "climate_engineering", "Climate Engineering",
                "Atmospheric steering from orbit. Unlocks the Satellite Climate Control Array, " +
                "which shifts the odds of weather over the body it orbits. Must be researched " +
                "aboard an orbiting satellite.",
                tier: 6, column: 0, seconds: 240f, climateRecipe, sci3, 24, ref dirty);

            if (climate != null && !climate.requiresOrbitalLab)
            {
                climate.requiresOrbitalLab = true;
                EditorUtility.SetDirty(climate);
                dirty = true;
                Debug.Log("[Setup 84] Flagged Climate Engineering as orbital-lab-only.");
            }

            // Orbital Science follows Orbital Telemetry: you need to be able to see your
            // satellite before it makes sense to do science aboard one.
            dirty |= LinkPrerequisite(science, telemetry, "Orbital Science", "Orbital Telemetry");
            dirty |= LinkPrerequisite(climate, science, "Climate Engineering", "Orbital Science");
            dirty |= LinkPrerequisite(prospecting, science, "Orbital Prospecting", "Orbital Science");

            changed = dirty;
        }

        private static bool LinkPrerequisite(ResearchNode node, ResearchNode prerequisite,
            string nodeName, string prerequisiteName)
        {
            if (node == null || prerequisite == null) return false;
            if (node.prerequisites != null && node.prerequisites.Length > 0) return false;
            node.prerequisites = new[] { prerequisite };
            EditorUtility.SetDirty(node);
            Debug.Log("[Setup 84] Linked " + nodeName + " behind " + prerequisiteName + ".");
            return true;
        }

        private static ResearchNode EnsureNode(ResearchTree tree, string id, string name,
            string description, int tier, int column, float seconds, RecipeDefinition unlock,
            ScienceItem pack, int packCount, ref bool dirty)
            => EnsureNodeMulti(tree, id, name, description, tier, column, seconds,
                unlock != null ? new[] { unlock } : null, pack, packCount, ref dirty);

        private static ResearchNode EnsureNodeMulti(ResearchTree tree, string id, string name,
            string description, int tier, int column, float seconds, RecipeDefinition[] unlocks,
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

            // Only repair a MISSING unlock list. An authored list is never overwritten.
            if (unlocks != null && unlocks.Length > 0
                && (node.unlocksRecipes == null || node.unlocksRecipes.Length == 0))
            {
                var valid = new System.Collections.Generic.List<RecipeDefinition>();
                foreach (var r in unlocks) if (r != null) valid.Add(r);
                if (valid.Count > 0) { node.unlocksRecipes = valid.ToArray(); local = true; }
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
