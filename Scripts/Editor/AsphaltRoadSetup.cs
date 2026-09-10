// Assets/Scripts/VoxelEngine/Editor/AsphaltRoadSetup.cs
//
// Step 72 — ASPHALT ROADS (9.41.0-dev):
// Authors the road surface block and its whole material chain, non-destructively.
//
//   BITUMEN   Heavy Fuel Oil 40 L  ->  Bitumen x4        (Chemical Plant, both the standing
//                                                          one and the ship one)
//   HOT MIX   Bitumen 1 + Sand 2 + Gravel 3 -> Asphalt x8 (Assembler, every tier)
//   ROAD      Asphalt 1 -> Asphalt Road block             (Crafting Bench, research gated)
//   PAVER     Iron Plate 4 + Iron Gear 2 + Steel Plate 2 -> Road Paver tool
//
// That chain is the design's sentence made literal: the column's heavy end, which nobody has an
// engine for, plus the aggregate the crusher was already making, becomes the surface a schedule
// runs on. It also gives Heavy Fuel Oil a genuine sink, which is the thing 9.38.0-dev left open
// when it retired the refinery fuel chain.
//
// NON-DESTRUCTIVE, per 7.4: create what is missing, connect what exists, never reset a number the
// team has tuned. Balance fields (block health, mining tier, stack size, mass, craft time, recipe
// quantities, research cost, tier and column) are written ONLY when they are still at their
// defaults, so a re-run after a balance pass changes nothing. Display names and descriptions are
// written only when empty, so a rename survives. Asset renames would go through
// `AssetDatabase.MoveAsset` to keep GUIDs — there are none to do in this round, and the block's
// `itemId` is the save key and is never changed once written.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Industrial;
using VoxelEngine.Items;
using VoxelEngine.Research;
using VoxelEngine.Simulation;

namespace VoxelEngine.EditorTools
{
    public static class AsphaltRoadSetup
    {
        private const string ASSET_ROOT     = "Assets/VoxelEngineAssets";
        private const string INDUSTRIAL     = ASSET_ROOT + "/Industrial";
        private const string PREFABS_FOLDER = INDUSTRIAL + "/Prefabs";
        private const string BLOCKS_FOLDER  = INDUSTRIAL + "/Blocks";
        private const string ITEMS_FOLDER   = INDUSTRIAL + "/Items";
        private const string PROC_FOLDER    = INDUSTRIAL + "/ProcessingRecipes";
        private const string RECIPES_ROOT   = ASSET_ROOT + "/Recipes";
        private const string MACHINE_RECIPES = ASSET_ROOT + "/Factory/MachineRecipes";
        private const string NODES          = ASSET_ROOT + "/Research/Nodes";
        private const string TREE_PATH      = ASSET_ROOT + "/Research/ResearchTree.asset";
        private const string RECIPE_REGISTRY = ASSET_ROOT + "/RecipeRegistry.asset";
        private const string CATALOG        = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        private const string ROAD_PREFAB     = PREFABS_FOLDER + "/AsphaltRoad.prefab";
        private const string ROAD_BLOCK      = BLOCKS_FOLDER  + "/Block_AsphaltRoad.asset";
        private const string ROAD_RECIPE     = RECIPES_ROOT   + "/Recipe_AsphaltRoad.asset";

        // The wide carriageway: one 4 m cell covers sixteen 1 m cells, so a player paving a road a
        // vehicle can actually drive along lays a quarter of the placements. The 1 m block stays
        // for hand-placed patching, footpaths and the odd culvert.
        private const float  WIDE_CELL_SIZE  = 4f;
        private const string WIDE_PREFAB     = PREFABS_FOLDER + "/AsphaltRoadWide.prefab";
        private const string WIDE_BLOCK      = BLOCKS_FOLDER  + "/Block_AsphaltRoadWide.asset";
        private const string WIDE_RECIPE     = RECIPES_ROOT   + "/Recipe_AsphaltRoadWide.asset";
        private const int    WIDE_ASPHALT_COST = 10;   // 16 m2 for 10 units: a bulk discount over 1/m2

        private const string BITUMEN_ITEM    = ITEMS_FOLDER   + "/Item_Bitumen.asset";
        private const string ASPHALT_ITEM    = ITEMS_FOLDER   + "/Item_Asphalt.asset";
        private const string PAVER_ITEM      = ITEMS_FOLDER   + "/Tool_RoadPaver.asset";
        private const string PAVER_RECIPE    = RECIPES_ROOT   + "/Recipe_RoadPaver.asset";

        private const string PROC_BITUMEN    = PROC_FOLDER    + "/Proc_BlownBitumen.asset";
        private const string MIX_ASPHALT     = MACHINE_RECIPES + "/MachineRecipe_MixAsphalt.asset";

        private const string NODE_PATH       = NODES + "/res_asphalt_roads.asset";
        private const string DISTILL_NODE    = NODES + "/res_atmospheric_distillation.asset";

        private const string TEX_ASPHALT     = PREFABS_FOLDER + "/Tex_RoadAsphalt.asset";
        private const string TEX_ASPHALT_NRM = PREFABS_FOLDER + "/Tex_RoadAsphaltNormal.asset";
        private const string TEX_SHOULDER    = PREFABS_FOLDER + "/Tex_RoadShoulder.asset";
        private const string TEX_SHOULDER_NRM= PREFABS_FOLDER + "/Tex_RoadShoulderNormal.asset";
        private const string MAT_ASPHALT     = PREFABS_FOLDER + "/Mat_RoadAsphalt.mat";
        private const string MAT_SHOULDER    = PREFABS_FOLDER + "/Mat_RoadShoulder.mat";
        private const string MAT_CRACK       = PREFABS_FOLDER + "/Mat_RoadCrack.mat";
        /// <summary>The first draft's wear material, retired when the four flat blobs became a
        /// fracture network and potholes. Kept as a path so a re-run deletes the orphan instead of
        /// leaving a dead material asset in the project.</summary>
        private const string MAT_WEAR_RETIRED = PREFABS_FOLDER + "/Mat_RoadWear.mat";
        private const string MAT_POTHOLE     = PREFABS_FOLDER + "/Mat_RoadPothole.mat";

        /// <summary>Bumped whenever the authored materials change meaning, so a project that already
        /// ran an earlier Step 72 gets the new look once instead of keeping the first-draft assets
        /// forever. Same marker pattern Step 69 uses for the distillation plant's `ModelRev2`.
        /// Rev 2: the first-draft asphalt read as an oil slick (near-black, smooth, no normal map)
        /// and the wear layer was four flat blobs rather than cracking.</summary>
        private const string SURFACE_REV_MARKER = "RoadSurfaceRev2";

        // The standing and ship chemical plants that blow bitumen, and every assembler that mixes it.
        private static readonly string[] ChemicalPlantPrefabs =
        {
            INDUSTRIAL + "/Prefabs/StationaryChemicalPlant.prefab",
            ASSET_ROOT + "/GridSystem/Prefabs/ChemicalPlant_Large.prefab",
        };

        private static readonly string[] AssemblerPrefabs =
        {
            ASSET_ROOT + "/StationPrefabs/Assembler.prefab",
            ASSET_ROOT + "/Factory/Prefabs/Assembler_Mk1.prefab",
            ASSET_ROOT + "/Factory/Prefabs/Assembler_Mk2.prefab",
            ASSET_ROOT + "/Factory/Prefabs/Assembler_Mk3.prefab",
        };

        // ════════════════════════════════════════════════════════════════
        //  SENTINEL-AWARE AUTHORING
        //
        //  `ItemDefinition` initialises its fields to a real item, not to empty:
        //      itemId = "iron_ore", displayName = "Iron Ore", maxStack = 900,
        //      massPerUnit = 1f, category = "Misc"
        //  so an `IsNullOrEmpty(displayName)` or `maxStack <= 0` guard is NEVER true on a freshly
        //  created asset, and every item this step authored shipped displaying as Iron Ore at 1 kg
        //  in stacks of 900. `ResearchNode` does the same with displayName = "New Research" and
        //  researchSeconds = 30f. Treat the initialiser value as "not authored yet".
        //
        //  This is the same trap the setup wizard's own repair pass documents ("old assets that
        //  were created during failed setup passes and kept the ItemDefinition defaults"), and the
        //  repair is the same shape: author unconditionally when the asset is new, and repair an
        //  existing asset only while it still holds a sentinel.
        // ════════════════════════════════════════════════════════════════

        private const string ITEM_NAME_SENTINEL    = "Iron Ore";
        private const int    ITEM_STACK_SENTINEL   = 900;
        private const float  ITEM_MASS_SENTINEL    = 1f;
        private const string NODE_NAME_SENTINEL    = "New Research";
        private const float  NODE_SECONDS_SENTINEL = 30f;

        private static bool UnsetName(string value)
            => string.IsNullOrWhiteSpace(value) || value == ITEM_NAME_SENTINEL;

        private static bool UnsetNodeName(string value)
            => string.IsNullOrWhiteSpace(value) || value == NODE_NAME_SENTINEL;

        private static bool UnsetStack(int value)
            => value <= 0 || value == ITEM_STACK_SENTINEL;

        private static bool UnsetMass(float value)
            => value <= 0f || Mathf.Approximately(value, ITEM_MASS_SENTINEL);

        private static bool UnsetResearchSeconds(float value)
            => value <= 0.01f || Mathf.Approximately(value, NODE_SECONDS_SENTINEL);

        [MenuItem("Tools/Voxel Engine/Run Step 72 (Asphalt Roads)", priority = 72)]
        public static void RunStep72Menu() => RunStep72();

        public static void RunStep72()
        {
            Debug.Log("[AsphaltRoadSetup] Step 72 - Asphalt Roads started.");

            foreach (var folder in new[] { INDUSTRIAL, PREFABS_FOLDER, BLOCKS_FOLDER, ITEMS_FOLDER,
                                           PROC_FOLDER, RECIPES_ROOT, MACHINE_RECIPES, NODES })
                EnsureFolder(folder);

            int created = 0, preserved = 0;

            // ── 1) Materials ──────────────────────────────────────────────
            // A project that ran the first-draft Step 72 already holds the oily assets, and
            // `GetOrCreate` would preserve them forever. The prefab carries a revision marker; if
            // it is missing, the surface assets are rebuilt once and the marker is stamped on.
            bool forceSurface = !PrefabHasMarker(ROAD_PREFAB, SURFACE_REV_MARKER);
            if (forceSurface)
                Debug.Log("[AsphaltRoadSetup] Rebuilding road surface assets (" + SURFACE_REV_MARKER + ").");

            var asphaltTex = AuthorRoadTextures(TEX_ASPHALT, "Tex_RoadAsphalt",
                                                TEX_ASPHALT_NRM, "Tex_RoadAsphaltNormal", 0, forceSurface);
            var shoulderTex = AuthorRoadTextures(TEX_SHOULDER, "Tex_RoadShoulder",
                                                 TEX_SHOULDER_NRM, "Tex_RoadShoulderNormal", 1, forceSurface);

            var asphaltMat  = AuthorSurfaceMaterial(MAT_ASPHALT, "Mat_RoadAsphalt", asphaltTex,
                                                    0.13f, Color.white, forceSurface);
            var shoulderMat = AuthorSurfaceMaterial(MAT_SHOULDER, "Mat_RoadShoulder", shoulderTex,
                                                    0.10f, Color.white, forceSurface);
            // The pothole floor is the shoulder aggregate in shadow: a hole shows the base course.
            var potholeMat  = AuthorSurfaceMaterial(MAT_POTHOLE, "Mat_RoadPothole", shoulderTex,
                                                    0.08f, new Color(0.34f, 0.32f, 0.30f), forceSurface);
            var crackMat    = AuthorCrackMaterial(MAT_CRACK, forceSurface);

            if (AssetDatabase.LoadAssetAtPath<Material>(MAT_WEAR_RETIRED) != null)
                AssetDatabase.DeleteAsset(MAT_WEAR_RETIRED);

            // ── 2) Road prefabs: 1 m patch cell and 4 m carriageway cell ──
            var roadPrefab = AuthorRoadPrefab(ROAD_PREFAB, "AsphaltRoad", 1f,
                                              asphaltMat, shoulderMat, crackMat, potholeMat,
                                              forceSurface, ref created, ref preserved);
            var widePrefab = AuthorRoadPrefab(WIDE_PREFAB, "AsphaltRoadWide", WIDE_CELL_SIZE,
                                              asphaltMat, shoulderMat, crackMat, potholeMat,
                                              forceSurface, ref created, ref preserved);

            // ── 3) Items ──────────────────────────────────────────────────
            var bitumen = GetOrCreate<ResourceItem>(BITUMEN_ITEM, ref created, ref preserved);
            bitumen.itemId = "item_bitumen";
            if (UnsetName(bitumen.displayName)) bitumen.displayName = "Bitumen";
            bitumen.description = "Blown residue from the heavy end of the column. Sticky, black and " +
                                  "worthless on its own - until it meets aggregate and becomes a road.";
            bitumen.iconTint = new Color(0.10f, 0.09f, 0.09f);
            bitumen.category = "Components";
            bitumen.subcategory = ResourceCategory.Component;
            if (UnsetStack(bitumen.maxStack))  bitumen.maxStack = 100;
            if (UnsetMass(bitumen.massPerUnit)) bitumen.massPerUnit = 2.0f;
            EditorUtility.SetDirty(bitumen);

            var asphalt = GetOrCreate<ResourceItem>(ASPHALT_ITEM, ref created, ref preserved);
            asphalt.itemId = "item_asphalt";
            if (UnsetName(asphalt.displayName)) asphalt.displayName = "Hot Mix Asphalt";
            asphalt.description = "Bitumen bound with sand and crushed aggregate. One unit paves one " +
                                  "metre of patch cell, ten pave a full four-metre carriageway slab, and " +
                                  "the same material repairs whichever it was laid as.";
            asphalt.iconTint = new Color(0.20f, 0.19f, 0.19f);
            asphalt.category = "Building";
            asphalt.subcategory = ResourceCategory.Component;
            if (UnsetStack(asphalt.maxStack))  asphalt.maxStack = 200;
            if (UnsetMass(asphalt.massPerUnit)) asphalt.massPerUnit = 3.5f;
            EditorUtility.SetDirty(asphalt);

            var roadBlock = GetOrCreate<BlockItem>(ROAD_BLOCK, ref created, ref preserved);
            roadBlock.itemId = "block_asphalt_road";
            if (UnsetName(roadBlock.displayName)) roadBlock.displayName = "Asphalt Road";
            roadBlock.description = "A one-metre paved cell that drapes over the terrain and shapes " +
                                    "itself to the strip around it. For footpaths, patching and " +
                                    "detail - the Road Paver lays the wide carriageway slab instead.";
            roadBlock.iconTint = new Color(0.16f, 0.16f, 0.17f);
            roadBlock.category = "Building";
            roadBlock.placedPrefab = roadPrefab;
            roadBlock.gridSize = Vector3Int.one;
            roadBlock.allowStacking = false;
            if (UnsetStack(roadBlock.maxStack))  roadBlock.maxStack = 200;
            if (UnsetMass(roadBlock.massPerUnit)) roadBlock.massPerUnit = 14f;
            if (roadBlock.blockHealth <= 0)  roadBlock.blockHealth = 120;
            if (roadBlock.miningTier <= 0)   roadBlock.miningTier = 1;
            EditorUtility.SetDirty(roadBlock);

            var wideBlock = GetOrCreate<BlockItem>(WIDE_BLOCK, ref created, ref preserved);
            wideBlock.itemId = "block_asphalt_road_wide";
            if (UnsetName(wideBlock.displayName)) wideBlock.displayName = "Asphalt Road (Wide)";
            wideBlock.description = "A four-metre carriageway slab - sixteen times the pavement of the " +
                                    "patch cell in one placement, so a road a vehicle can drive along " +
                                    "goes down fast. Drapes the terrain, auto-shapes its kerb from the " +
                                    "strip around it, and shares one wear pool with every cell it touches.";
            wideBlock.iconTint = new Color(0.22f, 0.22f, 0.23f);
            wideBlock.category = "Building";
            wideBlock.placedPrefab = widePrefab;
            wideBlock.gridSize = Vector3Int.one;
            wideBlock.allowStacking = false;
            if (UnsetStack(wideBlock.maxStack))  wideBlock.maxStack = 100;
            if (UnsetMass(wideBlock.massPerUnit)) wideBlock.massPerUnit = 56f;
            if (wideBlock.blockHealth <= 0)  wideBlock.blockHealth = 480;
            if (wideBlock.miningTier <= 0)   wideBlock.miningTier = 1;
            EditorUtility.SetDirty(wideBlock);

            var paver = GetOrCreate<RoadPaverTool>(PAVER_ITEM, ref created, ref preserved);
            paver.itemId = "tool_road_paver";
            if (UnsetName(paver.displayName)) paver.displayName = "Road Paver";
            paver.description = "Lays the four-metre carriageway slab by dragging: hold LMB and walk a " +
                                "line, and the road fills in under the aim. RMB lifts a slab back. " +
                                "Repairs a worn run with the same hot mix it was laid with.";
            paver.iconTint = new Color(0.90f, 0.62f, 0.20f);
            paver.category = "Tools";
            paver.toolType = ToolType.Other;
            paver.maxDurability = Mathf.Max(paver.maxDurability, 1500);
            if (paver.fireRate <= 0f) paver.fireRate = 12f;
            paver.pavingMaterial = asphalt;
            // The paver lays the WIDE slab: dragging is how a player builds a road a vehicle uses,
            // and the 1 m cell stays available for hand placement. Both go through the same
            // `TryComputePose` / `EvaluateCell`, so a dragged wide strip and a hand-placed patch
            // join the same runs and obey the same grade rules.
            paver.roadBlock = wideBlock;
            paver.materialPerCell = WIDE_ASPHALT_COST;
            paver.refundPerCell = WIDE_ASPHALT_COST / 2;
            // Area-priced, so a full repair of a worn run lands near a third of what laying it
            // cost whether the run is wide slabs or patch cells.
            if (paver.repairMaterialPerSquareMetre <= 0f) paver.repairMaterialPerSquareMetre = 0.19f;
            EditorUtility.SetDirty(paver);

            // ── 4) Bitumen from the column's heavy end ────────────────────
            var procBitumen = GetOrCreate<ProcessingRecipe>(PROC_BITUMEN, ref created, ref preserved);
            if (string.IsNullOrEmpty(procBitumen.displayName))
            {
                procBitumen.displayName = "Blow Bitumen";
                procBitumen.category = "Chemistry";
                procBitumen.secondsPerBatch = 9f;
                procBitumen.powerDrawMultiplier = 1.2f;
                procBitumen.fluidInputs = new[] { new FluidIO { liquid = LiquidType.HeavyFuelOil, litres = 40f } };
                procBitumen.outputs = new[] { new ProcessingIO { item = bitumen, count = 4 } };
                EditorUtility.SetDirty(procBitumen);
            }
            else if (procBitumen.outputs != null && procBitumen.outputs.Length > 0
                     && procBitumen.outputs[0].item == null)
            {
                // An existing recipe whose output went missing: reconnect it without touching numbers.
                var outputs = new List<ProcessingIO>(procBitumen.outputs);
                outputs[0] = new ProcessingIO { item = bitumen, count = Mathf.Max(1, outputs[0].count) };
                procBitumen.outputs = outputs.ToArray();
                EditorUtility.SetDirty(procBitumen);
            }

            int plantWired = AttachToChemicalPlants(procBitumen);

            // ── 5) Hot mix from bitumen plus aggregate ────────────────────
            var sand   = FindItem("Item_Sand");
            var gravel = FindItem("Item_Gravel");

            var mixAsphalt = GetOrCreate<MachineRecipe>(MIX_ASPHALT, ref created, ref preserved);
            if (string.IsNullOrEmpty(mixAsphalt.displayName))
            {
                mixAsphalt.displayName = "Mix Hot Asphalt";
                mixAsphalt.recipeType = MachineRecipeType.Custom;
                mixAsphalt.processSeconds = 5f;
                mixAsphalt.unlockedByDefault = false;
                mixAsphalt.outputItem = asphalt;
                mixAsphalt.outputCount = 8;
                var inputs = new List<MachineRecipeSlot>();
                if (bitumen != null) inputs.Add(new MachineRecipeSlot { item = bitumen, count = 1 });
                if (sand != null)    inputs.Add(new MachineRecipeSlot { item = sand,    count = 2 });
                if (gravel != null)  inputs.Add(new MachineRecipeSlot { item = gravel,  count = 3 });
                mixAsphalt.inputs = inputs.ToArray();
                EditorUtility.SetDirty(mixAsphalt);
            }
            else
            {
                mixAsphalt.outputItem ??= asphalt;
                EditorUtility.SetDirty(mixAsphalt);
            }

            int assemblerWired = AttachToAssemblers(mixAsphalt);

            // ── 6) Player-facing recipes ──────────────────────────────────
            var steelPlate = FindItem("Item_SteelPlate");
            var ironPlate  = FindItem("Item_IronPlate");
            var ironGear   = FindItem("Item_IronGear");

            var roadRecipe = GetOrCreate<RecipeDefinition>(ROAD_RECIPE, ref created, ref preserved);
            if (string.IsNullOrEmpty(roadRecipe.displayName)) roadRecipe.displayName = "Asphalt Road";
            roadRecipe.outputItem = roadBlock;
            if (roadRecipe.outputCount <= 0) roadRecipe.outputCount = 1;
            roadRecipe.unlockedByDefault = false;
            if (roadRecipe.requiredStation == StationTier.None && roadRecipe.craftSeconds <= 0f)
            {
                roadRecipe.requiredStation = StationTier.CraftingBench;
                roadRecipe.craftSeconds = 1.5f;
            }
            if (roadRecipe.inputs == null || roadRecipe.inputs.Length == 0)
            {
                var inputs = new List<RecipeIngredient>();
                Add(ref inputs, asphalt, 1);
                roadRecipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(roadRecipe);

            var wideRecipe = GetOrCreate<RecipeDefinition>(WIDE_RECIPE, ref created, ref preserved);
            if (string.IsNullOrEmpty(wideRecipe.displayName)) wideRecipe.displayName = "Asphalt Road (Wide)";
            wideRecipe.outputItem = wideBlock;
            if (wideRecipe.outputCount <= 0) wideRecipe.outputCount = 1;
            wideRecipe.unlockedByDefault = false;
            if (wideRecipe.requiredStation == StationTier.None && wideRecipe.craftSeconds <= 0f)
            {
                wideRecipe.requiredStation = StationTier.CraftingBench;
                // Sixteen times the pavement, but it is one pour rather than sixteen, so it is
                // quicker per square metre than the patch cell.
                wideRecipe.craftSeconds = 8f;
            }
            if (wideRecipe.inputs == null || wideRecipe.inputs.Length == 0)
            {
                var inputs = new List<RecipeIngredient>();
                Add(ref inputs, asphalt, WIDE_ASPHALT_COST);
                wideRecipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(wideRecipe);

            var paverRecipe = GetOrCreate<RecipeDefinition>(PAVER_RECIPE, ref created, ref preserved);
            if (string.IsNullOrEmpty(paverRecipe.displayName)) paverRecipe.displayName = "Road Paver";
            paverRecipe.outputItem = paver;
            if (paverRecipe.outputCount <= 0) paverRecipe.outputCount = 1;
            paverRecipe.unlockedByDefault = false;
            if (paverRecipe.requiredStation == StationTier.None && paverRecipe.craftSeconds <= 0f)
            {
                paverRecipe.requiredStation = StationTier.CraftingBench;
                paverRecipe.craftSeconds = 6f;
            }
            if (paverRecipe.inputs == null || paverRecipe.inputs.Length == 0)
            {
                var inputs = new List<RecipeIngredient>();
                Add(ref inputs, ironPlate, 4);
                Add(ref inputs, ironGear, 2);
                Add(ref inputs, steelPlate, 2);
                paverRecipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(paverRecipe);

            // ── 7) Registries, persistence, research gate ─────────────────
            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(RECIPE_REGISTRY);
            if (registry != null)
            {
                if (registry.recipes == null) registry.recipes = new List<RecipeDefinition>();
                foreach (var recipe in new[] { roadRecipe, wideRecipe, paverRecipe })
                {
                    if (recipe != null && !registry.recipes.Contains(recipe))
                    {
                        registry.recipes.Add(recipe);
                        EditorUtility.SetDirty(registry);
                    }
                }
            }

            EnsureItemPersisted(bitumen);
            EnsureItemPersisted(asphalt);
            EnsureItemPersisted(roadBlock);
            EnsureItemPersisted(wideBlock);
            EnsureItemPersisted(paver);

            WireResearchNode(ref created, ref preserved, roadRecipe, wideRecipe, paverRecipe);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[AsphaltRoadSetup] Step 72 complete. Created: {created}, Preserved: {preserved}, " +
                      $"chemical plants wired: {plantWired}, assemblers wired: {assemblerWired}.");

            EditorUtility.DisplayDialog("Voxel Engine - Asphalt Roads (Step 72)",
                "Authored asphalt road content:\n" +
                "  - Asphalt Road (Wide) 4 m carriageway slab - what the Road Paver lays\n" +
                "  - Asphalt Road 1 m patch cell for footpaths and hand-placed detail\n" +
                "  - Road Paver drag-to-pave tool (" + WIDE_ASPHALT_COST + " asphalt per wide slab)\n" +
                "  - Bitumen + Hot Mix Asphalt items\n" +
                "  - Blow Bitumen on " + plantWired + " chemical plant(s)\n" +
                "  - Mix Hot Asphalt on " + assemblerWired + " assembler(s)\n" +
                "  - Asphalt Roads research node (tier 6)\n" +
                "  - Registered in ItemPersistenceCatalog\n" +
                (forceSurface ? "  - Surface assets rebuilt (" + SURFACE_REV_MARKER +
                                "): real aggregate albedo + normal map, cracking damage layer\n" : "") +
                "\nCreated: " + created + ", Preserved: " + preserved, "OK");
        }

        // ════════════════════════════════════════════════════════════════
        //  PREFAB
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Authors one road-cell prefab. The same routine builds both variants — the 1 m patch cell
        /// and the 4 m carriageway cell — because they differ only in scale, and two routines would
        /// drift. `cellSize` is the variant's identity so it is always written; the proportions that
        /// follow from it are written too, since a 4 m slab at 1 m proportions looks like a tile.
        /// </summary>
        private static GameObject AuthorRoadPrefab(string prefabPath, string rootName, float cellSize,
                                                   Material asphaltMat, Material shoulderMat,
                                                   Material crackMat, Material potholeMat,
                                                   bool forceMaterials,
                                                   ref int created, ref int preserved)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            bool isExisting = existing != null;
            var root = isExisting ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(rootName);

            // RequireComponent on AsphaltRoad brings PlacedBlock with it.
            var placed = root.GetComponent<PlacedBlock>();
            if (placed == null) placed = root.AddComponent<PlacedBlock>();

            var road = root.GetComponent<AsphaltRoad>();
            if (road == null) road = root.AddComponent<AsphaltRoad>();

            // ── variant identity and the proportions that follow from it ──
            road.cellSize = cellSize;
            // A 4 m slab is sixteen times the pavement but does not need sixteen times the depth;
            // a wearing course is a wearing course. It gets a little deeper and a broader kerb so
            // the wide road reads as a road rather than as four tiles butted together.
            road.thickness      = Mathf.Lerp(0.08f, 0.13f, Mathf.InverseLerp(1f, 4f, cellSize));
            road.shoulderWidth  = Mathf.Lerp(0.09f, 0.19f, Mathf.InverseLerp(1f, 4f, cellSize));
            road.shoulderRise   = Mathf.Lerp(0.022f, 0.038f, Mathf.InverseLerp(1f, 4f, cellSize));

            // Sub-linear in area: a wide cell is more work to break up but not sixteen times more.
            int health = Mathf.RoundToInt(120f * Mathf.Sqrt(Mathf.Max(1f, cellSize * cellSize)));
            if (placed.Hp <= 0 || forceMaterials) placed.Hp = health;

            var visuals = root.transform.Find("Visuals");
            if (visuals == null)
            {
                var go = new GameObject("Visuals");
                go.transform.SetParent(root.transform, false);
                visuals = go.transform;
            }

            var surface = EnsureChild(visuals, "Surface");
            var surfaceFilter   = EnsureComponent<MeshFilter>(surface);
            var surfaceRenderer = EnsureComponent<MeshRenderer>(surface);
            EnsureComponent<MeshCollider>(surface);
            surfaceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            surfaceRenderer.receiveShadows = true;
            // Two submeshes: [0] asphalt wearing course, [1] aggregate shoulder/kerb.
            AssignBothMaterials(surfaceRenderer, asphaltMat, shoulderMat, forceMaterials);

            // Damage layer: [0] the fracture network, [1] potholes showing the base course.
            var wear = EnsureChild(visuals, "WearPatches");
            var wearFilter   = EnsureComponent<MeshFilter>(wear);
            var wearRenderer = EnsureComponent<MeshRenderer>(wear);
            wearRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wearRenderer.receiveShadows = true;
            wearRenderer.enabled = false;      // fresh asphalt is uncracked
            // Damage is geometry now, not a scaled decal, so it must sit at unit scale — a leftover
            // scale from the blob-decal version would stretch every crack.
            wear.localScale = Vector3.one;
            AssignBothMaterials(wearRenderer, crackMat, potholeMat, forceMaterials);

            // The meshes are runtime-generated: the prefab carries components, collider and
            // materials only, so nothing here bakes geometry that could go stale.
            if (surfaceFilter.sharedMesh != null && surfaceFilter.sharedMesh.hideFlags == HideFlags.DontSave)
                surfaceFilter.sharedMesh = null;
            if (wearFilter.sharedMesh != null && wearFilter.sharedMesh.hideFlags == HideFlags.DontSave)
                wearFilter.sharedMesh = null;

            EnsureMarker(root, SURFACE_REV_MARKER);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (isExisting) { PrefabUtility.UnloadPrefabContents(root); preserved++; }
            else { Object.DestroyImmediate(root); created++; }
            return prefab;
        }

        /// <summary>True when a prefab already carries a named marker child.</summary>
        private static bool PrefabHasMarker(string prefabPath, string markerName)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null) return false;      // nothing authored yet: build everything fresh
            return asset.transform.Find(markerName) != null;
        }

        /// <summary>Stamps a marker child on a prefab root so a later run can tell what revision it
        /// is looking at. Hidden and inert — it carries no component.</summary>
        private static void EnsureMarker(GameObject root, string markerName)
        {
            if (root.transform.Find(markerName) != null) return;
            var marker = new GameObject(markerName);
            marker.transform.SetParent(root.transform, false);
            marker.hideFlags = HideFlags.DontSaveInEditor;
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null) return child;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static T EnsureComponent<T>(Transform host) where T : Component
        {
            var component = host.GetComponent<T>();
            if (component == null) component = host.gameObject.AddComponent<T>();
            return component;
        }

        /// <summary>
        /// Assigns the two submesh materials. Normally it only fills slots that are empty or still
        /// on the built-in default, so a material the team swapped in survives a re-run.
        /// <paramref name="force"/> overrides that for the one-time surface upgrade, which deletes
        /// and recreates the material assets — a new GUID, so the old slot references go missing
        /// and have to be re-pointed rather than preserved.
        /// </summary>
        private static void AssignBothMaterials(Renderer renderer, Material primary, Material secondary,
                                                bool force = false)
        {
            if (renderer == null) return;
            var current = renderer.sharedMaterials;
            bool needsPrimary = force || current.Length < 1 || current[0] == null || IsDefaultMaterial(current[0]);
            bool needsSecondary = force || current.Length < 2 || current[1] == null || IsDefaultMaterial(current[1]);
            if (!needsPrimary && !needsSecondary) return;

            var next = new Material[2];
            next[0] = needsPrimary ? primary : current[0];
            next[1] = needsSecondary ? secondary : current[1];
            renderer.sharedMaterials = next;
        }

        private static bool IsDefaultMaterial(Material material)
        {
            if (material == null) return true;
            string path = AssetDatabase.GetAssetPath(material);
            return string.IsNullOrEmpty(path) || path.StartsWith("Library/unity default resources");
        }

        // ════════════════════════════════════════════════════════════════
        //  MATERIALS & TEXTURES
        //
        //  The first draft read as an oil slick for three concrete reasons, all fixed here:
        //    1. The material multiplied a near-black base colour (0.105) by a texture that was
        //       ALSO dark, so the result was ~0.01 albedo — darker than wet tar.
        //    2. There was no normal map, so the surface had no relief at all and lit as a flat
        //       plane. Flat + dark + slightly smooth is exactly what a puddle of oil looks like.
        //    3. The "aggregate" was per-pixel random noise, which aliases into a shimmer instead
        //       of reading as stones bound in binder.
        //  So: the texture now carries the true asphalt values and the material base colour is
        //  white; aggregate is a tileable Voronoi chip field with dark bitumen mortar between the
        //  chips; and a normal map is derived from the same height field the chips come from.
        // ════════════════════════════════════════════════════════════════

        private const int TEX_SIZE = 512;

        /// <summary>Albedo, normal and height for one road material, generated together so the
        /// relief and the colour always describe the same aggregate.</summary>
        private struct RoadSurfaceTextures
        {
            public Texture2D albedo;
            public Texture2D normal;
        }

        private static RoadSurfaceTextures AuthorRoadTextures(string albedoPath, string albedoName,
                                                              string normalPath, string normalName,
                                                              int style, bool force)
        {
            var albedo = force ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            var normal = force ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (albedo != null && normal != null) return new RoadSurfaceTextures { albedo = albedo, normal = normal };

            if (albedo != null) AssetDatabase.DeleteAsset(albedoPath);
            if (normal != null) AssetDatabase.DeleteAsset(normalPath);

            var height = new float[TEX_SIZE * TEX_SIZE];
            var colour = new Color[TEX_SIZE * TEX_SIZE];
            GenerateAggregateField(colour, height, style);

            albedo = new Texture2D(TEX_SIZE, TEX_SIZE, TextureFormat.RGBA32, true, false)
            {
                name = albedoName,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 8
            };
            albedo.SetPixels(colour);
            albedo.Apply(true, false);
            AssetDatabase.CreateAsset(albedo, albedoPath);

            // A normal map must be sampled as data, not as colour, so it is created linear.
            normal = new Texture2D(TEX_SIZE, TEX_SIZE, TextureFormat.RGBA32, true, true)
            {
                name = normalName,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 8
            };
            normal.SetPixels(HeightToNormal(height, style == 0 ? 2.2f : 3.2f));
            normal.Apply(true, false);
            AssetDatabase.CreateAsset(normal, normalPath);

            return new RoadSurfaceTextures { albedo = albedo, normal = normal };
        }

        /// <summary>
        /// Tileable aggregate field. Chips are a Voronoi cell set that wraps at the tile edge, so a
        /// 4 m cell and a 1 m cell both tile without a visible seam. Each chip gets its own tone
        /// (real aggregate is mixed granite/limestone, so it is not one grey), the mortar between
        /// chips is dark bitumen, and two octaves of tileable value noise add the patchiness a
        /// roller leaves plus fine grit.
        /// </summary>
        private static void GenerateAggregateField(Color[] colour, float[] height, int style)
        {
            // 32 chip-cells across one metre of wearing course puts aggregate at ~3 cm, which is
            // coarse wearing-course stone; the shoulder is coarser still. Fewer, larger cells read
            // as cobbles rather than as bound aggregate.
            int cells   = style == 0 ? 32 : 20;
            int seed    = style == 0 ? 7201 : 4111;
            float scale = TEX_SIZE / (float)cells;

            // Mortar is the binder squeezed up between stones: near-black, slightly warm.
            Color mortar = style == 0 ? new Color(0.055f, 0.053f, 0.056f)
                                      : new Color(0.150f, 0.138f, 0.122f);

            for (int y = 0; y < TEX_SIZE; y++)
            {
                for (int x = 0; x < TEX_SIZE; x++)
                {
                    // ── tileable Voronoi: nearest and second-nearest chip centre ──
                    float fx = x / scale, fy = y / scale;
                    int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
                    float f1 = float.MaxValue, f2 = float.MaxValue;
                    int nearestX = ix, nearestY = iy;

                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int cx = ix + dx, cy = iy + dy;
                        int wx = ((cx % cells) + cells) % cells;
                        int wy = ((cy % cells) + cells) % cells;
                        float px = (cx + Hash01(wx, wy, seed)) * scale;
                        float py = (cy + Hash01(wx, wy, seed + 977)) * scale;
                        float d = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                        if (d < f1)      { f2 = f1; f1 = d; nearestX = wx; nearestY = wy; }
                        else if (d < f2) { f2 = d; }
                    }

                    // Chip interior vs. mortar gap. `edge` is small at the boundary between chips.
                    float edge = Mathf.Clamp01((f2 - f1) / Mathf.Max(0.5f, scale * 0.42f));
                    // A narrow mortar band: real wearing course is stone-on-stone with binder in
                    // the gaps, not stones floating in black. A wide band reads as netting.
                    float chipMask = Mathf.SmoothStep(style == 0 ? 0.05f : 0.06f,
                                                      style == 0 ? 0.30f : 0.40f, edge);

                    // Per-chip tone: mixed aggregate, so chips vary in both brightness and hue.
                    // The wearing course keeps the contrast tight so it reads as ONE surface.
                    float toneJit = Hash01(nearestX, nearestY, seed + 31);
                    float warmJit = Hash01(nearestX, nearestY, seed + 57);
                    float tone = Mathf.Lerp(style == 0 ? 0.80f : 0.72f,
                                            style == 0 ? 1.22f : 1.30f, toneJit);
                    Color chip = style == 0
                        ? new Color(0.300f * tone, 0.298f * tone, 0.302f * tone)
                        : new Color(0.430f * tone, 0.404f * tone * Mathf.Lerp(0.94f, 1.08f, warmJit),
                                    0.362f * tone * Mathf.Lerp(0.90f, 1.02f, warmJit));

                    // Rounded crown on each chip: the stone is a little proud in its middle.
                    float crown = 1f - Mathf.Clamp01(f1 / Mathf.Max(0.5f, scale * 0.62f));

                    // Two octaves of tileable mottling — roller patchiness, then fine grit.
                    float patch = TileableValueNoise(x, y, 4,  seed + 131) - 0.5f;
                    float grit  = TileableValueNoise(x, y, 96, seed + 977) - 0.5f;

                    float mottle = 1f + patch * 0.30f + grit * 0.26f;
                    Color c = Color.Lerp(mortar, chip, chipMask) * mottle;

                    // Faint binder sheen in the mortar gaps only, so the stones stay matte.
                    c *= 1f - (1f - chipMask) * 0.10f;

                    c.a = 1f;
                    colour[y * TEX_SIZE + x] = c;
                    height[y * TEX_SIZE + x] = chipMask * (0.50f + 0.50f * crown) + grit * 0.20f;
                }
            }
        }

        /// <summary>Sobel of the height field into a tangent-space normal map.</summary>
        private static Color[] HeightToNormal(float[] height, float strength)
        {
            var normals = new Color[TEX_SIZE * TEX_SIZE];
            for (int y = 0; y < TEX_SIZE; y++)
            {
                for (int x = 0; x < TEX_SIZE; x++)
                {
                    int xm = ((x - 1) + TEX_SIZE) % TEX_SIZE, xp = (x + 1) % TEX_SIZE;
                    int ym = ((y - 1) + TEX_SIZE) % TEX_SIZE, yp = (y + 1) % TEX_SIZE;

                    float dx = (height[y * TEX_SIZE + xp] - height[y * TEX_SIZE + xm]) * strength;
                    float dy = (height[yp * TEX_SIZE + x] - height[ym * TEX_SIZE + x]) * strength;

                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    normals[y * TEX_SIZE + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f,
                                                          n.z * 0.5f + 0.5f, 1f);
                }
            }
            return normals;
        }

        /// <summary>Integer hash to a unit float. Deterministic, so a re-run reproduces the texture
        /// exactly rather than re-rolling a different road surface every time the step is run.</summary>
        // 2246822519 does not fit in an int, so as a bare literal it is typed uint and silently
        // widens the whole hash expression to long (CS0266 on assignment). Casting it to int inside
        // the unchecked block keeps the arithmetic wrapping in 32 bits, which is what the hash —
        // and the rendered preview the aggregate was tuned against — depends on.
        private const int HASH_STRIDE = unchecked((int)2246822519u);

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * HASH_STRIDE;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        /// <summary>Bilinear value noise on a lattice that wraps at the tile edge, so the texture
        /// has no seam. `frequency` must divide the tile evenly to tile correctly.</summary>
        private static float TileableValueNoise(int x, int y, int frequency, int seed)
        {
            float cell = TEX_SIZE / (float)frequency;
            float fx = x / cell, fy = y / cell;
            int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
            float tx = fx - ix, ty = fy - iy;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);

            int x0 = ((ix % frequency) + frequency) % frequency;
            int y0 = ((iy % frequency) + frequency) % frequency;
            int x1 = (x0 + 1) % frequency;
            int y1 = (y0 + 1) % frequency;

            float h00 = Hash01(x0, y0, seed), h10 = Hash01(x1, y0, seed);
            float h01 = Hash01(x0, y1, seed), h11 = Hash01(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), ty);
        }

        private static Material AuthorSurfaceMaterial(string path, string name, RoadSurfaceTextures tex,
                                                      float smoothness, Color tint, bool force)
        {
            var material = force ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            if (force) AssetDatabase.DeleteAsset(path);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = name };

            // The tint is white for the wearing course, because the texture already carries the
            // true asphalt values — multiplying a dark texture by a dark base colour is what
            // produced the oil-slick look. Only the pothole floor tints, to read as shadowed
            // rubble in a hole rather than as clean aggregate.
            if (material.HasProperty("_BaseMap"))   material.SetTexture("_BaseMap", tex.albedo);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
            material.mainTexture = tex.albedo;
            material.color = tint;

            if (tex.normal != null)
            {
                if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", tex.normal);
                material.EnableKeyword("_NORMALMAP");
            }
            // Aggregate is stone: no metal, and matte. Anything above ~0.25 smoothness starts to
            // read as wet.
            if (material.HasProperty("_Metallic"))   material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_BumpScale"))  material.SetFloat("_BumpScale", 1f);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Near-black matte fissure fill. No texture: a crack is a shadow, and texturing it
        /// would make it read as a different material laid into the surface.</summary>
        private static Material AuthorCrackMaterial(string path, bool force)
        {
            var material = force ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            if (force) AssetDatabase.DeleteAsset(path);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = "Mat_RoadCrack", color = new Color(0.030f, 0.028f, 0.028f) };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(0.030f, 0.028f, 0.028f));
            if (material.HasProperty("_Metallic"))   material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.04f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ════════════════════════════════════════════════════════════════
        //  RECIPE ATTACHMENT
        // ════════════════════════════════════════════════════════════════

        private static int AttachToChemicalPlants(ProcessingRecipe recipe)
        {
            if (recipe == null) return 0;
            int wired = 0;
            foreach (var path in ChemicalPlantPrefabs)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;

                var contents = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;
                foreach (var plant in contents.GetComponentsInChildren<StationaryChemicalPlant>(true))
                {
                    if (plant.knownRecipes == null) plant.knownRecipes = new List<ProcessingRecipe>();
                    if (!plant.knownRecipes.Contains(recipe)) { plant.knownRecipes.Add(recipe); dirty = true; }
                }
                foreach (var plant in contents.GetComponentsInChildren<VoxelEngine.GridSystem.GridChemicalPlant>(true))
                {
                    if (plant.knownRecipes == null) plant.knownRecipes = new List<ProcessingRecipe>();
                    if (!plant.knownRecipes.Contains(recipe)) { plant.knownRecipes.Add(recipe); dirty = true; }
                }

                if (dirty) { PrefabUtility.SaveAsPrefabAsset(contents, path); wired++; }
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return wired;
        }

        private static int AttachToAssemblers(MachineRecipe recipe)
        {
            if (recipe == null) return 0;
            int wired = 0;
            foreach (var path in AssemblerPrefabs)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;

                var contents = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;
                foreach (var assembler in contents.GetComponentsInChildren<Assembler>(true))
                {
                    if (assembler.knownRecipes == null) assembler.knownRecipes = new List<MachineRecipe>();
                    if (!assembler.knownRecipes.Contains(recipe)) { assembler.knownRecipes.Add(recipe); dirty = true; }
                }

                if (dirty) { PrefabUtility.SaveAsPrefabAsset(contents, path); wired++; }
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return wired;
        }

        // ════════════════════════════════════════════════════════════════
        //  RESEARCH
        // ════════════════════════════════════════════════════════════════

        private static void WireResearchNode(ref int created, ref int preserved,
                                             params RecipeDefinition[] recipes)
        {
            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(TREE_PATH);
            var distillation = AssetDatabase.LoadAssetAtPath<ResearchNode>(DISTILL_NODE);

            var node = GetOrCreate<ResearchNode>(NODE_PATH, ref created, ref preserved);
            node.nodeId = "res_asphalt_roads";
            if (UnsetNodeName(node.displayName))
                node.displayName = "Asphalt Roads";
            if (string.IsNullOrEmpty(node.description))
                node.description = "Blow the column's heavy residue into bitumen, bind it with sand and " +
                                   "crushed aggregate, and lay it. A paved strip is faster on foot, grips " +
                                   "better under wheels and lets a route become a schedule - and it wears " +
                                   "out under traffic, so it wants repairing with the same hot mix.";

            // Tier and column are only written while they are still at their defaults, so a layout
            // pass in the research UI is not undone by a re-run.
            if (node.category == ResearchCategory.Environment && node.subCategory == ResearchSubCategory.General)
            {
                node.subCategory = ResearchSubCategory.Building;
                node.tier = 6;
                node.column = 6;
                node.iconTint = new Color(0.30f, 0.30f, 0.32f);
            }
            if (UnsetResearchSeconds(node.researchSeconds)) node.researchSeconds = 45f;
            if (node.maxRanks < 1) node.maxRanks = 1;
            if (node.cost == null || node.cost.Length == 0) node.cost = new ResearchNode.ScienceCost[0];

            if (distillation != null)
            {
                var prerequisites = new List<ResearchNode>(node.prerequisites ?? new ResearchNode[0]);
                if (!prerequisites.Contains(distillation))
                {
                    prerequisites.Add(distillation);
                    node.prerequisites = prerequisites.ToArray();
                }
            }

            var unlocks = new List<RecipeDefinition>(node.unlocksRecipes ?? new RecipeDefinition[0]);
            foreach (var recipe in recipes)
            {
                if (recipe != null && !unlocks.Contains(recipe)) unlocks.Add(recipe);
            }
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

        // ════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════

        private static T GetOrCreate<T>(string path, ref int created, ref int preserved) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) { preserved++; return existing; }
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            created++;
            return asset;
        }

        private static void Add(ref List<RecipeIngredient> list, ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return;
            list.Add(new RecipeIngredient { item = item, count = count });
        }

        private static ItemDefinition FindItem(string assetName)
        {
            foreach (var root in new[] { ASSET_ROOT + "/Items", ITEMS_FOLDER,
                                         ASSET_ROOT + "/Industrial/Items", ASSET_ROOT + "/Factory/Items" })
            {
                var direct = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{root}/{assetName}.asset");
                if (direct != null) return direct;
            }
            var guids = AssetDatabase.FindAssets(assetName + " t:ItemDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item != null && item.name == assetName) return item;
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
                EnsureFolder(System.IO.Path.GetDirectoryName(CATALOG).Replace('\\', '/'));
                AssetDatabase.CreateAsset(catalog, CATALOG);
            }
            if (catalog.items == null) catalog.items = new List<ItemDefinition>();
            if (!catalog.items.Contains(item))
            {
                catalog.items.Add(item);
                EditorUtility.SetDirty(catalog);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            int last = path.LastIndexOf('/');
            if (last <= 0) return;
            EnsureFolder(path.Substring(0, last));
            AssetDatabase.CreateFolder(path.Substring(0, last), path.Substring(last + 1));
        }
    }
}
#endif
