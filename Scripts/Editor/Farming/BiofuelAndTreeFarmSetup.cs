#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Farming;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Research;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;
using VoxelEngine.Trees;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Non-destructive Setup Wizard step (17.25.0-dev) that authors:
    ///   - Tree Sapling (SeedItem), Wood Chips (ResourceItem Fuel), Biofuel Briquette (ResourceItem Fuel),
    ///     Organic Fertilizer (ResourceItem), and ensures Biomass has fuelSeconds configured.
    ///   - TreeFarmPlot (3x1x3 arboricultural timber planter plot prefab + BlockItem).
    ///   - AutoTreeFarm (4x3x4 automated forestry plot & gantry harvester prefab + BlockItem + MachineDefinition).
    ///   - BiomassPress (2x2x2 industrial screw-press & bio-refinery prefab + BlockItem + MachineDefinition).
    ///   - ProcessingRecipes for BiomassPress (Crops->Biomass+Biofuel, WoodLog->Chips+Biomass+Biofuel,
    ///     Biomass+Water->Biofuel+Briquette, WoodChips+Water->Biofuel+Biomass) and MachineRecipe for Crusher.
    ///   - Crafting recipes in RecipeRegistry, ResearchNode in ResearchTree, and ItemPersistenceCatalog entries.
    /// Invoked exclusively from VoxelEngineSetupWindow (Tools -> Voxel Engine -> Setup Wizard).
    /// </summary>
    public static class BiofuelAndTreeFarmSetup
    {
        private const string AssetRoot        = "Assets/VoxelEngineAssets";
        private const string ItemsFolder      = AssetRoot + "/Items";
        private const string RecipesFolder    = AssetRoot + "/Recipes";
        private const string ProcFolder       = AssetRoot + "/Recipes/Processing";
        private const string MachinesFolder   = AssetRoot + "/Machines";
        private const string PrefabsFolder    = AssetRoot + "/Prefabs/Machines";
        private const string FarmRoot         = AssetRoot + "/Farming";
        private const string FarmItemsFolder  = FarmRoot + "/Items";
        private const string FarmBlocksFolder = FarmRoot + "/Blocks";
        private const string FarmPrefabsFolder= FarmRoot + "/Prefabs";
        private const string FarmRecipesFolder= FarmRoot + "/Recipes";
        private const string ResearchFolder   = AssetRoot + "/Research";
        private const string CatalogPath      = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        public static void RunStep117()
        {
            RunInternal(interactive: true);
        }

        public static void RunSilent()
        {
            RunInternal(interactive: false);
        }

        private static void RunInternal(bool interactive)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (interactive)
                    EditorUtility.DisplayDialog("Biofuel & Tree Farm Setup", "Exit Play Mode before running setup.", "OK");
                return;
            }

            EnsureFolder(AssetRoot, "Items");
            EnsureFolder(AssetRoot, "Recipes");
            EnsureFolder(RecipesFolder, "Processing");
            EnsureFolder(AssetRoot, "Machines");
            EnsureFolder(AssetRoot, "Prefabs");
            EnsureFolder(AssetRoot + "/Prefabs", "Machines");
            EnsureFolder(AssetRoot, "Farming");
            EnsureFolder(FarmRoot, "Items");
            EnsureFolder(FarmRoot, "Blocks");
            EnsureFolder(FarmRoot, "Prefabs");
            EnsureFolder(FarmRoot, "Recipes");
            EnsureFolder(AssetRoot, "Materials");
            EnsureFolder(AssetRoot + "/Materials", "Forestry");

            // ── 1. Base & Forestry / Biofuel Items ──────────────────────────────
            var woodLog     = FindItem("Item_WoodLog", "wood_log", "Wood Log");
            var stone       = FindItem("Item_Stone", "stone", "Stone");
            var clay        = FindItem("Item_Clay", "clay", "Clay") ?? stone;
            var ironIngot   = FindItem("Item_IronIngot", "iron_ingot", "Iron Ingot");
            var copperIngot = FindItem("Item_CopperIngot", "copper_ingot", "Copper Ingot");
            var steelIngot  = FindItem("Item_SteelIngot", "steel_ingot", "Steel Ingot") ?? ironIngot;
            var steelPlate  = FindItem("Item_SteelPlate", "steel_plate", "Steel Plate") ?? steelIngot;
            var ironPlate   = FindItem("Item_IronPlate", "iron_plate", "Iron Plate") ?? ironIngot;
            var ironGear    = FindItem("Item_Gear", "iron_gear", "Iron Gear") ?? ironIngot;
            var copperWire  = FindItem("Item_CopperWire", "copper_wire", "Copper Wire") ?? copperIngot;
            var circuit     = FindItem("Item_Circuit", "electronic_circuit", "Electronic Circuit") ?? copperWire;
            var rawWheat    = FindItem("Food_WheatRaw", "food_wheat_raw", "Raw Wheat");

            var treeSapling = EnsureSeedItem(
                $"{FarmItemsFolder}/Seed_TreeSapling.asset",
                "seed_tree_sapling",
                "Tree Sapling",
                "Young arboricultural timber sapling. Plant in a 3x3 Tree Farming Plot or feed into an Automatic Tree Farm to sustainably cultivate Wood Logs, Saplings, and Biomass.",
                new Color(0.24f, 0.72f, 0.30f));

            var biomass = EnsureResourceItem(
                $"{ItemsFolder}/Item_Biomass.asset",
                "item_biomass",
                "Biomass",
                "Compressed organic plant matter from crops and forestry. Feed into the Biomass Press to refine Liquid Biofuel and Biofuel Briquettes, or use in the Biofarm Oxygen Garden.",
                new Color(0.35f, 0.65f, 0.30f),
                ResourceCategory.Fuel,
                "Farming",
                maxStack: 200,
                fuelSeconds: 15f);

            var woodChips = EnsureResourceItem(
                $"{ItemsFolder}/Item_WoodChips.asset",
                "item_wood_chips",
                "Wood Chips",
                "Shredded timber fiber produced by the Automatic Tree Farm, Biomass Press, or Crusher. Presses into Liquid Biofuel and Biomass, or burns as light solid fuel.",
                new Color(0.72f, 0.54f, 0.30f),
                ResourceCategory.Fuel,
                "Materials",
                maxStack: 200,
                fuelSeconds: 12f);

            var biofuelBriquette = EnsureResourceItem(
                $"{ItemsFolder}/Item_BiofuelBriquette.asset",
                "item_biofuel_briquette",
                "Biofuel Briquette",
                "High-density solid biofuel pressed from Biomass and Wood Chips. Burns cleanly for 65 seconds in Furnaces, Coke Kilns, Coal Generators, and Steam Locomotives.",
                new Color(0.44f, 0.78f, 0.20f),
                ResourceCategory.Fuel,
                "Materials",
                maxStack: 200,
                fuelSeconds: 65f);

            var fertilizer = EnsureResourceItem(
                $"{FarmItemsFolder}/Item_OrganicFertilizer.asset",
                "item_fertilizer",
                "Organic Fertilizer",
                "Nutrient-rich composted mulch crafted from Biomass and Wood Chips. Apply to Tree Farming Plots or load into an Automatic Tree Farm for 1.5x growth speed and bonus timber/biomass yield.",
                new Color(0.42f, 0.32f, 0.18f),
                ResourceCategory.Misc,
                "Farming",
                maxStack: 200,
                fuelSeconds: 0f);

            // Wire saplingItem onto wild scatter Tree prefabs non-destructively
            WireScatterTreesWithSapling(treeSapling, woodLog);

            // ── 2. ProcessingRecipes for BiomassPress ───────────────────────────
            var prCrops = EnsureProcessingRecipe(
                $"{ProcFolder}/PR_BiomassPress_CropsToBiomass.asset",
                "Crop Pressing (Crops → Biomass + Biofuel)",
                "Biofuel",
                seconds: 3.5f,
                inputs: rawWheat != null ? new[] { (rawWheat, 2) } : null,
                outputs: new[] { ((ItemDefinition)biomass, 3) },
                fluidInputs: null,
                fluidOutputs: new[] { (LiquidType.Biofuel, 20f) });

            var prWood = EnsureProcessingRecipe(
                $"{ProcFolder}/PR_BiomassPress_WoodToBiofuel.asset",
                "Timber Pulping (Wood Log → Chips + Biomass + Biofuel)",
                "Biofuel",
                seconds: 4.0f,
                inputs: woodLog != null ? new[] { (woodLog, 1) } : null,
                outputs: new[] { ((ItemDefinition)woodChips, 2), ((ItemDefinition)biomass, 2) },
                fluidInputs: null,
                fluidOutputs: new[] { (LiquidType.Biofuel, 15f) });

            var prBiomass = EnsureProcessingRecipe(
                $"{ProcFolder}/PR_BiomassPress_BiomassToBiofuel.asset",
                "Biofuel Extraction (Biomass + Water → Biofuel + Briquette)",
                "Biofuel",
                seconds: 4.0f,
                inputs: new[] { ((ItemDefinition)biomass, 2) },
                outputs: new[] { ((ItemDefinition)biofuelBriquette, 1) },
                fluidInputs: new[] { (LiquidType.Water, 10f) },
                fluidOutputs: new[] { (LiquidType.Biofuel, 40f) });

            var prChips = EnsureProcessingRecipe(
                $"{ProcFolder}/PR_BiomassPress_ChipsToBiofuel.asset",
                "Cellulose Hydro-Pressing (Wood Chips + Water → Biofuel + Biomass)",
                "Biofuel",
                seconds: 4.0f,
                inputs: new[] { ((ItemDefinition)woodChips, 2) },
                outputs: new[] { ((ItemDefinition)biomass, 1) },
                fluidInputs: new[] { (LiquidType.Water, 8f) },
                fluidOutputs: new[] { (LiquidType.Biofuel, 35f) });

            var pressRecipes = new List<ProcessingRecipe> { prBiomass, prChips, prCrops, prWood };

            // ── 3. Prefabs, BlockItems & MachineDefinitions ─────────────────────
            var treePlotPrefab = EnsureTreeFarmPlotPrefab(
                $"{FarmPrefabsFolder}/TreeFarmPlot.prefab",
                woodLog, treeSapling, biomass,
                out var blockTreePlot);

            var autoTreeFarmPrefab = EnsureAutoTreeFarmPrefab(
                $"{PrefabsFolder}/AutoTreeFarm.prefab",
                woodLog, treeSapling, biomass, woodChips,
                out var blockAutoTreeFarm);

            var biomassPressPrefab = EnsureBiomassPressPrefab(
                $"{PrefabsFolder}/BiomassPress.prefab",
                biomass, biofuelBriquette, woodChips, pressRecipes,
                out var blockBiomassPress);

            EnsureMachineDefinition(
                $"{MachinesFolder}/Machine_AutoTreeFarm.asset",
                "machine_auto_tree_farm",
                "Automatic Tree Farm",
                "Self-contained 4x3x4 automated forestry plot and gantry harvester. Grows full-sized timber trees with water irrigation and nutrient bonuses, automatically fells and replants them, tends nearby Tree Farming Plots within 10 m, and exports Wood Logs, Saplings, Biomass, and Wood Chips to belts or chutes.",
                MachineCategory.Processor,
                autoTreeFarmPrefab);

            EnsureMachineDefinition(
                $"{MachinesFolder}/Machine_BiomassPress.asset",
                "machine_biomass_press",
                "Biomass Press",
                "Industrial 2x2x2 screw-press and bio-refinery. Compresses farm crops, seeds, wood logs, wood chips, and biomass into solid Biomass, Biofuel Briquettes, and piped Liquid Biofuel for generators and fluid networks.",
                MachineCategory.Processor,
                biomassPressPrefab);

            // ── 4. Crafting Recipes & Research Node ─────────────────────────────
            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>($"{AssetRoot}/RecipeRegistry.asset");
            if (registry == null)
            {
                string[] rGuids = AssetDatabase.FindAssets("t:RecipeRegistry");
                if (rGuids.Length > 0)
                    registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(AssetDatabase.GUIDToAssetPath(rGuids[0]));
            }

            var recSapling = EnsureCraftRecipe(
                $"{FarmRecipesFolder}/Recipe_TreeSapling.asset",
                "Tree Sapling x2",
                treeSapling, 2, 1.5f,
                StationTier.None,
                unlockedByDefault: true,
                new[] { (woodLog, 1) },
                registry);

            var recTreePlot = EnsureCraftRecipe(
                $"{FarmRecipesFolder}/Recipe_TreeFarmPlot.asset",
                "Tree Farming Plot",
                blockTreePlot, 1, 3.0f,
                StationTier.CraftingBench,
                unlockedByDefault: true,
                new[] { (woodLog, 4), (clay, 2), (ironIngot, 2) },
                registry);

            var recFertilizer = EnsureCraftRecipe(
                $"{FarmRecipesFolder}/Recipe_OrganicFertilizer.asset",
                "Organic Fertilizer x3",
                fertilizer, 3, 2.5f,
                StationTier.CraftingBench,
                unlockedByDefault: true,
                new[] { ((ItemDefinition)biomass, 2), ((ItemDefinition)woodChips, 1) },
                registry);

            var recBriquette = EnsureCraftRecipe(
                $"{RecipesFolder}/Recipe_BiofuelBriquette.asset",
                "Biofuel Briquette x2",
                biofuelBriquette, 2, 3.0f,
                StationTier.CraftingBench,
                unlockedByDefault: true,
                new[] { ((ItemDefinition)biomass, 3), ((ItemDefinition)woodChips, 1) },
                registry);

            var recAutoTreeFarm = EnsureCraftRecipe(
                $"{RecipesFolder}/Recipe_AutoTreeFarm.asset",
                "Automatic Tree Farm",
                blockAutoTreeFarm, 1, 6.0f,
                StationTier.Assembler,
                unlockedByDefault: true,
                new[] { (steelPlate, 6), (ironGear, 4), (circuit, 2), (woodLog, 4) },
                registry);

            var recBiomassPress = EnsureCraftRecipe(
                $"{RecipesFolder}/Recipe_BiomassPress.asset",
                "Biomass Press",
                blockBiomassPress, 1, 6.0f,
                StationTier.Assembler,
                unlockedByDefault: true,
                new[] { (steelPlate, 6), (ironGear, 4), (copperIngot, 4), (circuit, 2) },
                registry);

            EnsureBiofuelResearchNode(
                recSapling, recTreePlot, recFertilizer, recBriquette, recAutoTreeFarm, recBiomassPress,
                blockTreePlot, blockAutoTreeFarm, blockBiomassPress);

            RegisterInPersistenceCatalog(
                treeSapling, biomass, woodChips, biofuelBriquette, fertilizer,
                blockTreePlot, blockAutoTreeFarm, blockBiomassPress);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[BiofuelAndTreeFarmSetup] 17.25.0-dev Biofuel Chain, Biomass Press & Automatic Tree Farm setup complete.");
            if (interactive)
            {
                EditorUtility.DisplayDialog(
                    "Biofuel Chain & Tree Farm Setup Complete",
                    "Authored and wired:\n" +
                    "• LiquidType.Biofuel (34.5 MJ/L combustible biofuel)\n" +
                    "• Tree Sapling, Wood Chips, Biofuel Briquette (65s fuel), Organic Fertilizer\n" +
                    "• Tree Farming Plot (3x1x3 arboricultural timber planter)\n" +
                    "• Automatic Tree Farm (4x3x4 gantry harvester + irrigation + 10m plot tending)\n" +
                    "• Biomass Press (2x2x2 screw-press & liquid Biofuel refinery)\n" +
                    "• ProcessingRecipes, Crafting Recipes, Research Node & Persistence Catalog",
                    "Awesome!");
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  PREFAB BUILDERS (with shared disk .mat assets — zero transient materials)
        // ════════════════════════════════════════════════════════════════════════

        private static GameObject EnsureTreeFarmPlotPrefab(
            string prefabPath,
            ItemDefinition woodLog,
            ItemDefinition sapling,
            ItemDefinition biomass,
            out BlockItem blockItem)
        {
            blockItem = EnsureBlockItem(
                $"{FarmBlocksFolder}/Block_TreeFarmPlot.asset",
                "block_tree_farm_plot",
                "Tree Farming Plot",
                "Wide 3x1x3 reinforced timber & loam planter bed engineered for full-sized trees. Right-click with a Tree Sapling to plant, irrigate with water or Sprinklers for 2x growth speed, fertilize for 1.5x speed, and harvest manually or automatically via Harvesters / Automatic Tree Farms.",
                new Color(0.28f, 0.62f, 0.26f),
                new Vector3Int(3, 1, 3),
                maxHp: 220,
                category: "Farming");

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var root = new GameObject("TreeFarmPlot");
            try
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, -0.30f, 0f);
                box.size = new Vector3(2.9f, 0.40f, 2.9f);

                var pb = root.AddComponent<PlacedBlock>();
                pb.Item = blockItem;
                pb.Hp = 220;

                var plot = root.AddComponent<TreeFarmPlot>();
                plot.woodLogItem = woodLog;
                plot.saplingItem = sapling;
                plot.biomassItem = biomass;
                plot.isPlanted = false;
                plot.growthProgress = 0f;
                plot.RebuildVisuals();
                AssignForestryMaterials(root);

                if (existing != null)
                {
                    var prev = existing.GetComponent<TreeFarmPlot>();
                    if (prev != null && prev.baseGrowTimeSeconds > 1f)
                        plot.baseGrowTimeSeconds = prev.baseGrowTimeSeconds;
                }

                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject EnsureAutoTreeFarmPrefab(
            string prefabPath,
            ItemDefinition woodLog,
            ItemDefinition sapling,
            ItemDefinition biomass,
            ItemDefinition woodChips,
            out BlockItem blockItem)
        {
            blockItem = EnsureBlockItem(
                $"{ItemsFolder}/Block_AutoTreeFarm.asset",
                "block_auto_tree_farm",
                "Automatic Tree Farm",
                "Self-contained 4x3x4 automated forestry plot and gantry saw-harvester. Grows full-sized timber trees in its central irrigated bed, automatically fells and replants them, tends nearby Tree Farming Plots within 10 m, and pushes Wood Logs, Saplings, Biomass, and Wood Chips to adjacent belts or chutes.",
                new Color(0.26f, 0.76f, 0.34f),
                new Vector3Int(4, 3, 4),
                maxHp: 650,
                category: "Farming");

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var root = new GameObject("AutoTreeFarm");
            try
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0f, 0f);
                box.size = new Vector3(3.9f, 2.9f, 3.9f);

                var pb = root.AddComponent<PlacedBlock>();
                pb.Item = blockItem;
                pb.Hp = 650;

                var power = root.AddComponent<PowerConsumer>();
                power.wattsPerSecond = 85f;
                power.connectRadius = 2.4f;

                var ports = root.AddComponent<PortConfig>();
                ports.EnsureAllFaces();

                var farm = root.AddComponent<AutoTreeFarm>();
                farm.woodLogItem = woodLog;
                farm.saplingItem = sapling;
                farm.biomassItem = biomass;
                farm.woodChipsItem = woodChips;
                farm.EnsureContainers();
                farm.EnsureTanks();
                farm.RebuildVisuals();
                AssignForestryMaterials(root);

                if (root.GetComponent<ItemPortRouting>() == null)
                    root.AddComponent<ItemPortRouting>();

                if (existing != null)
                {
                    var prev = existing.GetComponent<AutoTreeFarm>();
                    if (prev != null)
                    {
                        if (prev.baseWattsPerSecond > 1f) farm.baseWattsPerSecond = prev.baseWattsPerSecond;
                        if (prev.baseGrowCycleSeconds > 5f) farm.baseGrowCycleSeconds = prev.baseGrowCycleSeconds;
                    }
                }

                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject EnsureBiomassPressPrefab(
            string prefabPath,
            ItemDefinition biomass,
            ItemDefinition briquette,
            ItemDefinition woodChips,
            List<ProcessingRecipe> recipes,
            out BlockItem blockItem)
        {
            blockItem = EnsureBlockItem(
                $"{ItemsFolder}/Block_BiomassPress.asset",
                "block_biomass_press",
                "Biomass Press",
                "Industrial 2x2x2 screw-press and bio-refinery. Compresses harvested crops, seeds, wood logs, wood chips, and biomass into solid Biomass, Biofuel Briquettes, and piped Liquid Biofuel.",
                new Color(0.52f, 0.86f, 0.22f),
                new Vector3Int(2, 2, 2),
                maxHp: 480,
                category: "Machines");

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var root = new GameObject("BiomassPress");
            try
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0f, 0f);
                box.size = new Vector3(1.92f, 1.92f, 1.92f);

                var pb = root.AddComponent<PlacedBlock>();
                pb.Item = blockItem;
                pb.Hp = 480;

                var power = root.AddComponent<PowerConsumer>();
                power.wattsPerSecond = 90f;
                power.connectRadius = 1.9f;

                var ports = root.AddComponent<PortConfig>();
                ports.EnsureAllFaces();

                var press = root.AddComponent<BiomassPress>();
                press.biomassItem = biomass;
                press.biofuelBriquetteItem = briquette;
                press.woodChipsItem = woodChips;
                press.knownRecipes = new List<ProcessingRecipe>(recipes);
                press.EnsureContainers();
                press.EnsureTanks();
                press.RebuildVisuals();
                AssignForestryMaterials(root);

                if (root.GetComponent<ItemPortRouting>() == null)
                    root.AddComponent<ItemPortRouting>();

                if (existing != null)
                {
                    var prev = existing.GetComponent<BiomassPress>();
                    if (prev != null && prev.baseWattsPerSecond > 1f)
                        press.baseWattsPerSecond = prev.baseWattsPerSecond;
                }

                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AssignForestryMaterials(GameObject root)
        {
            if (root == null) return;
            string matFolder = $"{AssetRoot}/Materials/Forestry";

            var darkSteel  = GetOrCreateMaterial($"{matFolder}/Mat_ForestryDarkSteel.mat",  new Color(0.20f, 0.23f, 0.26f));
            var midSteel   = GetOrCreateMaterial($"{matFolder}/Mat_ForestryMidSteel.mat",   new Color(0.30f, 0.34f, 0.38f));
            var bioGreen   = GetOrCreateMaterial($"{matFolder}/Mat_ForestryBioGreen.mat",   new Color(0.24f, 0.56f, 0.30f));
            var biofuelGlow= GetOrCreateMaterial($"{matFolder}/Mat_ForestryBiofuelGlow.mat",new Color(0.52f, 0.86f, 0.22f), emissive: true);
            var waterBlue  = GetOrCreateMaterial($"{matFolder}/Mat_ForestryWaterBlue.mat",  new Color(0.20f, 0.62f, 0.88f));
            var soilLoam   = GetOrCreateMaterial($"{matFolder}/Mat_ForestrySoilLoam.mat",   new Color(0.26f, 0.17f, 0.10f));
            var timberWood = GetOrCreateMaterial($"{matFolder}/Mat_ForestryTimberWood.mat", new Color(0.42f, 0.27f, 0.14f));
            var barkBrown  = GetOrCreateMaterial($"{matFolder}/Mat_ForestryBarkBrown.mat",  new Color(0.38f, 0.24f, 0.12f));
            var canopyLeaf = GetOrCreateMaterial($"{matFolder}/Mat_ForestryCanopyLeaf.mat", new Color(0.18f, 0.54f, 0.22f));
            var hazardYel  = GetOrCreateMaterial($"{matFolder}/Mat_ForestryHazardYel.mat",  new Color(0.92f, 0.70f, 0.16f));
            var copperPipe = GetOrCreateMaterial($"{matFolder}/Mat_ForestryCopperPipe.mat", new Color(0.76f, 0.46f, 0.24f));

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                string n = r.gameObject.name;
                if (n.Contains("Soil") || n.Contains("Mulch"))
                    r.sharedMaterial = soilLoam;
                else if (n.Contains("Trunk"))
                    r.sharedMaterial = barkBrown;
                else if (n.Contains("Canopy"))
                    r.sharedMaterial = canopyLeaf;
                else if (n.Contains("Border_") || n.Contains("Post_"))
                    r.sharedMaterial = timberWood;
                else if (n.Contains("Water") || n.Contains("Mist") || n.Contains("Irrig"))
                    r.sharedMaterial = waterBlue;
                else if (n.Contains("Biofuel") || n.Contains("StatusBeacon"))
                    r.sharedMaterial = biofuelGlow;
                else if (n.Contains("PressHousing") || n.Contains("BioTank") || n.Contains("SawCarriage"))
                    r.sharedMaterial = bioGreen;
                else if (n.Contains("Hazard") || n.Contains("Gantry_"))
                    r.sharedMaterial = hazardYel;
                else if (n.Contains("Pipe") || n.Contains("Manifold"))
                    r.sharedMaterial = copperPipe;
                else if (n.Contains("Ram") || n.Contains("Blade") || n.Contains("Rail") || n.Contains("Chute") || n.Contains("Hopper"))
                    r.sharedMaterial = midSteel;
                else
                    r.sharedMaterial = darkSteel;
            }
        }

        private static Material GetOrCreateMaterial(string path, Color color, bool emissive = false)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Sprites/Default");
            mat = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.30f);
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 1.25f);
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ASSET & RECIPE HELPERS
        // ════════════════════════════════════════════════════════════════════════

        private static SeedItem EnsureSeedItem(
            string path, string id, string displayName, string description, Color tint)
        {
            var item = AssetDatabase.LoadAssetAtPath<SeedItem>(path);
            bool created = item == null;
            if (created)
            {
                item = ScriptableObject.CreateInstance<SeedItem>();
                AssetDatabase.CreateAsset(item, path);
            }
            if (created || string.IsNullOrEmpty(item.itemId)) item.itemId = id;
            if (created || string.IsNullOrEmpty(item.displayName)) item.displayName = displayName;
            if (created || string.IsNullOrEmpty(item.description)) item.description = description;
            if (created)
            {
                item.iconTint = tint;
                item.maxStack = 200;
                item.massPerUnit = 0.25f;
                item.category = "Farming";
            }
            EditorUtility.SetDirty(item);
            return item;
        }

        private static ResourceItem EnsureResourceItem(
            string path, string id, string displayName, string description,
            Color tint, ResourceCategory resCat, string category, int maxStack, float fuelSeconds)
        {
            var item = AssetDatabase.LoadAssetAtPath<ResourceItem>(path);
            bool created = item == null;
            if (created)
            {
                item = ScriptableObject.CreateInstance<ResourceItem>();
                AssetDatabase.CreateAsset(item, path);
            }
            if (created || string.IsNullOrEmpty(item.itemId)) item.itemId = id;
            if (created || string.IsNullOrEmpty(item.displayName)) item.displayName = displayName;
            if (created || string.IsNullOrEmpty(item.description)) item.description = description;
            if (created)
            {
                item.iconTint = tint;
                item.maxStack = maxStack;
                item.massPerUnit = 0.5f;
                item.category = category;
                item.resourceCategory = resCat;
                item.fuelSeconds = fuelSeconds;
            }
            else if (fuelSeconds > 0f && item.fuelSeconds <= 0f)
            {
                item.fuelSeconds = fuelSeconds;
                item.resourceCategory = ResourceCategory.Fuel;
            }
            EditorUtility.SetDirty(item);
            return item;
        }

        private static BlockItem EnsureBlockItem(
            string path, string id, string displayName, string description,
            Color tint, Vector3Int footprint, int maxHp, string category)
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
                item.massPerUnit = 12f;
                item.category = category;
                item.blockHealth = maxHp;
                item.miningTier = 1;
            }
            item.gridSize = footprint;
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

        private static ProcessingRecipe EnsureProcessingRecipe(
            string path, string displayName, string category, float seconds,
            (ItemDefinition item, int count)[] inputs,
            (ItemDefinition item, int count)[] outputs,
            (LiquidType liquid, float litres)[] fluidInputs,
            (LiquidType liquid, float litres)[] fluidOutputs)
        {
            var rec = AssetDatabase.LoadAssetAtPath<ProcessingRecipe>(path);
            bool created = rec == null;
            if (created)
            {
                rec = ScriptableObject.CreateInstance<ProcessingRecipe>();
                AssetDatabase.CreateAsset(rec, path);
            }
            rec.displayName = displayName;
            rec.category = category;
            if (created || rec.secondsPerBatch <= 0.1f)
                rec.secondsPerBatch = seconds;

            if (inputs != null)
            {
                var list = new List<ProcessingIO>();
                for (int i = 0; i < inputs.Length; i++)
                    if (inputs[i].item != null && inputs[i].count > 0)
                        list.Add(new ProcessingIO { item = inputs[i].item, count = inputs[i].count });
                rec.inputs = list.ToArray();
            }

            if (outputs != null)
            {
                var list = new List<ProcessingIO>();
                for (int i = 0; i < outputs.Length; i++)
                    if (outputs[i].item != null && outputs[i].count > 0)
                        list.Add(new ProcessingIO { item = outputs[i].item, count = outputs[i].count });
                rec.outputs = list.ToArray();
            }

            if (fluidInputs != null)
            {
                var list = new List<FluidIO>();
                for (int i = 0; i < fluidInputs.Length; i++)
                    if (fluidInputs[i].litres > 0f)
                        list.Add(new FluidIO { liquid = fluidInputs[i].liquid, litres = fluidInputs[i].litres });
                rec.fluidInputs = list.ToArray();
            }

            if (fluidOutputs != null)
            {
                var list = new List<FluidIO>();
                for (int i = 0; i < fluidOutputs.Length; i++)
                    if (fluidOutputs[i].litres > 0f)
                        list.Add(new FluidIO { liquid = fluidOutputs[i].liquid, litres = fluidOutputs[i].litres });
                rec.fluidOutputs = list.ToArray();
            }

            EditorUtility.SetDirty(rec);
            return rec;
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

        private static void WireScatterTreesWithSapling(ItemDefinition sapling, ItemDefinition woodLog)
        {
            if (sapling == null) return;
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.StartsWith("Assets/VoxelEngineAssets")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var tree = go.GetComponent<VoxelEngine.Trees.Tree>();
                if (tree == null) continue;
                bool dirty = false;
                if (tree.saplingItem == null) { tree.saplingItem = sapling; dirty = true; }
                if (tree.logItem == null && woodLog != null) { tree.logItem = woodLog; dirty = true; }
                if (dirty) EditorUtility.SetDirty(go);
            }
        }

        private static void EnsureBiofuelResearchNode(
            RecipeDefinition recSapling,
            RecipeDefinition recTreePlot,
            RecipeDefinition recFertilizer,
            RecipeDefinition recBriquette,
            RecipeDefinition recAutoTreeFarm,
            RecipeDefinition recBiomassPress,
            ItemDefinition blockTreePlot,
            ItemDefinition blockAutoTreeFarm,
            ItemDefinition blockBiomassPress)
        {
            EnsureFolder(AssetRoot, "Research");
            string nodePath = $"{ResearchFolder}/Research_BiofuelAndForestry.asset";
            var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(nodePath);
            bool created = node == null;
            if (created)
            {
                node = ScriptableObject.CreateInstance<ResearchNode>();
                AssetDatabase.CreateAsset(node, nodePath);
            }
            if (created || string.IsNullOrEmpty(node.nodeId)) node.nodeId = "research_biofuel_forestry";
            if (created || string.IsNullOrEmpty(node.displayName)) node.displayName = "Industrial Forestry & Biofuel Refining";
            if (created || string.IsNullOrEmpty(node.description))
                node.description = "Unlocks the Automatic Tree Farm, Tree Farming Plots, and the Biomass Press for sustainable timber harvesting and liquid/solid Biofuel refining.";
            if (created) node.researchSeconds = 40f;

            AddRecipeUnlock(node, recSapling);
            AddRecipeUnlock(node, recTreePlot);
            AddRecipeUnlock(node, recFertilizer);
            AddRecipeUnlock(node, recBriquette);
            AddRecipeUnlock(node, recAutoTreeFarm);
            AddRecipeUnlock(node, recBiomassPress);

            var unlockItems = new List<ItemDefinition>(node.unlocksItems ?? System.Array.Empty<ItemDefinition>());
            if (blockTreePlot != null && !unlockItems.Contains(blockTreePlot)) unlockItems.Add(blockTreePlot);
            if (blockAutoTreeFarm != null && !unlockItems.Contains(blockAutoTreeFarm)) unlockItems.Add(blockAutoTreeFarm);
            if (blockBiomassPress != null && !unlockItems.Contains(blockBiomassPress)) unlockItems.Add(blockBiomassPress);
            node.unlocksItems = unlockItems.ToArray();
            EditorUtility.SetDirty(node);

            string[] treeGuids = AssetDatabase.FindAssets("t:ResearchTree");
            for (int i = 0; i < treeGuids.Length; i++)
            {
                var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(AssetDatabase.GUIDToAssetPath(treeGuids[i]));
                if (tree != null && !tree.nodes.Contains(node))
                {
                    tree.nodes.Add(node);
                    EditorUtility.SetDirty(tree);
                }
            }
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

        private static void RegisterInPersistenceCatalog(params ItemDefinition[] items)
        {
            var cat = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(CatalogPath);
            if (cat == null) return;
            bool dirty = false;
            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i];
                if (it == null) continue;
                if (!cat.items.Contains(it))
                {
                    cat.items.Add(it);
                    dirty = true;
                }
            }
            if (dirty) EditorUtility.SetDirty(cat);
        }

        private static ItemDefinition FindItem(string assetName, string itemId, string displayName)
        {
            var direct = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/{assetName}.asset")
                      ?? AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{FarmItemsFolder}/{assetName}.asset");
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
