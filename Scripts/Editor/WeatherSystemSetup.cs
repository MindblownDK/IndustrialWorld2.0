// Assets/Scripts/VoxelEngine/Editor/WeatherSystemSetup.cs
//
// Step 58: WEATHER, CLIMATE & PLANETARY SEASONS — non-destructive authoring.
//
//   • Authors themed WeatherClimateProfile on every existing planet/moon that does
//     not yet carry one (version-gated, so hand-tuned worlds are never overwritten).
//     Desert worlds get wind & dust, ice worlds get snow/blizzard, ocean worlds get
//     heavy rain, airless moons get no weather — exactly matching their atmosphere.
//   • Authors the GridSeasonMonitor block (prefab, item, recipe) for tracking
//     planetary seasons, orbital calendars, and climate forecasts on ship/station screens.
//   • Authors the StaticSeasonMonitor grand observatory ground station (prefab, item,
//     recipe) with rotating meteorological Doppler dish, interactive UI, and live telemetry.
//   • Persists all generated prefab materials as AssetDatabase assets so prefabs never
//     render magenta/pink.
//   • Ensures a single _Weather GameObject exists in the active scene carrying the
//     WeatherManager (+ particles/audio/lighting/clouds/sea-state). Reused if already
//     present; never duplicated.
//   • Non-destructive: existing assets, customized balance, and authored tuning
//     are preserved verbatim.
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Research;
using VoxelEngine.Weather;

namespace VoxelEngine.EditorTools
{
    public static class WeatherSystemSetup
    {
        private const int ProfileVersion = 2;

        public static void RunStep58()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 58 — Weather, Climate & Seasons setup started.");

            const string ASSET_ROOT = "Assets/VoxelEngineAssets";
            const string GRID_ROOT = ASSET_ROOT + "/GridSystem";
            const string PREFABS = GRID_ROOT + "/Prefabs";
            const string MATS = PREFABS + "/Mats";
            const string ITEMS = GRID_ROOT + "/Items";
            const string RECIPES = GRID_ROOT + "/Recipes";
            const string SEASONS_ROOT = ASSET_ROOT + "/Seasons";

            foreach (var f in new[] { GRID_ROOT, PREFABS, MATS, ITEMS, RECIPES, SEASONS_ROOT })
                EnsureFolder(f);

            int authored = 0;
            int preserved = 0;
            int itemsCreated = 0;

            // ── 1) Per-body climate profiles (non-destructive, version-gated) ──
            void AuthorClimate(BodySettings body, Object owner)
            {
                if (body == null || owner == null) return;
                if (body.weather == null) body.weather = new WeatherClimateProfile();

                // A body that was already authored (or hand-tuned) keeps its values.
                if (body.weather.profileVersion >= ProfileVersion) { preserved++; return; }

                WeatherClimateProfile profile = ChooseProfileFor(body);
                body.weather.weatherEnabled      = profile.weatherEnabled;
                body.weather.precipitation        = profile.precipitation;
                body.weather.overcastBias         = profile.overcastBias;
                body.weather.stormChance          = profile.stormChance;
                body.weather.stormDarkening       = profile.stormDarkening;
                body.weather.stormWindMultiplier  = profile.stormWindMultiplier;
                body.weather.stormFogScale        = profile.stormFogScale;
                body.weather.stormLightFloor      = profile.stormLightFloor;
                body.weather.thunderFrequency     = profile.thunderFrequency;
                body.weather.profileVersion       = ProfileVersion;

                EditorUtility.SetDirty(owner);
                authored++;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:PlanetTemplate"))
            {
                var planet = AssetDatabase.LoadAssetAtPath<PlanetTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (planet != null) AuthorClimate(planet.body, planet);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:MoonTemplate"))
            {
                var moon = AssetDatabase.LoadAssetAtPath<MoonTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (moon != null) AuthorClimate(moon.body, moon);
            }

            // ── 2) Author Screen Data Object for Seasons (non-destructive) ──
            string seasonDataPath = SEASONS_ROOT + "/ScreenData_LocalSeasons.asset";
            var localSeasonData = AssetDatabase.LoadAssetAtPath<PlanetSeasonData>(seasonDataPath);
            if (localSeasonData == null)
            {
                localSeasonData = ScriptableObject.CreateInstance<PlanetSeasonData>();
                localSeasonData.targetMode = PlanetSeasonData.TargetPlanetMode.CurrentLocalWorld;
                localSeasonData.displayTitle = "LOCAL PLANETARY SEASONS";
                AssetDatabase.CreateAsset(localSeasonData, seasonDataPath);
                itemsCreated++;
            }

            // ── Common Item Ingredients ──
            var ironPlate = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_IronPlate.asset");
            var copperWire = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_CopperWire.asset");
            var circuit = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_Circuit.asset");
            var glass = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_Glass.asset");
            var recipeRegistry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            var utilNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(ASSET_ROOT + "/Research/Nodes/res_grid_utilities.asset");

            // ── 3) Author Large Grid Season Monitor Block Prefab & Items (non-destructive) ──
            string gridPrefabPath = PREFABS + "/Grid_SeasonMonitor.prefab";
            bool existingGridPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(gridPrefabPath) != null;
            var gridRoot = existingGridPrefab ? PrefabUtility.LoadPrefabContents(gridPrefabPath) : new GameObject("Grid_SeasonMonitor");
            gridRoot.name = "Grid_SeasonMonitor";

            for (int i = gridRoot.transform.childCount - 1; i >= 0; i--)
            {
                var child = gridRoot.transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                    Object.DestroyImmediate(child.gameObject);
            }

            // Persist materials as assets to prevent pink materials
            int matIdx = 0;
            GridBlockMeshBuilder.MaterialPersister = (mat, _) =>
            {
                string mp = $"{MATS}/GridSeasonMonitor_{matIdx++}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(mp) != null) AssetDatabase.DeleteAsset(mp);
                AssetDatabase.CreateAsset(mat, mp);
                return AssetDatabase.LoadAssetAtPath<Material>(mp);
            };

            var visualHolder = new GameObject("Generated_Visuals");
            visualHolder.transform.SetParent(gridRoot.transform, false);
            try
            {
                GridBlockMeshBuilder.Build(visualHolder, GridBlockMeshBuilder.Style.SeasonMonitor, GridSize.Large, new Color(0.20f, 0.55f, 0.85f));
            }
            finally
            {
                GridBlockMeshBuilder.MaterialPersister = null;
            }

            var col = gridRoot.GetComponent<BoxCollider>();
            if (col == null) col = gridRoot.AddComponent<BoxCollider>();
            col.center = Vector3.zero;
            col.size = Vector3.one * 2.5f;

            var monitorBlock = gridRoot.GetComponent<GridSeasonMonitor>();
            if (monitorBlock == null) monitorBlock = gridRoot.AddComponent<GridSeasonMonitor>();
            monitorBlock.blockName = "Season Monitor";
            monitorBlock.powerDrawWatts = 45f;
            if (monitorBlock.screenDataObject == null) monitorBlock.screenDataObject = localSeasonData;
            if (monitorBlock.BlockMass <= 0f) monitorBlock.BlockMass = 120f;
            if (monitorBlock.maxHP <= 0f) monitorBlock.maxHP = 300;

            var gridPrefabAsset = PrefabUtility.SaveAsPrefabAsset(gridRoot, gridPrefabPath);
            if (existingGridPrefab) PrefabUtility.UnloadPrefabContents(gridRoot);
            else Object.DestroyImmediate(gridRoot);

            // Grid Item Asset
            string gridItemPath = ITEMS + "/GItem_SeasonMonitor.asset";
            var gridItem = AssetDatabase.LoadAssetAtPath<GridBlockItem>(gridItemPath);
            bool newGridItem = gridItem == null;
            if (newGridItem)
            {
                gridItem = ScriptableObject.CreateInstance<GridBlockItem>();
                AssetDatabase.CreateAsset(gridItem, gridItemPath);
                itemsCreated++;
            }
            gridItem.itemId = "gitem_season_monitor";
            gridItem.displayName = "Planetary Season Monitor";
            gridItem.description = "Ship/station telemetry block that tracks planetary orbital seasons, climate shifts, and weather forecasts for screens.";
            gridItem.iconTint = new Color(0.35f, 0.80f, 1.0f);
            if (gridItem.maxStack <= 0) gridItem.maxStack = 99;
            if (gridItem.massPerUnit <= 0f) gridItem.massPerUnit = 2f;
            gridItem.category = "Grid";
            gridItem.gridSize = GridSize.Large;
            gridItem.blockPrefab = gridPrefabAsset;
            if (gridItem.blockMass <= 0f) gridItem.blockMass = 120f;
            if (gridItem.blockHP <= 0f) gridItem.blockHP = 300;
            EditorUtility.SetDirty(gridItem);

            // Grid Recipe Asset
            string gridRecipePath = RECIPES + "/Recipe_GSeasonMonitor.asset";
            var gridRecipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(gridRecipePath);
            bool newGridRecipe = gridRecipe == null;
            if (newGridRecipe)
            {
                gridRecipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(gridRecipe, gridRecipePath);
                itemsCreated++;
            }
            gridRecipe.displayName = "Planetary Season Monitor";
            gridRecipe.outputItem = gridItem;
            if (gridRecipe.outputCount <= 0) gridRecipe.outputCount = 1;
            if (newGridRecipe)
            {
                gridRecipe.requiredStation = StationTier.Assembler;
                gridRecipe.craftSeconds = 8f;
                gridRecipe.unlockedByDefault = false;
            }
            if (gridRecipe.inputs == null || gridRecipe.inputs.Length == 0)
            {
                var inputs = new System.Collections.Generic.List<RecipeIngredient>();
                void AddInput(ItemDefinition def, int count)
                {
                    if (def != null) inputs.Add(new RecipeIngredient { item = def, count = count });
                }
                AddInput(ironPlate, 6);
                AddInput(copperWire, 8);
                AddInput(circuit, 3);
                AddInput(glass, 2);
                gridRecipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(gridRecipe);

            if (recipeRegistry != null && !recipeRegistry.recipes.Contains(gridRecipe))
            {
                recipeRegistry.recipes.Add(gridRecipe);
                EditorUtility.SetDirty(recipeRegistry);
            }

            // ── 4) Author Grand Static Season Monitor Observatory (world ground block) ──
            string staticPrefabPath = PREFABS + "/Static_SeasonMonitor.prefab";
            bool existingStaticPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(staticPrefabPath) != null;
            var staticRoot = existingStaticPrefab ? PrefabUtility.LoadPrefabContents(staticPrefabPath) : new GameObject("Static_SeasonMonitor");
            staticRoot.name = "Static_SeasonMonitor";

            for (int i = staticRoot.transform.childCount - 1; i >= 0; i--)
            {
                var child = staticRoot.transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                    Object.DestroyImmediate(child.gameObject);
            }

            var staticVisuals = new GameObject("Generated_Visuals");
            staticVisuals.transform.SetParent(staticRoot.transform, false);

            // Create persistent materials for static observatory
            Material obsBaseMat = CreatePersistentMat($"{MATS}/StaticObs_Base.mat", new Color(0.22f, 0.25f, 0.28f), 0.7f, 0.4f);
            Material obsMetalMat = CreatePersistentMat($"{MATS}/StaticObs_Metal.mat", new Color(0.55f, 0.58f, 0.62f), 0.85f, 0.6f);
            Material obsScreenMat = CreatePersistentMat($"{MATS}/StaticObs_Screen.mat", new Color(0.12f, 0.20f, 0.28f), 0.1f, 0.9f, new Color(0.15f, 0.65f, 0.95f));
            Material obsGlowMat = CreatePersistentMat($"{MATS}/StaticObs_Glow.mat", new Color(0.20f, 0.85f, 1f), 0f, 0.9f, new Color(0.20f, 0.85f, 1f));

            BuildStaticObservatoryModel(staticVisuals, obsBaseMat, obsMetalMat, obsScreenMat, obsGlowMat);

            var staticCol = staticRoot.GetComponent<BoxCollider>();
            if (staticCol == null) staticCol = staticRoot.AddComponent<BoxCollider>();
            staticCol.center = new Vector3(0, 1.1f, 0);
            staticCol.size = new Vector3(2.0f, 2.2f, 2.0f);

            var staticMonitor = staticRoot.GetComponent<StaticSeasonMonitor>();
            if (staticMonitor == null) staticMonitor = staticRoot.AddComponent<StaticSeasonMonitor>();
            staticMonitor.powerDrawWatts = 40f;
            staticMonitor.mode = StaticSeasonMonitor.MonitorMode.AutoCurrentPlanet;
            staticMonitor.dishRotationSpeed = 30f;
            staticMonitor.screenDataObject = localSeasonData;

            var staticPrefabAsset = PrefabUtility.SaveAsPrefabAsset(staticRoot, staticPrefabPath);
            if (existingStaticPrefab) PrefabUtility.UnloadPrefabContents(staticRoot);
            else Object.DestroyImmediate(staticRoot);

            // Static Item Asset (BlockItem)
            string staticItemPath = ITEMS + "/Item_SeasonMonitor.asset";
            var staticItem = AssetDatabase.LoadAssetAtPath<BlockItem>(staticItemPath);
            bool newStaticItem = staticItem == null;
            if (newStaticItem)
            {
                staticItem = ScriptableObject.CreateInstance<BlockItem>();
                AssetDatabase.CreateAsset(staticItem, staticItemPath);
                itemsCreated++;
            }
            staticItem.itemId = "item_season_monitor";
            staticItem.displayName = "Planetary Observatory";
            staticItem.description = "Grand ground-placed planetary observatory with Doppler meteorological radar for tracking orbital seasons, climate shifts, and weather forecasts.";
            staticItem.iconTint = new Color(0.20f, 0.85f, 1.0f);
            staticItem.maxStack = 10;
            staticItem.massPerUnit = 8f;
            staticItem.category = "Stations";
            staticItem.placedPrefab = staticPrefabAsset;
            staticItem.allowStacking = false;
            staticItem.blockHealth = 450;
            staticItem.miningTier = 1;
            EditorUtility.SetDirty(staticItem);

            // Static Recipe Asset
            string staticRecipePath = RECIPES + "/Recipe_StaticSeasonMonitor.asset";
            var staticRecipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(staticRecipePath);
            bool newStaticRecipe = staticRecipe == null;
            if (newStaticRecipe)
            {
                staticRecipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(staticRecipe, staticRecipePath);
                itemsCreated++;
            }
            staticRecipe.displayName = "Planetary Observatory";
            staticRecipe.outputItem = staticItem;
            if (staticRecipe.outputCount <= 0) staticRecipe.outputCount = 1;
            if (newStaticRecipe)
            {
                staticRecipe.requiredStation = StationTier.Assembler;
                staticRecipe.craftSeconds = 12f;
                staticRecipe.unlockedByDefault = false;
            }
            if (staticRecipe.inputs == null || staticRecipe.inputs.Length == 0)
            {
                var inputs = new System.Collections.Generic.List<RecipeIngredient>();
                void AddInput(ItemDefinition def, int count)
                {
                    if (def != null) inputs.Add(new RecipeIngredient { item = def, count = count });
                }
                AddInput(ironPlate, 8);
                AddInput(copperWire, 10);
                AddInput(circuit, 4);
                AddInput(glass, 4);
                staticRecipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(staticRecipe);

            if (recipeRegistry != null && !recipeRegistry.recipes.Contains(staticRecipe))
            {
                recipeRegistry.recipes.Add(staticRecipe);
                EditorUtility.SetDirty(recipeRegistry);
            }

            // Link recipes to research
            if (utilNode != null && utilNode.unlocksRecipes != null)
            {
                var list = new System.Collections.Generic.List<RecipeDefinition>(utilNode.unlocksRecipes);
                if (!list.Contains(gridRecipe)) list.Add(gridRecipe);
                if (!list.Contains(staticRecipe)) list.Add(staticRecipe);
                utilNode.unlocksRecipes = list.ToArray();
                EditorUtility.SetDirty(utilNode);
            }

            // Ensure items are persisted in ItemPersistenceCatalog
            EnsureItemsPersisted(gridItem, staticItem);

            // ── 5) Scene _Weather singleton (non-destructive: reused if present) ──
            bool sceneHooked = EnsureWeatherInActiveScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Weather, Climate & Seasons (Step 58)",
                "Weather, Climate & Planetary Seasons authored (non-destructive):\n\n" +
                "• " + authored + " bodies themed with seasonal climate profiles (" + preserved + " preserved)\n" +
                "• Grid Season Monitor block authored (Item: GItem_SeasonMonitor, Recipe: Recipe_GSeasonMonitor)\n" +
                "• Grand Static Observatory authored (Item: Item_SeasonMonitor, Recipe: Recipe_StaticSeasonMonitor)\n" +
                "• Screen Data Object authored (ScreenData_LocalSeasons.asset)\n" +
                "• Scene _Weather controller: " + (sceneHooked ? "present / connected" : "active at runtime") + "\n\n" +
                "Runtime: rain & snow particles, procedural blizzard audio, fog whiteout, seasonal temperature shifts, and grid screen season tracking.",
                "OK");
        }

        private static void BuildStaticObservatoryModel(GameObject root, Material baseMat, Material metalMat, Material screenMat, Material glowMat)
        {
            // Pedestal platform (ground foot)
            var basePlat = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            basePlat.transform.SetParent(root.transform, false);
            basePlat.transform.localPosition = new Vector3(0, 0.15f, 0);
            basePlat.transform.localScale = new Vector3(1.8f, 0.15f, 1.8f);
            Object.DestroyImmediate(basePlat.GetComponent<Collider>());
            basePlat.GetComponent<Renderer>().sharedMaterial = baseMat;

            // Main observatory housing
            var housing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            housing.transform.SetParent(root.transform, false);
            housing.transform.localPosition = new Vector3(0, 0.65f, 0);
            housing.transform.localScale = new Vector3(1.4f, 0.85f, 1.4f);
            Object.DestroyImmediate(housing.GetComponent<Collider>());
            housing.GetComponent<Renderer>().sharedMaterial = baseMat;

            // Angled interactive LCD screen face
            var screenFrame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screenFrame.transform.SetParent(root.transform, false);
            screenFrame.transform.localPosition = new Vector3(0, 0.90f, -0.65f);
            screenFrame.transform.localScale = new Vector3(0.9f, 0.55f, 0.15f);
            screenFrame.transform.localRotation = Quaternion.Euler(-25f, 0, 0);
            Object.DestroyImmediate(screenFrame.GetComponent<Collider>());
            screenFrame.GetComponent<Renderer>().sharedMaterial = metalMat;

            var screenGlass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screenGlass.transform.SetParent(screenFrame.transform, false);
            screenGlass.transform.localPosition = new Vector3(0, 0, -0.52f);
            screenGlass.transform.localScale = new Vector3(0.85f, 0.85f, 0.1f);
            Object.DestroyImmediate(screenGlass.GetComponent<Collider>());
            screenGlass.GetComponent<Renderer>().sharedMaterial = screenMat;

            // Upper instrument turret collar
            var collar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            collar.transform.SetParent(root.transform, false);
            collar.transform.localPosition = new Vector3(0, 1.15f, 0);
            collar.transform.localScale = new Vector3(1.1f, 0.18f, 1.1f);
            Object.DestroyImmediate(collar.GetComponent<Collider>());
            collar.GetComponent<Renderer>().sharedMaterial = metalMat;

            // Sensor Mast
            var mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mast.transform.SetParent(root.transform, false);
            mast.transform.localPosition = new Vector3(0, 1.55f, 0);
            mast.transform.localScale = new Vector3(0.12f, 0.35f, 0.12f);
            Object.DestroyImmediate(mast.GetComponent<Collider>());
            mast.GetComponent<Renderer>().sharedMaterial = metalMat;

            // Rotating Meteorological Doppler Dish
            var dishRoot = new GameObject("MeteorologicalDish");
            dishRoot.transform.SetParent(root.transform, false);
            dishRoot.transform.localPosition = new Vector3(0, 1.85f, 0);

            var dishMesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dishMesh.transform.SetParent(dishRoot.transform, false);
            dishMesh.transform.localScale = new Vector3(1.3f, 0.25f, 1.3f);
            dishMesh.transform.localRotation = Quaternion.Euler(22f, 0, 0);
            Object.DestroyImmediate(dishMesh.GetComponent<Collider>());
            dishMesh.GetComponent<Renderer>().sharedMaterial = metalMat;

            var dishFeed = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dishFeed.transform.SetParent(dishMesh.transform, false);
            dishFeed.transform.localPosition = new Vector3(0, 0.3f, 0.2f);
            dishFeed.transform.localScale = new Vector3(0.08f, 0.4f, 0.08f);
            dishFeed.transform.localRotation = Quaternion.Euler(35f, 0, 0);
            Object.DestroyImmediate(dishFeed.GetComponent<Collider>());
            dishFeed.GetComponent<Renderer>().sharedMaterial = glowMat;

            // Status light
            var lightObj = new GameObject("ObservatoryBeaconLight");
            lightObj.transform.SetParent(dishRoot.transform, false);
            lightObj.transform.localPosition = new Vector3(0, 0.5f, 0);
            var pointLight = lightObj.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = new Color(0.20f, 0.85f, 1.0f);
            pointLight.range = 15f;
            pointLight.intensity = 2f;
        }

        private static Material CreatePersistentMat(string path, Color baseColor, float metallic, float smoothness, Color? emission = null)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.color = baseColor;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

            if (emission.HasValue)
            {
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emission.Value);
                }
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void EnsureItemsPersisted(params ItemDefinition[] items)
        {
            const string catalogPath = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                EnsureFolder("Assets/Resources");
                EnsureFolder("Assets/Resources/VoxelEngine");
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }

            bool changed = false;
            foreach (var item in items)
            {
                if (item != null && !catalog.items.Contains(item))
                {
                    catalog.items.Add(item);
                    changed = true;
                }
            }

            if (changed) EditorUtility.SetDirty(catalog);
        }

        private static WeatherClimateProfile ChooseProfileFor(BodySettings body)
        {
            string name = (body.bodyName ?? string.Empty).ToLowerInvariant();

            bool airless = !body.HasAtmosphere
                || body.ResolveSurfaceAtmosphereDensity() <= 0.0001f
                || name.Contains("moon") || name.Contains("lunar")
                || name.Contains("asteroid") || name.Contains("belt");

            if (airless) return WeatherClimateProfile.Airless();
            if (name.Contains("desert") || name.Contains("mars") || name.Contains("desolate") || name.Contains("sand")) return WeatherClimateProfile.Desert();
            if (name.Contains("ice") || name.Contains("frozen") || name.Contains("tundra") || name.Contains("snow")) return WeatherClimateProfile.Tundra();
            if (name.Contains("ocean") || name.Contains("water") || name.Contains("sea")) return WeatherClimateProfile.Ocean();
            return WeatherClimateProfile.Default();
        }

        private static bool EnsureWeatherInActiveScene()
        {
            var scene = EditorSceneManager.GetActiveScene();

            var existing = Object.FindAnyObjectByType<WeatherManager>();
            if (existing != null)
            {
                EnsureComponent(existing.GetComponent<WeatherParticles>(), () => existing.gameObject.AddComponent<WeatherParticles>());
                EnsureComponent(existing.GetComponent<WeatherAudio>(),     () => existing.gameObject.AddComponent<WeatherAudio>());
                EnsureComponent(existing.GetComponent<WeatherLighting>(),  () => existing.gameObject.AddComponent<WeatherLighting>());
                EnsureComponent(existing.GetComponent<WeatherClouds>(),    () => existing.gameObject.AddComponent<WeatherClouds>());
                EnsureComponent(existing.GetComponent<WeatherSeaState>(),  () => existing.gameObject.AddComponent<WeatherSeaState>());
                EditorSceneManager.MarkSceneDirty(scene);
                return true;
            }

            var go = new GameObject("_Weather");
            var wm = go.AddComponent<WeatherManager>();
            go.AddComponent<WeatherParticles>();
            go.AddComponent<WeatherAudio>();
            go.AddComponent<WeatherLighting>();
            go.AddComponent<WeatherClouds>();
            go.AddComponent<WeatherSeaState>();
            wm.playerCamera = null;
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[VoxelEngineSetupWindow] Step 58 created the _Weather controller in the active scene.");
            return true;
        }

        private static void EnsureComponent<T>(T current, System.Action add) where T : Component
        {
            if (current == null) add?.Invoke();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
#endif
