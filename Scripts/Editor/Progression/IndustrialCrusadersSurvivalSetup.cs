#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Farming;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Non-destructive Setup Wizard repairs for Power progression, science costs,
    /// edible livestock meat, and workstation recipe durations.
    /// </summary>
    public static class IndustrialCrusadersSurvivalSetup
    {
        private const string AssetRoot = "Assets/VoxelEngineAssets";
        private const string ResearchTreePath = AssetRoot + "/Research/ResearchTree.asset";
        private const string RecipeRegistryPath = AssetRoot + "/RecipeRegistry.asset";
        private const string RawMeatPath = AssetRoot + "/Fauna/Items/Item_RawMeat.asset";
        private const string SteakPath = AssetRoot + "/Fauna/Items/Food_Steak.asset";
        private const string SteakRecipePath = AssetRoot + "/Recipes/Smelt_Steak.asset";
        private const string FurnacePrefabPath = AssetRoot + "/StationPrefabs/Furnace.prefab";

        private sealed class SetupReport
        {
            public int assetsCreated;
            public int linksRepaired;
            public int recipesGated;
            public int researchCostsRepaired;
            public int recipeTimesInitialized;
            public int warnings;
        }

        public static void Run()
        {
            var report = new SetupReport();
            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(ResearchTreePath);
            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(RecipeRegistryPath);

            if (tree == null)
                Warn(report, $"'{ResearchTreePath}' was not found. Power gating and the Smelting cost repair need the existing research tree.");
            else if (tree.nodes == null)
            {
                tree.nodes = new List<ResearchNode>();
                EditorUtility.SetDirty(tree);
                report.linksRepaired++;
            }

            if (registry == null)
                Warn(report, $"'{RecipeRegistryPath}' was not found. Recipe assets can still be repaired, but registry links cannot be added.");
            else if (registry.recipes == null)
            {
                registry.recipes = new List<RecipeDefinition>();
                EditorUtility.SetDirty(registry);
                report.linksRepaired++;
            }

            EnsureGeneratorPowerGate(tree, registry, report);
            EnsureSmeltingPackICost(tree, report);
            EnsureSteakCookingContent(report);
            InitializeMissingWorkstationTimes(registry, report);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary =
                $"Assets created: {report.assetsCreated}\n" +
                $"Links repaired: {report.linksRepaired}\n" +
                $"Recipes gated behind Electricity: {report.recipesGated}\n" +
                $"Smelting research costs repaired: {report.researchCostsRepaired}\n" +
                $"Workstation recipe times initialized: {report.recipeTimesInitialized}\n" +
                $"Warnings: {report.warnings}\n\n" +
                (report.warnings > 0 ? "See the Console for items that need review." : "No warnings.");
            EditorUtility.DisplayDialog("17.6.0 Survival and Progression Repairs", summary, "OK");
            Debug.Log("[IndustrialCrusadersSurvivalSetup] " + summary.Replace("\n", " | "));
        }

        private static void EnsureGeneratorPowerGate(ResearchTree tree, RecipeRegistry registry, SetupReport report)
        {
            var generatorRecipes = FindCoalGeneratorRecipes(registry);
            if (generatorRecipes.Count == 0)
            {
                Warn(report, "No Coal Generator recipe asset was found. Build power content first; no recipe was invented.");
                return;
            }

            ResearchNode electricity = FindResearchNode(tree, "res_electricity");
            foreach (var recipe in generatorRecipes)
            {
                if (recipe.unlockedByDefault)
                {
                    recipe.unlockedByDefault = false;
                    EditorUtility.SetDirty(recipe);
                    report.recipesGated++;
                }

                if (registry != null && !registry.recipes.Contains(recipe))
                {
                    registry.recipes.Add(recipe);
                    EditorUtility.SetDirty(registry);
                    report.linksRepaired++;
                }
            }

            if (tree == null || electricity == null)
            {
                Warn(report, "Coal Generator recipes are locked by default, but the existing Electricity research node could not be found; its unlock link was not created.");
                return;
            }

            bool treeChanged = false;
            if (!tree.nodes.Contains(electricity))
            {
                tree.nodes.Add(electricity);
                treeChanged = true;
                report.linksRepaired++;
            }

            var moving = new HashSet<RecipeDefinition>(generatorRecipes);
            foreach (var node in tree.nodes)
            {
                if (node == null || node == electricity || node.unlocksRecipes == null) continue;
                var kept = new List<RecipeDefinition>(node.unlocksRecipes.Length);
                bool changed = false;
                foreach (var recipe in node.unlocksRecipes)
                {
                    if (recipe != null && moving.Contains(recipe))
                    {
                        changed = true;
                        report.linksRepaired++;
                        continue;
                    }
                    kept.Add(recipe);
                }
                if (!changed) continue;
                node.unlocksRecipes = kept.ToArray();
                EditorUtility.SetDirty(node);
            }

            var electricityUnlocks = new List<RecipeDefinition>(electricity.unlocksRecipes ?? Array.Empty<RecipeDefinition>());
            foreach (var recipe in generatorRecipes)
            {
                if (electricityUnlocks.Contains(recipe)) continue;
                electricityUnlocks.Add(recipe);
                report.linksRepaired++;
            }
            if (electricity.unlocksRecipes == null || electricityUnlocks.Count != electricity.unlocksRecipes.Length)
            {
                electricity.unlocksRecipes = electricityUnlocks.ToArray();
                EditorUtility.SetDirty(electricity);
            }
            if (treeChanged) EditorUtility.SetDirty(tree);
        }

        private static List<RecipeDefinition> FindCoalGeneratorRecipes(RecipeRegistry registry)
        {
            var found = new List<RecipeDefinition>();
            var seen = new HashSet<RecipeDefinition>();

            void AddIfGenerator(RecipeDefinition recipe)
            {
                if (recipe == null || !IsCoalGeneratorRecipe(recipe) || !seen.Add(recipe)) return;
                found.Add(recipe);
            }

            if (registry != null && registry.recipes != null)
                foreach (var recipe in registry.recipes) AddIfGenerator(recipe);

            foreach (string guid in AssetDatabase.FindAssets("t:RecipeDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
                AddIfGenerator(recipe);
            }
            return found;
        }

        private static bool IsCoalGeneratorRecipe(RecipeDefinition recipe)
        {
            if (recipe == null) return false;
            if (string.Equals(recipe.name, "Recipe_Generator", StringComparison.OrdinalIgnoreCase)) return true;

            var output = recipe.outputItem;
            return output != null &&
                (string.Equals(output.itemId, "block_gen_coal", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(output.displayName, "Coal Generator", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(output.name, "Block_Gen_Coal", StringComparison.OrdinalIgnoreCase));
        }

        private static void EnsureSmeltingPackICost(ResearchTree tree, SetupReport report)
        {
            ResearchNode smelting = FindResearchNode(tree, "res_smelting");
            ScienceItem scienceT1 = FindAssetByName<ScienceItem>("Item_ScienceT1");
            if (smelting == null)
            {
                Warn(report, "The existing 'res_smelting' research node was not found; its cost was not changed.");
                return;
            }
            if (scienceT1 == null)
            {
                Warn(report, "Science Pack I (Item_ScienceT1) was not found; the Smelting cost was not changed.");
                return;
            }
            if (tree != null && tree.nodes != null && !tree.nodes.Contains(smelting))
            {
                tree.nodes.Add(smelting);
                EditorUtility.SetDirty(tree);
                report.linksRepaired++;
            }

            int packICount = 0;
            if (smelting.cost != null)
            {
                foreach (var cost in smelting.cost)
                {
                    if (cost.pack != null && cost.pack.tier == 1 && cost.count > 0)
                        packICount = packICount > int.MaxValue - cost.count
                            ? int.MaxValue
                            : packICount + cost.count;
                }
            }
            if (packICount <= 0) packICount = 10;

            bool alreadyCorrect = smelting.cost != null
                && smelting.cost.Length == 1
                && smelting.cost[0].pack == scienceT1
                && smelting.cost[0].count == packICount;
            if (alreadyCorrect) return;

            smelting.cost = new[]
            {
                new ResearchNode.ScienceCost { pack = scienceT1, count = packICount }
            };
            EditorUtility.SetDirty(smelting);
            report.researchCostsRepaired++;
        }

        private static ResearchNode FindResearchNode(ResearchTree tree, string nodeId)
        {
            if (tree != null && tree.nodes != null)
            {
                foreach (var node in tree.nodes)
                {
                    if (node != null && string.Equals(node.nodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                        return node;
                }
            }

            foreach (string guid in AssetDatabase.FindAssets("t:ResearchNode"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(path);
                if (node != null && string.Equals(node.nodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                    return node;
            }
            return null;
        }

        private static T FindAssetByName<T>(string assetName) where T : UnityEngine.Object
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), assetName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }
            return null;
        }

        private static void EnsureSteakCookingContent(SetupReport report)
        {
            ItemDefinition rawMeat = EnsureRawMeat(report);
            FoodItem steak = EnsureSteak(report);
            if (rawMeat == null || steak == null) return;

            RegisterFoodItemsForPersistence(rawMeat, steak, report);
            SmeltingRecipe recipe = EnsureSteakRecipe(rawMeat, steak, report);
            if (recipe == null) return;

            RegisterSteakRecipeOnFurnace(recipe, report);
        }

        private static ItemDefinition EnsureRawMeat(SetupReport report)
        {
            ItemDefinition meat = LoadOrCreateAsset<ItemDefinition>(RawMeatPath, report, out bool created);
            if (meat == null) return null;

            bool changed = created;
            if (created)
            {
                meat.itemId = "item_raw_meat";
                meat.displayName = "Raw Meat";
                meat.description = "Fresh meat harvested from livestock. Cook it at a furnace for safe, filling food.";
                meat.iconTint = new Color(0.80f, 0.34f, 0.30f);
                meat.maxStack = 40;
                meat.massPerUnit = 0.5f;
                meat.category = "Food";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(meat.itemId)) { meat.itemId = "item_raw_meat"; changed = true; }
                if (string.IsNullOrWhiteSpace(meat.displayName)) { meat.displayName = "Raw Meat"; changed = true; }
                if (string.IsNullOrWhiteSpace(meat.description))
                {
                    meat.description = "Fresh meat harvested from livestock. Cook it at a furnace for safe, filling food.";
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(meat.category) || meat.category == "Misc")
                {
                    meat.category = "Food";
                    changed = true;
                }
                if (meat.maxStack <= 0) { meat.maxStack = 40; changed = true; }
                if (meat.massPerUnit <= 0f) { meat.massPerUnit = 0.5f; changed = true; }
            }

            if (changed) EditorUtility.SetDirty(meat);
            return meat;
        }

        private static FoodItem EnsureSteak(SetupReport report)
        {
            FoodItem steak = LoadOrCreateAsset<FoodItem>(SteakPath, report, out bool created);
            if (steak == null) return null;

            bool changed = created;
            if (created)
            {
                steak.itemId = "food_steak";
                steak.displayName = "Steak";
                steak.description = "Cooked livestock meat. Restores hunger and a little health.";
                steak.iconTint = new Color(0.58f, 0.24f, 0.17f);
                steak.maxStack = 64;
                steak.massPerUnit = 0.5f;
                steak.category = "Food";
                steak.hungerRestore = 35f;
                steak.healthRestore = 6f;
                steak.staminaRestore = 0f;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(steak.itemId)) { steak.itemId = "food_steak"; changed = true; }
                if (string.IsNullOrWhiteSpace(steak.displayName)) { steak.displayName = "Steak"; changed = true; }
                if (string.IsNullOrWhiteSpace(steak.description))
                {
                    steak.description = "Cooked livestock meat. Restores hunger and a little health.";
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(steak.category) || steak.category == "Misc")
                {
                    steak.category = "Food";
                    changed = true;
                }
                if (steak.maxStack <= 0) { steak.maxStack = 64; changed = true; }
                if (steak.massPerUnit <= 0f) { steak.massPerUnit = 0.5f; changed = true; }
                if (float.IsNaN(steak.hungerRestore) || float.IsInfinity(steak.hungerRestore) || steak.hungerRestore <= 0f)
                {
                    steak.hungerRestore = 35f;
                    changed = true;
                }
                if (float.IsNaN(steak.healthRestore) || float.IsInfinity(steak.healthRestore) || steak.healthRestore < 0f)
                {
                    steak.healthRestore = 0f;
                    changed = true;
                }
            }

            if (changed) EditorUtility.SetDirty(steak);
            return steak;
        }

        private static void RegisterFoodItemsForPersistence(ItemDefinition rawMeat, FoodItem steak, SetupReport report)
        {
            const string catalogPath = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";
            var catalog = LoadOrCreateAsset<ItemPersistenceCatalog>(catalogPath, report, out _);
            if (catalog == null) return;

            bool changed = false;
            if (catalog.items == null)
            {
                catalog.items = new List<ItemDefinition>();
                changed = true;
            }

            var itemGuids = new HashSet<string>(AssetDatabase.FindAssets("t:ItemDefinition"));
            foreach (string filter in new[]
            {
                "t:FoodItem", "t:ResourceItem", "t:BlockItem", "t:GridBlockItem",
                "t:ScienceItem", "t:PortableBatteryItem", "t:HydrogenCanisterItem", "t:JetpackItem"
            })
                foreach (string guid in AssetDatabase.FindAssets(filter)) itemGuids.Add(guid);

            var orderedGuids = new List<string>(itemGuids);
            orderedGuids.Sort((left, right) => string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(left), AssetDatabase.GUIDToAssetPath(right)));
            foreach (string guid in orderedGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item == null || catalog.items.Contains(item)) continue;
                catalog.items.Add(item);
                changed = true;
                report.linksRepaired++;
            }

            if (rawMeat != null && !catalog.items.Contains(rawMeat))
            {
                catalog.items.Add(rawMeat);
                changed = true;
                report.linksRepaired++;
            }
            if (steak != null && !catalog.items.Contains(steak))
            {
                catalog.items.Add(steak);
                changed = true;
                report.linksRepaired++;
            }
            if (changed) EditorUtility.SetDirty(catalog);
        }

        private static SmeltingRecipe EnsureSteakRecipe(ItemDefinition rawMeat, FoodItem steak, SetupReport report)
        {
            SmeltingRecipe recipe = LoadOrCreateAsset<SmeltingRecipe>(SteakRecipePath, report, out bool created);
            if (recipe == null) return null;

            bool changed = created;
            if (created)
            {
                recipe.input = rawMeat;
                recipe.inputCount = 1;
                recipe.output = steak;
                recipe.outputCount = 1;
                recipe.smeltSeconds = 8f;
            }
            else
            {
                if ((recipe.input != null && recipe.input != rawMeat)
                    || (recipe.output != null && recipe.output != steak))
                {
                    Warn(report, $"'{SteakRecipePath}' already has populated links to different items. Its content was preserved and it was not attached to the Furnace.");
                    return null;
                }

                if (recipe.input == null) { recipe.input = rawMeat; changed = true; }
                if (recipe.output == null) { recipe.output = steak; changed = true; }
                if (recipe.inputCount <= 0) { recipe.inputCount = 1; changed = true; }
                if (recipe.outputCount <= 0) { recipe.outputCount = 1; changed = true; }
                if (recipe.smeltSeconds <= 0f || float.IsNaN(recipe.smeltSeconds) || float.IsInfinity(recipe.smeltSeconds))
                {
                    recipe.smeltSeconds = 8f;
                    changed = true;
                }
            }

            if (changed) EditorUtility.SetDirty(recipe);
            return recipe.input == rawMeat && recipe.output == steak ? recipe : null;
        }

        private static void RegisterSteakRecipeOnFurnace(SmeltingRecipe recipe, SetupReport report)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FurnacePrefabPath) == null)
            {
                Warn(report, $"The existing Furnace prefab '{FurnacePrefabPath}' was not found; the Steak smelting asset was preserved but not registered.");
                return;
            }

            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(FurnacePrefabPath);
                var furnace = contents != null ? contents.GetComponentInChildren<Furnace>(true) : null;
                if (furnace == null)
                {
                    Warn(report, $"No Furnace component was found in '{FurnacePrefabPath}'; no prefab content was changed.");
                    return;
                }

                bool changed = false;
                if (furnace.knownRecipes == null)
                {
                    furnace.knownRecipes = new List<SmeltingRecipe>();
                    changed = true;
                }
                if (!furnace.knownRecipes.Contains(recipe))
                {
                    furnace.knownRecipes.Add(recipe);
                    changed = true;
                    report.linksRepaired++;
                }
                if (!changed) return;

                EditorUtility.SetDirty(furnace);
                if (PrefabUtility.SaveAsPrefabAsset(contents, FurnacePrefabPath) == null)
                    Warn(report, $"Unity could not save the updated Furnace prefab at '{FurnacePrefabPath}'.");
            }
            catch (Exception exception)
            {
                Warn(report, $"Furnace recipe registration failed without deleting the prefab: {exception.Message}");
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void InitializeMissingWorkstationTimes(RecipeRegistry registry, SetupReport report)
        {
            var recipes = new HashSet<RecipeDefinition>();
            if (registry != null && registry.recipes != null)
                foreach (var recipe in registry.recipes)
                    if (recipe != null) recipes.Add(recipe);

            AddRecipeAssets(recipes, "t:RecipeDefinition");
            // Unity's type filter may return only the concrete subtype for custom cooking recipes.
            AddRecipeAssets(recipes, "t:CookingRecipe");

            foreach (var recipe in recipes)
            {
                if (recipe.requiredStation == StationTier.None) continue;
                if (!float.IsNaN(recipe.craftSeconds) && !float.IsInfinity(recipe.craftSeconds) && recipe.craftSeconds > 0f)
                    continue;

                float suggested = CraftTimeDefaults.Suggest(recipe);
                if (suggested <= 0f) continue;
                recipe.craftSeconds = suggested;
                EditorUtility.SetDirty(recipe);
                report.recipeTimesInitialized++;
            }
        }

        private static void AddRecipeAssets(HashSet<RecipeDefinition> recipes, string filter)
        {
            foreach (string guid in AssetDatabase.FindAssets(filter))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
                if (recipe != null) recipes.Add(recipe);
            }
        }

        private static T LoadOrCreateAsset<T>(string path, SetupReport report, out bool created)
            where T : ScriptableObject
        {
            created = false;
            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
            if (mainAsset != null)
            {
                if (mainAsset is T typed) return typed;
                Warn(report, $"Preserved '{path}' because its existing asset has type '{mainAsset.GetType().Name}', not '{typeof(T).Name}'.");
                return null;
            }

            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            created = true;
            report.assetsCreated++;
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path == "Assets" || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folder = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(folder)) return;
            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, folder);
        }

        private static void Warn(SetupReport report, string message)
        {
            report.warnings++;
            Debug.LogWarning("[IndustrialCrusadersSurvivalSetup] " + message);
        }
    }
}
#endif
