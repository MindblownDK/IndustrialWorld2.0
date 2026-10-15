#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Research;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Non-destructive Setup Wizard step (17.24.0-dev) that authors:
    ///   - Grid Cargo Drop Hatch (Small & Large Grid): auto-pulls matching items from
    ///     grid containers, 4 toggleable multi-item filters (cockpit hotbar compatible,
    ///     1 active at a time), animated clam-shell bay doors, downward world item drop.
    ///   - Grid Receiving Chute (Small & Large Grid): wide flared catch hopper mounted
    ///     on a grid that captures falling DroppedItems and forwards them to grid cargo.
    ///   - Stationary Large Receiving Chute (3x2x3): wide flared ground hopper that
    ///     captures falling DroppedItems and feeds connected belts/chutes/funnels/chests.
    /// Invoked exclusively from VoxelEngineSetupWindow (Tools -> Voxel Engine -> Setup Wizard).
    /// </summary>
    public static class DropHatchLogisticsSetup
    {
        private const string AssetRoot        = "Assets/VoxelEngineAssets";
        private const string ItemsFolder      = AssetRoot + "/Items";
        private const string RecipesFolder    = AssetRoot + "/Recipes";
        private const string MachinesFolder   = AssetRoot + "/Machines";
        private const string PrefabsFolder    = AssetRoot + "/Prefabs/Machines";
        private const string GridRoot         = AssetRoot + "/GridSystem";
        private const string GridItemsFolder  = GridRoot + "/Items";
        private const string GridPrefabsFolder= GridRoot + "/Prefabs";
        private const string GridRecipesFolder= GridRoot + "/Recipes";
        private const string ResearchFolder   = AssetRoot + "/Research";

        public static void RunStep116()
        {
            RunInternal(interactive: true);
        }

        public static void RunSilent()
        {
            RunInternal(interactive: false);
        }

        private static void RunInternal(bool interactive)
        {
            EnsureFolder(AssetRoot, "Items");
            EnsureFolder(AssetRoot, "Recipes");
            EnsureFolder(AssetRoot, "Machines");
            EnsureFolder(AssetRoot, "Prefabs");
            EnsureFolder(AssetRoot + "/Prefabs", "Machines");
            EnsureFolder(AssetRoot, "GridSystem");
            EnsureFolder(GridRoot, "Items");
            EnsureFolder(GridRoot, "Prefabs");
            EnsureFolder(GridRoot, "Recipes");

            var ironIngot  = FindItem("Item_IronIngot", "iron_ingot", "Iron Ingot");
            var copperIngot= FindItem("Item_CopperIngot", "copper_ingot", "Copper Ingot");
            var steelIngot = FindItem("Item_SteelIngot", "steel_ingot", "Steel Ingot");
            var steelPlate = FindItem("Item_SteelPlate", "steel_plate", "Steel Plate") ?? steelIngot ?? ironIngot;
            var ironPlate  = FindItem("Item_IronPlate", "iron_plate", "Iron Plate") ?? ironIngot;
            var copperWire = FindItem("Item_CopperWire", "copper_wire", "Copper Wire") ?? copperIngot;
            var gear       = FindItem("Item_Gear", "iron_gear", "Iron Gear") ?? ironIngot;

            // ── 1. Grid Cargo Drop Hatch (Small & Large) ────────────────────────
            var smallHatchPrefab = EnsureDropHatchPrefab(
                $"{GridPrefabsFolder}/GridCargoDropHatch_Small.prefab",
                "Small Cargo Drop Hatch",
                GridSize.Small,
                slots: 12,
                maxMassKg: 50_000f);

            var largeHatchPrefab = EnsureDropHatchPrefab(
                $"{GridPrefabsFolder}/GridCargoDropHatch_Large.prefab",
                "Large Cargo Drop Hatch",
                GridSize.Large,
                slots: 20,
                maxMassKg: 250_000f);

            var smallHatchItem = EnsureGridItem(
                $"{GridItemsFolder}/GItem_SmallCargoDropHatch.asset",
                "gitem_small_cargo_drop_hatch",
                "Small Cargo Drop Hatch",
                "Small-grid belly cargo drop hatch with animated clam-shell bay doors, auto-pull from grid containers, and 4 cockpit-toggleable multi-item filters (only one filter active at a time).",
                new Color(0.92f, 0.68f, 0.16f),
                smallHatchPrefab,
                GridSize.Small,
                mass: 160f,
                hp: 340f);

            var largeHatchItem = EnsureGridItem(
                $"{GridItemsFolder}/GItem_LargeCargoDropHatch.asset",
                "gitem_large_cargo_drop_hatch",
                "Large Cargo Drop Hatch",
                "Large-grid heavy cargo drop bay with hydraulic clam-shell doors, auto-pull from ship cargo holds, and 4 cockpit-toggleable multi-item export filters.",
                new Color(0.95f, 0.72f, 0.18f),
                largeHatchPrefab,
                GridSize.Large,
                mass: 520f,
                hp: 820f);

            // ── 2. Grid Receiving Chute (Small & Large) ─────────────────────────
            var smallGridChutePrefab = EnsureGridReceivingChutePrefab(
                $"{GridPrefabsFolder}/GridReceivingChute_Small.prefab",
                "Small Grid Receiving Chute",
                GridSize.Small,
                slots: 16,
                maxMassKg: 80_000f);

            var largeGridChutePrefab = EnsureGridReceivingChutePrefab(
                $"{GridPrefabsFolder}/GridReceivingChute_Large.prefab",
                "Large Grid Receiving Chute",
                GridSize.Large,
                slots: 24,
                maxMassKg: 400_000f);

            var smallGridChuteItem = EnsureGridItem(
                $"{GridItemsFolder}/GItem_SmallReceivingChute.asset",
                "gitem_small_receiving_chute",
                "Small Grid Receiving Chute",
                "Wide flared catch hopper for small grids. Captures dropped items falling from above and automatically routes them into the grid's cargo containers.",
                new Color(0.22f, 0.78f, 0.92f),
                smallGridChutePrefab,
                GridSize.Small,
                mass: 150f,
                hp: 320f);

            var largeGridChuteItem = EnsureGridItem(
                $"{GridItemsFolder}/GItem_LargeReceivingChute.asset",
                "gitem_large_receiving_chute",
                "Large Grid Receiving Chute",
                "Extra-wide flared catch hopper for large ships, barges, and stations. Captures items dropped from hovering ships above and forwards them into grid storage.",
                new Color(0.20f, 0.82f, 0.95f),
                largeGridChutePrefab,
                GridSize.Large,
                mass: 480f,
                hp: 780f);

            // ── 3. Stationary Large Receiving Chute (3x2x3) ─────────────────────
            var stationaryChutePrefab = EnsureStationaryReceivingChutePrefab(
                $"{PrefabsFolder}/ReceivingChute.prefab",
                out var stationaryChuteBlock);

            EnsureMachineDefinition(
                $"{MachinesFolder}/MachineDef_ReceivingChute.asset",
                "receiving_chute",
                "Large Receiving Chute",
                "Wide 3x2x3 flared industrial receiving hopper that catches items dropped from hovering ships above and feeds connected conveyor belts, chutes, funnels, or chests.",
                MachineCategory.Custom,
                stationaryChutePrefab);

            if (stationaryChuteBlock != null)
            {
                stationaryChuteBlock.placedPrefab = stationaryChutePrefab;
                EditorUtility.SetDirty(stationaryChuteBlock);
            }

            // ── 4. Crafting Recipes & Registry Wiring ───────────────────────────
            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>($"{AssetRoot}/RecipeRegistry.asset");

            var recSmallHatch = EnsureCraftRecipe(
                $"{GridRecipesFolder}/Recipe_SmallCargoDropHatch.asset",
                "Small Cargo Drop Hatch",
                smallHatchItem, 1, 2.5f,
                StationTier.CraftingBench, true,
                new (ItemDefinition, int)[] { (steelPlate, 4), (ironPlate, 4), (gear, 2), (copperWire, 2) },
                registry);

            var recLargeHatch = EnsureCraftRecipe(
                $"{GridRecipesFolder}/Recipe_LargeCargoDropHatch.asset",
                "Large Cargo Drop Hatch",
                largeHatchItem, 1, 4.0f,
                StationTier.CraftingBench, true,
                new (ItemDefinition, int)[] { (steelPlate, 10), (ironPlate, 6), (gear, 4), (copperWire, 4) },
                registry);

            var recSmallGridChute = EnsureCraftRecipe(
                $"{GridRecipesFolder}/Recipe_SmallReceivingChute.asset",
                "Small Grid Receiving Chute",
                smallGridChuteItem, 1, 2.5f,
                StationTier.CraftingBench, true,
                new (ItemDefinition, int)[] { (steelPlate, 4), (ironPlate, 4), (gear, 2) },
                registry);

            var recLargeGridChute = EnsureCraftRecipe(
                $"{GridRecipesFolder}/Recipe_LargeReceivingChute.asset",
                "Large Grid Receiving Chute",
                largeGridChuteItem, 1, 4.0f,
                StationTier.CraftingBench, true,
                new (ItemDefinition, int)[] { (steelPlate, 10), (ironPlate, 8), (gear, 4) },
                registry);

            var recStationaryChute = EnsureCraftRecipe(
                $"{RecipesFolder}/Recipe_ReceivingChute.asset",
                "Large Receiving Chute",
                stationaryChuteBlock, 1, 3.0f,
                StationTier.CraftingBench, true,
                new (ItemDefinition, int)[] { (steelPlate, 6), (ironPlate, 8), (gear, 2) },
                registry);

            // Also link to Factory Automation research node if present so research tree inspection shows them.
            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>($"{ResearchFolder}/ResearchTree.asset");
            if (tree != null && tree.nodes != null)
            {
                for (int i = 0; i < tree.nodes.Count; i++)
                {
                    var node = tree.nodes[i];
                    if (node == null) continue;
                    if (node.nodeId == "res_factory_automation" || node.nodeId == "factory_foundations")
                    {
                        AddRecipeUnlock(node, recStationaryChute);
                        AddRecipeUnlock(node, recSmallHatch);
                        AddRecipeUnlock(node, recLargeHatch);
                        AddRecipeUnlock(node, recSmallGridChute);
                        AddRecipeUnlock(node, recLargeGridChute);
                        EditorUtility.SetDirty(node);
                        break;
                    }
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[DropHatchLogisticsSetup] Configured Small/Large Cargo Drop Hatches, Small/Large Grid Receiving Chutes, and Stationary Large Receiving Chute (3x2x3).");
            if (interactive)
            {
                EditorUtility.DisplayDialog(
                    "Cargo Drop Hatch & Receiving Chutes Setup",
                    "Configured non-destructively:\n" +
                    "• Small & Large Grid Cargo Drop Hatch (4 toggleable multi-item filters, auto-pull from grid containers, animated clam-shell bay doors)\n" +
                    "• Small & Large Grid Receiving Chutes (wide flared hopper + auto-push to grid cargo)\n" +
                    "• Stationary Large Receiving Chute (3x2x3 wide catch hopper + belt/chute/chest export)\n" +
                    "• Crafting recipes registered in RecipeRegistry.",
                    "OK");
            }
        }

        private static GameObject EnsureDropHatchPrefab(string path, string displayName, GridSize size, int slots, float maxMassKg)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            float scale = size == GridSize.Small ? 0.5f : 2.5f;

            var root = new GameObject( System.IO.Path.GetFileNameWithoutExtension(path) );
            try
            {
                root.transform.localScale = Vector3.one * scale;
                var box = root.AddComponent<BoxCollider>();
                box.size = Vector3.one;

                var hatch = root.AddComponent<GridCargoDropHatch>();
                hatch.blockName = displayName;
                hatch.maxHP = size == GridSize.Small ? 340f : 820f;
                hatch.currentHP = hatch.maxHP;
                hatch.slots = slots;
                hatch.maxMassKg = maxMassKg;
                hatch.autoPullFromGrid = true;
                hatch.autoOpenWithFilter = true;
                hatch.EnsureFilters();
                hatch.EnsureVisuals();

                if (existing != null)
                {
                    var prev = existing.GetComponent<GridCargoDropHatch>();
                    if (prev != null)
                    {
                        if (prev.slots > 0) hatch.slots = prev.slots;
                        if (prev.maxMassKg > 0f) hatch.maxMassKg = prev.maxMassKg;
                        if (prev.maxHP > 0f) { hatch.maxHP = prev.maxHP; hatch.currentHP = prev.maxHP; }
                    }
                }

                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject EnsureGridReceivingChutePrefab(string path, string displayName, GridSize size, int slots, float maxMassKg)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            float scale = size == GridSize.Small ? 0.5f : 2.5f;

            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            try
            {
                root.transform.localScale = Vector3.one * scale;
                var box = root.AddComponent<BoxCollider>();
                box.size = Vector3.one;

                var chute = root.AddComponent<GridReceivingChute>();
                chute.blockName = displayName;
                chute.maxHP = size == GridSize.Small ? 320f : 780f;
                chute.currentHP = chute.maxHP;
                chute.slots = slots;
                chute.maxMassKg = maxMassKg;
                chute.autoPushToGridCargo = true;
                chute.EnsureVisuals();

                if (existing != null)
                {
                    var prev = existing.GetComponent<GridReceivingChute>();
                    if (prev != null)
                    {
                        if (prev.slots > 0) chute.slots = prev.slots;
                        if (prev.maxMassKg > 0f) chute.maxMassKg = prev.maxMassKg;
                        if (prev.maxHP > 0f) { chute.maxHP = prev.maxHP; chute.currentHP = prev.maxHP; }
                    }
                }

                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject EnsureStationaryReceivingChutePrefab(string prefabPath, out BlockItem blockItem)
        {
            blockItem = EnsureBlockItem(
                $"{ItemsFolder}/Block_ReceivingChute.asset",
                "block_receiving_chute",
                "Large Receiving Chute",
                "Wide 3x2x3 flared industrial receiving hopper that catches items dropped from hovering ships above and feeds connected conveyor belts, chutes, funnels, or chests.",
                new Color(0.22f, 0.78f, 0.92f),
                new Vector3Int(3, 2, 3),
                maxHp: 420);

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var root = new GameObject("ReceivingChute");
            try
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0f, 0f);
                box.size = new Vector3(2.9f, 1.95f, 2.9f);

                var pb = root.AddComponent<PlacedBlock>();
                pb.Item = blockItem;
                pb.Hp = 420;

                var ports = root.AddComponent<PortConfig>();
                ports.EnsureAllFaces();

                var chute = root.AddComponent<ReceivingChute>();
                chute.bufferSlots = 16;
                chute.maxMassKg = 100_000f;
                chute.EnsureVisuals();

                if (root.GetComponent<ItemPortRouting>() == null)
                    root.AddComponent<ItemPortRouting>();

                if (existing != null)
                {
                    var prev = existing.GetComponent<ReceivingChute>();
                    if (prev != null)
                    {
                        if (prev.bufferSlots > 0) chute.bufferSlots = prev.bufferSlots;
                        if (prev.maxMassKg > 0f) chute.maxMassKg = prev.maxMassKg;
                    }
                }

                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GridBlockItem EnsureGridItem(
            string path, string id, string displayName, string description,
            Color tint, GameObject prefab, GridSize size, float mass, float hp)
        {
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(path);
            bool created = item == null;
            if (created)
            {
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                AssetDatabase.CreateAsset(item, path);
            }
            if (created || string.IsNullOrEmpty(item.itemId)) item.itemId = id;
            if (created || string.IsNullOrEmpty(item.displayName)) item.displayName = displayName;
            if (created || string.IsNullOrEmpty(item.description)) item.description = description;
            if (created)
            {
                item.iconTint = tint;
                item.maxStack = 20;
                item.massPerUnit = Mathf.Max(2f, mass * 0.02f);
                item.category = "Grid Blocks";
                item.gridSize = size;
                item.blockMass = mass;
                item.blockHP = hp;
            }
            if (prefab != null && item.blockPrefab != prefab)
                item.blockPrefab = prefab;

            EditorUtility.SetDirty(item);
            return item;
        }

        private static BlockItem EnsureBlockItem(
            string path, string id, string displayName, string description,
            Color tint, Vector3Int footprint, int maxHp)
        {
            var item = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool created = item == null;
            if (created)
            {
                item = ScriptableObject.CreateInstance<BlockItem>();
                AssetDatabase.CreateAsset(item, path);
            }
            if (created || string.IsNullOrEmpty(item.itemId)) item.itemId = id;
            if (created || string.IsNullOrEmpty(item.displayName)) item.displayName = displayName;
            if (created || string.IsNullOrEmpty(item.description)) item.description = description;
            if (created)
            {
                item.iconTint = tint;
                item.maxStack = 50;
                item.massPerUnit = 14f;
                item.category = "Logistics";
                item.blockHealth = maxHp;
                item.miningTier = 1;
                item.gridSize = footprint;
            }
            EditorUtility.SetDirty(item);
            return item;
        }

        private static MachineDefinition EnsureMachineDefinition(
            string path, string id, string displayName, string description,
            MachineCategory cat, GameObject prefab)
        {
            var def = AssetDatabase.LoadAssetAtPath<MachineDefinition>(path);
            bool created = def == null;
            if (created)
            {
                def = ScriptableObject.CreateInstance<MachineDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }
            def.machineId = id;
            def.displayName = displayName;
            def.description = description;
            def.category = cat;
            def.prefab = prefab;
            EditorUtility.SetDirty(def);
            return def;
        }

        private static RecipeDefinition EnsureCraftRecipe(
            string path, string name, ItemDefinition output, int outCount, float craftSec,
            StationTier tier, bool unlockedByDefault, (ItemDefinition item, int count)[] inputs,
            RecipeRegistry registry)
        {
            var rec = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
            bool created = rec == null;
            if (created)
            {
                rec = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(rec, path);
            }
            if (created || string.IsNullOrEmpty(rec.displayName)) rec.displayName = name;
            rec.outputItem = output;
            if (created)
            {
                rec.outputCount = Mathf.Max(1, outCount);
                rec.craftSeconds = craftSec;
                rec.requiredStation = tier;
                rec.unlockedByDefault = unlockedByDefault;
            }
            if ((rec.inputs == null || rec.inputs.Length == 0) && inputs != null)
            {
                var list = new List<RecipeIngredient>();
                for (int i = 0; i < inputs.Length; i++)
                {
                    if (inputs[i].item != null && inputs[i].count > 0)
                        list.Add(new RecipeIngredient { item = inputs[i].item, count = inputs[i].count });
                }
                rec.inputs = list.ToArray();
            }
            EditorUtility.SetDirty(rec);

            if (registry != null && !registry.recipes.Contains(rec))
            {
                registry.recipes.Add(rec);
                EditorUtility.SetDirty(registry);
            }
            return rec;
        }

        private static void AddRecipeUnlock(ResearchNode node, RecipeDefinition rec)
        {
            if (node == null || rec == null) return;
            var list = new List<RecipeDefinition>(node.unlocksRecipes ?? System.Array.Empty<RecipeDefinition>());
            if (!list.Contains(rec))
            {
                list.Add(rec);
                node.unlocksRecipes = list.ToArray();
            }
        }

        private static ItemDefinition FindItem(string assetName, string itemId, string displayName)
        {
            var direct = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/{assetName}.asset")
                      ?? AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{GridItemsFolder}/{assetName}.asset");
            if (direct != null) return direct;

            string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (it == null) continue;
                if (it.itemId == itemId || it.displayName == displayName) return it;
            }
            return null;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string full = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(full))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
