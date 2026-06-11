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
                "Run steps in order — most are idempotent and safe to re-run.",
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
                "  • Factorio-style research tree expansion (Plating, Electronics,\n" +
                "    Oil Extraction, Oil Refining, Plastics, Logistics Network,\n" +
                "    Mass Storage, Crystalline Storage, Wireless Access, Quarrying,\n" +
                "    Fluid Handling, Gas Processing, Nuclear Fission, Adv Electronics).\n" +
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

            if (GUILayout.Button("12. Build Grid System Content (All Ship/Vehicle Blocks: Cockpit, Thruster, Battery, Armor, Drill, Grinder, Refinery, Weapon)", GUILayout.Height(56)))
                BuildGridSystemContent();

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

            //          name        debug-color           T-range     H-range    prio  hOff  hAmp  hFreq   ridge  surf            sd  sub             sd2  beach ocean
            MakeBiome("Ocean",      new Color(0.10f,0.30f,0.55f),  0.00f,1.00f, 0.00f,1.00f,  0, -25f,  6f, 0.020f, 0.0f, MaterialId.Sand,    1, MaterialId.Sand,    3, false, true);
            MakeBiome("Beach",      new Color(0.95f,0.88f,0.60f),  0.30f,0.80f, 0.00f,1.00f,  3,   0f,  3f, 0.040f, 0.0f, MaterialId.Sand,    2, MaterialId.Sand,    4, true,  false);
            MakeBiome("Plains",     new Color(0.40f,0.75f,0.35f),  0.30f,0.70f, 0.30f,0.65f,  1,   2f, 10f, 0.020f, 0.0f, MaterialId.Clay,    1, MaterialId.Clay,    5, true,  false);
            // Steppes: dead-flat grassland with almost no vegetation. Player-friendly base-building biome.
            MakeBiome("Steppes",    new Color(0.70f,0.75f,0.50f),  0.45f,0.75f, 0.25f,0.55f,  2,   1f,  2f, 0.012f, 0.0f, MaterialId.Clay,    1, MaterialId.Clay,    4, true,  false);
            MakeBiome("Forest",     new Color(0.18f,0.45f,0.20f),  0.30f,0.65f, 0.55f,0.95f,  2,   6f, 18f, 0.018f, 0.2f, MaterialId.Clay,    1, MaterialId.Clay,    5, true,  false);
            MakeBiome("Desert",     new Color(0.93f,0.83f,0.45f),  0.65f,1.00f, 0.00f,0.30f,  2,   3f, 14f, 0.025f, 0.1f, MaterialId.Sand,    2, MaterialId.Sand,    6, true,  false);
            MakeBiome("Wasteland",  new Color(0.55f,0.45f,0.35f),  0.55f,0.80f, 0.20f,0.45f,  1,   4f, 20f, 0.030f, 0.4f, MaterialId.Clay,    1, MaterialId.Stone,   3, true,  false);
            MakeBiome("Tundra",     new Color(0.85f,0.92f,0.95f),  0.00f,0.30f, 0.30f,0.85f,  2,   1f,  8f, 0.020f, 0.1f, MaterialId.Ice,     1, MaterialId.Clay,    4, true,  false);
            MakeBiome("Mountains",  new Color(0.55f,0.55f,0.60f),  0.20f,0.70f, 0.20f,0.85f,  4,  35f, 60f, 0.015f, 0.85f,MaterialId.Stone,   1, MaterialId.Stone,   8, false, false);
            MakeBiome("SnowyPeaks", new Color(0.95f,0.97f,1.00f),  0.00f,0.25f, 0.30f,0.85f,  5,  45f, 65f, 0.014f, 0.90f,MaterialId.Ice,     2, MaterialId.Stone,   8, false, false);

            // --- Scatter prefabs (procedural, share materials so GPU Resident Drawer batches them) ---
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

            // Helper to apply scatter entries to a named biome.
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

            // --- Planet ---
            var planet = ScriptableObject.CreateInstance<PlanetSettings>();
            planet.seed = Random.Range(1, int.MaxValue);
            planet.biomeRegistry = biomeRegistry;
            AssetDatabase.CreateAsset(planet, $"{PLANET_FOLDER}/Planet_Earthlike.asset");

            // --- Terrain material (URP Lit, vertex-colour driven if URP installed) ---
            Material terrainMat = null;
            var litShader = Shader.Find("VoxelEngine/VoxelTerrainURP");
            if (litShader == null) litShader = Shader.Find("Standard");
            terrainMat = new Material(litShader) { name = "VoxelTerrain" };
            // Vertex Colour: URP Lit doesn't use it by default. We'll create a simple shader graph hint.
            terrainMat.color = Color.white;
            AssetDatabase.CreateAsset(terrainMat, $"{ASSET_ROOT}/VoxelTerrain.mat");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Voxel Engine",
                "Assets created in Assets/VoxelEngineAssets.\n\n" +
                "NOTE: For vertex-colour-driven terrain, assign a Shader Graph that multiplies BaseColor by VertexColor.\n" +
                "A starter shader graph 'VoxelTerrain.shadergraph' can be created in 4 nodes (see README).",
                "OK");
            Selection.activeObject = registry;
        }

        // ===== Manager spawning =====
        private void SpawnManagerAndPlayer()
        {
            var registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>($"{ASSET_ROOT}/MaterialRegistry.asset");
            var planet   = AssetDatabase.LoadAssetAtPath<PlanetSettings>($"{PLANET_FOLDER}/Planet_Earthlike.asset");
            var mat      = AssetDatabase.LoadAssetAtPath<Material>($"{ASSET_ROOT}/VoxelTerrain.mat");
            if (registry == null || planet == null || mat == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine",
                    "Run Step 1 first to create assets.", "OK");
                return;
            }

            // Manager
            var managerGo = new GameObject("VoxelWorld_Manager");
            var world = managerGo.AddComponent<VoxelEngine.Core.VoxelWorld>();
            world.materialRegistry = registry;
            world.planet           = planet;
            world.terrainMaterial  = mat;

            // ----- Player -----
            // Spawn well above the surface so the player drops onto terrain instead of clipping into it.
            var playerGo = new GameObject("Player");
            playerGo.transform.position = new Vector3(0, planet.baseHeight + 50, 0);

            // CharacterController for collisions with the voxel mesh.
            var ccp = playerGo.AddComponent<CharacterController>();
            ccp.height = 1.85f;
            ccp.radius = 0.4f;
            ccp.center = new Vector3(0, 0.925f, 0);
            ccp.slopeLimit = 55f;
            ccp.stepOffset = 0.4f;
            ccp.skinWidth  = 0.05f;
            ccp.minMoveDistance = 0f;

            // CameraPivot is created by PlayerController if absent — but we create it here
            // so we can wire the camera + pickaxe tool deterministically.
            var pivotGo = new GameObject("CameraPivot");
            pivotGo.transform.SetParent(playerGo.transform, false);
            pivotGo.transform.localPosition = new Vector3(0, 1.65f, 0);

            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(pivotGo.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.farClipPlane = 1500f;
            camGo.AddComponent<AudioListener>();

            var pc = playerGo.AddComponent<VoxelEngine.Player.PlayerController>();
            playerGo.AddComponent<VoxelEngine.Player.PlayerStats>();
            playerGo.AddComponent<VoxelEngine.Player.PlayerWaterState>();
            playerGo.AddComponent<VoxelEngine.Player.PlayerSpawner>();
            pc.cameraPivot  = pivotGo.transform;
            pc.playerCamera = cam;

            var inv = playerGo.AddComponent<VoxelEngine.Items.Inventory>();
            var pick = camGo.AddComponent<VoxelEngine.Player.PlayerInteractionTool>();
            pick.world        = world;
            pick.registry     = registry;
            pick.shootCamera  = cam;
            pick.inventory    = inv;

            // Held-tool viewmodel + camera-punch feedback on the camera.
            var held = camGo.AddComponent<VoxelEngine.Player.HeldToolView>();
            held.inventory = inv;
            camGo.AddComponent<VoxelEngine.Player.ToolFeedback>();

            // BuildSystem on the player (legacy single-block system, still used for chests/furnaces).
            var build = playerGo.AddComponent<VoxelEngine.Building.BuildSystem>();
            build.shootCamera = cam;
            build.inventory   = inv;

            // BuildSystemV2 on the player (Rust-style tiered building).
            var buildV2 = playerGo.AddComponent<VoxelEngine.Building.Tiered.BuildSystemV2>();
            buildV2.shootCamera = cam;
            buildV2.inventory   = inv;
            buildV2.registry    = AssetDatabase.LoadAssetAtPath<VoxelEngine.Building.Tiered.TieredBlockRegistry>(
                $"{ASSET_ROOT}/Tiered/TieredBlockRegistry.asset");

            // In-game HUD/UI (inventory + crafting + container panels + hotbar).
            var uiGo = new GameObject("GameUI");
            uiGo.transform.SetParent(playerGo.transform, false);
            var doc = uiGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            var panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(
                "Assets/Resources/MenuPanelSettings.asset");
            if (panelSettings != null) doc.panelSettings = panelSettings;
            var ui = uiGo.AddComponent<VoxelEngine.UI.GameUIController>();
            ui.inventory      = inv;
            ui.recipeRegistry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>(
                $"{ASSET_ROOT}/RecipeRegistry.asset");

            // Hammer build wheel (radial selector for tiered families).
            var wheelGo = new GameObject("HammerBuildWheel");
            wheelGo.transform.SetParent(playerGo.transform, false);
            var wheelDoc = wheelGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            if (panelSettings != null) wheelDoc.panelSettings = panelSettings;
            var wheel = wheelGo.AddComponent<VoxelEngine.Building.Tiered.HammerBuildWheel>();
            wheel.inventory = inv;
            wheel.registry  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Building.Tiered.TieredBlockRegistry>(
                $"{ASSET_ROOT}/Tiered/TieredBlockRegistry.asset");

            // Full map UI (M key opens).
            var mapGo = new GameObject("FullMap");
            mapGo.transform.SetParent(playerGo.transform, false);
            var mapDoc = mapGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            if (panelSettings != null) mapDoc.panelSettings = panelSettings;
            mapGo.AddComponent<VoxelEngine.UI.FullMap>();

            world.viewer = playerGo.transform;

            // World-state persistence (player position, inventory, placed blocks).
            if (Object.FindAnyObjectByType<VoxelEngine.Persistence.WorldStatePersistence>() == null)
            {
                var stateGo = new GameObject("WorldStatePersistence");
                stateGo.AddComponent<VoxelEngine.Persistence.WorldStatePersistence>();
            }

            // Light
            if (Object.FindAnyObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("Sun");
                lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
                var lt = lightGo.AddComponent<Light>();
                lt.type = LightType.Directional;
                lt.intensity = 1.2f;
            }

            Selection.activeObject = managerGo;
            EditorUtility.DisplayDialog("Voxel Engine",
                "Manager + Player spawned. Press Play!", "OK");
        }

        private void BuildMainMenuScene()
        {
            const string menuScenePath = "Assets/MainMenu.unity";
            const string gameScenePath = "Assets/Game.unity";

            // ===== STEP A: ensure the current scene (manager+player) is saved as Game.unity =====
            var currentScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            bool hasManager = false;
            foreach (var go in currentScene.GetRootGameObjects())
                if (go.GetComponentInChildren<VoxelEngine.Core.VoxelWorld>(true) != null) { hasManager = true; break; }

            if (!hasManager)
            {
                EditorUtility.DisplayDialog("Voxel Engine",
                    "The active scene doesn't contain a VoxelWorld_Manager.\n\n" +
                    "Run 'Step 2 - Spawn Manager + Player' first, THEN re-run this step.\n" +
                    "(Or open your existing game scene before running this.)", "OK");
                return;
            }

            // Save it as Assets/Game.unity (overwriting if needed).
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(currentScene, gameScenePath);
            Debug.Log("[Wizard] Saved game scene to " + gameScenePath);

            // Add the pause menu GameObject to the game scene if absent.
            bool hasPauseMenu = false;
            foreach (var go in currentScene.GetRootGameObjects())
                if (go.GetComponent<VoxelEngine.Menu.InGamePauseMenu>() != null) { hasPauseMenu = true; break; }
            if (!hasPauseMenu)
            {
                var pauseGo = new GameObject("PauseMenu");
                pauseGo.AddComponent<UnityEngine.UIElements.UIDocument>();
                pauseGo.AddComponent<VoxelEngine.Menu.InGamePauseMenu>();
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(currentScene, gameScenePath);
                Debug.Log("[Wizard] Added InGamePauseMenu to Game scene.");
            }

            // ===== STEP B: bake PanelSettings + theme into Resources/ =====
            const string resourcesFolder = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesFolder))
                AssetDatabase.CreateFolder("Assets", "Resources");
            const string panelSettingsPath = "Assets/Resources/MenuPanelSettings.asset";

            var panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(panelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
                panelSettings.name      = "MenuPanelSettings";
                panelSettings.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1920, 1080);
                panelSettings.match = 0.5f;
                AssetDatabase.CreateAsset(panelSettings, panelSettingsPath);
            }
            string[] themeCandidates =
            {
                "Packages/com.unity.ui/PackageResources/StyleSheets/Generated/Default/UnityDefaultRuntimeTheme.tss",
                "Packages/com.unity.modules.uielements/PackageResources/StyleSheets/Generated/Default/UnityDefaultRuntimeTheme.tss"
            };
            UnityEngine.UIElements.ThemeStyleSheet theme = null;
            foreach (var path in themeCandidates)
            {
                theme = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>(path);
                if (theme != null) break;
            }
            if (theme != null)
            {
                panelSettings.themeStyleSheet = theme;
                EditorUtility.SetDirty(panelSettings);
            }
            else
            {
                Debug.LogWarning("[Wizard] UnityDefaultRuntimeTheme.tss not found - menu will be unstyled.");
            }
            AssetDatabase.SaveAssets();

            // ===== STEP C: create the MainMenu scene =====
            var menuScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            var camGo = new GameObject("UICamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);
            cam.orthographic = true;

            var menuGo = new GameObject("MainMenuController");
            var doc = menuGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            doc.panelSettings = panelSettings;
            var ctrl = menuGo.AddComponent<VoxelEngine.Menu.MainMenuController>();
            ctrl.gameSceneName = "Game";

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(menuScene, menuScenePath);
            Debug.Log("[Wizard] Saved main menu scene to " + menuScenePath);

            // ===== STEP D: ensure BOTH scenes are in Build Settings, with MainMenu first =====
            var existing = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            existing.RemoveAll(s => s.path == menuScenePath || s.path == gameScenePath);
            existing.Insert(0, new EditorBuildSettingsScene(menuScenePath, true));
            existing.Insert(1, new EditorBuildSettingsScene(gameScenePath, true));
            EditorBuildSettings.scenes = existing.ToArray();
            Debug.Log("[Wizard] Build Settings scene list: MainMenu @ index 0, Game @ index 1");

            EditorUtility.DisplayDialog("Voxel Engine",
                "✅ Setup complete!\n\n" +
                "• Game scene saved to " + gameScenePath + "\n" +
                "• Pause menu attached to Game scene\n" +
                "• Main menu scene created at " + menuScenePath + "\n" +
                "• Both scenes added to Build Settings\n\n" +
                "MainMenu is now open. Press Play.", "OK");
        }

        // ============================================================
        //                   STEP 4 - CRAFTING CONTENT
        // ============================================================
        private void BuildCraftingContent()
        {
            const string itemsFolder    = ASSET_ROOT + "/Items";
            const string toolsFolder    = ASSET_ROOT + "/Tools";
            const string blocksFolder   = ASSET_ROOT + "/Blocks";
            const string recipesFolder  = ASSET_ROOT + "/Recipes";
            const string stationsFolder = ASSET_ROOT + "/StationPrefabs";

            // Cleanup of legacy single-tier blocks (replaced by tiered build system).
            string[] legacyAssets =
            {
                ASSET_ROOT + "/Blocks/Block_Foundation.asset",
                ASSET_ROOT + "/Blocks/Block_Wall.asset",
                ASSET_ROOT + "/Recipes/Recipe_Foundation.asset",
                ASSET_ROOT + "/Recipes/Recipe_Wall.asset",
                ASSET_ROOT + "/StationPrefabs/Foundation.prefab",
                ASSET_ROOT + "/StationPrefabs/Wall.prefab"
            };
            foreach (var path in legacyAssets)
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                    AssetDatabase.DeleteAsset(path);

            EnsureFolder(itemsFolder);
            EnsureFolder(toolsFolder);
            EnsureFolder(blocksFolder);
            EnsureFolder(recipesFolder);
            EnsureFolder(stationsFolder);

            // ----- Resource items (raw / ingots / fuel) -----
            var woodLog       = MakeResource(itemsFolder, "Wood Log",         new Color(0.40f,0.27f,0.16f), 999, VoxelEngine.Items.ResourceCategory.Raw,       fuelSeconds: 4f, uiCategory: "Resources");
            woodLog.description = "Burns for 4 seconds as fuel. Dropped by chopping trees.";
            var plank         = MakeResource(itemsFolder, "Wooden Plank",     new Color(0.55f,0.40f,0.25f), 999, VoxelEngine.Items.ResourceCategory.Component, fuelSeconds: 3f, uiCategory: "Resources");
            plank.description = "Burns for 3 seconds as fuel. Crafted from wood logs (2 planks per log).";
            var coalItem      = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{ITEM_FOLDER}/Item_Coal.asset"); // already exists

            // Coal MUST be a ResourceItem so the furnace fuel-check accepts it. Force-upgrade
            // every time Step 4 runs (idempotent — preserves the path and ID).
            string coalPath = $"{ITEM_FOLDER}/Item_Coal.asset";
            if (coalItem != null && !(coalItem is VoxelEngine.Items.ResourceItem))
            {
                AssetDatabase.DeleteAsset(coalPath);
                coalItem = null;
            }
            var coalRes = coalItem as VoxelEngine.Items.ResourceItem;
            if (coalRes == null)
            {
                coalRes = ScriptableObject.CreateInstance<VoxelEngine.Items.ResourceItem>();
                AssetDatabase.CreateAsset(coalRes, coalPath);
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
            coalItem = coalRes;
            // Stone/iron/copper/etc. ItemDefinitions exist in ITEM_FOLDER from step 1.
            var stone = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{ITEM_FOLDER}/Item_Stone.asset");
            var iron  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{ITEM_FOLDER}/Item_Iron.asset");
            var copper= AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{ITEM_FOLDER}/Item_Copper.asset");
            var nickel= AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{ITEM_FOLDER}/Item_Nickel.asset");

            var ironIngot   = MakeResource(itemsFolder, "Iron Ingot",   new Color(0.78f,0.78f,0.82f), 999, VoxelEngine.Items.ResourceCategory.Ingot, uiCategory: "Ingots");
            var copperIngot = MakeResource(itemsFolder, "Copper Ingot", new Color(0.85f,0.55f,0.30f), 999, VoxelEngine.Items.ResourceCategory.Ingot, uiCategory: "Ingots");
            var steelIngot  = MakeResource(itemsFolder, "Steel Ingot",  new Color(0.55f,0.58f,0.65f), 999, VoxelEngine.Items.ResourceCategory.Ingot, uiCategory: "Ingots");

            // ----- Tool items (pickaxes + axe) -----
            var pickWood  = MakeTool(toolsFolder, "Wooden Pickaxe", VoxelEngine.Items.ToolType.Pickaxe, tier: 1, dur: 60,  strength: 50f, brushRadius: 1.2f);
            var pickStone = MakeTool(toolsFolder, "Stone Pickaxe",  VoxelEngine.Items.ToolType.Pickaxe, tier: 2, dur: 150, strength: 70f, brushRadius: 1.4f);
            var pickIron  = MakeTool(toolsFolder, "Iron Pickaxe",   VoxelEngine.Items.ToolType.Pickaxe, tier: 3, dur: 400, strength: 95f, brushRadius: 1.6f);
            var pickSteel = MakeTool(toolsFolder, "Steel Pickaxe",  VoxelEngine.Items.ToolType.Pickaxe, tier: 4, dur: 900, strength: 130f, brushRadius: 1.8f);
            var axeWood   = MakeTool(toolsFolder, "Wooden Axe",     VoxelEngine.Items.ToolType.Axe,     tier: 1, dur: 60,  strength: 60f, brushRadius: 1.0f);
            var axeIron   = MakeTool(toolsFolder, "Iron Axe",       VoxelEngine.Items.ToolType.Axe,     tier: 3, dur: 400, strength: 110f, brushRadius: 1.0f);

            // Grinder Tool — hand tool that grinds DOWN grid blocks back into items.
            string grinderToolPath = $"{toolsFolder}/Tool_Grinder.asset";
            var grinderTool = AssetDatabase.LoadAssetAtPath<VoxelEngine.GridSystem.GrinderTool>(grinderToolPath);
            if (grinderTool == null) { grinderTool = ScriptableObject.CreateInstance<VoxelEngine.GridSystem.GrinderTool>(); AssetDatabase.CreateAsset(grinderTool, grinderToolPath); }
            grinderTool.itemId       = "grinder_tool";
            grinderTool.displayName  = "Grinder";
            grinderTool.description  = "Grinds down grid (ship/vehicle) blocks, returning them as items.";
            grinderTool.maxStack     = 1;
            grinderTool.toolType     = VoxelEngine.Items.ToolType.Other;
            grinderTool.miningTier   = 3;
            grinderTool.maxDurability = 600;
            grinderTool.iconTint     = new Color(0.85f, 0.55f, 0.15f);
            grinderTool.category     = "Tools";
            grinderTool.baseGrindTime = 3f;
            grinderTool.minGrindTime  = 1.2f;
            EditorUtility.SetDirty(grinderTool);

            // Leveling Tool — a special tool that flattens terrain to a target Y level (custom class).
            string levelToolPath = $"{toolsFolder}/Tool_LevelingTool.asset";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(levelToolPath) != null) AssetDatabase.DeleteAsset(levelToolPath);
            var lvTool = ScriptableObject.CreateInstance<VoxelEngine.Items.LevelingTool>();
            lvTool.itemId        = "leveling_tool";
            lvTool.displayName   = "Leveling Tool";
            lvTool.description   = "FIRST left-click: anchors the target height (the surface you look at). " +
                                    "Next left-clicks flatten terrain to that height in a 3m brush radius — " +
                                    "fills low spots with stone, carves out high spots. Perfect for building plots.";
            lvTool.toolType      = VoxelEngine.Items.ToolType.Other;
            lvTool.miningTier    = 2;
            lvTool.maxDurability = 500;
            lvTool.strength      = 1f;
            lvTool.fireRate      = 3f;
            lvTool.brushRadius   = 3f;
            lvTool.iconTint      = new Color(0.85f, 0.80f, 0.30f);
            lvTool.maxStack      = 1;
            lvTool.category      = "Tools";
            AssetDatabase.CreateAsset(lvTool, levelToolPath);

            // ----- Station block prefabs (procedural cube placeholders) -----
            var benchPrefab    = MakeStationPrefab(stationsFolder, "CraftingBench", new Color(0.50f,0.34f,0.20f),  VoxelEngine.Crafting.StationTier.CraftingBench, "Crafting Bench");
            var furnacePrefab  = MakeStationPrefab(stationsFolder, "Furnace",       new Color(0.30f,0.30f,0.32f),  VoxelEngine.Crafting.StationTier.Furnace,        "Furnace", isFurnace:true);
            var assemblerPrefab= MakeStationPrefab(stationsFolder, "Assembler",     new Color(0.20f,0.50f,0.85f),  VoxelEngine.Crafting.StationTier.Assembler,      "Assembler");
            var chestPrefab    = MakeChestPrefab(stationsFolder, "Chest",           new Color(0.55f,0.40f,0.20f), 30);

            // ----- BlockItems for each placeable -----
            var blockBench      = MakeBlock(blocksFolder, "Block_CraftingBench", "Crafting Bench", new Color(0.50f,0.34f,0.20f), benchPrefab, "Stations");
            blockBench.description    = "Tier-1 workstation. Place near the player to unlock stone/foundation/wall recipes.";
            var blockFurnace    = MakeBlock(blocksFolder, "Block_Furnace",       "Solid Fuel Furnace",  new Color(0.30f,0.30f,0.32f), furnacePrefab, "Stations");
            blockFurnace.description  = "Burns wood logs / planks / coal as fuel to smelt iron and copper ingots. Slow but no power required.";
            var blockAssembler  = MakeBlock(blocksFolder, "Block_Assembler",     "Assembler",      new Color(0.20f,0.50f,0.85f), assemblerPrefab, "Stations");
            blockAssembler.description= "Tier-3 workstation. Required to craft iron / steel pickaxes, batteries, and high-tier wires.";
            var blockChest      = MakeBlock(blocksFolder, "Block_Chest",         "Chest",          new Color(0.55f,0.40f,0.20f), chestPrefab, "Storage");
            blockChest.description    = "30-slot storage container. Right-click while looking at it to open.";

            // Bed prefab + BlockItem. Right-click sets the player's spawn point.
            string bedPath = $"{stationsFolder}/Bed.prefab";
            var bedRoot = new GameObject("Bed");
            var bedBase = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bedBase.transform.SetParent(bedRoot.transform, false);
            bedBase.transform.localScale = new Vector3(2f, 0.4f, 1f);
            bedBase.transform.localPosition = new Vector3(0, 0.2f, 0);
            bedBase.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(stationsFolder, "Mat_Bed", new Color(0.55f, 0.30f, 0.40f));
            // Headboard
            var bedHead = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bedHead.transform.SetParent(bedRoot.transform, false);
            bedHead.transform.localScale = new Vector3(0.4f, 0.6f, 1f);
            bedHead.transform.localPosition = new Vector3(0.8f, 0.5f, 0);
            bedHead.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(stationsFolder, "Mat_BedHead", new Color(0.40f, 0.25f, 0.20f));
            bedRoot.AddComponent<VoxelEngine.Building.Bed>();
            var bedPrefab = PrefabUtility.SaveAsPrefabAsset(bedRoot, bedPath);
            Object.DestroyImmediate(bedRoot);

            var blockBed = MakeBlock(blocksFolder, "Block_Bed", "Bed", new Color(0.55f,0.30f,0.40f), bedPrefab, "Stations");
            blockBed.description = "Right-click to set your respawn point here. If you die without a bed, you'll respawn at the world spawn.";

            // ----- Recipes -----
            var registry = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeRegistry>();

            // Tier 0 (player inventory)
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_Plank",         "Wooden Plank",       plank,        2, VoxelEngine.Crafting.StationTier.None, (woodLog, 1)));
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_PickWood",      "Wooden Pickaxe",     pickWood,     1, VoxelEngine.Crafting.StationTier.None, (woodLog, 3), (plank, 2)));
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_AxeWood",       "Wooden Axe",         axeWood,      1, VoxelEngine.Crafting.StationTier.None, (woodLog, 3), (plank, 2)));
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_CraftingBench", "Crafting Bench",     blockBench,   1, VoxelEngine.Crafting.StationTier.None, (woodLog, 4), (plank, 4)));

            // Tier 1 (Crafting Bench)
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_PickStone",     "Stone Pickaxe",      pickStone,    1, VoxelEngine.Crafting.StationTier.CraftingBench, (stone, 5), (plank, 2)));
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_LevelingTool",  "Leveling Tool",      lvTool,       1, VoxelEngine.Crafting.StationTier.CraftingBench, (ironIngot, 2), (plank, 4)));
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_Furnace",       "Furnace",            blockFurnace, 1, VoxelEngine.Crafting.StationTier.CraftingBench, (stone, 8)));
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_Chest",         "Chest",              blockChest,   1, VoxelEngine.Crafting.StationTier.CraftingBench, (plank, 8)));
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_Bed",           "Bed",                blockBed,     1, VoxelEngine.Crafting.StationTier.CraftingBench, (plank, 6), (woodLog, 2)));

            // Tier 2 (Furnace) - smelting recipes are separate
            // Tier 3 (Assembler)
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_PickIron",      "Iron Pickaxe",       pickIron,     1, VoxelEngine.Crafting.StationTier.Assembler, (ironIngot, 3), (plank, 2)));
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_AxeIron",       "Iron Axe",           axeIron,      1, VoxelEngine.Crafting.StationTier.Assembler, (ironIngot, 3), (plank, 2)));
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_GrinderTool",   "Grinder",            grinderTool,  1, VoxelEngine.Crafting.StationTier.Assembler, (ironIngot, 4), (plank, 1)));
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_PickSteel",     "Steel Pickaxe",      pickSteel,    1, VoxelEngine.Crafting.StationTier.Assembler, (steelIngot, 3), (plank, 1)));
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_Assembler",     "Assembler",          blockAssembler,1,VoxelEngine.Crafting.StationTier.CraftingBench, (ironIngot, 6), (plank, 4), (stone, 8)));

            AssetDatabase.CreateAsset(registry, $"{ASSET_ROOT}/RecipeRegistry.asset");

            // Also wire it into any existing GameUIController in the currently-open scene
            // so the player doesn't have to re-run step 2.
            var existingUis = Object.FindObjectsByType<VoxelEngine.UI.GameUIController>(FindObjectsInactive.Include);
            foreach (var ui in existingUis)
            {
                ui.recipeRegistry = registry;
                EditorUtility.SetDirty(ui);
            }

            // ----- Smelting recipes attached to the Furnace prefab -----
            var smIron   = MakeSmelt(recipesFolder, "Smelt_Iron",   iron,   1, ironIngot,   1, 4f);
            var smCopper = MakeSmelt(recipesFolder, "Smelt_Copper", copper, 1, copperIngot, 1, 4f);
            var smSteel  = MakeSmelt(recipesFolder, "Smelt_Steel",  iron,   1, steelIngot,  1, 8f); // simplistic — needs coal as fuel
            // Modifying components on a saved prefab requires opening it via the prefab API
            // and re-saving — EditorUtility.SetDirty alone doesn't persist.
            var smList = new System.Collections.Generic.List<VoxelEngine.Crafting.SmeltingRecipe> { smIron, smCopper, smSteel };
            AssignFurnaceRecipes(furnacePrefab, smList);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Configure tree scatter prefabs to drop wood logs on chop.
            ConfigureTreeChopping(woodLog);

            EditorUtility.DisplayDialog("Voxel Engine",
                "Crafting content created!\n\n" +
                "* 6 placeable blocks (Bench, Furnace, Assembler, Chest, Foundation, Wall)\n" +
                "* 6 tools (4 pickaxes + 2 axes) with mining tiers\n" +
                "* 16 recipes across all 3 station tiers\n" +
                "* 3 smelting recipes (auto-loaded into Furnace)\n\n" +
                "Trees now drop Wood Logs when chopped.\n" +
                "RecipeRegistry asset is wired into the GameUI automatically when you re-run Step 2.", "OK");
        }

        // ============================================================
        //                STEP 4 helper builders
        // ============================================================
        // Map a grid-block prefab name to its visual style.
        private static VoxelEngine.GridSystem.GridBlockMeshBuilder.Style GridStyleFor(string name)
        {
            string n = name.ToLower();
            if (n.Contains("cockpit"))    return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Cockpit;
            if (n.Contains("thruster"))   return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Thruster;
            if (n.Contains("battery"))    return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Battery;
            if (n.Contains("armor"))      return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Armor;
            if (n.Contains("glass"))      return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Glass;
            if (n.Contains("cargo"))      return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Cargo;
            if (n.Contains("liquidtank")) return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.LiquidTank;
            if (n.Contains("gastank"))    return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.GasTank;
            if (n.Contains("drill"))      return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Drill;
            if (n.Contains("grinder"))    return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Grinder;
            if (n.Contains("weapon"))     return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Weapon;
            if (n.Contains("docking"))    return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.DockingPort;
            if (n.Contains("wheel"))      return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Wheel;
            if (n.Contains("landing"))    return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.LandingGear;
            if (n.Contains("solar"))      return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.SolarPanel;
            if (n.Contains("reactor"))    return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Reactor;
            if (n.Contains("gyroscope"))  return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Gyroscope;
            if (n.Contains("refinery"))   return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Refinery;
            if (n.Contains("chemical"))   return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.ChemicalPlant;
            if (n.Contains("furnace"))    return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Refinery;
            if (n.Contains("h2o2"))       return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.H2O2;
            if (n.Contains("demolisher")) return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Demolisher;
            if (n.Contains("itempipe"))   return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.ItemPipe;
            if (n.Contains("gaspipe"))    return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.GasPipe;
            if (n.Contains("liquidpipe")) return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.LiquidPipe;
            return VoxelEngine.GridSystem.GridBlockMeshBuilder.Style.Generic;
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
                var leaf   = System.IO.Path.GetFileName(path);
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }

        private static VoxelEngine.Items.ResourceItem MakeResource(string folder, string display, Color tint, int maxStack,
            VoxelEngine.Items.ResourceCategory cat, float fuelSeconds = 0f, string uiCategory = null)
        {
            string id = display.ToLower().Replace(" ", "_");
            string path = $"{folder}/Item_{display.Replace(" ", "")}.asset";
            var item = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>(path);
            if (item == null) item = ScriptableObject.CreateInstance<VoxelEngine.Items.ResourceItem>();
            item.itemId = id; item.displayName = display; item.iconTint = tint;
            item.maxStack = maxStack; item.massPerUnit = 1f;
            item.subcategory = cat;
            item.fuelSeconds = fuelSeconds;
            // UI grouping: default to "Resources" but allow override (e.g. "Power", "Building").
            item.category = uiCategory ?? (cat == VoxelEngine.Items.ResourceCategory.Ingot ? "Ingots" : "Resources");
            if (!AssetDatabase.Contains(item)) AssetDatabase.CreateAsset(item, path);
            else EditorUtility.SetDirty(item);
            return item;
        }

        private static VoxelEngine.Items.ToolItem MakeTool(string folder, string display, VoxelEngine.Items.ToolType type,
            int tier, int dur, float strength, float brushRadius)
        {
            string id = display.ToLower().Replace(" ", "_");
            string path = $"{folder}/Tool_{display.Replace(" ", "")}.asset";
            var t = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ToolItem>(path);
            if (t == null) t = ScriptableObject.CreateInstance<VoxelEngine.Items.ToolItem>();
            t.itemId = id; t.displayName = display; t.maxStack = 1;
            t.toolType = type; t.miningTier = tier;
            t.maxDurability = dur; t.strength = strength; t.brushRadius = brushRadius; t.fireRate = 5f;
            t.iconTint = type == VoxelEngine.Items.ToolType.Pickaxe ? new Color(0.7f,0.7f,0.78f) : new Color(0.6f,0.45f,0.30f);
            t.category = "Tools";
            if (!AssetDatabase.Contains(t)) AssetDatabase.CreateAsset(t, path);
            else EditorUtility.SetDirty(t);
            return t;
        }

        private static VoxelEngine.Items.BlockItem MakeBlock(string folder, string assetName, string display, Color tint, GameObject prefab, string uiCategory = "Stations")
        {
            string path = $"{folder}/{assetName}.asset";
            var b = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.BlockItem>(path);
            if (b == null) b = ScriptableObject.CreateInstance<VoxelEngine.Items.BlockItem>();
            b.itemId = assetName.ToLower(); b.displayName = display;
            b.iconTint = tint; b.maxStack = 99; b.massPerUnit = 4f;
            b.placedPrefab = prefab; b.gridSize = Vector3Int.one;
            b.allowStacking = true; b.blockHealth = 200; b.miningTier = 1;
            b.category = uiCategory;
            if (!AssetDatabase.Contains(b)) AssetDatabase.CreateAsset(b, path);
            else EditorUtility.SetDirty(b);
            return b;
        }

        private static VoxelEngine.Crafting.RecipeDefinition MakeRecipe(string folder, string assetName, string display,
            VoxelEngine.Items.ItemDefinition output, int outputCount,
            VoxelEngine.Crafting.StationTier station, params (VoxelEngine.Items.ItemDefinition item, int count)[] inputs)
        {
            string path = $"{folder}/{assetName}.asset";
            var r = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
            r.displayName = display; r.outputItem = output; r.outputCount = outputCount;
            // Set a default craft time based on station tier; users can override later via the inspector.
            float defaultSeconds = station switch
            {
                VoxelEngine.Crafting.StationTier.None          => 0f,
                VoxelEngine.Crafting.StationTier.CraftingBench => 2f,
                VoxelEngine.Crafting.StationTier.Furnace       => 0f,
                VoxelEngine.Crafting.StationTier.Assembler     => 4f,
                _ => 0f
            };
            r.requiredStation = station; r.craftSeconds = defaultSeconds; r.unlockedByDefault = true;
            r.inputs = new VoxelEngine.Crafting.RecipeIngredient[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
                r.inputs[i] = new VoxelEngine.Crafting.RecipeIngredient { item = inputs[i].item, count = inputs[i].count };
            AssetDatabase.CreateAsset(r, path);
            return r;
        }

        private static VoxelEngine.Crafting.SmeltingRecipe MakeSmelt(string folder, string assetName,
            VoxelEngine.Items.ItemDefinition input, int inputCount,
            VoxelEngine.Items.ItemDefinition output, int outputCount, float seconds)
        {
            string path = $"{folder}/{assetName}.asset";
            var r = ScriptableObject.CreateInstance<VoxelEngine.Crafting.SmeltingRecipe>();
            r.input = input; r.inputCount = inputCount;
            r.output = output; r.outputCount = outputCount; r.smeltSeconds = seconds;
            AssetDatabase.CreateAsset(r, path);
            return r;
        }

        private static void AssignFurnaceRecipes(GameObject prefab, System.Collections.Generic.List<VoxelEngine.Crafting.SmeltingRecipe> recipes)
        {
            if (prefab == null) return;
            string path = AssetDatabase.GetAssetPath(prefab);
            // Load the prefab content into an editable scene-instance, edit it, save back.
            var contents = PrefabUtility.LoadPrefabContents(path);
            var furn = contents.GetComponent<VoxelEngine.Crafting.Furnace>();
            if (furn != null) furn.knownRecipes = new System.Collections.Generic.List<VoxelEngine.Crafting.SmeltingRecipe>(recipes);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        private static void AssignElectricFurnaceRecipes(GameObject prefab, System.Collections.Generic.List<VoxelEngine.Crafting.SmeltingRecipe> recipes)
        {
            if (prefab == null) return;
            string path = AssetDatabase.GetAssetPath(prefab);
            var contents = PrefabUtility.LoadPrefabContents(path);
            var furn = contents.GetComponent<VoxelEngine.Crafting.ElectricFurnace>();
            if (furn != null) furn.knownRecipes = new System.Collections.Generic.List<VoxelEngine.Crafting.SmeltingRecipe>(recipes);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        private static GameObject MakeStationPrefab(string folder, string name, Color color,
            VoxelEngine.Crafting.StationTier tier, string display, bool isFurnace = false)
        {
            string path = $"{folder}/{name}.prefab";
            var root = new GameObject(name);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Mesh";
            cube.transform.SetParent(root.transform, false);
            var mat = MakeColoredMat(folder, $"Mat_{name}", color);
            cube.GetComponent<Renderer>().sharedMaterial = mat;
            // Keep the box collider for raycast hits.
            var st = root.AddComponent<VoxelEngine.Crafting.CraftingStation>();
            st.tier = tier; st.displayName = display;
            if (isFurnace) root.AddComponent<VoxelEngine.Crafting.Furnace>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }
        private static GameObject MakeChestPrefab(string folder, string name, Color color, int size)
        {
            string path = $"{folder}/{name}.prefab";
            var root = new GameObject(name);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Mesh";
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = new Vector3(1f, 0.7f, 0.7f);
            var mat = MakeColoredMat(folder, $"Mat_{name}", color);
            cube.GetComponent<Renderer>().sharedMaterial = mat;
            var c = root.AddComponent<VoxelEngine.Building.Chest>();
            c.size = size;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }
        private static GameObject MakeBuildingPrefab(string folder, string name, Color color, Vector3 scale)
        {
            string path = $"{folder}/{name}.prefab";
            var root = new GameObject(name);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Mesh";
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = scale;
            var mat = MakeColoredMat(folder, $"Mat_{name}", color);
            cube.GetComponent<Renderer>().sharedMaterial = mat;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void ConfigureTreeChopping(VoxelEngine.Items.ItemDefinition logItem)
        {
            // Add the Tree component to the existing scatter tree prefabs created earlier.
            string[] treeNames = { "Tree_Oak", "Tree_Pine", "Tree_Dead" };
            string scatterFolder = ASSET_ROOT + "/Scatter";
            foreach (var n in treeNames)
            {
                string path = $"{scatterFolder}/{n}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (go.GetComponent<VoxelEngine.Trees.Tree>() == null)
                {
                    var t = go.AddComponent<VoxelEngine.Trees.Tree>();
                    t.maxHp = 80; t.minLogs = 2; t.maxLogs = 4;
                    t.logItem = logItem;
                    t.preferredTool = VoxelEngine.Items.ToolType.Axe;
                }
                PrefabUtility.SaveAsPrefabAsset(go, path);
                Object.DestroyImmediate(go);
            }
        }

        // ============================================================
        //         STEP 5 - TIERED BUILDING CONTENT (RUST-STYLE)
        // ============================================================
        private void BuildTieredContent()
        {
            const string tieredFolder    = ASSET_ROOT + "/Tiered";
            const string tieredPrefabs   = tieredFolder + "/Prefabs";
            const string tieredDefs      = tieredFolder + "/Definitions";
            const string tieredTokens    = tieredFolder + "/Tokens";
            const string tieredMats      = tieredFolder + "/Materials";
            const string tieredRecipes   = tieredFolder + "/Recipes";

            EnsureFolder(tieredFolder);
            EnsureFolder(tieredPrefabs);
            EnsureFolder(tieredDefs);
            EnsureFolder(tieredTokens);
            EnsureFolder(tieredMats);
            EnsureFolder(tieredRecipes);

            // ---------- Pull common item references created by Step 1 / Step 4 ----------
            var stone   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{ITEM_FOLDER}/Item_Stone.asset");
            var ironOre = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{ITEM_FOLDER}/Item_Iron.asset");
            string itemsFolder = ASSET_ROOT + "/Items";
            var woodLog     = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_WoodLog.asset");
            var plank       = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_WoodenPlank.asset");
            var ironIngot   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_IronIngot.asset");
            var steelIngot  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_SteelIngot.asset");
            if (woodLog == null || plank == null || stone == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine",
                    "Run Step 4 (Build Crafting Content) first — it creates Wood Log / Plank / Iron Ingot / Steel Ingot which the building system needs.",
                    "OK");
                return;
            }

            // ---------- Tier materials (one per tier, shared across all 9 families to keep GPU Resident Drawer batches dense) ----------
            var matWood  = MakeColoredMat(tieredMats, "Mat_Wood",  new Color(0.55f, 0.40f, 0.25f));
            var matStone = MakeColoredMat(tieredMats, "Mat_Stone", new Color(0.55f, 0.55f, 0.58f));
            var matIron  = MakeColoredMat(tieredMats, "Mat_Iron",  new Color(0.78f, 0.78f, 0.85f));
            var matSteel = MakeColoredMat(tieredMats, "Mat_Steel", new Color(0.40f, 0.45f, 0.55f));
            var tierMats = new[] { matWood, matStone, matIron, matSteel };

            // ---------- Build the 9 families, each with 4 tier prefabs ----------
            var registry = ScriptableObject.CreateInstance<VoxelEngine.Building.Tiered.TieredBlockRegistry>();

            VoxelEngine.Building.Tiered.TieredBlockDefinition MakeFamily(
                VoxelEngine.Building.Tiered.BuildFamily fam,
                string display,
                System.Action<GameObject, Material> meshBuilder,
                System.Action<GameObject> socketBuilder,
                VoxelEngine.Building.Tiered.TierCost placeCost,
                VoxelEngine.Building.Tiered.TierCost upWoodToStone,
                VoxelEngine.Building.Tiered.TierCost upStoneToIron,
                VoxelEngine.Building.Tiered.TierCost upIronToSteel)
            {
                var def = ScriptableObject.CreateInstance<VoxelEngine.Building.Tiered.TieredBlockDefinition>();
                def.family = fam;
                def.displayName = display;
                def.placeCost     = placeCost;
                def.woodToStone   = upWoodToStone;
                def.stoneToIron   = upStoneToIron;
                def.ironToSteel   = upIronToSteel;

                for (int t = 0; t < 4; t++)
                {
                    var tier = (VoxelEngine.Building.Tiered.BuildTier)t;
                    string name = $"{display}_{tier}";
                    string path = $"{tieredPrefabs}/{name}.prefab";

                    // Build root.
                    var root = new GameObject(name);
                    meshBuilder(root, tierMats[t]);
                    socketBuilder(root);

                    // Add the placed-block component.
                    root.AddComponent<VoxelEngine.Building.Tiered.PlacedTieredBlock>();

                    // Save as prefab.
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                    Object.DestroyImmediate(root);

                    if (tier == VoxelEngine.Building.Tiered.BuildTier.Wood)  def.woodPrefab  = prefab;
                    if (tier == VoxelEngine.Building.Tiered.BuildTier.Stone) def.stonePrefab = prefab;
                    if (tier == VoxelEngine.Building.Tiered.BuildTier.Iron)  def.ironPrefab  = prefab;
                    if (tier == VoxelEngine.Building.Tiered.BuildTier.Steel) def.steelPrefab = prefab;
                }
                AssetDatabase.CreateAsset(def, $"{tieredDefs}/TBlock_{display}.asset");
                registry.definitions.Add(def);
                return def;
            }

            // Helper: a TierCost from a list of (item, count) tuples.
            VoxelEngine.Building.Tiered.TierCost Cost(params (VoxelEngine.Items.ItemDefinition item, int n)[] items)
            {
                var c = new VoxelEngine.Building.Tiered.TierCost();
                c.items = new VoxelEngine.Building.Tiered.Ingredient[items.Length];
                for (int i = 0; i < items.Length; i++)
                    c.items[i] = new VoxelEngine.Building.Tiered.Ingredient { item = items[i].item, count = items[i].n };
                return c;
            }

            // ---------- Family-by-family meshes + sockets ----------
            // (All built procedurally with primitive cubes/cylinders for v1 — replace with art later.)

            // FOUNDATION — full 1x1x1 cube. Sockets: top, north, south, east, west.
            MakeFamily(VoxelEngine.Building.Tiered.BuildFamily.Foundation, "Foundation",
                (root, mat) => { AddBox(root, mat, Vector3.zero, Vector3.one); },
                (root) => {
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.Top,    new Vector3(0, 0.5f, 0),   VoxelEngine.Building.Tiered.BuildFamily.Foundation);
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.North,  new Vector3(0, 0,  0.5f),  VoxelEngine.Building.Tiered.BuildFamily.Foundation);
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.South,  new Vector3(0, 0, -0.5f),  VoxelEngine.Building.Tiered.BuildFamily.Foundation);
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.East,   new Vector3( 0.5f, 0, 0),  VoxelEngine.Building.Tiered.BuildFamily.Foundation);
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.West,   new Vector3(-0.5f, 0, 0),  VoxelEngine.Building.Tiered.BuildFamily.Foundation);
                },
                Cost((woodLog, 4)),
                Cost((stone, 8)),
                Cost((ironIngot, 4)),
                Cost((steelIngot, 4))
            );

            // WALL — 1.0 x 1.0 x 0.2.
            MakeFamily(VoxelEngine.Building.Tiered.BuildFamily.Wall, "Wall",
                (root, mat) => { AddBox(root, mat, new Vector3(0, 0.5f, 0), new Vector3(1f, 1f, 0.2f)); },
                (root) => {
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.Top, new Vector3(0, 1f, 0), VoxelEngine.Building.Tiered.BuildFamily.Wall);
                },
                Cost((woodLog, 2), (plank, 2)),
                Cost((stone, 4)),
                Cost((ironIngot, 2)),
                Cost((steelIngot, 2))
            );

            // DOORWAY — wall with a 0.5x0.7 hole. We build it from 3 boxes (top + 2 sides).
            MakeFamily(VoxelEngine.Building.Tiered.BuildFamily.Doorway, "Doorway",
                (root, mat) => {
                    AddBox(root, mat, new Vector3(-0.4f, 0.5f, 0), new Vector3(0.2f, 1f,   0.2f)); // left jamb
                    AddBox(root, mat, new Vector3( 0.4f, 0.5f, 0), new Vector3(0.2f, 1f,   0.2f)); // right jamb
                    AddBox(root, mat, new Vector3(0,    0.875f, 0), new Vector3(1f,  0.25f, 0.2f)); // lintel
                },
                (root) => {
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.Top, new Vector3(0, 1f, 0), VoxelEngine.Building.Tiered.BuildFamily.Doorway);
                },
                Cost((woodLog, 3), (plank, 2)),
                Cost((stone, 5)),
                Cost((ironIngot, 3)),
                Cost((steelIngot, 3))
            );

            // WINDOW — wall with a 0.5x0.4 mid-height hole.
            MakeFamily(VoxelEngine.Building.Tiered.BuildFamily.Window, "Window",
                (root, mat) => {
                    AddBox(root, mat, new Vector3(-0.4f, 0.5f, 0), new Vector3(0.2f, 1f,   0.2f));
                    AddBox(root, mat, new Vector3( 0.4f, 0.5f, 0), new Vector3(0.2f, 1f,   0.2f));
                    AddBox(root, mat, new Vector3(0,    0.85f, 0), new Vector3(1f,   0.3f, 0.2f));
                    AddBox(root, mat, new Vector3(0,    0.15f, 0), new Vector3(1f,   0.3f, 0.2f));
                },
                (root) => {
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.Top, new Vector3(0, 1f, 0), VoxelEngine.Building.Tiered.BuildFamily.Window);
                },
                Cost((woodLog, 2), (plank, 3)),
                Cost((stone, 4)),
                Cost((ironIngot, 2)),
                Cost((steelIngot, 2))
            );

            // FLOOR — 1.0 x 0.2 x 1.0 slab.
            MakeFamily(VoxelEngine.Building.Tiered.BuildFamily.Floor, "Floor",
                (root, mat) => { AddBox(root, mat, new Vector3(0, 0.1f, 0), new Vector3(1f, 0.2f, 1f)); },
                (root) => {
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.Top, new Vector3(0, 0.2f, 0), VoxelEngine.Building.Tiered.BuildFamily.Floor);
                },
                Cost((woodLog, 2), (plank, 2)),
                Cost((stone, 5)),
                Cost((ironIngot, 2)),
                Cost((steelIngot, 2))
            );

            // STAIRS — wedge built from 4 stacked boxes.
            MakeFamily(VoxelEngine.Building.Tiered.BuildFamily.Stairs, "Stairs",
                (root, mat) => {
                    for (int i = 0; i < 4; i++)
                    {
                        float h = 0.25f * (i + 1);
                        AddBox(root, mat,
                            new Vector3(0, 0.125f * (i + 1), -0.375f + 0.25f * i),
                            new Vector3(1f, 0.25f, 0.25f));
                    }
                },
                (root) => { /* stairs don't typically host other pieces */ },
                Cost((woodLog, 3), (plank, 3)),
                Cost((stone, 6)),
                Cost((ironIngot, 3)),
                Cost((steelIngot, 3))
            );

            // ROOF — 1x0.2x1 slab with sloped top (approximated by a tilted box).
            MakeFamily(VoxelEngine.Building.Tiered.BuildFamily.Roof, "Roof",
                (root, mat) => {
                    var go = AddBox(root, mat, new Vector3(0, 0.5f, 0), new Vector3(1f, 0.2f, 1f));
                    go.transform.localEulerAngles = new Vector3(20f, 0, 0);
                },
                (root) => { /* peak */ },
                Cost((woodLog, 3), (plank, 2)),
                Cost((stone, 5)),
                Cost((ironIngot, 2)),
                Cost((steelIngot, 2))
            );

            // PILLAR — 0.25 x 1.0 x 0.25 column.
            MakeFamily(VoxelEngine.Building.Tiered.BuildFamily.Pillar, "Pillar",
                (root, mat) => { AddBox(root, mat, new Vector3(0, 0.5f, 0), new Vector3(0.25f, 1f, 0.25f)); },
                (root) => {
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.Top,   new Vector3(0,  1f, 0), VoxelEngine.Building.Tiered.BuildFamily.Pillar);
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.North, new Vector3(0,  0.5f,  0.5f), VoxelEngine.Building.Tiered.BuildFamily.Pillar);
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.South, new Vector3(0,  0.5f, -0.5f), VoxelEngine.Building.Tiered.BuildFamily.Pillar);
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.East,  new Vector3( 0.5f, 0.5f, 0),  VoxelEngine.Building.Tiered.BuildFamily.Pillar);
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.West,  new Vector3(-0.5f, 0.5f, 0),  VoxelEngine.Building.Tiered.BuildFamily.Pillar);
                },
                Cost((woodLog, 1), (plank, 1)),
                Cost((stone, 3)),
                Cost((ironIngot, 1)),
                Cost((steelIngot, 1))
            );

            // HALF WALL — 1 x 0.5 x 0.2.
            MakeFamily(VoxelEngine.Building.Tiered.BuildFamily.HalfWall, "HalfWall",
                (root, mat) => { AddBox(root, mat, new Vector3(0, 0.25f, 0), new Vector3(1f, 0.5f, 0.2f)); },
                (root) => {
                    AddSocket(root, VoxelEngine.Building.Tiered.SocketSide.Top, new Vector3(0, 0.5f, 0), VoxelEngine.Building.Tiered.BuildFamily.HalfWall);
                },
                Cost((woodLog, 1), (plank, 1)),
                Cost((stone, 2)),
                Cost((ironIngot, 1)),
                Cost((steelIngot, 1))
            );

            AssetDatabase.CreateAsset(registry, $"{tieredFolder}/TieredBlockRegistry.asset");

            // ---------- Build Tokens (one per family) ----------
            VoxelEngine.Building.Tiered.BuildToken MakeToken(
                VoxelEngine.Building.Tiered.BuildFamily fam, string display, Color tint, string description)
            {
                var tok = ScriptableObject.CreateInstance<VoxelEngine.Building.Tiered.BuildToken>();
                tok.family      = fam;
                tok.itemId      = "build_" + display.ToLower();
                tok.displayName = display + " (Build)";
                tok.description = description;
                tok.iconTint    = tint;
                tok.maxStack    = 99;
                AssetDatabase.CreateAsset(tok, $"{tieredTokens}/Token_{display}.asset");
                return tok;
            }
            var tokFoundation = MakeToken(VoxelEngine.Building.Tiered.BuildFamily.Foundation, "Foundation", new Color(0.55f, 0.40f, 0.25f), "The base of every building. Place on flat ground first; everything else snaps to its top and edges. Hold in active hotbar slot to enter build mode. LMB places at Wood tier (consumes resources). Use the Hammer to upgrade placed pieces. Toggle grid-snap with G. Press R (or Ctrl+Wheel) to rotate the ghost 90 degrees.");
            var tokWall       = MakeToken(VoxelEngine.Building.Tiered.BuildFamily.Wall,       "Wall",       new Color(0.55f, 0.40f, 0.25f), "A solid 1x1m wall panel. Snaps to foundation top edges. Hold in active hotbar slot to enter build mode. LMB places at Wood tier (consumes resources). Use the Hammer to upgrade placed pieces. Toggle grid-snap with G. Press R (or Ctrl+Wheel) to rotate the ghost 90 degrees.");
            var tokDoorway    = MakeToken(VoxelEngine.Building.Tiered.BuildFamily.Doorway,    "Doorway",    new Color(0.55f, 0.40f, 0.25f), "A wall with a doorway opening. Walk through it; place a door later (coming soon). Hold in active hotbar slot to enter build mode. LMB places at Wood tier (consumes resources). Use the Hammer to upgrade placed pieces. Toggle grid-snap with G. Press R (or Ctrl+Wheel) to rotate the ghost 90 degrees.");
            var tokWindow     = MakeToken(VoxelEngine.Building.Tiered.BuildFamily.Window,     "Window",     new Color(0.55f, 0.40f, 0.25f), "A wall with a window opening. Lets light through and lets you peek out. Hold in active hotbar slot to enter build mode. LMB places at Wood tier (consumes resources). Use the Hammer to upgrade placed pieces. Toggle grid-snap with G. Press R (or Ctrl+Wheel) to rotate the ghost 90 degrees.");
            var tokFloor      = MakeToken(VoxelEngine.Building.Tiered.BuildFamily.Floor,      "Floor",      new Color(0.55f, 0.40f, 0.25f), "A 1x1m floor slab. Place on top of walls/pillars to make second stories. Hold in active hotbar slot to enter build mode. LMB places at Wood tier (consumes resources). Use the Hammer to upgrade placed pieces. Toggle grid-snap with G. Press R (or Ctrl+Wheel) to rotate the ghost 90 degrees.");
            var tokStairs     = MakeToken(VoxelEngine.Building.Tiered.BuildFamily.Stairs,     "Stairs",     new Color(0.55f, 0.40f, 0.25f), "A staircase that connects two height levels. Place against a foundation edge. Hold in active hotbar slot to enter build mode. LMB places at Wood tier (consumes resources). Use the Hammer to upgrade placed pieces. Toggle grid-snap with G. Press R (or Ctrl+Wheel) to rotate the ghost 90 degrees.");
            var tokRoof       = MakeToken(VoxelEngine.Building.Tiered.BuildFamily.Roof,       "Roof",       new Color(0.55f, 0.40f, 0.25f), "A sloped roof slab. Place on top of walls to seal the room. Hold in active hotbar slot to enter build mode. LMB places at Wood tier (consumes resources). Use the Hammer to upgrade placed pieces. Toggle grid-snap with G. Press R (or Ctrl+Wheel) to rotate the ghost 90 degrees.");
            var tokPillar     = MakeToken(VoxelEngine.Building.Tiered.BuildFamily.Pillar,     "Pillar",     new Color(0.55f, 0.40f, 0.25f), "A vertical column. Hosts walls on its sides and floors/roofs on its top. Hold in active hotbar slot to enter build mode. LMB places at Wood tier (consumes resources). Use the Hammer to upgrade placed pieces. Toggle grid-snap with G. Press R (or Ctrl+Wheel) to rotate the ghost 90 degrees.");
            var tokHalfWall   = MakeToken(VoxelEngine.Building.Tiered.BuildFamily.HalfWall,   "HalfWall",   new Color(0.55f, 0.40f, 0.25f), "A half-height wall (waist height). Useful for railings and counters. Hold in active hotbar slot to enter build mode. LMB places at Wood tier (consumes resources). Use the Hammer to upgrade placed pieces. Toggle grid-snap with G. Press R (or Ctrl+Wheel) to rotate the ghost 90 degrees.");

            // ---------- Hammer tool ----------
            var hammer = ScriptableObject.CreateInstance<VoxelEngine.Building.Tiered.Hammer>();
            hammer.itemId        = "hammer";
            hammer.displayName   = "Hammer";
            hammer.toolType      = VoxelEngine.Items.ToolType.Other;
            hammer.miningTier    = 0;
            hammer.maxDurability = 500;
            hammer.strength      = 1f;     // hammer doesn't do damage in normal mining; it upgrades.
            hammer.fireRate      = 4f;
            hammer.brushRadius   = 0.1f;
            hammer.iconTint      = new Color(0.85f, 0.55f, 0.20f);
            hammer.description   = "Used to upgrade placed buildings to their next tier. " +
                "Hold in active hotbar slot, look at a placed wood/stone/iron building, then LMB. " +
                "Each upgrade consumes the cost shown on the block. Wood -> Stone -> Iron -> Steel.";
            AssetDatabase.CreateAsset(hammer, $"{itemsFolder}/Tool_Hammer.asset");

            // ---------- Add recipes for all 9 build tokens + the hammer (Crafting Bench tier) ----------
            var recipeRegistry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>($"{ASSET_ROOT}/RecipeRegistry.asset");
            if (recipeRegistry == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine",
                    "Run Step 4 first — RecipeRegistry.asset doesn\'t exist yet.", "OK");
                return;
            }

            VoxelEngine.Crafting.RecipeDefinition AddRecipe(string assetName, string display,
                VoxelEngine.Items.ItemDefinition output, int outputCount,
                VoxelEngine.Crafting.StationTier station,
                params (VoxelEngine.Items.ItemDefinition item, int n)[] inputs)
            {
                string path = $"{tieredRecipes}/{assetName}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(path);
                if (existing != null) AssetDatabase.DeleteAsset(path);
                var r = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                r.displayName = display;
                r.outputItem = output;
                r.outputCount = outputCount;
                r.requiredStation = station;
                r.craftSeconds = 0f;
                r.unlockedByDefault = true;
                r.inputs = new VoxelEngine.Crafting.RecipeIngredient[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                    r.inputs[i] = new VoxelEngine.Crafting.RecipeIngredient { item = inputs[i].item, count = inputs[i].n };
                AssetDatabase.CreateAsset(r, path);
                recipeRegistry.recipes.Add(r);
                return r;
            }

            // Hammer is craftable in inventory (you need it to upgrade anything).
            AddRecipe("Recipe_Hammer", "Hammer", hammer, 1,
                VoxelEngine.Crafting.StationTier.None,
                (woodLog, 2), (plank, 2));

            // Build tokens — all from the Crafting Bench so the player has a small barrier.
            AddRecipe("Recipe_Tok_Foundation", "Foundation Token", tokFoundation, 1,
                VoxelEngine.Crafting.StationTier.CraftingBench, (plank, 2));
            AddRecipe("Recipe_Tok_Wall",       "Wall Token",       tokWall,       1,
                VoxelEngine.Crafting.StationTier.CraftingBench, (plank, 2));
            AddRecipe("Recipe_Tok_Doorway",    "Doorway Token",    tokDoorway,    1,
                VoxelEngine.Crafting.StationTier.CraftingBench, (plank, 2));
            AddRecipe("Recipe_Tok_Window",     "Window Token",     tokWindow,     1,
                VoxelEngine.Crafting.StationTier.CraftingBench, (plank, 2));
            AddRecipe("Recipe_Tok_Floor",      "Floor Token",      tokFloor,      1,
                VoxelEngine.Crafting.StationTier.CraftingBench, (plank, 2));
            AddRecipe("Recipe_Tok_Stairs",     "Stairs Token",     tokStairs,     1,
                VoxelEngine.Crafting.StationTier.CraftingBench, (plank, 2));
            AddRecipe("Recipe_Tok_Roof",       "Roof Token",       tokRoof,       1,
                VoxelEngine.Crafting.StationTier.CraftingBench, (plank, 2));
            AddRecipe("Recipe_Tok_Pillar",     "Pillar Token",     tokPillar,     1,
                VoxelEngine.Crafting.StationTier.CraftingBench, (plank, 2));
            AddRecipe("Recipe_Tok_HalfWall",   "Half Wall Token",  tokHalfWall,   1,
                VoxelEngine.Crafting.StationTier.CraftingBench, (plank, 2));

            EditorUtility.SetDirty(recipeRegistry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ---------- Wire registry into any existing BuildSystemV2 in the scene ----------
            var systems = Object.FindObjectsByType<VoxelEngine.Building.Tiered.BuildSystemV2>(FindObjectsInactive.Include);
            foreach (var sys in systems)
            {
                sys.registry = registry;
                EditorUtility.SetDirty(sys);
            }

            EditorUtility.DisplayDialog("Voxel Engine",
                "Tiered building content created!\n\n" +
                "* 36 prefabs (9 families x 4 tiers) in " + tieredPrefabs + "\n" +
                "* 9 build tokens + Hammer\n" +
                "* 10 new recipes added to RecipeRegistry\n" +
                "* TieredBlockRegistry asset created\n\n" +
                "Re-run Step 2 to spawn a player with BuildSystemV2 wired up,\n" +
                "or manually add the BuildSystemV2 component and assign the registry.",
                "OK");
        }

        // ---------- Tiered prefab building helpers (used by BuildTieredContent) ----------
        private static GameObject AddBox(GameObject parent, Material mat, Vector3 localPos, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Box";
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            // Keep collider on for raycasts to find the placed block.
            return go;
        }

        private static void AddSocket(GameObject parent,
            VoxelEngine.Building.Tiered.SocketSide side, Vector3 localPos,
            VoxelEngine.Building.Tiered.BuildFamily family)
        {
            var go = new GameObject($"Socket_{side}");
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            var sock = go.AddComponent<VoxelEngine.Building.Tiered.BuildSocket>();
            sock.side   = side;
            sock.family = family;
        }

        // ============================================================
        //              STEP 6 - POWER STARTER CONTENT
        // ============================================================
        private void BuildPowerContent()
        {
            const string powerFolder    = ASSET_ROOT + "/Power";
            const string wiresFolder    = powerFolder + "/Wires";
            const string prefabsFolder  = powerFolder + "/Prefabs";
            const string blocksFolder   = powerFolder + "/Blocks";
            const string recipesFolder  = powerFolder + "/Recipes";

            EnsureFolder(powerFolder);
            EnsureFolder(wiresFolder);
            EnsureFolder(prefabsFolder);
            EnsureFolder(blocksFolder);
            EnsureFolder(recipesFolder);

            // ---- Pull required items ----
            string itemsFolder = ASSET_ROOT + "/Items";
            var copperIngot = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_CopperIngot.asset");
            var ironIngot   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_IronIngot.asset");
            var steelIngot  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_SteelIngot.asset");
            var stone       = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{ITEM_FOLDER}/Item_Stone.asset");
            var coal        = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{ITEM_FOLDER}/Item_Coal.asset");
            var goldOre     = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{ITEM_FOLDER}/Item_Gold.asset");
            if (copperIngot == null || ironIngot == null || steelIngot == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine",
                    "Run Step 4 (Build Crafting Content) first - it creates Iron / Copper / Steel ingots which the power system needs.", "OK");
                return;
            }

            // ---- 1) Wire tier definitions ----
            VoxelEngine.Power.WireDefinition MakeWire(string name, float cap, Color tint)
            {
                var w = ScriptableObject.CreateInstance<VoxelEngine.Power.WireDefinition>();
                w.displayName   = name;
                w.capacityWatts = cap;
                w.tint          = tint;
                w.connectRadius = 1.6f;
                AssetDatabase.CreateAsset(w, $"{wiresFolder}/Wire_{name.Replace(" ", "")}.asset");
                return w;
            }
            var wireCopper = MakeWire("Copper",         1000f, new Color(0.85f, 0.45f, 0.20f));
            var wireIron   = MakeWire("Iron",           5000f, new Color(0.78f, 0.78f, 0.85f));
            var wireGold   = MakeWire("Gold",          25000f, new Color(0.95f, 0.78f, 0.20f));
            var wireSuper  = MakeWire("Superconductor",100000f,new Color(0.45f, 0.85f, 1.00f));

            // ---- 2) Helper: make a power prefab (cube + power-component) ----
            GameObject MakePowerPrefab<T>(string assetName, Color color, Vector3 scale,
                                         System.Action<T> configure) where T : VoxelEngine.Power.PowerNode
            {
                string path = $"{prefabsFolder}/{assetName}.prefab";
                var root = new GameObject(assetName);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform, false);
                cube.transform.localScale = scale;
                var mat = MakeColoredMat(prefabsFolder, $"Mat_{assetName}", color);
                cube.GetComponent<Renderer>().sharedMaterial = mat;

                var node = root.AddComponent<T>();
                configure?.Invoke(node);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                return prefab;
            }

            // Cable prefabs (one per wire tier).
            var cableCu = MakePowerPrefab<VoxelEngine.Power.PowerCable>("Cable_Copper", wireCopper.tint, new Vector3(0.2f, 0.2f, 1f),
                c => { c.wire = wireCopper; });
            var cableFe = MakePowerPrefab<VoxelEngine.Power.PowerCable>("Cable_Iron", wireIron.tint, new Vector3(0.2f, 0.2f, 1f),
                c => { c.wire = wireIron; });
            var cableAu = MakePowerPrefab<VoxelEngine.Power.PowerCable>("Cable_Gold", wireGold.tint, new Vector3(0.2f, 0.2f, 1f),
                c => { c.wire = wireGold; });
            var cableSc = MakePowerPrefab<VoxelEngine.Power.PowerCable>("Cable_Superconductor", wireSuper.tint, new Vector3(0.2f, 0.2f, 1f),
                c => { c.wire = wireSuper; });

            // Generator (coal-fired, 800 W/s while burning fuel).
            var genPrefab = MakePowerPrefab<VoxelEngine.Power.PowerGenerator>("Generator_Coal",
                new Color(0.30f, 0.30f, 0.32f), new Vector3(1.5f, 1.5f, 1.5f),
                g => { g.wattsPerSecond = 800f; g.isOn = false; g.connectRadius = 1.8f; });
            // Add the fuel-slot wrapper component to the generator prefab.
            {
                string path = AssetDatabase.GetAssetPath(genPrefab);
                var contents = PrefabUtility.LoadPrefabContents(path);
                if (contents.GetComponent<VoxelEngine.Power.CoalGeneratorFuel>() == null)
                    contents.AddComponent<VoxelEngine.Power.CoalGeneratorFuel>();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // Battery (1000 Wh).
            var batPrefab = MakePowerPrefab<VoxelEngine.Power.PowerBattery>("Battery_Basic",
                new Color(0.20f, 0.50f, 0.85f), new Vector3(0.8f, 1.2f, 0.8f),
                b => { b.capacityWattHours = 1000f; b.ioRate = 200f; b.connectRadius = 1.5f; });

            // Light consumer (10 W/s).
            var lightPrefab = MakePowerPrefab<VoxelEngine.Power.PowerConsumer>("Light_Basic",
                new Color(1f, 0.95f, 0.6f), new Vector3(0.3f, 0.3f, 0.3f),
                c => { c.wattsPerSecond = 10f; c.connectRadius = 1.5f; });

            // ---- 3) BlockItems (placeable via existing BuildSystem / hotbar) ----
            VoxelEngine.Items.BlockItem MakePowerBlock(string assetName, string display, Color tint, GameObject prefab,
                                                       string desc, int hp = 200)
            {
                string path = $"{blocksFolder}/{assetName}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var b = ScriptableObject.CreateInstance<VoxelEngine.Items.BlockItem>();
                b.itemId       = assetName.ToLower();
                b.displayName  = display;
                b.description  = desc;
                b.iconTint     = tint;
                b.maxStack     = 99;
                b.massPerUnit  = 2f;
                b.placedPrefab = prefab;
                b.gridSize     = Vector3Int.one;
                b.allowStacking= false;
                b.blockHealth  = hp;
                b.miningTier   = 1;
                b.category     = "Power";
                AssetDatabase.CreateAsset(b, path);
                return b;
            }
            var blockCableCu = MakePowerBlock("Block_Cable_Copper", "Copper Cable", wireCopper.tint, cableCu,
                "Carries up to 1000 W between machines. Cheap but limited - upgrade to Iron for serious factories.");
            var blockCableFe = MakePowerBlock("Block_Cable_Iron", "Iron Cable", wireIron.tint, cableFe,
                "Carries up to 5000 W. Mid-tier wire for industrial machines.");
            var blockCableAu = MakePowerBlock("Block_Cable_Gold", "Gold Cable", wireGold.tint, cableAu,
                "Carries up to 25000 W. Premium wire for heavy power loads.");
            var blockCableSc = MakePowerBlock("Block_Cable_Superconductor", "Superconductor Cable", wireSuper.tint, cableSc,
                "Carries up to 100000 W. End-game wire with no practical bottleneck.");

            var blockGen   = MakePowerBlock("Block_Gen_Coal", "Coal Generator", new Color(0.30f,0.30f,0.32f), genPrefab,
                "Burns coal to produce 800 W of electricity. Connect cables to its sides.", hp: 600);
            var blockBat   = MakePowerBlock("Block_Battery", "Battery", new Color(0.20f,0.50f,0.85f), batPrefab,
                "Stores up to 1000 Wh. Charges from generator surplus, discharges to power deficits.", hp: 400);
            var blockLight = MakePowerBlock("Block_Light", "Power Light", new Color(1f,0.95f,0.6f), lightPrefab,
                "Consumes 10 W. Glows when powered (visualisation TBD).", hp: 80);

            // ---- 4) Recipes (Crafting Bench / Assembler) ----
            var recipeRegistry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>($"{ASSET_ROOT}/RecipeRegistry.asset");
            if (recipeRegistry == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine",
                    "Run Step 4 first - RecipeRegistry.asset doesn\'t exist.", "OK");
                return;
            }

            VoxelEngine.Crafting.RecipeDefinition AddRecipe(string assetName, string display,
                VoxelEngine.Items.ItemDefinition output, int outputCount,
                VoxelEngine.Crafting.StationTier station,
                params (VoxelEngine.Items.ItemDefinition item, int n)[] inputs)
            {
                string path = $"{recipesFolder}/{assetName}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var r = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                r.displayName = display;
                r.outputItem = output;
                r.outputCount = outputCount;
                r.requiredStation = station;
                r.craftSeconds = 0f;
                r.unlockedByDefault = true;
                r.inputs = new VoxelEngine.Crafting.RecipeIngredient[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                    r.inputs[i] = new VoxelEngine.Crafting.RecipeIngredient { item = inputs[i].item, count = inputs[i].n };
                AssetDatabase.CreateAsset(r, path);
                recipeRegistry.recipes.Add(r);
                return r;
            }

            // Cables: 1 ingot -> 4 cables.
            AddRecipe("Recipe_Cable_Copper", "Copper Cable", blockCableCu, 4,
                VoxelEngine.Crafting.StationTier.CraftingBench, (copperIngot, 1));
            AddRecipe("Recipe_Cable_Iron",   "Iron Cable",   blockCableFe, 4,
                VoxelEngine.Crafting.StationTier.CraftingBench, (ironIngot, 1));
            AddRecipe("Recipe_Cable_Gold",   "Gold Cable",   blockCableAu, 4,
                VoxelEngine.Crafting.StationTier.Assembler,    (goldOre != null ? goldOre : ironIngot, 1));
            AddRecipe("Recipe_Cable_Super",  "Superconductor Cable", blockCableSc, 4,
                VoxelEngine.Crafting.StationTier.Assembler,    (steelIngot, 1), (goldOre != null ? goldOre : ironIngot, 1));

            // Devices.
            AddRecipe("Recipe_Generator", "Coal Generator", blockGen, 1,
                VoxelEngine.Crafting.StationTier.CraftingBench, (ironIngot, 4), (stone, 4));
            AddRecipe("Recipe_Battery",   "Battery",        blockBat, 1,
                VoxelEngine.Crafting.StationTier.Assembler,    (copperIngot, 4), (ironIngot, 2));
            AddRecipe("Recipe_Light",     "Power Light",    blockLight, 1,
                VoxelEngine.Crafting.StationTier.CraftingBench, (copperIngot, 1));

            // ===== Electric Furnace =====
            // Build a station prefab that holds BOTH the CraftingStation marker AND an ElectricFurnace.
            string efPath = $"{prefabsFolder}/ElectricFurnace.prefab";
            var efRoot = new GameObject("ElectricFurnace");
            var efCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            efCube.transform.SetParent(efRoot.transform, false);
            efCube.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);
            var efMat = MakeColoredMat(prefabsFolder, "Mat_ElectricFurnace", new Color(0.20f, 0.50f, 0.85f));
            efCube.GetComponent<Renderer>().sharedMaterial = efMat;
            var efStation = efRoot.AddComponent<VoxelEngine.Crafting.CraftingStation>();
            efStation.tier        = VoxelEngine.Crafting.StationTier.Furnace;
            efStation.displayName = "Electric Furnace";
            efRoot.AddComponent<VoxelEngine.Crafting.ElectricFurnace>();
            // PowerConsumer is added automatically by ElectricFurnace.Awake — but we add it now so
            // the prefab is correctly authored (don't rely on runtime AddComponent for prefab fields).
            var efConsumer = efRoot.AddComponent<VoxelEngine.Power.PowerConsumer>();
            efConsumer.wattsPerSecond = 200f;
            efConsumer.connectRadius  = 1.6f;
            var efPrefab = PrefabUtility.SaveAsPrefabAsset(efRoot, efPath);
            Object.DestroyImmediate(efRoot);

            // Smelting recipes — share the same recipes the solid Furnace uses, but FASTER (electric).
            // We re-load them from the existing recipes folder.
            string oldRecipesFolder = ASSET_ROOT + "/Recipes";
            var smIron2   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.SmeltingRecipe>($"{oldRecipesFolder}/Smelt_Iron.asset");
            var smCopper2 = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.SmeltingRecipe>($"{oldRecipesFolder}/Smelt_Copper.asset");
            var smSteel2  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.SmeltingRecipe>($"{oldRecipesFolder}/Smelt_Steel.asset");
            var efRecipes = new System.Collections.Generic.List<VoxelEngine.Crafting.SmeltingRecipe>();
            if (smIron2 != null)   efRecipes.Add(smIron2);
            if (smCopper2 != null) efRecipes.Add(smCopper2);
            if (smSteel2 != null)  efRecipes.Add(smSteel2);
            AssignElectricFurnaceRecipes(efPrefab, efRecipes);

            var blockElectric = MakePowerBlock("Block_ElectricFurnace", "Electric Furnace",
                new Color(0.20f,0.50f,0.85f), efPrefab,
                "Power-driven smelter with 1 input + 4 output slots. Pulls 200 W while smelting (5 W idle). " +
                "Accepts up to 4 upgrade modules (Speed, Efficiency). No fuel needed - just connect cables.",
                hp: 600);
            AddRecipe("Recipe_ElectricFurnace", "Electric Furnace", blockElectric, 1,
                VoxelEngine.Crafting.StationTier.Assembler, (ironIngot, 6), (copperIngot, 4), (steelIngot, 1));

            // ===== Furnace Upgrade Items =====
            var upSpeed = ScriptableObject.CreateInstance<VoxelEngine.Items.FurnaceUpgradeItem>();
            upSpeed.itemId        = "upgrade_speed";
            upSpeed.displayName   = "Speed Upgrade";
            upSpeed.description   = "Insert into an Electric Furnace upgrade slot to smelt 25% faster per module. " +
                                     "Stacks multiplicatively (4 modules ~244% speed).";
            upSpeed.iconTint      = new Color(0.95f, 0.55f, 0.20f);
            upSpeed.maxStack      = 99;
            upSpeed.massPerUnit   = 0.5f;
            upSpeed.category      = "Power";
            upSpeed.speedMultiplier      = 1.25f;
            upSpeed.efficiencyMultiplier = 1.35f; // also costs 35% more power per module
            AssetDatabase.CreateAsset(upSpeed, $"{itemsFolder}/Upgrade_Speed.asset");

            var upEff = ScriptableObject.CreateInstance<VoxelEngine.Items.FurnaceUpgradeItem>();
            upEff.itemId        = "upgrade_efficiency";
            upEff.displayName   = "Efficiency Upgrade";
            upEff.description   = "Insert into an Electric Furnace upgrade slot to use 20% less power per module. " +
                                   "Stacks multiplicatively (4 modules ~41% original draw).";
            upEff.iconTint      = new Color(0.40f, 0.90f, 0.45f);
            upEff.maxStack      = 99;
            upEff.massPerUnit   = 0.5f;
            upEff.category      = "Power";
            upEff.speedMultiplier      = 1.0f;
            upEff.efficiencyMultiplier = 0.8f;
            AssetDatabase.CreateAsset(upEff, $"{itemsFolder}/Upgrade_Efficiency.asset");

            AddRecipe("Recipe_Upgrade_Speed",      "Speed Upgrade",      upSpeed, 1,
                VoxelEngine.Crafting.StationTier.Assembler, (copperIngot, 2), (ironIngot, 1));
            AddRecipe("Recipe_Upgrade_Efficiency", "Efficiency Upgrade", upEff,   1,
                VoxelEngine.Crafting.StationTier.Assembler, (copperIngot, 2), (steelIngot, 1));

            // ===== Wireless transmitter + receiver =====
            // Transmitter: needs a PowerConsumer (auto-added by component); broadcasts a fraction.
            string txPath = $"{prefabsFolder}/Wireless_Transmitter.prefab";
            var txRoot = new GameObject("Wireless_Transmitter");
            var txCube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            txCube.transform.SetParent(txRoot.transform, false);
            txCube.transform.localScale = new Vector3(0.6f, 1.0f, 0.6f);
            txCube.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(prefabsFolder, "Mat_Tx", new Color(0.55f, 0.30f, 0.85f));
            var txConsumer = txRoot.AddComponent<VoxelEngine.Power.PowerConsumer>();
            txConsumer.connectRadius = 1.6f;
            var txComp = txRoot.AddComponent<VoxelEngine.Power.Wireless.PowerTransmitter>();
            txComp.maxBroadcastWatts = 2000f;
            txComp.efficiency = 0.5f;
            txComp.range = 30f;
            var txPrefab = PrefabUtility.SaveAsPrefabAsset(txRoot, txPath);
            Object.DestroyImmediate(txRoot);

            string rxPath = $"{prefabsFolder}/Wireless_Receiver.prefab";
            var rxRoot = new GameObject("Wireless_Receiver");
            var rxCube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rxCube.transform.SetParent(rxRoot.transform, false);
            rxCube.transform.localScale = new Vector3(0.6f, 0.8f, 0.6f);
            rxCube.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(prefabsFolder, "Mat_Rx", new Color(0.85f, 0.55f, 0.30f));
            var rxGen = rxRoot.AddComponent<VoxelEngine.Power.PowerGenerator>();
            rxGen.wattsPerSecond = 0f; rxGen.connectRadius = 1.6f;
            var rxComp = rxRoot.AddComponent<VoxelEngine.Power.Wireless.PowerReceiver>();
            rxComp.requestedWatts = 1000f;
            var rxPrefab = PrefabUtility.SaveAsPrefabAsset(rxRoot, rxPath);
            Object.DestroyImmediate(rxRoot);

            var blockTx = MakePowerBlock("Block_Tx", "Wireless Transmitter", new Color(0.55f, 0.30f, 0.85f), txPrefab,
                "Beams up to 2000 W to any Wireless Receiver within 30 m. Only 50% of the drained power " +
                "actually reaches the receivers - inefficient but cable-free. Requires High-Voltage Transmission research.",
                hp: 600);
            var blockRx = MakePowerBlock("Block_Rx", "Wireless Receiver", new Color(0.85f, 0.55f, 0.30f), rxPrefab,
                "Pulls broadcasted power out of the air and injects it into the local cable network. " +
                "Acts like a generator that produces whatever a nearby Transmitter sends. Requires High-Voltage Transmission research.",
                hp: 400);

            // Wireless recipes are gated by research (set unlockedByDefault=false; Step 7 will hook them into the HV node).
            var recTx = AddRecipe("Recipe_Wireless_Tx", "Wireless Transmitter", blockTx, 1,
                VoxelEngine.Crafting.StationTier.Assembler, (steelIngot, 4), (copperIngot, 8), (ironIngot, 2));
            recTx.unlockedByDefault = false; EditorUtility.SetDirty(recTx);
            var recRx = AddRecipe("Recipe_Wireless_Rx", "Wireless Receiver",  blockRx, 1,
                VoxelEngine.Crafting.StationTier.Assembler, (steelIngot, 2), (copperIngot, 6), (ironIngot, 1));
            recRx.unlockedByDefault = false; EditorUtility.SetDirty(recRx);

            EditorUtility.SetDirty(recipeRegistry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Ensure a PowerNetworkManager exists in the scene.
            var existing = Object.FindAnyObjectByType<VoxelEngine.Power.PowerNetworkManager>();
            if (existing == null)
            {
                var go = new GameObject("PowerNetworkManager");
                go.AddComponent<VoxelEngine.Power.PowerNetworkManager>();
                Debug.Log("[Wizard] Spawned PowerNetworkManager in scene.");
            }

            EditorUtility.DisplayDialog("Voxel Engine",
                "Power content created!\n\n" +
                "* 4 wire tiers (Copper 1k -> Iron 5k -> Gold 25k -> Superconductor 100k W)\n" +
                "* Coal Generator, Battery, Power Light\n" +
                "* 7 new recipes added to RecipeRegistry\n" +
                "* PowerNetworkManager added to current scene\n\n" +
                "Place a Generator, run cables to a Light. Networks rebuild automatically.",
                "OK");
        }

        // ============================================================
        //          STEP 7 - RESEARCH CONTENT (TECH TREE)
        // ============================================================
        private void BuildResearchContent()
        {
            const string researchFolder = ASSET_ROOT + "/Research";
            const string nodesFolder    = researchFolder + "/Nodes";
            const string itemsFolder    = ASSET_ROOT + "/Items";
            const string recipesFolder  = researchFolder + "/Recipes";
            const string prefabsFolder  = researchFolder + "/Prefabs";

            EnsureFolder(researchFolder);
            EnsureFolder(nodesFolder);
            EnsureFolder(recipesFolder);
            EnsureFolder(prefabsFolder);

            // ---- Pull existing items needed for science pack recipes ----
            var woodLog     = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_WoodLog.asset");
            var plank       = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_WoodenPlank.asset");
            var stone       = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{ITEM_FOLDER}/Item_Stone.asset");
            var coal        = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{ITEM_FOLDER}/Item_Coal.asset");
            var ironIngot   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_IronIngot.asset");
            var copperIngot = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_CopperIngot.asset");
            var steelIngot  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_SteelIngot.asset");
            if (woodLog == null || ironIngot == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine",
                    "Run Step 4 (Build Crafting Content) first - it creates wood/iron items the research tree needs.", "OK");
                return;
            }

            // ---- 1) Science pack items ----
            VoxelEngine.Items.ScienceItem MakeScience(string assetName, string display, Color tint, int tier, string desc)
            {
                string path = $"{itemsFolder}/{assetName}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var sci = ScriptableObject.CreateInstance<VoxelEngine.Items.ScienceItem>();
                sci.itemId      = assetName.ToLower();
                sci.displayName = display;
                sci.description = desc;
                sci.iconTint    = tint;
                sci.maxStack    = 999;
                sci.massPerUnit = 0.5f;
                sci.category    = "Science";
                sci.subcategory = VoxelEngine.Items.ResourceCategory.Misc;
                sci.tier        = tier;
                AssetDatabase.CreateAsset(sci, path);
                return sci;
            }
            var sciT1 = MakeScience("Item_ScienceT1", "Science Pack I",   new Color(0.85f, 0.30f, 0.30f), 1,
                "Tier 1 research input. Crafted in your inventory from wood + stone.");
            var sciT2 = MakeScience("Item_ScienceT2", "Science Pack II",  new Color(0.30f, 0.75f, 0.40f), 2,
                "Tier 2 research input. Crafted at a Crafting Bench from iron + copper ingots.");
            var sciT3 = MakeScience("Item_ScienceT3", "Science Pack III", new Color(0.30f, 0.60f, 0.95f), 3,
                "Tier 3 research input. Crafted at an Assembler from steel + circuitry.");

            // ---- 2) Research Lab prefab ----
            string labPath = $"{prefabsFolder}/ResearchLab.prefab";
            var labRoot = new GameObject("ResearchLab");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(labRoot.transform, false);
            cube.transform.localScale = new Vector3(1.6f, 1.4f, 1.2f);
            var mat = MakeColoredMat(prefabsFolder, "Mat_ResearchLab", new Color(0.50f, 0.30f, 0.70f));
            cube.GetComponent<Renderer>().sharedMaterial = mat;
            var stat = labRoot.AddComponent<VoxelEngine.Crafting.CraftingStation>();
            stat.tier        = VoxelEngine.Crafting.StationTier.Assembler;
            stat.displayName = "Research Lab";
            labRoot.AddComponent<VoxelEngine.Research.ResearchLab>();
            var labPrefab = PrefabUtility.SaveAsPrefabAsset(labRoot, labPath);
            Object.DestroyImmediate(labRoot);

            var blockLab = ScriptableObject.CreateInstance<VoxelEngine.Items.BlockItem>();
            blockLab.itemId       = "block_researchlab";
            blockLab.displayName  = "Research Lab";
            blockLab.description  = "Place to research new technologies. Drop science packs in its slots, then start a research from the Research menu (Y).";
            blockLab.iconTint     = new Color(0.50f, 0.30f, 0.70f);
            blockLab.maxStack     = 99;
            blockLab.placedPrefab = labPrefab;
            blockLab.gridSize     = Vector3Int.one;
            blockLab.blockHealth  = 600;
            blockLab.miningTier   = 1;
            blockLab.category     = "Stations";
            string labBlockPath = $"{researchFolder}/Block_ResearchLab.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(labBlockPath) != null) AssetDatabase.DeleteAsset(labBlockPath);
            AssetDatabase.CreateAsset(blockLab, labBlockPath);

            // ---- 3) Recipes for science packs + lab ----
            var recipeRegistry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>($"{ASSET_ROOT}/RecipeRegistry.asset");
            if (recipeRegistry == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine",
                    "Run Step 4 first - RecipeRegistry.asset doesn\'t exist.", "OK");
                return;
            }
            VoxelEngine.Crafting.RecipeDefinition AddRecipe(string assetName, string display,
                VoxelEngine.Items.ItemDefinition output, int outputCount,
                VoxelEngine.Crafting.StationTier station, bool unlockedByDefault,
                params (VoxelEngine.Items.ItemDefinition item, int n)[] inputs)
            {
                string path = $"{recipesFolder}/{assetName}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var r = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                r.displayName = display;
                r.outputItem = output;
                r.outputCount = outputCount;
                r.requiredStation = station;
                r.craftSeconds = 0f;
                r.unlockedByDefault = unlockedByDefault;
                r.inputs = new VoxelEngine.Crafting.RecipeIngredient[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                    r.inputs[i] = new VoxelEngine.Crafting.RecipeIngredient { item = inputs[i].item, count = inputs[i].n };
                AssetDatabase.CreateAsset(r, path);
                recipeRegistry.recipes.Add(r);
                return r;
            }

            // Science pack recipes (always unlocked).
            var recSci1 = AddRecipe("Recipe_ScienceT1", "Science Pack I",  sciT1, 1,
                VoxelEngine.Crafting.StationTier.None,          true,  (woodLog, 1), (stone, 1));
            var recSci2 = AddRecipe("Recipe_ScienceT2", "Science Pack II", sciT2, 1,
                VoxelEngine.Crafting.StationTier.CraftingBench, true,  (ironIngot, 1), (copperIngot, 1));
            var recSci3 = AddRecipe("Recipe_ScienceT3", "Science Pack III",sciT3, 1,
                VoxelEngine.Crafting.StationTier.Assembler,     true,  (steelIngot, 1), (copperIngot, 2));
            var recLab  = AddRecipe("Recipe_ResearchLab","Research Lab",   blockLab, 1,
                VoxelEngine.Crafting.StationTier.CraftingBench, true,  (plank, 4), (copperIngot, 2));

            // ---- 4) Find some existing recipes we'll gate behind research ----
            // We re-gate a few recipes that were previously unlockedByDefault. Players will need
            // to research them via the new nodes.
            VoxelEngine.Crafting.RecipeDefinition FindRecipe(string assetName)
            {
                var guids = AssetDatabase.FindAssets($"{assetName} t:RecipeDefinition");
                foreach (var g in guids)
                {
                    var p2 = AssetDatabase.GUIDToAssetPath(g);
                    if (System.IO.Path.GetFileNameWithoutExtension(p2) == assetName)
                        return AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(p2);
                }
                return null;
            }
            var recPickStone = FindRecipe("Recipe_PickStone");
            var recPickIron  = FindRecipe("Recipe_PickIron");
            var recPickSteel = FindRecipe("Recipe_PickSteel");
            var recFurnace   = FindRecipe("Recipe_Furnace");
            var recElectric  = FindRecipe("Recipe_ElectricFurnace");
            var recGenerator = FindRecipe("Recipe_Generator");
            var recBattery   = FindRecipe("Recipe_Battery");
            var recAssembler = FindRecipe("Recipe_Assembler");
            var recCableCu   = FindRecipe("Recipe_Cable_Copper");
            var recCableFe   = FindRecipe("Recipe_Cable_Iron");
            var recCableAu   = FindRecipe("Recipe_Cable_Gold");
            var recCableSc   = FindRecipe("Recipe_Cable_Super");

            // Lock recipes that will require research. (Players who don't unlock these
            // can still play, but with reduced options.)
            void Lock(VoxelEngine.Crafting.RecipeDefinition r) { if (r != null) { r.unlockedByDefault = false; EditorUtility.SetDirty(r); } }
            Lock(recPickStone); Lock(recPickIron); Lock(recPickSteel);
            Lock(recFurnace);   Lock(recElectric);
            Lock(recGenerator); Lock(recBattery);  Lock(recAssembler);
            Lock(recCableCu);   Lock(recCableFe);  Lock(recCableAu);  Lock(recCableSc);

            // ---- 5) Research nodes (the tree) ----
            VoxelEngine.Research.ResearchNode MakeNode(
                string id, string display, string desc, int tier, int col,
                float seconds, (VoxelEngine.Items.ScienceItem p, int n)[] cost,
                VoxelEngine.Crafting.RecipeDefinition[] unlocks,
                VoxelEngine.Research.ResearchNode[] prereqs = null)
            {
                string path = $"{nodesFolder}/{id}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var n = ScriptableObject.CreateInstance<VoxelEngine.Research.ResearchNode>();
                n.nodeId      = id;
                n.displayName = display;
                n.description = desc;
                n.tier = tier; n.column = col;
                n.researchSeconds = seconds;
                n.cost = new VoxelEngine.Research.ResearchNode.ScienceCost[cost.Length];
                for (int i = 0; i < cost.Length; i++)
                    n.cost[i] = new VoxelEngine.Research.ResearchNode.ScienceCost { pack = cost[i].p, count = cost[i].n };
                n.unlocksRecipes = unlocks ?? new VoxelEngine.Crafting.RecipeDefinition[0];
                n.prerequisites = prereqs ?? new VoxelEngine.Research.ResearchNode[0];
                AssetDatabase.CreateAsset(n, path);
                return n;
            }

            var tree = ScriptableObject.CreateInstance<VoxelEngine.Research.ResearchTree>();

            // Tier 1 - basic survival
            var nStoneWorking = MakeNode("res_stone_working", "Stone Working",
                "Learn to shape stone tools. Unlocks the Stone Pickaxe and the Solid Fuel Furnace. " +
                "Tier 1: can be researched instantly from the menu (no Lab required).",
                1, 0, 0f, new[] { (sciT1, 5) },
                new[] { recPickStone, recFurnace });

            // Tier 2 - smelting / iron
            var nSmelting = MakeNode("res_smelting", "Smelting",
                "Refining ores into ingots opens metal-tier tools and machines.",
                2, 0, 40f, new[] { (sciT1, 10), (sciT2, 5) },
                new[] { recPickIron },
                new[] { nStoneWorking });

            var nElectricity = MakeNode("res_electricity", "Electricity",
                "Generate and store electric power. Unlocks the Coal Generator and copper/iron cables.",
                2, 1, 40f, new[] { (sciT1, 10), (sciT2, 5) },
                new[] { recGenerator, recCableCu, recCableFe },
                new[] { nStoneWorking });

            // Tier 3 - advanced machines
            var nAdvManufacturing = MakeNode("res_adv_manufacturing", "Advanced Manufacturing",
                "Industrial-tier production unlocks the Assembler, Electric Furnace, and Battery.",
                3, 0, 60f, new[] { (sciT2, 15), (sciT3, 5) },
                new[] { recAssembler, recElectric, recBattery },
                new[] { nSmelting, nElectricity });

            var nSteelAlloy = MakeNode("res_steel_alloy", "Steel Alloy",
                "Combining iron and carbon yields steel - the strongest tool tier.",
                3, 1, 60f, new[] { (sciT2, 15), (sciT3, 5) },
                new[] { recPickSteel },
                new[] { nSmelting });

            // Look up wireless recipes (added in Step 6); include them in HV unlocks if found.
            var recWirelessTx = FindRecipe("Recipe_Wireless_Tx");
            var recWirelessRx = FindRecipe("Recipe_Wireless_Rx");
            var hvUnlocks = new System.Collections.Generic.List<VoxelEngine.Crafting.RecipeDefinition>
                            { recCableAu, recCableSc };
            if (recWirelessTx != null) hvUnlocks.Add(recWirelessTx);
            if (recWirelessRx != null) hvUnlocks.Add(recWirelessRx);

            var nHighVoltage = MakeNode("res_high_voltage", "High-Voltage Transmission",
                "Premium wire tiers carry more wattage between machines. Also unlocks Wireless Transmitter and Receiver.",
                3, 2, 60f, new[] { (sciT2, 10), (sciT3, 5) },
                hvUnlocks.ToArray(),
                new[] { nElectricity });

            tree.nodes.Add(nStoneWorking);
            tree.nodes.Add(nSmelting);
            tree.nodes.Add(nElectricity);
            tree.nodes.Add(nAdvManufacturing);
            tree.nodes.Add(nSteelAlloy);
            tree.nodes.Add(nHighVoltage);

            // ===== PLAYER UPGRADES =====
            // All player upgrades are instant (researchSeconds=0) and paid from inventory.
            // Most are REPEATABLE: each rank costs (rank+1) * baseCost when costScalesWithRank is true.
            VoxelEngine.Research.ResearchNode MakePlayerNode(
                string id, string display, string desc, int tier, int col,
                (VoxelEngine.Items.ScienceItem p, int n)[] cost,
                VoxelEngine.Research.PlayerUpgradeKind kind, float perRank, int maxRanks)
            {
                string path = $"{nodesFolder}/{id}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var n = ScriptableObject.CreateInstance<VoxelEngine.Research.ResearchNode>();
                n.nodeId = id;
                n.displayName = display;
                n.description = desc;
                n.category = VoxelEngine.Research.ResearchCategory.PlayerUpgrades;
                n.tier = tier; n.column = col;
                n.researchSeconds = 0f;
                n.upgradeKind = kind;
                n.upgradePerRankAmount = perRank;
                n.maxRanks = maxRanks;
                n.costScalesWithRank = (maxRanks > 1);
                n.cost = new VoxelEngine.Research.ResearchNode.ScienceCost[cost.Length];
                for (int i = 0; i < cost.Length; i++)
                    n.cost[i] = new VoxelEngine.Research.ResearchNode.ScienceCost { pack = cost[i].p, count = cost[i].n };
                AssetDatabase.CreateAsset(n, path);
                tree.nodes.Add(n);
                return n;
            }

            // Tier 1 — cheap, T1 science only.
            MakePlayerNode("up_hp",    "Vitality",
                "+20 max HP per rank. The body adapts to harsh environments.",
                1, 0, new[] { (sciT1, 3) },
                VoxelEngine.Research.PlayerUpgradeKind.BonusMaxHealth, 20f, 10);
            MakePlayerNode("up_stam",  "Endurance",
                "+15 max stamina per rank. Sprint and jump longer without resting.",
                1, 1, new[] { (sciT1, 3) },
                VoxelEngine.Research.PlayerUpgradeKind.BonusMaxStamina, 15f, 10);
            MakePlayerNode("up_dmg",   "Strength",
                "+2 flat damage per rank. Hit harder with any tool.",
                1, 2, new[] { (sciT1, 4) },
                VoxelEngine.Research.PlayerUpgradeKind.BonusDamage, 2f, 10);
            MakePlayerNode("up_inv",   "Pack Mule",
                "+5 backpack slots per rank. Carry more stuff back to base.",
                1, 3, new[] { (sciT1, 5) },
                VoxelEngine.Research.PlayerUpgradeKind.BonusInventorySlots, 5f, 6);

            // Tier 2 — requires T2 science as well.
            MakePlayerNode("up_sprint","Wind Sprinter",
                "+0.25 sprint speed multiplier per rank. CAPPED at 5x base speed.",
                2, 0, new[] { (sciT1, 5), (sciT2, 3) },
                VoxelEngine.Research.PlayerUpgradeKind.BonusSprintMultiplier, 0.25f, 10);

            // Tier 3 — Flight. Single-rank, ultra-expensive, requires a special artifact (TODO: artifact item).
            MakePlayerNode("up_flight","Flight",
                "Unlocks permanent flight (toggle with F or via Settings). " +
                "Requires a Mysterious Artifact (not yet implemented) plus a stack of Tier-3 science.",
                3, 0, new[] { (sciT3, 50) },
                VoxelEngine.Research.PlayerUpgradeKind.UnlockFlight, 1f, 1);

            string treePath = $"{researchFolder}/ResearchTree.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(treePath) != null) AssetDatabase.DeleteAsset(treePath);
            AssetDatabase.CreateAsset(tree, treePath);

            EditorUtility.SetDirty(recipeRegistry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ---- 6) Ensure ResearchManager + ResearchUI exist in the scene ----
            var rm = Object.FindAnyObjectByType<VoxelEngine.Research.ResearchManager>();
            if (rm == null)
            {
                var rmGo = new GameObject("ResearchManager");
                rm = rmGo.AddComponent<VoxelEngine.Research.ResearchManager>();
            }
            rm.tree = tree;
            EditorUtility.SetDirty(rm);

            var ui = Object.FindAnyObjectByType<VoxelEngine.Research.ResearchUI>();
            if (ui == null)
            {
                var uiGo = new GameObject("ResearchUI");
                uiGo.AddComponent<UnityEngine.UIElements.UIDocument>();
                var panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(
                    "Assets/Resources/MenuPanelSettings.asset");
                if (panelSettings != null) uiGo.GetComponent<UnityEngine.UIElements.UIDocument>().panelSettings = panelSettings;
                uiGo.AddComponent<VoxelEngine.Research.ResearchUI>();
            }

            EditorUtility.DisplayDialog("Voxel Engine",
                "Research content created!\n\n" +
                "* 3 Science Packs (T1, T2, T3)\n" +
                "* Research Lab (placeable + crafted at Crafting Bench)\n" +
                "* 6 tech tree nodes (Stone Working -> Smelting/Electricity -> Adv Mfg / Steel / HV)\n" +
                "* 12 existing recipes are now gated behind research\n" +
                "* ResearchManager + ResearchUI spawned in the scene\n\n" +
                "Press Y in-game to open the research tree.",
                "OK");
        }

        // ============================================================
        //          STEP 8 - FLUID CONTENT (water bucket, tank, pump, pipes)
        // ============================================================
        private void BuildFluidContent()
        {
            const string fluidFolder    = ASSET_ROOT + "/Fluids";
            const string prefabsFolder  = fluidFolder + "/Prefabs";
            const string blocksFolder   = fluidFolder + "/Blocks";
            const string recipesFolder  = fluidFolder + "/Recipes";
            const string itemsFolder    = ASSET_ROOT + "/Items";

            EnsureFolder(fluidFolder);
            EnsureFolder(prefabsFolder);
            EnsureFolder(blocksFolder);
            EnsureFolder(recipesFolder);

            // ---- Pull common items ----
            var ironIngot   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_IronIngot.asset");
            var copperIngot = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{itemsFolder}/Item_CopperIngot.asset");
            if (ironIngot == null || copperIngot == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine",
                    "Run Step 4 first (need iron+copper ingots).", "OK");
                return;
            }

            // ---- 1) Water Bucket item ----
            string bucketPath = $"{itemsFolder}/Tool_WaterBucket.asset";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(bucketPath) != null) AssetDatabase.DeleteAsset(bucketPath);
            var bucket = ScriptableObject.CreateInstance<VoxelEngine.Items.WaterBucket>();
            bucket.itemId       = "water_bucket";
            bucket.displayName  = "Water Bucket";
            bucket.description  = "LMB scoops a water voxel into the bucket. RMB places it elsewhere — and it spreads to fill holes! Use durability to track if it's filled (1 = full, 0 = empty).";
            bucket.iconTint     = new Color(0.20f, 0.50f, 0.85f);
            bucket.maxStack     = 1;
            bucket.maxDurability= 1;
            bucket.toolType     = VoxelEngine.Items.ToolType.Other;
            bucket.category     = "Fluids";
            AssetDatabase.CreateAsset(bucket, bucketPath);

            // ---- 2) Tank prefab(s) ----
            GameObject MakeTankPrefab(string name, Color color, bool isGlass)
            {
                string path = $"{prefabsFolder}/{name}.prefab";
                var root = new GameObject(name);
                // Solid hull cube.
                var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hull.transform.SetParent(root.transform, false);
                hull.transform.localScale = new Vector3(1f, 1.4f, 1f);
                hull.transform.localPosition = new Vector3(0, 0.7f, 0);
                var mat = MakeColoredMat(prefabsFolder, $"Mat_{name}",
                    isGlass ? new Color(color.r, color.g, color.b, 0.5f) : color);
                hull.GetComponent<Renderer>().sharedMaterial = mat;
                if (isGlass)
                {
                    // Inner WaterFill cube — scaled by Tank script. Cyan tint.
                    var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    fill.name = "WaterFill";
                    fill.transform.SetParent(root.transform, false);
                    fill.transform.localScale = new Vector3(0.85f, 0.001f, 0.85f);
                    fill.transform.localPosition = new Vector3(0, 0.05f, 0);
                    fill.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(prefabsFolder, $"Mat_{name}_Fill",
                        new Color(0.15f, 0.45f, 0.85f, 1f));
                    // Disable collider on inner fill so raycasts hit the hull.
                    var col = fill.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
                }
                var t = root.AddComponent<VoxelEngine.Fluids.WaterTank>();
                t.capacityLitres = 1000f;
                t.isGlass = isGlass;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                return prefab;
            }
            var tankSolid = MakeTankPrefab("Tank_Solid", new Color(0.45f, 0.45f, 0.50f), false);
            var tankGlass = MakeTankPrefab("Tank_Glass", new Color(0.55f, 0.75f, 0.95f), true);

            // ---- 3) Pump prefab ----
            GameObject MakePumpPrefab(string name, Color color)
            {
                string path = $"{prefabsFolder}/{name}.prefab";
                var root = new GameObject(name);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform, false);
                cube.transform.localScale = new Vector3(1f, 0.8f, 1f);
                cube.transform.localPosition = new Vector3(0, 0.4f, 0);
                cube.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(prefabsFolder, $"Mat_{name}", color);
                // Power consumer + pump.
                var pc = root.AddComponent<VoxelEngine.Power.PowerConsumer>();
                pc.connectRadius = 1.6f; pc.wattsPerSecond = 30f;
                var pump = root.AddComponent<VoxelEngine.Fluids.WaterPump>();
                pump.pumpLps = 20f;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                return prefab;
            }
            var pumpPrefab = MakePumpPrefab("WaterPump", new Color(0.55f, 0.30f, 0.20f));

            // ---- 4) Pipe prefab(s) ----
            // Pipes use the shared PipeVisualBuilder so they render the same chunky
            // core+arms style as Power / Data cables. The stretched cube is removed
            // here so the runtime visual is the only one shown.
            GameObject MakePipePrefab(string name, Color shellColor, Color innerColor, bool isGlass)
            {
                string path = $"{prefabsFolder}/{name}.prefab";
                var root = new GameObject(name);

                // Tiny invisible collider so raycasts (wrench, build-system) still register.
                var col = root.AddComponent<BoxCollider>();
                col.size = new Vector3(0.50f, 0.50f, 0.50f);

                var pipe = root.AddComponent<VoxelEngine.Fluids.WaterPipe>();
                pipe.maxFlowLps    = 50f;
                pipe.isGlass       = isGlass;
                pipe.connectRadius = 1.4f;

                var vb = root.AddComponent<VoxelEngine.Networks.PipeVisualBuilder>();
                vb.shellTint        = shellColor;
                vb.accentTint       = new Color(
                    Mathf.Clamp01(shellColor.r * 0.7f + 0.30f),
                    Mathf.Clamp01(shellColor.g * 0.7f + 0.30f),
                    Mathf.Clamp01(shellColor.b * 0.7f + 0.30f), 1f);
                vb.innerMediumTint  = innerColor;
                vb.isGlass          = isGlass;
                vb.style            = VoxelEngine.Networks.PipeStyle.Copper;
                vb.gridSize         = 1f;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                return prefab;
            }
            // Solid water pipe — warm burnished copper. Glass — translucent copper-tinted
            // shell with a vivid water-blue inner that previews the fluid.
            var pipeSolid = MakePipePrefab("Pipe_Solid",
                new Color(0.78f, 0.45f, 0.20f),
                new Color(0.25f, 0.55f, 0.90f), false);
            var pipeGlass = MakePipePrefab("Pipe_Glass",
                new Color(0.90f, 0.70f, 0.55f),
                new Color(0.25f, 0.65f, 0.95f), true);

            // ---- 5) BlockItems for the placeables ----
            VoxelEngine.Items.BlockItem MakeFluidBlock(string assetName, string display, Color tint, GameObject prefab, string desc, int hp = 200)
            {
                string path = $"{blocksFolder}/{assetName}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var b = ScriptableObject.CreateInstance<VoxelEngine.Items.BlockItem>();
                b.itemId       = assetName.ToLower();
                b.displayName  = display;
                b.description  = desc;
                b.iconTint     = tint;
                b.maxStack     = 99;
                b.placedPrefab = prefab;
                b.gridSize     = Vector3Int.one;
                b.blockHealth  = hp; b.miningTier = 1;
                b.category     = "Fluids";
                AssetDatabase.CreateAsset(b, path);
                return b;
            }
            var bTankSolid = MakeFluidBlock("Block_TankSolid", "Water Tank (Solid)", new Color(0.45f,0.45f,0.50f), tankSolid,
                "Stores 1000 L of water. Connect to pipes and pumps.");
            var bTankGlass = MakeFluidBlock("Block_TankGlass", "Water Tank (Glass)", new Color(0.55f,0.75f,0.95f), tankGlass,
                "Same as the solid tank but the water level is visible through the glass.");
            var bPump      = MakeFluidBlock("Block_WaterPump", "Water Pump",         new Color(0.55f,0.30f,0.20f), pumpPrefab,
                "Pulls 20 L/s of water into the network while powered (~30 W). Connect cables to power, pipes to tanks.");
            var bPipeSolid = MakeFluidBlock("Block_PipeSolid", "Water Pipe (Solid)", new Color(0.55f,0.30f,0.20f), pipeSolid,
                "Carries up to 50 L/s. Place between tanks and pumps to connect them.");
            var bPipeGlass = MakeFluidBlock("Block_PipeGlass", "Water Pipe (Glass)", new Color(0.55f,0.75f,0.95f), pipeGlass,
                "Glass variant of the water pipe — same capacity, but transparent.");

            // ---- 6) Recipes ----
            var recipeRegistry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>($"{ASSET_ROOT}/RecipeRegistry.asset");
            if (recipeRegistry == null) { EditorUtility.DisplayDialog("Voxel Engine", "Run Step 4 first.", "OK"); return; }

            VoxelEngine.Crafting.RecipeDefinition AddRecipe(string assetName, string display,
                VoxelEngine.Items.ItemDefinition output, int outputCount,
                VoxelEngine.Crafting.StationTier station,
                params (VoxelEngine.Items.ItemDefinition item, int n)[] inputs)
            {
                string path = $"{recipesFolder}/{assetName}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var r = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                r.displayName = display;
                r.outputItem = output; r.outputCount = outputCount;
                r.requiredStation = station;
                r.craftSeconds = station switch
                {
                    VoxelEngine.Crafting.StationTier.None => 0f,
                    VoxelEngine.Crafting.StationTier.CraftingBench => 2f,
                    VoxelEngine.Crafting.StationTier.Assembler => 4f,
                    _ => 0f
                };
                r.unlockedByDefault = true;
                r.inputs = new VoxelEngine.Crafting.RecipeIngredient[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                    r.inputs[i] = new VoxelEngine.Crafting.RecipeIngredient { item = inputs[i].item, count = inputs[i].n };
                AssetDatabase.CreateAsset(r, path);
                recipeRegistry.recipes.Add(r);
                return r;
            }

            AddRecipe("Recipe_WaterBucket", "Water Bucket",          bucket,    1, VoxelEngine.Crafting.StationTier.CraftingBench, (ironIngot, 3));
            AddRecipe("Recipe_TankSolid",   "Water Tank (Solid)",    bTankSolid,1, VoxelEngine.Crafting.StationTier.CraftingBench, (ironIngot, 6));
            AddRecipe("Recipe_TankGlass",   "Water Tank (Glass)",    bTankGlass,1, VoxelEngine.Crafting.StationTier.CraftingBench, (ironIngot, 4), (copperIngot, 4));
            AddRecipe("Recipe_PipeSolid",   "Water Pipe (Solid) x4", bPipeSolid,4, VoxelEngine.Crafting.StationTier.CraftingBench, (copperIngot, 1));
            AddRecipe("Recipe_PipeGlass",   "Water Pipe (Glass) x4", bPipeGlass,4, VoxelEngine.Crafting.StationTier.CraftingBench, (copperIngot, 2));
            AddRecipe("Recipe_WaterPump",   "Water Pump",            bPump,     1, VoxelEngine.Crafting.StationTier.Assembler,     (ironIngot, 4), (copperIngot, 6));

            EditorUtility.SetDirty(recipeRegistry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Spawn the FluidNetworkManager + FluidPhysics in the open scene.
            if (Object.FindAnyObjectByType<VoxelEngine.Fluids.FluidNetworkManager>() == null)
                new GameObject("FluidNetworkManager").AddComponent<VoxelEngine.Fluids.FluidNetworkManager>();
            if (Object.FindAnyObjectByType<VoxelEngine.Fluids.FluidSimManager>() == null)
                new GameObject("FluidSimManager").AddComponent<VoxelEngine.Fluids.FluidSimManager>();

            EditorUtility.DisplayDialog("Voxel Engine",
                "Fluid content created!\n\n" +
                "* Water Bucket (LMB scoop, RMB place — spreads to fill holes)\n" +
                "* Water Tank (Solid + Glass variants, 1000L each)\n" +
                "* Water Pump (20 L/s, ~30 W)\n" +
                "* Water Pipes (Solid + Glass, 50 L/s capacity)\n" +
                "* 6 new recipes added to RecipeRegistry\n" +
                "* FluidNetworkManager + FluidSimManager spawned in scene",
                "OK");
        }

        // ============================================================
        //  STEP 10 - INDUSTRIAL CONTENT PACK
        //  (plates, oil chain, plastic, electronics, expanded research)
        // ============================================================
        private void BuildIndustrialContent()
        {
            const string industrialFolder = ASSET_ROOT + "/Industrial";
            const string itemsFolder      = industrialFolder + "/Items";
            const string prefabsFolder    = industrialFolder + "/Prefabs";
            const string blocksFolder     = industrialFolder + "/Blocks";
            const string recipesFolder    = industrialFolder + "/Recipes";
            const string procRecFolder    = industrialFolder + "/ProcessingRecipes";
            const string researchFolder   = ASSET_ROOT + "/Research";
            const string nodesFolder      = researchFolder + "/Nodes";

            EnsureFolder(industrialFolder);
            EnsureFolder(itemsFolder);
            EnsureFolder(prefabsFolder);
            EnsureFolder(blocksFolder);
            EnsureFolder(recipesFolder);
            EnsureFolder(procRecFolder);
            EnsureFolder(researchFolder);
            EnsureFolder(nodesFolder);

            string craftItemsFolder = ASSET_ROOT + "/Items";

            // -------- Load all pre-existing items (from steps 1, 4, 6, 7, 8) --------
            var stone       = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItemsFolder}/Item_Stone.asset");
            var sand        = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItemsFolder}/Item_Sand.asset");
            var clay        = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItemsFolder}/Item_Clay.asset");
            var ice         = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItemsFolder}/Item_Ice.asset");
            var ironOre     = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItemsFolder}/Item_Iron.asset");
            var copperOre   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItemsFolder}/Item_Copper.asset");
            var coal        = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItemsFolder}/Item_Coal.asset");
            var nickelOre   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItemsFolder}/Item_Nickel.asset");
            var siliconOre  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItemsFolder}/Item_Silicon.asset");
            var goldOre     = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItemsFolder}/Item_Gold.asset");
            var silverOre   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItemsFolder}/Item_Silver.asset");
            var uraniumOre  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItemsFolder}/Item_Uranium.asset");
            var crudeOilRaw = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItemsFolder}/Item_CrudeOil.asset");

            // Wood logs / planks / ingots are written into ASSET_ROOT/Items by Step 4
            // (via MakeResource → Item_WoodLog.asset etc.). Earlier versions of Step 10
            // looked in /Recipes here by mistake — fixed to use craftItemsFolder.
            var woodLog     = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{craftItemsFolder}/Item_WoodLog.asset");
            var plank       = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{craftItemsFolder}/Item_WoodenPlank.asset");
            var ironIngot   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{craftItemsFolder}/Item_IronIngot.asset");
            var copperIngot = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{craftItemsFolder}/Item_CopperIngot.asset");
            var steelIngot  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{craftItemsFolder}/Item_SteelIngot.asset");

            if (ironIngot == null || copperIngot == null || steelIngot == null || woodLog == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine",
                    "Run Step 4 (Build Crafting Content) first — it creates the ingot/wood items the industrial pack depends on.", "OK");
                return;
            }

            // Science packs come from Step 7.
            var sciT1 = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ScienceItem>($"{craftItemsFolder}/Item_ScienceT1.asset");
            var sciT2 = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ScienceItem>($"{craftItemsFolder}/Item_ScienceT2.asset");
            var sciT3 = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ScienceItem>($"{craftItemsFolder}/Item_ScienceT3.asset");
            if (sciT1 == null || sciT2 == null || sciT3 == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine",
                    "Run Step 7 (Build Research Content) first — it creates the Science Pack items the industrial pack depends on.", "OK");
                return;
            }

            // Recipe registry (created by Step 4).
            var registry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>($"{ASSET_ROOT}/RecipeRegistry.asset");
            if (registry == null) { EditorUtility.DisplayDialog("Voxel Engine", "Run Step 4 first.", "OK"); return; }

            // ====================================================================
            //  1) NEW RESOURCE ITEMS — plates, gears, wires, circuits, oil chain
            // ====================================================================

            VoxelEngine.Items.ResourceItem MakeIndustrialResource(string assetName, string display, string desc,
                Color tint, VoxelEngine.Items.ResourceCategory cat, string uiCategory, int maxStack = 999)
            {
                string path = $"{itemsFolder}/{assetName}.asset";
                var r = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>(path);
                if (r == null) { r = ScriptableObject.CreateInstance<VoxelEngine.Items.ResourceItem>(); AssetDatabase.CreateAsset(r, path); }
                r.itemId      = assetName.ToLower();
                r.displayName = display;
                r.description = desc;
                r.iconTint    = tint;
                r.maxStack    = maxStack;
                r.massPerUnit = 1f;
                r.category    = uiCategory;
                r.subcategory = cat;
                r.fuelSeconds = 0f;
                EditorUtility.SetDirty(r);
                return r;
            }

            // ─ Plates ─
            var ironPlate   = MakeIndustrialResource("Item_IronPlate",   "Iron Plate",   "Pressed iron sheet. Core building block for machines, hulls, and circuits.",                       new Color(0.78f, 0.80f, 0.85f), VoxelEngine.Items.ResourceCategory.Component, "Plates");
            var copperPlate = MakeIndustrialResource("Item_CopperPlate", "Copper Plate", "Pressed copper sheet. Used for electrical components.",                                          new Color(0.85f, 0.55f, 0.30f), VoxelEngine.Items.ResourceCategory.Component, "Plates");
            var steelPlate  = MakeIndustrialResource("Item_SteelPlate",  "Steel Plate",  "Pressed steel sheet. Required for high-tier machines, armoured structures, and nuclear gear.",   new Color(0.60f, 0.62f, 0.68f), VoxelEngine.Items.ResourceCategory.Component, "Plates");

            // ─ Mechanical / electrical intermediates ─
            var ironGear    = MakeIndustrialResource("Item_IronGear",   "Iron Gear",     "Toothed iron cog. Used in any rotating machinery.",                           new Color(0.65f, 0.68f, 0.72f), VoxelEngine.Items.ResourceCategory.Component, "Mechanical");
            var copperWire  = MakeIndustrialResource("Item_CopperWire", "Copper Wire",   "Drawn copper conductor. The basic ingredient of all electronics.",            new Color(0.92f, 0.58f, 0.30f), VoxelEngine.Items.ResourceCategory.Component, "Electronics");
            var circuitBasic= MakeIndustrialResource("Item_Circuit",    "Electronic Circuit", "PCB with copper traces on an iron substrate. Brain of every machine.", new Color(0.30f, 0.65f, 0.40f), VoxelEngine.Items.ResourceCategory.Component, "Electronics");
            var circuitAdv  = MakeIndustrialResource("Item_AdvCircuit", "Advanced Circuit",  "Layered logic board. Powers high-tier automation and digital storage.",   new Color(0.30f, 0.55f, 0.95f), VoxelEngine.Items.ResourceCategory.Component, "Electronics");

            // ─ Glass (smelted from sand) ─
            var glass       = MakeIndustrialResource("Item_Glass",      "Glass",          "Clear pane fused from sand. Used in lab equipment and storage windows.",   new Color(0.70f, 0.88f, 0.95f), VoxelEngine.Items.ResourceCategory.Component, "Materials");

            // ─ Oil chain ─
            var emptyBarrel = MakeIndustrialResource("Item_EmptyBarrel","Empty Barrel",    "Pressed-steel drum. Fill it with crude oil at a Pumpjack.",               new Color(0.45f, 0.45f, 0.50f), VoxelEngine.Items.ResourceCategory.Component, "Oil", maxStack: 50);
            var crudeBarrel = MakeIndustrialResource("Item_CrudeOilBarrel","Crude Oil Barrel","Black gold. Feed it to an Oil Refinery to produce Refined Oil.",       new Color(0.10f, 0.08f, 0.06f), VoxelEngine.Items.ResourceCategory.Raw,       "Oil", maxStack: 50);
            var refinedBarrel = MakeIndustrialResource("Item_RefinedOilBarrel","Refined Oil Barrel","Cracked & distilled oil. Burns clean and feeds plastic synthesis.", new Color(0.50f, 0.30f, 0.10f), VoxelEngine.Items.ResourceCategory.Component, "Oil", maxStack: 50);
            // Refined oil is also a fuel (long burn time): useful in furnaces.
            refinedBarrel.fuelSeconds = 60f; EditorUtility.SetDirty(refinedBarrel);
            var plastic     = MakeIndustrialResource("Item_Plastic",    "Plastic Bar",     "Polymerised hydrocarbon block. Required for advanced circuits & insulation.", new Color(0.92f, 0.92f, 0.96f), VoxelEngine.Items.ResourceCategory.Component, "Oil");

            // ====================================================================
            //  2) FACTORY MACHINES — Pumpjack, Oil Refinery, Wireless Storage Term
            // ====================================================================

            GameObject MakeIndustrialPrefab(string name, Color color, Vector3 scale,
                System.Action<GameObject> configure)
            {
                string path = $"{prefabsFolder}/{name}.prefab";
                var root = new GameObject(name);

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Mesh";
                cube.transform.SetParent(root.transform, false);
                cube.transform.localScale = scale;
                var mat = MakeColoredMat(prefabsFolder, $"Mat_{name}", color);
                cube.GetComponent<Renderer>().sharedMaterial = mat;

                configure?.Invoke(root);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                return prefab;
            }

            // ─ Pumpjack ─
            var pumpjackPrefab = MakeIndustrialPrefab("Pumpjack",
                new Color(0.20f, 0.20f, 0.22f), new Vector3(1.6f, 2.2f, 1.6f),
                root =>
                {
                    var pump = root.AddComponent<VoxelEngine.Crafting.Pumpjack>();
                    pump.emptyBarrel        = emptyBarrel;
                    pump.crudeOilBarrel     = crudeBarrel;
                    pump.secondsPerCycle    = 8f;
                    pump.baseWattsPerSecond = 250f;
                    pump.idleWattsPerSecond = 10f;
                    pump.scanDepth          = 24;
                    pump.scanRadius         = 2;
                });

            // ─ Oil Refinery (CraftingStation + OilRefinery + processing recipes) ─
            string refineryPath = $"{prefabsFolder}/OilRefinery.prefab";
            GameObject refineryPrefab;
            {
                var root = new GameObject("OilRefinery");
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform, false);
                cube.transform.localScale = new Vector3(2.0f, 2.4f, 2.0f);
                cube.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(prefabsFolder, "Mat_OilRefinery", new Color(0.30f, 0.20f, 0.10f));
                var st = root.AddComponent<VoxelEngine.Crafting.CraftingStation>();
                st.tier = VoxelEngine.Crafting.StationTier.Assembler;
                st.displayName = "Oil Refinery";
                root.AddComponent<VoxelEngine.Crafting.OilRefinery>();
                refineryPrefab = PrefabUtility.SaveAsPrefabAsset(root, refineryPath);
                Object.DestroyImmediate(root);
            }

            // ─ Stationary Chemical Plant (world equivalent of the grid Chemical Plant) ─
            string chemPlantPath = $"{prefabsFolder}/StationaryChemicalPlant.prefab";
            GameObject chemPlantPrefab;
            {
                var root = new GameObject("StationaryChemicalPlant");
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform, false);
                cube.transform.localScale = new Vector3(2.2f, 2.4f, 2.2f);
                cube.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(prefabsFolder, "Mat_ChemicalPlant", new Color(0.40f, 0.55f, 0.35f));
                var st = root.AddComponent<VoxelEngine.Crafting.CraftingStation>();
                st.tier = VoxelEngine.Crafting.StationTier.Assembler;
                st.displayName = "Chemical Plant";
                root.AddComponent<VoxelEngine.Industrial.StationaryChemicalPlant>();
                chemPlantPrefab = PrefabUtility.SaveAsPrefabAsset(root, chemPlantPath);
                Object.DestroyImmediate(root);
            }

            // ─ Stationary Docking Port (landing pad) — ships lock to it ─
            string dockPath = $"{prefabsFolder}/StationaryDockingPort.prefab";
            GameObject stationaryDockPrefab;
            {
                var root = new GameObject("StationaryDockingPort");
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.transform.SetParent(root.transform, false);
                pad.transform.localScale = new Vector3(3f, 0.4f, 3f);
                pad.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(prefabsFolder, "Mat_DockPad", new Color(0.55f, 0.55f, 0.20f));
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.transform.SetParent(root.transform, false);
                ring.transform.localScale = new Vector3(1.6f, 0.1f, 1.6f);
                ring.transform.localPosition = new Vector3(0, 0.25f, 0);
                ring.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(prefabsFolder, "Mat_DockRing", new Color(0.2f, 0.7f, 0.9f));
                root.AddComponent<VoxelEngine.GridSystem.BaseDock>();   // auto-adds PlacedBlock
                stationaryDockPrefab = PrefabUtility.SaveAsPrefabAsset(root, dockPath);
                Object.DestroyImmediate(root);
            }

            // ─ Wireless Storage Terminal (uses existing StorageTerminal with isWireless=true) ─
            string wstPath = $"{prefabsFolder}/WirelessStorageTerminal.prefab";
            GameObject wstPrefab;
            {
                var root = new GameObject("WirelessStorageTerminal");
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform, false);
                cube.transform.localScale = new Vector3(0.9f, 1.4f, 0.4f);
                cube.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(prefabsFolder, "Mat_WST", new Color(0.55f, 0.30f, 0.85f));
                var pc = root.AddComponent<VoxelEngine.Power.PowerConsumer>();
                pc.connectRadius = 1.6f; pc.wattsPerSecond = 60f;
                var term = root.AddComponent<VoxelEngine.Storage.StorageTerminal>();
                term.isWireless    = true;
                term.wirelessRange = 60f;
                term.searchRadius  = 12f;
                wstPrefab = PrefabUtility.SaveAsPrefabAsset(root, wstPath);
                Object.DestroyImmediate(root);
            }

            // ─ BlockItems for the new placeables ─
            VoxelEngine.Items.BlockItem MakeIndustrialBlock(string assetName, string display, Color tint, GameObject prefab,
                string desc, string uiCategory = "Industrial", int hp = 600)
            {
                string path = $"{blocksFolder}/{assetName}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var b = ScriptableObject.CreateInstance<VoxelEngine.Items.BlockItem>();
                b.itemId       = assetName.ToLower();
                b.displayName  = display;
                b.description  = desc;
                b.iconTint     = tint;
                b.maxStack     = 50;
                b.massPerUnit  = 8f;
                b.placedPrefab = prefab;
                b.gridSize     = Vector3Int.one;
                b.allowStacking= false;
                b.blockHealth  = hp;
                b.miningTier   = 2;
                b.category     = uiCategory;
                AssetDatabase.CreateAsset(b, path);
                return b;
            }

            var blockPumpjack   = MakeIndustrialBlock("Block_Pumpjack",    "Pumpjack",          new Color(0.20f,0.20f,0.22f), pumpjackPrefab,
                "Powered surface extractor. Scans the column below itself for Crude Oil voxels, lifts one barrel per cycle (consumes an Empty Barrel). Place over an oil pool. ~250 W while running.");
            var blockRefinery   = MakeIndustrialBlock("Block_OilRefinery", "Oil Refinery",      new Color(0.30f,0.20f,0.10f), refineryPrefab,
                "Industrial multi-recipe processor. Crude Oil Barrel → Refined Oil Barrel + Empty Barrel, and Refined Oil + Coal → Plastic Bar + Empty Barrel. 2 input / 4 output / 2 upgrade slots. 400 W base draw.");
            var blockChemPlant  = MakeIndustrialBlock("Block_ChemicalPlant", "Chemical Plant",  new Color(0.40f,0.55f,0.35f), chemPlantPrefab,
                "Industrial chemistry processor. Refined Oil + Plastic → Liquid Fuel + Empty Barrel. 3 input / 3 output slots. 720 W base draw. Shares recipes with the grid Chemical Plant.");
            var blockDock       = MakeIndustrialBlock("Block_StationaryDockingPort", "Docking Pad", new Color(0.55f,0.55f,0.20f), stationaryDockPrefab,
                "Base-side landing pad. Ships with a Docking Port magnetically lock to it for cargo transfer.");
            var blockWirelessST = MakeIndustrialBlock("Block_WirelessStorageTerminal", "Wireless Storage Terminal", new Color(0.55f,0.30f,0.85f), wstPrefab,
                "Access the storage network from up to 60 m away. Requires power and a connected Server Rack. Unlocks with Wireless Access research.");

            // ====================================================================
            //  3) NEW SMELTING RECIPES — Glass
            // ====================================================================
            var smGlass = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.SmeltingRecipe>($"{recipesFolder}/Smelt_Glass.asset");
            if (smGlass == null)
            {
                smGlass = ScriptableObject.CreateInstance<VoxelEngine.Crafting.SmeltingRecipe>();
                AssetDatabase.CreateAsset(smGlass, $"{recipesFolder}/Smelt_Glass.asset");
            }
            smGlass.input = sand; smGlass.inputCount = 1;
            smGlass.output = glass; smGlass.outputCount = 1;
            smGlass.smeltSeconds = 4f;
            EditorUtility.SetDirty(smGlass);

            // Append glass smelting to the existing Furnace + Electric Furnace prefabs.
            AppendSmeltingRecipe($"{ASSET_ROOT}/StationPrefabs/Furnace.prefab",  smGlass);
            AppendSmeltingRecipe($"{ASSET_ROOT}/Power/Prefabs/ElectricFurnace.prefab", smGlass, electric: true);

            // ====================================================================
            //  4) NEW PROCESSING RECIPES — for the Oil Refinery
            // ====================================================================
            VoxelEngine.Crafting.ProcessingRecipe MakeProc(string assetName, string display, string category,
                (VoxelEngine.Items.ItemDefinition item, int n)[] inputs,
                (VoxelEngine.Items.ItemDefinition item, int n)[] outputs,
                float seconds, float powerMul = 1f,
                (VoxelEngine.Items.LiquidType liquid, float litres)[] fluidIn = null,
                (VoxelEngine.Items.LiquidType liquid, float litres)[] fluidOut = null)
            {
                string path = $"{procRecFolder}/{assetName}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var r = ScriptableObject.CreateInstance<VoxelEngine.Crafting.ProcessingRecipe>();
                r.displayName = display;
                r.category    = category;
                r.secondsPerBatch     = seconds;
                r.powerDrawMultiplier = powerMul;
                r.inputs  = new VoxelEngine.Crafting.ProcessingIO[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                    r.inputs[i]  = new VoxelEngine.Crafting.ProcessingIO { item = inputs[i].item,  count = inputs[i].n };
                r.outputs = new VoxelEngine.Crafting.ProcessingIO[outputs.Length];
                for (int i = 0; i < outputs.Length; i++)
                    r.outputs[i] = new VoxelEngine.Crafting.ProcessingIO { item = outputs[i].item, count = outputs[i].n };
                fluidIn  ??= System.Array.Empty<(VoxelEngine.Items.LiquidType, float)>();
                fluidOut ??= System.Array.Empty<(VoxelEngine.Items.LiquidType, float)>();
                r.fluidInputs  = new VoxelEngine.Crafting.FluidIO[fluidIn.Length];
                for (int i = 0; i < fluidIn.Length; i++)
                    r.fluidInputs[i]  = new VoxelEngine.Crafting.FluidIO { liquid = fluidIn[i].liquid,  litres = fluidIn[i].litres };
                r.fluidOutputs = new VoxelEngine.Crafting.FluidIO[fluidOut.Length];
                for (int i = 0; i < fluidOut.Length; i++)
                    r.fluidOutputs[i] = new VoxelEngine.Crafting.FluidIO { liquid = fluidOut[i].liquid, litres = fluidOut[i].litres };
                AssetDatabase.CreateAsset(r, path);
                return r;
            }

            var noItems = System.Array.Empty<(VoxelEngine.Items.ItemDefinition, int)>();

            // Crude → Refined: 100 L crude oil (liquid) → 80 L refined oil (liquid).
            var procRefine  = MakeProc("Proc_RefineOil", "Refine Crude Oil", "Refinery",
                noItems, noItems, seconds: 12f, powerMul: 1f,
                fluidIn:  new[] { (VoxelEngine.Items.LiquidType.CrudeOil,   100f) },
                fluidOut: new[] { (VoxelEngine.Items.LiquidType.RefinedOil,  80f) });

            // Refined oil (liquid) + coal → 2 plastic.  (No more barrels.)
            var procPlastic = MakeProc("Proc_MakePlastic", "Synthesise Plastic", "Plastics",
                new[] { (coal, 1) },
                new[] { ((VoxelEngine.Items.ItemDefinition)plastic, 2) },
                seconds: 14f, powerMul: 1.25f,
                fluidIn: new[] { (VoxelEngine.Items.LiquidType.RefinedOil, 50f) });

            // Liquid Fuel item — kept as the bottled/transportable product.
            var liquidFuel  = MakeIndustrialResource("Item_LiquidFuel", "Liquid Fuel",
                "High-performance synthesised fuel. Powers advanced thrusters and engines.",
                new Color(0.95f, 0.65f, 0.15f), VoxelEngine.Items.ResourceCategory.Component, "Oil", maxStack: 50);
            liquidFuel.fuelSeconds = 180f; EditorUtility.SetDirty(liquidFuel);

            // Chemistry: 60 L refined oil + 1 plastic → 100 L Liquid Fuel (tank) + 2 Liquid Fuel (item).
            var procLiquidFuel = MakeProc("Proc_MakeLiquidFuel", "Synthesise Liquid Fuel", "Chemistry",
                new[] { ((VoxelEngine.Items.ItemDefinition)plastic, 1) },
                new[] { ((VoxelEngine.Items.ItemDefinition)liquidFuel, 2) },
                seconds: 16f, powerMul: 1.3f,
                fluidIn:  new[] { (VoxelEngine.Items.LiquidType.RefinedOil, 60f) },
                fluidOut: new[] { (VoxelEngine.Items.LiquidType.LiquidFuel, 100f) });

            // Attach those recipes to the OilRefinery prefab.
            AppendOilRefineryRecipes(refineryPrefab, new List<VoxelEngine.Crafting.ProcessingRecipe> { procRefine, procPlastic });

            // Attach the Chemistry recipe to the Stationary Chemical Plant prefab.
            AppendChemicalPlantRecipes(chemPlantPrefab, new List<VoxelEngine.Crafting.ProcessingRecipe> { procLiquidFuel });

            // ====================================================================
            //  5) NEW CRAFTING RECIPES — registered into the global RecipeRegistry
            // ====================================================================
            VoxelEngine.Crafting.RecipeDefinition AddRecipe(string assetName, string display,
                VoxelEngine.Items.ItemDefinition output, int outputCount,
                VoxelEngine.Crafting.StationTier station,
                bool unlockedByDefault,
                params (VoxelEngine.Items.ItemDefinition item, int n)[] inputs)
            {
                string path = $"{recipesFolder}/{assetName}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var r = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                r.displayName       = display;
                r.outputItem        = output;
                r.outputCount       = outputCount;
                r.requiredStation   = station;
                r.craftSeconds      = station switch
                {
                    VoxelEngine.Crafting.StationTier.None          => 0f,
                    VoxelEngine.Crafting.StationTier.CraftingBench => 2f,
                    VoxelEngine.Crafting.StationTier.Furnace       => 0f,
                    VoxelEngine.Crafting.StationTier.Assembler     => 4f,
                    _ => 0f
                };
                r.unlockedByDefault = unlockedByDefault;
                r.inputs = new VoxelEngine.Crafting.RecipeIngredient[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                    r.inputs[i] = new VoxelEngine.Crafting.RecipeIngredient { item = inputs[i].item, count = inputs[i].n };
                AssetDatabase.CreateAsset(r, path);
                if (!registry.recipes.Contains(r)) registry.recipes.Add(r);
                return r;
            }

            // Remove any old duplicates of recipes we are about to (re)create so the
            // registry doesn't grow stale entries on every re-run.
            string[] toReplace =
            {
                "Recipe_IronPlate","Recipe_CopperPlate","Recipe_SteelPlate",
                "Recipe_IronGear","Recipe_CopperWire","Recipe_Circuit","Recipe_AdvCircuit",
                "Recipe_EmptyBarrel","Recipe_Pumpjack","Recipe_OilRefinery",
                "Recipe_Plastic_PlayerHint",
                "Recipe_WirelessStorageTerminal",
            };
            registry.recipes.RemoveAll(r => r != null && System.Array.IndexOf(toReplace, r.name) >= 0);

            // ── Plating tier (gated by "Plating" research) ──
            var recIronPlate   = AddRecipe("Recipe_IronPlate",   "Iron Plate",   ironPlate,   1, VoxelEngine.Crafting.StationTier.Assembler,     unlockedByDefault: false, (ironIngot,   2));
            var recCopperPlate = AddRecipe("Recipe_CopperPlate", "Copper Plate", copperPlate, 1, VoxelEngine.Crafting.StationTier.Assembler,     unlockedByDefault: false, (copperIngot, 2));
            var recSteelPlate  = AddRecipe("Recipe_SteelPlate",  "Steel Plate",  steelPlate,  1, VoxelEngine.Crafting.StationTier.Assembler,     unlockedByDefault: false, (steelIngot,  2));
            var recIronGear    = AddRecipe("Recipe_IronGear",    "Iron Gear",    ironGear,    1, VoxelEngine.Crafting.StationTier.Assembler,     unlockedByDefault: false, (ironPlate,   2));

            // ── Wires & circuits (gated by Electronics / Adv Electronics) ──
            var recCopperWire  = AddRecipe("Recipe_CopperWire",  "Copper Wire x2",     copperWire, 2, VoxelEngine.Crafting.StationTier.CraftingBench, unlockedByDefault: false, (copperIngot, 1));
            var recCircuit     = AddRecipe("Recipe_Circuit",     "Electronic Circuit", circuitBasic, 1, VoxelEngine.Crafting.StationTier.Assembler,    unlockedByDefault: false, (ironPlate, 1), (copperWire, 3));
            var recAdvCircuit  = AddRecipe("Recipe_AdvCircuit",  "Advanced Circuit",   circuitAdv,   1, VoxelEngine.Crafting.StationTier.Assembler,    unlockedByDefault: false, (circuitBasic, 2), (plastic, 2), (copperWire, 4));

            // ── Oil chain (gated by Oil Extraction / Refining / Plastics) ──
            var recEmptyBarrel = AddRecipe("Recipe_EmptyBarrel", "Empty Barrel",  emptyBarrel,   1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (steelPlate, 1), (ironPlate, 2));
            var recPumpjack    = AddRecipe("Recipe_Pumpjack",    "Pumpjack",      blockPumpjack, 1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (steelPlate, 8), (ironGear, 6), (circuitBasic, 2));
            var recRefinery    = AddRecipe("Recipe_OilRefinery", "Oil Refinery",  blockRefinery, 1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (steelPlate, 12), (ironGear, 8), (circuitBasic, 4), (copperPlate, 4));
            var recChemPlant   = AddRecipe("Recipe_ChemicalPlant", "Chemical Plant", blockChemPlant, 1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (steelPlate, 12), (circuitAdv, 4), (copperWire, 8), (glass, 4));
            var recDock        = AddRecipe("Recipe_StationaryDockingPort", "Docking Pad", blockDock, 1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: true, (steelPlate, 10), (copperWire, 6));

            // ── Wireless Storage Terminal (gated by Wireless Access) ──
            var recWST = AddRecipe("Recipe_WirelessStorageTerminal", "Wireless Storage Terminal", blockWirelessST, 1,
                VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false,
                (steelPlate, 4), (advCircuitOrFallback(circuitAdv, circuitBasic), 3), (copperWire, 8), (glass, 2));

            // ── Re-cost existing storage / power / quarry recipes to use plates+circuits ──
            // We REPLACE the inputs of these recipes in-place so they "make sense" in the new economy.
            // NOTE: the explicit (ItemDefinition) cast on the FIRST tuple element of
            // each array widens the inferred type from ResourceItem → ItemDefinition,
            // matching the helper's param signature. (C# does not apply tuple
            // covariance inside array literals.)
            RewireExistingRecipe("Recipe_Generator",      new[] { ((VoxelEngine.Items.ItemDefinition)ironPlate, 4), (ironGear, 2), (stone, 4) });
            RewireExistingRecipe("Recipe_Battery",        new[] { ((VoxelEngine.Items.ItemDefinition)ironPlate, 2), (copperWire, 6), (copperPlate, 2) });
            RewireExistingRecipe("Recipe_Cable_Copper",   new[] { ((VoxelEngine.Items.ItemDefinition)copperIngot, 1) });   // unchanged: 1 ingot → 4 cables
            RewireExistingRecipe("Recipe_Cable_Iron",     new[] { ((VoxelEngine.Items.ItemDefinition)ironPlate,   1) });   // now uses plates instead of ingots
            RewireExistingRecipe("Recipe_Cable_Gold",     new[] { (goldOreOrFallback(goldOre, ironIngot), 1), ((VoxelEngine.Items.ItemDefinition)copperWire, 2) });
            RewireExistingRecipe("Recipe_Cable_Super",    new[] { ((VoxelEngine.Items.ItemDefinition)steelPlate, 1), (goldOreOrFallback(goldOre, ironIngot), 1), (copperWire, 2) });
            RewireExistingRecipe("Recipe_ElectricFurnace",new[] { ((VoxelEngine.Items.ItemDefinition)steelPlate, 4), (circuitBasic, 2), (copperPlate, 2) });
            RewireExistingRecipe("Recipe_Assembler",      new[] { ((VoxelEngine.Items.ItemDefinition)ironPlate, 6), (ironGear, 4), (circuitBasic, 1), (stone, 8) });
            RewireExistingRecipe("Recipe_Wireless_Tx",    new[] { ((VoxelEngine.Items.ItemDefinition)steelPlate, 4), (advCircuitOrFallback(circuitAdv, circuitBasic), 2), (copperWire, 8) });
            RewireExistingRecipe("Recipe_Wireless_Rx",    new[] { ((VoxelEngine.Items.ItemDefinition)steelPlate, 2), (advCircuitOrFallback(circuitAdv, circuitBasic), 1), (copperWire, 6) });

            // ====================================================================
            //  6) RESEARCH NODES — expansion of the existing tree
            // ====================================================================
            const string treePath = researchFolder + "/ResearchTree.asset";
            var tree = AssetDatabase.LoadAssetAtPath<VoxelEngine.Research.ResearchTree>(treePath);
            if (tree == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine", "Run Step 7 (Build Research Content) first — the ResearchTree asset doesn't exist yet.", "OK");
                return;
            }

            // Helper: find an already-existing node by id (so we can use it as a prerequisite).
            VoxelEngine.Research.ResearchNode FindNode(string id)
            {
                foreach (var nd in tree.nodes)
                    if (nd != null && nd.nodeId == id) return nd;
                return null;
            }

            var nStoneWorking      = FindNode("res_stone_working");
            var nSmelting          = FindNode("res_smelting");
            var nElectricity       = FindNode("res_electricity");
            var nAdvMfg            = FindNode("res_adv_manufacturing");
            var nSteelAlloy        = FindNode("res_steel_alloy");
            var nHighVoltage       = FindNode("res_high_voltage");

            VoxelEngine.Research.ResearchNode MakeOrUpdateEnvNode(
                string id, string display, string desc,
                int tier, int col, VoxelEngine.Research.ResearchSubCategory sub,
                Color tint, float seconds,
                (VoxelEngine.Items.ScienceItem p, int n)[] cost,
                VoxelEngine.Crafting.RecipeDefinition[] unlocks,
                VoxelEngine.Research.ResearchNode[] prereqs = null)
            {
                string path = $"{nodesFolder}/{id}.asset";
                var n = AssetDatabase.LoadAssetAtPath<VoxelEngine.Research.ResearchNode>(path);
                if (n == null)
                {
                    n = ScriptableObject.CreateInstance<VoxelEngine.Research.ResearchNode>();
                    AssetDatabase.CreateAsset(n, path);
                }
                n.nodeId         = id;
                n.displayName    = display;
                n.description    = desc;
                n.category       = VoxelEngine.Research.ResearchCategory.Environment;
                n.subCategory    = sub;
                n.tier           = tier;
                n.column         = col;
                n.iconTint       = tint;
                n.researchSeconds= seconds;
                n.cost = new VoxelEngine.Research.ResearchNode.ScienceCost[cost.Length];
                for (int i = 0; i < cost.Length; i++)
                    n.cost[i] = new VoxelEngine.Research.ResearchNode.ScienceCost { pack = cost[i].p, count = cost[i].n };
                var unl = new List<VoxelEngine.Crafting.RecipeDefinition>();
                foreach (var u in unlocks ?? new VoxelEngine.Crafting.RecipeDefinition[0])
                    if (u != null) unl.Add(u);
                n.unlocksRecipes = unl.ToArray();
                n.prerequisites  = prereqs ?? new VoxelEngine.Research.ResearchNode[0];
                n.upgradeKind    = VoxelEngine.Research.PlayerUpgradeKind.None;
                n.maxRanks       = 1;
                EditorUtility.SetDirty(n);
                if (!tree.nodes.Contains(n)) tree.nodes.Add(n);
                return n;
            }

            // Re-tag existing nodes with sub-categories so they show up in the new tabs.
            void Retag(VoxelEngine.Research.ResearchNode n, VoxelEngine.Research.ResearchSubCategory sub, Color tint)
            {
                if (n == null) return;
                n.subCategory = sub;
                n.iconTint    = tint;
                EditorUtility.SetDirty(n);
            }
            Retag(nStoneWorking, VoxelEngine.Research.ResearchSubCategory.Production, new Color(0.55f, 0.50f, 0.45f));
            Retag(nSmelting,     VoxelEngine.Research.ResearchSubCategory.Production, new Color(0.85f, 0.55f, 0.30f));
            Retag(nElectricity,  VoxelEngine.Research.ResearchSubCategory.Power,      new Color(0.95f, 0.85f, 0.30f));
            Retag(nAdvMfg,       VoxelEngine.Research.ResearchSubCategory.Production, new Color(0.30f, 0.65f, 0.95f));
            Retag(nSteelAlloy,   VoxelEngine.Research.ResearchSubCategory.Production, new Color(0.60f, 0.62f, 0.68f));
            Retag(nHighVoltage,  VoxelEngine.Research.ResearchSubCategory.Power,      new Color(0.45f, 0.85f, 1.00f));

            // ── New nodes ──

            // T2 — Plating: unlocks iron / copper / steel plates and iron gear.
            var nPlating = MakeOrUpdateEnvNode("res_plating", "Plating",
                "Stamp ingots into plates and cut them into gears. Plates are the structural backbone of every advanced machine.",
                tier: 2, col: 2, sub: VoxelEngine.Research.ResearchSubCategory.Production,
                tint: new Color(0.78f, 0.80f, 0.85f), seconds: 35f,
                cost: new[] { (sciT1, 15), (sciT2, 8) },
                unlocks: new[] { recIronPlate, recCopperPlate, recSteelPlate, recIronGear },
                prereqs: new[] { nSmelting });

            // T2 — Glassworking: unlocks glass smelting (already gates Glass tank/pipe recipes if present).
            var recPipeGlass = FindRecipeByName("Recipe_PipeGlass");
            var recTankGlass = FindRecipeByName("Recipe_TankGlass");
            var nGlasswork = MakeOrUpdateEnvNode("res_glassworking", "Glassworking",
                "Smelt sand into glass. Required for lab equipment, decorative pipes, and observation tanks.",
                tier: 2, col: 3, sub: VoxelEngine.Research.ResearchSubCategory.Chemistry,
                tint: new Color(0.70f, 0.88f, 0.95f), seconds: 30f,
                cost: new[] { (sciT1, 10), (sciT2, 5) },
                unlocks: new[] { recPipeGlass, recTankGlass },
                prereqs: new[] { nSmelting });
            // Lock the glass-tank / glass-pipe recipes so the node actually gates them.
            LockRecipe(recPipeGlass); LockRecipe(recTankGlass);

            // T2 — Fluid Handling: gates the existing water bucket / pump / pipes / tanks.
            var recWaterBucket = FindRecipeByName("Recipe_WaterBucket");
            var recPipeSolid   = FindRecipeByName("Recipe_PipeSolid");
            var recTankSolid   = FindRecipeByName("Recipe_TankSolid");
            var recWaterPump   = FindRecipeByName("Recipe_WaterPump");
            var nFluidHandling = MakeOrUpdateEnvNode("res_fluid_handling", "Fluid Handling",
                "Move water with pumps and pipes. Foundational for refining, cooling, and farming.",
                tier: 2, col: 4, sub: VoxelEngine.Research.ResearchSubCategory.Logistics,
                tint: new Color(0.40f, 0.85f, 0.95f), seconds: 40f,
                cost: new[] { (sciT1, 12), (sciT2, 6) },
                unlocks: new[] { recWaterBucket, recPipeSolid, recTankSolid, recWaterPump },
                prereqs: new[] { nSmelting });
            LockRecipe(recWaterBucket); LockRecipe(recPipeSolid); LockRecipe(recTankSolid); LockRecipe(recWaterPump);

            // T3 — Electronics: unlocks Copper Wire + Electronic Circuit. Prereqs: Plating + Electricity.
            var nElectronics = MakeOrUpdateEnvNode("res_electronics", "Electronics",
                "Draw copper into wire and laminate it onto iron substrate to make Electronic Circuits — the prerequisite for nearly every advanced machine.",
                tier: 3, col: 3, sub: VoxelEngine.Research.ResearchSubCategory.Production,
                tint: new Color(0.30f, 0.65f, 0.40f), seconds: 60f,
                cost: new[] { (sciT2, 20), (sciT3, 8) },
                unlocks: new[] { recCopperWire, recCircuit },
                prereqs: new[] { nPlating, nElectricity });

            // T4 — Advanced Electronics: unlocks Adv Circuit. Needs Electronics + Adv Mfg + Plastics (chain).
            // Adv-Circuit recipe also requires plastic — players must complete Plastics first to truly use it.
            var nAdvElectronics = MakeOrUpdateEnvNode("res_adv_electronics", "Advanced Electronics",
                "Multi-layer logic boards built from plastic, copper wire, and basic circuits. Powers wireless access, mass storage, and nuclear control.",
                tier: 4, col: 3, sub: VoxelEngine.Research.ResearchSubCategory.Production,
                tint: new Color(0.30f, 0.55f, 0.95f), seconds: 90f,
                cost: new[] { (sciT2, 30), (sciT3, 15) },
                unlocks: new[] { recAdvCircuit },
                prereqs: new[] { nElectronics, nAdvMfg });

            // T3 — Oil Extraction: unlocks Empty Barrel + Pumpjack. Prereqs: Steel Alloy.
            var nOilExtraction = MakeOrUpdateEnvNode("res_oil_extraction", "Oil Extraction",
                "Press steel into Empty Barrels and assemble Pumpjacks to extract Crude Oil from underground reservoirs.",
                tier: 3, col: 4, sub: VoxelEngine.Research.ResearchSubCategory.Chemistry,
                tint: new Color(0.10f, 0.08f, 0.06f), seconds: 70f,
                cost: new[] { (sciT2, 20), (sciT3, 10) },
                unlocks: new[] { recEmptyBarrel, recPumpjack },
                prereqs: new[] { nSteelAlloy });

            // T4 — Oil Refining: unlocks Oil Refinery. Prereqs: Oil Extraction + Electronics.
            var nOilRefining = MakeOrUpdateEnvNode("res_oil_refining", "Oil Refining",
                "Crack and distil crude oil into Refined Oil — a high-energy industrial feedstock.",
                tier: 4, col: 4, sub: VoxelEngine.Research.ResearchSubCategory.Chemistry,
                tint: new Color(0.50f, 0.30f, 0.10f), seconds: 90f,
                cost: new[] { (sciT2, 25), (sciT3, 15) },
                unlocks: new[] { recRefinery, recChemPlant },
                prereqs: new[] { nOilExtraction, nElectronics });

            // T4 — Plastics: enables the plastic processing recipe (already loaded into refinery prefab).
            // No separate "recipe" item to unlock for plastic — it's automatic in the refinery — but
            // we keep the node so the player knows when plastic synthesis is possible (and the refinery
            // simply ignores recipes it doesn't yet "know about" via this node-driven flag in the future).
            var nPlastics = MakeOrUpdateEnvNode("res_plastics", "Plastics",
                "Polymerise refined oil and coal into Plastic Bars. Required for advanced circuits, durable insulation, and end-game logistics.",
                tier: 4, col: 5, sub: VoxelEngine.Research.ResearchSubCategory.Chemistry,
                tint: new Color(0.92f, 0.92f, 0.96f), seconds: 80f,
                cost: new[] { (sciT2, 20), (sciT3, 15) },
                unlocks: new VoxelEngine.Crafting.RecipeDefinition[0],
                prereqs: new[] { nOilRefining });

            // ─ Logistics Network (storage system) ─
            // We don't have the storage recipes auto-built in any earlier step, so this node simply
            // *unlocks* any recipe we can find by name. Authoring the storage recipes themselves is
            // delegated to a future step (or manual ScriptableObject creation). We still lock anything
            // we DO find so progression remains gated.
            VoxelEngine.Crafting.RecipeDefinition[] StorageRecipes(params string[] names)
            {
                var list = new List<VoxelEngine.Crafting.RecipeDefinition>();
                foreach (var nm in names)
                {
                    var r = FindRecipeByName(nm);
                    if (r != null) { LockRecipe(r); list.Add(r); }
                }
                return list.ToArray();
            }

            var nLogistics = MakeOrUpdateEnvNode("res_logistics_network", "Logistics Network",
                "Server Racks, Storage Disks (1K, 4K), Importers, Exporters, and the wired Storage Terminal. The foundation of automated inventory management.",
                tier: 3, col: 5, sub: VoxelEngine.Research.ResearchSubCategory.Storage,
                tint: new Color(0.55f, 0.65f, 0.95f), seconds: 75f,
                cost: new[] { (sciT2, 25), (sciT3, 10) },
                unlocks: StorageRecipes("Recipe_ServerRack","Recipe_StorageTerminal","Recipe_StorageDisk1K","Recipe_StorageDisk4K","Recipe_StorageImporter","Recipe_StorageExporter","Recipe_NASBlock_Hint"),
                prereqs: new[] { nElectronics });

            var nMassStorage = MakeOrUpdateEnvNode("res_mass_storage", "Mass Storage",
                "16K Storage Disks and NAS expansion blocks. For factories that produce thousands of items per minute.",
                tier: 4, col: 5, sub: VoxelEngine.Research.ResearchSubCategory.Storage,
                tint: new Color(0.45f, 0.55f, 0.95f), seconds: 100f,
                cost: new[] { (sciT2, 30), (sciT3, 20) },
                unlocks: StorageRecipes("Recipe_StorageDisk16K","Recipe_NASBlock"),
                prereqs: new[] { nLogistics });

            var nCrystalline = MakeOrUpdateEnvNode("res_crystalline_storage", "Crystalline Storage",
                "64K and 90K Storage Disks. Functionally bottomless storage for endgame mega-bases.",
                tier: 5, col: 5, sub: VoxelEngine.Research.ResearchSubCategory.Storage,
                tint: new Color(0.85f, 0.60f, 0.95f), seconds: 150f,
                cost: new[] { (sciT3, 40) },
                unlocks: StorageRecipes("Recipe_StorageDisk64K","Recipe_StorageDisk90K"),
                prereqs: new[] { nMassStorage, nAdvElectronics });

            // ─ Wireless Access (user-requested) ─
            var nWirelessAccess = MakeOrUpdateEnvNode("res_wireless_access", "Wireless Access",
                "Access your storage network from anywhere on the map. Unlocks the Wireless Storage Terminal — drop it in your base, power it, and your inventory becomes a portal to the whole network within 60 m.",
                tier: 4, col: 6, sub: VoxelEngine.Research.ResearchSubCategory.Storage,
                tint: new Color(0.55f, 0.30f, 0.85f), seconds: 120f,
                cost: new[] { (sciT2, 25), (sciT3, 20) },
                unlocks: new[] { recWST },
                prereqs: new[] { nLogistics, nHighVoltage });

            // ─ Item Logistics (item pipes) ─
            var nItemLogistics = MakeOrUpdateEnvNode("res_item_logistics", "Item Logistics",
                "Item Pipes carry stacks between chests and machines without manual hauling.",
                tier: 3, col: 6, sub: VoxelEngine.Research.ResearchSubCategory.Logistics,
                tint: new Color(0.40f, 0.85f, 0.95f), seconds: 50f,
                cost: new[] { (sciT2, 15), (sciT3, 5) },
                unlocks: StorageRecipes("Recipe_ItemPipe"),
                prereqs: new[] { nElectricity });

            // ─ Quarrying ─
            var nQuarrying = MakeOrUpdateEnvNode("res_quarrying", "Quarrying",
                "Industrial-scale automated mining. The Quarry block strip-mines an entire 16×16 area down to bedrock without player input. Upgrade with Range, Speed & Efficiency modules.",
                tier: 4, col: 6, sub: VoxelEngine.Research.ResearchSubCategory.Production,
                tint: new Color(0.50f, 0.55f, 0.60f), seconds: 110f,
                cost: new[] { (sciT2, 30), (sciT3, 18) },
                unlocks: StorageRecipes("Recipe_Quarry","Recipe_QuarryUpgradeRange","Recipe_QuarryUpgradeSpeed","Recipe_QuarryUpgradeEfficiency"),
                prereqs: new[] { nSteelAlloy, nItemLogistics });

            // ─ Gas Processing (Electrolyser, Hydrogen Engine, Gas Tank/Pipe) ─
            var nGasProcessing = MakeOrUpdateEnvNode("res_gas_processing", "Gas Processing",
                "Split ice into hydrogen and oxygen with an Electrolyser. Burn the hydrogen in a clean engine for steady, fuel-light power.",
                tier: 4, col: 4, sub: VoxelEngine.Research.ResearchSubCategory.Chemistry,
                tint: new Color(0.50f, 0.85f, 0.45f), seconds: 100f,
                cost: new[] { (sciT2, 25), (sciT3, 15) },
                unlocks: StorageRecipes("Recipe_Electrolyser","Recipe_HydrogenEngine","Recipe_GasTank","Recipe_GasPipe"),
                prereqs: new[] { nElectronics, nFluidHandling });

            // ─ Nuclear Fission ─
            var nNuclear = MakeOrUpdateEnvNode("res_nuclear_fission", "Nuclear Fission",
                "Uranium enrichment, reactor cores, steam turbines, portable reactors. The pinnacle of power generation — extremely dangerous if mismanaged.",
                tier: 5, col: 2, sub: VoxelEngine.Research.ResearchSubCategory.Power,
                tint: new Color(0.40f, 0.95f, 0.40f), seconds: 200f,
                cost: new[] { (sciT3, 60) },
                unlocks: StorageRecipes("Recipe_UraniumProcessor","Recipe_ReactorCore","Recipe_SteamTurbine","Recipe_PortableReactor","Recipe_WasteReprocessor"),
                prereqs: new[] { nAdvElectronics, nGasProcessing });

            // Save the tree.
            EditorUtility.SetDirty(tree);
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Industrial Pack",
                "Industrial content pack created!\n\n" +
                "ITEMS\n" +
                "  • Iron / Copper / Steel Plate, Iron Gear\n" +
                "  • Copper Wire, Electronic Circuit, Advanced Circuit\n" +
                "  • Glass (smelted at any furnace from Sand)\n" +
                "  • Empty / Crude / Refined Oil Barrel, Plastic Bar\n\n" +
                "BLOCKS\n" +
                "  • Pumpjack (extracts oil from underground reservoirs)\n" +
                "  • Oil Refinery (Crude → Refined → Plastic)\n" +
                "  • Wireless Storage Terminal\n\n" +
                "RESEARCH TREE (new nodes)\n" +
                "  T2: Plating, Glassworking, Fluid Handling\n" +
                "  T3: Electronics, Logistics Network, Item Logistics, Oil Extraction\n" +
                "  T4: Adv Electronics, Oil Refining, Plastics, Mass Storage,\n" +
                "      Wireless Access, Quarrying, Gas Processing\n" +
                "  T5: Crystalline Storage, Nuclear Fission\n\n" +
                "Many existing recipes were re-costed (machines now need plates+gears+circuits).\n" +
                "Open the new Factorio-style Research UI in-game (key Y) to explore.", "OK");
        }

        // ────────────────────────────────────────────────────────────
        //  Step 10 helpers
        // ────────────────────────────────────────────────────────────
        private static VoxelEngine.Items.ItemDefinition advCircuitOrFallback(VoxelEngine.Items.ItemDefinition adv, VoxelEngine.Items.ItemDefinition fallback)
            => adv != null ? adv : fallback;
        private static VoxelEngine.Items.ItemDefinition goldOreOrFallback(VoxelEngine.Items.ItemDefinition g, VoxelEngine.Items.ItemDefinition fallback)
            => g != null ? g : fallback;

        private static VoxelEngine.Crafting.RecipeDefinition FindRecipeByName(string assetName)
        {
            var guids = AssetDatabase.FindAssets($"{assetName} t:RecipeDefinition");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == assetName)
                    return AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(p);
            }
            return null;
        }

        private static void LockRecipe(VoxelEngine.Crafting.RecipeDefinition r)
        {
            if (r == null) return;
            r.unlockedByDefault = false;
            EditorUtility.SetDirty(r);
        }

        private static void RewireExistingRecipe(string assetName, (VoxelEngine.Items.ItemDefinition item, int n)[] inputs)
        {
            var r = FindRecipeByName(assetName);
            if (r == null) return;
            int count = 0;
            foreach (var i in inputs) if (i.item != null && i.n > 0) count++;
            r.inputs = new VoxelEngine.Crafting.RecipeIngredient[count];
            int j = 0;
            foreach (var i in inputs)
            {
                if (i.item == null || i.n <= 0) continue;
                r.inputs[j++] = new VoxelEngine.Crafting.RecipeIngredient { item = i.item, count = i.n };
            }
            EditorUtility.SetDirty(r);
        }

        private static void AppendSmeltingRecipe(string prefabPath, VoxelEngine.Crafting.SmeltingRecipe smRecipe, bool electric = false)
        {
            if (smRecipe == null) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return;
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if (electric)
                {
                    var furn = contents.GetComponent<VoxelEngine.Crafting.ElectricFurnace>();
                    if (furn != null)
                    {
                        if (furn.knownRecipes == null) furn.knownRecipes = new List<VoxelEngine.Crafting.SmeltingRecipe>();
                        if (!furn.knownRecipes.Contains(smRecipe)) furn.knownRecipes.Add(smRecipe);
                    }
                }
                else
                {
                    var furn = contents.GetComponent<VoxelEngine.Crafting.Furnace>();
                    if (furn != null)
                    {
                        if (furn.knownRecipes == null) furn.knownRecipes = new List<VoxelEngine.Crafting.SmeltingRecipe>();
                        if (!furn.knownRecipes.Contains(smRecipe)) furn.knownRecipes.Add(smRecipe);
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static void AppendOilRefineryRecipes(GameObject prefab, List<VoxelEngine.Crafting.ProcessingRecipe> recipes)
        {
            if (prefab == null) return;
            string path = AssetDatabase.GetAssetPath(prefab);
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var ref0 = contents.GetComponent<VoxelEngine.Crafting.OilRefinery>();
                if (ref0 != null)
                {
                    if (ref0.knownRecipes == null) ref0.knownRecipes = new List<VoxelEngine.Crafting.ProcessingRecipe>();
                    foreach (var r in recipes)
                        if (r != null && !ref0.knownRecipes.Contains(r))
                            ref0.knownRecipes.Add(r);
                }
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static void AppendChemicalPlantRecipes(GameObject prefab, List<VoxelEngine.Crafting.ProcessingRecipe> recipes)
        {
            if (prefab == null) return;
            string path = AssetDatabase.GetAssetPath(prefab);
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var cp = contents.GetComponent<VoxelEngine.Industrial.StationaryChemicalPlant>();
                if (cp != null)
                {
                    if (cp.knownRecipes == null) cp.knownRecipes = new List<VoxelEngine.Crafting.ProcessingRecipe>();
                    foreach (var r in recipes)
                        if (r != null && !cp.knownRecipes.Contains(r))
                            cp.knownRecipes.Add(r);
                }
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        // ============================================================
        //  STEP 11 - SURVIVAL + INDUSTRIAL LOGISTICS CONTENT
        //  Fills in: Farming, Storage Network, Item Pipes, Quarry,
        //            Gas Processing, Nuclear Fission
        //  Connects to the research nodes already authored in Step 10.
        // ============================================================
        private void BuildSurvivalAndLogisticsContent()
        {
            // -------- Folders --------
            const string ROOT          = ASSET_ROOT + "/Survival";
            const string ITEMS         = ROOT + "/Items";
            const string FARM_PREFABS  = ROOT + "/FarmPrefabs";
            const string FARM_BLOCKS   = ROOT + "/FarmBlocks";
            const string FARM_CROPS    = ROOT + "/Crops";
            const string FARM_RECIPES  = ROOT + "/FarmRecipes";
            const string STORE_PREFABS = ROOT + "/StoragePrefabs";
            const string STORE_BLOCKS  = ROOT + "/StorageBlocks";
            const string STORE_ITEMS   = ROOT + "/StorageItems";
            const string STORE_RECIPES = ROOT + "/StorageRecipes";
            const string NUKE_PREFABS  = ROOT + "/NuclearPrefabs";
            const string NUKE_BLOCKS   = ROOT + "/NuclearBlocks";
            const string NUKE_ITEMS    = ROOT + "/NuclearItems";
            const string NUKE_RECIPES  = ROOT + "/NuclearRecipes";
            const string GAS_PREFABS   = ROOT + "/GasPrefabs";
            const string GAS_BLOCKS    = ROOT + "/GasBlocks";
            const string GAS_RECIPES   = ROOT + "/GasRecipes";
            const string MISC_PREFABS  = ROOT + "/MiscPrefabs";
            const string MISC_BLOCKS   = ROOT + "/MiscBlocks";
            const string MISC_RECIPES  = ROOT + "/MiscRecipes";
            const string MISC_ITEMS    = ROOT + "/MiscItems";

            foreach (var f in new[] {
                ROOT, ITEMS, FARM_PREFABS, FARM_BLOCKS, FARM_CROPS, FARM_RECIPES,
                STORE_PREFABS, STORE_BLOCKS, STORE_ITEMS, STORE_RECIPES,
                NUKE_PREFABS, NUKE_BLOCKS, NUKE_ITEMS, NUKE_RECIPES,
                GAS_PREFABS, GAS_BLOCKS, GAS_RECIPES,
                MISC_PREFABS, MISC_BLOCKS, MISC_RECIPES, MISC_ITEMS,
            }) EnsureFolder(f);

            // -------- Required existing items --------
            string craftItems = ASSET_ROOT + "/Items";
            string industrialItems = ASSET_ROOT + "/Industrial/Items";

            var stone        = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItems}/Item_Stone.asset");
            var ice          = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItems}/Item_Ice.asset");
            var coal         = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItems}/Item_Coal.asset");
            var uraniumOre   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItems}/Item_Uranium.asset");
            var clay         = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>($"{craftItems}/Item_Clay.asset");
            var woodLog      = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{craftItems}/Item_WoodLog.asset");
            var plank        = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{craftItems}/Item_WoodenPlank.asset");
            var ironIngot    = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{craftItems}/Item_IronIngot.asset");
            var copperIngot  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{craftItems}/Item_CopperIngot.asset");
            var steelIngot   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{craftItems}/Item_SteelIngot.asset");

            var ironPlate    = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{industrialItems}/Item_IronPlate.asset");
            var copperPlate  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{industrialItems}/Item_CopperPlate.asset");
            var steelPlate   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{industrialItems}/Item_SteelPlate.asset");
            var ironGear     = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{industrialItems}/Item_IronGear.asset");
            var copperWire   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{industrialItems}/Item_CopperWire.asset");
            var circuit      = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{industrialItems}/Item_Circuit.asset");
            var advCircuit   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{industrialItems}/Item_AdvCircuit.asset");
            var glass        = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{industrialItems}/Item_Glass.asset");
            var plastic      = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{industrialItems}/Item_Plastic.asset");

            var sciT1 = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ScienceItem>($"{craftItems}/Item_ScienceT1.asset");
            var sciT2 = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ScienceItem>($"{craftItems}/Item_ScienceT2.asset");
            var sciT3 = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ScienceItem>($"{craftItems}/Item_ScienceT3.asset");

            if (ironIngot == null || ironPlate == null || circuit == null || sciT1 == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine",
                    "Run Steps 4 + 7 + 10 first — Step 11 expects the ingots / plates / circuits / science packs to exist.", "OK");
                return;
            }

            var registry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>($"{ASSET_ROOT}/RecipeRegistry.asset");
            if (registry == null) { EditorUtility.DisplayDialog("Voxel Engine", "Run Step 4 first.", "OK"); return; }

            const string researchFolder = ASSET_ROOT + "/Research";
            const string nodesFolder    = researchFolder + "/Nodes";
            var tree = AssetDatabase.LoadAssetAtPath<VoxelEngine.Research.ResearchTree>(researchFolder + "/ResearchTree.asset");
            if (tree == null) { EditorUtility.DisplayDialog("Voxel Engine", "Run Step 7 first.", "OK"); return; }

            // ============================================================
            //  Generic helpers  (local to Step 11 to keep things readable)
            // ============================================================
            VoxelEngine.Items.ResourceItem MakeRes(string folder, string assetName, string display, string desc, Color tint,
                VoxelEngine.Items.ResourceCategory cat, string uiCategory, int maxStack = 999, float fuelSeconds = 0f)
            {
                string path = $"{folder}/{assetName}.asset";
                var r = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>(path);
                if (r == null) { r = ScriptableObject.CreateInstance<VoxelEngine.Items.ResourceItem>(); AssetDatabase.CreateAsset(r, path); }
                r.itemId = assetName.ToLower(); r.displayName = display; r.description = desc;
                r.iconTint = tint; r.maxStack = maxStack; r.massPerUnit = 1f;
                r.category = uiCategory; r.subcategory = cat; r.fuelSeconds = fuelSeconds;
                EditorUtility.SetDirty(r);
                return r;
            }

            VoxelEngine.Items.BlockItem MakeBlk(string folder, string assetName, string display, string desc, Color tint,
                GameObject prefab, string uiCategory, int hp = 200, int miningTier = 1, int maxStack = 50)
            {
                string path = $"{folder}/{assetName}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var b = ScriptableObject.CreateInstance<VoxelEngine.Items.BlockItem>();
                b.itemId = assetName.ToLower(); b.displayName = display; b.description = desc;
                b.iconTint = tint; b.maxStack = maxStack; b.massPerUnit = 4f;
                b.placedPrefab = prefab; b.gridSize = Vector3Int.one;
                b.allowStacking = false; b.blockHealth = hp; b.miningTier = miningTier;
                b.category = uiCategory;
                AssetDatabase.CreateAsset(b, path);
                return b;
            }

            GameObject MakePref(string folder, string name, Color color, Vector3 scale, System.Action<GameObject> configure)
            {
                string path = $"{folder}/{name}.prefab";
                var root = new GameObject(name);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Mesh";
                cube.transform.SetParent(root.transform, false);
                cube.transform.localScale = scale;
                cube.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(folder, $"Mat_{name}", color);
                configure?.Invoke(root);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                return prefab;
            }

            VoxelEngine.Crafting.RecipeDefinition AddRecipe(string folder, string assetName, string display,
                VoxelEngine.Items.ItemDefinition output, int outputCount,
                VoxelEngine.Crafting.StationTier station, bool unlockedByDefault,
                params (VoxelEngine.Items.ItemDefinition item, int n)[] inputs)
            {
                string path = $"{folder}/{assetName}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var r = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                r.displayName = display;
                r.outputItem  = output;
                r.outputCount = outputCount;
                r.requiredStation = station;
                r.craftSeconds    = station switch
                {
                    VoxelEngine.Crafting.StationTier.None          => 0f,
                    VoxelEngine.Crafting.StationTier.CraftingBench => 2f,
                    VoxelEngine.Crafting.StationTier.Furnace       => 0f,
                    VoxelEngine.Crafting.StationTier.Assembler     => 4f,
                    _ => 0f
                };
                r.unlockedByDefault = unlockedByDefault;
                int validCount = 0;
                foreach (var i in inputs) if (i.item != null && i.n > 0) validCount++;
                r.inputs = new VoxelEngine.Crafting.RecipeIngredient[validCount];
                int j = 0;
                foreach (var i in inputs)
                {
                    if (i.item == null || i.n <= 0) continue;
                    r.inputs[j++] = new VoxelEngine.Crafting.RecipeIngredient { item = i.item, count = i.n };
                }
                AssetDatabase.CreateAsset(r, path);
                if (!registry.recipes.Contains(r)) registry.recipes.Add(r);
                return r;
            }

            // ════════════════════════════════════════════════════════════
            //  FARMING — crops, seeds, foods, tools, blocks, recipes
            // ════════════════════════════════════════════════════════════

            // ── Tools (Hoe is just a ToolItem with "Other" type) ──
            string hoePath = $"{ITEMS}/Tool_Hoe.asset";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(hoePath) != null) AssetDatabase.DeleteAsset(hoePath);
            var hoe = ScriptableObject.CreateInstance<VoxelEngine.Items.ToolItem>();
            hoe.itemId        = "hoe";
            hoe.displayName   = "Hoe";
            hoe.description   = "Used in conjunction with Tilled Soil blocks. Future: right-click on dirt to till in-place.";
            hoe.toolType      = VoxelEngine.Items.ToolType.Other;
            hoe.miningTier    = 0;
            hoe.maxDurability = 250;
            hoe.strength      = 1f;
            hoe.fireRate      = 4f;
            hoe.brushRadius   = 0.5f;
            hoe.iconTint      = new Color(0.55f, 0.40f, 0.25f);
            hoe.maxStack      = 1;
            hoe.category      = "Tools";
            AssetDatabase.CreateAsset(hoe, hoePath);

            // ── Foods (each crop yields a raw harvest item; cooking yields better food) ──
            var foodWheatRaw = ScriptableObject.CreateInstance<VoxelEngine.Farming.FoodItem>();
            foodWheatRaw.itemId = "wheat";  foodWheatRaw.displayName = "Wheat";  foodWheatRaw.description = "Raw wheat grain. Restores a little hunger when eaten raw. Bake into Bread for 3x the nutrition.";
            foodWheatRaw.iconTint = new Color(0.92f, 0.85f, 0.45f); foodWheatRaw.maxStack = 99; foodWheatRaw.category = "Food";
            foodWheatRaw.hungerRestore = 5f; foodWheatRaw.healthRestore = 0f; foodWheatRaw.staminaRestore = 0f;
            AssetDatabase.CreateAsset(foodWheatRaw, $"{ITEMS}/Food_Wheat.asset");

            var foodCornRaw = ScriptableObject.CreateInstance<VoxelEngine.Farming.FoodItem>();
            foodCornRaw.itemId = "corn"; foodCornRaw.displayName = "Corn"; foodCornRaw.description = "Sweet corn. Edible raw — feeds 8 hunger.";
            foodCornRaw.iconTint = new Color(0.95f, 0.82f, 0.20f); foodCornRaw.maxStack = 99; foodCornRaw.category = "Food";
            foodCornRaw.hungerRestore = 8f;
            AssetDatabase.CreateAsset(foodCornRaw, $"{ITEMS}/Food_Corn.asset");

            var foodCarrotRaw = ScriptableObject.CreateInstance<VoxelEngine.Farming.FoodItem>();
            foodCarrotRaw.itemId = "carrot"; foodCarrotRaw.displayName = "Carrot"; foodCarrotRaw.description = "Crunchy root vegetable. Feeds 10 hunger.";
            foodCarrotRaw.iconTint = new Color(0.95f, 0.55f, 0.20f); foodCarrotRaw.maxStack = 99; foodCarrotRaw.category = "Food";
            foodCarrotRaw.hungerRestore = 10f;
            AssetDatabase.CreateAsset(foodCarrotRaw, $"{ITEMS}/Food_Carrot.asset");

            var foodBread = ScriptableObject.CreateInstance<VoxelEngine.Farming.FoodItem>();
            foodBread.itemId = "bread"; foodBread.displayName = "Bread"; foodBread.description = "Baked from 3 wheat. Restores 25 hunger.";
            foodBread.iconTint = new Color(0.85f, 0.65f, 0.30f); foodBread.maxStack = 99; foodBread.category = "Food";
            foodBread.hungerRestore = 25f;
            AssetDatabase.CreateAsset(foodBread, $"{ITEMS}/Food_Bread.asset");

            var foodStew = ScriptableObject.CreateInstance<VoxelEngine.Farming.FoodItem>();
            foodStew.itemId = "stew"; foodStew.displayName = "Vegetable Stew"; foodStew.description = "Hearty stew from carrots + corn. Restores 35 hunger + 10 health.";
            foodStew.iconTint = new Color(0.85f, 0.40f, 0.20f); foodStew.maxStack = 99; foodStew.category = "Food";
            foodStew.hungerRestore = 35f; foodStew.healthRestore = 10f;
            AssetDatabase.CreateAsset(foodStew, $"{ITEMS}/Food_Stew.asset");

            // ── Seeds ──
            VoxelEngine.Farming.SeedItem MakeSeed(string assetName, string display, Color tint, string desc)
            {
                var s = ScriptableObject.CreateInstance<VoxelEngine.Farming.SeedItem>();
                s.itemId = assetName.ToLower(); s.displayName = display; s.description = desc;
                s.iconTint = tint; s.maxStack = 99; s.category = "Seeds";
                AssetDatabase.CreateAsset(s, $"{ITEMS}/{assetName}.asset");
                return s;
            }
            var seedWheat  = MakeSeed("Seed_Wheat",  "Wheat Seeds",  new Color(0.88f, 0.80f, 0.45f),
                "Plant on a Tilled Soil block. Grows in ~2 minutes when irrigated.");
            var seedCorn   = MakeSeed("Seed_Corn",   "Corn Seeds",   new Color(0.92f, 0.78f, 0.20f),
                "Plant on a Tilled Soil block. Grows in ~3 minutes when irrigated.");
            var seedCarrot = MakeSeed("Seed_Carrot", "Carrot Seeds", new Color(0.95f, 0.55f, 0.25f),
                "Plant on a Tilled Soil block. Grows in ~2 minutes when irrigated.");

            // ── Crop definitions (data) ──
            VoxelEngine.Farming.CropDefinition MakeCrop(string assetName, string display, float growthTime,
                float foodValue, VoxelEngine.Items.ItemDefinition harvest, int harvestAmount,
                VoxelEngine.Farming.SeedItem seed, int seedReturn = 1, bool requiresWater = true)
            {
                var c = ScriptableObject.CreateInstance<VoxelEngine.Farming.CropDefinition>();
                c.cropName = display; c.growthTime = growthTime; c.growthStages = 4;
                c.requiresWater = requiresWater; c.irrigatedSpeedMultiplier = 2f; c.droughtToleranceSec = 30f;
                c.harvestItem = harvest; c.harvestAmount = harvestAmount;
                c.seedItem = seed; c.seedReturnAmount = seedReturn;
                c.foodValue = foodValue;
                c.stageScales = new[] { 0.2f, 0.4f, 0.7f, 1.0f };
                AssetDatabase.CreateAsset(c, $"{FARM_CROPS}/{assetName}.asset");
                return c;
            }
            var cropWheat  = MakeCrop("Crop_Wheat",  "Wheat",  120f, 5f, foodWheatRaw,  3, seedWheat,  1);
            var cropCorn   = MakeCrop("Crop_Corn",   "Corn",   180f, 8f, foodCornRaw,   2, seedCorn,   1);
            var cropCarrot = MakeCrop("Crop_Carrot", "Carrot", 120f, 10f, foodCarrotRaw, 2, seedCarrot, 1);

            // Hook each seed back to its crop.
            seedWheat.crop = cropWheat;   EditorUtility.SetDirty(seedWheat);
            seedCorn.crop  = cropCorn;    EditorUtility.SetDirty(seedCorn);
            seedCarrot.crop = cropCarrot; EditorUtility.SetDirty(seedCarrot);

            // ── Tilled Soil (FarmPlot) prefab ──
            var tilledPrefab = MakePref(FARM_PREFABS, "TilledSoil",
                new Color(0.30f, 0.20f, 0.12f), new Vector3(1f, 0.15f, 1f),
                root => { root.AddComponent<VoxelEngine.Farming.FarmPlot>(); });

            var blockTilled = MakeBlk(FARM_BLOCKS, "Block_TilledSoil", "Tilled Soil",
                "A patch of farmable dirt. RMB with a Seed item to plant. RMB again when fully grown to harvest.",
                new Color(0.30f, 0.20f, 0.12f), tilledPrefab, "Farming", hp: 50);

            // ── Sprinkler prefab ──
            var sprinklerPrefab = MakePref(FARM_PREFABS, "Sprinkler",
                new Color(0.40f, 0.65f, 0.85f), new Vector3(0.4f, 0.6f, 0.4f),
                root =>
                {
                    var pc = root.AddComponent<VoxelEngine.Power.PowerConsumer>();
                    pc.wattsPerSecond = 20f; pc.connectRadius = 1.6f;
                    var s = root.AddComponent<VoxelEngine.Farming.Sprinkler>();
                    s.radius = 8f; s.requiresWaterConnection = true; s.waterConsumption = 2f;
                });
            var blockSprinkler = MakeBlk(FARM_BLOCKS, "Block_Sprinkler", "Sprinkler",
                "Powered automatic irrigation. Irrigates every Tilled Soil within 8 m. ~20 W and needs water pipes connected.",
                new Color(0.40f, 0.65f, 0.85f), sprinklerPrefab, "Farming", hp: 200);

            // ── Harvester prefab ──
            var harvesterPrefab = MakePref(FARM_PREFABS, "Harvester",
                new Color(0.55f, 0.45f, 0.20f), new Vector3(1.2f, 1.0f, 1.2f),
                root =>
                {
                    var pc = root.AddComponent<VoxelEngine.Power.PowerConsumer>();
                    pc.wattsPerSecond = 40f; pc.connectRadius = 1.6f;
                    var h = root.AddComponent<VoxelEngine.Farming.Harvester>();
                    h.scanRadius = 8f; h.scanInterval = 2f; h.autoReplant = true; h.outputSlots = 6;
                });
            var blockHarvester = MakeBlk(FARM_BLOCKS, "Block_Harvester", "Harvester",
                "Automated crop picker. Scans 8 m radius for mature Tilled Soils, harvests them, optionally replants. Push outputs through item pipes. ~40 W.",
                new Color(0.55f, 0.45f, 0.20f), harvesterPrefab, "Farming", hp: 400);

            // ── Farming recipes ──
            AddRecipe(FARM_RECIPES, "Recipe_Hoe",              "Hoe",                hoe,            1, VoxelEngine.Crafting.StationTier.None,          unlockedByDefault: false, (woodLog, 2), (ironIngot, 1));
            AddRecipe(FARM_RECIPES, "Recipe_TilledSoil",       "Tilled Soil",        blockTilled,    1, VoxelEngine.Crafting.StationTier.None,          unlockedByDefault: false, (clay, 1));
            AddRecipe(FARM_RECIPES, "Recipe_Sprinkler",        "Sprinkler",          blockSprinkler, 1, VoxelEngine.Crafting.StationTier.CraftingBench, unlockedByDefault: false, (ironPlate, 2), (copperWire, 4));
            AddRecipe(FARM_RECIPES, "Recipe_Harvester",        "Harvester",          blockHarvester, 1, VoxelEngine.Crafting.StationTier.Assembler,     unlockedByDefault: false, (ironPlate, 4), (ironGear, 4), (circuit, 1));

            // Cooking recipes — happen at any Furnace (treated like a smelting recipe? simpler as bench recipes here).
            AddRecipe(FARM_RECIPES, "Recipe_Cook_Bread", "Bread",            foodBread, 1, VoxelEngine.Crafting.StationTier.Furnace,       unlockedByDefault: false, (foodWheatRaw, 3));
            AddRecipe(FARM_RECIPES, "Recipe_Cook_Stew",  "Vegetable Stew",   foodStew,  1, VoxelEngine.Crafting.StationTier.Furnace,       unlockedByDefault: false, (foodCarrotRaw, 2), (foodCornRaw, 1));

            // ════════════════════════════════════════════════════════════
            //  WRENCH — universal network connector tool
            //  LMB on a ConnectionAnchor selects, LMB on a second connects them.
            //  RMB disconnects ALL connections from the clicked anchor.
            //  Shift+RMB cycles a machine face port between None / Input / Output.
            //  Required for advanced wiring: data cables, power cables, port config.
            // ════════════════════════════════════════════════════════════
            string wrenchPath = $"{ITEMS}/Tool_Wrench.asset";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(wrenchPath) != null)
                AssetDatabase.DeleteAsset(wrenchPath);
            var wrench = ScriptableObject.CreateInstance<VoxelEngine.Networks.WrenchTool>();
            wrench.itemId        = "wrench";
            wrench.displayName   = "Wrench";
            wrench.description   = "Industrial multi-tool for wiring networks together.\n\n" +
                                   "• LMB anchor → select. LMB second anchor → connect.\n" +
                                   "• RMB anchor → disconnect ALL.\n" +
                                   "• Shift+LMB anchor → disconnect the specific link you clicked.\n" +
                                   "• Shift+RMB machine face → cycle port: None ▸ Input ▸ Output.";
            wrench.toolType      = VoxelEngine.Items.ToolType.Other;
            wrench.miningTier    = 0;
            wrench.maxDurability = 1000;
            wrench.strength      = 0f;
            wrench.fireRate      = 4f;
            wrench.brushRadius   = 0.0f;
            wrench.iconTint      = new Color(0.85f, 0.55f, 0.20f);
            wrench.maxStack      = 1;
            wrench.category      = "Tools";
            AssetDatabase.CreateAsset(wrench, wrenchPath);

            // Recipe: 2 iron plates + 1 iron gear, crafted at the Crafting Bench.
            AddRecipe(MISC_RECIPES, "Recipe_Wrench", "Wrench", wrench, 1,
                VoxelEngine.Crafting.StationTier.CraftingBench,
                unlockedByDefault: false, (ironPlate, 2), (ironGear, 1));

            // ════════════════════════════════════════════════════════════
            //  POWER BUSBAR — clean cable-organization conduit
            //  A fat copper bar with brass tap-sockets that behaves like a
            //  PowerCable but lets the player run one trunk along the
            //  ceiling/floor of a factory with many machine cables snapping
            //  into it. Inherits all topology / wrench / PortConfig logic.
            // ════════════════════════════════════════════════════════════
            var busbarPrefab = MakePref(MISC_PREFABS, "PowerBusbar",
                new Color(0.78f, 0.45f, 0.20f), new Vector3(0.4f, 0.4f, 0.4f),
                root =>
                {
                    var bus = root.AddComponent<VoxelEngine.Power.PowerBusbar>();
                    bus.busAxis       = VoxelEngine.Power.PowerBusbar.BusAxis.X;
                    bus.busLength     = 2f;
                    bus.busRadius     = 0.18f;
                    bus.socketRadius  = 0.24f;
                    bus.socketSpacing = 0.50f;
                    bus.barTint       = new Color(0.78f, 0.45f, 0.20f);
                    bus.socketTint    = new Color(0.95f, 0.80f, 0.35f);
                    bus.connectRadius = 3.5f;
                    bus.gridSize      = 1f;
                });
            var blockBusbar = MakeBlk(MISC_BLOCKS, "Block_PowerBusbar", "Power Busbar",
                "A copper power-distribution bar with brass tap-sockets. Acts as a " +
                "thicker, longer power cable — perfect for running a single trunk " +
                "along the ceiling of a factory and snapping every machine cable into it.",
                new Color(0.78f, 0.45f, 0.20f), busbarPrefab, "Logistics", hp: 250, maxStack: 16);
            AddRecipe(MISC_RECIPES, "Recipe_PowerBusbar", "Power Busbar", blockBusbar, 1,
                VoxelEngine.Crafting.StationTier.CraftingBench,
                unlockedByDefault: false, (copperPlate, 4), (ironPlate, 1));

            // ════════════════════════════════════════════════════════════
            //  ITEM PIPES
            // ════════════════════════════════════════════════════════════
            // Item pipes use the wire-style PipeVisualBuilder. Solid = brushed steel,
            // glass variant exposes a vivid "item stream" inner core so the player
            // can see stacks flowing through the network.
            // BuildCraft / Thermal-Expansion look: dark steel sleeve along the
            // run + bright AMBER/orange terminal end-blocks at every junction.
            GameObject MakeItemPipePrefab(string assetName, Color shell, Color accent, Color inner, bool glass)
            {
                string path = $"{MISC_PREFABS}/{assetName}.prefab";
                var root = new GameObject(assetName);
                var col  = root.AddComponent<BoxCollider>();
                col.size = new Vector3(0.55f, 0.55f, 0.55f);

                var p = root.AddComponent<VoxelEngine.Transport.ItemPipe>();
                p.connectRadius = 3f; p.bufferSize = 4; p.tickInterval = 0.5f;
                p.isGlass = glass;

                var vb = root.AddComponent<VoxelEngine.Networks.PipeVisualBuilder>();
                vb.shellTint        = shell;
                vb.accentTint       = accent;
                vb.innerMediumTint  = inner;
                vb.isGlass          = glass;
                vb.style            = VoxelEngine.Networks.PipeStyle.Sleeve;
                vb.gridSize         = 1f;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                return prefab;
            }
            // Solid item pipe — dark steel sleeve, amber/orange terminals.
            var pipeItemPrefab      = MakeItemPipePrefab("ItemPipe",
                new Color(0.18f, 0.18f, 0.20f),   // sleeve
                new Color(0.95f, 0.55f, 0.12f),   // amber terminals (BC look)
                new Color(0.95f, 0.75f, 0.25f),
                false);
            // Glass item pipe — frosted shell + warm amber stream visible inside.
            var pipeItemGlassPrefab = MakeItemPipePrefab("ItemPipe_Glass",
                new Color(0.85f, 0.92f, 1.00f),
                new Color(0.95f, 0.55f, 0.12f),
                new Color(0.95f, 0.65f, 0.20f),
                true);

            var blockItemPipe = MakeBlk(MISC_BLOCKS, "Block_ItemPipe", "Item Pipe",
                "Carries item stacks between machines and chests automatically. Tick every 0.5 s.",
                new Color(0.55f, 0.55f, 0.60f), pipeItemPrefab, "Logistics", hp: 100, maxStack: 99);
            var blockItemPipeGlass = MakeBlk(MISC_BLOCKS, "Block_ItemPipe_Glass", "Item Pipe (Glass)",
                "Translucent variant of the Item Pipe — exposes the items flowing through. Same throughput.",
                new Color(0.92f, 0.96f, 1.00f), pipeItemGlassPrefab, "Logistics", hp: 100, maxStack: 99);

            AddRecipe(MISC_RECIPES, "Recipe_ItemPipe",       "Item Pipe x4",         blockItemPipe,      4,
                VoxelEngine.Crafting.StationTier.CraftingBench, unlockedByDefault: false, (ironPlate, 1));
            AddRecipe(MISC_RECIPES, "Recipe_ItemPipe_Glass", "Item Pipe (Glass) x4", blockItemPipeGlass, 4,
                VoxelEngine.Crafting.StationTier.CraftingBench, unlockedByDefault: false, (ironPlate, 1), (glass, 1));

            // ════════════════════════════════════════════════════════════
            //  QUARRY + UPGRADES (Range / Speed / Efficiency)
            // ════════════════════════════════════════════════════════════
            var quarryPrefab = MakePref(MISC_PREFABS, "Quarry",
                new Color(0.35f, 0.35f, 0.40f), new Vector3(2f, 2.4f, 2f),
root =>
                {
                    var pc = root.AddComponent<VoxelEngine.Power.PowerConsumer>();
                    pc.wattsPerSecond = 500f; pc.connectRadius = 2f;
                    var portCfg = root.AddComponent<VoxelEngine.Transport.PortConfig>();
                    portCfg.ports = new VoxelEngine.Transport.PortConfig.FacePort[]
                    {
                        new() { face = VoxelEngine.Transport.CubeFace.PosX, direction = VoxelEngine.Transport.PortDirection.Output, networkType = VoxelEngine.Transport.PortNetworkType.Any,   enabled = true },
                        new() { face = VoxelEngine.Transport.CubeFace.NegX, direction = VoxelEngine.Transport.PortDirection.None,   networkType = VoxelEngine.Transport.PortNetworkType.Any,   enabled = true },
                        new() { face = VoxelEngine.Transport.CubeFace.PosY, direction = VoxelEngine.Transport.PortDirection.None,   networkType = VoxelEngine.Transport.PortNetworkType.Any,   enabled = true },
                        new() { face = VoxelEngine.Transport.CubeFace.NegY, direction = VoxelEngine.Transport.PortDirection.None,   networkType = VoxelEngine.Transport.PortNetworkType.Any,   enabled = true },
                        new() { face = VoxelEngine.Transport.CubeFace.PosZ, direction = VoxelEngine.Transport.PortDirection.None,   networkType = VoxelEngine.Transport.PortNetworkType.Any,   enabled = true },
                        new() { face = VoxelEngine.Transport.CubeFace.NegZ, direction = VoxelEngine.Transport.PortDirection.Input,  networkType = VoxelEngine.Transport.PortNetworkType.Power, enabled = true },
                    };
                    var q = root.AddComponent<VoxelEngine.Transport.Quarry>();
                    q.defaultSize = 16; q.baseMineInterval = 0.5f; q.quarryTier = 3;
                    q.forwardOffset = 2f; q.frameBuildInterval = 0.06f;
                    q.frameColor = new Color(0.18f, 0.19f, 0.22f);
                    q.outputSlots = 6;
                    q.EnsureUpgrades();
                });
            var blockQuarry = MakeBlk(MISC_BLOCKS, "Block_Quarry", "Quarry",
                "Automated industrial strip-miner. Builds a frame, then digs out the rectangle in front of itself (default 16×16) down to bedrock. Accepts Range, Speed & Efficiency upgrades. Powered (~500 W). Tier-3 mining.",
                new Color(0.35f, 0.35f, 0.40f), quarryPrefab, "Logistics", hp: 800, miningTier: 3);

            // ── Quarry Upgrade items ────────────────────────
            var upgRange = ScriptableObject.CreateInstance<VoxelEngine.Items.QuarryUpgradeItem>();
            upgRange.itemId = "upgrade_quarry_range"; upgRange.displayName = "Range Upgrade";
            upgRange.description = "Increases the Quarry's mining area by +1 per module (max 10)."; upgRange.maxStack = 1;
            upgRange.category = "Upgrades"; upgRange.massPerUnit = 2;
            upgRange.iconTint = new Color(0.88f, 0.72f, 0.22f);
            upgRange.upgradeKind = VoxelEngine.Items.QuarryUpgradeKind.Range;
            upgRange.maxInstalled = 10; upgRange.level = 1;
            upgRange.badgeTint = new Color(0.88f, 0.72f, 0.22f);
            AssetDatabase.CreateAsset(upgRange, MISC_ITEMS + "/Upgrade_QuarryRange.asset");

            var upgSpeed = ScriptableObject.CreateInstance<VoxelEngine.Items.QuarryUpgradeItem>();
            upgSpeed.itemId = "upgrade_quarry_speed"; upgSpeed.displayName = "Speed Upgrade";
            upgSpeed.description = "Speeds up the Quarry's mining by -0.04s per module (max 10)."; upgSpeed.maxStack = 1;
            upgSpeed.category = "Upgrades"; upgSpeed.massPerUnit = 2;
            upgSpeed.iconTint = new Color(0.12f, 0.60f, 0.68f);
            upgSpeed.upgradeKind = VoxelEngine.Items.QuarryUpgradeKind.Speed;
            upgSpeed.maxInstalled = 10; upgSpeed.level = 1;
            upgSpeed.badgeTint = new Color(0.12f, 0.60f, 0.68f);
            AssetDatabase.CreateAsset(upgSpeed, MISC_ITEMS + "/Upgrade_QuarrySpeed.asset");

            var upgEff = ScriptableObject.CreateInstance<VoxelEngine.Items.QuarryUpgradeItem>();
            upgEff.itemId = "upgrade_quarry_efficiency"; upgEff.displayName = "Efficiency Upgrade";
            upgEff.description = "Makes the Quarry use 35W less power per module (max 5)."; upgEff.maxStack = 1;
            upgEff.category = "Upgrades"; upgEff.massPerUnit = 2;
            upgEff.iconTint = new Color(0.58f, 0.30f, 0.84f);
            upgEff.upgradeKind = VoxelEngine.Items.QuarryUpgradeKind.Efficiency;
            upgEff.maxInstalled = 5; upgEff.level = 1;
            upgEff.badgeTint = new Color(0.58f, 0.30f, 0.84f);
            AssetDatabase.CreateAsset(upgEff, MISC_ITEMS + "/Upgrade_QuarryEfficiency.asset");

            // ── Recipes ─────────────────────────────────────
            AddRecipe(MISC_RECIPES, "Recipe_Quarry", "Quarry", blockQuarry, 1,
                VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false,
                (steelPlate, 10), (ironGear, 8), (circuit, 4), (copperWire, 6));

            AddRecipe(MISC_RECIPES, "Recipe_QuarryUpgradeRange", "Range Upgrade", upgRange, 1,
                VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false,
                (steelPlate, 4), (copperWire, 8), (ironGear, 4));

            AddRecipe(MISC_RECIPES, "Recipe_QuarryUpgradeSpeed", "Speed Upgrade", upgSpeed, 1,
                VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false,
                (steelPlate, 4), (circuit, 3), (ironGear, 2));

            AddRecipe(MISC_RECIPES, "Recipe_QuarryUpgradeEfficiency", "Efficiency Upgrade", upgEff, 1,
                VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false,
                (steelPlate, 6), (circuit, 6), (ironGear, 4), (copperWire, 2));

            // ════════════════════════════════════════════════════════════//  GAS — Electrolyser, Hydrogen Engine, Gas Tank, Gas Pipe
            // ════════════════════════════════════════════════════════════
            // (Gas itself lives on the GasTank/Network components, not as items —
            //  no item-defs needed for hydrogen/oxygen.)

            var electrolyserPrefab = MakePref(GAS_PREFABS, "Electrolyser",
                new Color(0.40f, 0.85f, 0.45f), new Vector3(1.4f, 1.6f, 1.4f),
                root =>
                {
                    var pc = root.AddComponent<VoxelEngine.Power.PowerConsumer>();
                    pc.wattsPerSecond = 600f; pc.connectRadius = 1.8f;
                    var e = root.AddComponent<VoxelEngine.Gas.Electrolyser>();
                    e.iceItem = ice; e.icePerCycle = 1; e.cycleTime = 10f;
                    e.hydrogenPerCycle = 20f; e.oxygenPerCycle = 10f;
                    e.bufferCapacity = 200f;
                });
            var blockElectrolyser = MakeBlk(GAS_BLOCKS, "Block_Electrolyser", "Electrolyser",
                "Splits Ice (H₂O) into Hydrogen and Oxygen with electricity. ~600 W. Outputs gases via Gas Pipes to a Hydrogen Engine or Gas Tank.",
                new Color(0.40f, 0.85f, 0.45f), electrolyserPrefab, "Gas", hp: 500);

            var hydrogenEnginePrefab = MakePref(GAS_PREFABS, "HydrogenEngine",
                new Color(0.20f, 0.85f, 0.35f), new Vector3(1.5f, 1.2f, 1.5f),
                root =>
                {
                    var g = root.AddComponent<VoxelEngine.Power.PowerGenerator>();
                    g.wattsPerSecond = 0f; g.connectRadius = 1.8f;
                    var he = root.AddComponent<VoxelEngine.Gas.HydrogenEngine>();
                    he.hydrogenPerSecond = 5f; he.wattsOutput = 2000f; he.bufferCapacity = 100f;
                });
            var blockHydrogenEngine = MakeBlk(GAS_BLOCKS, "Block_HydrogenEngine", "Hydrogen Engine",
                "Clean-burning fuel cell: 5 H₂/s → 2000 W electricity. Connect a Gas Pipe from an Electrolyser or Gas Tank.",
                new Color(0.20f, 0.85f, 0.35f), hydrogenEnginePrefab, "Gas", hp: 500);

            var gasTankPrefab = MakePref(GAS_PREFABS, "GasTank",
                new Color(0.55f, 0.70f, 0.85f), new Vector3(1.2f, 1.4f, 1.2f),
                root =>
                {
                    var t = root.AddComponent<VoxelEngine.Gas.GasTank>();
                    t.capacity = 1000f; t.acceptInput = true; t.allowOutput = true;
                });
            var blockGasTank = MakeBlk(GAS_BLOCKS, "Block_GasTank", "Gas Tank",
                "Buffers up to 1000 units of one gas type. Auto-locks to first gas inserted. Pipe-in from Electrolyser, pipe-out to Engine.",
                new Color(0.55f, 0.70f, 0.85f), gasTankPrefab, "Gas", hp: 350);

            // Gas pipes use the wire-style PipeVisualBuilder. Solid = industrial
            // yellow gas-line steel (real-world H₂ pipeline standard), glass shows
            // a soft cyan-green hydrogen tint flowing inside.
            // Gas pipes use the SLIM BRASS profile — clearly different
            // silhouette from the fatter copper water pipes.
            GameObject MakeGasPipePrefab(string assetName, Color shell, Color accent, Color inner, bool glass)
            {
                string path = $"{GAS_PREFABS}/{assetName}.prefab";
                var root = new GameObject(assetName);
                var col  = root.AddComponent<BoxCollider>();
                col.size = new Vector3(0.40f, 0.40f, 0.40f);

                // GasPipe has [RequireComponent(typeof(PlacedBlock))] — Unity
                // auto-adds PlacedBlock when GasPipe is added.
                var p = root.AddComponent<VoxelEngine.Gas.GasPipe>();
                p.maxPressure = 100f; p.connectRadius = 3f; p.isGlass = glass;

                var vb = root.AddComponent<VoxelEngine.Networks.PipeVisualBuilder>();
                vb.shellTint        = shell;
                vb.accentTint       = accent;
                vb.innerMediumTint  = inner;
                vb.isGlass          = glass;
                vb.style            = VoxelEngine.Networks.PipeStyle.Brass;
                vb.gridSize         = 1f;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                return prefab;
            }
            // Solid gas pipe — warm polished brass with bright gold collars.
            var gasPipePrefab      = MakeGasPipePrefab("GasPipe",
                new Color(0.78f, 0.62f, 0.20f),
                new Color(0.98f, 0.85f, 0.35f),
                new Color(0.45f, 0.95f, 0.75f), false);
            // Glass gas pipe — translucent gold shell + cyan-green hydrogen glow.
            var gasPipeGlassPrefab = MakeGasPipePrefab("GasPipe_Glass",
                new Color(0.95f, 0.88f, 0.65f),
                new Color(0.98f, 0.85f, 0.35f),
                new Color(0.40f, 0.95f, 0.70f), true);

            var blockGasPipe = MakeBlk(GAS_BLOCKS, "Block_GasPipe", "Gas Pipe",
                "Universal gas conduit. Auto-connects to neighbour pipes / tanks / engines / electrolysers within 3 m.",
                new Color(0.92f, 0.78f, 0.18f), gasPipePrefab, "Gas", hp: 80, maxStack: 99);
            var blockGasPipeGlass = MakeBlk(GAS_BLOCKS, "Block_GasPipe_Glass", "Gas Pipe (Glass)",
                "Translucent gas conduit. Same throughput as the solid pipe, but you can see the gas inside.",
                new Color(0.88f, 0.96f, 0.92f), gasPipeGlassPrefab, "Gas", hp: 80, maxStack: 99);

            AddRecipe(GAS_RECIPES, "Recipe_Electrolyser",    "Electrolyser",    blockElectrolyser,    1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (steelPlate, 4), (circuit, 3), (copperWire, 6), (glass, 2));
            AddRecipe(GAS_RECIPES, "Recipe_HydrogenEngine",  "Hydrogen Engine", blockHydrogenEngine,  1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (steelPlate, 4), (ironGear, 4), (circuit, 2));
            AddRecipe(GAS_RECIPES, "Recipe_GasTank",         "Gas Tank",        blockGasTank,         1, VoxelEngine.Crafting.StationTier.CraftingBench, unlockedByDefault: false, (steelPlate, 3), (glass, 1));
            AddRecipe(GAS_RECIPES, "Recipe_GasPipe",         "Gas Pipe x4",         blockGasPipe,         4, VoxelEngine.Crafting.StationTier.CraftingBench, unlockedByDefault: false, (copperPlate, 1));
            AddRecipe(GAS_RECIPES, "Recipe_GasPipe_Glass",   "Gas Pipe (Glass) x4", blockGasPipeGlass,    4, VoxelEngine.Crafting.StationTier.CraftingBench, unlockedByDefault: false, (copperPlate, 1), (glass, 1));

            // ════════════════════════════════════════════════════════════
            //  NUCLEAR — items + processor + reactors + waste processing
            // ════════════════════════════════════════════════════════════
            var enrichedRod = MakeRes(NUKE_ITEMS, "Item_EnrichedFuelRod", "Enriched Fuel Rod",
                "Highly-radioactive HEU fuel for the big Reactor Core. Burns for 600 s of nuclear heat.",
                new Color(0.40f, 0.95f, 0.40f), VoxelEngine.Items.ResourceCategory.Component, "Nuclear", maxStack: 16);
            var leuPellet = MakeRes(NUKE_ITEMS, "Item_LEUPellet", "LEU Pellet",
                "Low-Enriched Uranium pellet. Safer than HEU rods. Fuels the Portable Reactor for 300 s.",
                new Color(0.30f, 0.85f, 0.30f), VoxelEngine.Items.ResourceCategory.Component, "Nuclear", maxStack: 32);
            var depletedUran = MakeRes(NUKE_ITEMS, "Item_DepletedUranium", "Depleted Uranium",
                "Byproduct of enrichment. Reprocess it to extract more LEU.",
                new Color(0.55f, 0.55f, 0.30f), VoxelEngine.Items.ResourceCategory.Misc, "Nuclear", maxStack: 99);
            var spentRod = MakeRes(NUKE_ITEMS, "Item_SpentFuelRod", "Spent Fuel Rod",
                "Used reactor fuel. Highly radioactive. Reprocess in a Waste Reprocessor to recover uranium.",
                new Color(0.65f, 0.30f, 0.30f), VoxelEngine.Items.ResourceCategory.Misc, "Nuclear", maxStack: 16);
            var hlw = MakeRes(NUKE_ITEMS, "Item_HighLevelWaste", "High-Level Waste",
                "Final radioactive waste. Store deep underground. Cannot be reprocessed further.",
                new Color(0.85f, 0.20f, 0.50f), VoxelEngine.Items.ResourceCategory.Misc, "Nuclear", maxStack: 99);

            var processorPrefab = MakePref(NUKE_PREFABS, "UraniumProcessor",
                new Color(0.50f, 0.30f, 0.70f), new Vector3(1.6f, 1.8f, 1.6f),
                root =>
                {
                    var pc = root.AddComponent<VoxelEngine.Power.PowerConsumer>();
                    pc.wattsPerSecond = 800f; pc.connectRadius = 1.8f;
                    var u = root.AddComponent<VoxelEngine.Nuclear.UraniumProcessor>();
                    u.processTime = 30f; u.orePerBatch = 5;
                    u.uraniumOreItem = uraniumOre;
                    u.enrichedFuelRod = enrichedRod;  u.fuelRodOutput = 1;
                    u.leuPellet       = leuPellet;    u.leuPelletOutput = 2;
                    u.depletedUranium = depletedUran; u.wasteOutput = 1;
                });
            var blockProcessor = MakeBlk(NUKE_BLOCKS, "Block_UraniumProcessor", "Uranium Processor",
                "Centrifuge-style enrichment plant. 5× Uranium Ore → 1× Enriched Fuel Rod + 2× LEU Pellet + 1× Depleted Uranium. ~800 W.",
                new Color(0.50f, 0.30f, 0.70f), processorPrefab, "Nuclear", hp: 800);

            var reactorPrefab = MakePref(NUKE_PREFABS, "ReactorCore",
                new Color(0.40f, 0.65f, 0.40f), new Vector3(2.4f, 2.8f, 2.4f),
                root =>
                {
                    var r = root.AddComponent<VoxelEngine.Nuclear.ReactorCore>();
                    r.fuelRodItem = enrichedRod; r.spentFuelRod = spentRod;
                    r.fuelRodBurnTime = 600f; r.maxThermalKW = 1000f;
                    r.controlRodLevel = 0.5f; r.coreTemperature = 300f;
                    r.maxSafeTemperature = 800f; r.passiveCoolingKW = 50f;
                    r.waterTankCapacity = 500f; r.steamTankCapacity = 500f;
                    r.waterPerKW = 0.5f;
                });
            var blockReactor = MakeBlk(NUKE_BLOCKS, "Block_ReactorCore", "Reactor Core",
                "Big nuclear reactor. Burns Enriched Fuel Rods to boil water into steam. Pipe water IN (via fluid pipes), pipe steam OUT (via gas pipes) to a Steam Turbine. KEEP IT COOLED or it overheats.",
                new Color(0.40f, 0.65f, 0.40f), reactorPrefab, "Nuclear", hp: 1500, miningTier: 3);

            var turbinePrefab = MakePref(NUKE_PREFABS, "SteamTurbine",
                new Color(0.65f, 0.65f, 0.70f), new Vector3(2.4f, 1.6f, 2.4f),
                root =>
                {
                    var g = root.AddComponent<VoxelEngine.Power.PowerGenerator>();
                    g.wattsPerSecond = 0f; g.connectRadius = 2f;
                    var t = root.AddComponent<VoxelEngine.Nuclear.SteamTurbine>();
                    t.efficiency = 0.33f; t.maxSteamInputPerSec = 100f;
                    t.steamTankCapacity = 300f; t.waterTankCapacity = 300f;
                    t.maxWattsOutput = 330000f;
                });
            var blockTurbine = MakeBlk(NUKE_BLOCKS, "Block_SteamTurbine", "Steam Turbine",
                "Converts reactor steam into electricity. Up to 330 kW output. Exhaust condenses back to water — recycle into the Reactor.",
                new Color(0.65f, 0.65f, 0.70f), turbinePrefab, "Nuclear", hp: 1200);

            var portablePrefab = MakePref(NUKE_PREFABS, "PortableReactor",
                new Color(0.30f, 0.85f, 0.50f), new Vector3(1f, 1.4f, 1f),
                root =>
                {
                    var g = root.AddComponent<VoxelEngine.Power.PowerGenerator>();
                    g.wattsPerSecond = 0f; g.connectRadius = 1.6f;
                    var pr = root.AddComponent<VoxelEngine.Nuclear.PortableReactor>();
                    pr.leuPelletItem = leuPellet; pr.pelletBurnTime = 300f;
                    pr.wasteItem = hlw; pr.iceItem = ice; pr.icePerPellet = 2;
                    pr.wattsOutput = 800f;
                });
            var blockPortable = MakeBlk(NUKE_BLOCKS, "Block_PortableReactor", "Portable Reactor",
                "Small RTG-style reactor. Uses LEU Pellets + Ice coolant for direct 800 W output — no water pipes needed. Great for off-grid bases.",
                new Color(0.30f, 0.85f, 0.50f), portablePrefab, "Nuclear", hp: 800);

            var reproPrefab = MakePref(NUKE_PREFABS, "WasteReprocessor",
                new Color(0.85f, 0.20f, 0.50f), new Vector3(1.8f, 1.8f, 1.8f),
                root =>
                {
                    var pc = root.AddComponent<VoxelEngine.Power.PowerConsumer>();
                    pc.wattsPerSecond = 400f; pc.connectRadius = 1.8f;
                    var w = root.AddComponent<VoxelEngine.Nuclear.WasteReprocessor>();
                    w.processTime = 60f;
                    w.spentFuelRodItem = spentRod;
                    w.recoveredUranium = uraniumOre; w.recoveredAmount = 2;
                    w.highLevelWaste = hlw;
                    w.depletedUraniumItem = depletedUran;
                    w.outputLeuPellet = leuPellet; w.leuFromDepleted = 1;
                });
            var blockRepro = MakeBlk(NUKE_BLOCKS, "Block_WasteReprocessor", "Waste Reprocessor",
                "PUREX-style reprocessing. 1× Spent Fuel Rod → 2× Uranium Ore + 1× High-Level Waste. 1× Depleted Uranium → 1× LEU Pellet. Slow (60 s/batch) and power-hungry (~400 W).",
                new Color(0.85f, 0.20f, 0.50f), reproPrefab, "Nuclear", hp: 1000);

            // Nuclear recipes (require Adv Circuits + lots of steel)
            AddRecipe(NUKE_RECIPES, "Recipe_UraniumProcessor", "Uranium Processor", blockProcessor, 1,
                VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false,
                (steelPlate, 10), (ironGear, 6), (advCircuit, 4), (copperWire, 8));
            AddRecipe(NUKE_RECIPES, "Recipe_ReactorCore", "Reactor Core", blockReactor, 1,
                VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false,
                (steelPlate, 24), (ironGear, 12), (advCircuit, 8), (glass, 6), (copperWire, 16));
            AddRecipe(NUKE_RECIPES, "Recipe_SteamTurbine", "Steam Turbine", blockTurbine, 1,
                VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false,
                (steelPlate, 16), (ironGear, 12), (advCircuit, 4), (copperWire, 8));
            AddRecipe(NUKE_RECIPES, "Recipe_PortableReactor", "Portable Reactor", blockPortable, 1,
                VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false,
                (steelPlate, 6), (advCircuit, 3), (copperWire, 4));
            AddRecipe(NUKE_RECIPES, "Recipe_WasteReprocessor", "Waste Reprocessor", blockRepro, 1,
                VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false,
                (steelPlate, 12), (ironGear, 8), (advCircuit, 6), (glass, 4));

            // ════════════════════════════════════════════════════════════
            //  STORAGE NETWORK — Disks, Components, Rack, NAS, Terminals, etc.
            // ════════════════════════════════════════════════════════════

            // ── 5 Storage Disks (1K / 4K / 16K / 64K / 90K) ──
            VoxelEngine.Storage.StorageDisk MakeDisk(string assetName, string display,
                VoxelEngine.Storage.DiskTier tier, Color tint, string desc)
            {
                string path = $"{STORE_ITEMS}/{assetName}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var d = ScriptableObject.CreateInstance<VoxelEngine.Storage.StorageDisk>();
                d.itemId = assetName.ToLower(); d.displayName = display; d.description = desc;
                d.iconTint = tint; d.maxStack = 1; d.category = "Storage";
                d.tier = tier;
                AssetDatabase.CreateAsset(d, path);
                return d;
            }
            var disk1K  = MakeDisk("Disk_1K",  "Storage Disk (1K)",  VoxelEngine.Storage.DiskTier.Disk1K,  new Color(0.60f,0.85f,0.60f), "Holds up to 1,000 items. Insert into a Server Rack slot.");
            var disk4K  = MakeDisk("Disk_4K",  "Storage Disk (4K)",  VoxelEngine.Storage.DiskTier.Disk4K,  new Color(0.40f,0.85f,0.85f), "Holds up to 4,000 items.");
            var disk16K = MakeDisk("Disk_16K", "Storage Disk (16K)", VoxelEngine.Storage.DiskTier.Disk16K, new Color(0.40f,0.55f,0.95f), "Holds up to 16,000 items.");
            var disk64K = MakeDisk("Disk_64K", "Storage Disk (64K)", VoxelEngine.Storage.DiskTier.Disk64K, new Color(0.65f,0.30f,0.90f), "Holds up to 64,000 items.");
            var disk90K = MakeDisk("Disk_90K", "Storage Disk (90K)", VoxelEngine.Storage.DiskTier.Disk90K, new Color(0.95f,0.30f,0.65f), "Holds up to 90,000 items. End-game storage.");

            // ── Server components (RAM / CPU / PSU at multiple tiers) ──
            VoxelEngine.Storage.ServerComponent MakeComp(string assetName, string display,
                VoxelEngine.Storage.ComponentType type, float value, Color tint, string desc)
            {
                string path = $"{STORE_ITEMS}/{assetName}.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
                var c = ScriptableObject.CreateInstance<VoxelEngine.Storage.ServerComponent>();
                c.itemId = assetName.ToLower(); c.displayName = display; c.description = desc;
                c.iconTint = tint; c.maxStack = 4; c.category = "Storage";
                c.componentType = type; c.value = value;
                AssetDatabase.CreateAsset(c, path);
                return c;
            }
            var ram4   = MakeComp("RAM_4",   "RAM Module (4)",   VoxelEngine.Storage.ComponentType.RAM,  4f,  new Color(0.30f, 0.70f, 0.95f), "4 pattern slots per module. Insert into Server Rack RAM slot.");
            var ram16  = MakeComp("RAM_16",  "RAM Module (16)",  VoxelEngine.Storage.ComponentType.RAM, 16f,  new Color(0.20f, 0.55f, 0.95f), "16 pattern slots per module.");
            var cpu1   = MakeComp("CPU_1",   "CPU (1x)",         VoxelEngine.Storage.ComponentType.CPU, 1f,   new Color(0.50f, 0.50f, 0.55f), "1x crafting speed multiplier.");
            var cpu2   = MakeComp("CPU_2",   "CPU (2x)",         VoxelEngine.Storage.ComponentType.CPU, 2f,   new Color(0.45f, 0.45f, 0.55f), "2x crafting speed multiplier.");
            var cpu4   = MakeComp("CPU_4",   "CPU (4x)",         VoxelEngine.Storage.ComponentType.CPU, 4f,   new Color(0.35f, 0.35f, 0.60f), "4x crafting speed multiplier. End-game CPU.");
            var psu500 = MakeComp("PSU_500", "PSU (500 W)",      VoxelEngine.Storage.ComponentType.PSU, 500f, new Color(0.85f, 0.65f, 0.20f), "Powers up to 500 W of storage hardware.");
            var psu2k  = MakeComp("PSU_2K",  "PSU (2000 W)",     VoxelEngine.Storage.ComponentType.PSU, 2000f,new Color(0.85f, 0.45f, 0.20f), "Powers up to 2000 W of storage hardware.");

            // ── Server Rack ──
            var serverRackPrefab = MakePref(STORE_PREFABS, "ServerRack",
                new Color(0.15f, 0.18f, 0.22f), new Vector3(1.2f, 2.0f, 1.0f),
                root => { root.AddComponent<VoxelEngine.Power.PowerConsumer>().connectRadius = 1.6f;
                          root.AddComponent<VoxelEngine.Storage.ServerRack>();
                          root.AddComponent<VoxelEngine.Storage.AutoCrafter>(); });
            var blockServerRack = MakeBlk(STORE_BLOCKS, "Block_ServerRack", "Server Rack",
                "The brain of the storage network. 6 disk slots + 4 RAM + 1 CPU + 1 PSU. Hosts the auto-crafting engine too. RMB to open.",
                new Color(0.15f, 0.18f, 0.22f), serverRackPrefab, "Storage", hp: 800);

            // ── NAS ──
            var nasPrefab = MakePref(STORE_PREFABS, "NASBlock",
                new Color(0.18f, 0.22f, 0.30f), new Vector3(1.2f, 1.2f, 1.0f),
                root => { root.AddComponent<VoxelEngine.Storage.NASBlock>(); });
            var blockNAS = MakeBlk(STORE_BLOCKS, "Block_NASBlock", "NAS Block",
                "10 extra disk slots that extend the nearest Server Rack's storage. Place near a Rack.",
                new Color(0.18f, 0.22f, 0.30f), nasPrefab, "Storage", hp: 500);

            // ── Storage Terminal (wired) ──
            var termPrefab = MakePref(STORE_PREFABS, "StorageTerminal",
                new Color(0.30f, 0.55f, 0.85f), new Vector3(0.9f, 1.4f, 0.4f),
                root => { var t = root.AddComponent<VoxelEngine.Storage.StorageTerminal>();
                          t.isWireless = false; t.searchRadius = 10f; });
            var blockTerm = MakeBlk(STORE_BLOCKS, "Block_StorageTerminal", "Storage Terminal",
                "Access the storage network. Auto-finds a Server Rack within 10 m. RMB to open.",
                new Color(0.30f, 0.55f, 0.85f), termPrefab, "Storage", hp: 400);

            // ── Crafting Terminal ──
            var craftTermPrefab = MakePref(STORE_PREFABS, "CraftingTerminal",
                new Color(0.85f, 0.55f, 0.30f), new Vector3(0.9f, 1.4f, 0.4f),
                root => { root.AddComponent<VoxelEngine.Storage.CraftingTerminal>().searchRadius = 10f; });
            var blockCraftTerm = MakeBlk(STORE_BLOCKS, "Block_CraftingTerminal", "Crafting Terminal",
                "Queue auto-crafting jobs from the Server Rack. Shows current craft queue with timers.",
                new Color(0.85f, 0.55f, 0.30f), craftTermPrefab, "Storage", hp: 400);

            // ── Pattern Terminal ──
            var patTermPrefab = MakePref(STORE_PREFABS, "PatternTerminal",
                new Color(0.65f, 0.30f, 0.85f), new Vector3(0.9f, 1.4f, 0.4f),
                root => { root.AddComponent<VoxelEngine.Storage.PatternTerminal>().searchRadius = 10f; });
            var blockPatTerm = MakeBlk(STORE_BLOCKS, "Block_PatternTerminal", "Pattern Terminal",
                "Define new auto-crafting patterns. Each pattern occupies one RAM slot in the Server Rack.",
                new Color(0.65f, 0.30f, 0.85f), patTermPrefab, "Storage", hp: 400);

            // ── Importer ──
            var importerPrefab = MakePref(STORE_PREFABS, "StorageImporter",
                new Color(0.30f, 0.85f, 0.45f), new Vector3(0.6f, 0.6f, 0.6f),
                root => { var i = root.AddComponent<VoxelEngine.Storage.StorageImporter>();
                          i.baseInterval = 1f; i.baseStackSize = 1; i.maxSpeedUpgrades = 4; i.maxStackUpgrades = 1; });
            var blockImporter = MakeBlk(STORE_BLOCKS, "Block_StorageImporter", "Storage Importer",
                "Pulls items OUT of an adjacent chest INTO the storage network. Accepts Speed (×4) and Stack (×1) upgrades.",
                new Color(0.30f, 0.85f, 0.45f), importerPrefab, "Storage", hp: 200);

            // ── Exporter ──
            var exporterPrefab = MakePref(STORE_PREFABS, "StorageExporter",
                new Color(0.85f, 0.45f, 0.30f), new Vector3(0.6f, 0.6f, 0.6f),
                root => { var e = root.AddComponent<VoxelEngine.Storage.StorageExporter>();
                          e.baseInterval = 1f; e.baseStackSize = 1; e.maxSpeedUpgrades = 4; e.maxStackUpgrades = 1;
                          e.filterMode = VoxelEngine.Storage.FilterMode.Whitelist; });
            var blockExporter = MakeBlk(STORE_BLOCKS, "Block_StorageExporter", "Storage Exporter",
                "Pushes items OUT of the storage network INTO an adjacent chest. Whitelist filter by default. Speed (×4) and Stack (×1) upgrades.",
                new Color(0.85f, 0.45f, 0.30f), exporterPrefab, "Storage", hp: 200);

            // ── Powerstation (4-PSU expansion) ──
            var powerstationPrefab = MakePref(STORE_PREFABS, "Powerstation",
                new Color(0.85f, 0.65f, 0.20f), new Vector3(1.2f, 1.2f, 1.0f),
                root => { root.AddComponent<VoxelEngine.Storage.Powerstation>().searchRadius = 8f; });
            var blockPowerstation = MakeBlk(STORE_BLOCKS, "Block_Powerstation", "Powerstation",
                "Holds 4 extra PSU modules and feeds their wattage to the nearest Server Rack within 8 m.",
                new Color(0.85f, 0.65f, 0.20f), powerstationPrefab, "Storage", hp: 500);

            // ── Disk Manipulator ──
            var diskManipPrefab = MakePref(STORE_PREFABS, "DiskManipulator",
                new Color(0.55f, 0.55f, 0.55f), new Vector3(1.0f, 1.0f, 0.6f),
                root => { root.AddComponent<VoxelEngine.Storage.DiskManipulator>(); });
            var blockDiskManip = MakeBlk(STORE_BLOCKS, "Block_DiskManipulator", "Disk Manipulator",
                "Transfers items from a Source Disk to a Destination Disk. Use to upgrade disk tiers.",
                new Color(0.55f, 0.55f, 0.55f), diskManipPrefab, "Storage", hp: 300);

            // ── Storage recipes (gated by Logistics Network / Mass Storage / Crystalline Storage) ──
            // Components
            AddRecipe(STORE_RECIPES, "Recipe_RAM_4",   "RAM Module (4)",   ram4,   1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (circuit, 1), (copperWire, 4));
            AddRecipe(STORE_RECIPES, "Recipe_RAM_16",  "RAM Module (16)",  ram16,  1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (advCircuit, 1), (copperWire, 4));
            AddRecipe(STORE_RECIPES, "Recipe_CPU_1",   "CPU (1x)",         cpu1,   1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (circuit, 2), (copperPlate, 1));
            AddRecipe(STORE_RECIPES, "Recipe_CPU_2",   "CPU (2x)",         cpu2,   1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (circuit, 4), (copperPlate, 2));
            AddRecipe(STORE_RECIPES, "Recipe_CPU_4",   "CPU (4x)",         cpu4,   1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (advCircuit, 2), (steelPlate, 1));
            AddRecipe(STORE_RECIPES, "Recipe_PSU_500", "PSU (500 W)",      psu500, 1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (ironPlate, 2), (copperWire, 6));
            AddRecipe(STORE_RECIPES, "Recipe_PSU_2K",  "PSU (2000 W)",     psu2k,  1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (steelPlate, 2), (copperWire, 8), (advCircuit, 1));

            // Disks
            AddRecipe(STORE_RECIPES, "Recipe_StorageDisk1K",  "Storage Disk (1K)",  disk1K,  1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (ironPlate, 1), (circuit, 1));
            AddRecipe(STORE_RECIPES, "Recipe_StorageDisk4K",  "Storage Disk (4K)",  disk4K,  1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (ironPlate, 2), (circuit, 2));
            AddRecipe(STORE_RECIPES, "Recipe_StorageDisk16K", "Storage Disk (16K)", disk16K, 1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (steelPlate, 1), (circuit, 4));
            AddRecipe(STORE_RECIPES, "Recipe_StorageDisk64K", "Storage Disk (64K)", disk64K, 1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (steelPlate, 2), (advCircuit, 2), (plastic, 2));
            AddRecipe(STORE_RECIPES, "Recipe_StorageDisk90K", "Storage Disk (90K)", disk90K, 1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (steelPlate, 3), (advCircuit, 4), (plastic, 4));

            // Blocks
            AddRecipe(STORE_RECIPES, "Recipe_ServerRack",       "Server Rack",       blockServerRack,    1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (steelPlate, 6), (advCircuit, 4), (copperWire, 8));
            AddRecipe(STORE_RECIPES, "Recipe_StorageTerminal",  "Storage Terminal",  blockTerm,          1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (ironPlate, 3), (circuit, 2), (glass, 1));
            AddRecipe(STORE_RECIPES, "Recipe_CraftingTerminal", "Crafting Terminal", blockCraftTerm,     1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (ironPlate, 3), (circuit, 3), (glass, 1));
            AddRecipe(STORE_RECIPES, "Recipe_PatternTerminal",  "Pattern Terminal",  blockPatTerm,       1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (ironPlate, 3), (advCircuit, 1), (glass, 1));
            AddRecipe(STORE_RECIPES, "Recipe_StorageImporter",  "Storage Importer",  blockImporter,      1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (ironPlate, 2), (ironGear, 2), (circuit, 1));
            AddRecipe(STORE_RECIPES, "Recipe_StorageExporter",  "Storage Exporter",  blockExporter,      1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (ironPlate, 2), (ironGear, 2), (circuit, 1));
            AddRecipe(STORE_RECIPES, "Recipe_NASBlock",         "NAS Block",         blockNAS,           1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (steelPlate, 4), (circuit, 4), (copperWire, 6));
            AddRecipe(STORE_RECIPES, "Recipe_Powerstation",     "Powerstation",      blockPowerstation,  1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (steelPlate, 4), (copperWire, 8), (circuit, 2));
            AddRecipe(STORE_RECIPES, "Recipe_DiskManipulator",  "Disk Manipulator",  blockDiskManip,     1, VoxelEngine.Crafting.StationTier.Assembler, unlockedByDefault: false, (ironPlate, 4), (circuit, 2));

            // ════════════════════════════════════════════════════════════
            //  RESEARCH — wire newly created recipes into existing nodes
            //             + add a brand-new "Farming" node
            // ════════════════════════════════════════════════════════════

            // Helper: find existing research nodes.
            VoxelEngine.Research.ResearchNode FindNode(string id)
            {
                foreach (var nd in tree.nodes) if (nd != null && nd.nodeId == id) return nd;
                return null;
            }

            void AppendUnlocks(string nodeId, params VoxelEngine.Crafting.RecipeDefinition[] recipes)
            {
                var n = FindNode(nodeId);
                if (n == null) return;
                var list = new List<VoxelEngine.Crafting.RecipeDefinition>(n.unlocksRecipes);
                foreach (var r in recipes)
                    if (r != null && !list.Contains(r)) list.Add(r);
                n.unlocksRecipes = list.ToArray();
                EditorUtility.SetDirty(n);
            }

            // Get every recipe we just made so we can rewire nodes.
            VoxelEngine.Crafting.RecipeDefinition RGet(string n) => FindRecipeByName(n);

            // Logistics Network: server rack + terminal + 1K/4K disks + importer/exporter.
            AppendUnlocks("res_logistics_network",
                RGet("Recipe_ServerRack"), RGet("Recipe_StorageTerminal"),
                RGet("Recipe_StorageDisk1K"), RGet("Recipe_StorageDisk4K"),
                RGet("Recipe_StorageImporter"), RGet("Recipe_StorageExporter"),
                RGet("Recipe_PatternTerminal"), RGet("Recipe_CraftingTerminal"),
                RGet("Recipe_RAM_4"), RGet("Recipe_CPU_1"), RGet("Recipe_PSU_500"),
                RGet("Recipe_DiskManipulator"));

            // Mass Storage: 16K + NAS + bigger PSU/CPU.
            AppendUnlocks("res_mass_storage",
                RGet("Recipe_StorageDisk16K"), RGet("Recipe_NASBlock"),
                RGet("Recipe_RAM_16"), RGet("Recipe_CPU_2"), RGet("Recipe_PSU_2K"),
                RGet("Recipe_Powerstation"));

            // Crystalline Storage: 64K + 90K + best CPU.
            AppendUnlocks("res_crystalline_storage",
                RGet("Recipe_StorageDisk64K"), RGet("Recipe_StorageDisk90K"),
                RGet("Recipe_CPU_4"));

            // Item Logistics: item pipe.
            AppendUnlocks("res_item_logistics",
                RGet("Recipe_ItemPipe"), RGet("Recipe_ItemPipe_Glass"),
                RGet("Recipe_Wrench"), RGet("Recipe_PowerBusbar"));

            // Quarrying.
            AppendUnlocks("res_quarrying", RGet("Recipe_Quarry"),
                RGet("Recipe_QuarryUpgradeRange"), RGet("Recipe_QuarryUpgradeSpeed"),
                RGet("Recipe_QuarryUpgradeEfficiency"));

            // Gas Processing.
            AppendUnlocks("res_gas_processing",
                RGet("Recipe_Electrolyser"), RGet("Recipe_HydrogenEngine"),
                RGet("Recipe_GasTank"), RGet("Recipe_GasPipe"), RGet("Recipe_GasPipe_Glass"));

            // Nuclear Fission.
            AppendUnlocks("res_nuclear_fission",
                RGet("Recipe_UraniumProcessor"), RGet("Recipe_ReactorCore"),
                RGet("Recipe_SteamTurbine"), RGet("Recipe_PortableReactor"),
                RGet("Recipe_WasteReprocessor"));

            // ── Brand-new "Farming" research node (Tier-2, prereq: Stone Working) ──
            var nStoneWorking = FindNode("res_stone_working");
            VoxelEngine.Research.ResearchNode farmingNode;
            {
                string path = $"{nodesFolder}/res_farming.asset";
                farmingNode = AssetDatabase.LoadAssetAtPath<VoxelEngine.Research.ResearchNode>(path);
                if (farmingNode == null)
                {
                    farmingNode = ScriptableObject.CreateInstance<VoxelEngine.Research.ResearchNode>();
                    AssetDatabase.CreateAsset(farmingNode, path);
                }
                farmingNode.nodeId       = "res_farming";
                farmingNode.displayName  = "Farming";
                farmingNode.description  = "Cultivate the land. Unlocks the Hoe, Tilled Soil, Wheat / Corn / Carrot seeds, " +
                                           "Sprinkler, Harvester, and cooking recipes for Bread + Vegetable Stew.";
                farmingNode.category     = VoxelEngine.Research.ResearchCategory.Environment;
                farmingNode.subCategory  = VoxelEngine.Research.ResearchSubCategory.Production;
                farmingNode.tier         = 2;
                farmingNode.column       = 5;
                farmingNode.iconTint     = new Color(0.40f, 0.80f, 0.30f);
                farmingNode.researchSeconds = 40f;
                farmingNode.cost = new VoxelEngine.Research.ResearchNode.ScienceCost[]
                {
                    new VoxelEngine.Research.ResearchNode.ScienceCost { pack = sciT1, count = 12 },
                    new VoxelEngine.Research.ResearchNode.ScienceCost { pack = sciT2, count = 5 },
                };
                var farmUnlocks = new List<VoxelEngine.Crafting.RecipeDefinition>();
                foreach (var name in new[] { "Recipe_Hoe", "Recipe_TilledSoil", "Recipe_Sprinkler",
                                             "Recipe_Harvester", "Recipe_Cook_Bread", "Recipe_Cook_Stew" })
                {
                    var r = RGet(name);
                    if (r != null) farmUnlocks.Add(r);
                }
                farmingNode.unlocksRecipes = farmUnlocks.ToArray();
                farmingNode.prerequisites  = nStoneWorking != null
                    ? new[] { nStoneWorking }
                    : new VoxelEngine.Research.ResearchNode[0];
                farmingNode.upgradeKind = VoxelEngine.Research.PlayerUpgradeKind.None;
                farmingNode.maxRanks    = 1;
                EditorUtility.SetDirty(farmingNode);
                if (!tree.nodes.Contains(farmingNode)) tree.nodes.Add(farmingNode);
            }

            // ────────────────────────────────────────────────────────────
            // Merged content generators (previously separate Tools menu items).
            // Farming crops/recipes + Nuclear fission content are now produced
            // as part of Step 11 so a single click builds the full survival tier.
            // ────────────────────────────────────────────────────────────
            VoxelEngine.Farming.FarmingSetupGuide.CreateAll();
            VoxelEngine.Nuclear.NuclearSetupGuide.CreateAll();

            // ────────────────────────────────────────────────────────────
            // Save everything.
            // ────────────────────────────────────────────────────────────
            EditorUtility.SetDirty(tree);
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Step 11",
                "Survival + Industrial Logistics content built!\n\n" +
                "FARMING\n" +
                "  • Hoe, Tilled Soil block, Sprinkler, Harvester\n" +
                "  • Wheat / Corn / Carrot seeds + raw foods + Bread + Vegetable Stew\n" +
                "  • NEW research node: Farming (Tier 2)\n\n" +
                "STORAGE NETWORK\n" +
                "  • 5 disk tiers (1K / 4K / 16K / 64K / 90K)\n" +
                "  • RAM ×2 / CPU ×3 / PSU ×2 component tiers\n" +
                "  • Server Rack, NAS, Storage / Crafting / Pattern Terminals\n" +
                "  • Importer, Exporter, Powerstation, Disk Manipulator\n\n" +
                "OTHER\n" +
                "  • Wrench (universal network connector tool)\n" +
                "  • Power Busbar (clean cable-organization conduit)\n" +
                "  • Item Pipes (Solid + Glass) — BuildCraft sleeve style\n" +
                "  • Quarry + Upgrades (Range / Speed / Efficiency)\n" +
                "  • Electrolyser, Hydrogen Engine, Gas Tank, Gas Pipe (Solid + Glass)\n" +
                "  • Enriched Fuel Rod, LEU Pellet, Depleted Uranium, Spent Fuel Rod, High-Level Waste\n" +
                "  • Uranium Processor, Reactor Core, Steam Turbine, Portable Reactor, Waste Reprocessor\n\n" +
                "All recipes wired into the existing research nodes (Logistics Network, Mass Storage,\n" +
                "Crystalline Storage, Wireless Access, Item Logistics, Quarrying, Gas Processing, Nuclear Fission).\n\n" +
                "Open the Research UI in-game (Y) — everything actually unlocks real recipes now!",
                "OK");
        }



        // ===== Procedural scatter-prefab helpers =====
        private static Material MakeColoredMat(string folder, string name, Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = name };
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(m, $"{folder}/{name}.mat");
            return m;
        }

        private static GameObject MakeTreePrefab(string folder, string name, Material trunkMat, Material leafMat,
                                                 float trunkHeight, float leafSize, bool conifer)
        {
            var root = new GameObject(name);
            // Trunk
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localScale    = new Vector3(0.4f, trunkHeight, 0.4f);
            trunk.transform.localPosition = new Vector3(0, trunkHeight, 0);
            trunk.GetComponent<Renderer>().sharedMaterial = trunkMat;
            Object.DestroyImmediate(trunk.GetComponent<Collider>());

            // Foliage
            if (conifer)
            {
                for (int i = 0; i < 3; i++)
                {
                    var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    cone.name = $"Leaves_{i}";
                    cone.transform.SetParent(root.transform, false);
                    float s = leafSize * (1.4f - i * 0.35f);
                    cone.transform.localScale    = new Vector3(s, 0.6f, s);
                    cone.transform.localPosition = new Vector3(0, trunkHeight * 1.6f + i * 1.0f, 0);
                    cone.GetComponent<Renderer>().sharedMaterial = leafMat;
                    Object.DestroyImmediate(cone.GetComponent<Collider>());
                }
            }
            else
            {
                var leaves = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                leaves.name = "Leaves";
                leaves.transform.SetParent(root.transform, false);
                leaves.transform.localScale    = Vector3.one * leafSize * 2.4f;
                leaves.transform.localPosition = new Vector3(0, trunkHeight * 2.0f + 0.6f, 0);
                leaves.GetComponent<Renderer>().sharedMaterial = leafMat;
                Object.DestroyImmediate(leaves.GetComponent<Collider>());
            }

            string path = $"{folder}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject MakeRockPrefab(string folder, string name, Material mat, float size)
        {
            var root = new GameObject(name);
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = "Rock";
            rock.transform.SetParent(root.transform, false);
            rock.transform.localScale    = new Vector3(size * 1.0f, size * 0.7f, size * 1.2f);
            rock.transform.localRotation = Quaternion.Euler(Random.Range(-15f,15f), Random.Range(0f,360f), Random.Range(-15f,15f));
            rock.transform.localPosition = new Vector3(0, size * 0.3f, 0);
            rock.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(rock.GetComponent<Collider>());

            string path = $"{folder}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject MakeCactusPrefab(string folder, string name, Material mat)
        {
            var root = new GameObject(name);
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "Stem";
            stem.transform.SetParent(root.transform, false);
            stem.transform.localScale    = new Vector3(0.6f, 1.6f, 0.6f);
            stem.transform.localPosition = new Vector3(0, 1.6f, 0);
            stem.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(stem.GetComponent<Collider>());

            for (int i = 0; i < 2; i++)
            {
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                arm.name = $"Arm_{i}";
                arm.transform.SetParent(root.transform, false);
                arm.transform.localScale    = new Vector3(0.4f, 0.6f, 0.4f);
                arm.transform.localPosition = new Vector3(i == 0 ? 0.5f : -0.5f, 2.0f, 0);
                arm.transform.localRotation = Quaternion.Euler(0, 0, i == 0 ? -25f : 25f);
                arm.GetComponent<Renderer>().sharedMaterial = mat;
                Object.DestroyImmediate(arm.GetComponent<Collider>());
            }

            string path = $"{folder}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private void BuildGridSystemContent()
        {
            const string GRID_ROOT = ASSET_ROOT + "/GridSystem";
            const string ITEMS     = GRID_ROOT + "/Items";
            const string PREFABS   = GRID_ROOT + "/Prefabs";
            const string RECIPES   = GRID_ROOT + "/Recipes";
            const string NODES     = ASSET_ROOT + "/Research/Nodes";

            foreach (var f in new[] { GRID_ROOT, ITEMS, PREFABS, RECIPES }) EnsureFolder(f);

            // -- Dependencies --
            string craftItems = ASSET_ROOT + "/Items";
            string indItems   = ASSET_ROOT + "/Industrial/Items";
            var steelPlate  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{indItems}/Item_SteelPlate.asset");
            var ironPlate   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{indItems}/Item_IronPlate.asset");
            var circuit     = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{indItems}/Item_Circuit.asset");
            var advCircuit  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{indItems}/Item_AdvCircuit.asset");
            var copperWire  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{indItems}/Item_CopperWire.asset");
            var glass       = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ResourceItem>($"{indItems}/Item_Glass.asset");
            var sciT2 = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ScienceItem>($"{craftItems}/Item_ScienceT2.asset");
            var sciT3 = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ScienceItem>($"{craftItems}/Item_ScienceT3.asset");

            if (steelPlate == null || circuit == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine", "Run Step 10 (Industrial Content) first.", "OK");
                return;
            }

            var registry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>($"{ASSET_ROOT}/RecipeRegistry.asset");
            var tree = AssetDatabase.LoadAssetAtPath<VoxelEngine.Research.ResearchTree>($"{ASSET_ROOT}/Research/ResearchTree.asset");

            // -- Helpers --
            VoxelEngine.GridSystem.GridBlockItem MakeGItem(string assetName, string display, Color tint, 
                GameObject prefab, VoxelEngine.GridSystem.GridSize size, float mass, float hp)
            {
                string path = $"{ITEMS}/{assetName}.asset";
                var b = AssetDatabase.LoadAssetAtPath<VoxelEngine.GridSystem.GridBlockItem>(path);
                if (b == null) { b = ScriptableObject.CreateInstance<VoxelEngine.GridSystem.GridBlockItem>(); AssetDatabase.CreateAsset(b, path); }
                b.itemId = assetName.ToLower(); b.displayName = display; b.iconTint = tint;
                b.maxStack = 20; b.gridSize = size; b.blockPrefab = prefab;
                b.blockMass = mass; b.blockHP = hp; b.category = "Grid Blocks";
                EditorUtility.SetDirty(b);
                return b;
            }

            // NOTE: the legacy `scale` arg is now ignored — every block model is
            // authored to fill its cell by GridBlockMeshBuilder (so blocks tile with
            // no gaps and the ghost/collider always match). Style + size are inferred
            // from the prefab name (e.g. "Cockpit_Small", "Thruster_Large").
            GameObject MakeGPref<T>(string name, Color color, Vector3 scale, System.Action<T> config = null) where T : VoxelEngine.GridSystem.GridBlock
            {
                string path = $"{PREFABS}/{name}.prefab";
                var root = new GameObject(name);

                var size  = name.Contains("Small") ? VoxelEngine.GridSystem.GridSize.Small : VoxelEngine.GridSystem.GridSize.Large;
                var style = GridStyleFor(name);

                // Persist every generated material as an asset so the saved prefab keeps
                // valid references (otherwise the runtime-only materials are dropped and
                // the blocks render magenta).
                EnsureFolder(PREFABS + "/Mats");
                int matIdx = 0;
                VoxelEngine.GridSystem.GridBlockMeshBuilder.MaterialPersister = (mat, _) =>
                {
                    string mp = $"{PREFABS}/Mats/{name}_{matIdx++}.mat";
                    if (AssetDatabase.LoadAssetAtPath<Material>(mp) != null) AssetDatabase.DeleteAsset(mp);
                    AssetDatabase.CreateAsset(mat, mp);
                    return AssetDatabase.LoadAssetAtPath<Material>(mp);
                };
                VoxelEngine.GridSystem.GridBlockMeshBuilder.Build(root, style, size, color);
                VoxelEngine.GridSystem.GridBlockMeshBuilder.MaterialPersister = null;

                // Cell-sized box collider on the root so placement + ghost line up exactly.
                float cs = VoxelEngine.GridSystem.GridSizeExt.CellSize(size);
                var box = root.AddComponent<BoxCollider>();
                box.size = new Vector3(cs, cs, cs);

                var b = root.AddComponent<T>();
                config?.Invoke(b);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                return prefab;
            }

            // -- Recipes --
            var recipes = new System.Collections.Generic.List<VoxelEngine.Crafting.RecipeDefinition>();
            VoxelEngine.Crafting.RecipeDefinition AddGRecipe(string name, string display, VoxelEngine.Items.ItemDefinition output, params (VoxelEngine.Items.ItemDefinition item, int n)[] inputs)
            {
                if (output == null) return null;
                var r = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                r.displayName = display; r.outputItem = output; r.outputCount = 1; r.requiredStation = VoxelEngine.Crafting.StationTier.Assembler; r.craftSeconds = 4f; r.unlockedByDefault = false;
                var valid = new System.Collections.Generic.List<VoxelEngine.Crafting.RecipeIngredient>();
                foreach (var (item, n) in inputs) if (item != null) valid.Add(new VoxelEngine.Crafting.RecipeIngredient { item = item, count = n });
                r.inputs = valid.ToArray();
                AssetDatabase.CreateAsset(r, $"{RECIPES}/{name}.asset");
                if (registry != null && !registry.recipes.Contains(r)) registry.recipes.Add(r);
                recipes.Add(r); return r;
            }

            // -- 1) Cockpits --
            var cockSmallPref = MakeGPref<VoxelEngine.GridSystem.GridCockpit>("Cockpit_Small", new Color(0.2f, 0.4f, 0.8f), new Vector3(0.8f, 0.8f, 1.2f));
            var cockLargePref = MakeGPref<VoxelEngine.GridSystem.GridCockpit>("Cockpit_Large", new Color(0.2f, 0.4f, 0.8f), new Vector3(2f, 2f, 3f));
            var itemCockSmall = MakeGItem("GItem_CockpitSmall", "Small Cockpit", Color.white, cockSmallPref, VoxelEngine.GridSystem.GridSize.Small, 200, 500);
            var itemCockLarge = MakeGItem("GItem_CockpitLarge", "Large Cockpit", Color.white, cockLargePref, VoxelEngine.GridSystem.GridSize.Large, 1500, 2000);
            AddGRecipe("Recipe_GCockpitSmall", "Small Cockpit", itemCockSmall, (steelPlate, 4), (circuit, 2), (glass, 2));
            AddGRecipe("Recipe_GCockpitLarge", "Large Cockpit", itemCockLarge, (steelPlate, 10), (circuit, 6), (glass, 6));

            // -- 2) Thrusters (Atmospheric / Ion / Hydrogen × Small & Large) --
            void MakeThruster(string id, string display, VoxelEngine.GridSystem.GridSize sz, Color col,
                VoxelEngine.GridSystem.ThrusterType type, float thrust, float power, float h2,
                params (VoxelEngine.Items.ItemDefinition item, int n)[] cost)
            {
                var pref = MakeGPref<VoxelEngine.GridSystem.GridThruster>(id, col, Vector3.one,
                    t => { t.thrusterType = type; t.maxThrustN = thrust; t.powerAtMaxThrust = power; t.hydrogenPerSecond = h2; });
                float mass = sz == VoxelEngine.GridSystem.GridSize.Small ? 50 : 800;
                float hp   = sz == VoxelEngine.GridSystem.GridSize.Small ? 200 : 1000;
                var item = MakeGItem("GItem_" + id, display, Color.white, pref, sz, mass, hp);
                AddGRecipe("Recipe_G" + id, display, item, cost);
            }
            var TAtmo = VoxelEngine.GridSystem.ThrusterType.Atmospheric;
            var TIon  = VoxelEngine.GridSystem.ThrusterType.Ion;
            var THyd  = VoxelEngine.GridSystem.ThrusterType.Hydrogen;
            var SzS = VoxelEngine.GridSystem.GridSize.Small;
            var SzL = VoxelEngine.GridSystem.GridSize.Large;

            // Higher thrust, lower power draw for better flight feel.
            MakeThruster("AtmoThruster_Small", "Small Atmospheric Thruster", SzS, new Color(0.85f,0.45f,0.15f), TAtmo, 60000f,  200f, 0f, (steelPlate, 2), (copperWire, 4));
            MakeThruster("AtmoThruster_Large", "Large Atmospheric Thruster", SzL, new Color(0.85f,0.45f,0.15f), TAtmo, 800000f, 2500f, 0f, (steelPlate, 12), (copperWire, 16), (circuit, 2));
            MakeThruster("IonThruster_Small",  "Small Ion Thruster",         SzS, new Color(0.5f,0.3f,0.95f),  TIon,  45000f,  300f, 0f, (steelPlate, 2), (circuit, 3));
            MakeThruster("IonThruster_Large",  "Large Ion Thruster",         SzL, new Color(0.5f,0.3f,0.95f),  TIon,  600000f, 3500f, 0f, (steelPlate, 12), (circuit, 8), (copperWire, 8));
            MakeThruster("HydroThruster_Small","Small Hydrogen Thruster",    SzS, new Color(0.2f,0.55f,0.95f), THyd,  80000f,  0f,   3f, (steelPlate, 2), (copperWire, 4));
            MakeThruster("HydroThruster_Large","Large Hydrogen Thruster",    SzL, new Color(0.2f,0.55f,0.95f), THyd,  1000000f,0f,   18f,(steelPlate, 12), (copperWire, 16), (circuit, 2));

            // Gyroscope — provides rotational control (yaw/pitch/roll).
            var gyroPref = MakeGPref<VoxelEngine.GridSystem.GridGyroscope>("Gyroscope_Large", new Color(0.7f, 0.7f, 0.75f), Vector3.one,
                g => { g.torquePower = 80000f; });
            var itemGyro = MakeGItem("GItem_Gyroscope", "Gyroscope", Color.white, gyroPref, SzL, 300, 500);
            AddGRecipe("Recipe_GGyroscope", "Gyroscope", itemGyro, (steelPlate, 6), (circuit, 4), (copperWire, 6));
            var gyroPrefS = MakeGPref<VoxelEngine.GridSystem.GridGyroscope>("Gyroscope_Small", new Color(0.7f, 0.7f, 0.75f), Vector3.one,
                g => { g.torquePower = 6000f; });
            var itemGyroS = MakeGItem("GItem_GyroscopeSmall", "Small Gyroscope", Color.white, gyroPrefS, SzS, 30, 150);
            AddGRecipe("Recipe_GGyroscopeSmall", "Small Gyroscope", itemGyroS, (steelPlate, 1), (circuit, 1), (copperWire, 2));

            // -- 3) Energy --
            var batSmallPref = MakeGPref<VoxelEngine.GridSystem.GridBattery>("Battery_Small", new Color(0.2f, 0.7f, 0.3f), new Vector3(0.5f, 0.5f, 0.5f),
                b => { b.capacityWh = 1000000f; b.maxDischargeRate = 5000f; });
            var batLargePref = MakeGPref<VoxelEngine.GridSystem.GridBattery>("Battery_Large", new Color(0.2f, 0.7f, 0.3f), new Vector3(2f, 2f, 2f),
                b => { b.capacityWh = 25000000f; b.maxDischargeRate = 60000f; });
            var itemBatSmall = MakeGItem("GItem_BatterySmall", "Small Battery", Color.white, batSmallPref, VoxelEngine.GridSystem.GridSize.Small, 100, 300);
            var itemBatLarge = MakeGItem("GItem_BatteryLarge", "Large Battery", Color.white, batLargePref, VoxelEngine.GridSystem.GridSize.Large, 800, 600);
            AddGRecipe("Recipe_GBatSmall", "Small Battery", itemBatSmall, (ironPlate, 2), (copperWire, 8));
            AddGRecipe("Recipe_GBatLarge", "Large Battery", itemBatLarge, (ironPlate, 10), (copperWire, 24), (circuit, 4));

            // -- 4) Structure (Armor) --
            var armorSmallPref = MakeGPref<VoxelEngine.GridSystem.GridArmorBlock>("Armor_Small", new Color(0.35f, 0.35f, 0.4f), new Vector3(0.5f, 0.5f, 0.5f));
            var armorLargePref = MakeGPref<VoxelEngine.GridSystem.GridArmorBlock>("Armor_Large", new Color(0.35f, 0.35f, 0.4f), new Vector3(2.5f, 2.5f, 2.5f));
            var itemArmorSmall = MakeGItem("GItem_ArmorSmall", "Small Armor Block", Color.white, armorSmallPref, VoxelEngine.GridSystem.GridSize.Small, 250, 800);
            var itemArmorLarge = MakeGItem("GItem_ArmorLarge", "Large Armor Block", Color.white, armorLargePref, VoxelEngine.GridSystem.GridSize.Large, 1500, 2400);
            AddGRecipe("Recipe_GArmorSmall", "Small Armor Block", itemArmorSmall, (steelPlate, 1));
            AddGRecipe("Recipe_GArmorLarge", "Large Armor Block", itemArmorLarge, (steelPlate, 6));

            // -- 5) Tools & Industrial (Large) --
            var drillPref = MakeGPref<VoxelEngine.GridSystem.GridDrill>("Drill_Large", new Color(0.8f, 0.6f, 0.1f), new Vector3(1.5f, 1.5f, 2.0f),
                d => { d.drillRadius = 2f; d.drillStrength = 120f; d.drillRate = 3f; });
            var itemDrill = MakeGItem("GItem_Drill", "Mining Drill", Color.white, drillPref, VoxelEngine.GridSystem.GridSize.Large, 920, 650);
            AddGRecipe("Recipe_GDrill", "Mining Drill", itemDrill, (steelPlate, 8), (circuit, 4), (copperWire, 6));

            var grinderPref = MakeGPref<VoxelEngine.GridSystem.GridGrinder>("Grinder_Large", new Color(0.6f, 0.2f, 0.2f), new Vector3(1.2f, 1.2f, 1.6f),
                g => { g.grindRadius = 1.2f; g.grindStrength = 60f; g.grindRate = 5f; });
            var itemGrinder = MakeGItem("GItem_Grinder", "Grinder", Color.white, grinderPref, VoxelEngine.GridSystem.GridSize.Large, 280, 560);
            AddGRecipe("Recipe_GGrinder", "Grinder", itemGrinder, (steelPlate, 5), (circuit, 2));

            // Ship Electric Furnace — auto-smelts ship cargo into ingots.
            var furnacePref = MakeGPref<VoxelEngine.GridSystem.GridElectricFurnace>("ElectricFurnace_Large", new Color(0.5f, 0.3f, 0.2f), Vector3.one,
                f => { f.baseWattsPerSecond = 300f; f.autoPull = true; });
            var itemFurnace = MakeGItem("GItem_ElectricFurnace", "Ship Electric Furnace", Color.white, furnacePref, VoxelEngine.GridSystem.GridSize.Large, 600, 800);
            AddGRecipe("Recipe_GElectricFurnace", "Ship Electric Furnace", itemFurnace, (steelPlate, 8), (circuit, 4), (copperWire, 6));

            var refineryPref = MakeGPref<VoxelEngine.GridSystem.GridRefinery>("Refinery_Large", new Color(0.5f, 0.4f, 0.2f), new Vector3(2.5f, 2.5f, 2.5f),
                r => { r.baseWattsPerSecond = 850f; });
            var itemRefinery = MakeGItem("GItem_Refinery", "Ship Refinery", Color.white, refineryPref, VoxelEngine.GridSystem.GridSize.Large, 1200, 900);
            AddGRecipe("Recipe_GRefinery", "Ship Refinery", itemRefinery, (steelPlate, 14), (circuit, 8), (copperWire, 10));

            // -- 6) Weapon (gated separately behind Ship Armament) --
            var weaponPref = MakeGPref<VoxelEngine.GridSystem.GridWeapon>("Weapon_Large", new Color(0.2f, 0.2f, 0.2f), new Vector3(0.8f, 0.8f, 2.0f),
                w => { w.damage = 50f; w.fireRate = 4f; w.range = 200f; w.powerPerShot = 80f; });
            var itemWeapon = MakeGItem("GItem_Weapon", "Gatling Weapon", Color.white, weaponPref, VoxelEngine.GridSystem.GridSize.Large, 310, 620);
            var recWeapon = AddGRecipe("Recipe_GWeapon", "Gatling Weapon", itemWeapon, (steelPlate, 6), (circuit, 4), (copperWire, 8));

            // Ammunition for the Gatling Weapon (itemId contains "ammo" — matched by GridWeapon).
            var itemAmmo = MakeResource(ITEMS, "Ammo Magazine", new Color(0.75f, 0.6f, 0.15f), 200, VoxelEngine.Items.ResourceCategory.Component, uiCategory: "Grid Blocks");
            itemAmmo.itemId = "ammo_magazine"; itemAmmo.massPerUnit = 2f; EditorUtility.SetDirty(itemAmmo);
            var recAmmo = AddGRecipe("Recipe_GAmmo", "Ammo Magazine", itemAmmo, (steelPlate, 1), (copperWire, 2));

            // ════════════════════════════════════════════════════════════════
            //  ADDITIONAL GRID BLOCKS — full parity with the GridSystem scripts.
            // ════════════════════════════════════════════════════════════════

            // -- 7) Logistics & Storage --
            var cargoSmallPref = MakeGPref<VoxelEngine.GridSystem.GridCargoContainer>("Cargo_Small", new Color(0.55f, 0.45f, 0.25f), new Vector3(0.5f, 0.5f, 0.5f), c => { c.slots = 12; c.maxMassKg = 100_000f; });
            var itemCargoSmall = MakeGItem("GItem_CargoSmall", "Small Cargo Container", Color.white, cargoSmallPref, VoxelEngine.GridSystem.GridSize.Small, 120, 300);
            AddGRecipe("Recipe_GCargoSmall", "Small Cargo Container", itemCargoSmall, (steelPlate, 2), (ironPlate, 2));

            var cargoLargePref = MakeGPref<VoxelEngine.GridSystem.GridCargoContainer>("Cargo_Large", new Color(0.55f, 0.45f, 0.25f), new Vector3(2.5f, 2.5f, 2.5f), c => { c.slots = 24; c.maxMassKg = 1_000_000f; });
            var itemCargoLarge = MakeGItem("GItem_CargoLarge", "Large Cargo Container", Color.white, cargoLargePref, VoxelEngine.GridSystem.GridSize.Large, 400, 700);
            AddGRecipe("Recipe_GCargoLarge", "Large Cargo Container", itemCargoLarge, (steelPlate, 6), (ironPlate, 4));

            var pipePref = MakeGPref<VoxelEngine.GridSystem.GridItemPipe>("ItemPipe_Small", new Color(0.7f, 0.7f, 0.75f), new Vector3(0.3f, 0.3f, 0.8f), p => p.transferRate = 10f);
            var itemPipe = MakeGItem("GItem_ItemPipe", "Item Pipe", Color.white, pipePref, VoxelEngine.GridSystem.GridSize.Small, 30, 120);
            AddGRecipe("Recipe_GItemPipe", "Item Pipe", itemPipe, (ironPlate, 1), (copperWire, 1));

            // Gas pipe — distributes hydrogen to thrusters grid-wide.
            var gasPipePref = MakeGPref<VoxelEngine.GridSystem.GridGasPipe>("GasPipe_Small", new Color(0.4f, 0.7f, 0.9f), new Vector3(0.3f, 0.3f, 0.8f), p => p.throughput = 50f);
            var gasPipe = MakeGItem("GItem_GasPipe", "Gas Pipe", Color.white, gasPipePref, VoxelEngine.GridSystem.GridSize.Small, 30, 120);
            AddGRecipe("Recipe_GGasPipe", "Gas Pipe", gasPipe, (ironPlate, 1), (glass, 1));

            // Liquid pipe — connects liquid tanks + machines grid-wide.
            var liqPipePref = MakeGPref<VoxelEngine.GridSystem.GridLiquidPipe>("LiquidPipe_Small", new Color(0.3f, 0.5f, 0.85f), new Vector3(0.3f, 0.3f, 0.8f), p => p.throughput = 50f);
            var liqPipe = MakeGItem("GItem_LiquidPipe", "Liquid Pipe", Color.white, liqPipePref, VoxelEngine.GridSystem.GridSize.Small, 30, 120);
            AddGRecipe("Recipe_GLiquidPipe", "Liquid Pipe", liqPipe, (ironPlate, 1), (copperWire, 1));

            var dockPref = MakeGPref<VoxelEngine.GridSystem.GridDockingPort>("DockingPort_Large", new Color(0.6f, 0.6f, 0.2f), new Vector3(1.5f, 0.5f, 1.5f));
            var itemDock = MakeGItem("GItem_DockingPort", "Docking Port", Color.white, dockPref, VoxelEngine.GridSystem.GridSize.Large, 410, 500);
            AddGRecipe("Recipe_GDockingPort", "Docking Port", itemDock, (steelPlate, 5), (circuit, 2), (copperWire, 4));

            // -- 8) Mobility & Landing --
            var wheelPref = MakeGPref<VoxelEngine.GridSystem.GridWheel>("Wheel_Large", new Color(0.12f, 0.12f, 0.12f), new Vector3(1.2f, 1.2f, 0.6f), w => { w.driveForce = 15000f; w.steerAngle = 30f; });
            var itemWheel = MakeGItem("GItem_Wheel", "Wheel", Color.white, wheelPref, VoxelEngine.GridSystem.GridSize.Large, 220, 400);
            AddGRecipe("Recipe_GWheel", "Wheel", itemWheel, (steelPlate, 3), (copperWire, 2));

            var gearPref = MakeGPref<VoxelEngine.GridSystem.GridLandingGear>("LandingGear_Large", new Color(0.5f, 0.5f, 0.55f), new Vector3(0.8f, 1.0f, 0.8f));
            var itemGear = MakeGItem("GItem_LandingGear", "Landing Gear", Color.white, gearPref, VoxelEngine.GridSystem.GridSize.Large, 480, 450);
            AddGRecipe("Recipe_GLandingGear", "Landing Gear", itemGear, (steelPlate, 4), (ironPlate, 2));

            // -- 9) Power generation --
            var solarPref = MakeGPref<VoxelEngine.GridSystem.GridSolarPanel>("SolarPanel_Large", new Color(0.1f, 0.2f, 0.5f), new Vector3(2.5f, 0.2f, 2.5f), s => s.maxOutput = 400f);
            var itemSolar = MakeGItem("GItem_SolarPanel", "Solar Panel", Color.white, solarPref, VoxelEngine.GridSystem.GridSize.Large, 350, 250);
            AddGRecipe("Recipe_GSolarPanel", "Solar Panel", itemSolar, (steelPlate, 4), (circuit, 6), (glass, 6));

            var reactorPref = MakeGPref<VoxelEngine.GridSystem.GridPortableReactor>("PortableReactor_Large", new Color(0.2f, 0.6f, 0.2f), new Vector3(1.8f, 1.8f, 1.8f), r => { r.wattsOutput = 800f; r.pelletBurnTime = 300f; });
            var itemReactor = MakeGItem("GItem_PortableReactor", "Portable Reactor", Color.white, reactorPref, VoxelEngine.GridSystem.GridSize.Large, 1400, 1100);
            var recReactor = AddGRecipe("Recipe_GPortableReactor", "Portable Reactor", itemReactor, (steelPlate, 16), (advCircuit ?? circuit, 6), (copperWire, 12));

            // -- 10) Fluids & Gas --
            // Unified Liquid Tank — replaces the old Water Tank + Liquid Fuel Tank.
            // Liquid type is chosen from the tank's UI (Water / Crude / Refined / Liquid Fuel).
            var liquidTankPref = MakeGPref<VoxelEngine.GridSystem.GridLiquidTank>("LiquidTank_Large", new Color(0.25f, 0.5f, 0.85f), new Vector3(1.5f, 1.8f, 1.5f), t => { t.capacity = 500f; t.liquidType = VoxelEngine.Items.LiquidType.Water; });
            var itemLiquidTank = MakeGItem("GItem_LiquidTank", "Liquid Tank", Color.white, liquidTankPref, VoxelEngine.GridSystem.GridSize.Large, 220, 400);
            AddGRecipe("Recipe_GLiquidTank", "Liquid Tank", itemLiquidTank, (steelPlate, 5), (glass, 2), (copperWire, 2));

            var gasTankPref = MakeGPref<VoxelEngine.GridSystem.GridGasTank>("GasTank_Large", new Color(0.55f, 0.7f, 0.85f), new Vector3(1.5f, 1.8f, 1.5f), t => t.capacity = 500f);
            var itemGasTank = MakeGItem("GItem_GasTank", "Gas Tank", Color.white, gasTankPref, VoxelEngine.GridSystem.GridSize.Large, 240, 380);
            AddGRecipe("Recipe_GGasTank", "Gas Tank", itemGasTank, (steelPlate, 5), (glass, 2), (copperWire, 2));

            var h2o2Pref = MakeGPref<VoxelEngine.GridSystem.GridH2O2Generator>("H2O2Generator_Large", new Color(0.3f, 0.7f, 0.9f), new Vector3(1.5f, 1.5f, 2.0f));
            var itemH2O2 = MakeGItem("GItem_H2O2Generator", "H2/O2 Generator", Color.white, h2o2Pref, VoxelEngine.GridSystem.GridSize.Large, 600, 700);
            AddGRecipe("Recipe_GH2O2Generator", "H2/O2 Generator", itemH2O2, (steelPlate, 8), (circuit, 4), (copperWire, 8));

            // -- 11) Structure / Misc --
            var glassPref = MakeGPref<VoxelEngine.GridSystem.GridGlassBlock>("Glass_Small", new Color(0.7f, 0.85f, 0.95f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f));
            var itemGlassBlk = MakeGItem("GItem_GlassBlock", "Glass Block", Color.white, glassPref, VoxelEngine.GridSystem.GridSize.Small, 40, 120);
            AddGRecipe("Recipe_GGlassBlock", "Glass Block", itemGlassBlk, (glass, 1));

            var demoPref = MakeGPref<VoxelEngine.GridSystem.GridDemolisher>("Demolisher_Large", new Color(0.7f, 0.3f, 0.1f), new Vector3(1.2f, 1.2f, 1.6f), d => { d.damagePerSecond = 50f; d.terrainDPS = 30f; });
            var itemDemo = MakeGItem("GItem_Demolisher", "Demolisher", Color.white, demoPref, VoxelEngine.GridSystem.GridSize.Large, 320, 560);
            AddGRecipe("Recipe_GDemolisher", "Demolisher", itemDemo, (steelPlate, 6), (circuit, 3));

            // -- 12) Chemical Plant (grid) — shares Chemistry ProcessingRecipes --
            string procFolder = ASSET_ROOT + "/Industrial/ProcessingRecipes";
            var procRefineShared  = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.ProcessingRecipe>($"{procFolder}/Proc_RefineOil.asset");
            var procPlasticShared = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.ProcessingRecipe>($"{procFolder}/Proc_MakePlastic.asset");
            var procChemistry     = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.ProcessingRecipe>($"{procFolder}/Proc_MakeLiquidFuel.asset");

            var chemPref = MakeGPref<VoxelEngine.GridSystem.GridChemicalPlant>("ChemicalPlant_Large", new Color(0.5f, 0.7f, 0.4f), new Vector3(2.5f, 2.5f, 2.5f),
                cp =>
                {
                    cp.knownRecipes = new System.Collections.Generic.List<VoxelEngine.Crafting.ProcessingRecipe>();
                    if (procChemistry != null) cp.knownRecipes.Add(procChemistry);
                });
            var itemChem = MakeGItem("GItem_ChemicalPlant", "Ship Chemical Plant", Color.white, chemPref, VoxelEngine.GridSystem.GridSize.Large, 1100, 900);
            AddGRecipe("Recipe_GChemicalPlant", "Ship Chemical Plant", itemChem, (steelPlate, 12), (circuit, 8), (copperWire, 8));

            // -- Recipe parity: grid Refinery uses the SAME ProcessingRecipe assets
            //    as the stationary Oil Refinery (Refine Crude Oil + Synthesise Plastic).
            {
                string refPath = AssetDatabase.GetAssetPath(refineryPref);
                var refContents = PrefabUtility.LoadPrefabContents(refPath);
                try
                {
                    var gr = refContents.GetComponent<VoxelEngine.GridSystem.GridRefinery>();
                    if (gr != null)
                    {
                        gr.knownRecipes = new System.Collections.Generic.List<VoxelEngine.Crafting.ProcessingRecipe>();
                        if (procRefineShared  != null) gr.knownRecipes.Add(procRefineShared);
                        if (procPlasticShared != null) gr.knownRecipes.Add(procPlasticShared);
                    }
                    PrefabUtility.SaveAsPrefabAsset(refContents, refPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(refContents); }
            }

            // -- Research Nodes --
            if (tree != null)
            {
                var nShip = FindNodeByName(tree, "res_shipbuilding");
                if (nShip == null)
                {
                    nShip = ScriptableObject.CreateInstance<VoxelEngine.Research.ResearchNode>();
                    nShip.nodeId = "res_shipbuilding";
                    nShip.displayName = "Shipbuilding";
                    nShip.description = "Design and construct functional spacecraft from a full grid block set.";
                    nShip.category = VoxelEngine.Research.ResearchCategory.Environment;
                    nShip.subCategory = VoxelEngine.Research.ResearchSubCategory.Building;
                    nShip.tier = 3; nShip.column = 4;
                    nShip.iconTint = new Color(0.3f, 0.6f, 0.9f);
                    nShip.researchSeconds = 60f;
                    nShip.cost = new[] {
                        new VoxelEngine.Research.ResearchNode.ScienceCost { pack = sciT2, count = 20 },
                        new VoxelEngine.Research.ResearchNode.ScienceCost { pack = sciT3, count = 10 }
                    };
                    var nAdvMfg = FindNodeByName(tree, "res_adv_manufacturing");
                    if (nAdvMfg != null) nShip.prerequisites = new[] { nAdvMfg };
                    AssetDatabase.CreateAsset(nShip, $"{NODES}/res_shipbuilding.asset");
                    tree.nodes.Add(nShip);
                }
                // Everything except the weapon unlocks with Shipbuilding.
                nShip.unlocksRecipes = recipes.FindAll(r => r != null && r != recWeapon && r != recAmmo).ToArray();
                EditorUtility.SetDirty(nShip);

                if (recWeapon != null)
                {
                    var nArm = FindNodeByName(tree, "res_ship_weapons");
                    if (nArm == null)
                    {
                        nArm = ScriptableObject.CreateInstance<VoxelEngine.Research.ResearchNode>();
                        nArm.nodeId = "res_ship_weapons";
                        nArm.displayName = "Ship Armament";
                        nArm.description = "Unlocks ship-mounted weapons and defense systems.";
                        nArm.category = VoxelEngine.Research.ResearchCategory.Environment;
                        nArm.subCategory = VoxelEngine.Research.ResearchSubCategory.Military;
                        nArm.tier = 4; nArm.column = 5;
                        nArm.iconTint = new Color(0.9f, 0.25f, 0.2f);
                        nArm.researchSeconds = 90f;
                        nArm.cost = new[] { new VoxelEngine.Research.ResearchNode.ScienceCost { pack = sciT2, count = 30 } };
                        nArm.prerequisites = new[] { nShip };
                        AssetDatabase.CreateAsset(nArm, $"{NODES}/res_ship_weapons.asset");
                        tree.nodes.Add(nArm);
                    }
                    nArm.unlocksRecipes = recAmmo != null ? new[] { recWeapon, recAmmo } : new[] { recWeapon };
                    EditorUtility.SetDirty(nArm);
                }
                EditorUtility.SetDirty(tree);
            }

            if (registry != null) EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Grid System",
                $"Step 12 complete — generated {recipes.Count} grid blocks (prefabs + items + recipes) in:\n{GRID_ROOT}\n\n" +
                "• Cockpit, Thruster, Battery, Armor (Small + Large)\n" +
                "• Drill, Grinder, Refinery, Weapon, Demolisher (Large)\n" +
                "• Cargo, Item Pipe, Docking Port, Wheel, Landing Gear\n" +
                "• Solar Panel, Portable Reactor, Water/Fuel/Gas Tanks, H2/O2 Gen\n" +
                "• Glass Block, Chemical Plant\n\n" +
                "Grid Refinery shares the SAME ProcessingRecipes as the Oil Refinery.\n" +
                "Recipes registered and gated behind Shipbuilding / Ship Armament research.", "OK");
        }

        private static VoxelEngine.Research.ResearchNode FindNodeByName(VoxelEngine.Research.ResearchTree tree, string id)
        {
            if (tree == null) return null;
            foreach (var n in tree.nodes) if (n != null && n.nodeId == id) return n;
            return null;
        }

        private static void EnsureFolders()
        {
            void Ensure(string p)
            {
                if (!AssetDatabase.IsValidFolder(p))
                {
                    var parent = Path.GetDirectoryName(p).Replace("\\", "/");
                    var leaf   = Path.GetFileName(p);
                    AssetDatabase.CreateFolder(parent, leaf);
                }
            }
            Ensure(ASSET_ROOT);
            Ensure(MAT_FOLDER);
            Ensure(ITEM_FOLDER);
            Ensure(PLANET_FOLDER);
            Ensure(BIOME_FOLDER);
        }
    }
}
#endif
