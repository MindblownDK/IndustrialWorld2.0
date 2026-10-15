// Assets/Scripts/Editor/Crafting/OreWashingSetup.cs
//
// Section 6.2 (4.6.0) — Ore Washing, Enrichment & Tailings Loop.
// Non-destructive setup step:
//   - Creates/repairs Washed Iron Ore, Washed Copper Ore, Mineral Tailings, and Ore Washer block/prefab.
//   - Creates/repairs Ore Washer MachineRecipes (Wash Iron Ore, Wash Copper Ore, Reprocess Tailings).
//   - Creates/repairs Crusher tailings recipe (Crush Mineral Tailings -> Gravel + Sand).
//   - Creates/repairs bonus-yield SmeltingRecipes (Crushed Iron/Copper -> 1 Ingot, Washed Iron/Copper -> 2 Ingots,
//     Mineral Tailings -> Stone) and wires them non-destructively to Furnace, ElectricFurnace, and GridElectricFurnace.
//   - Creates/repairs PollutionSource_OreWashing profile and wires PollutionEmitter on OreWasher.prefab.
//   - Creates/repairs Recipe_OreWasher and ResNode_OreWashing in RecipeRegistry and ResearchTree.
//   - Registers all new items in ItemPersistenceCatalog.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Environment;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Research;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;
using VoxelEngine.UI;

namespace VoxelEngine.EditorTools
{
    public static class OreWashingSetup
    {
        private const string ROOT            = "Assets/VoxelEngineAssets";
        private const string FAC_ROOT        = ROOT + "/Factory";
        private const string FAC_ITEMS       = FAC_ROOT + "/Items";
        private const string FAC_PREFABS     = FAC_ROOT + "/Prefabs";
        private const string FAC_MATS        = FAC_PREFABS + "/Materials";
        private const string FAC_RECIPES     = FAC_ROOT + "/Recipes";
        private const string FAC_MRECIPES    = FAC_ROOT + "/MachineRecipes";
        private const string FAC_MDEFS       = FAC_ROOT + "/MachineDefinitions";
        private const string BASE_ITEMS      = ROOT + "/Items";
        private const string IND_ITEMS       = ROOT + "/Industrial/Items";
        private const string BASE_RECIPES    = ROOT + "/Recipes";
        private const string NODES_FOLDER    = ROOT + "/Research/Nodes";
        private const string SCIENCE_FOLDER  = ROOT + "/Research/Science";
        private const string POLLUTION_PROFS = ROOT + "/Environment/Pollution/Profiles";
        private const string REGISTRY_PATH   = ROOT + "/RecipeRegistry.asset";
        private const string TREE_PATH       = ROOT + "/Research/ResearchTree.asset";
        private const string CATALOG_PATH    = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        // Sentinel defaults on newly instantiated ScriptableObjects
        private const string ITEM_ID_SENTINEL      = "iron_ore";
        private const string ITEM_NAME_SENTINEL    = "Iron Ore";
        private const int    ITEM_STACK_SENTINEL   = 900;
        private const float  ITEM_MASS_SENTINEL    = 1f;
        private const string NODE_ID_SENTINEL      = "research_id";
        private const string NODE_NAME_SENTINEL    = "New Research";
        private const float  NODE_SECONDS_SENTINEL = 30f;

        [MenuItem("Tools/Voxel Engine/Build Ore Washing & Tailings Loop")]
        public static void RunStep115() => RunInternal(showDialog: true);

        public static void RunSilent() => RunInternal(showDialog: false);

        private static void RunInternal(bool showDialog)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (showDialog)
                    EditorUtility.DisplayDialog("Ore Washing Setup", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                EnsureFolders();
                int created = 0, repaired = 0, preserved = 0;

                // 1. Canonical base items
                var ironOre     = LoadCanonicalItem(IND_ITEMS + "/Item_IronOre.asset", "iron_ore", "Item_IronOre");
                var copperOre   = LoadCanonicalItem(IND_ITEMS + "/Item_CopperOre.asset", "copper_ore", "Item_CopperOre");
                var ironIngot   = LoadCanonicalItem(BASE_ITEMS + "/Item_IronIngot.asset", "iron_ingot", "Item_IronIngot");
                var copperIngot = LoadCanonicalItem(BASE_ITEMS + "/Item_CopperIngot.asset", "copper_ingot", "Item_CopperIngot");
                var stone       = LoadCanonicalItem(BASE_ITEMS + "/Item_Stone.asset", "stone", "Item_Stone");
                var sand        = LoadCanonicalItem(BASE_ITEMS + "/Item_Sand.asset", "sand", "Item_Sand");

                // 2. Ensure Crushed Ores, Gravel, Washed Ores, and Mineral Tailings
                var gravel = EnsureResourceItem(FAC_ITEMS + "/Item_Gravel.asset",
                    "gravel", "Gravel",
                    "Crushed stone aggregate used in construction and filtration.",
                    new Color(0.56f, 0.54f, 0.50f), 100, ResourceCategory.Raw,
                    ref created, ref repaired, ref preserved);

                var crushedIron = EnsureResourceItem(FAC_ITEMS + "/Item_CrushedIronOre.asset",
                    "crushed_iron_ore", "Crushed Iron Ore",
                    "Crushed iron-bearing ore ready for direct smelting or hydro-washing enrichment.",
                    new Color(0.66f, 0.45f, 0.34f), 100, ResourceCategory.Raw,
                    ref created, ref repaired, ref preserved);

                var crushedCopper = EnsureResourceItem(FAC_ITEMS + "/Item_CrushedCopperOre.asset",
                    "crushed_copper_ore", "Crushed Copper Ore",
                    "Crushed copper-bearing ore ready for direct smelting or hydro-washing enrichment.",
                    new Color(0.84f, 0.48f, 0.24f), 100, ResourceCategory.Raw,
                    ref created, ref repaired, ref preserved);

                var washedIron = EnsureResourceItem(FAC_ITEMS + "/Item_WashedIronOre.asset",
                    "washed_iron_ore", "Washed Iron Ore",
                    "Hydro-classified iron concentrate stripped of gangue. Smelts into 2 Iron Ingots per unit.",
                    new Color(0.78f, 0.56f, 0.42f), 100, ResourceCategory.Component,
                    ref created, ref repaired, ref preserved);

                var washedCopper = EnsureResourceItem(FAC_ITEMS + "/Item_WashedCopperOre.asset",
                    "washed_copper_ore", "Washed Copper Ore",
                    "Hydro-classified copper concentrate stripped of gangue. Smelts into 2 Copper Ingots per unit.",
                    new Color(0.92f, 0.54f, 0.28f), 100, ResourceCategory.Component,
                    ref created, ref repaired, ref preserved);

                var tailings = EnsureResourceItem(FAC_ITEMS + "/Item_MineralTailings.asset",
                    "mineral_tailings", "Mineral Tailings",
                    "Wet mineral gangue separated during ore washing. Can be stored, reprocessed in an Ore Washer for sand and residual ore, crushed into gravel, or vitrified in a furnace into stone.",
                    new Color(0.46f, 0.43f, 0.36f), 200, ResourceCategory.Raw,
                    ref created, ref repaired, ref preserved);

                // 3. MachineRecipes for OreWasher (Washing) & Crusher (Crushing)
                var washIronRecipe = EnsureMachineRecipe(FAC_MRECIPES + "/MachineRecipe_WashIronOre.asset",
                    "Wash Iron Ore", MachineRecipeType.Washing, 4.0f,
                    new[] { (crushedIron, 2) }, washedIron, 2, tailings, 1, 1.0f,
                    ref created, ref repaired, ref preserved);

                var washCopperRecipe = EnsureMachineRecipe(FAC_MRECIPES + "/MachineRecipe_WashCopperOre.asset",
                    "Wash Copper Ore", MachineRecipeType.Washing, 4.0f,
                    new[] { (crushedCopper, 2) }, washedCopper, 2, tailings, 1, 1.0f,
                    ref created, ref repaired, ref preserved);

                var reprocessTailingsRecipe = EnsureMachineRecipe(FAC_MRECIPES + "/MachineRecipe_ReprocessTailings.asset",
                    "Reprocess Mineral Tailings", MachineRecipeType.Washing, 5.0f,
                    new[] { (tailings, 4) }, sand, 2, crushedIron, 1, 0.5f,
                    ref created, ref repaired, ref preserved);

                var crushTailingsRecipe = EnsureMachineRecipe(FAC_MRECIPES + "/MachineRecipe_CrushTailings.asset",
                    "Crush Mineral Tailings", MachineRecipeType.Crushing, 3.0f,
                    new[] { (tailings, 2) }, gravel, 1, sand, 1, 0.65f,
                    ref created, ref repaired, ref preserved);

                // 4. SmeltingRecipes for Crushed Ore, Washed Ore (Bonus Yield), and Tailings Vitrification
                var smeltCrushedIron = EnsureSmeltingRecipe(BASE_RECIPES + "/Smelt_CrushedIron.asset",
                    crushedIron, 1, ironIngot, 1, 3.5f, ref created, ref repaired, ref preserved);
                var smeltCrushedCopper = EnsureSmeltingRecipe(BASE_RECIPES + "/Smelt_CrushedCopper.asset",
                    crushedCopper, 1, copperIngot, 1, 3.5f, ref created, ref repaired, ref preserved);
                var smeltWashedIron = EnsureSmeltingRecipe(BASE_RECIPES + "/Smelt_WashedIron.asset",
                    washedIron, 1, ironIngot, 2, 3.0f, ref created, ref repaired, ref preserved);
                var smeltWashedCopper = EnsureSmeltingRecipe(BASE_RECIPES + "/Smelt_WashedCopper.asset",
                    washedCopper, 1, copperIngot, 2, 3.0f, ref created, ref repaired, ref preserved);
                var smeltTailings = EnsureSmeltingRecipe(BASE_RECIPES + "/Smelt_TailingsToSlag.asset",
                    tailings, 2, stone, 1, 3.5f, ref created, ref repaired, ref preserved);

                // 5. Pollution Profile for Ore Washing Effluent
                var washPollutionProfile = EnsurePollutionProfile(
                    POLLUTION_PROFS + "/PollutionSource_OreWashing.asset",
                    "ore_washing", "Ore Washing Effluent",
                    new PollutionLoad { airborneSmog = 0.25f, runoff = 0.06f, climateLoad = 0.005f },
                    new Vector3(0f, 1.2f, 0f),
                    ref created, ref repaired, ref preserved);

                // 6. OreWasher Prefab, BlockItem, and MachineDefinition
                var washerRecipes = new List<MachineRecipe> { washIronRecipe, washCopperRecipe, reprocessTailingsRecipe };
                var oreWasherPrefab = EnsureOreWasherPrefab(washerRecipes, washPollutionProfile,
                    ref created, ref repaired, ref preserved);

                var blockOreWasher = EnsureOreWasherBlock(oreWasherPrefab, washPollutionProfile,
                    ref created, ref repaired, ref preserved);

                EnsureMachineDefinition(FAC_MDEFS + "/MachineDef_OreWasher.asset",
                    "ore_washer", "Ore Washer",
                    "Hydro-rotary trommel and sluice classifier that washes crushed ores with water into bonus-yield washed ore and mineral tailings.",
                    MachineCategory.ChemicalPlant, 280f, 8f, 1f, 2, 4, 2,
                    new Color(0.22f, 0.28f, 0.34f), new Color(0.24f, 0.72f, 0.88f), oreWasherPrefab,
                    ref created, ref repaired, ref preserved);

                // 7. Wire Crusher & Furnaces non-destructively
                WireCrusherPrefab(crushTailingsRecipe, ref repaired, ref preserved);
                WireFurnacePrefabs(new[]
                {
                    smeltCrushedIron, smeltCrushedCopper,
                    smeltWashedIron, smeltWashedCopper,
                    smeltTailings
                }, ref repaired, ref preserved);

                // 8. Crafting Recipe & Research Node
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(REGISTRY_PATH);
                var steelPlate = LoadCanonicalItem(IND_ITEMS + "/Item_SteelPlate.asset", "steel_plate", "Item_SteelPlate")
                    ?? LoadCanonicalItem(BASE_ITEMS + "/Item_SteelIngot.asset", "steel_ingot", "Item_SteelIngot");
                var ironGear   = LoadCanonicalItem(IND_ITEMS + "/Item_IronGear.asset", "iron_gear", "Item_IronGear");
                var copperWire = LoadCanonicalItem(IND_ITEMS + "/Item_CopperWire.asset", "copper_wire", "Item_CopperWire");
                var circuit    = LoadCanonicalItem(IND_ITEMS + "/Item_Circuit.asset", "circuit", "Item_Circuit");

                var recipeOreWasher = EnsureCraftingRecipe(
                    FAC_RECIPES + "/Recipe_OreWasher.asset",
                    "Ore Washer", blockOreWasher, 1, 8f, StationTier.Assembler, false,
                    new[] { (steelPlate, 8), (ironGear, 6), (copperWire, 4), (circuit, 2) },
                    registry, ref created, ref repaired, ref preserved);

                var recipeStabilizeTailings = EnsureCraftingRecipe(
                    FAC_RECIPES + "/Recipe_CompactTailings.asset",
                    "Compact Mineral Tailings", gravel, 1, 2.5f, StationTier.CraftingBench, false,
                    new[] { (tailings, 3) },
                    registry, ref created, ref repaired, ref preserved);

                EnsureResearchNode(recipeOreWasher, recipeStabilizeTailings,
                    ref created, ref repaired, ref preserved);

                // 9. Register in ItemPersistenceCatalog
                EnsurePersisted(gravel);
                EnsurePersisted(crushedIron);
                EnsurePersisted(crushedCopper);
                EnsurePersisted(washedIron);
                EnsurePersisted(washedCopper);
                EnsurePersisted(tailings);
                EnsurePersisted(blockOreWasher);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[OreWashingSetup] Complete. Created: {created}, Repaired: {repaired}, Preserved: {preserved}.");
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("Ore Washing & Tailings Setup",
                        "Section 6.2 (4.6.0) Ore Washing, Enrichment & Tailings Loop is ready.\n\n" +
                        "Created / Verified:\n" +
                        "  - Washed Iron Ore, Washed Copper Ore, Mineral Tailings\n" +
                        "  - Ore Washer prefab, block item & machine definition\n" +
                        "  - Ore Washer recipes (Wash Iron/Copper, Reprocess Tailings)\n" +
                        "  - Crusher recipe (Crush Mineral Tailings -> Gravel + Sand)\n" +
                        "  - Bonus-yield SmeltingRecipes (Crushed Ore 1:1, Washed Ore 1:2, Tailings -> Stone)\n" +
                        "  - PollutionSource_OreWashing effluent profile & research node\n\n" +
                        $"Created: {created}   Repaired: {repaired}   Preserved: {preserved}",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[OreWashingSetup] Failed: " + ex);
                if (showDialog)
                    EditorUtility.DisplayDialog("Ore Washing Setup", "Setup failed: " + ex.Message, "OK");
            }
        }

        // ── Asset Authoring Helpers ───────────────────────────────────

        private static ItemDefinition LoadCanonicalItem(string canonicalPath, string itemId, string assetStem)
        {
            var direct = AssetDatabase.LoadAssetAtPath<ItemDefinition>(canonicalPath);
            if (direct != null) return direct;
            string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { ROOT });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item == null) continue;
                if (string.Equals(item.itemId, itemId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileNameWithoutExtension(path), assetStem, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }

        private static ResourceItem EnsureResourceItem(string path, string id, string display, string desc,
            Color tint, int maxStack, ResourceCategory subcat,
            ref int created, ref int repaired, ref int preserved)
        {
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var item = AssetDatabase.LoadAssetAtPath<ResourceItem>(path);
            bool isNew = item == null;
            if (isNew)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    AssetDatabase.DeleteAsset(path);
                item = ScriptableObject.CreateInstance<ResourceItem>();
                item.itemId = id;
                item.displayName = display;
                item.description = desc;
                item.iconTint = tint;
                item.maxStack = maxStack;
                item.massPerUnit = 1f;
                item.category = "Factory";
                item.subcategory = subcat;
                AssetDatabase.CreateAsset(item, path);
                EditorUtility.SetDirty(item);
                created++;
                return item;
            }

            bool dirty = false;
            if (string.IsNullOrWhiteSpace(item.itemId) || item.itemId == ITEM_ID_SENTINEL)
            { item.itemId = id; dirty = true; }
            if (string.IsNullOrWhiteSpace(item.displayName) || item.displayName == ITEM_NAME_SENTINEL)
            { item.displayName = display; dirty = true; }
            if (string.IsNullOrWhiteSpace(item.description))
            { item.description = desc; dirty = true; }
            if (item.maxStack <= 0 || item.maxStack == ITEM_STACK_SENTINEL)
            { item.maxStack = maxStack; dirty = true; }
            if (item.massPerUnit <= 0f)
            { item.massPerUnit = ITEM_MASS_SENTINEL; dirty = true; }
            if (string.IsNullOrWhiteSpace(item.category) || item.category == "Resources")
            { item.category = "Factory"; dirty = true; }
            if (item.iconTint == Color.white)
            { item.iconTint = tint; dirty = true; }

            if (dirty) { EditorUtility.SetDirty(item); repaired++; }
            else preserved++;
            return item;
        }

        private static MachineRecipe EnsureMachineRecipe(string path, string display,
            MachineRecipeType type, float processSeconds,
            (ItemDefinition item, int count)[] inputs,
            ItemDefinition output, int outputCount,
            ItemDefinition byproduct, int byproductCount, float byproductChance,
            ref int created, ref int repaired, ref int preserved)
        {
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var recipe = AssetDatabase.LoadAssetAtPath<MachineRecipe>(path);
            bool isNew = recipe == null;
            if (isNew)
            {
                recipe = ScriptableObject.CreateInstance<MachineRecipe>();
                recipe.displayName = display;
                recipe.recipeType = type;
                recipe.processSeconds = processSeconds;
                recipe.outputItem = output;
                recipe.outputCount = outputCount;
                recipe.byproductItem = byproduct;
                recipe.byproductCount = byproductCount;
                recipe.byproductChance = byproductChance;
                var list = new List<RecipeIngredient>();
                if (inputs != null)
                {
                    foreach (var (it, cnt) in inputs)
                        if (it != null) list.Add(new RecipeIngredient { item = it, count = Mathf.Max(1, cnt) });
                }
                recipe.inputs = list.ToArray();
                AssetDatabase.CreateAsset(recipe, path);
                EditorUtility.SetDirty(recipe);
                created++;
                return recipe;
            }

            bool dirty = false;
            if (string.IsNullOrWhiteSpace(recipe.displayName)) { recipe.displayName = display; dirty = true; }
            if (recipe.recipeType != type) { recipe.recipeType = type; dirty = true; }
            if (recipe.processSeconds <= 0f) { recipe.processSeconds = processSeconds; dirty = true; }
            if (recipe.outputItem == null && output != null) { recipe.outputItem = output; dirty = true; }
            if (recipe.outputCount <= 0) { recipe.outputCount = Mathf.Max(1, outputCount); dirty = true; }
            if (recipe.byproductItem == null && byproduct != null)
            {
                recipe.byproductItem = byproduct;
                if (recipe.byproductCount <= 0) recipe.byproductCount = Mathf.Max(1, byproductCount);
                if (recipe.byproductChance <= 0f) recipe.byproductChance = byproductChance;
                dirty = true;
            }
            if (recipe.inputs == null || recipe.inputs.Length == 0 || recipe.inputs[0].item == null)
            {
                var list = new List<RecipeIngredient>();
                if (inputs != null)
                {
                    foreach (var (it, cnt) in inputs)
                        if (it != null) list.Add(new RecipeIngredient { item = it, count = Mathf.Max(1, cnt) });
                }
                recipe.inputs = list.ToArray();
                dirty = true;
            }

            if (dirty) { EditorUtility.SetDirty(recipe); repaired++; }
            else preserved++;
            return recipe;
        }

        private static SmeltingRecipe EnsureSmeltingRecipe(string path,
            ItemDefinition input, int inputCount,
            ItemDefinition output, int outputCount, float smeltSeconds,
            ref int created, ref int repaired, ref int preserved)
        {
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var recipe = AssetDatabase.LoadAssetAtPath<SmeltingRecipe>(path);
            bool isNew = recipe == null;
            if (isNew)
            {
                recipe = ScriptableObject.CreateInstance<SmeltingRecipe>();
                recipe.input = input;
                recipe.inputCount = Mathf.Max(1, inputCount);
                recipe.output = output;
                recipe.outputCount = Mathf.Max(1, outputCount);
                recipe.smeltSeconds = smeltSeconds;
                AssetDatabase.CreateAsset(recipe, path);
                EditorUtility.SetDirty(recipe);
                created++;
                return recipe;
            }

            bool dirty = false;
            if (recipe.input == null && input != null) { recipe.input = input; dirty = true; }
            if (recipe.inputCount <= 0) { recipe.inputCount = Mathf.Max(1, inputCount); dirty = true; }
            if (recipe.output == null && output != null) { recipe.output = output; dirty = true; }
            if (recipe.outputCount <= 0) { recipe.outputCount = Mathf.Max(1, outputCount); dirty = true; }
            if (recipe.smeltSeconds <= 0f) { recipe.smeltSeconds = smeltSeconds; dirty = true; }

            if (dirty) { EditorUtility.SetDirty(recipe); repaired++; }
            else preserved++;
            return recipe;
        }

        private static PollutionSourceProfile EnsurePollutionProfile(string path, string id, string display,
            PollutionLoad defaultLoad, Vector3 defaultOffset,
            ref int created, ref int repaired, ref int preserved)
        {
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var profile = AssetDatabase.LoadAssetAtPath<PollutionSourceProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PollutionSourceProfile>();
                profile.sourceId = id;
                profile.displayName = display;
                profile.perSecond = defaultLoad;
                profile.localOffset = defaultOffset;
                AssetDatabase.CreateAsset(profile, path);
                EditorUtility.SetDirty(profile);
                created++;
                return profile;
            }

            bool dirty = false;
            if (string.IsNullOrWhiteSpace(profile.sourceId)) { profile.sourceId = id; dirty = true; }
            if (string.IsNullOrWhiteSpace(profile.displayName)) { profile.displayName = display; dirty = true; }
            if (profile.perSecond.Total <= 0f) { profile.perSecond = defaultLoad; dirty = true; }
            if (dirty) { EditorUtility.SetDirty(profile); repaired++; }
            else preserved++;
            return profile;
        }

        private static GameObject EnsureOreWasherPrefab(List<MachineRecipe> recipes,
            PollutionSourceProfile pollutionProfile,
            ref int created, ref int repaired, ref int preserved)
        {
            string prefabPath = FAC_PREFABS + "/OreWasher.prefab";
            EnsureFolder(FAC_PREFABS);
            EnsureFolder(FAC_MATS);

            bool exists = AssetDatabase.LoadMainAssetAtPath(prefabPath) != null;
            GameObject root = exists
                ? PrefabUtility.LoadPrefabContents(prefabPath)
                : new GameObject("OreWasher");

            bool dirty = !exists;

            var bodyMat   = GetOrCreateMaterial(FAC_MATS + "/Mat_OreWasherBody.mat", "Mat_OreWasherBody", new Color(0.22f, 0.28f, 0.34f));
            var drumMat   = GetOrCreateMaterial(FAC_MATS + "/Mat_OreWasherDrum.mat", "Mat_OreWasherDrum", new Color(0.28f, 0.58f, 0.74f));
            var brassMat  = GetOrCreateMaterial(FAC_MATS + "/Mat_OreWasherBrass.mat", "Mat_OreWasherBrass", new Color(0.76f, 0.56f, 0.24f));
            var waterMat  = GetOrCreateMaterial(FAC_MATS + "/Mat_OreWasherWater.mat", "Mat_OreWasherWater", new Color(0.18f, 0.62f, 0.86f));
            var oreMat    = GetOrCreateMaterial(FAC_MATS + "/Mat_OreWasherOre.mat", "Mat_OreWasherOre", new Color(0.84f, 0.54f, 0.32f));
            var lampMat   = GetOrCreateMaterial(FAC_MATS + "/Mat_OreWasherLamp.mat", "Mat_OreWasherLamp", new Color(0.24f, 0.78f, 0.88f), emissive: true);

            dirty |= EnsurePrim(root, "Generated_Base", PrimitiveType.Cube,
                new Vector3(0f, 0.14f, 0f), new Vector3(1.88f, 0.28f, 1.88f), Vector3.zero, bodyMat);
            dirty |= EnsurePrim(root, "Generated_SluiceBasin", PrimitiveType.Cube,
                new Vector3(0f, 0.52f, 0.04f), new Vector3(1.68f, 0.48f, 1.56f), Vector3.zero, bodyMat);
            dirty |= EnsurePrim(root, "Generated_WaterSurface", PrimitiveType.Cube,
                new Vector3(0f, 0.68f, 0.04f), new Vector3(1.50f, 0.08f, 1.38f), Vector3.zero, waterMat);
            dirty |= EnsurePrim(root, "Generated_TrommelDrum", PrimitiveType.Cylinder,
                new Vector3(0f, 1.18f, -0.06f), new Vector3(0.78f, 0.68f, 0.78f), new Vector3(90f, 0f, 0f), drumMat);
            dirty |= EnsurePrim(root, "Generated_AgitatorPaddle", PrimitiveType.Cube,
                new Vector3(0f, 0.76f, 0.22f), new Vector3(0.92f, 0.10f, 0.18f), Vector3.zero, brassMat);
            dirty |= EnsurePrim(root, "Generated_FeedHopper", PrimitiveType.Cube,
                new Vector3(0f, 1.56f, -0.52f), new Vector3(0.84f, 0.36f, 0.64f), Vector3.zero, brassMat);
            dirty |= EnsurePrim(root, "Generated_SprayBar", PrimitiveType.Cylinder,
                new Vector3(-0.62f, 1.44f, 0f), new Vector3(0.12f, 0.72f, 0.12f), new Vector3(90f, 0f, 0f), brassMat);
            dirty |= EnsurePrim(root, "Generated_TailingsChute", PrimitiveType.Cube,
                new Vector3(0.76f, 0.44f, 0.35f), new Vector3(0.42f, 0.24f, 0.52f), new Vector3(0f, 0f, -16f), bodyMat);
            dirty |= EnsurePrim(root, "Generated_WashedOre", PrimitiveType.Sphere,
                new Vector3(0f, 0.82f, 0.36f), new Vector3(0.26f, 0.18f, 0.26f), Vector3.zero, oreMat);
            dirty |= EnsurePrim(root, "Generated_StatusLamp", PrimitiveType.Cube,
                new Vector3(0.72f, 1.48f, -0.68f), new Vector3(0.16f, 0.16f, 0.16f), Vector3.zero, lampMat);

            var box = root.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = root.AddComponent<BoxCollider>();
                box.size = new Vector3(1.9f, 1.8f, 1.9f);
                box.center = new Vector3(0f, 0.9f, 0f);
                dirty = true;
            }

            var power = root.GetComponent<PowerConsumer>();
            if (power == null)
            {
                power = root.AddComponent<PowerConsumer>();
                power.connectRadius = 1.8f;
                power.wattsPerSecond = 8f;
                dirty = true;
            }

            var ports = root.GetComponent<PortConfig>();
            if (ports == null)
            {
                ports = root.AddComponent<PortConfig>();
                ports.EnsureAllFaces();
                dirty = true;
            }

            if (root.GetComponent<ItemPortRouting>() == null)
            {
                root.AddComponent<ItemPortRouting>();
                dirty = true;
            }

            var washer = root.GetComponent<OreWasher>();
            if (washer == null)
            {
                washer = root.AddComponent<OreWasher>();
                washer.baseWattsPerSecond = 280f;
                washer.idleWattsPerSecond = 8f;
                washer.waterLitresPerBatch = 20f;
                washer.pipedWaterRefillLps = 60f;
                dirty = true;
            }
            washer.EnsureContainers();
            washer.EnsureTanks();
            washer.knownRecipes ??= new List<MachineRecipe>();
            foreach (var r in recipes)
            {
                if (r != null && !washer.knownRecipes.Contains(r))
                {
                    washer.knownRecipes.Add(r);
                    dirty = true;
                }
            }

            if (root.GetComponent<OreWasherMotionAnimator>() == null)
            {
                root.AddComponent<OreWasherMotionAnimator>();
                dirty = true;
            }

            var emitter = root.GetComponent<PollutionEmitter>();
            if (emitter == null)
            {
                emitter = root.AddComponent<PollutionEmitter>();
                dirty = true;
            }
            if (emitter.profile == null && pollutionProfile != null)
            {
                emitter.profile = pollutionProfile;
                dirty = true;
            }

            var theme = root.GetComponent<UIThemeOverride>();
            if (theme == null)
            {
                theme = root.AddComponent<UIThemeOverride>();
                theme.overrideAccent = false;
                theme.accentColor = new Color(0.24f, 0.72f, 0.88f);
                dirty = true;
            }

            var indicator = root.GetComponent<FactoryStatusIndicator>();
            if (indicator == null)
            {
                indicator = root.AddComponent<FactoryStatusIndicator>();
                indicator.rendererChildName = "Generated_StatusLamp";
                indicator.idleColor = new Color(0.92f, 0.62f, 0.14f);
                indicator.activeColor = new Color(0.22f, 0.82f, 0.46f);
                indicator.offlineColor = new Color(0.86f, 0.24f, 0.24f);
                dirty = true;
            }

            if (dirty)
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (exists) repaired++; else created++;
            }
            else preserved++;

            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else UnityEngine.Object.DestroyImmediate(root);

            return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        private static BlockItem EnsureOreWasherBlock(GameObject prefab, PollutionSourceProfile profile,
            ref int created, ref int repaired, ref int preserved)
        {
            string path = FAC_ITEMS + "/Block_OreWasher.asset";
            var block = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool isNew = block == null;
            string baseDesc = "Hydro-rotary trommel and settling sluice that washes Crushed Ore with Water into bonus-yield Washed Ore and Mineral Tailings.";
            if (profile != null)
            {
                baseDesc += $"\n\nRated air emissions: {PollutionUnits.FormatRate(profile.perSecond.airborneSmog)} at full load." +
                            $"\nRated soil/water effluent: {PollutionUnits.FormatContaminantRate(profile.perSecond.runoff)} at full load.";
            }

            if (isNew)
            {
                block = ScriptableObject.CreateInstance<BlockItem>();
                block.itemId = "block_ore_washer";
                block.displayName = "Ore Washer";
                block.description = baseDesc;
                block.iconTint = new Color(0.24f, 0.72f, 0.88f);
                block.category = "Factory";
                block.maxStack = 10;
                block.massPerUnit = 25f;
                block.placedPrefab = prefab;
                block.maxHp = 440;
                block.requiredTier = 1;
                block.footprint = new Vector3Int(2, 2, 2);
                AssetDatabase.CreateAsset(block, path);
                EditorUtility.SetDirty(block);
                created++;
                return block;
            }

            bool dirty = false;
            if (string.IsNullOrWhiteSpace(block.itemId) || block.itemId == ITEM_ID_SENTINEL)
            { block.itemId = "block_ore_washer"; dirty = true; }
            if (string.IsNullOrWhiteSpace(block.displayName) || block.displayName == ITEM_NAME_SENTINEL)
            { block.displayName = "Ore Washer"; dirty = true; }
            if (string.IsNullOrWhiteSpace(block.description))
            { block.description = baseDesc; dirty = true; }
            if (block.placedPrefab == null && prefab != null)
            { block.placedPrefab = prefab; dirty = true; }
            if (block.maxHp <= 0) { block.maxHp = 440; dirty = true; }
            if (block.maxStack <= 0 || block.maxStack == ITEM_STACK_SENTINEL) { block.maxStack = 10; dirty = true; }
            if (block.footprint == Vector3Int.zero || block.footprint == Vector3Int.one)
            { block.footprint = new Vector3Int(2, 2, 2); dirty = true; }

            if (dirty) { EditorUtility.SetDirty(block); repaired++; }
            else preserved++;
            return block;
        }

        private static void EnsureMachineDefinition(string path, string id, string display, string desc,
            MachineCategory cat, float baseWatts, float idleWatts, float speedMult,
            int inSlots, int outSlots, int upgSlots, Color primary, Color accent, GameObject prefab,
            ref int created, ref int repaired, ref int preserved)
        {
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var def = AssetDatabase.LoadAssetAtPath<MachineDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<MachineDefinition>();
                def.machineId = id;
                def.displayName = display;
                def.description = desc;
                def.category = cat;
                def.baseWattsPerSecond = baseWatts;
                def.idleWattsPerSecond = idleWatts;
                def.speedMultiplier = speedMult;
                def.inputSlots = inSlots;
                def.outputSlots = outSlots;
                def.upgradeSlots = upgSlots;
                def.primaryColor = primary;
                def.accentColor = accent;
                def.prefab = prefab;
                AssetDatabase.CreateAsset(def, path);
                EditorUtility.SetDirty(def);
                created++;
                return;
            }

            bool dirty = false;
            if (string.IsNullOrWhiteSpace(def.machineId) || def.machineId == "machine_new")
            { def.machineId = id; dirty = true; }
            if (string.IsNullOrWhiteSpace(def.displayName) || def.displayName == "New Machine")
            { def.displayName = display; dirty = true; }
            if (def.prefab == null && prefab != null)
            { def.prefab = prefab; dirty = true; }
            if (dirty) { EditorUtility.SetDirty(def); repaired++; }
            else preserved++;
        }

        private static void WireCrusherPrefab(MachineRecipe crushTailings, ref int repaired, ref int preserved)
        {
            if (crushTailings == null) return;
            string path = FAC_PREFABS + "/Crusher.prefab";
            if (AssetDatabase.LoadMainAssetAtPath(path) == null) return;

            var root = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;
            var crusher = root.GetComponentInChildren<Crusher>(true);
            if (crusher != null)
            {
                crusher.knownRecipes ??= new List<MachineRecipe>();
                if (!crusher.knownRecipes.Contains(crushTailings))
                {
                    crusher.knownRecipes.Add(crushTailings);
                    dirty = true;
                }
            }
            if (dirty)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                repaired++;
            }
            else preserved++;
            PrefabUtility.UnloadPrefabContents(root);
        }

        private static void WireFurnacePrefabs(SmeltingRecipe[] recipesToAdd, ref int repaired, ref int preserved)
        {
            if (recipesToAdd == null || recipesToAdd.Length == 0) return;
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { ROOT });
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                bool hasFurnace = asset.GetComponentInChildren<Furnace>(true) != null
                    || asset.GetComponentInChildren<ElectricFurnace>(true) != null
                    || asset.GetComponentInChildren<GridElectricFurnace>(true) != null;
                if (!hasFurnace) continue;

                var root = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;

                foreach (var f in root.GetComponentsInChildren<Furnace>(true))
                {
                    f.knownRecipes ??= new List<SmeltingRecipe>();
                    foreach (var r in recipesToAdd)
                        if (r != null && !f.knownRecipes.Contains(r)) { f.knownRecipes.Add(r); dirty = true; }
                }
                foreach (var ef in root.GetComponentsInChildren<ElectricFurnace>(true))
                {
                    ef.knownRecipes ??= new List<SmeltingRecipe>();
                    foreach (var r in recipesToAdd)
                        if (r != null && !ef.knownRecipes.Contains(r)) { ef.knownRecipes.Add(r); dirty = true; }
                }
                foreach (var gf in root.GetComponentsInChildren<GridElectricFurnace>(true))
                {
                    gf.knownRecipes ??= new List<SmeltingRecipe>();
                    foreach (var r in recipesToAdd)
                        if (r != null && !gf.knownRecipes.Contains(r)) { gf.knownRecipes.Add(r); dirty = true; }
                }

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    repaired++;
                }
                else preserved++;
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static RecipeDefinition EnsureCraftingRecipe(string path, string display,
            ItemDefinition output, int outputCount, float craftSeconds, StationTier station, bool unlockedByDefault,
            (ItemDefinition item, int count)[] inputs, RecipeRegistry registry,
            ref int created, ref int repaired, ref int preserved)
        {
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
            bool isNew = recipe == null;
            if (isNew)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = display;
                recipe.outputItem = output;
                recipe.outputCount = Mathf.Max(1, outputCount);
                recipe.craftSeconds = craftSeconds;
                recipe.requiredStation = station;
                recipe.unlockedByDefault = unlockedByDefault;
                var list = new List<RecipeIngredient>();
                if (inputs != null)
                {
                    foreach (var (it, cnt) in inputs)
                        if (it != null) list.Add(new RecipeIngredient { item = it, count = Mathf.Max(1, cnt) });
                }
                recipe.inputs = list.ToArray();
                AssetDatabase.CreateAsset(recipe, path);
                EditorUtility.SetDirty(recipe);
                created++;
            }
            else
            {
                bool dirty = false;
                if (string.IsNullOrWhiteSpace(recipe.displayName)) { recipe.displayName = display; dirty = true; }
                if (recipe.outputItem == null && output != null) { recipe.outputItem = output; dirty = true; }
                if (recipe.outputCount <= 0) { recipe.outputCount = Mathf.Max(1, outputCount); dirty = true; }
                if (recipe.craftSeconds <= 0f) { recipe.craftSeconds = craftSeconds; dirty = true; }
                if (recipe.inputs == null || recipe.inputs.Length == 0 || recipe.inputs[0].item == null)
                {
                    var list = new List<RecipeIngredient>();
                    if (inputs != null)
                    {
                        foreach (var (it, cnt) in inputs)
                            if (it != null) list.Add(new RecipeIngredient { item = it, count = Mathf.Max(1, cnt) });
                    }
                    recipe.inputs = list.ToArray();
                    dirty = true;
                }
                if (dirty) { EditorUtility.SetDirty(recipe); repaired++; }
                else preserved++;
            }

            if (registry != null)
            {
                registry.recipes ??= new List<RecipeDefinition>();
                if (!registry.recipes.Contains(recipe))
                {
                    registry.recipes.Add(recipe);
                    EditorUtility.SetDirty(registry);
                }
            }
            return recipe;
        }

        private static void EnsureResearchNode(RecipeDefinition washerRecipe, RecipeDefinition tailingsRecipe,
            ref int created, ref int repaired, ref int preserved)
        {
            EnsureFolder(NODES_FOLDER);
            string nodePath = NODES_FOLDER + "/ResNode_OreWashing.asset";
            var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(nodePath);
            bool isNew = node == null;

            var prereqFactory = AssetDatabase.LoadAssetAtPath<ResearchNode>(NODES_FOLDER + "/res_factory_automation.asset")
                ?? AssetDatabase.LoadAssetAtPath<ResearchNode>(NODES_FOLDER + "/res_factory_logistics.asset");
            var prereqFluid = AssetDatabase.LoadAssetAtPath<ResearchNode>(NODES_FOLDER + "/res_fluid_handling.asset");
            var sciT2 = AssetDatabase.LoadAssetAtPath<ScienceItem>(BASE_ITEMS + "/Item_ScienceT2.asset")
                ?? AssetDatabase.LoadAssetAtPath<ScienceItem>(SCIENCE_FOLDER + "/Science_Red.asset");
            var sciT3 = AssetDatabase.LoadAssetAtPath<ScienceItem>(BASE_ITEMS + "/Item_ScienceT3.asset")
                ?? AssetDatabase.LoadAssetAtPath<ScienceItem>(SCIENCE_FOLDER + "/Science_Green.asset");

            if (isNew)
            {
                node = ScriptableObject.CreateInstance<ResearchNode>();
                node.nodeId = "res_ore_washing";
                node.displayName = "Ore Washing & Enrichment";
                node.description = "Hydro-rotary ore washing and sluice classification. Washes crushed ores with water into high-purity concentrate that smelts for double ingot yield, and recovers sand and gravel from mineral tailings.";
                node.iconTint = new Color(0.24f, 0.72f, 0.88f);
                node.category = ResearchCategory.Environment;
                node.subCategory = ResearchSubCategory.Production;
                node.tier = 4;
                node.column = 2;
                node.researchSeconds = 75f;
                var prereqList = new List<ResearchNode>();
                if (prereqFactory != null) prereqList.Add(prereqFactory);
                if (prereqFluid != null && !prereqList.Contains(prereqFluid)) prereqList.Add(prereqFluid);
                node.prerequisites = prereqList.ToArray();

                var costs = new List<ResearchNode.ScienceCost>();
                if (sciT2 != null) costs.Add(new ResearchNode.ScienceCost { pack = sciT2, count = 20 });
                if (sciT3 != null) costs.Add(new ResearchNode.ScienceCost { pack = sciT3, count = 8 });
                node.cost = costs.ToArray();

                var unlocks = new List<RecipeDefinition>();
                if (washerRecipe != null) unlocks.Add(washerRecipe);
                if (tailingsRecipe != null) unlocks.Add(tailingsRecipe);
                node.unlocksRecipes = unlocks.ToArray();

                AssetDatabase.CreateAsset(node, nodePath);
                EditorUtility.SetDirty(node);
                created++;
            }
            else
            {
                bool dirty = false;
                if (string.IsNullOrWhiteSpace(node.nodeId) || node.nodeId == NODE_ID_SENTINEL)
                { node.nodeId = "res_ore_washing"; dirty = true; }
                if (string.IsNullOrWhiteSpace(node.displayName) || node.displayName == NODE_NAME_SENTINEL)
                { node.displayName = "Ore Washing & Enrichment"; dirty = true; }
                if (string.IsNullOrWhiteSpace(node.description))
                {
                    node.description = "Hydro-rotary ore washing and sluice classification. Washes crushed ores with water into high-purity concentrate that smelts for double ingot yield, and recovers sand and gravel from mineral tailings.";
                    dirty = true;
                }
                if (node.researchSeconds <= 0f || Mathf.Approximately(node.researchSeconds, NODE_SECONDS_SENTINEL))
                { node.researchSeconds = 75f; dirty = true; }
                if ((node.prerequisites == null || node.prerequisites.Length == 0) && (prereqFactory != null || prereqFluid != null))
                {
                    var prereqList = new List<ResearchNode>();
                    if (prereqFactory != null) prereqList.Add(prereqFactory);
                    if (prereqFluid != null && !prereqList.Contains(prereqFluid)) prereqList.Add(prereqFluid);
                    node.prerequisites = prereqList.ToArray();
                    dirty = true;
                }
                if ((node.cost == null || node.cost.Length == 0) && (sciT2 != null || sciT3 != null))
                {
                    var costs = new List<ResearchNode.ScienceCost>();
                    if (sciT2 != null) costs.Add(new ResearchNode.ScienceCost { pack = sciT2, count = 20 });
                    if (sciT3 != null) costs.Add(new ResearchNode.ScienceCost { pack = sciT3, count = 8 });
                    node.cost = costs.ToArray();
                    dirty = true;
                }

                var unlocks = new List<RecipeDefinition>(node.unlocksRecipes ?? Array.Empty<RecipeDefinition>());
                if (washerRecipe != null && !unlocks.Contains(washerRecipe)) { unlocks.Add(washerRecipe); dirty = true; }
                if (tailingsRecipe != null && !unlocks.Contains(tailingsRecipe)) { unlocks.Add(tailingsRecipe); dirty = true; }
                if (dirty) node.unlocksRecipes = unlocks.ToArray();

                if (dirty) { EditorUtility.SetDirty(node); repaired++; }
                else preserved++;
            }

            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(TREE_PATH);
            if (tree != null)
            {
                tree.nodes ??= new List<ResearchNode>();
                if (!tree.nodes.Contains(node))
                {
                    tree.nodes.Add(node);
                    EditorUtility.SetDirty(tree);
                }
            }
        }

        private static bool EnsurePrim(GameObject root, string name, PrimitiveType type,
            Vector3 localPos, Vector3 localScale, Vector3 localEuler, Material mat)
        {
            var existing = root.transform.Find(name);
            if (existing != null)
            {
                var rend = existing.GetComponent<Renderer>();
                if (rend != null && rend.sharedMaterial == null && mat != null)
                {
                    rend.sharedMaterial = mat;
                    return true;
                }
                return false;
            }

            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.transform.localEulerAngles = localEuler;
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            return true;
        }

        private static Material GetOrCreateMaterial(string path, string name, Color color, bool emissive = false)
        {
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = name, color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 1.4f);
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void EnsurePersisted(ItemDefinition item)
        {
            if (item == null) return;
            EnsureFolder(Path.GetDirectoryName(CATALOG_PATH)?.Replace('\\', '/'));
            var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                AssetDatabase.CreateAsset(catalog, CATALOG_PATH);
            }
            catalog.items ??= new List<ItemDefinition>();
            if (!catalog.items.Contains(item))
            {
                catalog.items.Add(item);
                EditorUtility.SetDirty(catalog);
            }
        }

        private static void EnsureFolders()
        {
            EnsureFolder(FAC_ROOT);
            EnsureFolder(FAC_ITEMS);
            EnsureFolder(FAC_PREFABS);
            EnsureFolder(FAC_MATS);
            EnsureFolder(FAC_RECIPES);
            EnsureFolder(FAC_MRECIPES);
            EnsureFolder(FAC_MDEFS);
            EnsureFolder(POLLUTION_PROFS);
            EnsureFolder(NODES_FOLDER);
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
