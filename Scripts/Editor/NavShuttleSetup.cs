// Assets/Scripts/VoxelEngine/Editor/NavShuttleSetup.cs
//
// Step 66 — the REFUEL CONNECTOR, its Auto-Run Pilot research node, and the ship-side autopilot
// authority (9.35.0-dev). Its own file rather than another method on the engine-room setup, because
// this round is navigation and not atmosphere, and the only shared parts worth pulling out of that
// file are three tiny folder/item helpers — duplicated here on purpose rather than reached into as
// internals of a class that has its own step numbers to protect.
//
// Non-destructive, like every other authored step: create what is missing, re-point what exists, and
// never touch a number a designer has already set. An existing pad keeps its mass, HP, watts and
// litres per second; an existing item keeps its stack, mass and HP; an existing recipe keeps its
// station, craft time and ingredient list; and an existing research node keeps its cost. A second run
// creates nothing.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    public static class NavShuttleSetup
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";
        private const string GRID_ROOT  = ASSET_ROOT + "/GridSystem";
        private const string PREFABS    = GRID_ROOT + "/Prefabs";
        private const string MATS       = PREFABS + "/Mats";
        private const string ITEMS      = GRID_ROOT + "/Items";
        private const string RECIPES    = GRID_ROOT + "/Recipes";
        private const string NODES      = ASSET_ROOT + "/Research/Nodes";
        private const string TREE_PATH  = ASSET_ROOT + "/Research/ResearchTree.asset";
        private const string NODE_PATH  = NODES + "/res_auto_run_pilot.asset";
        private const string UTIL_PATH  = NODES + "/res_grid_utilities.asset";
        private const string CATALOG    = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        [MenuItem("Tools/Voxel Engine/Setup Step 66 — Refuel Connector & Auto-Run")]
        public static void RunStep66Menu() => RunStep66();

        public static void RunStep66()
        {
            Debug.Log("[NavShuttleSetup] Step 66 — refuel connector & auto-run started.");

            foreach (var f in new[] { GRID_ROOT, PREFABS, MATS, ITEMS, RECIPES, NODES })
                EnsureFolder(f);

            var ironPlate  = FindItem("Item_IronPlate");
            var steelPlate = FindItem("Item_SteelPlate");
            var circuit    = FindItem("Item_Circuit");
            var rubber     = FindItem("Item_Rubber");
            var copperWire = FindItem("Item_CopperWire");

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            var utilNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(UTIL_PATH);

            int created = 0, preserved = 0;
            var recipes = new List<RecipeDefinition>();
            var items = new List<ItemDefinition>();

            AuthorConnector(GridSize.Large, "Grid_RefuelConnector_Large", "gitem_refuelconnector_large",
                "Refuel Connector", 88f, 380f, 24000f, 40f, true,
                new (ItemDefinition, int)[] { (steelPlate, 4), (ironPlate, 3), (circuit, 2) },
                rubber, copperWire, ref created, ref preserved, recipes, items);

            AuthorConnector(GridSize.Small, "Grid_RefuelConnector_Small", "gitem_refuelconnector_small",
                "Fuel Hatch (Small)", 21f, 130f, 4800f, 12f, false,
                new (ItemDefinition, int)[] { (ironPlate, 2), (circuit, 1) },
                null, null, ref created, ref preserved, recipes, items);

            foreach (var recipe in recipes)
            {
                if (recipe == null || registry == null) continue;
                if (!registry.recipes.Contains(recipe))
                {
                    registry.recipes.Add(recipe);
                    EditorUtility.SetDirty(registry);
                }
            }

            EnsureAutoRunNode(recipes, utilNode, ref created, ref preserved);
            EnsureItemsPersisted(items);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[NavShuttleSetup] Step 66 complete — created " + created + ", preserved " + preserved + ".");
            EditorUtility.DisplayDialog("Voxel Engine — Refuel Connector & Auto-Run (Step 66)",
                "Shuttle plumbing authored.\n\n" +
                "• Assets created: " + created + " (" + preserved + " existing preserved)\n" +
                "• REFUEL CONNECTOR (large) — a named waymark with a magnetic lock: a mated ship takes the\n" +
                "  full rate, a hovering one takes 45 percent, and the panel says which\n" +
                "• FUEL HATCH (Small) — soft capture only, no lock, deliberately slower: the fitting a\n" +
                "  deckhouse or a small craft can actually afford\n" +
                "• AUTO-RUN PILOT research node, hung under Grid Utilities, holding both recipes\n" +
                "• Nothing in an existing pad was rewritten — mass, HP, watts and litres per second you\n" +
                "  have tuned stay exactly as you left them\n\n" +
                "Runtime: name the pad on its panel. That name is the waymark a shuttle is flown to, and it\n" +
                "is saved with the block, so a reload keeps every schedule pointed at the same fitting.\n\n" +
                "A pad on a ship tracks that ship. If the pad is destroyed, the route keeps the last place it\n" +
                "saw it and says so — a frozen destination beats a crashed one.", "OK");
        }

        // ── The auto-run gate ────────────────────────────────────────────────
        /// <summary>A shuttle is a machine, so it is earned: one node, one prerequisite, and the two
        /// connector recipes under it. A node that already exists keeps its cost and its label — the
        /// step only fills the two fields that must be right for the tree to draw and the recipes to
        /// unlock, and appends to its unlock list rather than replacing it.</summary>
        static void EnsureAutoRunNode(List<RecipeDefinition> recipes, ResearchNode utilNode,
            ref int created, ref int preserved)
        {
            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(TREE_PATH);
            var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(NODE_PATH);
            bool newNode = node == null;
            if (newNode)
            {
                node = ScriptableObject.CreateInstance<ResearchNode>();
                AssetDatabase.CreateAsset(node, NODE_PATH);
                created++;
            }
            else preserved++;

            node.nodeId = "res_auto_run_pilot";
            if (string.IsNullOrEmpty(node.displayName) || node.displayName == "New Research")
                node.displayName = "Auto-Run Pilot";
            if (string.IsNullOrEmpty(node.description))
                node.description = "Let a fitted route recorder command the helm: named waymarks, a refuel pad to return to, a target to leave at, and a loop that stops when the numbers say stop.";
            if (node.category == ResearchCategory.Environment && node.subCategory == ResearchSubCategory.General)
            {
                node.subCategory = ResearchSubCategory.Logistics;
                node.tier = 3;
                node.iconTint = new Color(0.55f, 0.85f, 0.70f);
            }
            if (node.researchSeconds <= 0.01f) node.researchSeconds = 90f;

            if (utilNode != null)
            {
                var pre = new List<ResearchNode>(node.prerequisites ?? new ResearchNode[0]);
                if (!pre.Contains(utilNode)) { pre.Add(utilNode); node.prerequisites = pre.ToArray(); }
            }

            var unlocks = new List<RecipeDefinition>(node.unlocksRecipes ?? new RecipeDefinition[0]);
            bool changed = false;
            foreach (var r in recipes)
                if (r != null && !unlocks.Contains(r)) { unlocks.Add(r); changed = true; }
            if (changed) node.unlocksRecipes = unlocks.ToArray();

            EditorUtility.SetDirty(node);
            if (tree != null && !tree.nodes.Contains(node))
            {
                tree.nodes.Add(node);
                EditorUtility.SetDirty(tree);
            }
        }

        // ── The pad itself ───────────────────────────────────────────────────
        static void AuthorConnector(GridSize size, string prefabName, string itemId,
            string displayName, float mass, float hp, float powerWatts, float litresPerSecond,
            bool offersLock, (ItemDefinition item, int count)[] inputs,
            ItemDefinition sealIngredient, ItemDefinition wireIngredient,
            ref int created, ref int preserved,
            List<RecipeDefinition> recipes, List<ItemDefinition> items)
        {
            string prefabPath = PREFABS + "/" + prefabName + ".prefab";
            bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            var root = existing ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(prefabName);
            root.name = prefabName;

            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                    Object.DestroyImmediate(child.gameObject);
            }

            int matIdx = 0;
            GridBlockMeshBuilder.MaterialPersister = (mat, _) =>
            {
                string mp = $"{MATS}/{prefabName}_{matIdx++}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(mp) != null) AssetDatabase.DeleteAsset(mp);
                AssetDatabase.CreateAsset(mat, mp);
                return AssetDatabase.LoadAssetAtPath<Material>(mp);
            };

            var visuals = new GameObject("Generated_Visuals");
            visuals.transform.SetParent(root.transform, false);
            try
            {
                GridBlockMeshBuilder.Build(visuals, GridBlockMeshBuilder.Style.RefuelConnector, size,
                    new Color(0.42f, 0.50f, 0.46f));
            }
            finally { GridBlockMeshBuilder.MaterialPersister = null; }

            float cs = size.CellSize();
            var col = root.GetComponent<BoxCollider>();
            if (col == null) col = root.AddComponent<BoxCollider>();
            // Deck plate and stub only: a pad you have to walk past must not be a wall.
            col.center = new Vector3(0f, -cs * 0.18f, 0f);
            col.size = new Vector3(cs * 0.78f, cs * 0.62f, cs * 0.78f);

            var connector = root.GetComponent<VoxelEngine.Navigation.GridConnectorBlock>();
            if (connector == null) connector = root.AddComponent<VoxelEngine.Navigation.GridConnectorBlock>();
            connector.blockName = displayName;
            if (connector.powerWatts <= 0.01f) connector.powerWatts = powerWatts;
            if (connector.gasLitresPerSecond <= 0.01f) connector.gasLitresPerSecond = litresPerSecond;
            if (connector.captureRadiusCells <= 0.5f) connector.captureRadiusCells = size == GridSize.Large ? 6f : 4f;
            if (size == GridSize.Large) connector.offersMagneticLock = offersLock;
            if (connector.BlockMass <= 0f) connector.BlockMass = mass;
            if (connector.maxHP <= 0f) connector.maxHP = hp;

            var prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);

            string itemPath = ITEMS + "/GItem_" + prefabName.Replace("Grid_", string.Empty) + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(itemPath);
            bool newItem = item == null;
            if (newItem)
            {
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                AssetDatabase.CreateAsset(item, itemPath);
                created++;
            }
            else preserved++;

            item.itemId = itemId;
            item.displayName = displayName;
            item.description = size == GridSize.Large
                ? "A named waymark with a magnetic lock. A ship that flies here is served one at a time, and power, hydrogen, liquid fuel and cargo cross between the two grids at this fitting's rating. The ship decides when it has had enough — the pad only serves."
                : "A soft-capture hatch for a deckhouse or a small craft: the same queue and the same services as a full connector, at less than half the rate, because nothing is mated and a flange that is not bolted cannot pump.";
            item.iconTint = new Color(0.52f, 0.70f, 0.58f);
            if (item.maxStack <= 0) item.maxStack = 99;
            if (item.massPerUnit <= 0f) item.massPerUnit = size == GridSize.Large ? 1.8f : 0.5f;
            item.category = "Grid";
            item.gridSize = size;
            item.blockPrefab = prefabAsset;
            if (item.blockMass <= 0f) item.blockMass = mass;
            if (item.blockHP <= 0f) item.blockHP = hp;
            EditorUtility.SetDirty(item);
            items.Add(item);

            string recipePath = RECIPES + "/Recipe_" + prefabName.Replace("Grid_", string.Empty) + ".asset";
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recipePath);
            bool newRecipe = recipe == null;
            if (newRecipe)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, recipePath);
                created++;
            }
            else preserved++;

            recipe.displayName = displayName;
            recipe.outputItem = item;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            if (newRecipe)
            {
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = size == GridSize.Large ? 7f : 3.5f;
                recipe.unlockedByDefault = false;
            }
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var list = new List<RecipeIngredient>();
                foreach (var (ing, count) in inputs)
                    if (ing != null) list.Add(new RecipeIngredient { item = ing, count = count });
                // A pad without a seal is a leak with a name, and copper is what carries the charge —
                // both are optional content in this build order, so they are added only if they exist.
                if (sealIngredient != null) list.Add(new RecipeIngredient { item = sealIngredient, count = 2 });
                if (wireIngredient != null) list.Add(new RecipeIngredient { item = wireIngredient, count = 2 });
                recipe.inputs = list.ToArray();
            }
            EditorUtility.SetDirty(recipe);
            recipes.Add(recipe);
        }

        // ── Shared helpers, kept local so this step owns its own behaviour ───
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash <= 0) return;
            string parent = path.Substring(0, slash), leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, leaf);
        }

        static ItemDefinition FindItem(string assetName)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { ASSET_ROOT }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == assetName)
                    return AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
            }
            return null;
        }

        static void EnsureItemsPersisted(List<ItemDefinition> items)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(CATALOG);
            if (catalog == null)
            {
                EnsureFolder("Assets/Resources");
                EnsureFolder("Assets/Resources/VoxelEngine");
                catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                AssetDatabase.CreateAsset(catalog, CATALOG);
            }
            bool changed = false;
            foreach (var item in items)
                if (item != null && !catalog.items.Contains(item)) { catalog.items.Add(item); changed = true; }
            if (changed) EditorUtility.SetDirty(catalog);
        }
    }
}
