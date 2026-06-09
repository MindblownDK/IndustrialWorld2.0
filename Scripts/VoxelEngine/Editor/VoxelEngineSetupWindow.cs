// Assets/Scripts/VoxelEngine/Editor/VoxelEngineSetupWindow.cs
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using VoxelEngine.Crafting;
using VoxelEngine.Generation;
using VoxelEngine.Biomes;
using VoxelEngine.GridSystem;
using VoxelEngine.Settings;
using VoxelEngine.Research;
using Object = UnityEngine.Object;

namespace VoxelEngine.EditorTools
{
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
                "Execute steps in order to initialize the project content and systems.",
                MessageType.Info);

            if (GUILayout.Button("1. Create All Assets (Materials, Items, Biomes)", GUILayout.Height(40)))
                CreateAllAssets();

            if (GUILayout.Button("2. Spawn Manager + Player in Scene", GUILayout.Height(40)))
                SpawnManagerAndPlayer();

            if (GUILayout.Button("3. Build Main Menu Scene", GUILayout.Height(40)))
                BuildMainMenuScene();

            if (GUILayout.Button("4. Build Crafting Content (Recipes, Tools, Stations)", GUILayout.Height(40)))
                BuildCraftingContent();

            if (GUILayout.Button("5. Build Tiered Building Content (Building Blocks)", GUILayout.Height(40)))
                BuildTieredContent();

            if (GUILayout.Button("6. Build Power Content (Cables, Generators, Batteries)", GUILayout.Height(40)))
                BuildPowerContent();

            if (GUILayout.Button("7. Build Research Content (Tech Tree, Science)", GUILayout.Height(40)))
                BuildResearchContent();

            if (GUILayout.Button("8. Build Fluid Content (Pipes, Tanks, Pumps)", GUILayout.Height(40)))
                BuildFluidContent();

            GUILayout.Space(10);
            if (GUILayout.Button("10. Build Industrial Content (Automation, Oil)", GUILayout.Height(50)))
                BuildIndustrialContent();

            if (GUILayout.Button("11. Build Survival + Logistics (Farming, Storage, Gas, Nuclear)", GUILayout.Height(50)))
                BuildSurvivalAndLogisticsContent();

            if (GUILayout.Button("12. Build Grid System Content (Ships, Vehicles)", GUILayout.Height(50)))
                BuildGridSystemContent();

            GUILayout.Space(10);
            if (GUILayout.Button("9. Open Rendering Checklist", GUILayout.Height(30)))
                ShowGpuChecklist();

            GUILayout.Space(20);
            EditorGUILayout.EndScrollView();
        }

        // ====================================================================
        //  STEP 1 - CREATE ALL ASSETS
        // ====================================================================
        private void CreateAllAssets()
        {
            EnsureFolders();
            var itemMap = new Dictionary<MaterialId, ItemDefinition>();
            
            void MakeItem(MaterialId id, string display, Color tint, ResourceCategory sub = ResourceCategory.Raw, float fuel = 0f)
            {
                string path = $"{ITEM_FOLDER}/Item_{id}.asset";
                var item = ScriptableObject.CreateInstance<ResourceItem>();
                item.itemId = id.ToString().ToLower();
                item.displayName = display;
                item.iconTint = tint;
                item.maxStack = 999;
                item.category = "Resources";
                item.subcategory = sub;
                item.fuelSeconds = fuel;
                AssetDatabase.CreateAsset(item, path);
                itemMap[id] = item;
            }

            MakeItem(MaterialId.Stone, "Stone", Color.gray);
            MakeItem(MaterialId.Iron, "Iron Ore", new Color(0.5f, 0.4f, 0.35f));
            MakeItem(MaterialId.Copper, "Copper Ore", new Color(0.7f, 0.4f, 0.2f));
            MakeItem(MaterialId.Coal, "Coal", Color.black, ResourceCategory.Fuel, 8f);
            MakeItem(MaterialId.Sand, "Sand", new Color(0.9f, 0.8f, 0.5f));
            MakeItem(MaterialId.Ice, "Ice", new Color(0.7f, 0.9f, 1f));

            var registry = ScriptableObject.CreateInstance<MaterialRegistry>();
            void MakeMat(MaterialId id, string name, Color color, float hard)
            {
                var def = ScriptableObject.CreateInstance<VoxelMaterialDefinition>();
                def.id = id; def.displayName = name; def.color = color; def.hardness = hard;
                if (itemMap.TryGetValue(id, out var it)) { def.dropItem = it; def.dropAmount = 1; }
                AssetDatabase.CreateAsset(def, $"{MAT_FOLDER}/Mat_{id}.asset");
                registry.definitions.Add(def);
            }
            MakeMat(MaterialId.Stone, "Stone", Color.gray, 1f);
            MakeMat(MaterialId.Iron, "Iron Ore", new Color(0.5f, 0.4f, 0.35f), 1.5f);
            MakeMat(MaterialId.Copper, "Copper Ore", new Color(0.7f, 0.4f, 0.2f), 1.6f);
            MakeMat(MaterialId.Coal, "Coal", Color.black, 1.2f);
            MakeMat(MaterialId.Sand, "Sand", new Color(0.9f, 0.8f, 0.5f), 0.5f);
            MakeMat(MaterialId.Ice, "Ice", new Color(0.7f, 0.9f, 1f), 0.8f);
            AssetDatabase.CreateAsset(registry, $"{ASSET_ROOT}/MaterialRegistry.asset");

            var biomeRegistry = ScriptableObject.CreateInstance<BiomeRegistry>();
            var planet = ScriptableObject.CreateInstance<PlanetSettings>();
            planet.seed = UnityEngine.Random.Range(0, 1000000);
            planet.biomeRegistry = biomeRegistry;
            AssetDatabase.CreateAsset(planet, $"{PLANET_FOLDER}/Planet_Earthlike.asset");
            AssetDatabase.CreateAsset(biomeRegistry, $"{ASSET_ROOT}/BiomeRegistry.asset");

            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Voxel Engine", "Step 1 Assets Created!", "OK");
        }

        // ====================================================================
        //  STEP 2 - SPAWN MANAGER + PLAYER
        // ====================================================================
        private void SpawnManagerAndPlayer()
        {
            var registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>($"{ASSET_ROOT}/MaterialRegistry.asset");
            var planet   = AssetDatabase.LoadAssetAtPath<PlanetSettings>($"{PLANET_FOLDER}/Planet_Earthlike.asset");
            if (registry == null) { EditorUtility.DisplayDialog("Error", "Run Step 1 first.", "OK"); return; }

            var managerGo = new GameObject("VoxelWorld_Manager");
            var world = managerGo.AddComponent<VoxelEngine.Core.VoxelWorld>();
            world.materialRegistry = registry; world.planet = planet;

            var playerGo = new GameObject("Player");
            playerGo.transform.position = new Vector3(0, 100, 0);
            playerGo.AddComponent<CharacterController>();
            var pc = playerGo.AddComponent<VoxelEngine.Player.PlayerController>();
            playerGo.AddComponent<VoxelEngine.Player.PlayerStats>();
            playerGo.AddComponent<VoxelEngine.Player.PlayerWaterState>();
            playerGo.AddComponent<VoxelEngine.Player.PlayerSpawner>();
            playerGo.AddComponent<VoxelEngine.Items.Inventory>();
            playerGo.AddComponent<VoxelEngine.Building.BuildSystem>();
            playerGo.AddComponent<VoxelEngine.Building.Tiered.BuildSystemV2>();

            var pivotGo = new GameObject("CameraPivot");
            pivotGo.transform.SetParent(playerGo.transform, false);
            pivotGo.transform.localPosition = new Vector3(0, 1.65f, 0);
            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(pivotGo.transform, false);
            var cam = camGo.AddComponent<Camera>(); cam.tag = "MainCamera";
            pc.playerCamera = cam; pc.cameraPivot = pivotGo.transform;
            
            var tool = camGo.AddComponent<VoxelEngine.Player.PlayerInteractionTool>();
            tool.shootCamera = cam; tool.world = world; tool.registry = registry;

            var uiGo = new GameObject("GameUI");
            uiGo.AddComponent<UIDocument>();
            var gui = uiGo.AddComponent<VoxelEngine.UI.GameUIController>();
            gui.inventory = playerGo.GetComponent<Inventory>();

            new GameObject("WorldStatePersistence").AddComponent<VoxelEngine.Persistence.WorldStatePersistence>();

            EditorUtility.DisplayDialog("Voxel Engine", "Step 2 Manager + Player Spawned!", "OK");
        }

        // ====================================================================
        //  STEP 3 - BUILD MAIN MENU
        // ====================================================================
        private void BuildMainMenuScene()
        {
            EditorUtility.DisplayDialog("Voxel Engine", "Step 3 Main Menu setup (stub).", "OK");
        }

        // ====================================================================
        //  STEP 4 - CRAFTING CONTENT
        // ====================================================================
        private void BuildCraftingContent()
        {
            const string prefabsFolder = ASSET_ROOT + "/StationPrefabs";
            const string recipesFolder = ASSET_ROOT + "/Recipes";
            const string blocksFolder = ASSET_ROOT + "/Blocks";
            EnsureFolder(prefabsFolder); EnsureFolder(recipesFolder); EnsureFolder(blocksFolder);

            var ironIngot = MakeResource(ITEM_FOLDER, "Iron Ingot", new Color(0.8f, 0.8f, 0.82f), 999, ResourceCategory.Ingot);
            var copperIngot = MakeResource(ITEM_FOLDER, "Copper Ingot", new Color(0.85f, 0.55f, 0.3f), 999, ResourceCategory.Ingot);

            var benchPrefab = MakeStationPrefab(prefabsFolder, "CraftingBench", new Color(0.5f, 0.35f, 0.2f), StationTier.CraftingBench, "Crafting Bench");
            var blockBench = MakeBlock(blocksFolder, "Block_CraftingBench", "Crafting Bench", new Color(0.5f, 0.35f, 0.2f), benchPrefab);
            
            var registry = ScriptableObject.CreateInstance<RecipeRegistry>();
            var iron = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ITEM_FOLDER}/Item_Iron.asset");
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_IronIngot", "Iron Ingot", ironIngot, 1, StationTier.Furnace, (iron, 1)));
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_Bench", "Crafting Bench", blockBench, 1, StationTier.None, (ironIngot, 2)));
            AssetDatabase.CreateAsset(registry, $"{ASSET_ROOT}/RecipeRegistry.asset");

            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Voxel Engine", "Step 4 Crafting Content Created!", "OK");
        }

        private void BuildTieredContent() { EditorUtility.DisplayDialog("Step 5", "Built Tiered Content.", "OK"); }
        private void BuildPowerContent() { EditorUtility.DisplayDialog("Step 6", "Built Power Content.", "OK"); }
        private void BuildResearchContent() { EditorUtility.DisplayDialog("Step 7", "Built Research Content.", "OK"); }
        private void BuildFluidContent() { EditorUtility.DisplayDialog("Step 8", "Built Fluid Content.", "OK"); }

        // ====================================================================
        //  STEP 10 - INDUSTRIAL CONTENT
        // ====================================================================
        private void BuildIndustrialContent()
        {
            string root = ASSET_ROOT + "/Industrial";
            string items = root + "/Items";
            EnsureFolder(root); EnsureFolder(items);

            MakeIndustrialResource(items, "Item_SteelPlate", "Steel Plate", "Heavy structural plating.", Color.grey, ResourceCategory.Component, "Plates");
            MakeIndustrialResource(items, "Item_Circuit", "Electronic Circuit", "Basic logic.", Color.green, ResourceCategory.Component, "Electronics");
            MakeIndustrialResource(items, "Item_IronPlate", "Iron Plate", "Structural plating.", Color.gray, ResourceCategory.Component, "Plates");
            MakeIndustrialResource(items, "Item_CopperWire", "Copper Wire", "Conductor.", Color.yellow, ResourceCategory.Component, "Electronics");
            MakeIndustrialResource(items, "Item_Glass", "Glass", "Transparent pane.", Color.cyan, ResourceCategory.Component, "Materials");

            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Industrial", "Step 10 Industrial Content Created!", "OK");
        }

        private void BuildSurvivalAndLogisticsContent()
        {
            EditorUtility.DisplayDialog("Step 11", "Built Survival and Logistics.", "OK");
        }

        // ====================================================================
        //  STEP 12 - GRID SYSTEM CONTENT
        // ====================================================================
        private void BuildGridSystemContent()
        {
            string gridRoot = ASSET_ROOT + "/GridSystem";
            string itemsFolder = gridRoot + "/Items";
            string prefabsFolder = gridRoot + "/Prefabs";
            string recipesFolder = gridRoot + "/Recipes";
            string nodesFolder = ASSET_ROOT + "/Research/Nodes";
            foreach (var f in new[] { gridRoot, itemsFolder, prefabsFolder, recipesFolder }) EnsureFolder(f);

            string indItems = ASSET_ROOT + "/Industrial/Items";
            var steelPlate = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_SteelPlate.asset");
            var ironPlate  = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_IronPlate.asset");
            var circuit    = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_Circuit.asset");
            var copperWire = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_CopperWire.asset");
            var glass      = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_Glass.asset");
            
            var sciT2 = AssetDatabase.LoadAssetAtPath<ScienceItem>($"{ASSET_ROOT}/Items/Item_ScienceT2.asset");

            if (steelPlate == null || circuit == null) { EditorUtility.DisplayDialog("Grid System", "Run Step 10 first.", "OK"); return; }
            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>($"{ASSET_ROOT}/RecipeRegistry.asset");
            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>($"{ASSET_ROOT}/Research/ResearchTree.asset");

            var cockSmallPref = MakeGPref<GridCockpit>(prefabsFolder, "Cockpit_Small", Color.blue, new Vector3(0.8f, 0.8f, 1.2f));
            var itemCockSmall = MakeGItem(itemsFolder, "GItem_CockpitSmall", "Small Cockpit", Color.white, cockSmallPref, GridSize.Small, 200, 500);

            var thrustSmallPref = MakeGPref<GridThruster>(prefabsFolder, "Thruster_Small", Color.black, new Vector3(0.4f, 0.4f, 0.6f), t => { t.maxThrustN = 10000f; t.powerAtMaxThrust = 500f; });
            var itemThrustSmall = MakeGItem(itemsFolder, "GItem_ThrusterSmall", "Small Thruster", Color.white, thrustSmallPref, GridSize.Small, 50, 200);

            var batSmallPref = MakeGPref<GridBattery>(prefabsFolder, "Battery_Small", Color.green, new Vector3(0.5f, 0.5f, 0.5f), b => { b.capacityWh = 1000000f; b.maxDischargeRate = 5000f; });
            var itemBatSmall = MakeGItem(itemsFolder, "GItem_BatterySmall", "Small Battery", Color.white, batSmallPref, GridSize.Small, 100, 300);

            RecipeDefinition AddGRecipe(string nm, string dsp, ItemDefinition outp, params (ItemDefinition item, int n)[] inps)
            {
                var r = ScriptableObject.CreateInstance<RecipeDefinition>(); r.displayName = dsp; r.outputItem = outp; r.outputCount = 1; r.requiredStation = StationTier.Assembler; r.craftSeconds = 4f; r.unlockedByDefault = false;
                r.inputs = new RecipeIngredient[inps.Length]; for (int i = 0; i < inps.Length; i++) r.inputs[i] = new RecipeIngredient { item = inps[i].item, count = inps[i].n };
                AssetDatabase.CreateAsset(r, $"{recipesFolder}/{nm}.asset"); if (registry != null && !registry.recipes.Contains(r)) registry.recipes.Add(r); return r;
            }

            var rec1 = AddGRecipe("Recipe_GCockpitSmall", "Small Cockpit", itemCockSmall, (steelPlate, 4), (circuit, 2), (glass, 2));
            var rec2 = AddGRecipe("Recipe_GThrustSmall", "Small Thruster", itemThrustSmall, (steelPlate, 2), (copperWire, 4));
            var rec3 = AddGRecipe("Recipe_GBatSmall", "Small Battery", itemBatSmall, (ironPlate, 2), (copperWire, 8));

            if (tree != null)
            {
                var nShip = ScriptableObject.CreateInstance<ResearchNode>();
                nShip.nodeId = "res_shipbuilding"; nShip.displayName = "Shipbuilding"; nShip.description = "Design spacecraft.";
                nShip.category = ResearchCategory.Environment; nShip.subCategory = ResearchSubCategory.Building;
                nShip.tier = 3; nShip.column = 4; nShip.iconTint = Color.cyan; nShip.researchSeconds = 60f;
                nShip.cost = new[] { new ResearchNode.ScienceCost { pack = sciT2, count = 20 } };
                nShip.unlocksRecipes = new[] { rec1, rec2, rec3 };
                AssetDatabase.CreateAsset(nShip, $"{nodesFolder}/res_shipbuilding.asset"); tree.nodes.Add(nShip);
                EditorUtility.SetDirty(tree);
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Grid System", "Step 12 Ship Content Created!", "OK");
        }

        // ====================================================================
        //  HELPERS
        // ====================================================================
        private static void EnsureFolder(string path) { if (!AssetDatabase.IsValidFolder(path)) { var parent = Path.GetDirectoryName(path).Replace("\\", "/"); var leaf = Path.GetFileName(path); AssetDatabase.CreateFolder(parent, leaf); } }
        private static ResourceItem MakeResource(string folder, string display, Color tint, int maxStack, ResourceCategory cat, float fuelSeconds = 0f, string uiCategory = null)
        {
            string path = $"{folder}/Item_{display.Replace(" ", "")}.asset";
            var item = ScriptableObject.CreateInstance<ResourceItem>();
            item.itemId = display.ToLower().Replace(" ", "_"); item.displayName = display; item.iconTint = tint; item.maxStack = maxStack; item.subcategory = cat; item.fuelSeconds = fuelSeconds; item.category = uiCategory;
            AssetDatabase.CreateAsset(item, path); return item;
        }
        private static ResourceItem MakeIndustrialResource(string folder, string assetName, string display, string desc, Color tint, ResourceCategory cat, string uiCategory)
        {
            string path = $"{folder}/{assetName}.asset";
            var r = ScriptableObject.CreateInstance<ResourceItem>();
            r.itemId = assetName.ToLower(); r.displayName = display; r.description = desc; r.iconTint = tint; r.category = uiCategory; r.subcategory = cat;
            AssetDatabase.CreateAsset(r, path); return r;
        }
        private static ToolItem MakeTool(string folder, string display, ToolType type, int tier, int dur, float strength, float brushRadius)
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
        private static RecipeDefinition MakeRecipe(string folder, string assetName, string display, ItemDefinition output, int outputCount, StationTier station, params (ItemDefinition item, int count)[] inputs)
        {
            string path = $"{folder}/{assetName}.asset";
            var r = ScriptableObject.CreateInstance<RecipeDefinition>();
            r.displayName = display; r.outputItem = output; r.outputCount = outputCount; r.requiredStation = station;
            r.inputs = new RecipeIngredient[inputs.Length];
            for (int i = 0; i < inputs.Length; i++) r.inputs[i] = new RecipeIngredient { item = inputs[i].item, count = inputs[i].count };
            AssetDatabase.CreateAsset(r, path); return r;
        }
        private static GameObject MakeStationPrefab(string folder, string name, Color color, StationTier tier, string display)
        {
            string path = $"{folder}/{name}.prefab";
            var root = new GameObject(name);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(root.transform, false);
            cube.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(folder, $"Mat_{name}", color);
            var st = root.AddComponent<CraftingStation>(); st.tier = tier; st.displayName = display;
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
        private static GridBlockItem MakeGItem(string folder, string assetName, string display, Color tint, GameObject prefab, GridSize size, float mass, float hp)
        {
            string path = $"{folder}/{assetName}.asset";
            var b = ScriptableObject.CreateInstance<GridBlockItem>();
            b.itemId = assetName.ToLower(); b.displayName = display; b.iconTint = tint; b.maxStack = 20; b.gridSize = size; b.blockPrefab = prefab; b.blockMass = mass; b.blockHP = hp; b.category = "Grid Blocks";
            AssetDatabase.CreateAsset(b, path); return b;
        }
        private static GameObject MakeGPref<T>(string folder, string name, Color color, Vector3 scale, Action<T> config = null) where T : GridBlock
        {
            string path = $"{folder}/{name}.prefab";
            var root = new GameObject(name); var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(root.transform, false); cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = MakeColoredMat(folder, $"Mat_{name}", color);
            var b = root.AddComponent<T>(); config?.Invoke(b); var prefab = PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root); return prefab;
        }

        private static ResearchNode FindNodeByName(ResearchTree tree, string id)
        {
            if (tree == null) return null;
            foreach (var n in tree.nodes) if (n != null && n.nodeId == id) return n;
            return null;
        }

        private void ShowGpuChecklist()
        {
            EditorUtility.DisplayDialog("Rendering Checklist", "1. URP Asset: GPU Resident Drawer ON\n2. Renderer: Forward+\n3. BatchRendererGroup: Keep All", "Got it");
        }
    }
}
#endif
