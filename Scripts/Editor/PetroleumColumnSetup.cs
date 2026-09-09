// Assets/Scripts/VoxelEngine/Editor/PetroleumColumnSetup.cs
//
// Step 69 — ADVANCED DISTILLATION TOWER (9.38.0-dev): the dedicated petroleum
// plant where crude oil becomes the six fractions. The Oil Refinery is NOT the
// column any more — this round walks the earlier refinery refit back to its
// legacy two-tank machine and authors a big new block instead, exactly as the
// playtest asked: a tower that LOOKS like a distillation plant, with one world
// sight gauge per cut (plus one for the crude feed), each gauge coloured by its
// liquid, each mounted above the outlet it measures.
//
// The machine code half lives in Scripts/Crafting/AdvancedDistillationTower.cs.
// This step authors the content half:
//   • the tower prefab itself — model, six draw pipes + coloured sight gauges,
//     the crude feed gauge, tank list, gauge-pivot wiring, power consumer;
//   • Proc_AtmosphericCut.asset — 100 L crude → the six-fraction ladder of the
//     design (2 LPG / 8 naphtha / 12 kerosene / 26 diesel / 18 gasoline /
//     32 heavy fuel oil; 98 L out, the 2 L off-gas deferred to the flare round);
//   • Proc_ReRunRefinedOil.asset — the conversion cut so legacy Refined Oil
//     stock stays spendable through the same column;
//   • Proc_NaphthaPlastic.asset — the naphtha-fed plastic recipe, appended to
//     the refinery next to its legacy plastic recipe (it beats it per litre);
//   • Block_AdvancedDistillationTower + Recipe_AdvancedDistillationTower +
//     a research node (Atmospheric Distillation, requires Oil Refining);
//   • migration: the two distillation recipes are detached from the OilRefinery
//     prefab's list if an earlier run of this step put them there, so crude is
//     converted ONLY in the tower from now on.
//
// Non-destructive, like every other authored step:
//   • recipe assets are populated only on first creation — existing recipe
//     assets keep every tuned number;
//   • an existing tower prefab keeps its tuning; visuals are built only when
//     missing, and gauge-pivot wiring only fills empty slots;
//   • prefab recipe lists are append-only / detach-our-own only;
//   • existing research node costs, labels and prerequisites are preserved;
//   • nothing old is removed: legacy refinery recipes keep running, and the
//     refinery's own prefab list is only ever restored to legacy + naphtha.
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

        // Recipe asset names — keep in step with the original authoring block in
        // VoxelEngineSetupWindow so a later full re-run never forks the set.
        private const string CUT_PATH    = PROC_FOLDER + "/Proc_AtmosphericCut.asset";
        private const string RERUN_PATH  = PROC_FOLDER + "/Proc_ReRunRefinedOil.asset";
        private const string NAPHTHA_PATH = PROC_FOLDER + "/Proc_NaphthaPlastic.asset";

        private const string TOWER_PREFAB_PATH = PREFABS_FOLDER + "/AdvancedDistillationTower.prefab";
        private const string TOWER_ITEM_PATH   = BLOCKS_FOLDER + "/Block_AdvancedDistillationTower.asset";
        private const string TOWER_RECIPE_PATH = RECIPES_ROOT + "/Recipe_AdvancedDistillationTower.asset";
        private const string TOWER_NODE_PATH   = NODES + "/res_atmospheric_distillation.asset";
        private const string OIL_REFINING_NODE_PATH = NODES + "/res_oil_refining.asset";

        [MenuItem("Tools/Voxel Engine/Setup Step 69 — Advanced Distillation Tower Content")]
        public static void RunStep69Menu() => RunStep69();

        public static void RunStep69()
        {
            Debug.Log("[PetroleumColumnSetup] Step 69 — advanced distillation tower started.");

            foreach (var f in new[] { INDUSTRIAL, PROC_FOLDER, PREFABS_FOLDER, BLOCKS_FOLDER, RECIPES_ROOT, NODES })
                EnsureFolder(f);

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

            // ── 2) The tower prefab: a plant, not a box ─────────────────────
            var towerRoot = GetOrCreatePrefabContents(TOWER_PREFAB_PATH, out bool towerExisted);
            bool visualsBuilt = false;
            try
            {
                var st = towerRoot.GetComponent<CraftingStation>();
                if (st == null) st = towerRoot.AddComponent<CraftingStation>();
                if (string.IsNullOrEmpty(st.displayName)) st.displayName = "Advanced Distillation Tower";
                st.tier = StationTier.Assembler;

                var tower = towerRoot.GetComponent<AdvancedDistillationTower>();
                if (tower == null) tower = towerRoot.AddComponent<AdvancedDistillationTower>();

                var power = towerRoot.GetComponent<VoxelEngine.Power.PowerConsumer>();
                if (power == null)
                {
                    power = towerRoot.AddComponent<VoxelEngine.Power.PowerConsumer>();
                    power.connectRadius = 2.6f;
                    power.wattsPerSecond = tower.idleWattsPerSecond;
                }

                // Visuals are built only when the prefab never had them; a tuned
                // model is never rebuilt.
                if (towerRoot.transform.Find("Visuals") == null)
                {
                    BuildTowerVisuals(towerRoot.transform);
                    visualsBuilt = true;
                }

                tower.EnsureTanks();

                // Gauge-pivot wiring: fill only missing slots so a hand-tuned list survives.
                var allTanks = tower.FluidTanks; // feed + 6 cuts
                if (tower.gaugePivots == null) tower.gaugePivots = new List<Transform>();
                var wanted = AllGaugePivotNames();
                for (int i = tower.gaugePivots.Count; i < allTanks.Count; i++)
                    tower.gaugePivots.Add(null);
                for (int i = 0; i < allTanks.Count; i++)
                {
                    if (tower.gaugePivots[i] != null) continue;
                    var found = FindNamedInVisuals(towerRoot.transform, wanted[i]);
                    if (found != null) tower.gaugePivots[i] = found;
                }
                EditorUtility.SetDirty(tower);

                // Recipe list: the tower owns distillation.
                if (tower.knownRecipes == null) tower.knownRecipes = new List<ProcessingRecipe>();
                AppendUnique(tower.knownRecipes, cut);
                AppendUnique(tower.knownRecipes, rerun);
                EditorUtility.SetDirty(tower);

                SavePrefabContents(TOWER_PREFAB_PATH, towerRoot, towerExisted);
            }
            finally
            {
                if (towerExisted) PrefabUtility.UnloadPrefabContents(towerRoot);
                else Object.DestroyImmediate(towerRoot);
            }
            if (towerExisted) preserved++; else created++;

            // ── 3) Refinery migration: crude distillation leaves the refinery ─
            //    The refinery keeps its legacy recipes + the naphtha plastic feed;
            //    the two distillation cuts belong to the tower only. Detaching
            //    exactly the assets this step owns is a migration of our own
            //    earlier content, not a player's tuning.
            string refineryPath = PREFABS_FOLDER + "/OilRefinery.prefab";
            if (AssetDatabase.LoadMainAssetAtPath(refineryPath) != null)
            {
                var refRoot = PrefabUtility.LoadPrefabContents(refineryPath);
                try
                {
                    var refinery = refRoot.GetComponent<OilRefinery>();
                    if (refinery != null)
                    {
                        bool dirty = false;
                        if (refinery.knownRecipes != null)
                        {
                            for (int i = refinery.knownRecipes.Count - 1; i >= 0; i--)
                            {
                                var r = refinery.knownRecipes[i];
                                if (r == null) continue;
                                string p = AssetDatabase.GetAssetPath(r);
                                if (p == CUT_PATH || p == RERUN_PATH)
                                {
                                    refinery.knownRecipes.RemoveAt(i);
                                    dirty = true;
                                }
                            }
                        }
                        if (naphthaPlastic != null)
                        {
                            if (refinery.knownRecipes == null) refinery.knownRecipes = new List<ProcessingRecipe>();
                            if (!refinery.knownRecipes.Contains(naphthaPlastic))
                            {
                                refinery.knownRecipes.Add(naphthaPlastic);
                                dirty = true;
                            }
                        }
                        if (dirty) EditorUtility.SetDirty(refinery);
                    }
                    PrefabUtility.SaveAsPrefabAsset(refRoot, refineryPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(refRoot); }
            }
            else Debug.LogWarning("[PetroleumColumnSetup] OilRefinery.prefab not found — run the industrial content step first.");

            // ── 4) Block item + craft recipe + research gate ────────────────
            var steelPlate = FindItem("Item_SteelPlate");
            var ironGear   = FindItem("Item_IronGear");
            var circuit    = FindItem("Item_Circuit");
            var glass      = FindItem("Item_Glass");
            var copperWire = FindItem("Item_CopperWire");

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TOWER_PREFAB_PATH);

            var block = GetOrCreate<BlockItem>(TOWER_ITEM_PATH, ref created, ref preserved);
            block.itemId = "block_advanced_distillation_tower";
            block.displayName = "Advanced Distillation Tower";
            block.description = "The petroleum-era plant: a tall atmospheric distillation column that turns 100 L of crude "
                + "into six typed product cuts — LPG, naphtha, kerosene, diesel, gasoline and heavy fuel oil — each held in "
                + "its own tank, each with a coloured sight gauge on the tower and an analog dial in the panel. Only this "
                + "block converts crude; the Oil Refinery keeps its legacy refined-oil recipes. Feeds one liquid at a time "
                + "(crude for the Atmospheric Cut, refined oil for the Re-Run cut).";
            block.iconTint = new Color(0.44f, 0.48f, 0.55f);
            if (block.maxStack <= 0) block.maxStack = 5;
            if (block.massPerUnit <= 0f) block.massPerUnit = 60f;
            block.category = "Industrial";
            block.placedPrefab = prefabAsset;
            block.gridSize = Vector3Int.one;
            block.allowStacking = false;
            if (block.blockHealth <= 0) block.blockHealth = 1600;
            if (block.miningTier <= 0) block.miningTier = 3;
            EditorUtility.SetDirty(block);

            var recipe = GetOrCreate<RecipeDefinition>(TOWER_RECIPE_PATH, ref created, ref preserved);
            recipe.displayName = "Advanced Distillation Tower";
            recipe.outputItem = block;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            if (recipe.requiredStation == StationTier.None && recipe.craftSeconds <= 0f)
            {
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 12f;
                recipe.unlockedByDefault = false;
            }
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var list = new List<RecipeIngredient>();
                Add(list, steelPlate, 24);
                Add(list, ironGear, 14);
                Add(list, circuit, 8);
                Add(list, glass, 6);
                Add(list, copperWire, 10);
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
            var node = GetOrCreate<ResearchNode>(TOWER_NODE_PATH, ref created, ref preserved);
            node.nodeId = "res_atmospheric_distillation";
            if (string.IsNullOrEmpty(node.displayName) || node.displayName == "New Research")
                node.displayName = "Atmospheric Distillation";
            if (string.IsNullOrEmpty(node.description))
                node.description = "The tall tower that turns crude into six real products: LPG, naphtha, kerosene, diesel, "
                    + "gasoline and heavy fuel oil — each a typed tank, each worth carrying because the heavy end is worth "
                    + "something too. Build it on a base that already refines oil.";
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
            EditorUtility.DisplayDialog("Voxel Engine — Advanced Distillation Tower (Step 69)",
                "Distillation tower authored" + (visualsBuilt ? " (visuals built)." : "; existing prefab kept.") + "\n\n" +
                "• Created: " + created + " (" + preserved + " existing preserved)\n" +
                "• ADVANCED DISTILLATION TOWER — a big plant block: tall column with a stripper,\n" +
                "  six draw pipes and a coloured world sight gauge above each outlet, plus a\n" +
                "  crude-feed gauge. Gauges rise and fall with the tanks; each colour is the\n" +
                "  liquid's own.\n" +
                "• ATMOSPHERIC CUT — 100 L crude -> LPG 2 / Naphtha 8 / Kerosene 12 /\n" +
                "  Diesel 26 / Gasoline 18 / Heavy Fuel Oil 32 L (98 L out, 2 L off-gas)\n" +
                "• RE-RUN REFINED OIL — conversion cut so pre-9.38 stock stays spendable\n" +
                "• Crude conversion lives ONLY on the tower now: the refinery prefab had the\n" +
                "  two distillation recipes detached and keeps its legacy set + the naphtha\n" +
                "  plastic feed\n" +
                "• Recipe_AdvancedDistillationTower, gated by ATMOSPHERIC DISTILLATION research\n" +
                "  (tier 5, requires Oil Refining)\n" +
                "• The panel shows one analog dial per tank with pour / draw / drain controls\n" +
                "• Nothing tuned was rewritten: existing recipes, prefab lists, tank numbers\n" +
                "  and gauge wiring keep their values on a re-run", "OK");
        }

        // ── Tower model ──────────────────────────────────────────────────────
        private static readonly (string label, LiquidType liquid, float y)[] ProductLevels =
        {
            ("LPG",            LiquidType.Lpg,          6.9f),
            ("Naphtha",        LiquidType.Naphtha,      5.7f),
            ("Kerosene",       LiquidType.Kerosene,     4.5f),
            ("Diesel",         LiquidType.Diesel,       3.3f),
            ("Gasoline",       LiquidType.Gasoline,     2.1f),
            ("Heavy Fuel Oil", LiquidType.HeavyFuelOil, 0.9f),
        };

        private static string[] AllGaugePivotNames()
        {
            var names = new string[1 + ProductLevels.Length];
            names[0] = "Gauge_CrudeFeed";
            for (int i = 0; i < ProductLevels.Length; i++) names[1 + i] = "Gauge_" + ProductLevels[i].label.Replace(" ", "");
            return names;
        }

        private static Transform FindNamedInVisuals(Transform root, string name)
        {
            var visuals = root.Find("Visuals");
            if (visuals == null) return null;
            var gauge = visuals.Find(name);
            if (gauge == null) return null;
            return gauge.Find("FillPivot");
        }

        /// <summary>
        /// Builds the whole tower model under a "Visuals" child: base pad + plinth,
        /// the tall main column with a dome, a stripper column with a cross pipe,
        /// six product draw pipes on the +X side (heavy at the bottom, LPG at the
        /// top — heavies settle low) each with a sight gauge ABOVE the outlet, and
        /// the crude feed line with its own gauge on the opposite side. Returns
        /// nothing: the gauge pivots are found by name afterwards.
        /// </summary>
        private static void BuildTowerVisuals(Transform root)
        {
            var visuals = new GameObject("Visuals");
            visuals.transform.SetParent(root, false);

            var hull = MakeMat(PREFABS_FOLDER, "Mat_TowerHull",
                new Color(0.36f, 0.38f, 0.42f));
            var dark = MakeMat(PREFABS_FOLDER, "Mat_TowerDark",
                new Color(0.16f, 0.17f, 0.19f));
            var trim = MakeMat(PREFABS_FOLDER, "Mat_TowerTrim",
                new Color(0.72f, 0.55f, 0.18f));
            var glass = MakeMat(PREFABS_FOLDER, "Mat_TowerGlass",
                new Color(0.55f, 0.66f, 0.72f));
            // Gauge fill materials — one per liquid, baked so no runtime tinting is
            // needed. Crude is lifted a touch so the dark feed shows against the
            // dark housing.
            var feedMat = MakeMat(PREFABS_FOLDER, "Mat_Tower_CrudeFeed",
                Color.Lerp(LiquidType.CrudeOil.Color(), new Color(0.5f, 0.45f, 0.36f), 0.55f));
            var liquidMats = new Dictionary<LiquidType, Material>
            {
                { LiquidType.CrudeOil,     feedMat },
                { LiquidType.Lpg,          MakeMat(PREFABS_FOLDER, "Mat_Tower_LPG",          LiquidType.Lpg.Color()) },
                { LiquidType.Naphtha,      MakeMat(PREFABS_FOLDER, "Mat_Tower_Naphtha",      LiquidType.Naphtha.Color()) },
                { LiquidType.Kerosene,     MakeMat(PREFABS_FOLDER, "Mat_Tower_Kerosene",     LiquidType.Kerosene.Color()) },
                { LiquidType.Diesel,       MakeMat(PREFABS_FOLDER, "Mat_Tower_Diesel",       LiquidType.Diesel.Color()) },
                { LiquidType.Gasoline,     MakeMat(PREFABS_FOLDER, "Mat_Tower_Gasoline",     LiquidType.Gasoline.Color()) },
                { LiquidType.HeavyFuelOil, MakeMat(PREFABS_FOLDER, "Mat_Tower_HeavyFuelOil", LiquidType.HeavyFuelOil.Color()) },
            };

            // Base pad + plinth (bottom of the plant).
            Prim(visuals.transform, PrimitiveType.Cube, "Pad", new Vector3(0f, 0.08f, 0f), new Vector3(3.9f, 0.16f, 3.3f), dark);
            Prim(visuals.transform, PrimitiveType.Cube, "Plinth", new Vector3(0f, 0.41f, 0f), new Vector3(3.3f, 0.5f, 2.7f), dark);

            // Main atmospheric column (tall cylinder with a dome).
            Prim(visuals.transform, PrimitiveType.Cylinder, "MainColumn",
                new Vector3(0f, 4.61f, 0f), new Vector3(1.24f, 3.95f, 1.24f), hull);
            Prim(visuals.transform, PrimitiveType.Sphere, "ColumnDome",
                new Vector3(0f, 8.75f, 0f), new Vector3(0.75f, 0.75f, 0.75f), hull);
            Prim(visuals.transform, PrimitiveType.Cylinder, "OverheadVent",
                new Vector3(0f, 9.18f, 0f), new Vector3(0.12f, 0.35f, 0.12f), trim);

            // Stripper column + cross pipe (the little column that says "plant").
            Prim(visuals.transform, PrimitiveType.Cube, "StripperPedestal",
                new Vector3(-1.5f, 0.41f, -0.6f), new Vector3(1.1f, 0.5f, 1.0f), dark);
            Prim(visuals.transform, PrimitiveType.Cylinder, "StripperColumn",
                new Vector3(-1.5f, 2.12f, -0.6f), new Vector3(0.58f, 1.5f, 0.58f), hull);
            Prim(visuals.transform, PrimitiveType.Sphere, "StripperDome",
                new Vector3(-1.5f, 3.66f, -0.6f), new Vector3(0.38f, 0.38f, 0.38f), hull);
            Prim(visuals.transform, PrimitiveType.Cylinder, "CrossPipe",
                new Vector3(-0.28f, 2.6f, -0.6f), new Vector3(0.16f, 0.95f, 0.16f), dark,
                new Vector3(0f, 0f, 90f));

            // Six product draws, heavy at the bottom, LPG at the top. Each outlet
            // pipe gets a coloured valve cube at its tip and a sight gauge ABOVE it.
            foreach (var lvl in ProductLevels)
            {
                float y = lvl.y;
                string tag = lvl.label.Replace(" ", "");
                var mat = liquidMats[lvl.liquid];
                Prim(visuals.transform, PrimitiveType.Cylinder, $"Pipe_{tag}",
                    new Vector3(0.87f, y, 0f), new Vector3(0.16f, 0.26f, 0.16f), dark,
                    new Vector3(0f, 0f, 90f));
                Prim(visuals.transform, PrimitiveType.Cube, $"Valve_{tag}",
                    new Vector3(1.14f, y, 0f), new Vector3(0.2f, 0.09f, 0.2f), mat);
                BuildSightGauge(visuals.transform, $"Gauge_{tag}", mat,
                    new Vector3(1.36f, 0f, 0f), y + 0.12f, 1.0f, glass, dark);
            }

            // Crude feed on the -X side: pipe, valve and a taller gauge.
            var crudeMat = liquidMats[LiquidType.CrudeOil];
            Prim(visuals.transform, PrimitiveType.Cylinder, "Pipe_CrudeFeed",
                new Vector3(-0.87f, 0.72f, 0.55f), new Vector3(0.18f, 0.26f, 0.18f), dark,
                new Vector3(0f, 0f, 90f));
            Prim(visuals.transform, PrimitiveType.Cube, "Valve_CrudeFeed",
                new Vector3(-1.14f, 0.72f, 0.55f), new Vector3(0.22f, 0.1f, 0.22f), crudeMat);
            BuildSightGauge(visuals.transform, "Gauge_CrudeFeed", crudeMat,
                new Vector3(-1.36f, 0f, 0.55f), 0.8f, 1.3f, glass, dark);
        }

        /// <summary>
        /// A vertical sight gauge: a dark steel instrument plate with a coloured
        /// fill bar on EACH face (so the level reads from both sides of the tower),
        /// dark end caps and a coloured identity cap on top. The fill bars hang
        /// from a single "FillPivot" at the gauge bottom: the machine scales that
        /// pivot's Y by the tank fill, so the coloured bars grow from zero like
        /// liquid rising in a sight tube. Mounted at localPos, bottom at groundY.
        /// </summary>
        private static void BuildSightGauge(Transform parent, string name, Material fillMat,
            Vector3 localPos, float groundY, float height, Material glass, Material dark)
        {
            var gauge = new GameObject(name);
            gauge.transform.SetParent(parent, false);
            gauge.transform.localPosition = localPos;

            float fillH = Mathf.Max(0.2f, height - 0.16f);   // leave room for the end caps

            // Housing: a thin dark plate (X depth 0.02) with glass-toned top and
            // bottom end caps and a coloured identity cap.
            Prim(gauge.transform, PrimitiveType.Cube, "Housing",
                new Vector3(0f, groundY + height / 2f, 0f), new Vector3(0.02f, height, 0.26f), dark);
            Prim(gauge.transform, PrimitiveType.Cube, "EndCapBottom",
                new Vector3(0f, groundY + 0.02f, 0f), new Vector3(0.09f, 0.04f, 0.3f), glass);
            Prim(gauge.transform, PrimitiveType.Cube, "EndCapTop",
                new Vector3(0f, groundY + height - 0.02f, 0f), new Vector3(0.09f, 0.04f, 0.3f), glass);
            Prim(gauge.transform, PrimitiveType.Cube, "Cap",
                new Vector3(0f, groundY + height + 0.03f, 0f), new Vector3(0.09f, 0.05f, 0.3f), fillMat);

            // FillPivot at the bottom of the scale. Its two fill bars (front face,
            // back face) are its only children, so scaling the pivot's Y grows the
            // bars from the bottom while every other part of the gauge stays put.
            var pivot = new GameObject("FillPivot");
            pivot.transform.SetParent(gauge.transform, false);
            pivot.transform.localPosition = new Vector3(0f, groundY, 0f);

            float barHalf = fillH / 2f;
            float barThick = 0.03f;
            float faceGap = 0.03f;   // just in front of the housing face
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? faceGap : -faceGap;
                var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(fill.GetComponent<Collider>());
                fill.name = side == 0 ? "FillFront" : "FillBack";
                fill.transform.SetParent(pivot.transform, false);
                fill.transform.localScale = new Vector3(barThick, fillH, 0.2f);
                fill.transform.localPosition = new Vector3(x, barHalf, 0f);
                fill.GetComponent<Renderer>().sharedMaterial = fillMat;
            }
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

        // ── small local helpers (the shared ones are private to another class) ─
        private static GameObject GetOrCreatePrefabContents(string path, out bool existed)
        {
            existed = AssetDatabase.LoadMainAssetAtPath(path) != null;
            if (existed) return PrefabUtility.LoadPrefabContents(path);
            var go = new GameObject("AdvancedDistillationTower");
            // A clean root collider sized to the whole plant — one hitbox the
            // player can click and walk around.
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 4.75f, 0f);
            col.size = new Vector3(4.1f, 9.6f, 3.5f);
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
