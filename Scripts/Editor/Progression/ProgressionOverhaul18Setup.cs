// Assets/Scripts/Editor/Progression/ProgressionOverhaul18Setup.cs
//
// Major 18.0.0-dev Progression, 6-Science-Pack & Asset Consolidation Overhaul.
// Idempotent, non-destructive editor setup step invoked from
// Tools -> Voxel Engine -> Voxel Engine Setup ("Core & Project Bootstrap" and "Maintenance & Repair").
//
// Responsibilities:
//   1. Expands Science Packs to 6 tiers (I Red, II Green, III Blue, IV Purple, V Amber/Gold, VI Cyan/White)
//      with Pack I default-unlocked in hand and Packs II–VI gated by dedicated research nodes.
//   2. Retires Water Bucket, Fire Igniter, up_flight, up_stam, and broken duplicate res_adv_automation.
//   3. Replaces up_inv with Exoskeletal Load Harness (+75 kg carry capacity per rank, 6 ranks).
//   4. Consolidates all duplicate ItemDefinition, CropDefinition, and RecipeDefinition twins by repointing
//      every serialized reference across prefabs, ScriptableObjects, and open scenes before deleting retired files.
//   5. Fixes early-game station softlocks (Iron tools, Plates, Gears, Electronic Circuit craftable at Crafting Bench).
//   6. Registers all valid ResearchNode assets into ResearchTree.asset, authors missing progression nodes
//      (res_science_pack_2..6, res_storage_crafting, res_build_from_network, res_railways, res_rail_signalling),
//      wires every unclaimed recipe, locks research-claimed recipes, and balances T1–T8 costs across Packs I–VI.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoxelEngine.Crafting;
using VoxelEngine.Farming;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    public static class ProgressionOverhaul18Setup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string ItemsDir = Root + "/Items";
        private const string RecipesDir = Root + "/Recipes";
        private const string ResearchDir = Root + "/Research";
        private const string NodesDir = ResearchDir + "/Nodes";
        private const string TreePath = ResearchDir + "/ResearchTree.asset";
        private const string RegistryPath = Root + "/RecipeRegistry.asset";

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Voxel Engine — 18.1.0 Overhaul",
                    "Exit Play Mode before running the progression overhaul.", "OK");
                return;
            }

            var summary = ExecuteCore(silent: false);
            EditorUtility.DisplayDialog("Voxel Engine — 18.1.0 Progression & Icon Overhaul", summary, "OK");
        }

        public static void RunSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            ExecuteCore(silent: true);
        }

        private static string ExecuteCore(bool silent = false)
        {
            EnsureFolder(ItemsDir);
            EnsureFolder(RecipesDir);
            EnsureFolder(ResearchDir);
            EnsureFolder(NodesDir);

            // Ensure canonical Farming & Control Rod assets exist before repointing twins.
            FarmingSetupGuide.CreateAll();
            VoxelEngine.Nuclear.NuclearSetupGuide.CreateAll();

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(RegistryPath);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<RecipeRegistry>();
                AssetDatabase.CreateAsset(registry, RegistryPath);
            }
            if (registry.recipes == null) registry.recipes = new List<RecipeDefinition>();

            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(TreePath);
            if (tree == null)
            {
                tree = ScriptableObject.CreateInstance<ResearchTree>();
                AssetDatabase.CreateAsset(tree, TreePath);
            }
            if (tree.nodes == null) tree.nodes = new List<ResearchNode>();

            // 1) Normalize canonical Cobalt, Sand, Biomass, and Liquid Canister before twin consolidation.
            NormalizeCanonicalResource(ItemsDir + "/Item_Cobalt.asset", "cobalt", "Cobalt", "Resource");
            NormalizeCanonicalResource(ItemsDir + "/Item_Sand.asset", "sand", "Sand", "Resource");
            NormalizeCanonicalResource(ItemsDir + "/Item_Biomass.asset", "biomass", "Biomass", "Farming");
            var canister = EnsureCanonicalLiquidCanister(registry, out var canisterRecipe);

            // 2) Repoint all duplicate Items, Crops, and Recipes across the entire project and delete retired twins.
            int repointedProps = ConsolidateDuplicateAssets(registry, tree, out int deletedTwins);

            // 3) Author Science Packs I–VI + Recipes + Research Lab 6-slot container + Icon Sync.
            var packs = EnsureSixSciencePacks(registry, out var packRecipes);
            UpgradeResearchLabPrefab();
            ItemIconSync.Sync(auto: true);

            // 4) Remove retired PlayerUpgrade / duplicate ResearchNodes (up_flight, up_stam, res_adv_automation).
            int removedNodes = RemoveRetiredResearchNodes(tree);

            // 5) Update Player Upgrade nodes (up_inv -> Exoskeletal Load Harness +75 kg/rank).
            UpdatePlayerUpgradeNodes(tree, packs);

            // 6) Fix early-game Crafting Bench bootstrap softlocks (Iron tools, Plates, Gears, Electronic Circuit).
            int stationFixes = FixBootstrapCraftingStations();

            // 7) Author new progression nodes & register all valid ResearchNode assets into ResearchTree.
            int registeredNodes = EnsureAndRegisterAllResearchNodes(tree, registry, packs, packRecipes, canisterRecipe);

            // 8) Wire every unclaimed / ungated recipe onto its canonical ResearchNode & lock claimed recipes.
            int wiredRecipes = WireAllProgressionRecipes(tree, registry);

            // 9) Balance science pack costs across T1–T8 (Packs I–VI) & enforce DAG bootstrap safety.
            int rebalancedNodes = RebalanceAllNodeScienceCosts(tree, packs);

            // 10) Final registry, tree, item-description, icon-binding & crop-icon hygiene.
            CleanRegistryAndTree(registry, tree);
            int filledDescriptions = PopulateMissingItemDescriptions();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ItemIconSync.Sync(auto: silent);
            FarmingSetupGuide.SyncCropIcons();
            AssetDatabase.SaveAssets();

            string summary =
                "[18.1.0-dev] Progression, Wild-Crop & Item-Icon Overhaul complete:\n\n" +
                "• Science Packs I–VI configured (Red, Green, Blue, Purple, Amber, Cyan)\n" +
                "• Farming cooking recipes registered in RecipeRegistry & 6 wild crop scatter prefabs wired to habitable biomes\n" +
                $"• Serialized references repointed: {repointedProps}\n" +
                $"• Retired duplicate/legacy assets deleted: {deletedTwins}\n" +
                $"• Retired research nodes removed: {removedNodes}\n" +
                $"• ResearchTree active nodes registered: {tree.nodes.Count} (+{registeredNodes} newly linked)\n" +
                $"• Crafting station bootstrap fixes applied: {stationFixes}\n" +
                $"• Progression recipes wired & gated: {wiredRecipes}\n" +
                $"• Research node costs balanced across Packs I–VI: {rebalancedNodes}\n" +
                $"• Blank item descriptions populated: {filledDescriptions}";
            Debug.Log(summary.Replace("\n", " | "));
            return summary;
        }

        private static int PopulateMissingItemDescriptions()
        {
            int updated = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { Root }))
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null || !string.IsNullOrWhiteSpace(item.description)) continue;

                string label = !string.IsNullOrWhiteSpace(item.displayName) ? item.displayName.Trim() : item.name;
                string id = (item.itemId ?? string.Empty).Trim().ToLowerInvariant();
                string cat = (item.category ?? string.Empty).Trim();

                string desc = id switch
                {
                    "cobalt" => "Dense bluish-silver transition-metal ore refined for superalloys, reactor control rods, and high-energy cells.",
                    "sand" => "Fine silica aggregate harvested from beaches, riverbeds, and deserts. Smelt into Glass or blend into concrete and asphalt.",
                    "stationary_radar_beacon" => "Fixed surface radar transponder that marks industrial outposts and relays local airspace telemetry.",
                    _ when id.StartsWith("gitem_") => $"Modular grid-construct component ({label}) engineered for vehicles, vessels, and orbital frames.",
                    _ when id.StartsWith("build_") => $"Tiered structural building element ({label}) supporting wood, stone, concrete, and reinforced construction.",
                    _ when !string.IsNullOrEmpty(cat) => $"{label} ({cat}) used in industrial crafting, construction, and logistics.",
                    _ => $"{label} used in industrial crafting, construction, and world progression."
                };

                item.description = desc;
                EditorUtility.SetDirty(item);
                updated++;
            }
            return updated;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 1. Canonical Resource & Liquid Canister Normalization
        // ─────────────────────────────────────────────────────────────────────────

        private static void NormalizeCanonicalResource(string path, string itemId, string displayName, string category)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item == null) return;
            bool dirty = false;
            if (!string.Equals(item.itemId, itemId, StringComparison.Ordinal)) { item.itemId = itemId; dirty = true; }
            if (string.IsNullOrWhiteSpace(item.displayName)) { item.displayName = displayName; dirty = true; }
            if (string.IsNullOrWhiteSpace(item.category) || item.category == "Blocks") { item.category = category; dirty = true; }
            if (item.maxStack <= 0) { item.maxStack = 999; dirty = true; }
            if (item.massPerUnit <= 0f) { item.massPerUnit = 1f; dirty = true; }
            if (dirty) EditorUtility.SetDirty(item);
        }

        private static LiquidCanister EnsureCanonicalLiquidCanister(RecipeRegistry registry, out RecipeDefinition recipe)
        {
            string itemPath = ItemsDir + "/Tool_LiquidCanister.asset";
            var canister = AssetDatabase.LoadAssetAtPath<LiquidCanister>(itemPath);
            if (canister == null)
            {
                canister = ScriptableObject.CreateInstance<LiquidCanister>();
                AssetDatabase.CreateAsset(canister, itemPath);
            }
            canister.itemId = "liquid_canister";
            canister.displayName = "Liquid Canister";
            canister.description =
                "A 10 L industrial canister that holds ONE liquid at a time. RMB on a liquid pool to scoop " +
                "0.5 L per click until full; LMB pours 0.5 L into the world. RMB a liquid tank or water pump " +
                "to transfer liquid, or RMB an active jack pump to draw crude oil.";
            canister.maxStack = 1;
            canister.maxDurability = LiquidCanister.CapacityMl;
            canister.massPerUnit = 2f;
            canister.toolType = ToolType.Other;
            canister.category = "Tools";
            if (canister.iconTint == default) canister.iconTint = new Color(0.20f, 0.50f, 0.85f);
            // Force re-serialization so any legacy m_EditorClassIdentifier is rewritten cleanly.
            EditorUtility.SetDirty(canister);

            string recPath = RecipesDir + "/Recipe_LiquidCanister.asset";
            recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recPath);
            if (recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, recPath);
            }
            recipe.displayName = "Liquid Canister";
            recipe.outputItem = canister;
            recipe.outputCount = 1;
            recipe.requiredStation = StationTier.CraftingBench;
            if (recipe.craftSeconds <= 0f) recipe.craftSeconds = 2.5f;
            recipe.unlockedByDefault = false;
            var ironIngot = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemsDir + "/Item_IronIngot.asset");
            if ((recipe.inputs == null || recipe.inputs.Length == 0 || recipe.inputs[0].item == null) && ironIngot != null)
            {
                recipe.inputs = new[] { new RecipeIngredient { item = ironIngot, count = 3 } };
            }
            EditorUtility.SetDirty(recipe);

            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }
            return canister;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 2. Duplicate Asset Consolidation & Retired Tool Removal
        // ─────────────────────────────────────────────────────────────────────────

        private static int ConsolidateDuplicateAssets(RecipeRegistry registry, ResearchTree tree, out int deletedCount)
        {
            var repointPairs = new (string retired, string canonical)[]
            {
                // Water Bucket -> Canonical Liquid Canister
                (ItemsDir + "/Tool_WaterBucket.asset",                     ItemsDir + "/Tool_LiquidCanister.asset"),
                (RecipesDir + "/Recipe_WaterBucket.asset",                 RecipesDir + "/Recipe_LiquidCanister.asset"),
                (Root + "/Fluids/Recipes/Recipe_LiquidCanister.asset",     RecipesDir + "/Recipe_LiquidCanister.asset"),

                // Ore / Raw Material / Biomass twins
                (Root + "/Nuclear/Items/Item_Cobalt.asset",                ItemsDir + "/Item_Cobalt.asset"),
                (Root + "/Industrial/Items/Item_Sand.asset",               ItemsDir + "/Item_Sand.asset"),
                (Root + "/Survival/MiscItems/Item_Biomass.asset",          ItemsDir + "/Item_Biomass.asset"),

                // Nuclear twins -> Canonical Survival/NuclearItems
                (Root + "/Nuclear/Items/Item_DepletedUranium.asset",       Root + "/Survival/NuclearItems/Item_DepletedUranium.asset"),
                (Root + "/Nuclear/Items/Item_EnrichedFuelRod.asset",       Root + "/Survival/NuclearItems/Item_EnrichedFuelRod.asset"),
                (Root + "/Nuclear/Items/Item_HighLevelWaste.asset",        Root + "/Survival/NuclearItems/Item_HighLevelWaste.asset"),
                (Root + "/Nuclear/Items/Item_LEUPellet.asset",             Root + "/Survival/NuclearItems/Item_LEUPellet.asset"),
                (Root + "/Nuclear/Items/Item_SpentFuelRod.asset",          Root + "/Survival/NuclearItems/Item_SpentFuelRod.asset"),

                // Farming Food, Seed & Crop twins -> Canonical Farming/*
                (Root + "/Survival/Items/Food_Wheat.asset",                Root + "/Farming/Foods/Food_Wheat.asset"),
                (Root + "/Survival/Items/Food_Corn.asset",                 Root + "/Farming/Foods/Food_Corn.asset"),
                (Root + "/Survival/Items/Food_Carrot.asset",               Root + "/Farming/Foods/Food_Carrot.asset"),
                (Root + "/Survival/Items/Food_Bread.asset",                Root + "/Farming/Foods/Food_Bread.asset"),
                (Root + "/Survival/Items/Food_Stew.asset",                 Root + "/Farming/Foods/Food_Stew.asset"),
                (Root + "/Survival/Items/Seed_Wheat.asset",                Root + "/Farming/Seeds/Seed_Wheat.asset"),
                (Root + "/Survival/Items/Seed_Corn.asset",                 Root + "/Farming/Seeds/Seed_Corn.asset"),
                (Root + "/Survival/Items/Seed_Carrot.asset",               Root + "/Farming/Seeds/Seed_Carrot.asset"),
                (Root + "/Survival/Crops/Crop_Wheat.asset",                Root + "/Farming/Crops/Crop_Wheat.asset"),
                (Root + "/Survival/Crops/Crop_Corn.asset",                 Root + "/Farming/Crops/Crop_Corn.asset"),
                (Root + "/Survival/Crops/Crop_Carrot.asset",               Root + "/Farming/Crops/Crop_Carrot.asset"),
                (Root + "/Survival/FarmRecipes/Recipe_Cook_Bread.asset",   Root + "/Farming/Recipes/Cook_Bread.asset"),
                (Root + "/Survival/FarmRecipes/Recipe_Cook_Stew.asset",    Root + "/Farming/Recipes/Cook_Stew.asset"),

                // Industrial / Research / Fluid / Grid duplicate recipes
                (Root + "/Industrial/Recipes/Recipe_AdvCircuit.asset",             RecipesDir + "/Recipe_AdvCircuit.asset"),
                (Root + "/Industrial/Recipes/Recipe_ChemicalPlant.asset",          RecipesDir + "/Recipe_ChemicalPlant.asset"),
                (Root + "/Industrial/Recipes/Recipe_Circuit.asset",                RecipesDir + "/Recipe_Circuit.asset"),
                (Root + "/Industrial/Recipes/Recipe_CopperPlate.asset",            RecipesDir + "/Recipe_CopperPlate.asset"),
                (Root + "/Industrial/Recipes/Recipe_IronGear.asset",               RecipesDir + "/Recipe_IronGear.asset"),
                (Root + "/Industrial/Recipes/Recipe_IronPlate.asset",              RecipesDir + "/Recipe_IronPlate.asset"),
                (Root + "/Industrial/Recipes/Recipe_OilRefinery.asset",            RecipesDir + "/Recipe_OilRefinery.asset"),
                (Root + "/Industrial/Recipes/Recipe_Pumpjack.asset",               RecipesDir + "/Recipe_Pumpjack.asset"),
                (Root + "/Industrial/Recipes/Recipe_StationaryDockingPort.asset",  RecipesDir + "/Recipe_StationaryDockingPort.asset"),
                (Root + "/Industrial/Recipes/Recipe_SteelPlate.asset",             RecipesDir + "/Recipe_SteelPlate.asset"),
                (Root + "/Industrial/Recipes/Recipe_WirelessStorageTerminal.asset",RecipesDir + "/Recipe_WirelessStorageTerminal.asset"),
                (Root + "/Research/Recipes/Recipe_ResearchLab.asset",              RecipesDir + "/Recipe_ResearchLab.asset"),
                (RecipesDir + "/Recipe_PipeGlass.asset",                           Root + "/Fluids/Recipes/Recipe_PipeGlass.asset"),
                (RecipesDir + "/Recipe_PipeSolid.asset",                           Root + "/Fluids/Recipes/Recipe_PipeSolid.asset"),
                (RecipesDir + "/Recipe_TankGlass.asset",                           Root + "/Fluids/Recipes/Recipe_TankGlass.asset"),
                (RecipesDir + "/Recipe_TankSolid.asset",                           Root + "/Fluids/Recipes/Recipe_TankSolid.asset"),
                (RecipesDir + "/Recipe_WaterPump.asset",                           Root + "/Fluids/Recipes/Recipe_WaterPump.asset"),
                (RecipesDir + "/Recipe_PortableHydrogenTank.asset",                Root + "/GridSystem/Recipes/Recipe_PortableHydrogenTank.asset"),
                (Root + "/GridSystem/Recipes/Recipe_GThrustLarge.asset",           Root + "/GridSystem/Recipes/Recipe_GAtmoThruster_Large.asset"),
            };

            var repointMap = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            var pathsToDelete = new List<string>();

            foreach (var (retiredPath, canonicalPath) in repointPairs)
            {
                var retiredObj = AssetDatabase.LoadMainAssetAtPath(retiredPath);
                var canonicalObj = AssetDatabase.LoadMainAssetAtPath(canonicalPath);
                if (retiredObj != null && canonicalObj != null && retiredObj != canonicalObj)
                {
                    repointMap[retiredObj] = canonicalObj;
                    // Transfer icon if canonical lacks one and retired had one
                    if (canonicalObj is ItemDefinition canonItem && retiredObj is ItemDefinition retItem
                        && canonItem.icon == null && retItem.icon != null)
                    {
                        canonItem.icon = retItem.icon;
                        EditorUtility.SetDirty(canonItem);
                    }
                    pathsToDelete.Add(retiredPath);
                }
            }

            // Pure deletions (Fire Igniter + Water Bucket icon)
            string[] pureDeletePaths =
            {
                ItemsDir + "/Tool_FireIgniter.asset",
                RecipesDir + "/Recipe_FireIgniter.asset",
                Root + "/ItemIcons/Items/water_bucket.png",
            };
            var nullOutObjects = new HashSet<UnityEngine.Object>();
            foreach (string p in pureDeletePaths)
            {
                var obj = AssetDatabase.LoadMainAssetAtPath(p);
                if (obj != null)
                {
                    nullOutObjects.Add(obj);
                    pathsToDelete.Add(p);
                }
            }

            int repointed = 0;
            if (repointMap.Count > 0 || nullOutObjects.Count > 0)
            {
                // Scan all ScriptableObjects and Prefabs under Assets/VoxelEngineAssets
                string[] assetGuids = AssetDatabase.FindAssets("", new[] { Root });
                var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string guid in assetGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath) || !visitedPaths.Add(assetPath)) continue;
                    if (pathsToDelete.Contains(assetPath)) continue;

                    if (assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    {
                        var main = AssetDatabase.LoadMainAssetAtPath(assetPath);
                        if (main != null)
                            repointed += RepointSerializedObject(main, repointMap, nullOutObjects);
                    }
                    else if (assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                        if (prefab != null)
                        {
                            foreach (var comp in prefab.GetComponentsInChildren<Component>(true))
                            {
                                if (comp == null) continue;
                                repointed += RepointSerializedObject(comp, repointMap, nullOutObjects);
                            }
                        }
                    }
                }

                // Also scan loaded scenes
                for (int s = 0; s < SceneManager.sceneCount; s++)
                {
                    var scene = SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;
                    int before = repointed;
                    foreach (var rootGo in scene.GetRootGameObjects())
                    {
                        foreach (var comp in rootGo.GetComponentsInChildren<Component>(true))
                        {
                            if (comp == null) continue;
                            repointed += RepointSerializedObject(comp, repointMap, nullOutObjects);
                        }
                    }
                    if (repointed > before)
                        EditorSceneManager.MarkSceneDirty(scene);
                }
            }

            AssetDatabase.SaveAssets();

            deletedCount = 0;
            foreach (string delPath in pathsToDelete.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (AssetDatabase.LoadMainAssetAtPath(delPath) != null || File.Exists(delPath))
                {
                    if (AssetDatabase.DeleteAsset(delPath))
                        deletedCount++;
                }
            }
            return repointed;
        }

        private static int RepointSerializedObject(
            UnityEngine.Object target,
            Dictionary<UnityEngine.Object, UnityEngine.Object> repointMap,
            HashSet<UnityEngine.Object> nullOutObjects)
        {
            var so = new SerializedObject(target);
            var prop = so.GetIterator();
            int count = 0;
            bool enterChildren = true;
            while (prop.Next(enterChildren))
            {
                enterChildren = true;
                if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                var val = prop.objectReferenceValue;
                if (val == null) continue;
                if (repointMap.TryGetValue(val, out var replacement))
                {
                    prop.objectReferenceValue = replacement;
                    count++;
                }
                else if (nullOutObjects.Contains(val))
                {
                    prop.objectReferenceValue = null;
                    count++;
                }
            }
            if (count > 0)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(target);
            }
            return count;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 3. Six Science Packs (I–VI) + Canonical Recipes + Research Lab
        // ─────────────────────────────────────────────────────────────────────────

        private static ScienceItem[] EnsureSixSciencePacks(RecipeRegistry registry, out RecipeDefinition[] recipes)
        {
            var packs = new ScienceItem[ScienceItem.MaxTier + 1];
            recipes = new RecipeDefinition[ScienceItem.MaxTier + 1];

            (string name, string desc, float mass)[] specs =
            {
                default,
                ("Science Pack I",
                 "Red basic research pack. Crafted in hand from rough timber and field stone to fund early survival and metallurgical research.",
                 0.25f),
                ("Science Pack II",
                 "Green automation research pack. Combines smelted iron and copper at a Crafting Bench to drive electromechanical, fluid, and early factory research.",
                 0.30f),
                ("Science Pack III",
                 "Blue industrial research pack. Assembled from structural steel, electronic circuits, and refined copper to power grid ships, logistics networks, and automated manufacturing.",
                 0.35f),
                ("Science Pack IV",
                 "Purple chemical research pack. Polymer-sealed catalyst vial assembled from plastic bars, steel plates, and circuitry for petrochemicals, atmospheric telemetry, and mass storage.",
                 0.40f),
                ("Science Pack V",
                 "Amber high-energy research pack. Machined brass-and-graphite containment cell housing silicon logic and advanced circuits for nuclear engineering, orbital science, and heavy industry.",
                 0.45f),
                ("Science Pack VI",
                 "Cyan astral singularity research pack. Platinum-and-gold magnetic core stabilized by low-enriched uranium and advanced circuitry for orbital stations, warp drives, and singularity harvesting.",
                 0.50f),
            };

            for (int tier = 1; tier <= ScienceItem.MaxTier; tier++)
            {
                string path = $"{ItemsDir}/Item_ScienceT{tier}.asset";
                var pack = AssetDatabase.LoadAssetAtPath<ScienceItem>(path);
                if (pack == null)
                {
                    pack = ScriptableObject.CreateInstance<ScienceItem>();
                    AssetDatabase.CreateAsset(pack, path);
                }
                pack.itemId = $"item_sciencet{tier}";
                pack.displayName = specs[tier].name;
                pack.description = specs[tier].desc;
                pack.tier = tier;
                pack.iconTint = ScienceItem.GetTierColor(tier);
                pack.category = "Science";
                pack.subcategory = ResourceCategory.Component;
                pack.maxStack = 999;
                pack.massPerUnit = specs[tier].mass;

                var iconSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Root}/ItemIcons/Items/item_sciencet{tier}.png");
                if (iconSprite != null) pack.icon = iconSprite;
                EditorUtility.SetDirty(pack);
                packs[tier] = pack;
            }

            var wood       = LoadItem(ItemsDir + "/Item_WoodLog.asset", "wood_log");
            var stone      = LoadItem(ItemsDir + "/Item_Stone.asset", "stone");
            var iron       = LoadItem(ItemsDir + "/Item_IronIngot.asset", "iron_ingot");
            var copper     = LoadItem(ItemsDir + "/Item_CopperIngot.asset", "copper_ingot");
            var steel      = LoadItem(ItemsDir + "/Item_SteelIngot.asset", "steel_ingot");
            var circuit    = LoadItem(Root + "/Industrial/Items/Item_Circuit.asset", "item_circuit");
            var plastic    = LoadItem(Root + "/Industrial/Items/Item_Plastic.asset", "item_plastic");
            var steelPlate = LoadItem(Root + "/Industrial/Items/Item_SteelPlate.asset", "item_steelplate");
            var advCircuit = LoadItem(Root + "/Industrial/Items/Item_AdvCircuit.asset", "item_advcircuit");
            var brass      = LoadItem(ItemsDir + "/Item_BrassIngot.asset", "brass_ingot");
            var silicon    = LoadItem(ItemsDir + "/Item_Silicon.asset", "silicon");
            var graphite   = LoadItem(Root + "/Industrial/Items/Item_Graphite.asset", "graphite");
            var leuPellet  = LoadItem(Root + "/Survival/NuclearItems/Item_LEUPellet.asset", "item_leupellet");
            var platinum   = LoadItem(ItemsDir + "/Item_Platinum.asset", "platinum");
            var gold       = LoadItem(ItemsDir + "/Item_Gold.asset", "gold");

            recipes[1] = WriteScienceRecipe(registry, 1, packs[1], StationTier.None,          1, 4f,  true,  (wood, 1), (stone, 1));
            recipes[2] = WriteScienceRecipe(registry, 2, packs[2], StationTier.CraftingBench, 1, 5f,  false, (iron, 1), (copper, 1));
            recipes[3] = WriteScienceRecipe(registry, 3, packs[3], StationTier.Assembler,     1, 7f,  false, (steel, 1), (circuit, 1), (copper, 1));
            recipes[4] = WriteScienceRecipe(registry, 4, packs[4], StationTier.Assembler,     2, 10f, false, (plastic, 2), (steelPlate, 2), (circuit, 1));
            recipes[5] = WriteScienceRecipe(registry, 5, packs[5], StationTier.Assembler,     2, 14f, false, (advCircuit, 1), (brass, 2), (silicon, 2), (graphite, 1));
            recipes[6] = WriteScienceRecipe(registry, 6, packs[6], StationTier.Assembler,     2, 18f, false, (leuPellet, 1), (advCircuit, 2), (platinum, 2), (gold, 2));

            return packs;
        }

        private static RecipeDefinition WriteScienceRecipe(
            RecipeRegistry registry, int tier, ScienceItem pack, StationTier station,
            int outCount, float seconds, bool unlockedByDefault,
            params (ItemDefinition item, int count)[] ingredients)
        {
            string path = $"{RecipesDir}/Recipe_ScienceT{tier}.asset";
            var rec = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
            if (rec == null)
            {
                rec = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(rec, path);
            }
            rec.displayName = outCount > 1 ? $"{pack.displayName} x{outCount}" : pack.displayName;
            rec.outputItem = pack;
            rec.outputCount = outCount;
            rec.requiredStation = station;
            rec.craftSeconds = seconds;
            rec.unlockedByDefault = unlockedByDefault;

            var list = new List<RecipeIngredient>();
            foreach (var (it, cnt) in ingredients)
                if (it != null && cnt > 0) list.Add(new RecipeIngredient { item = it, count = cnt });
            if (list.Count > 0) rec.inputs = list.ToArray();

            EditorUtility.SetDirty(rec);
            if (!registry.recipes.Contains(rec))
            {
                registry.recipes.Add(rec);
                EditorUtility.SetDirty(registry);
            }
            return rec;
        }

        private static void UpgradeResearchLabPrefab()
        {
            var labPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/ResearchLab.prefab");
            if (labPrefab == null) return;
            var lab = labPrefab.GetComponent<ResearchLab>();
            if (lab != null)
            {
                lab.EnsureContainers();
                EditorUtility.SetDirty(labPrefab);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 4 & 5. Retired Research Nodes & Player Upgrades
        // ─────────────────────────────────────────────────────────────────────────

        private static int RemoveRetiredResearchNodes(ResearchTree tree)
        {
            string[] retiredNodeIds = { "up_flight", "up_stam", "res_adv_automation" };
            var retiredSet = new HashSet<string>(retiredNodeIds, StringComparer.OrdinalIgnoreCase);
            var doomedNodes = new HashSet<ResearchNode>();

            foreach (string guid in AssetDatabase.FindAssets("t:ResearchNode", new[] { ResearchDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(path);
                if (node == null) continue;
                if (retiredSet.Contains(node.nodeId ?? string.Empty))
                    doomedNodes.Add(node);
            }

            if (tree.nodes != null)
                tree.nodes.RemoveAll(n => n == null || doomedNodes.Contains(n) || retiredSet.Contains(n.nodeId ?? string.Empty));

            foreach (string guid in AssetDatabase.FindAssets("t:ResearchNode", new[] { ResearchDir }))
            {
                var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(AssetDatabase.GUIDToAssetPath(guid));
                if (node == null || doomedNodes.Contains(node) || node.prerequisites == null) continue;
                int before = node.prerequisites.Length;
                node.prerequisites = node.prerequisites
                    .Where(p => p != null && !doomedNodes.Contains(p) && !retiredSet.Contains(p.nodeId ?? string.Empty))
                    .Distinct()
                    .ToArray();
                if (node.prerequisites.Length != before)
                    EditorUtility.SetDirty(node);
            }

            int removed = 0;
            foreach (var doomed in doomedNodes)
            {
                string p = AssetDatabase.GetAssetPath(doomed);
                if (!string.IsNullOrEmpty(p) && AssetDatabase.DeleteAsset(p))
                    removed++;
            }
            EditorUtility.SetDirty(tree);
            return removed;
        }

        private static void UpdatePlayerUpgradeNodes(ResearchTree tree, ScienceItem[] packs)
        {
            var upInv = FindOrCreateNode("Research_up_inv", "up_inv");
            upInv.displayName = "Exoskeletal Load Harness";
            upInv.description =
                "+75 kg max carry capacity per rank. Pneumatic load-transfer struts and servo-braced hip frames " +
                "route pack mass directly through your boots into the ground.";
            upInv.category = ResearchCategory.PlayerUpgrades;
            upInv.subCategory = ResearchSubCategory.General;
            upInv.tier = 1;
            upInv.column = 2;
            upInv.iconTint = new Color(0.88f, 0.66f, 0.24f);
            upInv.upgradeKind = PlayerUpgradeKind.BonusCarryWeightKg;
            upInv.upgradePerRankAmount = 75f;
            upInv.maxRanks = 6;
            upInv.costScalesWithRank = true;
            upInv.researchSeconds = 20f;
            upInv.unlocksRecipes = Array.Empty<RecipeDefinition>();
            upInv.cost = new[]
            {
                new ResearchNode.ScienceCost { pack = packs[1], count = 6 }
            };
            EditorUtility.SetDirty(upInv);
            if (!tree.nodes.Contains(upInv)) tree.nodes.Add(upInv);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 6. Fix Early-Game Crafting Bench Bootstrap Softlocks
        // ─────────────────────────────────────────────────────────────────────────

        private static int FixBootstrapCraftingStations()
        {
            int fixedCount = 0;
            string[] benchRecipeNames =
            {
                "Recipe_PickIron",
                "Recipe_AxeIron",
                "Recipe_IronSword",
                "Recipe_PickSteel",
                "Recipe_IronPlate",
                "Recipe_CopperPlate",
                "Recipe_SteelPlate",
                "Recipe_IronGear",
                "Recipe_Circuit",
                "Recipe_CopperWire",
                "Recipe_PortableBattery",
                "Recipe_ProspectingScanner",
            };

            foreach (string rName in benchRecipeNames)
            {
                var rec = FindCanonicalRecipe(rName);
                if (rec == null) continue;
                bool dirty = false;
                if (rec.requiredStation != StationTier.CraftingBench)
                {
                    rec.requiredStation = StationTier.CraftingBench;
                    dirty = true;
                }
                // Ensure Iron Sword can be forged at Smelting tier from Iron Ingot + Plank (matching Iron Pickaxe / Axe)
                if (rName == "Recipe_IronSword")
                {
                    var ironIngot = LoadItem(ItemsDir + "/Item_IronIngot.asset", "iron_ingot");
                    var plank = LoadItem(ItemsDir + "/Item_WoodenPlank.asset", "wooden_plank");
                    if (ironIngot != null && plank != null)
                    {
                        rec.inputs = new[]
                        {
                            new RecipeIngredient { item = ironIngot, count = 3 },
                            new RecipeIngredient { item = plank, count = 2 },
                        };
                        dirty = true;
                    }
                }
                if (dirty)
                {
                    EditorUtility.SetDirty(rec);
                    fixedCount++;
                }
            }
            return fixedCount;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 7. Author Missing Nodes & Register All Valid Nodes in ResearchTree
        // ─────────────────────────────────────────────────────────────────────────

        private static int EnsureAndRegisterAllResearchNodes(
            ResearchTree tree,
            RecipeRegistry registry,
            ScienceItem[] packs,
            RecipeDefinition[] packRecipes,
            RecipeDefinition canisterRecipe)
        {
            var nodeIndex = BuildNodeIndex();

            ResearchNode Get(string id) => nodeIndex.TryGetValue(id, out var n) ? n : null;

            // Dedicated Science Pack II–VI Research Nodes
            var nSci2 = EnsureEnvironmentNode(nodeIndex, "Research_res_science_pack_2", "res_science_pack_2",
                "Automation Science (Pack II)",
                "Standardize smelted iron-and-copper reagent flasks for electromechanical, fluid, and early manufacturing research.",
                ResearchSubCategory.General, tier: 2, col: 0, seconds: 15f,
                ScienceItem.GetTierColor(2),
                prereqs: new[] { Get("res_smelting") },
                unlocks: new[] { packRecipes[2] });

            var nSci3 = EnsureEnvironmentNode(nodeIndex, "Research_res_science_pack_3", "res_science_pack_3",
                "Industrial Science (Pack III)",
                "Combine structural steel, electronic circuits, and refined copper into blue industrial research cylinders.",
                ResearchSubCategory.General, tier: 3, col: 0, seconds: 40f,
                ScienceItem.GetTierColor(3),
                prereqs: new[] { nSci2, Get("res_adv_manufacturing"), Get("res_steel_alloy"), Get("res_electronics") },
                unlocks: new[] { packRecipes[3] });

            var nSci4 = EnsureEnvironmentNode(nodeIndex, "Research_res_science_pack_4", "res_science_pack_4",
                "Chemical & Polymer Science (Pack IV)",
                "Seal high-purity polymer catalysts in steel containment vials to unlock advanced petrochemicals, telemetry, and mass storage.",
                ResearchSubCategory.General, tier: 4, col: 0, seconds: 65f,
                ScienceItem.GetTierColor(4),
                prereqs: new[] { nSci3, Get("res_plastics"), Get("res_factory_automation") },
                unlocks: new[] { packRecipes[4] });

            var nSci5 = EnsureEnvironmentNode(nodeIndex, "Research_res_science_pack_5", "res_science_pack_5",
                "High-Energy Production Science (Pack V)",
                "Machine brass-and-graphite isotope canisters around silicon and advanced circuits to fund nuclear, orbital, and foundry engineering.",
                ResearchSubCategory.General, tier: 5, col: 0, seconds: 95f,
                ScienceItem.GetTierColor(5),
                prereqs: new[] { nSci4, Get("res_adv_electronics"), Get("res_metallurgical_foundry") },
                unlocks: new[] { packRecipes[5] });

            var nSci6 = EnsureEnvironmentNode(nodeIndex, "Research_res_science_pack_6", "res_science_pack_6",
                "Astral & Singularity Science (Pack VI)",
                "Stabilize low-enriched uranium inside a platinum-and-gold magnetic core to unlock deep-space stations, warp gates, and singularity harvesting.",
                ResearchSubCategory.General, tier: 7, col: 0, seconds: 140f,
                ScienceItem.GetTierColor(6),
                prereqs: new[] { nSci5, Get("orbital_science"), Get("res_nuclear_fission") },
                unlocks: new[] { packRecipes[6] });

            // Storage Auto-Crafting (activates runtime IsUnlocked("res_storage_crafting") in GameUIController)
            var nStorageCrafting = EnsureEnvironmentNode(nodeIndex, "Research_res_storage_crafting", "res_storage_crafting",
                "Network Auto-Crafting",
                "Encode crafting patterns onto digital cards and dispatch on-demand jobs across your storage network via Crafting and Pattern Terminals.",
                ResearchSubCategory.Storage, tier: 4, col: 6, seconds: 90f,
                new Color(0.38f, 0.78f, 0.96f),
                prereqs: new[] { Get("res_logistics_network"), Get("res_factory_automation") },
                unlocks: FindRecipes("Recipe_CraftingTerminal", "Recipe_PatternTerminal", "Recipe_BlankPattern", "Recipe_CraftingCard"));

            // Wireless Build-From-Network (activates runtime IsUnlocked("res_build_from_network") in BuildSystem & GridBuilder)
            EnsureEnvironmentNode(nodeIndex, "Research_res_build_from_network", "res_build_from_network",
                "Network Construction Link",
                "Equip the Wireless Handheld Terminal and draw building materials straight from in-range Wireless Storage Terminals while constructing.",
                ResearchSubCategory.Storage, tier: 5, col: 6, seconds: 120f,
                new Color(0.62f, 0.44f, 0.96f),
                prereqs: new[] { Get("res_wireless_access"), nStorageCrafting },
                unlocks: FindRecipes("Recipe_WirelessHandheldTerminal"));

            // Railways & Automated Signalling
            var nRailways = EnsureEnvironmentNode(nodeIndex, "Research_res_railways", "res_railways",
                "Steam Railways & Trackwork",
                "Lay heavy steel rail tracks, switches, buffers, and stations to haul bulk freight behind steam locomotives and coupled rail trucks.",
                ResearchSubCategory.Logistics, tier: 3, col: 7, seconds: 65f,
                new Color(0.76f, 0.56f, 0.34f),
                prereqs: new[] { Get("res_steel_alloy") },
                unlocks: FindRecipes("Recipe_RailTrack", "Recipe_RailSwitch", "Recipe_RailBuffer",
                    "Recipe_RailStation", "Recipe_SteamEngine", "Recipe_RailTruck", "Recipe_RailCoupler"));

            EnsureEnvironmentNode(nodeIndex, "Research_res_rail_signalling", "res_rail_signalling",
                "Automated Rail Signalling",
                "Coordinate multi-train networks with block signals, automated rail layers, timetable schedules, departure boards, and Nixie readouts.",
                ResearchSubCategory.Logistics, tier: 4, col: 7, seconds: 95f,
                new Color(0.92f, 0.68f, 0.28f),
                prereqs: new[] { nRailways, Get("res_electronics") },
                unlocks: FindRecipes("Recipe_RailSignal", "Recipe_TrainSchedule", "Recipe_RailLayer",
                    "Recipe_HangingDepartureBoard", "Recipe_NixieReadout", "Recipe_BrassDisplayScreen"));

            // Connect Maritime branch cleanly into Shipbuilding (T3 -> T4..T8) instead of floating at T1
            RetierMaritimeBranch(nodeIndex, Get("res_shipbuilding"), Get("res_steel_alloy"), Get("res_adv_electronics"));

            // Ensure Adv Electronics depends on Plastics so Advanced Circuits cannot unlock before Plastic Bars
            var nAdvElec = Get("res_adv_electronics");
            var nPlastics = Get("res_plastics");
            if (nAdvElec != null && nPlastics != null)
                AppendPrerequisite(nAdvElec, nPlastics);

            // Register EVERY valid ResearchNode asset under Research/ into ResearchTree.asset
            int newlyRegistered = 0;
            foreach (var kv in nodeIndex.OrderBy(k => k.Value.tier).ThenBy(k => k.Value.column).ThenBy(k => k.Key))
            {
                var node = kv.Value;
                if (node == null) continue;
                if (!tree.nodes.Contains(node))
                {
                    tree.nodes.Add(node);
                    newlyRegistered++;
                }
            }
            EditorUtility.SetDirty(tree);
            return newlyRegistered;
        }

        private static void RetierMaritimeBranch(
            Dictionary<string, ResearchNode> nodeIndex,
            ResearchNode shipbuilding,
            ResearchNode steelAlloy,
            ResearchNode advElectronics)
        {
            if (nodeIndex.TryGetValue("res_maritime_hull", out var hull) && hull != null)
            {
                hull.tier = 4;
                if (shipbuilding != null) AppendPrerequisite(hull, shipbuilding);
                EditorUtility.SetDirty(hull);
            }
            if (nodeIndex.TryGetValue("res_maritime_hydromech", out var hydro) && hydro != null)
            {
                hydro.tier = 4;
                if (shipbuilding != null) AppendPrerequisite(hydro, shipbuilding);
                EditorUtility.SetDirty(hydro);
            }
            if (nodeIndex.TryGetValue("res_maritime_mechanical", out var mech) && mech != null)
            {
                mech.tier = 5;
                if (steelAlloy != null) AppendPrerequisite(mech, steelAlloy);
                EditorUtility.SetDirty(mech);
            }
            if (nodeIndex.TryGetValue("res_maritime_combustion", out var comb) && comb != null)
            {
                comb.tier = 5;
                EditorUtility.SetDirty(comb);
            }
            if (nodeIndex.TryGetValue("res_maritime_heavy", out var heavy) && heavy != null)
            {
                heavy.tier = 6;
                EditorUtility.SetDirty(heavy);
            }
            if (nodeIndex.TryGetValue("res_maritime_advanced", out var adv) && adv != null)
            {
                adv.tier = 7;
                if (advElectronics != null) AppendPrerequisite(adv, advElectronics);
                EditorUtility.SetDirty(adv);
            }
            if (nodeIndex.TryGetValue("res_maritime_turbo", out var turbo) && turbo != null)
            {
                turbo.tier = 8;
                EditorUtility.SetDirty(turbo);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 8. Wire All Unclaimed / Ungated Progression Recipes
        // ─────────────────────────────────────────────────────────────────────────

        private static int WireAllProgressionRecipes(ResearchTree tree, RecipeRegistry registry)
        {
            var nodeById = new Dictionary<string, ResearchNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in tree.nodes)
                if (n != null && !string.IsNullOrWhiteSpace(n.nodeId))
                    nodeById[n.nodeId] = n;

            var assignments = new (string nodeId, string[] recipeNames)[]
            {
                ("res_stone_working", new[]
                {
                    "Recipe_PickStone", "Recipe_StoneSword", "Recipe_LevelingTool"
                }),
                ("res_smelting", new[]
                {
                    "Recipe_Furnace", "Recipe_PickIron", "Recipe_AxeIron",
                    "Recipe_IronSword", "Recipe_IronChest", "Recipe_ProspectingScanner"
                }),
                ("res_electricity", new[]
                {
                    "Recipe_Generator", "Recipe_CopperLVWire", "Recipe_IronLVWire",
                    "Recipe_Battery", "Recipe_PortableBattery"
                }),
                ("res_fluid_handling", new[]
                {
                    "Recipe_LiquidCanister", "Recipe_PipeSolid", "Recipe_TankSolid", "Recipe_WaterPump"
                }),
                ("res_glassworking", new[]
                {
                    "Recipe_PipeGlass", "Recipe_TankGlass"
                }),
                ("res_farming", new[]
                {
                    "Recipe_Hoe", "Recipe_TilledSoil", "Recipe_Sprinkler", "Recipe_Harvester",
                    "Recipe_SeedPlanter", "Recipe_LivestockPen", "Recipe_WaterTower",
                    "Cook_Bread", "Cook_Stew", "Cook_RoastPotato", "Cook_CornBread",
                    "Cook_BerryPie", "Cook_PumpkinSoup"
                }),
                ("res_shipbuilding", new[]
                {
                    "Recipe_GThrustSmall", "Recipe_GAtmoThruster_Large", "Recipe_GGridWindow"
                }),
                ("res_grid_utilities", new[]
                {
                    "Recipe_AirVent_Large", "Recipe_AirVent_Small",
                    "Recipe_VentilationUnit_Large", "Recipe_VentilationUnit_Small",
                    "Recipe_GasVent_Large", "Recipe_GasVent_Small",
                    "Recipe_ExhaustScrubber_Large", "Recipe_ExhaustScrubber_Small",
                    "Recipe_Heatshield_Large", "Recipe_Heatshield_Small",
                    "Recipe_RouteRecorder_Large", "Recipe_RouteRecorder_Small",
                    "Recipe_WelderTool", "Recipe_StationaryDockingPort"
                }),
                ("res_item_logistics", new[]
                {
                    "Recipe_ItemPipe", "Recipe_ItemPipe_Glass", "Recipe_Wrench",
                    "Recipe_PowerBusbar", "Recipe_ProviderChest", "Recipe_RequesterChest", "Recipe_BufferChest"
                }),
                ("res_logistics_network", new[]
                {
                    "Recipe_CPU_1", "Recipe_PSU_500", "Recipe_RAM_4", "Recipe_DiskManipulator",
                    "Recipe_StorageDrawer", "Recipe_StorageDrawerController", "Recipe_StorageItemDisplay",
                    "Recipe_DrawerUpgradeStack1x", "Recipe_DrawerUpgradeStack2x",
                    "Recipe_DataPipe", "Recipe_ExternalStorage"
                }),
                ("res_mass_storage", new[]
                {
                    "Recipe_CPU_2", "Recipe_PSU_2K", "Recipe_RAM_16", "Recipe_Powerstation",
                    "Recipe_DrawerUpgradeVoid", "Recipe_DrawerUpgradeStack4x",
                    "Recipe_DrawerUpgradeStack8x", "Recipe_SecurityBlock"
                }),
                ("res_crystalline_storage", new[]
                {
                    "Recipe_CPU_4", "Recipe_DrawerUpgradeStack16x"
                }),
                ("res_wireless_access", new[]
                {
                    "Recipe_WirelessStorageTerminal", "Recipe_TransmissionTower"
                }),
                ("res_factory_automation", new[]
                {
                    "Recipe_DronePort"
                }),
                ("res_oil_extraction", new[]
                {
                    "Recipe_TankSolid", "Recipe_PipeSolid"
                }),
                ("res_gas_processing", new[]
                {
                    "Recipe_GasPipe_Glass", "Recipe_SpaceHelmet", "Recipe_OxygenTank",
                    "Recipe_Cryobed", "Recipe_Biofarm"
                }),
                ("orbital_telemetry", new[]
                {
                    "Recipe_StaticSeasonMonitor", "Recipe_GSeasonMonitor"
                }),
                ("orbital_science", new[]
                {
                    "Recipe_SatelliteSensorArray", "Recipe_SatelliteWeatherRadar"
                }),
                ("orbital_construction", new[]
                {
                    "Recipe_StationLifeSupport"
                }),
                ("res_maritime_hydromech", new[]
                {
                    "Recipe_MMarineWaterPump"
                }),
                ("res_maritime_combustion", new[]
                {
                    "Recipe_MShipConsole"
                }),
                ("res_maritime_heavy", new[]
                {
                    "Recipe_MTurboLarge", "Recipe_MTurboSmall"
                }),
                ("res_nuclear_fission", new[]
                {
                    "Recipe_ControlRod"
                }),
                ("res_quarrying", new[]
                {
                    "Recipe_DeepSurveyScanner", "Recipe_DeepCoreExtractor"
                }),
            };

            // Keep TankSolid/PipeSolid SOLELY on res_fluid_handling so they aren't double-claimed by T3 Oil Logistics;
            // instead, give res_oil_extraction & res_plastics clean description clarity while keeping single-owner recipe locks.
            int wired = 0;
            foreach (var (nodeId, rNames) in assignments)
            {
                if (nodeId == "res_oil_extraction") continue;
                if (!nodeById.TryGetValue(nodeId, out var node) || node == null) continue;
                var list = new List<RecipeDefinition>(node.unlocksRecipes ?? Array.Empty<RecipeDefinition>());
                bool nodeDirty = false;

                foreach (string rName in rNames)
                {
                    var rec = FindCanonicalRecipe(rName);
                    if (rec == null) continue;

                    if (!registry.recipes.Contains(rec))
                    {
                        registry.recipes.Add(rec);
                        EditorUtility.SetDirty(registry);
                    }
                    if (!list.Contains(rec))
                    {
                        list.Add(rec);
                        nodeDirty = true;
                        wired++;
                    }
                }

                if (nodeDirty)
                {
                    node.unlocksRecipes = list.Where(r => r != null).Distinct().ToArray();
                    EditorUtility.SetDirty(node);
                }
            }

            // Enforce unlockedByDefault = false on EVERY recipe claimed by ANY node in ResearchTree
            // (except Science Pack I which is never claimed and stays unlockedByDefault = true).
            foreach (var node in tree.nodes)
            {
                if (node == null || node.unlocksRecipes == null) continue;
                foreach (var rec in node.unlocksRecipes)
                {
                    if (rec == null) continue;
                    if (rec.outputItem is ScienceItem sci && sci.EffectiveTier <= 1) continue;
                    if (rec.unlockedByDefault)
                    {
                        rec.unlockedByDefault = false;
                        EditorUtility.SetDirty(rec);
                    }
                    if (!registry.recipes.Contains(rec))
                    {
                        registry.recipes.Add(rec);
                        EditorUtility.SetDirty(registry);
                    }
                }
            }

            return wired;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 9. Rebalance T1–T8 Science Costs Across Packs I–VI + DAG Bootstrap Guard
        // ─────────────────────────────────────────────────────────────────────────

        private static int RebalanceAllNodeScienceCosts(ResearchTree tree, ScienceItem[] packs)
        {
            var nodeById = new Dictionary<string, ResearchNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in tree.nodes)
                if (n != null && !string.IsNullOrWhiteSpace(n.nodeId))
                    nodeById[n.nodeId] = n;

            // Compute transitive ancestor sets for each res_science_pack_K so no ancestor of Pack K
            // can EVER require Pack >= K (guarantees zero circular science dependencies).
            var maxAllowedPackByNode = new Dictionary<ResearchNode, int>();
            foreach (var n in tree.nodes)
            {
                if (n == null) continue;
                maxAllowedPackByNode[n] = DefaultMaxPackForTier(n);
            }

            for (int k = ScienceItem.MaxTier; k >= 2; k--)
            {
                if (!nodeById.TryGetValue($"res_science_pack_{k}", out var sciNode) || sciNode == null)
                    continue;
                int cap = k - 1;
                var stack = new Stack<ResearchNode>();
                var seen = new HashSet<ResearchNode>();
                stack.Push(sciNode);
                seen.Add(sciNode);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (maxAllowedPackByNode.TryGetValue(cur, out int curCap) && cap < curCap)
                        maxAllowedPackByNode[cur] = cap;
                    if (cur.prerequisites == null) continue;
                    foreach (var pre in cur.prerequisites)
                    {
                        if (pre != null && seen.Add(pre))
                            stack.Push(pre);
                    }
                }
            }

            int updated = 0;
            foreach (var node in tree.nodes)
            {
                if (node == null) continue;
                int maxPack = maxAllowedPackByNode.TryGetValue(node, out int cap) ? cap : 1;
                var newCosts = BuildBalancedCostForNode(node, packs, maxPack);
                if (!CostsEqual(node.cost, newCosts))
                {
                    node.cost = newCosts;
                    EditorUtility.SetDirty(node);
                    updated++;
                }
                if (node.researchSeconds <= 0f && node.nodeId != "res_stone_working" && node.nodeId != "res_smelting")
                {
                    node.researchSeconds = Mathf.Clamp(node.tier * 20f, 15f, 240f);
                    EditorUtility.SetDirty(node);
                }
            }
            return updated;
        }

        private static int DefaultMaxPackForTier(ResearchNode node)
        {
            if (node.category == ResearchCategory.PlayerUpgrades)
                return node.tier <= 1 ? 1 : 2;

            return node.tier switch
            {
                <= 1 => 1,
                2    => 2,
                3    => 3,
                4    => 4,
                5    => 5,
                6    => 5,
                _    => 6, // T7 & T8 use Endgame Cyan Pack VI (except ancestors of res_science_pack_6)
            };
        }

        private static ResearchNode.ScienceCost[] BuildBalancedCostForNode(
            ResearchNode node, ScienceItem[] packs, int maxPack)
        {
            string id = node.nodeId ?? string.Empty;

            // Explicit milestone costs for core bootstrap & Science Pack unlock nodes
            if (id.Equals("res_stone_working", StringComparison.OrdinalIgnoreCase))
                return MakeCosts(packs, (1, 5));
            if (id.Equals("res_smelting", StringComparison.OrdinalIgnoreCase))
                return MakeCosts(packs, (1, 10));
            if (id.Equals("res_science_pack_2", StringComparison.OrdinalIgnoreCase))
                return MakeCosts(packs, (1, 12));
            if (id.Equals("res_science_pack_3", StringComparison.OrdinalIgnoreCase))
                return MakeCosts(packs, (1, 24), (2, 18));
            if (id.Equals("res_science_pack_4", StringComparison.OrdinalIgnoreCase))
                return MakeCosts(packs, (2, 30), (3, 20));
            if (id.Equals("res_science_pack_5", StringComparison.OrdinalIgnoreCase))
                return MakeCosts(packs, (3, 35), (4, 25));
            if (id.Equals("res_science_pack_6", StringComparison.OrdinalIgnoreCase))
                return MakeCosts(packs, (4, 45), (5, 35));

            if (node.category == ResearchCategory.PlayerUpgrades)
            {
                if (maxPack <= 1) return MakeCosts(packs, (1, id == "up_inv" ? 6 : 4));
                return MakeCosts(packs, (1, 6), (2, 4));
            }

            // Tier-proportional, spectrum-balanced costs capped strictly at maxPack
            return maxPack switch
            {
                1 => MakeCosts(packs, (1, Mathf.Clamp(node.tier * 8, 6, 20))),
                2 => node.tier <= 2
                    ? MakeCosts(packs, (1, 12), (2, 8))
                    : MakeCosts(packs, (1, 20), (2, 15)),
                3 => node.tier <= 3
                    ? MakeCosts(packs, (1, 20), (2, 16), (3, 10))
                    : MakeCosts(packs, (2, 24), (3, 16)),
                4 => node.tier <= 4
                    ? MakeCosts(packs, (2, 24), (3, 18), (4, 12))
                    : MakeCosts(packs, (3, 28), (4, 20)),
                5 => node.tier <= 5
                    ? MakeCosts(packs, (3, 30), (4, 22), (5, 14))
                    : MakeCosts(packs, (4, 36), (5, 26)),
                _ => node.tier <= 7
                    ? MakeCosts(packs, (4, 40), (5, 30), (6, 20))
                    : MakeCosts(packs, (4, 50), (5, 45), (6, 35)),
            };
        }

        private static ResearchNode.ScienceCost[] MakeCosts(
            ScienceItem[] packs, params (int tier, int count)[] entries)
        {
            var list = new List<ResearchNode.ScienceCost>(entries.Length);
            foreach (var (t, c) in entries)
            {
                if (t >= 1 && t <= ScienceItem.MaxTier && packs[t] != null && c > 0)
                    list.Add(new ResearchNode.ScienceCost { pack = packs[t], count = c });
            }
            return list.ToArray();
        }

        private static bool CostsEqual(ResearchNode.ScienceCost[] a, ResearchNode.ScienceCost[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].pack != b[i].pack || a[i].count != b[i].count) return false;
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 10. Registry & Tree Hygiene + Lookup Helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static void CleanRegistryAndTree(RecipeRegistry registry, ResearchTree tree)
        {
            if (registry.recipes != null)
            {
                var seen = new HashSet<RecipeDefinition>();
                var cleaned = new List<RecipeDefinition>(registry.recipes.Count);
                foreach (var r in registry.recipes)
                {
                    if (r == null || r.outputItem == null) continue;
                    if (seen.Add(r)) cleaned.Add(r);
                }
                if (cleaned.Count != registry.recipes.Count)
                {
                    registry.recipes = cleaned;
                    EditorUtility.SetDirty(registry);
                }
            }

            if (tree.nodes != null)
            {
                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var cleanedNodes = new List<ResearchNode>(tree.nodes.Count);
                foreach (var n in tree.nodes)
                {
                    if (n == null || string.IsNullOrWhiteSpace(n.nodeId)) continue;
                    if (seenIds.Add(n.nodeId)) cleanedNodes.Add(n);
                }
                tree.nodes = cleanedNodes;
                EditorUtility.SetDirty(tree);
            }
        }

        private static Dictionary<string, ResearchNode> BuildNodeIndex()
        {
            var map = new Dictionary<string, ResearchNode>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:ResearchNode", new[] { ResearchDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(path);
                if (node == null || string.IsNullOrWhiteSpace(node.nodeId)) continue;
                if (!map.ContainsKey(node.nodeId))
                    map[node.nodeId] = node;
            }
            return map;
        }

        private static ResearchNode FindOrCreateNode(string assetName, string nodeId)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:ResearchNode", new[] { ResearchDir }))
            {
                var existing = AssetDatabase.LoadAssetAtPath<ResearchNode>(AssetDatabase.GUIDToAssetPath(guid));
                if (existing != null && string.Equals(existing.nodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                    return existing;
            }
            string path = $"{NodesDir}/{assetName}.asset";
            var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(path);
            if (node == null)
            {
                node = ScriptableObject.CreateInstance<ResearchNode>();
                node.nodeId = nodeId;
                AssetDatabase.CreateAsset(node, path);
            }
            return node;
        }

        private static ResearchNode EnsureEnvironmentNode(
            Dictionary<string, ResearchNode> nodeIndex,
            string assetName,
            string nodeId,
            string displayName,
            string description,
            ResearchSubCategory subCategory,
            int tier,
            int col,
            float seconds,
            Color tint,
            ResearchNode[] prereqs,
            RecipeDefinition[] unlocks)
        {
            var node = FindOrCreateNode(assetName, nodeId);
            node.nodeId = nodeId;
            node.displayName = displayName;
            node.description = description;
            node.category = ResearchCategory.Environment;
            node.subCategory = subCategory;
            node.tier = tier;
            node.column = col;
            node.researchSeconds = seconds;
            node.iconTint = tint;
            node.upgradeKind = PlayerUpgradeKind.None;
            node.maxRanks = 1;
            node.prerequisites = (prereqs ?? Array.Empty<ResearchNode>()).Where(p => p != null && p != node).Distinct().ToArray();

            var mergedUnlocks = new List<RecipeDefinition>(node.unlocksRecipes ?? Array.Empty<RecipeDefinition>());
            if (unlocks != null)
            {
                foreach (var r in unlocks)
                    if (r != null && !mergedUnlocks.Contains(r)) mergedUnlocks.Add(r);
            }
            node.unlocksRecipes = mergedUnlocks.Where(r => r != null).Distinct().ToArray();

            EditorUtility.SetDirty(node);
            nodeIndex[nodeId] = node;
            return node;
        }

        private static void AppendPrerequisite(ResearchNode node, ResearchNode prereq)
        {
            if (node == null || prereq == null || node == prereq) return;
            var list = new List<ResearchNode>(node.prerequisites ?? Array.Empty<ResearchNode>());
            if (!list.Contains(prereq))
            {
                list.Add(prereq);
                node.prerequisites = list.Where(p => p != null).Distinct().ToArray();
                EditorUtility.SetDirty(node);
            }
        }

        private static RecipeDefinition[] FindRecipes(params string[] names)
        {
            var list = new List<RecipeDefinition>();
            foreach (string nm in names)
            {
                var r = FindCanonicalRecipe(nm);
                if (r != null && !list.Contains(r)) list.Add(r);
            }
            return list.ToArray();
        }

        private static RecipeDefinition FindCanonicalRecipe(string assetName)
        {
            string direct = $"{RecipesDir}/{assetName}.asset";
            var rec = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(direct);
            if (rec != null) return rec;

            foreach (string guid in AssetDatabase.FindAssets($"{assetName} t:RecipeDefinition", new[] { Root }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(p), assetName, StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<RecipeDefinition>(p);
            }
            return null;
        }

        private static ItemDefinition LoadItem(string preferredPath, string fallbackItemId)
        {
            var direct = AssetDatabase.LoadAssetAtPath<ItemDefinition>(preferredPath);
            if (direct != null) return direct;

            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { Root }))
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item != null && string.Equals(item.itemId, fallbackItemId, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string name = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
