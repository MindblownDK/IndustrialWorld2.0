// Assets/Scripts/VoxelEngine/Editor/FlareStackSetup.cs
//
// Step 70 — FLARE STACK & WASTE-HEAT RECOVERY (9.39.0-dev):
// Authors the stationary industrial Flare Stack tower, large & small Grid Flare Vents,
// recipes, block items, analog world dials, and the "Flare Disposal & Heat Recovery" research node.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Gas;
using VoxelEngine.GridSystem;
using VoxelEngine.Industrial;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Research;
using VoxelEngine.Transport;

namespace VoxelEngine.EditorTools
{
    public static class FlareStackSetup
    {
        private const string ASSET_ROOT     = "Assets/VoxelEngineAssets";
        private const string INDUSTRIAL     = ASSET_ROOT + "/Industrial";
        private const string PREFABS_FOLDER = INDUSTRIAL + "/Prefabs";
        private const string BLOCKS_FOLDER  = INDUSTRIAL + "/Blocks";
        private const string RECIPES_ROOT   = ASSET_ROOT + "/Recipes";
        private const string GRID_PREFABS   = ASSET_ROOT + "/GridSystem/Prefabs";
        private const string GRID_ITEMS     = ASSET_ROOT + "/GridSystem/Items";
        private const string GRID_MATS      = ASSET_ROOT + "/GridSystem/Materials";
        private const string NODES          = ASSET_ROOT + "/Research/Nodes";
        private const string TREE_PATH      = ASSET_ROOT + "/Research/ResearchTree.asset";
        private const string CATALOG        = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        private const string TOWER_PREFAB_PATH = PREFABS_FOLDER + "/FlareStack.prefab";
        private const string TOWER_BLOCK_PATH  = BLOCKS_FOLDER + "/Block_FlareStack.asset";
        private const string TOWER_RECIPE_PATH = RECIPES_ROOT + "/Recipe_FlareStack.asset";

        private const string GVENT_L_PREFAB    = GRID_PREFABS + "/Grid_FlareVent_Large.prefab";
        private const string GVENT_L_ITEM      = GRID_ITEMS + "/GItem_FlareVent_Large.asset";
        private const string GVENT_L_RECIPE    = RECIPES_ROOT + "/Recipe_GFlareVent_Large.asset";

        private const string GVENT_S_PREFAB    = GRID_PREFABS + "/Grid_FlareVent_Small.prefab";
        private const string GVENT_S_ITEM      = GRID_ITEMS + "/GItem_FlareVent_Small.asset";
        private const string GVENT_S_RECIPE    = RECIPES_ROOT + "/Recipe_GFlareVent_Small.asset";

        private const string NODE_PATH         = NODES + "/ResNode_FlareDisposal.asset";
        private const string PETRO_DIST_NODE   = NODES + "/ResNode_PetroleumDistillation.asset";
        private const string OIL_REFINING_NODE = NODES + "/ResNode_OilRefining.asset";

        [MenuItem("Tools/Voxel Engine/Run Step 70 (Flare Stack & Heat Recovery)", priority = 70)]
        public static void RunStep70()
        {
            int created = 0, preserved = 0;
            EnsureFolders();

            var steelPlate = FindItem("Item_SteelPlate");
            var copperWire = FindItem("Item_CopperWire");
            var ironIngot  = FindItem("Item_IronIngot");

            var towerPrefab = AuthorFlareTowerPrefab(ref created, ref preserved);
            var towerBlock  = AuthorFlareTowerBlock(towerPrefab, ref created, ref preserved);
            var towerRecipe = AuthorFlareTowerRecipe(towerBlock, steelPlate, copperWire, ironIngot, ref created, ref preserved);

            var gventLPrefab = AuthorGridFlareVentPrefab(GridSize.Large, GVENT_L_PREFAB, ref created, ref preserved);
            var gventLItem   = AuthorGridFlareVentItem(GridSize.Large, GVENT_L_ITEM, gventLPrefab, "gitem_flarevent_large", "Grid Flare Vent (Large)", ref created, ref preserved);
            var gventLRecipe = AuthorGridFlareVentRecipe(GVENT_L_RECIPE, "Grid Flare Vent (Large)", gventLItem, steelPlate, copperWire, 8, 4, ref created, ref preserved);

            var gventSPrefab = AuthorGridFlareVentPrefab(GridSize.Small, GVENT_S_PREFAB, ref created, ref preserved);
            var gventSItem   = AuthorGridFlareVentItem(GridSize.Small, GVENT_S_ITEM, gventSPrefab, "gitem_flarevent_small", "Grid Flare Vent (Small)", ref created, ref preserved);
            var gventSRecipe = AuthorGridFlareVentRecipe(GVENT_S_RECIPE, "Grid Flare Vent (Small)", gventSItem, steelPlate, copperWire, 4, 2, ref created, ref preserved);

            EnsureItemPersisted(towerBlock);
            EnsureItemPersisted(gventLItem);
            EnsureItemPersisted(gventSItem);

            WireResearchNode(towerRecipe, gventLRecipe, gventSRecipe, ref created, ref preserved);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Flare Stack (Step 70)",
                "Authored Flare Stack content:\n" +
                "  • Stationary Flare Stack tower with analog gauges & flame particles\n" +
                "  • Grid Flare Vent (Large & Small) with persisted materials\n" +
                "  • Flare Disposal & Heat Recovery research node (Tier 5)\n" +
                "  • Registered in ItemPersistenceCatalog\n\n" +
                "Created: " + created + ", Preserved: " + preserved, "OK");
        }

        private static void EnsureFolders()
        {
            EnsureFolder(ASSET_ROOT);
            EnsureFolder(INDUSTRIAL);
            EnsureFolder(PREFABS_FOLDER);
            EnsureFolder(BLOCKS_FOLDER);
            EnsureFolder(RECIPES_ROOT);
            EnsureFolder(ASSET_ROOT + "/GridSystem");
            EnsureFolder(GRID_PREFABS);
            EnsureFolder(GRID_ITEMS);
            EnsureFolder(GRID_MATS);
            EnsureFolder(NODES);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int last = path.LastIndexOf('/');
            if (last <= 0) return;
            string parent = path.Substring(0, last);
            string child  = path.Substring(last + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, child);
        }

        private static GameObject AuthorFlareTowerPrefab(ref int created, ref int preserved)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(TOWER_PREFAB_PATH);
            bool isExisting = existing != null;
            var root = isExisting ? PrefabUtility.LoadPrefabContents(TOWER_PREFAB_PATH) : new GameObject("FlareStack");

            var fs = root.GetComponent<FlareStack>();
            if (fs == null) fs = root.AddComponent<FlareStack>();
            fs.maxBurnRateLitresPerSecond = 25f;
            fs.recoveryEfficiency = 0.18f;

            var gen = root.GetComponent<PowerGenerator>();
            if (gen == null) gen = root.AddComponent<PowerGenerator>();
            gen.isOn = false;
            gen.wattsPerSecond = 0f;

            var pc = root.GetComponent<PortConfig>();
            if (pc == null) pc = root.AddComponent<PortConfig>();
            pc.EnsureAllFaces();

            var col = root.GetComponent<BoxCollider>();
            if (col == null) col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 2.75f, 0f);
            col.size   = new Vector3(2.2f, 5.5f, 2.2f);

            var oldVisuals = root.transform.Find("Visuals");
            if (oldVisuals != null) Object.DestroyImmediate(oldVisuals.gameObject);
            BuildTowerVisuals(root.transform);

            fs.AutoWireVisuals();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, TOWER_PREFAB_PATH);
            if (isExisting) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);

            if (isExisting) preserved++; else created++;
            return prefab;
        }

        private static void BuildTowerVisuals(Transform root)
        {
            var visuals = new GameObject("Visuals");
            visuals.transform.SetParent(root, false);

            var metal     = MakeMat(PREFABS_FOLDER, "Mat_FlareMetal", new Color(0.28f, 0.30f, 0.34f));
            var dark      = MakeMat(PREFABS_FOLDER, "Mat_FlareDark",  new Color(0.14f, 0.15f, 0.17f));
            var trim      = MakeMat(PREFABS_FOLDER, "Mat_FlareTrim",  new Color(0.85f, 0.45f, 0.12f));
            var gold      = MakeMat(PREFABS_FOLDER, "Mat_FlareGold",  new Color(0.95f, 0.68f, 0.18f));
            var faceMat   = MakeMat(PREFABS_FOLDER, "Mat_FlareFace",  new Color(0.10f, 0.11f, 0.13f));
            var needleMat = MakeMat(PREFABS_FOLDER, "Mat_FlareNeedle",new Color(0.88f, 0.25f, 0.18f));

            // Concrete base pad
            Prim(visuals.transform, PrimitiveType.Cube, "BasePad", new Vector3(0f, 0.15f, 0f), new Vector3(2.0f, 0.3f, 2.0f), dark);

            // 4 Lattice derrick legs
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f * Mathf.Deg2Rad;
                float rx = Mathf.Cos(angle) * 0.75f;
                float rz = Mathf.Sin(angle) * 0.75f;
                Prim(visuals.transform, PrimitiveType.Cylinder, "DerrickLeg_" + i,
                    new Vector3(rx * 0.7f, 2.7f, rz * 0.7f), new Vector3(0.12f, 2.5f, 0.12f), metal);
            }

            // Horizontal cross braces at 3 levels
            float[] levels = { 1.5f, 3.0f, 4.5f };
            for (int l = 0; l < levels.Length; l++)
            {
                float y = levels[l];
                Prim(visuals.transform, PrimitiveType.Cube, "BraceFrame_" + l,
                    new Vector3(0f, y, 0f), new Vector3(1.2f - l * 0.15f, 0.08f, 1.2f - l * 0.15f), dark);
            }

            // Central flare stack riser column
            Prim(visuals.transform, PrimitiveType.Cylinder, "FlareRiser",
                new Vector3(0f, 2.75f, 0f), new Vector3(0.42f, 2.45f, 0.42f), metal);

            // Top flare cowl & windshield ring
            Prim(visuals.transform, PrimitiveType.Cylinder, "FlareCowl",
                new Vector3(0f, 5.25f, 0f), new Vector3(0.65f, 0.25f, 0.65f), trim);

            // Waste heat recovery boiler / generator box on the side
            Prim(visuals.transform, PrimitiveType.Cube, "HeatRecoveryUnit",
                new Vector3(0.65f, 0.65f, 0f), new Vector3(0.7f, 0.7f, 0.8f), dark);
            Prim(visuals.transform, PrimitiveType.Cylinder, "RecoveryCoil",
                new Vector3(0.65f, 0.65f, 0f), new Vector3(0.5f, 0.32f, 0.5f), trim);

            // Service panel on front for analog dials
            Prim(visuals.transform, PrimitiveType.Cube, "InstrumentPanel",
                new Vector3(0f, 1.35f, 0.88f), new Vector3(1.4f, 0.75f, 0.12f), dark);

            // ── Analog Gauges on the Flare Stack: Input Feed + Burn Load ──
            BuildDial(visuals.transform, "Gauge_Input", trim, faceMat, needleMat,
                new Vector3(-0.38f, 1.35f, 0.95f), Vector3.zero);
            BuildDial(visuals.transform, "Gauge_BurnLoad", gold, faceMat, needleMat,
                new Vector3(0.38f, 1.35f, 0.95f), Vector3.zero);

            // Flare tip flame container
            var tip = new GameObject("FlareTip");
            tip.transform.SetParent(visuals.transform, false);
            tip.transform.localPosition = new Vector3(0f, 5.55f, 0f);

            var lgo = new GameObject("FlareLight");
            lgo.transform.SetParent(tip.transform, false);
            var light = lgo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.65f, 0.2f);
            light.range = 18f;
            light.intensity = 3.0f;
        }

        private static void BuildDial(Transform parent, string name, Material rimMat, Material faceMat,
            Material needleMat, Vector3 localPos, Vector3 facingEuler)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPos;
            holder.transform.localEulerAngles = facingEuler;

            Prim(holder.transform, PrimitiveType.Cylinder, "Bezel",
                Vector3.zero, new Vector3(0.54f, 0.03f, 0.54f), rimMat, new Vector3(90f, 0f, 0f));
            Prim(holder.transform, PrimitiveType.Cylinder, "Face",
                new Vector3(0f, 0f, 0.022f), new Vector3(0.46f, 0.02f, 0.46f), faceMat, new Vector3(90f, 0f, 0f));

            // Five ticks across the sweep (-135° empty on left to +135° full on right)
            for (int t = 0; t < 5; t++)
            {
                float deg = -135f + t * 67.5f;
                float rad = deg * Mathf.Deg2Rad;
                Prim(holder.transform, PrimitiveType.Cube, "Tick" + t,
                    new Vector3(Mathf.Sin(rad) * 0.17f, Mathf.Cos(rad) * 0.17f, 0.035f),
                    new Vector3(0.03f, 0.065f, 0.01f), rimMat, new Vector3(0f, 0f, deg));
            }

            var pivot = new GameObject("NeedlePivot");
            pivot.transform.SetParent(holder.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, 0.045f);

            Prim(pivot.transform, PrimitiveType.Cube, "Needle",
                new Vector3(0f, 0.09f, 0.005f), new Vector3(0.03f, 0.18f, 0.015f), needleMat);

            Prim(pivot.transform, PrimitiveType.Cylinder, "Hub",
                new Vector3(0f, 0f, 0.01f), new Vector3(0.09f, 0.012f, 0.09f), rimMat, new Vector3(90f, 0f, 0f));
        }

        private static BlockItem AuthorFlareTowerBlock(GameObject prefab, ref int created, ref int preserved)
        {
            var item = AssetDatabase.LoadAssetAtPath<BlockItem>(TOWER_BLOCK_PATH);
            bool isNew = item == null;
            if (isNew)
            {
                item = ScriptableObject.CreateInstance<BlockItem>();
                AssetDatabase.CreateAsset(item, TOWER_BLOCK_PATH);
                created++;
            }
            else preserved++;

            item.itemId = "block_flare_stack";
            item.displayName = "Flare Stack";
            item.description = "Tall derrick flare tower that safely destroys excess petroleum cuts and off-gases. Features waste-heat power recovery and analog instruments.";
            item.category = "Industrial";
            item.placedPrefab = prefab;
            item.maxStack = 5;
            item.massPerUnit = 80f;
            item.iconTint = new Color(1.0f, 0.55f, 0.15f);
            item.gridSize = Vector3Int.one;
            item.allowStacking = false;
            if (item.blockHealth <= 0) item.blockHealth = 1500;
            if (item.miningTier <= 0) item.miningTier = 3;
            EditorUtility.SetDirty(item);
            return item;
        }

        private static RecipeDefinition AuthorFlareTowerRecipe(BlockItem item, ItemDefinition steel, ItemDefinition wire, ItemDefinition iron, ref int created, ref int preserved)
        {
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(TOWER_RECIPE_PATH);
            bool isNew = recipe == null;
            if (isNew)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, TOWER_RECIPE_PATH);
                created++;
            }
            else preserved++;

            recipe.displayName = "Flare Stack";
            recipe.outputItem = item;
            recipe.outputCount = 1;
            recipe.requiredStation = StationTier.Assembler;
            recipe.craftSeconds = 12f;
            recipe.unlockedByDefault = false;

            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var list = new List<RecipeIngredient>();
                if (steel != null) list.Add(new RecipeIngredient { item = steel, count = 12 });
                if (wire != null)  list.Add(new RecipeIngredient { item = wire, count = 6 });
                if (iron != null)  list.Add(new RecipeIngredient { item = iron, count = 4 });
                recipe.inputs = list.ToArray();
            }
            EditorUtility.SetDirty(recipe);
            return recipe;
        }

        private static GameObject AuthorGridFlareVentPrefab(GridSize size, string path, ref int created, ref int preserved)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool isExisting = existing != null;
            string name = size == GridSize.Large ? "Grid_FlareVent_Large" : "Grid_FlareVent_Small";
            var root = isExisting ? PrefabUtility.LoadPrefabContents(path) : new GameObject(name);

            var flare = root.GetComponent<GridFlareStack>();
            if (flare == null) flare = root.AddComponent<GridFlareStack>();
            flare.blockName = size == GridSize.Large ? "Grid Flare Vent (Large)" : "Grid Flare Vent (Small)";

            float cs = size.CellSize();
            var col = root.GetComponent<BoxCollider>();
            if (col == null) col = root.AddComponent<BoxCollider>();
            col.center = Vector3.zero;
            col.size = Vector3.one * cs;

            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                    Object.DestroyImmediate(child.gameObject);
            }

            int matIdx = 0;
            GridBlockMeshBuilder.MaterialPersister = (mat, _) =>
            {
                string mp = $"{GRID_MATS}/{name}_{matIdx++}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(mp) != null) AssetDatabase.DeleteAsset(mp);
                AssetDatabase.CreateAsset(mat, mp);
                return AssetDatabase.LoadAssetAtPath<Material>(mp);
            };

            var visuals = new GameObject("Generated_Visuals");
            visuals.transform.SetParent(root.transform, false);

            try
            {
                GridBlockMeshBuilder.Build(visuals, GridBlockMeshBuilder.Style.FlareVent, size,
                    new Color(0.44f, 0.48f, 0.52f));
            }
            finally
            {
                GridBlockMeshBuilder.MaterialPersister = null;
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (isExisting) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);

            if (isExisting) preserved++; else created++;
            return prefab;
        }

        private static GridBlockItem AuthorGridFlareVentItem(GridSize size, string path, GameObject prefab, string itemId, string displayName, ref int created, ref int preserved)
        {
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(path);
            bool isNew = item == null;
            if (isNew)
            {
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                AssetDatabase.CreateAsset(item, path);
                created++;
            }
            else preserved++;

            item.itemId = itemId;
            item.displayName = displayName;
            item.description = "Directional flare vent that disposes of excess fuels and exhaust gases with waste-heat electrical power generation.";
            item.category = "Grid";
            item.gridSize = size;
            item.blockPrefab = prefab;
            item.iconTint = new Color(1.0f, 0.55f, 0.15f);
            item.maxStack = 99;
            item.massPerUnit = size == GridSize.Large ? 1.8f : 0.4f;
            if (item.blockMass <= 0f) item.blockMass = size == GridSize.Large ? 1800f : 200f;
            if (item.blockHP <= 0f) item.blockHP = size == GridSize.Large ? 1200f : 300f;
            EditorUtility.SetDirty(item);
            return item;
        }

        private static RecipeDefinition AuthorGridFlareVentRecipe(string path, string name, GridBlockItem item, ItemDefinition steel, ItemDefinition wire, int steelCount, int wireCount, ref int created, ref int preserved)
        {
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
            bool isNew = recipe == null;
            if (isNew)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, path);
                created++;
            }
            else preserved++;

            recipe.displayName = name;
            recipe.outputItem = item;
            recipe.outputCount = 1;
            recipe.requiredStation = StationTier.Assembler;
            recipe.craftSeconds = item.gridSize == GridSize.Large ? 6f : 3f;
            recipe.unlockedByDefault = false;

            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var list = new List<RecipeIngredient>();
                if (steel != null) list.Add(new RecipeIngredient { item = steel, count = steelCount });
                if (wire != null)  list.Add(new RecipeIngredient { item = wire, count = wireCount });
                recipe.inputs = list.ToArray();
            }
            EditorUtility.SetDirty(recipe);
            return recipe;
        }

        private static ItemDefinition FindItem(string assetName)
        {
            var direct = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Items/" + assetName + ".asset");
            if (direct != null) return direct;
            var guids = AssetDatabase.FindAssets(assetName + " t:ItemDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                var p = AssetDatabase.GUIDToAssetPath(guids[i]);
                var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
                if (it != null) return it;
            }
            return null;
        }

        private static void EnsureItemPersisted(ItemDefinition item)
        {
            if (item == null) return;
            var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(CATALOG);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                AssetDatabase.CreateAsset(catalog, CATALOG);
            }
            if (catalog.items == null) catalog.items = new List<ItemDefinition>();
            if (!catalog.items.Contains(item))
            {
                catalog.items.Add(item);
                EditorUtility.SetDirty(catalog);
            }
        }

        private static void WireResearchNode(RecipeDefinition towerRecipe, RecipeDefinition gventLRecipe, RecipeDefinition gventSRecipe, ref int created, ref int preserved)
        {
            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(TREE_PATH);
            var distNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(PETRO_DIST_NODE)
                           ?? AssetDatabase.LoadAssetAtPath<ResearchNode>(OIL_REFINING_NODE);

            var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(NODE_PATH);
            bool isNew = node == null;
            if (isNew)
            {
                node = ScriptableObject.CreateInstance<ResearchNode>();
                AssetDatabase.CreateAsset(node, NODE_PATH);
                created++;
            }
            else preserved++;

            node.nodeId = "res_flare_disposal";
            if (string.IsNullOrEmpty(node.displayName) || node.displayName == "New Research")
                node.displayName = "Flare Disposal & Heat Recovery";
            if (string.IsNullOrEmpty(node.description))
                node.description = "High-temperature thermal oxidiser stack and vent flares for safe disposal of excess fractions, off-gas, and light hydrocarbons with waste-heat electrical power generation.";

            node.subCategory = ResearchSubCategory.Chemistry;
            node.tier = 5;
            node.column = 5;
            node.iconTint = new Color(1.0f, 0.55f, 0.15f);
            if (node.researchSeconds <= 0.01f) node.researchSeconds = 60f;
            if (node.maxRanks < 1) node.maxRanks = 1;
            if (node.cost == null || node.cost.Length == 0)
                node.cost = new ResearchNode.ScienceCost[0];

            if (distNode != null)
            {
                var pre = new List<ResearchNode>(node.prerequisites ?? new ResearchNode[0]);
                if (!pre.Contains(distNode)) { pre.Add(distNode); node.prerequisites = pre.ToArray(); }
            }

            var unlocks = new List<RecipeDefinition>(node.unlocksRecipes ?? new RecipeDefinition[0]);
            if (towerRecipe != null && !unlocks.Contains(towerRecipe)) unlocks.Add(towerRecipe);
            if (gventLRecipe != null && !unlocks.Contains(gventLRecipe)) unlocks.Add(gventLRecipe);
            if (gventSRecipe != null && !unlocks.Contains(gventSRecipe)) unlocks.Add(gventSRecipe);
            node.unlocksRecipes = unlocks.ToArray();

            EditorUtility.SetDirty(node);

            if (tree != null)
            {
                if (tree.nodes == null) tree.nodes = new List<ResearchNode>();
                if (!tree.nodes.Contains(node))
                {
                    tree.nodes.Add(node);
                    EditorUtility.SetDirty(tree);
                }
            }
        }

        private static GameObject Prim(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, Vector3? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (rot.HasValue) go.transform.localRotation = Quaternion.Euler(rot.Value);
            var rend = go.GetComponent<Renderer>();
            if (rend != null && mat != null) rend.sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            return go;
        }

        private static Material MakeMat(string folder, string name, Color color)
        {
            string path = folder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
#endif
