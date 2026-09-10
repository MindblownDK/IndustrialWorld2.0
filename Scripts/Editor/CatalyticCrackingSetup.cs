// Assets/Scripts/VoxelEngine/Editor/CatalyticCrackingSetup.cs
//
// Step 71 — CATALYTIC CRACKING & DOWNSTREAM PETROCHEMICALS (9.40.0-dev):
// Authors the high-temperature Catalytic Cracker & Continuous Reformer unit,
// advanced catalyst items (Zeolite, Platinum Pellet), downstream polymer/lubricant
// items (Synthetic Resin, Industrial Lubricant), conversion processing recipes,
// block item, craft recipe, and research node.
//
// Non-destructive, idempotent, and safe across rebuilds.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Industrial;
using VoxelEngine.Items;
using VoxelEngine.Research;
using VoxelEngine.Transport;

namespace VoxelEngine.EditorTools
{
    public static class CatalyticCrackingSetup
    {
        private const string ASSET_ROOT     = "Assets/VoxelEngineAssets";
        private const string INDUSTRIAL     = ASSET_ROOT + "/Industrial";
        private const string ITEMS_FOLDER   = INDUSTRIAL + "/Items";
        private const string PROC_FOLDER    = INDUSTRIAL + "/ProcessingRecipes";
        private const string PREFABS_FOLDER = INDUSTRIAL + "/Prefabs";
        private const string BLOCKS_FOLDER  = INDUSTRIAL + "/Blocks";
        private const string RECIPES_ROOT   = ASSET_ROOT + "/Recipes";
        private const string NODES          = ASSET_ROOT + "/Research/Nodes";
        private const string TREE_PATH      = ASSET_ROOT + "/Research/ResearchTree.asset";
        private const string CATALOG        = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        // Paths for new assets
        private const string CRACKER_PREFAB_PATH = PREFABS_FOLDER + "/CatalyticCracker.prefab";
        private const string CRACKER_ITEM_PATH   = BLOCKS_FOLDER + "/Block_CatalyticCracker.asset";
        private const string CRACKER_RECIPE_PATH = RECIPES_ROOT + "/Recipe_CatalyticCracker.asset";
        private const string CRACKER_NODE_PATH   = NODES + "/res_catalytic_cracking.asset";

        // Downstream Items
        private const string ZEOLITE_ITEM_PATH   = ITEMS_FOLDER + "/Item_ZeoliteCatalyst.asset";
        private const string PLATINUM_ITEM_PATH  = ITEMS_FOLDER + "/Item_PlatinumCatalyst.asset";
        private const string RESIN_ITEM_PATH     = ITEMS_FOLDER + "/Item_SyntheticResin.asset";
        private const string LUBE_ITEM_PATH      = ITEMS_FOLDER + "/Item_IndustrialLubricant.asset";

        // Processing Recipes
        private const string PROC_FCC_PATH       = PROC_FOLDER + "/Proc_FluidCatalyticCracking.asset";
        private const string PROC_CCR_PATH       = PROC_FOLDER + "/Proc_CatalyticReforming.asset";
        private const string PROC_HYDRO_PATH     = PROC_FOLDER + "/Proc_Hydrocracking.asset";
        private const string PROC_RESIN_PATH     = PROC_FOLDER + "/Proc_SyntheticResin.asset";
        private const string PROC_LUBE_PATH      = PROC_FOLDER + "/Proc_IndustrialLubricant.asset";
        private const string PROC_ZEOLITE_PATH   = PROC_FOLDER + "/Proc_ZeoliteCatalyst.asset";
        private const string PROC_PLATINUM_PATH  = PROC_FOLDER + "/Proc_PlatinumCatalyst.asset";

        // ════════════════════════════════════════════════════════════════
        //  SENTINEL-AWARE AUTHORING
        //
        //  `ItemDefinition` initialises its fields to a real item, not to empty:
        //      itemId = "iron_ore", displayName = "Iron Ore", maxStack = 900, massPerUnit = 1f
        //  and `ResearchNode` initialises displayName = "New Research", researchSeconds = 30f.
        //  An `IsNullOrEmpty(displayName)` or `maxStack <= 0` guard is therefore NEVER true on a
        //  freshly created asset, so the catalysts, the polymer, the lubricant and the cracker
        //  block all shipped displaying as "Iron Ore" at 1 kg in stacks of 900. Treat the
        //  initialiser value as "not authored yet" and repair it on a re-run.
        // ════════════════════════════════════════════════════════════════

        private const string ITEM_NAME_SENTINEL    = "Iron Ore";
        private const int    ITEM_STACK_SENTINEL   = 900;
        private const float  ITEM_MASS_SENTINEL    = 1f;
        private const float  NODE_SECONDS_SENTINEL = 30f;

        private static bool UnsetName(string value)
            => string.IsNullOrWhiteSpace(value) || value == ITEM_NAME_SENTINEL;

        private static bool UnsetStack(int value)
            => value <= 0 || value == ITEM_STACK_SENTINEL;

        private static bool UnsetMass(float value)
            => value <= 0f || Mathf.Approximately(value, ITEM_MASS_SENTINEL);

        private static bool UnsetResearchSeconds(float value)
            => value <= 0.01f || Mathf.Approximately(value, NODE_SECONDS_SENTINEL);

        [MenuItem("Tools/Voxel Engine/Run Step 71 (Catalytic Cracking & Petrochemicals)", priority = 71)]
        public static void RunStep71Menu() => RunStep71();

        public static void RunStep71()
        {
            Debug.Log("[CatalyticCrackingSetup] Step 71 — Catalytic Cracking & Petrochemicals started.");

            foreach (var f in new[] { INDUSTRIAL, ITEMS_FOLDER, PROC_FOLDER, PREFABS_FOLDER, BLOCKS_FOLDER, RECIPES_ROOT, NODES })
                EnsureFolder(f);

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            int created = 0, preserved = 0;

            // ── 1) Downstream & Catalyst Items ──────────────────────────────
            var zeolite = GetOrCreate<ResourceItem>(ZEOLITE_ITEM_PATH, ref created, ref preserved);
            zeolite.itemId = "item_zeolite_catalyst";
            if (UnsetName(zeolite.displayName)) zeolite.displayName = "Zeolite Catalyst";
            zeolite.description = "Microporous aluminosilicate catalyst pellet required for fluid catalytic cracking, cracking heavy residues into light diesel and gasoline.";
            zeolite.iconTint = new Color(0.85f, 0.82f, 0.72f);
            if (UnsetStack(zeolite.maxStack)) zeolite.maxStack = 100;
            if (UnsetMass(zeolite.massPerUnit)) zeolite.massPerUnit = 0.5f;
            zeolite.category = "Components";
            zeolite.subcategory = ResourceCategory.Component;
            EditorUtility.SetDirty(zeolite);

            var platCat = GetOrCreate<ResourceItem>(PLATINUM_ITEM_PATH, ref created, ref preserved);
            platCat.itemId = "item_platinum_catalyst";
            if (UnsetName(platCat.displayName)) platCat.displayName = "Platinum Catalyst";
            platCat.description = "Noble metal catalyst pellet for high-severity continuous catalytic reforming and hydrocracking.";
            platCat.iconTint = new Color(0.82f, 0.88f, 0.95f);
            if (UnsetStack(platCat.maxStack)) platCat.maxStack = 100;
            if (UnsetMass(platCat.massPerUnit)) platCat.massPerUnit = 0.8f;
            platCat.category = "Components";
            platCat.subcategory = ResourceCategory.Component;
            EditorUtility.SetDirty(platCat);

            var resin = GetOrCreate<ResourceItem>(RESIN_ITEM_PATH, ref created, ref preserved);
            resin.itemId = "item_synthetic_resin";
            if (UnsetName(resin.displayName)) resin.displayName = "Synthetic Resin";
            resin.description = "Advanced thermoset polymer resin synthesized from cracked naphtha and LPG. Used for composite armor, carbon composites, and high-tier hull parts.";
            resin.iconTint = new Color(0.40f, 0.85f, 0.75f);
            if (UnsetStack(resin.maxStack)) resin.maxStack = 50;
            if (UnsetMass(resin.massPerUnit)) resin.massPerUnit = 1.2f;
            resin.category = "Components";
            resin.subcategory = ResourceCategory.Component;
            EditorUtility.SetDirty(resin);

            var lube = GetOrCreate<ResourceItem>(LUBE_ITEM_PATH, ref created, ref preserved);
            lube.itemId = "item_industrial_lubricant";
            if (UnsetName(lube.displayName)) lube.displayName = "Industrial Lubricant";
            lube.description = "High-shear synthetic lubricant oil refined from heavy petroleum cuts. Boosts engine efficiency, mechanical gearboxes, and turbine power.";
            lube.iconTint = new Color(0.88f, 0.58f, 0.15f);
            if (UnsetStack(lube.maxStack)) lube.maxStack = 50;
            if (UnsetMass(lube.massPerUnit)) lube.massPerUnit = 1.0f;
            lube.category = "Components";
            lube.subcategory = ResourceCategory.Component;
            EditorUtility.SetDirty(lube);

            var coal = FindItem("Item_Coal");
            var stone = FindItem("Item_Stone");
            var goldIngot = FindItem("Item_GoldIngot") ?? FindItem("Item_CopperIngot");
            var ironIngot = FindItem("Item_IronIngot");

            // ── 2) Processing Recipes ───────────────────────────────────────
            var procFCC = GetOrCreate<ProcessingRecipe>(PROC_FCC_PATH, ref created, ref preserved);
            if (string.IsNullOrEmpty(procFCC.displayName))
            {
                procFCC.displayName = "Fluid Catalytic Cracking (FCC)";
                procFCC.category = "Cracking";
                procFCC.secondsPerBatch = 12f; procFCC.powerDrawMultiplier = 1.4f;
                procFCC.fluidInputs = new[]
                {
                    new FluidIO { liquid = LiquidType.HeavyFuelOil, litres = 100f },
                    new FluidIO { liquid = LiquidType.Water,        litres = 20f }
                };
                if (zeolite != null) procFCC.inputs = new[] { new ProcessingIO { item = zeolite, count = 1 } };
                procFCC.fluidOutputs = new[]
                {
                    new FluidIO { liquid = LiquidType.Diesel,   litres = 45f },
                    new FluidIO { liquid = LiquidType.Gasoline, litres = 30f },
                    new FluidIO { liquid = LiquidType.Lpg,      litres = 15f },
                    new FluidIO { liquid = LiquidType.Naphtha,  litres = 5f }
                };
                EditorUtility.SetDirty(procFCC);
            }

            var procCCR = GetOrCreate<ProcessingRecipe>(PROC_CCR_PATH, ref created, ref preserved);
            if (string.IsNullOrEmpty(procCCR.displayName))
            {
                procCCR.displayName = "Continuous Catalytic Reforming (CCR)";
                procCCR.category = "Reforming";
                procCCR.secondsPerBatch = 14f; procCCR.powerDrawMultiplier = 1.5f;
                procCCR.fluidInputs = new[] { new FluidIO { liquid = LiquidType.Naphtha, litres = 80f } };
                if (platCat != null) procCCR.inputs = new[] { new ProcessingIO { item = platCat, count = 1 } };
                procCCR.fluidOutputs = new[]
                {
                    new FluidIO { liquid = LiquidType.Gasoline, litres = 60f },
                    new FluidIO { liquid = LiquidType.Lpg,      litres = 20f }
                };
                EditorUtility.SetDirty(procCCR);
            }

            var procHydro = GetOrCreate<ProcessingRecipe>(PROC_HYDRO_PATH, ref created, ref preserved);
            if (string.IsNullOrEmpty(procHydro.displayName))
            {
                procHydro.displayName = "High-Yield Hydrocracking";
                procHydro.category = "Cracking";
                procHydro.secondsPerBatch = 16f; procHydro.powerDrawMultiplier = 1.6f;
                procHydro.fluidInputs = new[]
                {
                    new FluidIO { liquid = LiquidType.HeavyFuelOil, litres = 80f },
                    new FluidIO { liquid = LiquidType.Lpg,          litres = 20f }
                };
                if (platCat != null) procHydro.inputs = new[] { new ProcessingIO { item = platCat, count = 1 } };
                procHydro.fluidOutputs = new[]
                {
                    new FluidIO { liquid = LiquidType.Diesel,   litres = 50f },
                    new FluidIO { liquid = LiquidType.Kerosene, litres = 35f }
                };
                EditorUtility.SetDirty(procHydro);
            }

            var procResin = GetOrCreate<ProcessingRecipe>(PROC_RESIN_PATH, ref created, ref preserved);
            if (string.IsNullOrEmpty(procResin.displayName))
            {
                procResin.displayName = "Synthesise Synthetic Resin";
                procResin.category = "Plastics";
                procResin.secondsPerBatch = 10f; procResin.powerDrawMultiplier = 1.3f;
                procResin.fluidInputs = new[]
                {
                    new FluidIO { liquid = LiquidType.Naphtha, litres = 40f },
                    new FluidIO { liquid = LiquidType.Lpg,     litres = 20f }
                };
                if (coal != null) procResin.inputs = new[] { new ProcessingIO { item = coal, count = 1 } };
                if (resin != null) procResin.outputs = new[] { new ProcessingIO { item = resin, count = 3 } };
                procResin.fluidOutputs = new[] { new FluidIO { liquid = LiquidType.RefinedOil, litres = 10f } };
                EditorUtility.SetDirty(procResin);
            }

            var procLube = GetOrCreate<ProcessingRecipe>(PROC_LUBE_PATH, ref created, ref preserved);
            if (string.IsNullOrEmpty(procLube.displayName))
            {
                procLube.displayName = "Synthesise Industrial Lubricant";
                procLube.category = "Chemistry";
                procLube.secondsPerBatch = 12f; procLube.powerDrawMultiplier = 1.25f;
                procLube.fluidInputs = new[]
                {
                    new FluidIO { liquid = LiquidType.HeavyFuelOil, litres = 60f },
                    new FluidIO { liquid = LiquidType.RefinedOil,   litres = 20f }
                };
                if (lube != null) procLube.outputs = new[] { new ProcessingIO { item = lube, count = 2 } };
                procLube.fluidOutputs = new[] { new FluidIO { liquid = LiquidType.Diesel, litres = 20f } };
                EditorUtility.SetDirty(procLube);
            }

            var procZeo = GetOrCreate<ProcessingRecipe>(PROC_ZEOLITE_PATH, ref created, ref preserved);
            if (string.IsNullOrEmpty(procZeo.displayName))
            {
                procZeo.displayName = "Synthesise Zeolite Catalyst";
                procZeo.category = "Chemistry";
                procZeo.secondsPerBatch = 8f; procZeo.powerDrawMultiplier = 1.1f;
                var inList = new List<ProcessingIO>();
                if (stone != null) inList.Add(new ProcessingIO { item = stone, count = 2 });
                if (coal != null) inList.Add(new ProcessingIO { item = coal, count = 1 });
                procZeo.inputs = inList.ToArray();
                procZeo.fluidInputs = new[] { new FluidIO { liquid = LiquidType.Water, litres = 20f } };
                if (zeolite != null) procZeo.outputs = new[] { new ProcessingIO { item = zeolite, count = 4 } };
                EditorUtility.SetDirty(procZeo);
            }

            var procPlat = GetOrCreate<ProcessingRecipe>(PROC_PLATINUM_PATH, ref created, ref preserved);
            if (string.IsNullOrEmpty(procPlat.displayName))
            {
                procPlat.displayName = "Synthesise Platinum Catalyst";
                procPlat.category = "Chemistry";
                procPlat.secondsPerBatch = 12f; procPlat.powerDrawMultiplier = 1.4f;
                var inList = new List<ProcessingIO>();
                if (goldIngot != null) inList.Add(new ProcessingIO { item = goldIngot, count = 1 });
                if (ironIngot != null) inList.Add(new ProcessingIO { item = ironIngot, count = 1 });
                procPlat.inputs = inList.ToArray();
                procPlat.fluidInputs = new[] { new FluidIO { liquid = LiquidType.RefinedOil, litres = 10f } };
                if (platCat != null) procPlat.outputs = new[] { new ProcessingIO { item = platCat, count = 2 } };
                EditorUtility.SetDirty(procPlat);
            }

            // ── 3) Catalytic Cracker Prefab ─────────────────────────────────
            AuthorCatalyticCrackerPrefab(new List<ProcessingRecipe> { procFCC, procCCR, procHydro, procResin, procLube }, ref created, ref preserved);

            // ── 4) Block Item & Craft Recipe ────────────────────────────────
            var steelPlate = FindItem("Item_SteelPlate");
            var ironGear   = FindItem("Item_IronGear");
            var circuit    = FindItem("Item_Circuit");
            var glass      = FindItem("Item_Glass");
            var copperWire = FindItem("Item_CopperWire");

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CRACKER_PREFAB_PATH);

            var block = GetOrCreate<BlockItem>(CRACKER_ITEM_PATH, ref created, ref preserved);
            block.itemId = "block_catalytic_cracker";
            if (UnsetName(block.displayName)) block.displayName = "Catalytic Cracker & Reformer";
            block.description = "Heavy industrial catalytic reactor unit. Thermally cracks heavy petroleum residues (HFO) into light transportation fuels (Diesel, Gasoline, LPG) and reforms Naphtha into synthetic resins, high-octane gasoline, and industrial lubricants.";
            block.iconTint = new Color(0.85f, 0.45f, 0.18f);
            if (UnsetStack(block.maxStack)) block.maxStack = 5;
            if (UnsetMass(block.massPerUnit)) block.massPerUnit = 1200f;
            block.category = "Industrial";
            block.placedPrefab = prefabAsset;
            block.gridSize = Vector3Int.one;
            block.allowStacking = false;
            if (block.blockHealth <= 0) block.blockHealth = 2200;
            if (block.miningTier <= 0) block.miningTier = 3;
            EditorUtility.SetDirty(block);

            var recipe = GetOrCreate<RecipeDefinition>(CRACKER_RECIPE_PATH, ref created, ref preserved);
            if (string.IsNullOrEmpty(recipe.displayName)) recipe.displayName = "Catalytic Cracker & Reformer";
            recipe.outputItem = block;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            if (recipe.requiredStation == StationTier.None && recipe.craftSeconds <= 0f)
            {
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 16f;
                recipe.unlockedByDefault = false;
            }
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var list = new List<RecipeIngredient>();
                Add(list, steelPlate, 36);
                Add(list, ironGear, 20);
                Add(list, circuit, 12);
                Add(list, glass, 8);
                Add(list, copperWire, 16);
                recipe.inputs = list.ToArray();
            }
            EditorUtility.SetDirty(recipe);
            if (registry != null && !registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }

            // ── 5) Research Gate ────────────────────────────────────────────
            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(TREE_PATH);
            var distNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(NODES + "/res_atmospheric_distillation.asset");
            var flareNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(NODES + "/res_flare_disposal.asset");

            var node = GetOrCreate<ResearchNode>(CRACKER_NODE_PATH, ref created, ref preserved);
            node.nodeId = "res_catalytic_cracking";
            if (string.IsNullOrEmpty(node.displayName) || node.displayName == "New Research")
                node.displayName = "Catalytic Cracking & Petrochemicals";
            if (string.IsNullOrEmpty(node.description))
                node.description = "High-temperature fluid catalytic cracking (FCC) and continuous catalytic reforming (CCR). Unlocks the Catalytic Cracker & Reformer, catalyst syntheses, and downstream polymer/lubricant production.";
            if (node.category == ResearchCategory.Environment && node.subCategory == ResearchSubCategory.General)
            {
                node.subCategory = ResearchSubCategory.Chemistry;
                node.tier = 6;
                node.column = 5;
                node.iconTint = new Color(0.92f, 0.52f, 0.20f);
            }
            if (UnsetResearchSeconds(node.researchSeconds)) node.researchSeconds = 90f;
            if (node.maxRanks < 1) node.maxRanks = 1;
            if (node.cost == null || node.cost.Length == 0)
                node.cost = new ResearchNode.ScienceCost[0];

            var preList = new List<ResearchNode>(node.prerequisites ?? new ResearchNode[0]);
            if (distNode != null && !preList.Contains(distNode)) preList.Add(distNode);
            if (flareNode != null && !preList.Contains(flareNode)) preList.Add(flareNode);
            node.prerequisites = preList.ToArray();

            var unList = new List<RecipeDefinition>(node.unlocksRecipes ?? new RecipeDefinition[0]);
            if (!unList.Contains(recipe)) unList.Add(recipe);
            node.unlocksRecipes = unList.ToArray();
            EditorUtility.SetDirty(node);

            if (tree != null && !tree.nodes.Contains(node))
            {
                tree.nodes.Add(node);
                EditorUtility.SetDirty(tree);
            }

            // ── 6) Append Catalyst recipes to Chemical Plant ────────────────
            AppendChemPlantRecipes(new[] { procZeo, procPlat });

            AssetDatabase.SaveAssets();
            Debug.Log($"[CatalyticCrackingSetup] Step 71 complete. Created: {created}, Preserved: {preserved}");

            EditorUtility.DisplayDialog("Voxel Engine — Catalytic Cracking (Step 71)",
                $"Step 71 complete!\n\n" +
                $"• Catalytic Cracker & Reformer machine authored\n" +
                $"• 4 New Petrochemical Items (Zeolite, Platinum Catalyst, Synthetic Resin, Industrial Lubricant)\n" +
                $"• 7 Processing Recipes (FCC, CCR Reforming, Hydrocracking, Resins, Lubricants, Catalysts)\n" +
                $"• Research Node: Catalytic Cracking & Petrochemicals (Tier 6)\n" +
                $"• Created: {created}, Preserved: {preserved}", "OK");
        }

        private static GameObject AuthorCatalyticCrackerPrefab(List<ProcessingRecipe> recipes, ref int created, ref int preserved)
        {
            bool isExisting = AssetDatabase.LoadAssetAtPath<GameObject>(CRACKER_PREFAB_PATH) != null;
            var root = isExisting ? PrefabUtility.LoadPrefabContents(CRACKER_PREFAB_PATH) : new GameObject("CatalyticCracker");

            var st = root.GetComponent<CraftingStation>();
            if (st == null) st = root.AddComponent<CraftingStation>();
            st.displayName = "Catalytic Cracker & Reformer";
            st.tier = StationTier.Assembler;

            var cracker = root.GetComponent<CatalyticCracker>();
            if (cracker == null) cracker = root.AddComponent<CatalyticCracker>();
            foreach (var r in recipes)
            {
                if (r != null && !cracker.knownRecipes.Contains(r)) cracker.knownRecipes.Add(r);
            }

            var power = root.GetComponent<VoxelEngine.Power.PowerConsumer>();
            if (power == null) power = root.AddComponent<VoxelEngine.Power.PowerConsumer>();
            power.connectRadius = 2.5f;
            power.wattsPerSecond = cracker.idleWattsPerSecond;

            var pc = root.GetComponent<PortConfig>();
            if (pc == null) pc = root.AddComponent<PortConfig>();
            pc.EnsureAllFaces();

            var routing = root.GetComponent<ItemPortRouting>();
            if (routing == null) routing = root.AddComponent<ItemPortRouting>();

            var col = root.GetComponent<BoxCollider>();
            if (col == null) col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.8f, 0f);
            col.size   = new Vector3(3.2f, 3.6f, 2.6f);

            var oldVis = root.transform.Find("Visuals");
            if (oldVis != null) Object.DestroyImmediate(oldVis.gameObject);
            BuildCrackerVisuals(root.transform);

            cracker.AutoWireVisuals();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, CRACKER_PREFAB_PATH);
            if (isExisting) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);

            if (isExisting) preserved++; else created++;
            return prefab;
        }

        private static void BuildCrackerVisuals(Transform root)
        {
            var visuals = new GameObject("Visuals");
            visuals.transform.SetParent(root, false);

            var skidMat   = MakeMat(PREFABS_FOLDER, "Mat_CrackerSkid",   new Color(0.18f, 0.20f, 0.23f));
            var vesselMat = MakeMat(PREFABS_FOLDER, "Mat_CrackerVessel", new Color(0.48f, 0.50f, 0.54f));
            var heatMat   = MakeMat(PREFABS_FOLDER, "Mat_CrackerHeat",   new Color(0.85f, 0.42f, 0.12f));
            var pipeMat   = MakeMat(PREFABS_FOLDER, "Mat_CrackerPipe",   new Color(0.68f, 0.64f, 0.58f));
            var trimMat   = MakeMat(PREFABS_FOLDER, "Mat_CrackerTrim",   new Color(0.92f, 0.65f, 0.18f));
            var faceMat   = MakeMat(PREFABS_FOLDER, "Mat_CrackerFace",   new Color(0.10f, 0.11f, 0.13f));
            var needleMat = MakeMat(PREFABS_FOLDER, "Mat_CrackerNeedle", new Color(0.90f, 0.22f, 0.15f));

            // Skid foundation
            Prim(visuals.transform, PrimitiveType.Cube, "SkidBase", new Vector3(0f, 0.15f, 0f), new Vector3(3.0f, 0.3f, 2.4f), skidMat);

            // Central Main Reactor Column (FCC Reactor)
            Prim(visuals.transform, PrimitiveType.Cylinder, "ReactorColumn", new Vector3(-0.6f, 1.8f, 0f), new Vector3(1.1f, 1.5f, 1.1f), vesselMat);
            // Heat Jacket rings
            for (int r = 0; r < 4; r++)
            {
                Prim(visuals.transform, PrimitiveType.Cylinder, $"HeatRib_{r}", new Vector3(-0.6f, 0.8f + r * 0.65f, 0f), new Vector3(1.18f, 0.08f, 1.18f), heatMat);
            }

            // Regenerator Column
            Prim(visuals.transform, PrimitiveType.Cylinder, "RegeneratorColumn", new Vector3(0.7f, 1.5f, -0.2f), new Vector3(0.85f, 1.2f, 0.85f), vesselMat);

            // Overhead Fractionator manifold
            Prim(visuals.transform, PrimitiveType.Cube, "TopManifold", new Vector3(-0.6f, 3.4f, 0f), new Vector3(1.3f, 0.3f, 1.3f), trimMat);

            // Angled Transfer Riser pipe between reactor & regenerator
            var riser = Prim(visuals.transform, PrimitiveType.Cylinder, "TransferRiser", new Vector3(0.05f, 1.8f, -0.1f), new Vector3(0.22f, 0.8f, 0.22f), pipeMat);
            riser.transform.localRotation = Quaternion.Euler(0f, 0f, 40f);

            // Feed & Product Flange Headers
            Prim(visuals.transform, PrimitiveType.Cylinder, "Header_Feed", new Vector3(-1.35f, 0.8f, 0.6f), new Vector3(0.28f, 0.4f, 0.28f), heatMat);
            Prim(visuals.transform, PrimitiveType.Cylinder, "Header_Product", new Vector3(1.35f, 0.8f, 0.6f), new Vector3(0.28f, 0.4f, 0.28f), trimMat);

            // 3 Analog Dials on front panel (Z = +1.1)
            BuildDial(visuals.transform, "Gauge_Temp",    new Vector3(-0.8f, 1.1f, 1.18f), faceMat, heatMat, needleMat);
            BuildDial(visuals.transform, "Gauge_Feed",    new Vector3( 0.0f, 1.1f, 1.18f), faceMat, trimMat, needleMat);
            BuildDial(visuals.transform, "Gauge_Product", new Vector3( 0.8f, 1.1f, 1.18f), faceMat, vesselMat, needleMat);

            // Sight Glass & Internal Reactor Glow
            var sight = Prim(visuals.transform, PrimitiveType.Cylinder, "SightGlass", new Vector3(-0.6f, 1.8f, 0.56f), new Vector3(0.35f, 0.05f, 0.35f), faceMat);
            sight.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var glowGO = new GameObject("ReactorGlow");
            glowGO.transform.SetParent(visuals.transform, false);
            glowGO.transform.localPosition = new Vector3(-0.6f, 1.8f, 0.7f);
            var l = glowGO.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 3.5f;
            l.intensity = 1.5f;
            l.color = new Color(1.0f, 0.55f, 0.15f);
        }

        private static void BuildDial(Transform parent, string name, Vector3 pos, Material faceMat, Material rimMat, Material needleMat)
        {
            var dial = new GameObject(name);
            dial.transform.SetParent(parent, false);
            dial.transform.localPosition = pos;

            // Bezel rim
            var rim = Prim(dial.transform, PrimitiveType.Cylinder, "Bezel", Vector3.zero, new Vector3(0.42f, 0.03f, 0.42f), rimMat);
            rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Dial face
            var face = Prim(dial.transform, PrimitiveType.Cylinder, "Face", new Vector3(0f, 0f, 0.015f), new Vector3(0.36f, 0.02f, 0.36f), faceMat);
            face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Pivot + Needle
            var pivot = new GameObject("NeedlePivot");
            pivot.transform.SetParent(dial.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, 0.03f);
            pivot.transform.localRotation = Quaternion.Euler(0f, 0f, -135f);

            var needle = Prim(pivot.transform, PrimitiveType.Cube, "Needle", new Vector3(0f, 0.08f, 0f), new Vector3(0.02f, 0.16f, 0.01f), needleMat);
            var hub = Prim(pivot.transform, PrimitiveType.Cylinder, "Hub", Vector3.zero, new Vector3(0.06f, 0.02f, 0.06f), rimMat);
            hub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private static GameObject Prim(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            var ren = go.GetComponent<Renderer>();
            if (ren != null && mat != null) ren.sharedMaterial = mat;
            return go;
        }

        private static Material MakeMat(string folder, string name, Color color)
        {
            string path = $"{folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader s = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard")
                    ?? Shader.Find("Sprites/Default")
                    ?? Shader.Find("Unlit/Color");
            var mat = new Material(s) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void AppendChemPlantRecipes(IEnumerable<ProcessingRecipe> recipes)
        {
            string[] paths = new[]
            {
                PREFABS_FOLDER + "/StationaryChemicalPlant.prefab",
                ASSET_ROOT + "/GridSystem/Prefabs/ChemicalPlant_Large.prefab"
            };

            foreach (var p in paths)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go == null) continue;

                var root = PrefabUtility.LoadPrefabContents(p);
                var chem = root.GetComponent<StationaryChemicalPlant>();
                if (chem != null)
                {
                    bool changed = false;
                    foreach (var r in recipes)
                    {
                        if (r != null && !chem.knownRecipes.Contains(r))
                        {
                            chem.knownRecipes.Add(r);
                            changed = true;
                        }
                    }
                    if (changed) PrefabUtility.SaveAsPrefabAsset(root, p);
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static T GetOrCreate<T>(string path, ref int created, ref int preserved) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) { preserved++; return existing; }
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            created++;
            return asset;
        }

        private static ResourceItem FindItem(string name)
        {
            string[] roots = new[] { ITEMS_FOLDER, INDUSTRIAL + "/Items", ASSET_ROOT + "/Items" };
            foreach (var r in roots)
            {
                var it = AssetDatabase.LoadAssetAtPath<ResourceItem>($"{r}/{name}.asset");
                if (it != null) return it;
            }
            return null;
        }

        private static void Add(List<RecipeIngredient> list, ItemDefinition item, int count)
        {
            if (item != null && count > 0) list.Add(new RecipeIngredient { item = item, count = count });
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            string leaf   = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
