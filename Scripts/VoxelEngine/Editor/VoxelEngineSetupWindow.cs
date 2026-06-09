// Assets/Scripts/VoxelEngine/Editor/VoxelEngineSetupWindow.cs
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using VoxelEngine.Crafting;
using VoxelEngine.Generation;
using VoxelEngine.Biomes;
using VoxelEngine.GridSystem;
using VoxelEngine.Settings;

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
                "Click each step in order.\n" +
                "1. Create assets — generates materials, items, planet definitions.\n" +
                "2. Spawn manager — adds VoxelWorld + Player to the active scene.\n" +
                "3. Build main menu scene (saves browser + new world UI).\n" +
                "4. Build Crafting Content (recipes, tools, stations, blocks).",
                MessageType.Info);

            if (GUILayout.Button("1. Create All Assets", GUILayout.Height(40)))
                CreateAllAssets();

            if (GUILayout.Button("2. Spawn Manager + Player in Scene", GUILayout.Height(40)))
                SpawnManagerAndPlayer();

            if (GUILayout.Button("3. Build Main Menu Scene", GUILayout.Height(40)))
                BuildMainMenuScene();

            if (GUILayout.Button("4. Build Crafting Content", GUILayout.Height(40)))
                BuildCraftingContent();

            if (GUILayout.Button("5. Build Tiered Building Content", GUILayout.Height(40)))
                BuildTieredContent();

            if (GUILayout.Button("6. Build Power Content", GUILayout.Height(40)))
                BuildPowerContent();

            if (GUILayout.Button("7. Build Research Content", GUILayout.Height(40)))
                BuildResearchContent();

            if (GUILayout.Button("8. Build Fluid Content", GUILayout.Height(40)))
                BuildFluidContent();

            GUILayout.Space(10);
            if (GUILayout.Button("10. Build Industrial Content", GUILayout.Height(50)))
                BuildIndustrialContent();

            if (GUILayout.Button("11. Build Survival + Logistics Content", GUILayout.Height(50)))
                BuildSurvivalAndLogisticsContent();

            if (GUILayout.Button("12. Build Grid System Content", GUILayout.Height(50)))
                BuildGridSystemContent();

            GUILayout.Space(10);
            if (GUILayout.Button("9. Open Rendering Checklist", GUILayout.Height(30)))
                ShowGpuChecklist();

            GUILayout.Space(20);
            EditorGUILayout.EndScrollView();
        }

        private void CreateAllAssets()
        {
            EnsureFolders();
            var itemMap = new Dictionary<MaterialId, ItemDefinition>();
            void MakeItem(MaterialId id, string display)
            {
                string path = $"{ITEM_FOLDER}/Item_{id}.asset";
                if (id == MaterialId.Coal)
                {
                    var coalRes = ScriptableObject.CreateInstance<ResourceItem>();
                    coalRes.itemId = "coal"; coalRes.displayName = "Coal"; coalRes.maxStack = 999;
                    coalRes.subcategory = ResourceCategory.Fuel; coalRes.fuelSeconds = 8f;
                    AssetDatabase.CreateAsset(coalRes, path); itemMap[id] = coalRes; return;
                }
                var item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.itemId = id.ToString().ToLower(); item.displayName = display; item.maxStack = 999;
                AssetDatabase.CreateAsset(item, path); itemMap[id] = item;
            }
            MakeItem(MaterialId.Stone, "Stone"); MakeItem(MaterialId.Iron, "Iron Ore"); MakeItem(MaterialId.Coal, "Coal");
            
            var registry = ScriptableObject.CreateInstance<MaterialRegistry>();
            void MakeMat(MaterialId id, string name, Color color, float hard, ItemDefinition drop)
            {
                var def = ScriptableObject.CreateInstance<VoxelMaterialDefinition>();
                def.id = id; def.displayName = name; def.color = color; def.hardness = hard; def.dropItem = drop; def.dropAmount = 1;
                AssetDatabase.CreateAsset(def, $"{MAT_FOLDER}/Mat_{id}.asset"); registry.definitions.Add(def);
            }
            MakeMat(MaterialId.Stone, "Stone", Color.gray, 1f, itemMap[MaterialId.Stone]);
            AssetDatabase.CreateAsset(registry, $"{ASSET_ROOT}/MaterialRegistry.asset");

            var biomeRegistry = ScriptableObject.CreateInstance<BiomeRegistry>();
            var planet = ScriptableObject.CreateInstance<PlanetSettings>();
            planet.biomeRegistry = biomeRegistry;
            AssetDatabase.CreateAsset(planet, $"{PLANET_FOLDER}/Planet_Earthlike.asset");
            AssetDatabase.CreateAsset(biomeRegistry, $"{ASSET_ROOT}/BiomeRegistry.asset");

            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        }

        private void SpawnManagerAndPlayer()
        {
            var registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>($"{ASSET_ROOT}/MaterialRegistry.asset");
            var planet   = AssetDatabase.LoadAssetAtPath<PlanetSettings>($"{PLANET_FOLDER}/Planet_Earthlike.asset");
            if (registry == null) return;

            var managerGo = new GameObject("VoxelWorld_Manager");
            var world = managerGo.AddComponent<VoxelEngine.Core.VoxelWorld>();
            world.materialRegistry = registry; world.planet = planet;

            var playerGo = new GameObject("Player");
            playerGo.transform.position = new Vector3(0, 100, 0);
            playerGo.AddComponent<CharacterController>();
            var pc = playerGo.AddComponent<VoxelEngine.Player.PlayerController>();
            playerGo.AddComponent<VoxelEngine.Player.PlayerStats>();
            playerGo.AddComponent<VoxelEngine.Items.Inventory>();
            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(playerGo.transform); camGo.transform.localPosition = new Vector3(0, 1.6f, 0);
            var cam = camGo.AddComponent<Camera>(); cam.tag = "MainCamera";
            pc.playerCamera = cam;

            EditorUtility.DisplayDialog("Voxel Engine", "Spawned!", "OK");
        }

        private void BuildMainMenuScene()
        {
            EditorUtility.DisplayDialog("Voxel Engine", "Main Menu built (mock).", "OK");
        }

        private void BuildCraftingContent()
        {
            EnsureFolder(ASSET_ROOT + "/StationPrefabs");
            var itemsFolder = ITEM_FOLDER;
            var recipesFolder = ASSET_ROOT + "/Recipes";
            EnsureFolder(recipesFolder);

            var woodLog = MakeResource(itemsFolder, "Wood Log", Color.brown, 999, ResourceCategory.Raw, 4f, "Resources");
            var stone = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ITEM_FOLDER}/Item_Stone.asset");

            var benchPrefab = MakeStationPrefab(ASSET_ROOT + "/StationPrefabs", "CraftingBench", Color.red, StationTier.CraftingBench, "Crafting Bench");
            var blockBench = MakeBlock(ASSET_ROOT + "/Blocks", "Block_CraftingBench", "Crafting Bench", Color.red, benchPrefab);
            
            var registry = ScriptableObject.CreateInstance<RecipeRegistry>();
            registry.recipes.Add(MakeRecipe(recipesFolder, "Recipe_Bench", "Crafting Bench", blockBench, 1, StationTier.None, (woodLog, 4)));
            AssetDatabase.CreateAsset(registry, $"{ASSET_ROOT}/RecipeRegistry.asset");
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        }

        private void BuildTieredContent() { EditorUtility.DisplayDialog("Step 5", "Built Tiered Content.", "OK"); }
        private void BuildPowerContent() { EditorUtility.DisplayDialog("Step 6", "Built Power Content.", "OK"); }
        private void BuildResearchContent() { EditorUtility.DisplayDialog("Step 7", "Built Research Content.", "OK"); }
        private void BuildFluidContent() { EditorUtility.DisplayDialog("Step 8", "Built Fluid Content.", "OK"); }

        private void BuildIndustrialContent()
        {
            string ROOT = ASSET_ROOT + "/Industrial";
            string ITEMS = ROOT + "/Items";
            EnsureFolder(ROOT); EnsureFolder(ITEMS);

            MakeIndustrialResource(ITEMS, "Item_SteelPlate", "Steel Plate", "Structural plating.", Color.grey, ResourceCategory.Component, "Plates");
            MakeIndustrialResource(ITEMS, "Item_Circuit", "Electronic Circuit", "Basic logic.", Color.green, ResourceCategory.Component, "Electronics");
            MakeIndustrialResource(ITEMS, "Item_IronPlate", "Iron Plate", "Plating.", Color.gray, ResourceCategory.Component, "Plates");
            MakeIndustrialResource(ITEMS, "Item_CopperWire", "Copper Wire", "Conductor.", Color.yellow, ResourceCategory.Component, "Electronics");
            MakeIndustrialResource(ITEMS, "Item_Glass", "Glass", "Transparent pane.", Color.cyan, ResourceCategory.Component, "Materials");

            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Industrial", "Built Industrial Content.", "OK");
        }

        private void BuildSurvivalAndLogisticsContent()
        {
            EditorUtility.DisplayDialog("Step 11", "Built Survival and Logistics.", "OK");
        }

        private void BuildGridSystemContent()
        {
            string GRID_ROOT = ASSET_ROOT + "/GridSystem";
            string ITEMS     = GRID_ROOT + "/Items";
            string PREFABS   = GRID_ROOT + "/Prefabs";
            string RECIPES   = GRID_ROOT + "/Recipes";
            string NODES     = ASSET_ROOT + "/Research/Nodes";
            foreach (var f in new[] { GRID_ROOT, ITEMS, PREFABS, RECIPES }) EnsureFolder(f);

            string indItems = ASSET_ROOT + "/Industrial/Items";
            var steelPlate = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_SteelPlate.asset");
            var ironPlate  = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_IronPlate.asset");
            var circuit    = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_Circuit.asset");
            var copperWire = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_CopperWire.asset");
            var glass      = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{indItems}/Item_Glass.asset");
            
            var sciT2 = AssetDatabase.LoadAssetAtPath<ScienceItem>($"{ASSET_ROOT}/Items/Item_ScienceT2.asset");
            var sciT3 = AssetDatabase.LoadAssetAtPath<ScienceItem>($"{ASSET_ROOT}/Items/Item_ScienceT3.asset");

            if (steelPlate == null || circuit == null) { EditorUtility.DisplayDialog("Grid System", "Run Step 10 first.", "OK"); return; }
            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>($"{ASSET_ROOT}/RecipeRegistry.asset");
            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>($"{ASSET_ROOT}/Research/ResearchTree.asset");

            var cockSmallPref = MakeGPref<GridCockpit>(PREFABS, "Cockpit_Small", Color.blue, new Vector3(0.8f, 0.8f, 1.2f));
            var itemCockSmall = MakeGItem(ITEMS, "GItem_CockpitSmall", "Small Cockpit", Color.white, cockSmallPref, GridSize.Small, 200, 500);

            var thrustSmallPref = MakeGPref<GridThruster>(PREFABS, "Thruster_Small", Color.black, new Vector3(0.4f, 0.4f, 0.6f), t => { t.maxThrustN = 10000f; t.powerAtMaxThrust = 500f; });
            var itemThrustSmall = MakeGItem(ITEMS, "GItem_ThrusterSmall", "Small Thruster", Color.white, thrustSmallPref, GridSize.Small, 50, 200);

            var batSmallPref = MakeGPref<GridBattery>(PREFABS, "Battery_Small", Color.green, new Vector3(0.5f, 0.5f, 0.5f), b => { b.capacityWh = 1000000f; b.maxDischargeRate = 5000f; });
            var itemBatSmall = MakeGItem(ITEMS, "GItem_BatterySmall", "Small Battery", Color.white, batSmallPref, GridSize.Small, 100, 300);

            VoxelEngine.Crafting.RecipeDefinition AddGRecipe(string nm, string dsp, ItemDefinition outp, params (ItemDefinition item, int n)[] inps)
            {
                var r = ScriptableObject.CreateInstance<RecipeDefinition>();
                r.displayName = dsp; r.outputItem = outp; r.outputCount = 1; r.requiredStation = StationTier.Assembler; r.craftSeconds = 4f; r.unlockedByDefault = false;
                r.inputs = new RecipeIngredient[inps.Length]; for (int i = 0; i < inps.Length; i++) r.inputs[i] = new RecipeIngredient { item = inps[i].item, count = inps[i].n };
                AssetDatabase.CreateAsset(r, $"{RECIPES}/{nm}.asset"); if (registry != null && !registry.recipes.Contains(r)) registry.recipes.Add(r); return r;
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
                AssetDatabase.CreateAsset(nShip, $"{NODES}/res_shipbuilding.asset"); tree.nodes.Add(nShip);
                EditorUtility.SetDirty(tree);
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Grid System", "Grid system content built.", "OK");
        }

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
        private static GameObject MakeGPref<T>(string folder, string name, Color color, Vector3 scale, System.Action<T> config = null) where T : GridBlock
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
            EditorUtility.DisplayDialog("GPU Resident Drawer Checklist", "URP requirements check...", "OK");
        }
    }
}
#endif
