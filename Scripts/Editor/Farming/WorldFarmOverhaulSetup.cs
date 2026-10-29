#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Farming;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Non-destructive Setup Wizard step (18.3.0-dev) that puts the world-side farm
    /// overhaul into an existing project.
    ///
    /// Created when missing:
    ///   * Small Plant Box, Big Plant Box and Large Plant Bed - three separate blocks,
    ///     because a 1x1 bed and a 3x3 bed are not the same item at two prices. Each is a
    ///     prefab, a block item, an icon and a recipe;
    ///   * Auto Farm Plot - the powered, self-emptying version of tilled soil. Tilled Soil
    ///     itself is left exactly as it is and stays the manual early-game block.
    ///
    /// Repaired when present, never rewired from scratch:
    ///   * the Sprinkler prefab gains the 18.3.0 body (mast, sight glass, rotating head)
    ///     and its new tank fields, keeping the authored wattage;
    ///   * the Automatic Tree Farm rebuilds its body so the input hopper and output bin
    ///     stand outside the frame instead of behind it.
    ///
    /// Nothing is deleted and no authored number is overwritten: an existing field keeps
    /// its value, an existing prefab keeps its tuning, and every asset only ever gains
    /// components it did not have.
    ///
    /// Editor-only: invoked from Tools -> Voxel Engine -> Voxel Engine Setup.
    /// </summary>
    public static class WorldFarmOverhaulSetup
    {
        private const string AssetRoot         = "Assets/VoxelEngineAssets";
        private const string FarmRoot          = AssetRoot + "/Farming";
        private const string FarmPrefabsFolder = FarmRoot + "/Prefabs";
        private const string FarmBlocksFolder  = FarmRoot + "/Blocks";
        private const string FarmRecipesFolder = FarmRoot + "/Recipes";
        private const string ForestryMats      = AssetRoot + "/Materials/Forestry";
        private const string IconRoot          = AssetRoot + "/ItemIcons/Farming";
        private const string RecipeRegistryPath = AssetRoot + "/RecipeRegistry.asset";
        private const string CatalogPath       = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        public static void RunStep124()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Farm Overhaul Setup",
                    "Exit Play Mode before running setup.", "OK");
                return;
            }

            var created = new List<string>();
            var repaired = new List<string>();
            var preserved = new List<string>();

            EnsureFolders();

            // Icons first, deliberately. They depend on nothing but the folder and the item
            // ids, so if anything later in this step throws, the art the player sees is
            // already on disk. 18.6.1-dev moved this above the block authoring for exactly
            // that reason: a compile error in this file had meant no icon was ever written.
            EnsureFarmIcons(created, repaired);

            var woodLog = FindItem("Item_WoodLog");
            var clay = FindItem("Item_Clay");
            var ironPlate = FindItem("Item_IronPlate");
            var steelPlate = FindItem("Item_SteelPlate") ?? ironPlate;
            var copperWire = FindItem("Item_CopperWire");
            var glass = FindItem("Item_Glass");
            var circuit = FindItem("Item_Circuit");
            var sapling = FindItem("Seed_TreeSapling");
            var biomass = FindItem("Item_Biomass");

            // ── 1) Three planting boxes ─────────────────────────────────────
            var small = EnsurePlantBox(PlantBoxSize.Small, "PlantBoxSmall", "Block_PlantBoxSmall",
                "block_plant_box_small", "Small Plant Box", 1, 40f,
                "A one-metre planting box. Right-click with a seed to plant, right-click when ripe to harvest, "
                + "fertilize once with biomass or Organic Fertilizer, and water it by hand with a Liquid Canister "
                + "or automatically with a Sprinkler. Holds one crop.",
                new Color(0.34f, 0.66f, 0.32f),
                woodLog, clay, ironPlate, copperWire, glass, sapling, biomass,
                created, repaired, preserved);

            var big = EnsurePlantBox(PlantBoxSize.Big, "PlantBoxBig", "Block_PlantBoxBig",
                "block_plant_box_big", "Big Plant Box", 2, 60f,
                "A two-by-two planting box with four cells. Same rules as the small box - hand water or a Sprinkler, "
                + "one fertilizer per cell - but four crops share one tank.",
                new Color(0.30f, 0.64f, 0.34f),
                woodLog, clay, ironPlate, copperWire, glass, sapling, biomass,
                created, repaired, preserved);

            var large = EnsurePlantBox(PlantBoxSize.Large, "PlantBedLarge", "Block_PlantBedLarge",
                "block_plant_bed_large", "Large Plant Bed", 3, 90f,
                "A three-by-three raised bed with nine cells and a trellis. The only planting box that accepts a "
                + "tree sapling: one cell can carry a tree to full timber while the other eight grow crops. Hand water, "
                + "or connect a Sprinkler to keep the whole bed irrigated.",
                new Color(0.26f, 0.60f, 0.30f),
                woodLog, clay, ironPlate, copperWire, glass, sapling, biomass,
                created, repaired, preserved);

            EnsureRecipe("Recipe_PlantBoxSmall", "Small Plant Box", small, StationTier.CraftingBench,
                created, repaired, preserved, ((ItemDefinition)woodLog, 4), ((ItemDefinition)clay, 2));
            EnsureRecipe("Recipe_PlantBoxBig", "Big Plant Box", big, StationTier.CraftingBench,
                created, repaired, preserved, ((ItemDefinition)woodLog, 8), ((ItemDefinition)clay, 4), ((ItemDefinition)ironPlate, 2));
            EnsureRecipe("Recipe_PlantBedLarge", "Large Plant Bed", large, StationTier.Assembler,
                created, repaired, preserved, ((ItemDefinition)woodLog, 12), ((ItemDefinition)clay, 8),
                ((ItemDefinition)ironPlate, 4), ((ItemDefinition)copperWire, 4));

            // ── 2) Auto Farm Plots (Small / Medium / Large) ─────────────────
            EnsureAutoFarmPlots(ironPlate, steelPlate, clay, glass, copperWire,
                woodLog, sapling, biomass, created, repaired, preserved);

            // ── 3) Sprinkler rebuild ────────────────────────────────────────
            RepairSprinkler(ironPlate, copperWire, glass, created, repaired, preserved);

            // ── 4) Automatic Tree Farm restyle ──────────────────────────────
            RepairAutoTreeFarm(woodLog, sapling, biomass, repaired, preserved);

            // ── 5) Icons ────────────────────────────────────────────────────
            // A second chance: the pass at the top of this step is the one that matters, but
            // an icon deleted between then and now is simply re-authored.
            EnsureFarmIcons(created, repaired);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 18.5.0-dev: report what is on disk, not just what the step believes it did.
            // "I don't think it got made" is unanswerable from a count, so every asset the
            // round claims is checked here by path and named MISSING when it is not there.
            var missing = new List<string>();
            string[] required =
            {
                FarmBlocksFolder + "/Block_PlantBoxSmall.asset",
                FarmBlocksFolder + "/Block_PlantBoxBig.asset",
                FarmBlocksFolder + "/Block_PlantBedLarge.asset",
                FarmBlocksFolder + "/Block_AutoFarmPlot.asset",
                FarmBlocksFolder + "/Block_AutoFarmPlotMedium.asset",
                FarmBlocksFolder + "/Block_AutoFarmPlotLarge.asset",
                FarmBlocksFolder + "/Block_Sprinkler.asset",
                FarmPrefabsFolder + "/PlantBoxSmall.prefab",
                FarmPrefabsFolder + "/PlantBoxBig.prefab",
                FarmPrefabsFolder + "/PlantBedLarge.prefab",
                FarmPrefabsFolder + "/AutoFarmPlot.prefab",
                FarmPrefabsFolder + "/AutoFarmPlotMedium.prefab",
                FarmPrefabsFolder + "/AutoFarmPlotLarge.prefab",
                FarmPrefabsFolder + "/Sprinkler.prefab",
                FarmRecipesFolder + "/Recipe_PlantBoxSmall.asset",
                FarmRecipesFolder + "/Recipe_PlantBoxBig.asset",
                FarmRecipesFolder + "/Recipe_PlantBedLarge.asset",
                FarmRecipesFolder + "/Recipe_AutoFarmPlot.asset",
                FarmRecipesFolder + "/Recipe_AutoFarmPlotMedium.asset",
                FarmRecipesFolder + "/Recipe_AutoFarmPlotLarge.asset",
                FarmRecipesFolder + "/Recipe_Sprinkler.asset",
                IconRoot + "/block_plant_box_small.png",
                IconRoot + "/block_plant_box_big.png",
                IconRoot + "/block_plant_bed_large.png",
                IconRoot + "/block_auto_farm_plot.png",
                IconRoot + "/block_auto_farm_plot_medium.png",
                IconRoot + "/block_auto_farm_plot_large.png",
            };
            for (int i = 0; i < required.Length; i++)
                if (AssetDatabase.LoadMainAssetAtPath(required[i]) == null) missing.Add(required[i]);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Farm overhaul (18.3.0-dev)");
            sb.AppendLine();
            sb.AppendLine($"Created: {created.Count}");
            AppendList(sb, created);
            sb.AppendLine();
            sb.AppendLine($"Repaired: {repaired.Count}");
            AppendList(sb, repaired);
            sb.AppendLine();
            sb.AppendLine($"Preserved: {preserved.Count}");
            AppendList(sb, preserved);
            sb.AppendLine();
            sb.AppendLine(missing.Count == 0
                ? "On disk: all " + required.Length + " new blocks, prefabs and recipes are present."
                : "MISSING ON DISK (" + missing.Count + "):");
            for (int i = 0; i < missing.Count; i++) sb.AppendLine("  ! " + missing[i]);
            sb.AppendLine();
            sb.AppendLine("Plant boxes: Small = 1 crop, Big = 4, Large = 9 (trees only on Large).");
            sb.AppendLine("Sprinkler: fill by hand with a Liquid Canister, or connect a pipe for automatic refill.");

            Debug.Log("[Farm Overhaul] " + sb.ToString().Replace("\n", "\n[Farm Overhaul] "));
            EditorUtility.DisplayDialog("Farm Overhaul Complete",
                $"Created {created.Count}, repaired {repaired.Count}, preserved {preserved.Count}.\n"
                + (missing.Count == 0
                    ? "All new blocks, prefabs and recipes are on disk.\n\n"
                    : $"{missing.Count} asset(s) are MISSING - see the Console.\n\n")
                + "All five blocks are in the Farming category of the crafting screen; "
                + "the three planting boxes are craftable at a Crafting Bench, the Auto Farm Plot "
                + "and the Large Plant Bed at the Assembler.", "OK");
        }

        // ── Planting boxes ──────────────────────────────────────────────────
        private static BlockItem EnsurePlantBox(PlantBoxSize size, string prefabName, string assetName,
            string itemId, string displayName, int cellsPerSide, float waterCapacity, string description,
            Color tint, ItemDefinition woodLog, ItemDefinition clay, ItemDefinition ironPlate,
            ItemDefinition copperWire, ItemDefinition glass, ItemDefinition sapling, ItemDefinition biomass,
            List<string> created, List<string> repaired, List<string> preserved)
        {
            int side = Mathf.Clamp(cellsPerSide, 1, 3);
            var block = EnsureBlockItem($"{FarmBlocksFolder}/{assetName}.asset", itemId, displayName, description,
                tint, new Vector3Int(side, 1, side), maxHp: 90 + side * side * 30,
                category: "Farming", created, repaired, preserved);

            string prefabPath = $"{FarmPrefabsFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            var root = new GameObject(prefabName);
            try
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.16f, 0f);
                box.size = new Vector3(side * 0.96f, 0.34f, side * 0.96f);

                var pb = root.AddComponent<PlacedBlock>();
                pb.Item = block;
                pb.Hp = block.blockHealth;

                var plant = root.AddComponent<PlantBox>();
                plant.boxSize = size;
                plant.cellsPerSide = side;
                plant.waterCapacity = waterCapacity;
                plant.waterStored = waterCapacity;
                plant.woodLogItem = woodLog;
                plant.saplingItem = sapling;
                plant.biomassItem = biomass;
                plant.autoDumpWhenRipe = true;
                plant.replantAfterPortHarvest = true;
                plant.RebuildVisuals();
                AssignFarmMaterials(root);

                var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                block.placedPrefab = saved;
                EditorUtility.SetDirty(block);

                if (existing != null) preserved.Add($"{displayName} prefab tuning");
                else created.Add($"{displayName} prefab");

                return block;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ── Auto Farm Plot family ───────────────────────────────────────────
        // 18.6.0-dev: one size was not enough. The plot is now three blocks built on the
        // planting-box component - 1, 4 and 9 cells - each with its own tank, its own
        // small power draw and its own output chute. The old single plot keeps its item
        // id, its recipe and its place in the crafting screen; it is simply rebuilt as the
        // Small member of the family, and because `FarmPlot` never saved anything there is
        // no crop state to lose in the swap.
        private static void EnsureAutoFarmPlots(ItemDefinition ironPlate, ItemDefinition steelPlate,
            ItemDefinition clay, ItemDefinition glass, ItemDefinition copperWire,
            ItemDefinition woodLog, ItemDefinition sapling, ItemDefinition biomass,
            List<string> created, List<string> repaired, List<string> preserved)
        {
            var small = EnsureAutoFarmPlot(PlantBoxSize.Small, "AutoFarmPlot", "Block_AutoFarmPlot",
                "block_auto_farm_plot", "Auto Farm Plot", 1, 40f, 8f,
                "The powered one-metre plot. Plant a seed and it hands its harvest to a belt, chute, funnel "
                + "or item pipe standing next to it, replanting itself if self re-seeding is left on. Water it "
                + "by hand with a Liquid Canister or with a Sprinkler. Draws a trickle of power while it grows. "
                + "Tilled Soil stays the manual plot for the early game.",
                new Color(0.44f, 0.62f, 0.30f),
                woodLog, sapling, biomass, created, repaired, preserved);

            var medium = EnsureAutoFarmPlot(PlantBoxSize.Big, "AutoFarmPlotMedium", "Block_AutoFarmPlotMedium",
                "block_auto_farm_plot_medium", "Medium Auto Farm Plot", 2, 60f, 10f,
                "A powered two-by-two plot with four cells. Same rules as the small plot - one tank, one output "
                + "chute, one self re-seed switch - but four crops share the water and the power. Hand water or "
                + "add a Sprinkler.",
                new Color(0.40f, 0.60f, 0.28f),
                woodLog, sapling, biomass, created, repaired, preserved);

            var large = EnsureAutoFarmPlot(PlantBoxSize.Large, "AutoFarmPlotLarge", "Block_AutoFarmPlotLarge",
                "block_auto_farm_plot_large", "Large Auto Farm Plot", 3, 90f, 14f,
                "A powered three-by-three plot: nine cells, one 90 L tank and one output chute. The biggest "
                + "automated bed, and the one that wants a Sprinkler and a belt rather than a canister. "
                + "Trees belong to the Large Plant Bed, not here.",
                new Color(0.36f, 0.58f, 0.26f),
                woodLog, sapling, biomass, created, repaired, preserved);

            EnsureRecipe("Recipe_AutoFarmPlot", "Auto Farm Plot", small, StationTier.Assembler,
                created, repaired, preserved, ((ItemDefinition)ironPlate, 2), ((ItemDefinition)clay, 2), ((ItemDefinition)copperWire, 2));
            EnsureRecipe("Recipe_AutoFarmPlotMedium", "Medium Auto Farm Plot", medium, StationTier.Assembler,
                created, repaired, preserved, ((ItemDefinition)ironPlate, 4), ((ItemDefinition)clay, 4),
                ((ItemDefinition)copperWire, 4), ((ItemDefinition)steelPlate, 2));
            EnsureRecipe("Recipe_AutoFarmPlotLarge", "Large Auto Farm Plot", large, StationTier.Assembler,
                created, repaired, preserved, ((ItemDefinition)steelPlate, 6), ((ItemDefinition)clay, 8),
                ((ItemDefinition)copperWire, 8), ((ItemDefinition)glass, 4));
        }

        /// <summary>
        /// One member of the Auto Farm Plot family: a planting-box bed that draws power while
        /// it grows and empties itself into whatever stands beside it.
        /// </summary>
        private static BlockItem EnsureAutoFarmPlot(PlantBoxSize size, string prefabName, string assetName,
            string itemId, string displayName, int cellsPerSide, float waterCapacity, float watts,
            string description, Color tint, ItemDefinition woodLog, ItemDefinition sapling,
            ItemDefinition biomass, List<string> created, List<string> repaired, List<string> preserved)
        {
            int side = Mathf.Clamp(cellsPerSide, 1, 3);
            var block = EnsureBlockItem($"{FarmBlocksFolder}/{assetName}.asset", itemId, displayName, description,
                tint, new Vector3Int(side, 1, side), maxHp: 110 + side * side * 20,
                category: "Farming", created, repaired, preserved);

            string prefabPath = $"{FarmPrefabsFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            var root = new GameObject(prefabName);
            try
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.16f, 0f);
                box.size = new Vector3(side * 0.96f, 0.34f, side * 0.96f);

                var pb = root.AddComponent<PlacedBlock>();
                pb.Item = block;
                pb.Hp = block.blockHealth;

                var power = root.AddComponent<PowerConsumer>();
                power.wattsPerSecond = watts;
                power.connectRadius = 2f;

                var plot = root.AddComponent<PlantBox>();
                plot.boxSize = size;
                plot.cellsPerSide = side;
                plot.waterCapacity = waterCapacity;
                plot.waterStored = waterCapacity;
                plot.powerDraw = watts;
                plot.requiresPower = true;
                plot.allowTrees = false;      // trees are the Large Plant Bed's job
                plot.autoDumpWhenRipe = true;
                plot.replantAfterPortHarvest = true;
                plot.growthSpeedMultiplier = 0.5f;
                plot.woodLogItem = woodLog;
                plot.saplingItem = sapling;
                plot.biomassItem = biomass;
                plot.RebuildVisuals();
                AssignFarmMaterials(root);

                var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                block.placedPrefab = saved;
                EditorUtility.SetDirty(block);

                if (existing != null) preserved.Add($"{displayName} prefab tuning");
                else created.Add($"{displayName} prefab");

                return block;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ── Sprinkler ───────────────────────────────────────────────────────
        /// <summary>
        /// Repair or create the sprinkler. A project that already has one keeps its authored
        /// numbers: only fields that were never set are filled, and the body is rebuilt so an
        /// old cube becomes the 18.3.0 mast. A project with no sprinkler at all gets one built
        /// from nothing, because "the sprinkler was never made" is a defect in this step, not
        /// a message in a report.
        /// </summary>
        private static void RepairSprinkler(ItemDefinition ironPlate, ItemDefinition copperWire,
            ItemDefinition glass, List<string> created, List<string> repaired, List<string> preserved)
        {
            const string prefabPath = FarmPrefabsFolder + "/Sprinkler.prefab";
            const string blockPath = FarmBlocksFolder + "/Block_Sprinkler.asset";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null
                || AssetDatabase.LoadAssetAtPath<BlockItem>(blockPath) == null)
            {
                CreateSprinkler(ironPlate, copperWire, glass, created, repaired);
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                bool changed = false;

                var power = contents.GetComponent<PowerConsumer>();
                if (power == null)
                {
                    power = contents.AddComponent<PowerConsumer>();
                    power.wattsPerSecond = 20f;
                    power.connectRadius = 2f;
                    changed = true;
                    repaired.Add("Sprinkler power draw");
                }

                var sprinkler = contents.GetComponent<Sprinkler>();
                if (sprinkler == null)
                {
                    sprinkler = contents.AddComponent<Sprinkler>();
                    changed = true;
                    repaired.Add("Sprinkler component");
                }

                // Only fill in what has never been authored; a tuned sprinkler keeps its numbers.
                if (sprinkler.waterCapacity <= 0f) { sprinkler.waterCapacity = 60f; sprinkler.waterStored = 60f; changed = true; }
                if (sprinkler.waterStored <= 0f) { sprinkler.waterStored = sprinkler.waterCapacity; changed = true; }
                if (sprinkler.litresPerSecond <= 0f) { sprinkler.litresPerSecond = 0.4f; changed = true; }
                if (sprinkler.refillLitresPerSecond <= 0f) { sprinkler.refillLitresPerSecond = 2f; changed = true; }
                if (sprinkler.radius <= 0f) { sprinkler.radius = 8f; changed = true; }
                if (sprinkler.wattsWhileSpraying <= 0f) { sprinkler.wattsWhileSpraying = 20f; changed = true; }

                // The old body was a single blue cube; the new one is a mast with a sight
                // glass, so an old prefab is rebuilt rather than left as it is.
                sprinkler.RebuildVisuals();
                AssignFarmMaterials(contents);
                changed = true;

                var collider = contents.GetComponent<BoxCollider>();
                if (collider != null)
                {
                    collider.center = new Vector3(0f, 0.30f, 0f);
                    collider.size = new Vector3(0.60f, 0.70f, 0.60f);
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                    repaired.Add("Sprinkler body (mast, sight glass, rotating head)");
                }
                else preserved.Add("Sprinkler prefab");

                var block = AssetDatabase.LoadAssetAtPath<BlockItem>(blockPath);
                if (block != null)
                {
                    AppendIfMissing(block,
                        "internal tank",
                        " Holds its own water tank: fill it by hand with a Liquid Canister, or connect a pipe "
                        + "for automatic refill. Draws about 20 W while it sprays.",
                        repaired);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Build a sprinkler from nothing: prefab, block item, recipe and registry entry,
        /// so "the sprinkler was never made" is answered by the setup step instead of by a
        /// hunt through folders. Runs only when the prefab or block item is missing.
        /// </summary>
        private static void CreateSprinkler(ItemDefinition ironPlate, ItemDefinition copperWire,
            ItemDefinition glass, List<string> created, List<string> repaired)
        {
            EnsureFolderPath(FarmPrefabsFolder);
            EnsureFolderPath(FarmBlocksFolder);

            var block = EnsureBlockItem($"{FarmBlocksFolder}/Block_Sprinkler.asset",
                "block_sprinkler", "Sprinkler",
                "Powered irrigation mast with its own water tank. Sprays about 0.4 L per second over an 8 m radius "
                + "while it has power and water. Fill the tank by hand with a Liquid Canister, or connect a pipe or "
                + "tank within 3.5 m and it refills itself. Every planting box, farm plot and sapling bed inside the "
                + "radius counts as irrigated.",
                new Color(0.40f, 0.65f, 0.85f), new Vector3Int(1, 1, 1), maxHp: 200,
                category: "Farming", created, repaired, new List<string>());

            const string prefabPath = FarmPrefabsFolder + "/Sprinkler.prefab";
            var root = new GameObject("Sprinkler");
            try
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.32f, 0f);
                box.size = new Vector3(0.62f, 0.72f, 0.62f);

                var pb = root.AddComponent<PlacedBlock>();
                pb.Item = block;
                pb.Hp = block.blockHealth;

                var power = root.AddComponent<PowerConsumer>();
                power.wattsPerSecond = 20f;
                power.connectRadius = 2f;

                var sprinkler = root.AddComponent<Sprinkler>();
                sprinkler.radius = 8f;
                sprinkler.requiresWaterConnection = true;
                sprinkler.waterCapacity = 60f;
                sprinkler.waterStored = 60f;
                sprinkler.litresPerSecond = 0.4f;
                sprinkler.refillLitresPerSecond = 2f;
                sprinkler.wattsWhileSpraying = 20f;
                sprinkler.RebuildVisuals();
                AssignFarmMaterials(root);

                var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                block.placedPrefab = saved;
                EditorUtility.SetDirty(block);
                created.Add("Sprinkler prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            EnsureRecipe("Recipe_Sprinkler", "Sprinkler", block, StationTier.CraftingBench,
                created, repaired, new List<string>(),
                ((ItemDefinition)ironPlate, 2), ((ItemDefinition)copperWire, 4));
        }

        // ── Automatic Tree Farm ─────────────────────────────────────────────
        private static void RepairAutoTreeFarm(ItemDefinition woodLog, ItemDefinition sapling,
            ItemDefinition biomass, List<string> repaired, List<string> preserved)
        {
            const string prefabPath = FarmPrefabsFolder + "/AutoTreeFarm.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                preserved.Add("Automatic Tree Farm prefab (not present yet - run the forestry content step first)");
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var farm = contents.GetComponent<AutoTreeFarm>();
                if (farm == null)
                {
                    preserved.Add("Automatic Tree Farm (component missing)");
                    return;
                }

                if (woodLog != null) farm.woodLogItem = woodLog;
                if (sapling != null) farm.saplingItem = sapling;
                if (biomass != null) farm.biomassItem = biomass;

                farm.EnsureContainers();
                farm.EnsureTanks();
                farm.RebuildVisuals();
                AssignFarmMaterials(contents);

                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                repaired.Add("Automatic Tree Farm body (machine fills the frame, hopper and output bin outside)");

                var block = AssetDatabase.LoadAssetAtPath<BlockItem>($"{FarmBlocksFolder}/Block_AutoTreeFarm.asset");
                if (block != null)
                {
                    AppendIfMissing(block,
                        "hopper",
                        " Saplings and fertilizer go into the intake hopper on one side; logs, saplings, biomass "
                        + "and wood chips come out of the open bin on the other.",
                        repaired);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // ── Icons ───────────────────────────────────────────────────────────
        private enum IconKind { SmallBox, BigBox, LargeBed, AutoPlot, AutoPlotMedium, AutoPlotLarge, VerticalFarm, FarmBox }

        /// <summary>
        /// 18.4.0-dev step: the two grid farm plots are authored by the grid content step
        /// inside the wizard; this step gives them their icons and makes sure their items
        /// can survive a save. It never creates the prefabs - that is the grid step's job.
        /// </summary>
        public static void RunStep125()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            const string gridIconRoot = AssetRoot + "/ItemIcons/Grid Blocks";
            EnsureIcon("gitem_verticalfarm", IconKind.VerticalFarm, gridIconRoot);
            EnsureIcon("gitem_farmbox", IconKind.FarmBox, gridIconRoot);

            var vertical = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{AssetRoot}/Items/GItem_VerticalFarm.asset");
            var farmBox = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{AssetRoot}/Items/GItem_FarmBox.asset");
            EnsureCatalogEntry(vertical);
            EnsureCatalogEntry(farmBox);

            if (vertical == null || farmBox == null)
            {
                Debug.LogWarning("[Farm Overhaul] Grid farm items are missing - run the grid block content step first, then this step.");
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Farm Overhaul] Grid farm plots: icons and save catalog ready.");
        }

        /// <summary>
        /// Author a 128x128 block icon at the ItemIcons convention path, only when the file
        /// is not there yet - the same rule the radar sticker follows. Hand-drawn art is
        /// never overwritten; delete the PNG to regenerate it.
        /// </summary>
        /// <summary>
        /// Author and bind every farm icon this round owns. Safe to call repeatedly and safe
        /// to call on its own: an existing PNG is never overwritten, and an item that already
        /// has the right sprite is left alone.
        /// </summary>
        public static void EnsureFarmIcons(List<string> created, List<string> repaired)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            EnsureFolderPath(IconRoot);

            var wanted = new (string itemId, IconKind kind)[]
            {
                ("block_plant_box_small",       IconKind.SmallBox),
                ("block_plant_box_big",         IconKind.BigBox),
                ("block_plant_bed_large",       IconKind.LargeBed),
                ("block_auto_farm_plot",        IconKind.AutoPlot),
                ("block_auto_farm_plot_medium", IconKind.AutoPlotMedium),
                ("block_auto_farm_plot_large",  IconKind.AutoPlotLarge),
            };

            for (int i = 0; i < wanted.Length; i++)
            {
                string path = $"{IconRoot}/{wanted[i].itemId}.png";
                bool existed = System.IO.File.Exists(path);
                EnsureIcon(wanted[i].itemId, wanted[i].kind, IconRoot);

                if (!existed && System.IO.File.Exists(path))
                {
                    created?.Add($"{wanted[i].itemId} icon");
                    continue;
                }

                // The PNG was already there but the item may still have lost its binding -
                // a re-import drops the reference and the slot goes blank. Rebind in place.
                if (BindIcon(path, wanted[i].itemId)) repaired?.Add($"{wanted[i].itemId} icon binding");
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>Give every item claiming this id the sprite at <paramref name="path"/>.</summary>
        private static bool BindIcon(string path, string itemId)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) return false;

            bool rebound = false;
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null) continue;
                if (!string.Equals(item.itemId, itemId, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (item.icon == sprite) continue;
                item.icon = sprite;
                EditorUtility.SetDirty(item);
                rebound = true;
            }
            return rebound;
        }

        /// <summary>
        /// Standalone entry point so the icons can be repaired without touching a single
        /// prefab or recipe: "Repair farm item icons" in the Farming &amp; Greenhouse group.
        /// </summary>
        public static void RunIconRepair()
        {
            var created = new List<string>();
            var repaired = new List<string>();
            EnsureFarmIcons(created, repaired);

            // The two grid plots live in their own icon folder and their own step.
            const string gridIconRoot = AssetRoot + "/ItemIcons/Grid Blocks";
            EnsureFolderPath(gridIconRoot);
            var gridIcons = new (string itemId, IconKind kind)[]
            {
                ("gitem_verticalfarm", IconKind.VerticalFarm),
                ("gitem_farmbox",      IconKind.FarmBox),
            };
            for (int i = 0; i < gridIcons.Length; i++)
            {
                string path = $"{gridIconRoot}/{gridIcons[i].itemId}.png";
                bool existed = System.IO.File.Exists(path);
                EnsureIcon(gridIcons[i].itemId, gridIcons[i].kind, gridIconRoot);
                if (!existed && System.IO.File.Exists(path)) created.Add($"{gridIcons[i].itemId} icon");
                else if (BindIcon(path, gridIcons[i].itemId)) repaired.Add($"{gridIcons[i].itemId} icon binding");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary = $"Farm icons: {created.Count} authored, {repaired.Count} rebound.";
            Debug.Log("[Farm Overhaul] " + summary);
            EditorUtility.DisplayDialog("Farm Icons",
                summary + "\n\n" + IconRoot + "\n" + gridIconRoot
                + "\n\nIf a slot is still blank the item is drawing its iconTint instead: "
                + "select the item asset and check that Icon is set.", "OK");
        }

        private static void EnsureIcon(string itemId, IconKind kind, string iconRoot)
        {
            string path = $"{iconRoot}/{itemId}.png";
            if (System.IO.File.Exists(path)) return;

            EnsureFolderPath(iconRoot);

            const int S = 128;
            var px = new Color32[S * S];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);

            Color32 soil = new Color32(74, 52, 30, 255);
            Color32 soilDark = new Color32(52, 36, 21, 255);
            Color32 timber = new Color32(108, 74, 42, 255);
            Color32 leaf = new Color32(86, 156, 74, 255);
            Color32 steel = new Color32(120, 128, 138, 255);
            Color32 water = new Color32(58, 148, 214, 255);
            Color32 glow = new Color32(132, 214, 96, 255);

            if (kind == IconKind.VerticalFarm)
            {
                // Three stacked trays on a mast - a silhouette nothing else on the grid has.
                for (int t = 0; t < 3; t++)
                {
                    int ty0 = 26 + t * 26;
                    int ty1 = ty0 + 18;
                    for (int y = ty0; y < ty1; y++)
                    {
                        for (int x = 20; x < 108; x++)
                        {
                            bool edge = x < 24 || x >= 104 || y == ty0 || y == ty1 - 1;
                            px[y * S + x] = edge ? steel : (((x + y) / 5) % 2 == 0 ? leaf : soil);
                        }
                    }
                    for (int x = 44; x < 84; x++) px[(ty0 - 1) * S + x] = water;
                }
                for (int y = 18; y < 112; y++)
                {
                    px[y * S + 24] = steel;
                    px[y * S + 103] = steel;
                }

                var texV = new Texture2D(S, S, TextureFormat.RGBA32, false);
                texV.SetPixels32(px);
                texV.Apply();
                System.IO.File.WriteAllBytes(path, texV.EncodeToPNG());
                Object.DestroyImmediate(texV);
                FinishIconImport(path, itemId);
                return;
            }

            if (kind == IconKind.FarmBox)
            {
                // Nine cells with the tree socket picked out in leaf green, seen from above.
                for (int y = 24; y < 104; y++)
                {
                    for (int x = 24; x < 104; x++)
                    {
                        bool edge = x < 30 || x >= 98 || y < 30 || y >= 98;
                        if (edge) { px[y * S + x] = steel; continue; }
                        int cx = (x - 30) / 23, cy = (y - 30) / 23;
                        bool tree = cx == 2 && cy == 2;
                        px[y * S + x] = tree ? leaf : (((cx + cy) % 2 == 0) ? soil : soilDark);
                    }
                }
                for (int y = 44; y < 68; y++)
                    for (int x = 56; x < 72; x++)
                        px[y * S + x] = timber;

                var texF = new Texture2D(S, S, TextureFormat.RGBA32, false);
                texF.SetPixels32(px);
                texF.Apply();
                System.IO.File.WriteAllBytes(path, texF.EncodeToPNG());
                Object.DestroyImmediate(texF);
                FinishIconImport(path, itemId);
                return;
            }

            // A tray seen at a slight angle: a base slab, a rim, then cells on top.
            int cells = kind == IconKind.SmallBox || kind == IconKind.AutoPlot ? 1
                : kind == IconKind.BigBox || kind == IconKind.AutoPlotMedium ? 2 : 3;
            bool auto = kind == IconKind.AutoPlot || kind == IconKind.AutoPlotMedium || kind == IconKind.AutoPlotLarge;

            int left = 16, right = 112, top = 40, bottom = 104;
            int span = right - left;
            int cellSpan = span / Mathf.Max(1, cells);

            for (int y = top; y < bottom; y++)
            {
                for (int x = left; x < right; x++)
                {
                    int cx = (x - left) / Mathf.Max(1, cellSpan);
                    int cy = (y - top) / Mathf.Max(1, cellSpan);
                    bool edge = x < left + 5 || x >= right - 5 || y < top + 5 || y >= bottom - 5;

                    Color32 c;
                    if (edge) c = timber;
                    else
                    {
                        // Rapeseed-green crop rows on dark soil, one shade per cell so the
                        // grid reads at inventory size.
                        bool furrow = ((x + y) / 6) % 2 == 0;
                        c = furrow ? leaf : soil;
                        if ((cx + cy) % 2 == 1) c = furrow ? soil : soilDark;
                    }
                    px[y * S + x] = c;
                }
            }

            // Steel band under the tray, and the automatic plot's port chute.
            for (int y = bottom; y < Mathf.Min(S, bottom + 10); y++)
                for (int x = left; x < right; x++)
                    px[y * S + x] = steel;

            if (auto)
            {
                for (int y = bottom - 26; y < bottom - 6; y++)
                    for (int x = right - 6; x < Mathf.Min(S, right + 14); x++)
                        px[y * S + x] = water;
                for (int y = top + 6; y < top + 16; y++)
                    for (int x = left + 10; x < left + 26; x++)
                        px[y * S + x] = glow;
            }

            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();

            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            FinishIconImport(path, itemId);
        }

        private static void FinishIconImport(string path, string itemId)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }

            // Bind the sprite to whichever item claims this id, so the icon shows up
            // without a second pass through ItemIconSync.
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition"))
                {
                    var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    if (item == null || !string.Equals(item.itemId, itemId, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (item.icon != null) continue;
                    item.icon = sprite;
                    EditorUtility.SetDirty(item);
                }
            }

            Debug.Log("[Farm Overhaul] Authored farm icon at " + path + ".");
        }

        // ── Item, recipe and material plumbing ──────────────────────────────
        private static BlockItem EnsureBlockItem(string path, string itemId, string displayName,
            string description, Color tint, Vector3Int footprint, int maxHp, string category,
            List<string> created, List<string> repaired, List<string> preserved)
        {
            var item = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool isNew = item == null;
            if (isNew)
            {
                EnsureFolderPath(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
                item = ScriptableObject.CreateInstance<BlockItem>();
                AssetDatabase.CreateAsset(item, path);
                created.Add(displayName);
            }

            if (string.IsNullOrEmpty(item.itemId)) { item.itemId = itemId; if (!isNew) repaired.Add($"{displayName} id"); }
            if (string.IsNullOrEmpty(item.displayName)) { item.displayName = displayName; }
            if (string.IsNullOrEmpty(item.description)) { item.description = description; if (!isNew) repaired.Add($"{displayName} description"); }
            if (string.IsNullOrEmpty(item.category)) item.category = category;
            if (item.gridSize == Vector3Int.zero) item.gridSize = footprint;
            if (item.blockHealth <= 0) item.blockHealth = maxHp;
            if (item.maxStack <= 0) item.maxStack = 50;

            if (isNew)
            {
                item.iconTint = tint;
                item.massPerUnit = 12f;
                item.miningTier = 1;
                item.allowStacking = true;
            }
            else preserved.Add($"{displayName} tuning");

            EditorUtility.SetDirty(item);
            EnsureCatalogEntry(item);
            return item;
        }

        private static void EnsureRecipe(string assetName, string displayName, ItemDefinition output,
            StationTier station, List<string> created, List<string> repaired, List<string> preserved,
            params (ItemDefinition item, int count)[] inputs)
        {
            if (output == null) return;

            string path = $"{FarmRecipesFolder}/{assetName}.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
            bool isNew = recipe == null;
            if (isNew)
            {
                EnsureFolderPath(FarmRecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, path);
                created.Add($"{displayName} recipe");
            }

            if (recipe.outputItem == null) recipe.outputItem = output;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            if (recipe.requiredStation == StationTier.None && station != StationTier.None) recipe.requiredStation = station;
            if (recipe.craftSeconds <= 0f && station != StationTier.None)
                recipe.craftSeconds = CraftTimeDefaults.Suggest(station, recipe.inputs);
            if (recipe.displayName == null) recipe.displayName = displayName;

            if (isNew)
            {
                recipe.displayName = displayName;
                recipe.requiredStation = station;
                recipe.unlockedByDefault = true;
                var valid = new List<RecipeIngredient>();
                foreach (var (item, count) in inputs)
                    if (item != null && count > 0) valid.Add(new RecipeIngredient { item = item, count = count });
                recipe.inputs = valid.ToArray();
                if (recipe.craftSeconds <= 0f && station != StationTier.None)
                    recipe.craftSeconds = CraftTimeDefaults.Suggest(station, recipe.inputs);
            }
            else preserved.Add($"{displayName} recipe inputs");

            EditorUtility.SetDirty(recipe);

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(RecipeRegistryPath);
            if (registry != null)
            {
                if (registry.recipes == null) registry.recipes = new List<RecipeDefinition>();
                if (!registry.recipes.Contains(recipe))
                {
                    registry.recipes.Add(recipe);
                    EditorUtility.SetDirty(registry);
                    repaired.Add($"{displayName} RecipeRegistry link");
                }
            }
        }

        private static void EnsureCatalogEntry(ItemDefinition item)
        {
            if (item == null) return;

            var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(CatalogPath);
            if (catalog == null)
            {
                EnsureFolderPath("Assets/Resources/VoxelEngine");
                catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            if (catalog.items == null) catalog.items = new List<ItemDefinition>();
            if (catalog.items.Contains(item)) return;

            catalog.items.Add(item);
            EditorUtility.SetDirty(catalog);
        }

        private static ItemDefinition FindItem(string assetFileName)
        {
            var direct = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{AssetRoot}/Items/{assetFileName}.asset");
            if (direct != null) return direct;

            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == assetFileName)
                    return AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            }
            return null;
        }

        /// <summary>
        /// Materials by part name. Same palette the forestry prefabs use, so a planting
        /// box standing next to a Seed Planter looks like it belongs to the same kit.
        /// </summary>
        private static void AssignFarmMaterials(GameObject root)
        {
            if (root == null) return;

            var darkSteel = GetOrCreateMaterial($"{ForestryMats}/Mat_ForestryDarkSteel.mat", new Color(0.20f, 0.23f, 0.26f));
            var midSteel = GetOrCreateMaterial($"{ForestryMats}/Mat_ForestryMidSteel.mat", new Color(0.30f, 0.34f, 0.38f));
            var timber = GetOrCreateMaterial($"{ForestryMats}/Mat_ForestryTimberWood.mat", new Color(0.42f, 0.27f, 0.14f));
            var soil = GetOrCreateMaterial($"{ForestryMats}/Mat_ForestrySoilLoam.mat", new Color(0.26f, 0.17f, 0.10f));
            var leaf = GetOrCreateMaterial($"{ForestryMats}/Mat_ForestryCanopyLeaf.mat", new Color(0.18f, 0.54f, 0.22f));
            var water = GetOrCreateMaterial($"{ForestryMats}/Mat_ForestryWaterBlue.mat", new Color(0.20f, 0.62f, 0.88f));
            var glow = GetOrCreateMaterial($"{ForestryMats}/Mat_ForestryBiofuelGlow.mat", new Color(0.52f, 0.86f, 0.22f), emissive: true);
            var brass = GetOrCreateMaterial($"{ForestryMats}/Mat_ForestryCopperPipe.mat", new Color(0.76f, 0.46f, 0.24f));

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                string n = r.gameObject.name;

                if (n.Contains("Soil") || n.Contains("Mulch")) r.sharedMaterial = soil;
                else if (n.Contains("Crop") || n.Contains("Leaf") || n.Contains("Canopy") || n.Contains("Sapling")) r.sharedMaterial = leaf;
                else if (n.Contains("Curb") || n.Contains("Trellis") || n.Contains("Post")) r.sharedMaterial = timber;
                else if (n.Contains("Water") || n.Contains("Gauge") || n.Contains("Irrig") || n.Contains("Nozzle")) r.sharedMaterial = water;
                else if (n.Contains("Beacon") || n.Contains("Status") || n.Contains("Lamp")) r.sharedMaterial = glow;
                else if (n.Contains("Head") || n.Contains("Base") || n.Contains("Housing") || n.Contains("Glass")) r.sharedMaterial = brass;
                else if (n.Contains("Chute") || n.Contains("Port") || n.Contains("Output") || n.Contains("Hopper")
                         || n.Contains("Bin") || n.Contains("Mast") || n.Contains("Frame")) r.sharedMaterial = midSteel;
                else r.sharedMaterial = darkSteel;
            }
        }

        private static Material GetOrCreateMaterial(string path, Color color, bool emissive = false)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            EnsureFolderPath(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Sprites/Default");
            mat = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.30f);
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 1.25f);
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void AppendIfMissing(ItemDefinition item, string marker, string sentence,
            List<string> repaired)
        {
            if (item == null || string.IsNullOrEmpty(marker)) return;
            if (!string.IsNullOrEmpty(item.description)
                && item.description.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            item.description = string.IsNullOrEmpty(item.description)
                ? sentence.TrimStart()
                : item.description.TrimEnd() + sentence;
            EditorUtility.SetDirty(item);
            repaired.Add($"{item.displayName} description");
        }

        private static void AppendList(System.Text.StringBuilder sb, List<string> items)
        {
            if (items == null || items.Count == 0) { sb.AppendLine("  none"); return; }
            for (int i = 0; i < items.Count; i++) sb.AppendLine("  - " + items[i]);
        }

        private static void EnsureFolders()
        {
            EnsureFolderPath(FarmPrefabsFolder);
            EnsureFolderPath(FarmBlocksFolder);
            EnsureFolderPath(FarmRecipesFolder);
            EnsureFolderPath(IconRoot);
        }

        private static void EnsureFolderPath(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolderPath(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
