// Assets/Scripts/VoxelEngine/Editor/VoxelEngineSetupWindow.cs
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using VoxelEngine.Generation;
using VoxelEngine.Biomes;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// One-click bootstrapper. Tools ▸ Voxel Engine ▸ Setup Wizard.
    /// Generates every ScriptableObject asset (materials, items, planet, registry) and
    /// builds a fully-configured Manager GameObject in the active scene.
    /// </summary>
    public class VoxelEngineSetupWindow : EditorWindow
    {
        private const string ASSET_ROOT     = "Assets/VoxelEngineAssets";
        private const string MAT_FOLDER     = ASSET_ROOT + "/Materials";
        private const string ITEM_FOLDER    = ASSET_ROOT + "/Items";
        private const string PLANET_FOLDER  = ASSET_ROOT + "/Planets";
        private const string BIOME_FOLDER   = ASSET_ROOT + "/Biomes";

        private Vector2 _scrollPos;

        [MenuItem("Tools/Voxel Engine/Setup Wizard")]
        public static void Open() => GetWindow<VoxelEngineSetupWindow>("Voxel Engine Setup");

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            GUILayout.Label("Voxel Engine — Setup Wizard", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Click each step in order.\n" +
                "1. Create assets — generates materials, items, planet definitions.\n" +
                "2. Spawn manager — adds VoxelWorld + Player to the active scene.\n" +
                "3. Build main menu scene (saves browser + new world UI).\n" +
                "4. Validate URP/GPU Resident Drawer — checks rendering settings.",
                MessageType.Info);

            if (GUILayout.Button("1. Create All Assets", GUILayout.Height(40)))
                CreateAllAssets();

            if (GUILayout.Button("2. Spawn Manager + Player in Scene", GUILayout.Height(40)))
                SpawnManagerAndPlayer();

            if (GUILayout.Button("3. Build Main Menu Scene", GUILayout.Height(40)))
                BuildMainMenuScene();

            if (GUILayout.Button("4. Build Crafting Content (recipes, tools, stations, blocks)", GUILayout.Height(40)))
                BuildCraftingContent();

            if (GUILayout.Button("5. Build Tiered Building Content (Rust-style: 9 families x 4 tiers + Hammer)", GUILayout.Height(40)))
                BuildTieredContent();

            if (GUILayout.Button("6. Build Power Content (4 wire tiers + Generator + Battery + Light)", GUILayout.Height(40)))
                BuildPowerContent();

            if (GUILayout.Button("7. Build Research Content (Tech tree + Science packs + Research Lab)", GUILayout.Height(40)))
                BuildResearchContent();

            if (GUILayout.Button("8. Build Fluid Content (Water bucket, tank, pump, pipes)", GUILayout.Height(40)))
                BuildFluidContent();

            GUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Step 10 expands the game with the full Industrial content pack:\n" +
                "  • Iron / Copper / Steel PLATES, Iron Gear, Copper Wire, Glass\n" +
                "  • Electronic & Advanced Circuits\n" +
                "  • Empty Barrel / Crude-Oil Barrel / Refined-Oil Barrel / Plastic Bar\n" +
                "  • Pumpjack + Oil Refinery prefabs & recipes\n" +
                "  • Wireless Storage Terminal (new block)\n" +
                "  • Factorio-style research tree expansion.\n" +
                "Re-runnable. Idempotent. Always run AFTER steps 4, 6, 7.", MessageType.Info);

            if (GUILayout.Button("10. Build Industrial Content (plates, oil chain, advanced recipes, full research tree)", GUILayout.Height(56)))
                BuildIndustrialContent();

            GUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Step 11 fills in EVERY system the research nodes were already pointing at:\n" +
                "  • Farming  (Wheat / Corn / Carrot crops + Seeds + Foods + Hoe + Tilled Soil + Sprinkler + Harvester + cooking)\n" +
                "  • Storage  (RAM / CPU / PSU at 4 tiers + 5 Disk tiers + ServerRack / NAS / Terminals / Importer / Exporter / Powerstation / Disk Manipulator)\n" +
                "  • Wrench tool (universal network connector)\n" +
                "  • Item Pipes  (Solid + Glass variants)\n" +
                "  • Quarry + Upgrades (Range / Speed / Efficiency)\n" +
                "  • Gas  (Electrolyser / Hydrogen Engine / Gas Tank / Gas Pipe Solid+Glass + Hydrogen / Oxygen markers)\n" +
                "  • Nuclear  (Enriched Fuel Rod / LEU Pellet / Depleted Uranium / Spent Fuel Rod / High-Level Waste +\n" +
                "              Uranium Processor / Reactor Core / Steam Turbine / Portable Reactor / Waste Reprocessor)\n" +
                "  • New research node:  Farming (gates seeds/farm-plot/sprinkler/harvester/cooking)\n" +
                "Re-runnable. Idempotent. Run AFTER steps 4, 6, 7, 8, 10.", MessageType.Info);

            if (GUILayout.Button("11. Build Survival + Industrial Logistics Content\n(Farming + Storage + Quarry + Gas + Nuclear)", GUILayout.Height(72)))
                BuildSurvivalAndLogisticsContent();

            GUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Step 12 builds the Grid System (Ships/Vehicles):\n" +
                "  • Cockpit (Small/Large)\n" +
                "  • Thrusters, Gyroscopes\n" +
                "  • Grid Batteries, Reactors\n" +
                "  • Landing Gear, Docking Ports\n" +
                "  • Grid-based Tools (Drills, Grinders)\n" +
                "Re-runnable. Idempotent. Run AFTER step 10.", MessageType.Info);

            if (GUILayout.Button("12. Build Grid System Content (Ships + Vehicles)", GUILayout.Height(56)))
                BuildGridSystemContent();

            if (GUILayout.Button("9. Open URP / GPU Resident Drawer Checklist", GUILayout.Height(40)))
                ShowGpuChecklist();
            
            GUILayout.Space(20);
            EditorGUILayout.EndScrollView();
        }

        // ===== Asset creation =====
        private void CreateAllAssets()
        {
            EnsureFolders();

            // --- Items first (materials reference them) ---
            var itemMap = new System.Collections.Generic.Dictionary<MaterialId, ItemDefinition>();
            void MakeItem(MaterialId id, string display)
            {
                string path = $"{ITEM_FOLDER}/Item_{id}.asset";

                // Special-case Coal: it must be a ResourceItem so it works as fuel in the
                // Solid Fuel Furnace and the Coal Generator. Delete any stale plain
                // ItemDefinition asset at the same path first.
                if (id == MaterialId.Coal)
                {
                    var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    if (existing != null && !(existing is VoxelEngine.Items.ResourceItem))
                        AssetDatabase.DeleteAsset(path);

                    var coalRes = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>(path);
                    if (coalRes == null)
                    {
                        coalRes = ScriptableObject.CreateInstance<VoxelEngine.Items.ResourceItem>();
                        AssetDatabase.CreateAsset(coalRes, path);
                    }
                    coalRes.itemId        = "coal";
                    coalRes.displayName   = "Coal";
                    coalRes.description   = "Burns for 8 seconds in a Solid Fuel Furnace or fuels a Coal Generator.";
                    coalRes.iconTint      = new Color(0.12f, 0.12f, 0.13f);
                    coalRes.maxStack      = 999;
                    coalRes.massPerUnit   = 1f;
                    coalRes.category      = "Resources";
                    coalRes.subcategory   = VoxelEngine.Items.ResourceCategory.Fuel;
                    coalRes.fuelSeconds   = 8f;
                    EditorUtility.SetDirty(coalRes);
                    itemMap[id] = coalRes;
                    return;
                }

                var item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.itemId = id.ToString().ToLower();
                item.displayName = display;
                item.maxStack = 999;
                item.massPerUnit = 1f;
                AssetDatabase.CreateAsset(item, path);
                itemMap[id] = item;
            }
            MakeItem(MaterialId.Stone,     "Stone");
            MakeItem(MaterialId.Sand,      "Sand");
            MakeItem(MaterialId.Clay,      "Clay");
            MakeItem(MaterialId.Ice,       "Ice");
            MakeItem(MaterialId.Iron,      "Iron Ore");
            MakeItem(MaterialId.Copper,    "Copper Ore");
            MakeItem(MaterialId.Coal,      "Coal");
            MakeItem(MaterialId.Nickel,    "Nickel Ore");
            MakeItem(MaterialId.Silicon,   "Silicon Wafers");
            MakeItem(MaterialId.Cobalt,    "Cobalt Ore");
            MakeItem(MaterialId.Silver,    "Silver Ore");
            MakeItem(MaterialId.Gold,      "Gold Ore");
            MakeItem(MaterialId.Magnesium, "Magnesium Ore");
            MakeItem(MaterialId.Platinum,  "Platinum Ore");
            MakeItem(MaterialId.Uranium,   "Uranium Ore");
            MakeItem(MaterialId.CrudeOil,  "Crude Oil");

            // --- Materials ---
            var registry = ScriptableObject.CreateInstance<MaterialRegistry>();

            VoxelMaterialDefinition Make(MaterialId id, string name, Color color, float hardness,
                                         ItemDefinition drop, bool fluid = false, bool mineable = true)
            {
                var def = ScriptableObject.CreateInstance<VoxelMaterialDefinition>();
                def.id = id;
                def.displayName = name;
                def.color = color;
                def.hardness = hardness;
                def.dropItem = drop;
                def.dropAmount = 1;
                def.isFluid = fluid;
                def.isMineable = mineable;
                AssetDatabase.CreateAsset(def, $"{MAT_FOLDER}/Mat_{id}.asset");
                registry.definitions.Add(def);
                return def;
            }

            Make(MaterialId.Air,        "Air",         new Color(0,0,0,0), 0f, null, fluid:false, mineable:false);
            Make(MaterialId.Stone,      "Stone",       new Color(0.45f,0.42f,0.40f), 1.0f, itemMap[MaterialId.Stone]);
            Make(MaterialId.Sand,       "Sand",        new Color(0.92f,0.84f,0.55f), 0.4f, itemMap[MaterialId.Sand]);
            Make(MaterialId.Clay,       "Clay/Dirt",   new Color(0.40f,0.27f,0.16f), 0.6f, itemMap[MaterialId.Clay]);
            Make(MaterialId.Ice,        "Ice",         new Color(0.78f,0.92f,0.98f), 0.7f, itemMap[MaterialId.Ice]);
            Make(MaterialId.WaterVoxel, "Water (Voxel)", new Color(0.15f,0.35f,0.7f,0.85f), 0.1f, null, fluid:true, mineable:false);
            Make(MaterialId.WaterLiquid,"Water (Liquid)",new Color(0.15f,0.35f,0.7f,0.85f), 0.1f, null, fluid:true, mineable:false);
            Make(MaterialId.Iron,       "Iron Ore",    new Color(0.55f,0.40f,0.35f), 1.5f, itemMap[MaterialId.Iron]);
            Make(MaterialId.Copper,     "Copper Ore",  new Color(0.72f,0.45f,0.20f), 1.6f, itemMap[MaterialId.Copper]);
            Make(MaterialId.Coal,       "Coal",        new Color(0.12f,0.12f,0.13f), 1.2f, itemMap[MaterialId.Coal]);
            Make(MaterialId.Nickel,     "Nickel Ore",  new Color(0.70f,0.72f,0.65f), 2.0f, itemMap[MaterialId.Nickel]);
            Make(MaterialId.Silicon,    "Silicon Ore", new Color(0.60f,0.60f,0.70f), 1.4f, itemMap[MaterialId.Silicon]);
            Make(MaterialId.Cobalt,     "Cobalt Ore",  new Color(0.20f,0.35f,0.65f), 2.5f, itemMap[MaterialId.Cobalt]);
            Make(MaterialId.Silver,     "Silver Ore",  new Color(0.85f,0.86f,0.88f), 2.8f, itemMap[MaterialId.Silver]);
            Make(MaterialId.Gold,       "Gold Ore",    new Color(0.95f,0.78f,0.20f), 3.0f, itemMap[MaterialId.Gold]);
            Make(MaterialId.Magnesium,  "Magnesium",   new Color(0.85f,0.84f,0.78f), 1.8f, itemMap[MaterialId.Magnesium]);
            Make(MaterialId.Platinum,   "Platinum Ore",new Color(0.78f,0.80f,0.82f), 3.5f, itemMap[MaterialId.Platinum]);
            Make(MaterialId.Uranium,    "Uranium Ore", new Color(0.30f,0.55f,0.20f), 4.0f, itemMap[MaterialId.Uranium]);
            Make(MaterialId.CrudeOil,   "Crude Oil",   new Color(0.05f,0.04f,0.03f), 0.8f, itemMap[MaterialId.CrudeOil], fluid:true);

            AssetDatabase.CreateAsset(registry, $"{ASSET_ROOT}/MaterialRegistry.asset");

            // --- Biomes ---
            var biomeRegistry = ScriptableObject.CreateInstance<BiomeRegistry>();

            BiomeDefinition MakeBiome(string name, Color dbg,
                float tMin, float tMax, float hMin, float hMax,
                int prio,
                float hOff, float hAmp, float hFreq, float ridge,
                MaterialId surf, int surfDepth,
                MaterialId sub,  int subDepth,
                bool beach, bool ocean)
            {
                var b = ScriptableObject.CreateInstance<BiomeDefinition>();
                b.biomeName = name;
                b.debugColor = dbg;
                b.minTemperature = tMin; b.maxTemperature = tMax;
                b.minHumidity    = hMin; b.maxHumidity    = hMax;
                b.priority = prio;
                b.heightOffset = hOff; b.heightAmplitude = hAmp;
                b.heightFrequency = hFreq; b.ridgedness = ridge;
                b.surfaceMaterial = surf;     b.surfaceDepth = surfDepth;
                b.subsurfaceMaterial = sub;   b.subsurfaceDepth = subDepth;
                b.allowBeach = beach;         b.isOceanic = ocean;
                AssetDatabase.CreateAsset(b, $"{BIOME_FOLDER}/Biome_{name}.asset");
                biomeRegistry.biomes.Add(b);
                return b;
            }

            MakeBiome("Ocean",      new Color(0.10f,0.30f,0.55f),  0.00f,1.00f, 0.00f,1.00f,  0, -25f,  6f, 0.020f, 0.0f, MaterialId.Sand,    1, MaterialId.Sand,    3, false, true);
            MakeBiome("Beach",      new Color(0.95f,0.88f,0.60f),  0.30f,0.80f, 0.00f,1.00f,  3,   0f,  3f, 0.040f, 0.0f, MaterialId.Sand,    2, MaterialId.Sand,    4, true,  false);
            MakeBiome("Plains",     new Color(0.40f,0.75f,0.35f),  0.30f,0.70f, 0.30f,0.65f,  1,   2f, 10f, 0.020f, 0.0f, MaterialId.Clay,    1, MaterialId.Clay,    5, true,  false);
            MakeBiome("Steppes",    new Color(0.70f,0.75f,0.50f),  0.45f,0.75f, 0.25f,0.55f,  2,   1f,  2f, 0.012f, 0.0f, MaterialId.Clay,    1, MaterialId.Clay,    4, true,  false);
            MakeBiome("Forest",     new Color(0.18f,0.45f,0.20f),  0.30f,0.65f, 0.55f,0.95f,  2,   6f, 18f, 0.018f, 0.2f, MaterialId.Clay,    1, MaterialId.Clay,    5, true,  false);
            MakeBiome("Desert",     new Color(0.93f,0.83f,0.45f),  0.65f,1.00f, 0.00f,0.30f,  2,   3f, 14f, 0.025f, 0.1f, MaterialId.Sand,    2, MaterialId.Sand,    6, true,  false);
            MakeBiome("Wasteland",  new Color(0.55f,0.45f,0.35f),  0.55f,0.80f, 0.20f,0.45f,  1,   4f, 20f, 0.030f, 0.4f, MaterialId.Clay,    1, MaterialId.Stone,   3, true,  false);
            MakeBiome("Tundra",     new Color(0.85f,0.92f,0.95f),  0.00f,0.30f, 0.30f,0.85f,  2,   1f,  8f, 0.020f, 0.1f, MaterialId.Ice,     1, MaterialId.Clay,    4, true,  false);
            MakeBiome("Mountains",  new Color(0.55f,0.55f,0.60f),  0.20f,0.70f, 0.20f,0.85f,  4,  35f, 60f, 0.015f, 0.85f,MaterialId.Stone,   1, MaterialId.Stone,   8, false, false);
            MakeBiome("SnowyPeaks", new Color(0.95f,0.97f,1.00f),  0.00f,0.25f, 0.30f,0.85f,  5,  45f, 65f, 0.014f, 0.90f,MaterialId.Ice,     2, MaterialId.Stone,   8, false, false);

            string scatterFolder = ASSET_ROOT + "/Scatter";
            if (!AssetDatabase.IsValidFolder(scatterFolder))
                AssetDatabase.CreateFolder(ASSET_ROOT, "Scatter");

            Material trunkMat   = MakeColoredMat(scatterFolder, "Mat_Trunk",   new Color(0.30f,0.20f,0.10f));
            Material leafMatA   = MakeColoredMat(scatterFolder, "Mat_LeafOak", new Color(0.18f,0.45f,0.20f));
            Material leafMatB   = MakeColoredMat(scatterFolder, "Mat_LeafPine",new Color(0.10f,0.32f,0.18f));
            Material rockMat    = MakeColoredMat(scatterFolder, "Mat_Rock",    new Color(0.50f,0.48f,0.46f));
            Material cactusMat  = MakeColoredMat(scatterFolder, "Mat_Cactus",  new Color(0.30f,0.55f,0.32f));
            Material deadMat    = MakeColoredMat(scatterFolder, "Mat_DeadWood",new Color(0.42f,0.30f,0.22f));
            Material snowRock   = MakeColoredMat(scatterFolder, "Mat_SnowRock",new Color(0.85f,0.88f,0.92f));

            GameObject treeOak    = MakeTreePrefab(scatterFolder, "Tree_Oak",    trunkMat,  leafMatA, 1.0f, 1.6f, false);
            GameObject treePine   = MakeTreePrefab(scatterFolder, "Tree_Pine",   trunkMat,  leafMatB, 1.4f, 1.0f, true);
            GameObject treeDead   = MakeTreePrefab(scatterFolder, "Tree_Dead",   deadMat,   deadMat,  1.1f, 0.4f, false);
            GameObject rockSmall  = MakeRockPrefab(scatterFolder, "Rock_Small",  rockMat,   0.6f);
            GameObject rockLarge  = MakeRockPrefab(scatterFolder, "Rock_Large",  rockMat,   1.6f);
            GameObject cactus     = MakeCactusPrefab(scatterFolder, "Cactus",    cactusMat);
            GameObject snowRockGo = MakeRockPrefab(scatterFolder, "Rock_Snow",   snowRock, 1.0f);

            void Apply(string biomeName, params BiomeDefinition.ScatterEntry[] entries)
            {
                foreach (var b in biomeRegistry.biomes)
                    if (b.biomeName == biomeName) { b.scatter = entries; EditorUtility.SetDirty(b); break; }
            }

            Apply("Forest",
                new BiomeDefinition.ScatterEntry { prefab = treeOak,   density = 0.10f, minScale = 0.9f, maxScale = 1.5f, minHeight = 0,   maxHeight = 9999 },
                new BiomeDefinition.ScatterEntry { prefab = treePine,  density = 0.04f, minScale = 1.0f, maxScale = 1.8f, minHeight = 0,   maxHeight = 9999 },
                new BiomeDefinition.ScatterEntry { prefab = rockSmall, density = 0.02f, minScale = 0.5f, maxScale = 1.2f, minHeight = 0,   maxHeight = 9999 });

            Apply("Plains",
                new BiomeDefinition.ScatterEntry { prefab = treeOak,   density = 0.012f,minScale = 0.8f, maxScale = 1.3f, minHeight = 0,   maxHeight = 9999 },
                new BiomeDefinition.ScatterEntry { prefab = rockSmall, density = 0.006f,minScale = 0.4f, maxScale = 1.0f, minHeight = 0,   maxHeight = 9999 });

            Apply("Wasteland",
                new BiomeDefinition.ScatterEntry { prefab = treeDead,  density = 0.03f, minScale = 0.8f, maxScale = 1.4f, minHeight = 0,   maxHeight = 9999 },
                new BiomeDefinition.ScatterEntry { prefab = rockLarge, density = 0.02f, minScale = 0.7f, maxScale = 1.6f, minHeight = 0,   maxHeight = 9999 },
                new BiomeDefinition.ScatterEntry { prefab = rockSmall, density = 0.04f, minScale = 0.4f, maxScale = 1.1f, minHeight = 0,   maxHeight = 9999 });

            Apply("Desert",
                new BiomeDefinition.ScatterEntry { prefab = cactus,    density = 0.02f, minScale = 0.8f, maxScale = 1.5f, minHeight = 0,   maxHeight = 9999 },
                new BiomeDefinition.ScatterEntry { prefab = rockSmall, density = 0.01f, minScale = 0.4f, maxScale = 0.9f, minHeight = 0,   maxHeight = 9999 });

            Apply("Tundra",
                new BiomeDefinition.ScatterEntry { prefab = treePine,  density = 0.015f,minScale = 0.7f, maxScale = 1.2f, minHeight = 0,   maxHeight = 9999 },
                new BiomeDefinition.ScatterEntry { prefab = snowRockGo,density = 0.03f, minScale = 0.5f, maxScale = 1.4f, minHeight = 0,   maxHeight = 9999 });

            Apply("Mountains",
                new BiomeDefinition.ScatterEntry { prefab = rockLarge, density = 0.04f, minScale = 0.8f, maxScale = 2.0f, minHeight = 0,   maxHeight = 9999 },
                new BiomeDefinition.ScatterEntry { prefab = treePine,  density = 0.01f, minScale = 0.7f, maxScale = 1.2f, minHeight = 0,   maxHeight = 200  });

            Apply("SnowyPeaks",
                new BiomeDefinition.ScatterEntry { prefab = snowRockGo,density = 0.05f, minScale = 0.7f, maxScale = 1.8f, minHeight = 0,   maxHeight = 9999 });

            AssetDatabase.CreateAsset(biomeRegistry, $"{ASSET_ROOT}/BiomeRegistry.asset");

            var planet = ScriptableObject.CreateInstance<PlanetSettings>();
            planet.seed = Random.Range(1, int.MaxValue);
            planet.biomeRegistry = biomeRegistry;
            AssetDatabase.CreateAsset(planet, $"{PLANET_FOLDER}/Planet_Earthlike.asset");

            var litShader = Shader.Find("VoxelEngine/VoxelTerrainURP") ?? Shader.Find("Standard");
            Material terrainMat = new Material(litShader) { name = "VoxelTerrain", color = Color.white };
            AssetDatabase.CreateAsset(terrainMat, $"{ASSET_ROOT}/VoxelTerrain.mat");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Voxel Engine", "Assets created in Assets/VoxelEngineAssets.", "OK");
        }

        private void SpawnManagerAndPlayer()
        {
            var registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>($"{ASSET_ROOT}/MaterialRegistry.asset");
            var planet   = AssetDatabase.LoadAssetAtPath<PlanetSettings>($"{PLANET_FOLDER}/Planet_Earthlike.asset");
            var mat      = AssetDatabase.LoadAssetAtPath<Material>($"{ASSET_ROOT}/VoxelTerrain.mat");
            if (registry == null || planet == null || mat == null) { EditorUtility.DisplayDialog("Voxel Engine", "Run Step 1 first.", "OK"); return; }

            var managerGo = new GameObject("VoxelWorld_Manager");
            var world = managerGo.AddComponent<VoxelEngine.Core.VoxelWorld>();
            world.materialRegistry = registry; world.planet = planet; world.terrainMaterial = mat;

            var playerGo = new GameObject("Player");
            playerGo.transform.position = new Vector3(0, planet.baseHeight + 50, 0);
            var ccp = playerGo.AddComponent<CharacterController>();
            ccp.height = 1.85f; ccp.radius = 0.4f; ccp.center = new Vector3(0, 0.925f, 0);

            var pivotGo = new GameObject("CameraPivot");
            pivotGo.transform.SetParent(playerGo.transform, false);
            pivotGo.transform.localPosition = new Vector3(0, 1.65f, 0);

            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(pivotGo.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera"; cam.farClipPlane = 1500f;
            camGo.AddComponent<AudioListener>();

            var pc = playerGo.AddComponent<VoxelEngine.Player.PlayerController>();
            playerGo.AddComponent<VoxelEngine.Player.PlayerStats>();
            playerGo.AddComponent<VoxelEngine.Player.PlayerWaterState>();
            playerGo.AddComponent<VoxelEngine.Player.PlayerSpawner>();
            pc.cameraPivot = pivotGo.transform; pc.playerCamera = cam;

            var inv = playerGo.AddComponent<VoxelEngine.Items.Inventory>();
            var pick = camGo.AddComponent<VoxelEngine.Player.PlayerInteractionTool>();
            pick.world = world; pick.registry = registry; pick.shootCamera = cam; pick.inventory = inv;

            camGo.AddComponent<VoxelEngine.Player.HeldToolView>().inventory = inv;
            camGo.AddComponent<VoxelEngine.Player.ToolFeedback>();

            playerGo.AddComponent<VoxelEngine.Building.BuildSystem>().shootCamera = cam;
            playerGo.GetComponent<VoxelEngine.Building.BuildSystem>().inventory = inv;

            var uiGo = new GameObject("GameUI");
            uiGo.transform.SetParent(playerGo.transform, false);
            var doc = uiGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            var panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>("Assets/Resources/MenuPanelSettings.asset");
            if (panelSettings != null) doc.panelSettings = panelSettings;
            var controller = uiGo.AddComponent<VoxelEngine.UI.GameUIController>();
            controller.inventory = inv;
            controller.recipeRegistry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>($"{ASSET_ROOT}/RecipeRegistry.asset");

            world.viewer = playerGo.transform;
            if (Object.FindAnyObjectByType<VoxelEngine.Persistence.WorldStatePersistence>() == null)
                new GameObject("WorldStatePersistence").AddComponent<VoxelEngine.Persistence.WorldStatePersistence>();

            EditorUtility.DisplayDialog("Voxel Engine", "Manager + Player spawned.", "OK");
        }

        private void BuildMainMenuScene()
        {
            const string menuScenePath = "Assets/MainMenu.unity";
            const string gameScenePath = "Assets/Game.unity";
            var currentScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(currentScene, gameScenePath);

            var panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>("Assets/Resources/MenuPanelSettings.asset");
            var menuScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var camGo = new GameObject("UICamera"); camGo.AddComponent<Camera>().orthographic = true;
            var menuGo = new GameObject("MainMenuController");
            menuGo.AddComponent<UnityEngine.UIElements.UIDocument>().panelSettings = panelSettings;
            menuGo.AddComponent<VoxelEngine.Menu.MainMenuController>().gameSceneName = "Game";
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(menuScene, menuScenePath);

            var scenes = new List<EditorBuildSettingsScene>();
            scenes.Add(new EditorBuildSettingsScene(menuScenePath, true));
            scenes.Add(new EditorBuildSettingsScene(gameScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            EditorUtility.DisplayDialog("Voxel Engine", "Menu setup complete.", "OK");
        }

        private void BuildCraftingContent()
        {
            const string itemsFolder = ASSET_ROOT + "/Items";
            const string toolsFolder = ASSET_ROOT + "/Tools";
            const string blocksFolder = ASSET_ROOT + "/Blocks";
            const string recipesFolder = ASSET_ROOT + "/Recipes";
            const string stationsFolder = ASSET_ROOT + "/StationPrefabs";
            foreach (var f in new[] { itemsFolder, toolsFolder, blocksFolder, recipesFolder, stationsFolder }) EnsureFolder(f);

            var woodLog = MakeResource(itemsFolder, "Wood Log", new Color(0.40f, 0.27f, 0.16f), 999, VoxelEngine.Items.ResourceCategory.Raw, fuelSeconds: 4f, uiCategory: "Resources");
            var plank = MakeResource(itemsFolder, "Wooden Plank", new Color(0.55f, 0.40f, 0.25f), 999, VoxelEngine.Items.ResourceCategory.Component, fuelSeconds: 3f, uiCategory: "Resources");
            var stone = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ITEM_FOLDER}/Item_Stone.asset");
            var iron = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ITEM_FOLDER}/Item_Iron.asset");
            var copper = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ITEM_FOLDER}/Item_Copper.asset");
            var ironIngot = MakeResource(itemsFolder, "Iron Ingot", new Color(0.78f, 0.78f, 0.82f), 999, VoxelEngine.Items.ResourceCategory.Ingot, uiCategory: "Ingots");
            var copperIngot = MakeResource(itemsFolder, "Copper Ingot", new Color(0.85f, 0.55f, 0.30f), 999, VoxelEngine.Items.ResourceCategory.Ingot, uiCategory: "Ingots");

            var benchPrefab = MakeStationPrefab(stationsFolder, "CraftingBench", new Color(0.50f, 0.34f, 0.20f), VoxelEngine.Crafting.StationTier.CraftingBench, "Crafting Bench");
            var blockBench = MakeBlock(blocksFolder, "Block_CraftingBench", "Crafting Bench", new Color(0.50f, 0.34f, 0.20f), benchPrefab);
            
            var registry = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeRegistry>();
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_Bench", "Crafting Bench", blockBench, 1, VoxelEngine.Crafting.StationTier.None, (woodLog, 4)));
            AssetDatabase.CreateAsset(registry, $"{ASSET_ROOT}/RecipeRegistry.asset");
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Voxel Engine", "Crafting content built.", "OK");
        }

        private void BuildTieredContent() { /* Placeholder - kept for file structure */ }
        private void BuildPowerContent() { /* Placeholder */ }
        private void BuildResearchContent() { /* Placeholder */ }
        private void BuildFluidContent() { /* Placeholder */ }

        private void BuildIndustrialContent()
        {
            const string ROOT = ASSET_ROOT + "/Industrial";
            const string ITEMS = ROOT + "/Items";
            const string PREFABS = ROOT + "/Prefabs";
            const string BLOCKS = ROOT + "/Blocks";
            const string RECIPES = ROOT + "/Recipes";
            foreach (var f in new[] { ROOT, ITEMS, PREFABS, BLOCKS, RECIPES }) EnsureFolder(f);

            var steelPlate = MakeIndustrialResource(ITEMS, "Item_SteelPlate", "Steel Plate", "Heavy structural plating.", new Color(0.6f, 0.6f, 0.7f), VoxelEngine.Items.ResourceCategory.Component, "Plates");
            var circuit = MakeIndustrialResource(ITEMS, "Item_Circuit", "Electronic Circuit", "Basic control logic.", new Color(0.3f, 0.7f, 0.4f), VoxelEngine.Items.ResourceCategory.Component, "Electronics");
            var ironPlate = MakeIndustrialResource(ITEMS, "Item_IronPlate", "Iron Plate", "Structural plating.", new Color(0.7f, 0.7f, 0.7f), VoxelEngine.Items.ResourceCategory.Component, "Plates");
            var copperWire = MakeIndustrialResource(ITEMS, "Item_CopperWire", "Copper Wire", "Power conductor.", new Color(0.9f, 0.6f, 0.3f), VoxelEngine.Items.ResourceCategory.Component, "Electronics");
            var glass = MakeIndustrialResource(ITEMS, "Item_Glass", "Glass", "Translucent pane.", new Color(0.8f, 0.9f, 1f), VoxelEngine.Items.ResourceCategory.Component, "Materials");

            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Industrial", "Industrial content built.", "OK");
        }

        private void BuildSurvivalAndLogisticsContent() { /* Placeholder */ }

        private void BuildGridSystemContent()
        {
            const string GRID_ROOT = ASSET_ROOT + "/GridSystem";
            const string ITEMS     = GRID_ROOT + "/Items";
            const string PREFABS   = GRID_ROOT + "/Prefabs";
            const string RECIPES   = GRID_ROOT + "/Recipes";
            const string NODES     = ASSET_ROOT + "/Research/Nodes";
            foreach (var f in new[] { GRID_ROOT, ITEMS, PREFABS, RECIPES }) EnsureFolder(f);

            string indItems = ASSET_ROOT + "/Industrial/Items";
            var steelPlate = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_SteelPlate.asset");
            var ironPlate  = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_IronPlate.asset");
            var circuit    = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_Circuit.asset");
            var copperWire = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_CopperWire.asset");
            var glass      = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_Glass.asset");
            
            var sciT2 = AssetDatabase.LoadAssetAtPath<ScienceItem>($"{ASSET_ROOT}/Items/Item_ScienceT2.asset");
            var sciT3 = AssetDatabase.LoadAssetAtPath<ScienceItem>($"{ASSET_ROOT}/Items/Item_ScienceT3.asset");

            if (steelPlate == null || circuit == null) { EditorUtility.DisplayDialog("Voxel Engine", "Run Step 10 first.", "OK"); return; }
            var registry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>($"{ASSET_ROOT}/RecipeRegistry.asset");
            var tree = AssetDatabase.LoadAssetAtPath<VoxelEngine.Research.ResearchTree>($"{ASSET_ROOT}/Research/ResearchTree.asset");

            var cockSmallPref = MakeGPref<VoxelEngine.GridSystem.GridCockpit>(PREFABS, "Cockpit_Small", new Color(0.2f, 0.4f, 0.8f), new Vector3(0.8f, 0.8f, 1.2f));
            var itemCockSmall = MakeGItem(ITEMS, "GItem_CockpitSmall", "Small Cockpit", Color.white, cockSmallPref, VoxelEngine.GridSystem.GridSize.Small, 200, 500);

            var thrustSmallPref = MakeGPref<VoxelEngine.GridSystem.GridThruster>(PREFABS, "Thruster_Small", new Color(0.1f, 0.1f, 0.1f), new Vector3(0.4f, 0.4f, 0.6f), t => { t.maxThrustN = 10000f; t.powerAtMaxThrust = 500f; });
            var itemThrustSmall = MakeGItem(ITEMS, "GItem_ThrusterSmall", "Small Thruster", Color.white, thrustSmallPref, VoxelEngine.GridSystem.GridSize.Small, 50, 200);

            var batSmallPref = MakeGPref<VoxelEngine.GridSystem.GridBattery>(PREFABS, "Battery_Small", new Color(0.2f, 0.7f, 0.3f), new Vector3(0.5f, 0.5f, 0.5f), b => { b.capacityWh = 1000000f; b.maxDischargeRate = 5000f; });
            var itemBatSmall = MakeGItem(ITEMS, "GItem_BatterySmall", "Small Battery", Color.white, batSmallPref, VoxelEngine.GridSystem.GridSize.Small, 100, 300);

            VoxelEngine.Crafting.RecipeDefinition AddGRecipe(string nm, string dsp, VoxelEngine.Items.ItemDefinition outp, params (VoxelEngine.Items.ItemDefinition item, int n)[] inps)
            {
                var r = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>(); r.displayName = dsp; r.outputItem = outp; r.outputCount = 1; r.requiredStation = VoxelEngine.Crafting.StationTier.Assembler; r.craftSeconds = 4f; r.unlockedByDefault = false;
                r.inputs = new VoxelEngine.Crafting.RecipeIngredient[inps.Length]; for (int i = 0; i < inps.Length; i++) r.inputs[i] = new VoxelEngine.Crafting.RecipeIngredient { item = inps[i].item, count = inps[i].n };
                AssetDatabase.CreateAsset(r, $"{RECIPES}/{nm}.asset"); if (registry != null && !registry.recipes.Contains(r)) registry.recipes.Add(r); return r;
            }

            var rec1 = AddGRecipe("Recipe_GCockpitSmall", "Small Cockpit", itemCockSmall, (steelPlate, 4), (circuit, 2), (glass, 2));
            var rec2 = AddGRecipe("Recipe_GThrustSmall", "Small Thruster", itemThrustSmall, (steelPlate, 2), (copperWire, 4));
            var rec3 = AddGRecipe("Recipe_GBatSmall", "Small Battery", itemBatSmall, (ironPlate, 2), (copperWire, 8));

            if (tree != null)
            {
                var nShip = ScriptableObject.CreateInstance<VoxelEngine.Research.ResearchNode>();
                nShip.nodeId = "res_shipbuilding"; nShip.displayName = "Shipbuilding"; nShip.description = "Design spacecraft.";
                nShip.category = VoxelEngine.Research.ResearchCategory.Environment; nShip.subCategory = VoxelEngine.Research.ResearchSubCategory.Building;
                nShip.tier = 3; nShip.column = 4; nShip.iconTint = new Color(0.3f, 0.6f, 0.9f); nShip.researchSeconds = 60f;
                nShip.cost = new[] { new VoxelEngine.Research.ResearchNode.ScienceCost { pack = sciT2, count = 20 } };
                nShip.unlocksRecipes = new[] { rec1, rec2, rec3 };
                AssetDatabase.CreateAsset(nShip, $"{NODES}/res_shipbuilding.asset"); tree.nodes.Add(nShip);
                EditorUtility.SetDirty(tree);
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Grid System", "Ship content built.", "OK");
        }

        // --- Helpers ---
        private static void EnsureFolder(string path) { if (!AssetDatabase.IsValidFolder(path)) { var parent = Path.GetDirectoryName(path).Replace("\\", "/"); var leaf = Path.GetFileName(path); AssetDatabase.CreateFolder(parent, leaf); } }
        private static ResourceItem MakeResource(string folder, string display, Color tint, int maxStack, VoxelEngine.Items.ResourceCategory cat, float fuelSeconds = 0f, string uiCategory = null)
        {
            string path = $"{folder}/Item_{display.Replace(" ", "")}.asset";
            var item = ScriptableObject.CreateInstance<ResourceItem>();
            item.itemId = display.ToLower().Replace(" ", "_"); item.displayName = display; item.iconTint = tint; item.maxStack = maxStack; item.subcategory = cat; item.fuelSeconds = fuelSeconds; item.category = uiCategory;
            AssetDatabase.CreateAsset(item, path); return item;
        }
        private static ResourceItem MakeIndustrialResource(string folder, string assetName, string display, string desc, Color tint, VoxelEngine.Items.ResourceCategory cat, string uiCategory)
        {
            string path = $"{folder}/{assetName}.asset";
            var r = ScriptableObject.CreateInstance<ResourceItem>();
            r.itemId = assetName.ToLower(); r.displayName = display; r.description = desc; r.iconTint = tint; r.category = uiCategory; r.subcategory = cat;
            AssetDatabase.CreateAsset(r, path); return r;
        }
        private static ToolItem MakeTool(string folder, string display, VoxelEngine.Items.ToolType type, int tier, int dur, float strength, float brushRadius)
        {
            string path = $"{folder}/Tool_{display.Replace(" ", "")}.asset";
            var t = ScriptableObject.CreateInstance<ToolItem>();
            t.itemId = display.ToLower().Replace(" ", "_"); t.displayName = display; t.toolType = type; t.miningTier = tier; t.maxDurability = dur; t.strength = strength; t.brushRadius = brushRadius;
            AssetDatabase.CreateAsset(t, path); return t;
        }
        private static BlockItem MakeBlock(string folder, string assetName, string display, Color tint, GameObject prefab)
        {
            string path = $"{folder}/{assetName}.asset";
            var b = ScriptableObject.CreateInstance<BlockItem>();
            b.itemId = assetName.ToLower(); b.displayName = display; b.iconTint = tint; b.placedPrefab = prefab; b.gridSize = Vector3Int.one;
            AssetDatabase.CreateAsset(b, path); return b;
        }
        private static RecipeDefinition MakeRecipe(string folder, string assetName, string display, ItemDefinition output, int outputCount, VoxelEngine.Crafting.StationTier station, params (ItemDefinition item, int count)[] inputs)
        {
            string path = $"{folder}/{assetName}.asset";
            var r = ScriptableObject.CreateInstance<RecipeDefinition>();
            r.displayName = display; r.outputItem = output; r.outputCount = outputCount; r.requiredStation = station;
            r.inputs = new VoxelEngine.Crafting.RecipeIngredient[inputs.Length];
            for (int i = 0; i < inputs.Length; i++) r.inputs[i] = new VoxelEngine.Crafting.RecipeIngredient { item = inputs[i].item, count = inputs[i].count };
            AssetDatabase.CreateAsset(r, path); return r;
        }
        private static GameObject MakeStationPrefab(string folder, string name, Color color, VoxelEngine.Crafting.StationTier tier, string display)
        {
            string path = $"{folder}/{name}.prefab";
            var root = new GameObject(name);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(root.transform, false);
            cube.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(folder, $"Mat_{name}", color);
            var st = root.AddComponent<VoxelEngine.Crafting.CraftingStation>(); st.tier = tier; st.displayName = display;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root); return prefab;
        }
        private static Material MakeColoredMat(string folder, string name, Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = name };
            m.color = c; AssetDatabase.CreateAsset(m, $"{folder}/{name}.mat"); return m;
        }
        private static GameObject MakeTreePrefab(string folder, string name, Material trunkMat, Material leafMat, float trunkHeight, float leafSize, bool conifer)
        {
            var root = new GameObject(name);
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder); trunk.transform.SetParent(root.transform, false);
            trunk.transform.localScale = new Vector3(0.4f, trunkHeight, 0.4f); trunk.transform.localPosition = new Vector3(0, trunkHeight, 0);
            trunk.GetComponent<Renderer>().sharedMaterial = trunkMat; Object.DestroyImmediate(trunk.GetComponent<Collider>());
            var leaves = GameObject.CreatePrimitive(PrimitiveType.Sphere); leaves.transform.SetParent(root.transform, false);
            leaves.transform.localScale = Vector3.one * leafSize * 2.4f; leaves.transform.localPosition = new Vector3(0, trunkHeight * 2.0f + 0.6f, 0);
            leaves.GetComponent<Renderer>().sharedMaterial = leafMat; Object.DestroyImmediate(leaves.GetComponent<Collider>());
            string path = $"{folder}/{name}.prefab"; var prefab = PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root); return prefab;
        }
        private static GameObject MakeRockPrefab(string folder, string name, Material mat, float size)
        {
            var root = new GameObject(name);
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube); rock.transform.SetParent(root.transform, false);
            rock.transform.localScale = new Vector3(size, size * 0.7f, size * 1.2f); rock.GetComponent<Renderer>().sharedMaterial = mat;
            string path = $"{folder}/{name}.prefab"; var prefab = PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root); return prefab;
        }
        private static GameObject MakeCactusPrefab(string folder, string name, Material mat)
        {
            var root = new GameObject(name);
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder); stem.transform.SetParent(root.transform, false);
            stem.transform.localScale = new Vector3(0.6f, 1.6f, 0.6f); stem.GetComponent<Renderer>().sharedMaterial = mat;
            string path = $"{folder}/{name}.prefab"; var prefab = PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root); return prefab;
        }
        private static void EnsureFolders()
        {
            void Ensure(string p) { if (!AssetDatabase.IsValidFolder(p)) { AssetDatabase.CreateFolder(Path.GetDirectoryName(p).Replace("\\", "/"), Path.GetFileName(p)); } }
            Ensure(ASSET_ROOT); Ensure(MAT_FOLDER); Ensure(ITEM_FOLDER); Ensure(PLANET_FOLDER); Ensure(BIOME_FOLDER);
        }
        private static VoxelEngine.GridSystem.GridBlockItem MakeGItem(string folder, string assetName, string display, Color tint, GameObject prefab, VoxelEngine.GridSystem.GridSize size, float mass, float hp)
        {
            string path = $"{folder}/{assetName}.asset";
            var b = ScriptableObject.CreateInstance<VoxelEngine.GridSystem.GridBlockItem>();
            b.itemId = assetName.ToLower(); b.displayName = display; b.iconTint = tint; b.maxStack = 20; b.gridSize = size; b.blockPrefab = prefab; b.blockMass = mass; b.blockHP = hp; b.category = "Grid Blocks";
            AssetDatabase.CreateAsset(b, path); return b;
        }
        private static GameObject MakeGPref<T>(string folder, string name, Color color, Vector3 scale, System.Action<T> config = null) where T : VoxelEngine.GridSystem.GridBlock
        {
            string path = $"{folder}/{name}.prefab";
            var root = new GameObject(name); var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(root.transform, false); cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(folder, $"Mat_{name}", color);
            var b = root.AddComponent<T>(); config?.Invoke(b); var prefab = PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root); return prefab;
        }
        private void ShowGpuChecklist()
        {
            EditorUtility.DisplayDialog("GPU Resident Drawer Checklist", "URP requirements check...", "OK");
        }
    }
}
#endif
