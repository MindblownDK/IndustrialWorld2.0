// Assets/Scripts/VoxelEngine/Editor/PetroleumColumnSetup.cs
//
// Step 69 — DISTILLATION PLANT (9.38.0-dev): the dedicated petroleum plant where
// crude oil becomes the six products. The Oil Refinery is NOT the column — it
// walks back to its legacy machine and this round authors a big new block
// instead, exactly as the playtest asked: a wide plant hall that LOOKS like a
// real distillation plant, with an analog dial above every outlet and every
// inlet, each dial rimmed in its own liquid's colour and each needle driven by
// the tank it measures.
//
// The machine code half lives in Scripts/Crafting/DistillationPlant.cs.
// This step authors the content half:
//   • the plant prefab itself — wide skid, drums, two columns and a stack, the
//     six product outlets in a row with a dial above each valve, the two feed
//     inlets (crude + refined) with their dials, tank list and needle wiring;
//   • Proc_AtmosphericCut.asset — 100 L crude → the six-product ladder of the
//     design (2 LPG / 8 naphtha / 12 kerosene / 26 diesel / 18 gasoline /
//     32 heavy fuel oil; 98 L out, the 2 L off-gas deferred to the flare round);
//   • Proc_ReRunRefinedOil.asset — the conversion cut so legacy Refined Oil
//     stock stays spendable through the same plant;
//   • Proc_NaphthaPlastic.asset — the naphtha-fed plastic recipe, appended to
//     the refinery next to its legacy plastic recipe (it beats it per litre);
//   • Block_DistillationPlant + Recipe_DistillationPlant + a research node
//     (Atmospheric Distillation, requires Oil Refining);
//   • fuel-chain migration: Refine Crude Oil, Distil Heavy Fuel Oil and Distil
//     Marine Gas Oil are detached from BOTH the standing Oil Refinery and the
//     ship Refinery — the recipe assets are kept (saves that hold them keep
//     running), but no refinery makes those products any more.
//
// Non-destructive, like every other authored step:
//   • recipe assets are populated only on first creation — existing recipe
//     assets keep every tuned number;
//   • an existing plant prefab keeps its tuning; the MODEL is rebuilt only when
//     it is the older tower shape (the "ModelRev2" marker is absent), and dial
//     wiring only fills empty slots;
//   • prefab recipe lists are append-only, plus the deliberate detach of the
//     three fuel-chain recipes this round retires;
//   • existing research node costs, labels and prerequisites are preserved;
//   • asset renames (Advanced Distillation Tower → Distillation Plant) go
//     through AssetDatabase.MoveAsset, so GUIDs — and therefore every save and
//     prefab reference — survive.
// A second run creates nothing and re-links nothing twice.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    public static class PetroleumColumnSetup
    {
        private const string ASSET_ROOT     = "Assets/VoxelEngineAssets";
        private const string INDUSTRIAL     = ASSET_ROOT + "/Industrial";
        private const string PROC_FOLDER    = INDUSTRIAL + "/ProcessingRecipes";
        private const string PREFABS_FOLDER = INDUSTRIAL + "/Prefabs";
        private const string BLOCKS_FOLDER  = INDUSTRIAL + "/Blocks";
        private const string RECIPES_ROOT   = ASSET_ROOT + "/Recipes";
        private const string NODES          = ASSET_ROOT + "/Research/Nodes";
        private const string TREE_PATH      = ASSET_ROOT + "/Research/ResearchTree.asset";
        private const string CATALOG        = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";
        private const string SHIP_REFINERY  = ASSET_ROOT + "/GridSystem/Prefabs/Refinery_Large.prefab";
        private const string STAND_REFINERY = PREFABS_FOLDER + "/OilRefinery.prefab";

        // Processing recipe assets (names never change — saves and prefabs hold them).
        private const string CUT_PATH      = PROC_FOLDER + "/Proc_AtmosphericCut.asset";
        private const string RERUN_PATH    = PROC_FOLDER + "/Proc_ReRunRefinedOil.asset";
        private const string NAPHTHA_PATH  = PROC_FOLDER + "/Proc_NaphthaPlastic.asset";

        // The three retired fuel-chain recipes: detached from every refinery, kept
        // as assets so a save that already runs them keeps working.
        private static readonly string[] RETIRED_RECIPE_NAMES =
        {
            "Proc_RefineOil", "Proc_RefineHeavyFuelOil", "Proc_RefineMGO",
        };

        private const string PLANT_PREFAB_PATH = PREFABS_FOLDER + "/DistillationPlant.prefab";
        private const string PLANT_ITEM_PATH   = BLOCKS_FOLDER + "/Block_DistillationPlant.asset";
        private const string PLANT_RECIPE_PATH = RECIPES_ROOT + "/Recipe_DistillationPlant.asset";
        private const string PLANT_NODE_PATH   = NODES + "/res_atmospheric_distillation.asset";
        private const string OIL_REFINING_NODE_PATH = NODES + "/res_oil_refining.asset";

        // Pre-rename asset names (this step's own earlier output).
        private const string LEGACY_PREFAB_PATH = PREFABS_FOLDER + "/AdvancedDistillationTower.prefab";
        private const string LEGACY_ITEM_PATH   = BLOCKS_FOLDER + "/Block_AdvancedDistillationTower.asset";
        private const string LEGACY_RECIPE_PATH = RECIPES_ROOT + "/Recipe_AdvancedDistillationTower.asset";

        // Bump when the model changes shape: the prefab is rebuilt only when this
        // marker is missing, so a hand-tuned model is never thrown away twice.
        private const string MODEL_REV = "ModelRev2";

        [MenuItem("Tools/Voxel Engine/Setup Step 69 — Distillation Plant Content")]
        public static void RunStep69Menu() => RunStep69();

        public static void RunStep69()
        {
            Debug.Log("[PetroleumColumnSetup] Step 69 — distillation plant started.");

            foreach (var f in new[] { INDUSTRIAL, PROC_FOLDER, PREFABS_FOLDER, BLOCKS_FOLDER, RECIPES_ROOT, NODES })
                EnsureFolder(f);

            // Rename this step's own earlier assets (GUID-preserving, so placed
            // blocks, saves and prefab links all keep working).
            RenameOwnAsset(LEGACY_PREFAB_PATH, PLANT_PREFAB_PATH);
            RenameOwnAsset(LEGACY_ITEM_PATH,   PLANT_ITEM_PATH);
            RenameOwnAsset(LEGACY_RECIPE_PATH, PLANT_RECIPE_PATH);

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");

            int created = 0, preserved = 0;

            // ── 1) The three processing recipes ──────────────────────────────
            //    (populated only on first creation; existing assets keep tuning)
            var cut = GetOrCreate<ProcessingRecipe>(CUT_PATH, ref created, ref preserved);
            if (string.IsNullOrEmpty(cut.displayName))
            {
                cut.displayName = "Fractionate Crude Oil (Atmospheric Cut)";
                cut.category = "Distillation";
                cut.secondsPerBatch = 16f; cut.powerDrawMultiplier = 1.25f;
                cut.fluidInputs  = new[] { new FluidIO { liquid = LiquidType.CrudeOil, litres = 100f } };
                cut.fluidOutputs = new[]
                {
                    new FluidIO { liquid = LiquidType.Lpg,          litres = 2f },
                    new FluidIO { liquid = LiquidType.Naphtha,      litres = 8f },
                    new FluidIO { liquid = LiquidType.Kerosene,     litres = 12f },
                    new FluidIO { liquid = LiquidType.Diesel,       litres = 26f },
                    new FluidIO { liquid = LiquidType.Gasoline,     litres = 18f },
                    new FluidIO { liquid = LiquidType.HeavyFuelOil, litres = 32f },
                };
                EditorUtility.SetDirty(cut);
            }

            var rerun = GetOrCreate<ProcessingRecipe>(RERUN_PATH, ref created, ref preserved);
            if (string.IsNullOrEmpty(rerun.displayName))
            {
                rerun.displayName = "Re-Run Refined Oil (Conversion Cut)";
                rerun.category = "Distillation";
                rerun.secondsPerBatch = 18f; rerun.powerDrawMultiplier = 1.3f;
                rerun.fluidInputs  = new[] { new FluidIO { liquid = LiquidType.RefinedOil, litres = 100f } };
                rerun.fluidOutputs = new[]
                {
                    new FluidIO { liquid = LiquidType.Naphtha,  litres = 15f },
                    new FluidIO { liquid = LiquidType.Kerosene, litres = 20f },
                    new FluidIO { liquid = LiquidType.Diesel,   litres = 40f },
                    new FluidIO { liquid = LiquidType.Gasoline, litres = 15f },
                };
                EditorUtility.SetDirty(rerun);
            }

            // Coal + plastic resolve under either of the two item roots.
            var coal = FindItem("Item_Coal");
            var plastic = FindItem("Item_Plastic");
            ProcessingRecipe naphthaPlastic = null;
            if (coal != null && plastic != null)
            {
                naphthaPlastic = GetOrCreate<ProcessingRecipe>(NAPHTHA_PATH, ref created, ref preserved);
                if (string.IsNullOrEmpty(naphthaPlastic.displayName))
                {
                    naphthaPlastic.displayName = "Synthesise Plastic (Naphtha Feed)";
                    naphthaPlastic.category = "Plastics";
                    naphthaPlastic.secondsPerBatch = 12f; naphthaPlastic.powerDrawMultiplier = 1.2f;
                    naphthaPlastic.inputs = new[] { new ProcessingIO { item = coal, count = 1 } };
                    naphthaPlastic.outputs = new[] { new ProcessingIO { item = plastic, count = 3 } };
                    naphthaPlastic.fluidInputs = new[] { new FluidIO { liquid = LiquidType.Naphtha, litres = 30f } };
                    EditorUtility.SetDirty(naphthaPlastic);
                }
            }
            else
            {
                Debug.LogWarning("[PetroleumColumnSetup] Coal or Plastic item not found — the naphtha plastic recipe was skipped. Run the industrial content step first.");
            }

            // ── 2) The plant prefab: a plant hall, not a box ─────────────────
            var plantRoot = GetOrCreatePrefabContents(PLANT_PREFAB_PATH, out bool plantExisted);
            bool visualsRebuilt = false;
            try
            {
                var st = plantRoot.GetComponent<CraftingStation>();
                if (st == null) st = plantRoot.AddComponent<CraftingStation>();
                // Rename in place — a hand-set custom name is left alone.
                if (string.IsNullOrEmpty(st.displayName) || st.displayName == "Advanced Distillation Tower")
                    st.displayName = "Distillation Plant";
                st.tier = StationTier.Assembler;

                var plant = plantRoot.GetComponent<DistillationPlant>();
                if (plant == null) plant = plantRoot.AddComponent<DistillationPlant>();

                var power = plantRoot.GetComponent<VoxelEngine.Power.PowerConsumer>();
                if (power == null)
                {
                    power = plantRoot.AddComponent<VoxelEngine.Power.PowerConsumer>();
                    power.connectRadius = 3.0f;
                    power.wattsPerSecond = plant.idleWattsPerSecond;
                }

                // The model is rebuilt only when it is the older tower build — the
                // revision marker is what tells them apart; a tuned model is kept.
                var visuals = plantRoot.transform.Find("Visuals");
                if (visuals == null || visuals.Find(MODEL_REV) == null)
                {
                    if (visuals != null) Object.DestroyImmediate(visuals.gameObject);
                    BuildPlantVisuals(plantRoot.transform);
                    visualsRebuilt = true;

                    var box = plantRoot.GetComponent<BoxCollider>();
                    if (box == null) box = plantRoot.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 2.95f, 0.5f);
                    box.size   = new Vector3(7.4f, 6.0f, 4.8f);
                }

                plant.EnsureTanks();

                // Dial-needle wiring: fill only missing slots so a hand-tuned list
                // survives, and keep the tank map in step with the names.
                var wanted      = GaugeNamesInOrder();
                var wantedTanks = GaugeTankMap();
                if (plant.gaugeNeedles == null) plant.gaugeNeedles = new List<Transform>();
                if (plant.gaugeTankIndices == null) plant.gaugeTankIndices = new List<int>();
                for (int i = plant.gaugeNeedles.Count; i < wanted.Length; i++) plant.gaugeNeedles.Add(null);
                for (int i = plant.gaugeTankIndices.Count; i < wanted.Length; i++) plant.gaugeTankIndices.Add(wantedTanks[i]);
                for (int i = 0; i < wanted.Length && i < plant.gaugeNeedles.Count; i++)
                {
                    if (plant.gaugeNeedles[i] != null) continue;
                    var found = FindNamedInVisuals(plantRoot.transform, wanted[i]);
                    if (found != null) plant.gaugeNeedles[i] = found;
                }
                EditorUtility.SetDirty(plant);

                // Recipe list: the plant owns crude conversion.
                if (plant.knownRecipes == null) plant.knownRecipes = new List<ProcessingRecipe>();
                AppendUnique(plant.knownRecipes, cut);
                AppendUnique(plant.knownRecipes, rerun);
                EditorUtility.SetDirty(plant);

                SavePrefabContents(PLANT_PREFAB_PATH, plantRoot, plantExisted);
            }
            finally
            {
                if (plantExisted) PrefabUtility.UnloadPrefabContents(plantRoot);
                else Object.DestroyImmediate(plantRoot);
            }
            if (plantExisted) preserved++; else created++;

            // ── 3) Fuel-chain migration on BOTH refineries ───────────────────
            //    Refine Crude Oil / Distil Heavy Fuel Oil / Distil Marine Gas Oil
            //    leave the standing refinery AND the ship refinery. The recipe
            //    assets stay (old saves keep running them); the refineries keep
            //    their plastic recipes, and the standing one gains the naphtha feed.
            MigrateRefineryRecipes(STAND_REFINERY, naphthaPlastic, required: true);
            MigrateRefineryRecipes(SHIP_REFINERY,  naphthaPlastic, required: false);

            // ── 4) Block item + craft recipe + research gate ────────────────
            var steelPlate = FindItem("Item_SteelPlate");
            var ironGear   = FindItem("Item_IronGear");
            var circuit    = FindItem("Item_Circuit");
            var glass      = FindItem("Item_Glass");
            var copperWire = FindItem("Item_CopperWire");

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PLANT_PREFAB_PATH);

            var block = GetOrCreate<BlockItem>(PLANT_ITEM_PATH, ref created, ref preserved);
            // itemId is deliberately NOT renamed: it is the save key, and renaming it
            // would drop every already-placed plant out of an existing world.
            block.itemId = "block_advanced_distillation_tower";
            if (string.IsNullOrEmpty(block.displayName) || block.displayName == "Advanced Distillation Tower")
                block.displayName = "Distillation Plant";
            block.description = "The petroleum-era plant: a wide distillation hall that turns 100 L of crude into six typed "
                + "products — LPG, naphtha, kerosene, diesel, gasoline and heavy fuel oil — each held in its own tank, each "
                + "with an analog dial on the plant that moves as the tank fills. Only this block converts crude; the Oil "
                + "Refinery keeps its plastic recipes and stops making refined oil, heavy fuel oil and marine gas oil. Feeds "
                + "one liquid at a time (crude for the Atmospheric Cut, refined oil for the Re-Run cut).";
            block.iconTint = new Color(0.44f, 0.48f, 0.55f);
            if (block.maxStack <= 0) block.maxStack = 5;
            if (block.massPerUnit <= 0f) block.massPerUnit = 90f;
            block.category = "Industrial";
            block.placedPrefab = prefabAsset;
            block.gridSize = Vector3Int.one;
            block.allowStacking = false;
            if (block.blockHealth <= 0) block.blockHealth = 1800;
            if (block.miningTier <= 0) block.miningTier = 3;
            EditorUtility.SetDirty(block);

            var recipe = GetOrCreate<RecipeDefinition>(PLANT_RECIPE_PATH, ref created, ref preserved);
            if (string.IsNullOrEmpty(recipe.displayName) || recipe.displayName == "Advanced Distillation Tower")
                recipe.displayName = "Distillation Plant";
            recipe.outputItem = block;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            if (recipe.requiredStation == StationTier.None && recipe.craftSeconds <= 0f)
            {
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 14f;
                recipe.unlockedByDefault = false;
            }
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var list = new List<RecipeIngredient>();
                Add(list, steelPlate, 32);
                Add(list, ironGear, 18);
                Add(list, circuit, 10);
                Add(list, glass, 8);
                Add(list, copperWire, 14);
                recipe.inputs = list.ToArray();
            }
            EditorUtility.SetDirty(recipe);
            if (registry != null && !registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }

            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(TREE_PATH);
            var refiningNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(OIL_REFINING_NODE_PATH);
            var node = GetOrCreate<ResearchNode>(PLANT_NODE_PATH, ref created, ref preserved);
            node.nodeId = "res_atmospheric_distillation";
            if (string.IsNullOrEmpty(node.displayName) || node.displayName == "New Research")
                node.displayName = "Atmospheric Distillation";
            if (string.IsNullOrEmpty(node.description))
                node.description = "The plant hall that turns crude into six real products: LPG, naphtha, kerosene, diesel, "
                    + "gasoline and heavy fuel oil — each a typed tank, each worth carrying because the heavy end is worth "
                    + "something too. Build it on a base that already handled oil.";
            if (node.category == ResearchCategory.Environment && node.subCategory == ResearchSubCategory.General)
            {
                node.subCategory = ResearchSubCategory.Chemistry;
                node.tier = 5;
                node.column = 4;
                node.iconTint = new Color(0.62f, 0.55f, 0.40f);
            }
            if (node.researchSeconds <= 0.01f) node.researchSeconds = 75f;
            if (node.maxRanks < 1) node.maxRanks = 1;
            if (node.cost == null || node.cost.Length == 0)
                node.cost = new ResearchNode.ScienceCost[0];   // lab-time unlock, like the nav utility nodes

            // Prerequisite + unlock list, appended without replacing anything tuned.
            if (refiningNode != null)
            {
                var pre = new List<ResearchNode>(node.prerequisites ?? new ResearchNode[0]);
                if (!pre.Contains(refiningNode)) { pre.Add(refiningNode); node.prerequisites = pre.ToArray(); }
            }
            var unlocks = new List<RecipeDefinition>(node.unlocksRecipes ?? new RecipeDefinition[0]);
            if (!unlocks.Contains(recipe)) { unlocks.Add(recipe); node.unlocksRecipes = unlocks.ToArray(); }
            EditorUtility.SetDirty(node);
            if (tree != null && !tree.nodes.Contains(node))
            {
                tree.nodes.Add(node);
                EditorUtility.SetDirty(tree);
            }

            EnsureItemPersisted(block);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[PetroleumColumnSetup] Step 69 complete — created " + created + ", preserved " + preserved + ".");
            EditorUtility.DisplayDialog("Voxel Engine — Distillation Plant (Step 69)",
                "Distillation Plant authored" + (visualsRebuilt ? " (model built)." : "; existing model kept.") + "\n\n" +
                "• Created: " + created + " (" + preserved + " existing preserved)\n" +
                "• DISTILLATION PLANT — a wide plant hall: skid, drums, main column with its\n" +
                "  platform rings, stripper column, stack and pipe work. The six products leave\n" +
                "  through a row of valves along the front, each with its own analog dial above\n" +
                "  it, rimmed and hubbed in that liquid's colour; the crude and refined-oil\n" +
                "  inlets on the left end have dials too. Needles sweep as the tanks fill.\n" +
                "• ATMOSPHERIC CUT — 100 L crude -> LPG 2 / Naphtha 8 / Kerosene 12 /\n" +
                "  Diesel 26 / Gasoline 18 / Heavy Fuel Oil 32 L (98 L out, 2 L off-gas)\n" +
                "• RE-RUN REFINED OIL — conversion cut so pre-9.38 stock stays spendable\n" +
                "• Fuel-chain migration: Refine Crude Oil, Distil Heavy Fuel Oil and Distil\n" +
                "  Marine Gas Oil are detached from the standing AND the ship refinery (the\n" +
                "  recipe assets are kept for saves that already run them). Plastic stays on\n" +
                "  both; the naphtha-fed plastic recipe is added next to the legacy one.\n" +
                "• Recipe_DistillationPlant, gated by ATMOSPHERIC DISTILLATION research\n" +
                "  (tier 5, requires Oil Refining)\n" +
                "• The panel shows one analog dial per tank; dials, readouts and the progress\n" +
                "  bar update live without rebuilding the panel (no more scroll snapping)\n" +
                "• Nothing tuned was rewritten: existing recipes, prefab lists, tank numbers\n" +
                "  and dial wiring keep their values on a re-run", "OK");
        }

        // ── Plant model ──────────────────────────────────────────────────────
        /// <summary>Product outlets run left to right along the front of the plant.</summary>
        private static readonly (string label, LiquidType liquid)[] ProductOrder =
        {
            ("LPG",            LiquidType.Lpg),
            ("Naphtha",        LiquidType.Naphtha),
            ("Kerosene",       LiquidType.Kerosene),
            ("Diesel",         LiquidType.Diesel),
            ("Gasoline",       LiquidType.Gasoline),
            ("Heavy Fuel Oil", LiquidType.HeavyFuelOil),
        };

        /// <summary>Dial order on the model: crude inlet, the six products, refined inlet.</summary>
        private static string[] GaugeNamesInOrder()
        {
            var names = new string[ProductOrder.Length + 2];
            names[0] = "Gauge_CrudeFeed";
            for (int i = 0; i < ProductOrder.Length; i++) names[1 + i] = "Gauge_" + ProductOrder[i].label.Replace(" ", "");
            names[names.Length - 1] = "Gauge_RefinedFeed";
            return names;
        }

        /// <summary>Which tank each dial reads: both inlets read the feed tank (index 0).</summary>
        private static int[] GaugeTankMap()
        {
            var map = new int[ProductOrder.Length + 2];
            map[0] = 0;
            for (int i = 0; i < ProductOrder.Length; i++) map[1 + i] = 1 + i;
            map[map.Length - 1] = 0;
            return map;
        }

        private static Transform FindNamedInVisuals(Transform root, string name)
        {
            var visuals = root.Find("Visuals");
            if (visuals == null) return null;
            var gauge = visuals.Find(name);
            if (gauge == null) return null;
            return gauge.Find("NeedlePivot");
        }

        /// <summary>
        /// Builds the plant hall under a "Visuals" child: a wide skid deck, two
        /// horizontal drums with end caps, the main distillation column (dome,
        /// overhead vent, two platform rings and a ladder) with a stripper column
        /// and a small stabiliser beside it, a pipe bridge tying the tops together,
        /// a vent stack — and, along the front, the six product outlets in a row,
        /// each with a valve and its own analog dial above it. The crude and
        /// refined-oil inlets stand on the left end with dials of their own.
        /// </summary>
        private static void BuildPlantVisuals(Transform root)
        {
            var visuals = new GameObject("Visuals");
            visuals.transform.SetParent(root, false);

            var hull  = MakeMat(PREFABS_FOLDER, "Mat_PlantHull",  new Color(0.36f, 0.38f, 0.42f));
            var dark  = MakeMat(PREFABS_FOLDER, "Mat_PlantDark",  new Color(0.16f, 0.17f, 0.19f));
            var trim  = MakeMat(PREFABS_FOLDER, "Mat_PlantTrim",  new Color(0.72f, 0.55f, 0.18f));
            var inlet = MakeMat(PREFABS_FOLDER, "Mat_PlantInlet", new Color(0.88f, 0.50f, 0.12f));   // the orange feed valves
            var needleMat = MakeMat(PREFABS_FOLDER, "Mat_PlantNeedle", new Color(0.85f, 0.28f, 0.20f));
            var faceMat   = MakeMat(PREFABS_FOLDER, "Mat_PlantDialFace", new Color(0.10f, 0.11f, 0.13f));

            // One material per liquid, baked so no runtime tinting is needed. Crude
            // is lifted a touch so the dark feed reads against the dark housing.
            var feedMat = MakeMat(PREFABS_FOLDER, "Mat_Plant_CrudeFeed",
                Color.Lerp(LiquidType.CrudeOil.Color(), new Color(0.5f, 0.45f, 0.36f), 0.55f));
            var refinedMat = MakeMat(PREFABS_FOLDER, "Mat_Plant_RefinedOil", LiquidType.RefinedOil.Color());
            var liquidMats = new Dictionary<LiquidType, Material>
            {
                { LiquidType.CrudeOil,     feedMat },
                { LiquidType.RefinedOil,   refinedMat },
                { LiquidType.Lpg,          MakeMat(PREFABS_FOLDER, "Mat_Plant_LPG",          LiquidType.Lpg.Color()) },
                { LiquidType.Naphtha,      MakeMat(PREFABS_FOLDER, "Mat_Plant_Naphtha",      LiquidType.Naphtha.Color()) },
                { LiquidType.Kerosene,     MakeMat(PREFABS_FOLDER, "Mat_Plant_Kerosene",     LiquidType.Kerosene.Color()) },
                { LiquidType.Diesel,       MakeMat(PREFABS_FOLDER, "Mat_Plant_Diesel",       LiquidType.Diesel.Color()) },
                { LiquidType.Gasoline,     MakeMat(PREFABS_FOLDER, "Mat_Plant_Gasoline",     LiquidType.Gasoline.Color()) },
                { LiquidType.HeavyFuelOil, MakeMat(PREFABS_FOLDER, "Mat_Plant_HeavyFuelOil", LiquidType.HeavyFuelOil.Color()) },
            };

            // ── Skid + deck ──────────────────────────────────────────────────
            Prim(visuals.transform, PrimitiveType.Cube, "Skid",   new Vector3(0f, 0.12f, 0f), new Vector3(6.8f, 0.24f, 4.6f), dark);
            Prim(visuals.transform, PrimitiveType.Cube, "Deck",   new Vector3(0f, 0.27f, 0f), new Vector3(6.4f, 0.08f, 4.2f), hull);
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? -1.95f : 1.95f;
                Prim(visuals.transform, PrimitiveType.Cube, "Railing" + side,
                    new Vector3(0f, 0.66f, z), new Vector3(6.4f, 0.06f, 0.06f), dark);
            }

            // ── Horizontal drums (the light-end receivers) ────────────────────
            for (int d = 0; d < 2; d++)
            {
                float x = d == 0 ? -2.15f : 1.45f;
                string tag = d == 0 ? "A" : "B";
                Prim(visuals.transform, PrimitiveType.Cylinder, "Drum_" + tag,
                    new Vector3(x, 1.0f, -1.35f), new Vector3(0.62f, 0.85f, 0.62f), hull, new Vector3(0f, 0f, 90f));
                for (int e = 0; e < 2; e++)
                    Prim(visuals.transform, PrimitiveType.Sphere, "DrumCap_" + tag + e,
                        new Vector3(x + (e == 0 ? -0.85f : 0.85f), 1.0f, -1.35f), new Vector3(0.62f, 0.62f, 0.62f), hull);
            }

            // ── Main column: dome, overhead vent, platform rings, ladder ───────
            Prim(visuals.transform, PrimitiveType.Cylinder, "MainColumn",
                new Vector3(2.35f, 2.9f, -0.15f), new Vector3(1.0f, 2.3f, 1.0f), hull);
            Prim(visuals.transform, PrimitiveType.Sphere, "ColumnDome",
                new Vector3(2.35f, 5.35f, -0.15f), new Vector3(0.62f, 0.62f, 0.62f), hull);
            Prim(visuals.transform, PrimitiveType.Cylinder, "OverheadVent",
                new Vector3(2.35f, 5.78f, -0.15f), new Vector3(0.12f, 0.32f, 0.12f), trim);
            Prim(visuals.transform, PrimitiveType.Cylinder, "Platform_Low",
                new Vector3(2.35f, 2.0f, -0.15f), new Vector3(1.26f, 0.05f, 1.26f), dark);
            Prim(visuals.transform, PrimitiveType.Cylinder, "Platform_High",
                new Vector3(2.35f, 3.9f, -0.15f), new Vector3(1.26f, 0.05f, 1.26f), dark);
            Prim(visuals.transform, PrimitiveType.Cube, "Ladder",
                new Vector3(1.79f, 2.6f, -0.15f), new Vector3(0.06f, 4.4f, 0.34f), dark);

            // ── Stripper column + stabiliser, and the pipe work between them ───
            Prim(visuals.transform, PrimitiveType.Cylinder, "StripperColumn",
                new Vector3(-0.55f, 1.95f, -0.35f), new Vector3(0.62f, 1.45f, 0.62f), hull);
            Prim(visuals.transform, PrimitiveType.Sphere, "StripperDome",
                new Vector3(-0.55f, 3.5f, -0.35f), new Vector3(0.4f, 0.4f, 0.4f), hull);
            Prim(visuals.transform, PrimitiveType.Cylinder, "StabiliserColumn",
                new Vector3(-1.9f, 1.35f, -0.55f), new Vector3(0.44f, 1.0f, 0.44f), hull);
            Prim(visuals.transform, PrimitiveType.Sphere, "StabiliserDome",
                new Vector3(-1.9f, 2.14f, -0.55f), new Vector3(0.28f, 0.28f, 0.28f), hull);

            Prim(visuals.transform, PrimitiveType.Cylinder, "PipeBridge",
                new Vector3(0.9f, 4.7f, -0.2f), new Vector3(0.13f, 1.45f, 0.13f), dark, new Vector3(0f, 0f, 90f));
            Prim(visuals.transform, PrimitiveType.Cylinder, "Riser_Main",
                new Vector3(2.35f, 4.4f, -0.2f), new Vector3(0.1f, 0.6f, 0.1f), dark);
            Prim(visuals.transform, PrimitiveType.Cylinder, "Riser_Stripper",
                new Vector3(-0.55f, 3.75f, -0.35f), new Vector3(0.1f, 0.7f, 0.1f), dark);
            Prim(visuals.transform, PrimitiveType.Cylinder, "Pipe_RearRun",
                new Vector3(0.9f, 3.2f, -1.35f), new Vector3(0.1f, 1.6f, 0.1f), dark, new Vector3(0f, 0f, 90f));

            // ── Vent stack ───────────────────────────────────────────────────
            Prim(visuals.transform, PrimitiveType.Cylinder, "Stack",
                new Vector3(-2.95f, 2.2f, -1.6f), new Vector3(0.24f, 4.4f, 0.24f), dark);
            Prim(visuals.transform, PrimitiveType.Cube, "StackBand",
                new Vector3(-2.95f, 3.6f, -1.6f), new Vector3(0.28f, 0.08f, 0.28f), trim);
            Prim(visuals.transform, PrimitiveType.Cylinder, "StackCap",
                new Vector3(-2.95f, 4.48f, -1.6f), new Vector3(0.34f, 0.12f, 0.34f), trim);

            // ── The product row: six outlets along the front, dial above each ──
            for (int i = 0; i < ProductOrder.Length; i++)
            {
                float x = -2.4f + i * 0.96f;
                string tag = ProductOrder[i].label.Replace(" ", "");
                var mat = liquidMats[ProductOrder[i].liquid];

                // Stub pipe out of the deck, a valve in the product's own colour,
                // a post, and the dial on top of it.
                Prim(visuals.transform, PrimitiveType.Cylinder, "Stand_" + tag,
                    new Vector3(x, 0.42f, 2.25f), new Vector3(0.12f, 0.24f, 0.12f), dark);
                Prim(visuals.transform, PrimitiveType.Cylinder, "Pipe_" + tag,
                    new Vector3(x, 0.55f, 1.95f), new Vector3(0.16f, 0.2f, 0.16f), dark, new Vector3(90f, 0f, 0f));
                Prim(visuals.transform, PrimitiveType.Cube, "Valve_" + tag,
                    new Vector3(x, 0.55f, 2.35f), new Vector3(0.26f, 0.14f, 0.3f), mat);
                Prim(visuals.transform, PrimitiveType.Cylinder, "DialPost_" + tag,
                    new Vector3(x, 1.2f, 2.45f), new Vector3(0.05f, 0.65f, 0.05f), dark);
                BuildDial(visuals.transform, "Gauge_" + tag, mat, faceMat, needleMat,
                    new Vector3(x, 1.85f, 2.45f), Vector3.zero);
            }

            // ── Feed inlets on the left end: crude and refined, dial above each ─
            BuildInlet(visuals.transform, "CrudeFeed",   liquidMats[LiquidType.CrudeOil],   inlet, dark, faceMat, needleMat, 0.85f);
            BuildInlet(visuals.transform, "RefinedFeed", liquidMats[LiquidType.RefinedOil], inlet, dark, faceMat, needleMat, -0.25f);

            // Revision marker: present = this model is current, so a re-run keeps it.
            var marker = new GameObject(MODEL_REV);
            marker.transform.SetParent(visuals.transform, false);
        }

        /// <summary>
        /// One feed inlet on the left end of the deck: pipe stub, an orange valve
        /// (the feed colour the playtest reference used), a post and a dial facing
        /// outward on -X. Two inlets exist because the plant has two feeds — crude
        /// for the Atmospheric Cut and refined oil for the Re-Run — and both pour
        /// into the same auto-typing feed tank, which is why both dials read it.
        /// </summary>
        private static void BuildInlet(Transform parent, string name, Material liquidMat, Material valveMat,
            Material dark, Material faceMat, Material needleMat, float z)
        {
            Prim(parent, PrimitiveType.Cylinder, "Pipe_" + name,
                new Vector3(-3.15f, 0.55f, z), new Vector3(0.18f, 0.28f, 0.18f), dark, new Vector3(0f, 0f, 90f));
            Prim(parent, PrimitiveType.Cube, "Valve_" + name,
                new Vector3(-3.5f, 0.55f, z), new Vector3(0.16f, 0.32f, 0.32f), valveMat);
            Prim(parent, PrimitiveType.Cylinder, "DialPost_" + name,
                new Vector3(-3.35f, 1.2f, z), new Vector3(0.05f, 0.65f, 0.05f), dark);
            BuildDial(parent, "Gauge_" + name, liquidMat, faceMat, needleMat,
                new Vector3(-3.35f, 1.85f, z), new Vector3(0f, -90f, 0f));
        }

        /// <summary>
        /// A round analog dial, exactly the instrument the panel draws: a bezel in
        /// the liquid's own colour, a dark face with five tick marks, a red needle
        /// that swings -135°..+135° and a hub cap. The needle hangs off a
        /// "NeedlePivot" child at the hub and the machine rotates that pivot, so
        /// the dial reads like a real gauge rather than a tube. Returns nothing —
        /// the pivot is found by name afterwards.
        /// </summary>
        private static void BuildDial(Transform parent, string name, Material rimMat, Material faceMat,
            Material needleMat, Vector3 localPos, Vector3 facingEuler)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPos;
            holder.transform.localEulerAngles = facingEuler;

            // Bezel + face are cylinders laid on their side, so their flat faces
            // point along the holder's local Z (the direction the dial looks).
            Prim(holder.transform, PrimitiveType.Cylinder, "Bezel",
                Vector3.zero, new Vector3(0.62f, 0.03f, 0.62f), rimMat, new Vector3(90f, 0f, 0f));
            Prim(holder.transform, PrimitiveType.Cylinder, "Face",
                new Vector3(0f, 0f, 0.026f), new Vector3(0.52f, 0.02f, 0.52f), faceMat, new Vector3(90f, 0f, 0f));

            // Five ticks across the sweep (left 0, centre half, right full).
            for (int t = 0; t < 5; t++)
            {
                float deg = -135f + t * 67.5f;
                float rad = deg * Mathf.Deg2Rad;
                Prim(holder.transform, PrimitiveType.Cube, "Tick" + t,
                    new Vector3(Mathf.Sin(rad) * 0.2f, Mathf.Cos(rad) * 0.2f, 0.045f),
                    new Vector3(0.035f, 0.075f, 0.012f), rimMat, new Vector3(0f, 0f, deg));
            }

            // Needle pivot at the hub: the machine sets its local Z rotation.
            var pivot = new GameObject("NeedlePivot");
            pivot.transform.SetParent(holder.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, 0.05f);

            Prim(pivot.transform, PrimitiveType.Cube, "Needle",
                new Vector3(0f, 0.105f, 0.006f), new Vector3(0.035f, 0.21f, 0.02f), needleMat);

            // Hub cap in the liquid's colour (sits on the pivot, so it never wobbles).
            Prim(pivot.transform, PrimitiveType.Cylinder, "Hub",
                new Vector3(0f, 0f, 0.012f), new Vector3(0.11f, 0.014f, 0.11f), rimMat, new Vector3(90f, 0f, 0f));
        }

        private static GameObject Prim(Transform parent, PrimitiveType type, string name,
            Vector3 localPos, Vector3 scale, Material mat, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        // ── Refinery migration ───────────────────────────────────────────────
        /// <summary>
        /// Detaches the retired fuel-chain recipes from a refinery prefab (standing
        /// or ship) and (re)attaches the naphtha plastic recipe. Detaching is
        /// deliberate: the playtest decided no refinery makes Refined Oil, Heavy
        /// Fuel Oil or Marine Gas Oil any more — the ASSETS stay, so a save that
        /// already holds one keeps running it.
        /// </summary>
        private static void MigrateRefineryRecipes(string prefabPath, ProcessingRecipe naphthaPlastic, bool required)
        {
            if (AssetDatabase.LoadMainAssetAtPath(prefabPath) == null)
            {
                if (required)
                    Debug.LogWarning("[PetroleumColumnSetup] " + prefabPath + " not found — run the industrial content step first (the refinery still holds the retired fuel recipes until then).");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                bool dirty = false;
                var refinery = root.GetComponent<OilRefinery>();
                var shipRefinery = root.GetComponent<VoxelEngine.GridSystem.GridRefinery>();

                if (refinery != null)
                {
                    refinery.knownRecipes ??= new List<ProcessingRecipe>();
                    if (DetachRetiredRecipes(refinery.knownRecipes)) dirty = true;
                    if (naphthaPlastic != null && !refinery.knownRecipes.Contains(naphthaPlastic))
                    {
                        refinery.knownRecipes.Add(naphthaPlastic);
                        dirty = true;
                    }
                    if (dirty) EditorUtility.SetDirty(refinery);
                }

                if (shipRefinery != null)
                {
                    shipRefinery.knownRecipes ??= new List<ProcessingRecipe>();
                    if (DetachRetiredRecipes(shipRefinery.knownRecipes)) dirty = true;
                    if (naphthaPlastic != null && !shipRefinery.knownRecipes.Contains(naphthaPlastic))
                    {
                        shipRefinery.knownRecipes.Add(naphthaPlastic);
                        dirty = true;
                    }
                    if (dirty) EditorUtility.SetDirty(shipRefinery);
                }

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Debug.Log("[PetroleumColumnSetup] Fuel-chain migration applied to " + prefabPath + ".");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>Removes the three retired recipes from a list (matched by asset name,
        /// so a moved asset still detaches). Returns true when the list changed.</summary>
        private static bool DetachRetiredRecipes(List<ProcessingRecipe> list)
        {
            bool changed = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var r = list[i];
                if (r == null) continue;
                string file = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(r));
                if (System.Array.IndexOf(RETIRED_RECIPE_NAMES, file) < 0) continue;
                list.RemoveAt(i);
                changed = true;
            }
            return changed;
        }

        // ── small local helpers (the shared ones are private to another class) ─
        /// <summary>Renames one of this step's own assets without losing its GUID —
        /// so every save, prefab and research reference follows the new name.</summary>
        private static void RenameOwnAsset(string legacyPath, string newPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(newPath) != null) return;      // already renamed
            if (AssetDatabase.LoadMainAssetAtPath(legacyPath) == null) return;   // nothing to move
            string error = AssetDatabase.MoveAsset(legacyPath, newPath);
            if (!string.IsNullOrEmpty(error))
                Debug.LogWarning("[PetroleumColumnSetup] Could not rename " + legacyPath + " → " + newPath + ": " + error);
        }

        private static GameObject GetOrCreatePrefabContents(string path, out bool existed)
        {
            existed = AssetDatabase.LoadMainAssetAtPath(path) != null;
            if (existed) return PrefabUtility.LoadPrefabContents(path);
            var go = new GameObject("DistillationPlant");
            // A clean root collider sized to the whole plant — one hitbox the
            // player can click and walk around.
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 2.95f, 0.5f);
            col.size = new Vector3(7.4f, 6.0f, 4.8f);
            return go;
        }

        private static void SavePrefabContents(string path, GameObject contents, bool existed)
        {
            PrefabUtility.SaveAsPrefabAsset(contents, path);
        }

        private static void AppendUnique(List<ProcessingRecipe> list, ProcessingRecipe recipe)
        {
            if (recipe == null) return;
            if (list == null) return;
            if (!list.Contains(recipe)) list.Add(recipe);
        }

        private static T GetOrCreate<T>(string path, ref int created, ref int preserved) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) { preserved++; return a; }
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            created++;
            return a;
        }

        private static Material MakeMat(string folder, string name, Color c)
        {
            EnsureFolder(folder);
            string path = $"{folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = name, color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash <= 0) return;
            string parent = path.Substring(0, slash), leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, leaf);
        }

        private static ItemDefinition FindItem(string assetName)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { ASSET_ROOT }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == assetName)
                    return AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
            }
            return null;
        }

        private static void Add(List<RecipeIngredient> list, ItemDefinition item, int count)
        {
            if (item != null) list.Add(new RecipeIngredient { item = item, count = count });
        }

        private static void EnsureItemPersisted(ItemDefinition item)
        {
            if (item == null) return;
            var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(CATALOG);
            if (catalog == null)
            {
                EnsureFolder("Assets/Resources");
                EnsureFolder("Assets/Resources/VoxelEngine");
                catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                AssetDatabase.CreateAsset(catalog, CATALOG);
            }
            if (!catalog.items.Contains(item))
            {
                catalog.items.Add(item);
                EditorUtility.SetDirty(catalog);
            }
        }
    }
}
#endif
