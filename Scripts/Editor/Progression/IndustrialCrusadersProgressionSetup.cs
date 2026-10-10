#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Combat;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Non-destructive progression pass exposed from the Voxel Engine Setup Wizard.
    /// It creates missing sword/research assets, repairs empty links, and moves only
    /// the explicitly listed recipe unlock links to their intended progression nodes.
    /// Existing recipe costs, item stats, and research balance values are preserved.
    /// </summary>
    public static class IndustrialCrusadersProgressionSetup
    {
        private const string AssetRoot = "Assets/VoxelEngineAssets";
        private const string ResearchRoot = AssetRoot + "/Research";
        private const string NodeFolder = ResearchRoot + "/Nodes";
        private const string RecipeFolder = AssetRoot + "/Recipes";
        private const string CombatItemFolder = AssetRoot + "/Combat/Items";
        private const string ResearchTreePath = ResearchRoot + "/ResearchTree.asset";
        private const string RecipeRegistryPath = AssetRoot + "/RecipeRegistry.asset";

        private sealed class RepairCounts
        {
            public int assetsCreated;
            public int linksRepaired;
            public int recipesGated;
            public int unlockLinksMoved;
            public int warnings;
            public string copperAudit = "Not checked";
        }

        public static void Run()
        {
            EnsureFolder(AssetRoot + "/Combat");
            EnsureFolder(CombatItemFolder);
            EnsureFolder(RecipeFolder);
            EnsureFolder(ResearchRoot);
            EnsureFolder(NodeFolder);

            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(ResearchTreePath);
            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(RecipeRegistryPath);
            if (tree == null || registry == null)
            {
                EditorUtility.DisplayDialog("IndustrialCrusaders Progression",
                    "The research tree or recipe registry is missing. Run the base crafting, research, and factory content entries in Tools > Voxel Engine > Voxel Engine Setup first. No assets were changed.",
                    "OK");
                return;
            }

            var counts = new RepairCounts();
            if (tree.nodes == null)
            {
                tree.nodes = new List<ResearchNode>();
                EditorUtility.SetDirty(tree);
                counts.linksRepaired++;
            }
            if (registry.recipes == null)
            {
                registry.recipes = new List<RecipeDefinition>();
                EditorUtility.SetDirty(registry);
                counts.linksRepaired++;
            }
            RepairCopperSmeltingLinks(counts);
            var plank = FindAssetByName<ItemDefinition>("Item_WoodenPlank");
            var stone = FindAssetByName<ItemDefinition>("Item_Stone");
            var scienceT1 = FindAssetByName<ScienceItem>("Item_ScienceT1");
            var scienceT2 = FindAssetByName<ScienceItem>("Item_ScienceT2");
            var scienceT3 = FindAssetByName<ScienceItem>("Item_ScienceT3");

            if (plank == null || stone == null)
            {
                EditorUtility.DisplayDialog("IndustrialCrusaders Progression",
                    "Wooden Plank or Stone is missing. Run Core & Project Bootstrap > Build base crafting first. No progression assets were changed.",
                    "OK");
                return;
            }

            var woodenSword = EnsureSword(
                "Weapon_WoodenSword", "wooden_sword", "Wooden Sword",
                "A light starter blade made from wooden planks.", new Color(0.62f, 0.43f, 0.25f),
                damage: 18f, range: 2.4f, cooldown: 0.62f, durability: 180, miningTier: 1, counts: counts);
            var stoneSword = EnsureSword(
                "Weapon_StoneSword", "stone_sword", "Stone Sword",
                "A heavier stone blade. Unlocked with Stone Working.", new Color(0.60f, 0.66f, 0.72f),
                damage: 28f, range: 2.6f, cooldown: 0.58f, durability: 320, miningTier: 2, counts: counts);

            RecipeDefinition woodenSwordRecipe = null;
            RecipeDefinition stoneSwordRecipe = null;
            if (woodenSword != null)
            {
                woodenSwordRecipe = EnsureRecipe(
                    "Recipe_WoodenSword", "Wooden Sword", woodenSword,
                    StationTier.None, true,
                    new[] { plank }, new[] { 3 }, registry, counts);
            }
            if (stoneSword != null)
            {
                stoneSwordRecipe = EnsureRecipe(
                    "Recipe_StoneSword", "Stone Sword", stoneSword,
                    StationTier.CraftingBench, true,
                    new[] { stone, plank }, new[] { 4, 2 }, registry, counts);
            }

            var stoneWorking = FindOrCreateNode(tree, "res_stone_working", "Stone Working",
                "Learn to shape stone tools and early equipment.", 1,
                ResearchSubCategory.Production, new Color(0.68f, 0.71f, 0.76f),
                0f, new[] { scienceT1 }, new[] { 5 }, counts);
            if (stoneWorking != null && stoneSwordRecipe != null)
                MoveUnlocksToNode(tree, stoneWorking, new[] { stoneSwordRecipe }, counts);

            var factoryLogistics = FindNode(tree, "res_factory_logistics");
            var advancedManufacturing = FindNode(tree, "res_adv_manufacturing");
            var electricity = FindNode(tree, "res_electricity");
            var steelAlloy = FindNode(tree, "res_steel_alloy");

            if (factoryLogistics == null)
            {
                counts.warnings++;
                Debug.LogWarning("[IndustrialCrusadersProgressionSetup] res_factory_logistics is missing. Run the Factory Foundations setup entry; factory recipe tiers were not modified.");
            }
            else if (scienceT2 == null || scienceT3 == null)
            {
                counts.warnings++;
                Debug.LogWarning("[IndustrialCrusadersProgressionSetup] Science Pack II or III is missing. Factory research nodes were not authored without their research currency.");
            }
            else
            {
                var mk1Node = FindOrCreateNode(tree, "res_factory_automation", "Factory Automation",
                    "Automate production with the first powered assembler tier and faster material handling.", 4,
                    ResearchSubCategory.Logistics, new Color(0.18f, 0.72f, 0.88f),
                    90f, new[] { scienceT2, scienceT3 }, new[] { 15, 5 }, counts);
                var mk2Node = FindOrCreateNode(tree, "res_advanced_automation", "Advanced Automation",
                    "Scale the factory with higher assembler and express conveyor technology.", 5,
                    ResearchSubCategory.Production, new Color(0.30f, 0.62f, 0.94f),
                    120f, new[] { scienceT2, scienceT3 }, new[] { 25, 15 }, counts);
                var precisionNode = FindOrCreateNode(tree, "res_precision_assembly", "Precision Assembly",
                    "Unlock the highest conventional assembler tier for complex industrial production.", 6,
                    ResearchSubCategory.Production, new Color(0.48f, 0.68f, 0.98f),
                    160f, new[] { scienceT2, scienceT3 }, new[] { 35, 25 }, counts);
                var lightingNode = FindOrCreateNode(tree, "res_led_lighting", "Industrial Lighting",
                    "Develop efficient segmented LED lighting for factories and grid structures.", 4,
                    ResearchSubCategory.Power, new Color(0.22f, 0.78f, 0.88f),
                    70f, new[] { scienceT2, scienceT3 }, new[] { 12, 5 }, counts);
                var grinderNode = FindOrCreateNode(tree, "res_grid_reclamation", "Grid Reclamation",
                    "Recover placed ship and vehicle components with a purpose-built grinder tool.", 4,
                    ResearchSubCategory.Production, new Color(0.92f, 0.57f, 0.19f),
                    80f, new[] { scienceT2, scienceT3 }, new[] { 12, 6 }, counts);

                if (mk1Node != null)
                {
                    MergePrerequisites(mk1Node, new[] { factoryLogistics }, counts);
                    MoveRecipeNames(tree, mk1Node, new[]
                    {
                        "Recipe_AssemblerMk1", "Recipe_ConveyorFast",
                        "Recipe_ConveyorSplitterMk2", "Recipe_Crusher"
                    }, registry, counts);
                }
                if (mk2Node != null)
                {
                    var prereqs = new List<ResearchNode>();
                    if (mk1Node != null) prereqs.Add(mk1Node);
                    if (steelAlloy != null) prereqs.Add(steelAlloy);
                    MergePrerequisites(mk2Node, prereqs, counts);
                    MoveRecipeNames(tree, mk2Node, new[]
                    {
                        "Recipe_AssemblerMk2",
                        "Recipe_ConveyorExpress", "Recipe_ConveyorSplitterMk3"
                    }, registry, counts);
                }
                if (precisionNode != null)
                {
                    var prereqs = new List<ResearchNode>();
                    if (mk2Node != null) prereqs.Add(mk2Node);
                    if (steelAlloy != null) prereqs.Add(steelAlloy);
                    MergePrerequisites(precisionNode, prereqs, counts);
                    MoveRecipeNames(tree, precisionNode, new[] { "Recipe_AssemblerMk3" }, registry, counts);
                }
                if (lightingNode != null)
                {
                    var prereqs = new List<ResearchNode> { factoryLogistics };
                    if (electricity != null) prereqs.Add(electricity);
                    MergePrerequisites(lightingNode, prereqs, counts);
                    MoveRecipeNames(tree, lightingNode, new[]
                    {
                        "Recipe_LEDStripFactory", "Recipe_GLEDStrip", "Recipe_LargeGridLEDStrip", "Recipe_LEDStrip"
                    }, registry, counts, "Recipe_LEDStrip");
                }
                if (grinderNode != null)
                {
                    var prereqs = new List<ResearchNode> { factoryLogistics };
                    if (advancedManufacturing != null) prereqs.Add(advancedManufacturing);
                    if (steelAlloy != null) prereqs.Add(steelAlloy);
                    MergePrerequisites(grinderNode, prereqs, counts);
                    MoveRecipeNames(tree, grinderNode, new[] { "Recipe_GrinderTool" }, registry, counts);
                }
            }

            if (woodenSwordRecipe != null)
            {
                // Starter equipment is deliberately available from the first inventory recipe list.
                if (!woodenSwordRecipe.unlockedByDefault)
                {
                    woodenSwordRecipe.unlockedByDefault = true;
                    EditorUtility.SetDirty(woodenSwordRecipe);
                    counts.linksRepaired++;
                }
            }

            if (stoneSwordRecipe != null)
            {
                if (stoneWorking != null)
                {
                    if (stoneSwordRecipe.unlockedByDefault)
                    {
                        stoneSwordRecipe.unlockedByDefault = false;
                        EditorUtility.SetDirty(stoneSwordRecipe);
                        counts.recipesGated++;
                    }
                    MoveUnlocksToNode(tree, stoneWorking, new[] { stoneSwordRecipe }, counts);
                }
                else
                {
                    counts.warnings++;
                    Debug.LogWarning("[IndustrialCrusadersProgressionSetup] Stone Working could not be resolved; the Stone Sword recipe was left available rather than creating an inaccessible recipe.");
                }
            }

            EditorUtility.SetDirty(tree);
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("IndustrialCrusaders Progression",
                "Progression repair complete.\n\n" +
                "Copper chain: " + counts.copperAudit + "\n" +
                "Wooden Sword: available from the start.\n" +
                "Stone Sword: " + (stoneSwordRecipe == null ? "recipe not created (see warnings)" : stoneWorking != null ? "linked to Stone Working" : "left available because Stone Working was unavailable") + "\n" +
                "Assembler Mk.1–3, fast/express conveyors, LED strips, and Grinder: gated behind staged research.\n\n" +
                $"Created assets: {counts.assetsCreated}\n" +
                $"Repaired links: {counts.linksRepaired}\n" +
                $"Recipes gated: {counts.recipesGated}\n" +
                $"Unlock links moved: {counts.unlockLinksMoved}\n" +
                $"Warnings: {counts.warnings}\n\n" +
                "Existing item stats, costs, timings, and populated recipe ingredients were preserved.",
                "OK");
        }

        private static void RepairCopperSmeltingLinks(RepairCounts counts)
        {
            const string orePath = AssetRoot + "/Industrial/Items/Item_CopperOre.asset";
            const string ingotPath = AssetRoot + "/Items/Item_CopperIngot.asset";
            const string recipePath = AssetRoot + "/Recipes/Smelt_Copper.asset";
            const string materialPath = AssetRoot + "/Materials/Mat_Copper.asset";
            const string materialRegistryPath = AssetRoot + "/MaterialRegistry.asset";
            const string furnacePath = AssetRoot + "/StationPrefabs/Furnace.prefab";

            var ore = AssetDatabase.LoadAssetAtPath<ItemDefinition>(orePath);
            var ingot = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ingotPath);
            var recipe = AssetDatabase.LoadAssetAtPath<SmeltingRecipe>(recipePath);
            var material = AssetDatabase.LoadAssetAtPath<VoxelEngine.Materials.VoxelMaterialDefinition>(materialPath);
            var materialRegistry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Materials.MaterialRegistry>(materialRegistryPath);
            int changes = 0;
            bool auditComplete = true;

            if (ore == null || ingot == null || recipe == null)
            {
                counts.warnings++;
                counts.copperAudit = "could not verify (canonical ore, ingot, or recipe asset missing)";
                Debug.LogWarning("[IndustrialCrusadersProgressionSetup] Copper smelting audit could not resolve all canonical assets. No recipe balance values were changed.");
                return;
            }

            bool recipeChanged = false;
            if (recipe.input == null)
            {
                recipe.input = ore;
                recipeChanged = true;
                changes++;
            }
            else if (!ReferenceEquals(recipe.input, ore) && IsLegacyCopperOre(recipe.input))
            {
                recipe.input = ore;
                recipeChanged = true;
                changes++;
            }
            else if (!ItemIdentity.Same(recipe.input, ore))
            {
                counts.warnings++;
                auditComplete = false;
                Debug.LogWarning($"[IndustrialCrusadersProgressionSetup] Preserved the populated copper smelting input '{recipe.input.name}' because it is not the known legacy duplicate. Review '{recipePath}' manually.");
            }

            if (recipe.output == null)
            {
                recipe.output = ingot;
                recipeChanged = true;
                changes++;
            }
            else if (!ItemIdentity.Same(recipe.output, ingot))
            {
                counts.warnings++;
                auditComplete = false;
                Debug.LogWarning($"[IndustrialCrusadersProgressionSetup] Preserved the populated copper smelting output '{recipe.output.name}'. Review '{recipePath}' manually.");
            }
            if (recipeChanged) EditorUtility.SetDirty(recipe);

            if (material != null)
            {
                if (material.dropItem == null)
                {
                    material.dropItem = ore;
                    EditorUtility.SetDirty(material);
                    changes++;
                }
                else if (!ReferenceEquals(material.dropItem, ore) && IsLegacyCopperOre(material.dropItem))
                {
                    material.dropItem = ore;
                    EditorUtility.SetDirty(material);
                    changes++;
                }
                else if (!ItemIdentity.Same(material.dropItem, ore))
                {
                    counts.warnings++;
                    auditComplete = false;
                    Debug.LogWarning($"[IndustrialCrusadersProgressionSetup] Preserved the populated Mat_Copper drop link '{material.dropItem.name}'.");
                }
            }
            else
            {
                counts.warnings++;
                auditComplete = false;
                Debug.LogWarning($"[IndustrialCrusadersProgressionSetup] Copper material definition is missing at '{materialPath}'.");
            }

            if (materialRegistry != null && material != null)
            {
                if (materialRegistry.definitions == null)
                {
                    materialRegistry.definitions = new List<VoxelEngine.Materials.VoxelMaterialDefinition>();
                    EditorUtility.SetDirty(materialRegistry);
                    changes++;
                }
                if (!materialRegistry.definitions.Contains(material))
                {
                    materialRegistry.definitions.Add(material);
                    EditorUtility.SetDirty(materialRegistry);
                    changes++;
                }
            }
            else if (materialRegistry == null)
            {
                counts.warnings++;
                auditComplete = false;
                Debug.LogWarning($"[IndustrialCrusadersProgressionSetup] Material registry is missing at '{materialRegistryPath}'.");
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(furnacePath) != null)
            {
                GameObject furnaceRoot = null;
                bool prefabChanged = false;
                try
                {
                    furnaceRoot = PrefabUtility.LoadPrefabContents(furnacePath);
                    var furnace = furnaceRoot != null ? furnaceRoot.GetComponent<Furnace>() : null;
                    if (furnace == null)
                    {
                        counts.warnings++;
                        auditComplete = false;
                        Debug.LogWarning($"[IndustrialCrusadersProgressionSetup] Furnace prefab at '{furnacePath}' has no Furnace component; it was left untouched.");
                    }
                    else
                    {
                        if (furnace.knownRecipes == null)
                        {
                            furnace.knownRecipes = new List<SmeltingRecipe>();
                            prefabChanged = true;
                        }
                        if (!furnace.knownRecipes.Contains(recipe))
                        {
                            furnace.knownRecipes.Add(recipe);
                            prefabChanged = true;
                            changes++;
                        }
                    }
                    if (prefabChanged)
                    {
                        EditorUtility.SetDirty(furnaceRoot);
                        PrefabUtility.SaveAsPrefabAsset(furnaceRoot, furnacePath);
                    }
                }
                catch (Exception ex)
                {
                    counts.warnings++;
                    auditComplete = false;
                    Debug.LogWarning($"[IndustrialCrusadersProgressionSetup] Could not audit the Furnace prefab: {ex.Message}");
                }
                finally
                {
                    if (furnaceRoot != null) PrefabUtility.UnloadPrefabContents(furnaceRoot);
                }
            }
            else
            {
                counts.warnings++;
                auditComplete = false;
                Debug.LogWarning($"[IndustrialCrusadersProgressionSetup] Furnace prefab is missing at '{furnacePath}'.");
            }

            AssetDatabase.SaveAssets();
            counts.linksRepaired += changes;
            counts.copperAudit = !auditComplete
                ? changes > 0
                    ? $"repaired {changes} link(s); additional mismatches need manual review"
                    : "incomplete: populated links were preserved; inspect the warnings"
                : changes == 0
                    ? "verified: canonical ore, ingot output, material drop, and Furnace recipe link are intact"
                    : $"repaired {changes} missing or legacy copper link(s)";
            Debug.Log("[IndustrialCrusadersProgressionSetup] Copper smelting audit: " + counts.copperAudit + ".");
        }

        private static bool IsLegacyCopperOre(ItemDefinition item)
        {
            if (item == null) return false;
            if (string.Equals(item.itemId, "copper", StringComparison.OrdinalIgnoreCase)) return true;
            string path = AssetDatabase.GetAssetPath(item)?.Replace("\\", "/");
            return string.Equals(path, AssetRoot + "/Items/Item_Copper.asset", StringComparison.OrdinalIgnoreCase);
        }

        private static WeaponItem EnsureSword(string assetName, string itemId, string displayName,
            string description, Color tint, float damage, float range, float cooldown,
            int durability, int miningTier, RepairCounts counts)
        {
            string path = $"{CombatItemFolder}/{assetName}.asset";
            var sword = AssetDatabase.LoadAssetAtPath<WeaponItem>(path);
            bool created = false;
            if (sword == null)
            {
                var other = AssetDatabase.LoadMainAssetAtPath(path);
                if (other != null)
                {
                    counts.warnings++;
                    Debug.LogError($"[IndustrialCrusadersProgressionSetup] Preserved '{path}' because it is not a WeaponItem.");
                    return null;
                }
                sword = ScriptableObject.CreateInstance<WeaponItem>();
                AssetDatabase.CreateAsset(sword, path);
                created = true;
                counts.assetsCreated++;
            }

            bool changed = false;
            if (created)
            {
                sword.itemId = itemId;
                sword.displayName = displayName;
                sword.description = description;
                sword.iconTint = tint;
                sword.maxStack = 1;
                sword.category = "Weapons";
                sword.toolType = ToolType.Sword;
                sword.attackMode = WeaponItem.AttackMode.Melee;
                sword.damage = damage;
                sword.range = range;
                sword.attackCooldown = cooldown;
                sword.damageType = VoxelEngine.Combat.DamageType.Melee;
                sword.miningTier = miningTier;
                sword.maxDurability = durability;
                sword.strength = damage;
                sword.brushRadius = 1f;
                sword.fireRate = 1f / Mathf.Max(0.05f, cooldown);
                changed = true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(sword.itemId)) { sword.itemId = itemId; changed = true; }
                if (string.IsNullOrWhiteSpace(sword.displayName)) { sword.displayName = displayName; changed = true; }
                if (string.IsNullOrWhiteSpace(sword.description)) { sword.description = description; changed = true; }
                if (string.IsNullOrWhiteSpace(sword.category)) { sword.category = "Weapons"; changed = true; }
                if (sword.toolType != ToolType.Sword) { sword.toolType = ToolType.Sword; changed = true; }
                if (sword.attackMode != WeaponItem.AttackMode.Melee) { sword.attackMode = WeaponItem.AttackMode.Melee; changed = true; }
            }
            if (changed) EditorUtility.SetDirty(sword);
            return sword;
        }

        private static RecipeDefinition EnsureRecipe(string assetName, string displayName,
            ItemDefinition output, StationTier station, bool unlockedByDefault,
            ItemDefinition[] ingredients, int[] counts, RecipeRegistry registry, RepairCounts repairs)
        {
            var recipe = FindRecipe(assetName);
            bool created = false;
            if (recipe == null)
            {
                string path = $"{RecipeFolder}/{assetName}.asset";
                var other = AssetDatabase.LoadMainAssetAtPath(path);
                if (other != null)
                {
                    repairs.warnings++;
                    Debug.LogError($"[IndustrialCrusadersProgressionSetup] Preserved '{path}' because it is not a RecipeDefinition.");
                    return null;
                }
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, path);
                created = true;
                repairs.assetsCreated++;
            }

            if (!created && recipe.outputItem != null && output != null && recipe.outputItem != output)
            {
                repairs.warnings++;
                Debug.LogWarning($"[IndustrialCrusadersProgressionSetup] '{recipe.name}' already outputs '{recipe.outputItem.displayName}', not '{output.displayName}'. Its content was preserved; review this recipe link.");
                return null;
            }

            bool changed = false;
            if (created)
            {
                recipe.displayName = displayName;
                recipe.outputItem = output;
                recipe.outputCount = 1;
                recipe.requiredStation = station;
                recipe.unlockedByDefault = unlockedByDefault;
                recipe.inputs = BuildInputs(ingredients, counts);
                recipe.craftSeconds = CraftTimeDefaults.Suggest(recipe);
                changed = true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(recipe.displayName)) { recipe.displayName = displayName; changed = true; }
                if (recipe.outputItem == null && output != null) { recipe.outputItem = output; changed = true; repairs.linksRepaired++; }
                if (recipe.outputCount <= 0) { recipe.outputCount = 1; changed = true; }
                if (recipe.inputs == null || recipe.inputs.Length == 0)
                {
                    recipe.inputs = BuildInputs(ingredients, counts);
                    changed = recipe.inputs.Length > 0 || changed;
                    if (recipe.inputs.Length > 0) repairs.linksRepaired++;
                }
                // These two fields define the requested unlock and station gates; populated
                // ingredient arrays, output counts, and craft timing remain designer-owned.
                if (recipe.requiredStation != station) { recipe.requiredStation = station; changed = true; }
                if (recipe.unlockedByDefault != unlockedByDefault) { recipe.unlockedByDefault = unlockedByDefault; changed = true; }
            }

            if (changed) EditorUtility.SetDirty(recipe);
            if (registry != null && !registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                repairs.linksRepaired++;
            }
            return recipe;
        }

        private static ResearchNode FindOrCreateNode(ResearchTree tree, string id, string displayName,
            string description, int tier, ResearchSubCategory subCategory, Color tint,
            float researchSeconds, ScienceItem[] packs, int[] counts, RepairCounts repairs)
        {
            var node = FindNode(tree, id);
            bool created = false;
            if (node == null)
            {
                string path = $"{NodeFolder}/{id}.asset";
                node = AssetDatabase.LoadAssetAtPath<ResearchNode>(path);
                if (node == null)
                {
                    var other = AssetDatabase.LoadMainAssetAtPath(path);
                    if (other != null)
                    {
                        repairs.warnings++;
                        Debug.LogError($"[IndustrialCrusadersProgressionSetup] Preserved '{path}' because it is not a ResearchNode.");
                        return null;
                    }
                    if (BuildCost(packs, counts).Length == 0)
                    {
                        repairs.warnings++;
                        Debug.LogWarning($"[IndustrialCrusadersProgressionSetup] Did not create '{id}' because its science-pack links are missing.");
                        return null;
                    }
                    node = ScriptableObject.CreateInstance<ResearchNode>();
                    AssetDatabase.CreateAsset(node, path);
                    created = true;
                    repairs.assetsCreated++;
                }
            }

            bool changed = false;
            if (created)
            {
                node.nodeId = id;
                node.displayName = displayName;
                node.description = description;
                node.category = ResearchCategory.Environment;
                node.subCategory = subCategory;
                node.tier = tier;
                node.column = 0;
                node.iconTint = tint;
                node.researchSeconds = researchSeconds;
                node.cost = BuildCost(packs, counts);
                node.unlocksRecipes = Array.Empty<RecipeDefinition>();
                node.prerequisites = Array.Empty<ResearchNode>();
                node.upgradeKind = PlayerUpgradeKind.None;
                node.maxRanks = 1;
                node.costScalesWithRank = false;
                changed = true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(node.nodeId)) { node.nodeId = id; changed = true; }
                if (string.IsNullOrWhiteSpace(node.displayName)) { node.displayName = displayName; changed = true; }
                if (string.IsNullOrWhiteSpace(node.description)) { node.description = description; changed = true; }
                if (node.prerequisites == null) { node.prerequisites = Array.Empty<ResearchNode>(); changed = true; }
                if (node.unlocksRecipes == null) { node.unlocksRecipes = Array.Empty<RecipeDefinition>(); changed = true; }
            }

            if (!tree.nodes.Contains(node))
            {
                tree.nodes.Add(node);
                changed = true;
                repairs.linksRepaired++;
            }
            if (changed) EditorUtility.SetDirty(node);
            return node;
        }

        private static void MoveRecipeNames(ResearchTree tree, ResearchNode target,
            string[] assetNames, RecipeRegistry registry, RepairCounts counts,
            params string[] optionalAssetNames)
        {
            var optionalNames = new HashSet<string>(optionalAssetNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var recipes = new List<RecipeDefinition>();
            foreach (string assetName in assetNames)
            {
                var recipe = FindRecipe(assetName);
                if (recipe == null)
                {
                    if (!optionalNames.Contains(assetName))
                    {
                        counts.warnings++;
                        Debug.LogWarning($"[IndustrialCrusadersProgressionSetup] Recipe '{assetName}' was not found; its content was not changed.");
                    }
                    continue;
                }
                if (registry != null && !registry.recipes.Contains(recipe))
                {
                    registry.recipes.Add(recipe);
                    counts.linksRepaired++;
                    EditorUtility.SetDirty(registry);
                }
                if (recipe.unlockedByDefault)
                {
                    recipe.unlockedByDefault = false;
                    EditorUtility.SetDirty(recipe);
                    counts.recipesGated++;
                }
                recipes.Add(recipe);
            }
            MoveUnlocksToNode(tree, target, recipes, counts);
        }

        private static void MoveUnlocksToNode(ResearchTree tree, ResearchNode target,
            IEnumerable<RecipeDefinition> recipes, RepairCounts counts)
        {
            if (tree == null || target == null || recipes == null) return;
            var moving = new HashSet<RecipeDefinition>();
            foreach (var recipe in recipes) if (recipe != null) moving.Add(recipe);
            if (moving.Count == 0) return;

            foreach (var node in tree.nodes)
            {
                if (node == null || node == target || node.unlocksRecipes == null) continue;
                var kept = new List<RecipeDefinition>(node.unlocksRecipes.Length);
                bool removed = false;
                foreach (var recipe in node.unlocksRecipes)
                {
                    if (recipe != null && moving.Contains(recipe))
                    {
                        removed = true;
                        counts.unlockLinksMoved++;
                        continue;
                    }
                    kept.Add(recipe);
                }
                if (!removed) continue;
                node.unlocksRecipes = kept.ToArray();
                EditorUtility.SetDirty(node);
            }

            var additions = new List<RecipeDefinition>(target.unlocksRecipes ?? Array.Empty<RecipeDefinition>());
            bool changed = false;
            foreach (var recipe in moving)
            {
                if (additions.Contains(recipe)) continue;
                additions.Add(recipe);
                changed = true;
                counts.unlockLinksMoved++;
            }
            if (changed)
            {
                target.unlocksRecipes = additions.ToArray();
                EditorUtility.SetDirty(target);
            }
        }

        private static void MergePrerequisites(ResearchNode node, IEnumerable<ResearchNode> prerequisites, RepairCounts counts)
        {
            if (node == null || prerequisites == null) return;
            var merged = new List<ResearchNode>(node.prerequisites ?? Array.Empty<ResearchNode>());
            bool changed = false;
            foreach (var prerequisite in prerequisites)
            {
                if (prerequisite == null || prerequisite == node || merged.Contains(prerequisite)) continue;
                merged.Add(prerequisite);
                changed = true;
                counts.linksRepaired++;
            }
            if (!changed) return;
            node.prerequisites = merged.ToArray();
            EditorUtility.SetDirty(node);
        }

        private static RecipeIngredient[] BuildInputs(ItemDefinition[] ingredients, int[] counts)
        {
            var result = new List<RecipeIngredient>();
            if (ingredients == null || counts == null) return Array.Empty<RecipeIngredient>();
            int length = Math.Min(ingredients.Length, counts.Length);
            for (int i = 0; i < length; i++)
            {
                if (ingredients[i] == null || counts[i] <= 0) continue;
                result.Add(new RecipeIngredient { item = ingredients[i], count = counts[i] });
            }
            return result.ToArray();
        }

        private static ResearchNode.ScienceCost[] BuildCost(ScienceItem[] packs, int[] counts)
        {
            var result = new List<ResearchNode.ScienceCost>();
            if (packs == null || counts == null) return result.ToArray();
            int length = Math.Min(packs.Length, counts.Length);
            for (int i = 0; i < length; i++)
            {
                if (packs[i] == null || counts[i] <= 0) continue;
                result.Add(new ResearchNode.ScienceCost { pack = packs[i], count = counts[i] });
            }
            return result.ToArray();
        }

        private static ResearchNode FindNode(ResearchTree tree, string id)
        {
            if (tree == null || tree.nodes == null) return null;
            foreach (var node in tree.nodes)
                if (node != null && string.Equals(node.nodeId, id, StringComparison.OrdinalIgnoreCase))
                    return node;
            return null;
        }

        private static RecipeDefinition FindRecipe(string assetName)
            => FindAssetByName<RecipeDefinition>(assetName);

        private static T FindAssetByName<T>(string assetName) where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets(assetName + " t:" + typeof(T).Name);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), assetName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
            string leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
