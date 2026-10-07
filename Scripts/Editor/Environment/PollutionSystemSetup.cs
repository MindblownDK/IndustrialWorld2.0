// Assets/Scripts/Editor/Environment/PollutionSystemSetup.cs
//
// Airborne pollution + Phase 2 runoff recovery setup (RunStep114 is retained as a stable API).
// Non-destructive: creates missing assets/components, reconnects missing references,
// and preserves authored tuning.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Environment;
using VoxelEngine.Gas;
using VoxelEngine.GridSystem;
using VoxelEngine.Industrial;
using VoxelEngine.Items;
using VoxelEngine.Maritime;
using VoxelEngine.Power;
using VoxelEngine.Research;
using VoxelEngine.Transport;

namespace VoxelEngine.EditorTools
{
    public static class PollutionSystemSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string PollutionRoot = Root + "/Environment/Pollution";
        private const string ProfilesFolder = PollutionRoot + "/Profiles";
        private const string PrefabsFolder = PollutionRoot + "/Prefabs";
        private const string MaterialsFolder = PollutionRoot + "/Materials";
        private const string ItemsFolder = Root + "/Items";
        private const string BlocksFolder = Root + "/Blocks";
        private const string RecipesFolder = Root + "/Recipes";
        private const string NodesFolder = Root + "/Research/Nodes";
        private const string RecipeRegistryPath = Root + "/RecipeRegistry.asset";
        private const string ResearchTreePath = Root + "/Research/ResearchTree.asset";
        private const string CatalogPath = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        private const string CarbonPath = ItemsFolder + "/Item_CarbonConcentrate.asset";
        private const string SludgePath = ItemsFolder + "/Item_RemediationSludge.asset";
        private const string StonePath = ItemsFolder + "/Item_Stone.asset";
        private const string GraphitePath = Root + "/Industrial/Items/Item_Graphite.asset";
        private const string HarvesterPrefabPath = PrefabsFolder + "/AtmosphericCarbonHarvester.prefab";
        private const string HarvesterBlockPath = BlocksFolder + "/Block_AtmosphericCarbonHarvester.asset";
        private const string HarvesterRecipePath = RecipesFolder + "/Recipe_AtmosphericCarbonHarvester.asset";
        private const string GraphiteRecipePath = RecipesFolder + "/Recipe_CarbonConcentrateToGraphite.asset";
        private const string SludgeRecipePath = RecipesFolder + "/Recipe_StabilizeRemediationSludge.asset";
        private const string ResearchNodePath = NodesFolder + "/ResNode_AtmosphericCarbonCapture.asset";

        public static void RunStep114()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Pollution System", "Exit Play Mode before running setup.", "OK");
                return;
            }

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(RecipeRegistryPath);
            if (registry == null)
            {
                EditorUtility.DisplayDialog("Pollution System",
                    "Run Core & Project Bootstrap -> Build base crafting first. RecipeRegistry.asset is missing.", "OK");
                return;
            }
            registry.recipes ??= new List<RecipeDefinition>();

            try
            {
                EnsureFolders();
                int created = 0, repaired = 0, preserved = 0;
                var graphite = EnsureGraphiteItem(ref created, ref repaired, ref preserved);

                var combustion = EnsureProfile(ProfilesFolder + "/PollutionSource_Combustion.asset",
                    "combustion", "Solid-Fuel Combustion",
                    new PollutionLoad { airborneSmog = 4f, runoff = 0.04f, climateLoad = 0.08f },
                    new Vector3(0f, 2.2f, 0f), ref created, ref repaired, ref preserved);
                var process = EnsureProfile(ProfilesFolder + "/PollutionSource_IndustrialProcess.asset",
                    "industrial_process", "Industrial Process Emissions",
                    new PollutionLoad { airborneSmog = 1.2f, runoff = 0.01f, climateLoad = 0.03f },
                    new Vector3(0f, 2.5f, 0f), ref created, ref repaired, ref preserved);
                var flare = EnsureProfile(ProfilesFolder + "/PollutionSource_Flare.asset",
                    "flare", "Flare Combustion",
                    new PollutionLoad { airborneSmog = 3f, runoff = 0.03f, climateLoad = 0.1f },
                    new Vector3(0f, 5.5f, 0f), ref created, ref repaired, ref preserved);
                var exhaust = EnsureProfile(ProfilesFolder + "/PollutionSource_RoutedExhaust.asset",
                    "routed_exhaust", "Routed Engine Exhaust",
                    new PollutionLoad { airborneSmog = 5f, runoff = 0.08f, climateLoad = 0.12f },
                    new Vector3(0f, 0.5f, 0f), ref created, ref repaired, ref preserved);
                var smelting = EnsureProfile(ProfilesFolder + "/PollutionSource_ElectricSmelting.asset",
                    "electric_smelting", "Electric Smelting Process",
                    new PollutionLoad { airborneSmog = 0.7f, runoff = 0.01f, climateLoad = 0.01f },
                    new Vector3(0f, 2f, 0f), ref created, ref repaired, ref preserved);

                var carbon = EnsureCarbonItem(ref created, ref repaired, ref preserved);
                var sludge = EnsureSludgeItem(ref created, ref repaired, ref preserved);
                var prefab = EnsureHarvesterPrefab(carbon, sludge, ref created, ref repaired, ref preserved);
                var block = EnsureHarvesterBlock(prefab, ref created, ref repaired, ref preserved);

                ItemDefinition steel = FindItem("steel_ingot", "Item_SteelIngot")
                    ?? FindItem("steel_plate", "Item_SteelPlate");
                ItemDefinition wire = FindItem("copper_wire", "Item_CopperWire");
                ItemDefinition circuit = FindItem("circuit", "Item_Circuit");
                ItemDefinition stone = EnsureStoneItem(ref created, ref repaired, ref preserved);

                var harvesterRecipe = EnsureRecipe(HarvesterRecipePath, "Atmospheric Carbon Harvester",
                    block, 1, 35f, false,
                    Ingredients(steel, 30, wire, 18, circuit, 6), registry,
                    ref created, ref repaired, ref preserved);
                var graphiteRecipe = EnsureRecipe(GraphiteRecipePath, "Carbon to Graphite",
                    graphite, 1, 8f, false,
                    new[] { new RecipeIngredient { item = carbon, count = 4 } }, registry,
                    ref created, ref repaired, ref preserved);
                var sludgeRecipe = EnsureRecipe(SludgeRecipePath, "Stabilize Remediation Sludge",
                    stone, 1, 12f, false,
                    new[] { new RecipeIngredient { item = sludge, count = 4 } }, registry,
                    ref created, ref repaired, ref preserved);

                EnsurePersisted(graphite);
                EnsurePersisted(carbon);
                EnsurePersisted(sludge);
                EnsurePersisted(stone);
                EnsurePersisted(block);
                EnsureResearch(harvesterRecipe, graphiteRecipe, sludgeRecipe,
                    ref created, ref repaired, ref preserved);
                int sources = WireSourcePrefabs(combustion, process, flare, exhaust, smelting,
                    ref repaired, ref preserved);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[PollutionSetup] Complete. Created {created}, repaired {repaired}, " +
                          $"preserved {preserved}; {sources} emitting prefab(s) audited.");
                EditorUtility.DisplayDialog("Pollution Recovery Setup",
                    "Airborne pollution and Phase 2 runoff recovery content are ready.\n\n" +
                    "Created/reconnected:\n" +
                    "  - Air and soil/water effluent profiles on industrial sources\n" +
                    "  - Graphite, Carbon Concentrate and Remediation Sludge\n" +
                    "  - Carbon to Graphite and sludge stabilization recipes\n" +
                    "  - Combined atmospheric capture and soil remediation\n\n" +
                    $"Created: {created}   Repaired: {repaired}   Preserved: {preserved}\n" +
                    $"Source prefabs audited: {sources}", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("[PollutionSetup] Failed: " + ex);
                EditorUtility.DisplayDialog("Pollution System", "Setup stopped: " + ex.Message, "OK");
            }
        }

        private static PollutionSourceProfile EnsureProfile(string path, string id, string display,
            PollutionLoad defaultLoad, Vector3 defaultOffset,
            ref int created, ref int repaired, ref int preserved)
        {
            var profile = AssetDatabase.LoadAssetAtPath<PollutionSourceProfile>(path);
            bool isNew = profile == null;
            if (isNew)
            {
                profile = ScriptableObject.CreateInstance<PollutionSourceProfile>();
                profile.sourceId = id;
                profile.displayName = display;
                profile.perSecond = defaultLoad;
                profile.localOffset = defaultOffset;
                AssetDatabase.CreateAsset(profile, path);
                created++;
            }
            else
            {
                bool dirty = false;
                if (string.IsNullOrWhiteSpace(profile.sourceId)) { profile.sourceId = id; dirty = true; }
                if (string.IsNullOrWhiteSpace(profile.displayName)) { profile.displayName = display; dirty = true; }
                if (profile.perSecond.Total <= 0f) { profile.perSecond = defaultLoad; dirty = true; }
                else if (profile.perSecond.runoff <= 0f && defaultLoad.runoff > 0f)
                {
                    var load = profile.perSecond;
                    load.runoff = defaultLoad.runoff;
                    profile.perSecond = load;
                    dirty = true;
                }
                if (dirty) { repaired++; EditorUtility.SetDirty(profile); }
                else preserved++;
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static ItemDefinition EnsureGraphiteItem(ref int created, ref int repaired, ref int preserved)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ItemDefinition>(GraphitePath)
                ?? FindItem("graphite", "Item_Graphite");
            if (existing == null)
            {
                EnsureFolder(Path.GetDirectoryName(GraphitePath)?.Replace('\\', '/'));
                if (AssetDatabase.LoadMainAssetAtPath(GraphitePath) != null)
                    throw new InvalidOperationException("Graphite asset path is occupied by a non-item asset: " + GraphitePath);

                var graphite = ScriptableObject.CreateInstance<ResourceItem>();
                graphite.itemId = "graphite";
                graphite.displayName = "Graphite";
                graphite.description = "Refined carbon used in electrodes, electrical components and industrial recipes.";
                graphite.category = "Resources";
                graphite.subcategory = ResourceCategory.Raw;
                graphite.maxStack = 999;
                graphite.massPerUnit = 1f;
                graphite.fuelSeconds = 0f;
                graphite.iconTint = new Color(0.18f, 0.19f, 0.21f);
                AssetDatabase.CreateAsset(graphite, GraphitePath);
                created++;
                return graphite;
            }

            bool dirty = false;
            if (!string.Equals(existing.itemId, "graphite", StringComparison.OrdinalIgnoreCase))
            {
                existing.itemId = "graphite";
                dirty = true;
            }
            if (string.IsNullOrWhiteSpace(existing.displayName))
            {
                existing.displayName = "Graphite";
                dirty = true;
            }
            if (string.IsNullOrWhiteSpace(existing.description))
            {
                existing.description = "Refined carbon used in electrodes, electrical components and industrial recipes.";
                dirty = true;
            }
            if (existing.maxStack <= 0) { existing.maxStack = 999; dirty = true; }
            if (existing.massPerUnit <= 0f) { existing.massPerUnit = 1f; dirty = true; }
            if (dirty)
            {
                repaired++;
                EditorUtility.SetDirty(existing);
            }
            else preserved++;
            return existing;
        }

        private static ItemDefinition EnsureStoneItem(ref int created, ref int repaired, ref int preserved)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ItemDefinition>(StonePath)
                ?? FindItem("stone", "Item_Stone");
            if (existing == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(StonePath) != null)
                    throw new InvalidOperationException("Stone asset path is occupied by a non-item asset: " + StonePath);

                existing = ScriptableObject.CreateInstance<ItemDefinition>();
                existing.itemId = "stone";
                existing.displayName = "Stone";
                existing.description = "Core mineral resource used by construction and stabilized industrial recovery.";
                existing.category = "Resources";
                existing.maxStack = 999;
                existing.massPerUnit = 1f;
                existing.iconTint = new Color(0.55f, 0.55f, 0.58f);
                AssetDatabase.CreateAsset(existing, StonePath);
                created++;
                return existing;
            }

            bool dirty = false;
            if (string.IsNullOrWhiteSpace(existing.itemId)) { existing.itemId = "stone"; dirty = true; }
            if (string.IsNullOrWhiteSpace(existing.displayName)) { existing.displayName = "Stone"; dirty = true; }
            if (string.IsNullOrWhiteSpace(existing.description))
            {
                existing.description = "Core mineral resource used by construction and stabilized industrial recovery.";
                dirty = true;
            }
            if (existing.maxStack <= 0) { existing.maxStack = 999; dirty = true; }
            if (existing.massPerUnit <= 0f) { existing.massPerUnit = 1f; dirty = true; }
            if (dirty)
            {
                repaired++;
                EditorUtility.SetDirty(existing);
            }
            else preserved++;
            return existing;
        }

        private static ResourceItem EnsureCarbonItem(ref int created, ref int repaired, ref int preserved)
        {
            var item = AssetDatabase.LoadAssetAtPath<ResourceItem>(CarbonPath);
            bool isNew = item == null;
            if (isNew)
            {
                item = ScriptableObject.CreateInstance<ResourceItem>();
                item.itemId = "carbon_concentrate";
                item.displayName = "Carbon Concentrate";
                item.description = "Dense captured atmospheric carbon and soot. Compress it into Graphite at an Assembler.";
                item.category = "Resources";
                item.subcategory = ResourceCategory.Raw;
                item.maxStack = 900;
                item.massPerUnit = 2f;
                item.fuelSeconds = 0f;
                item.iconTint = new Color(0.22f, 0.24f, 0.25f);
                AssetDatabase.CreateAsset(item, CarbonPath);
                created++;
            }
            else
            {
                bool dirty = false;
                if (item.itemId != "carbon_concentrate") { item.itemId = "carbon_concentrate"; dirty = true; }
                if (string.IsNullOrWhiteSpace(item.displayName)) { item.displayName = "Carbon Concentrate"; dirty = true; }
                if (string.IsNullOrWhiteSpace(item.description))
                {
                    item.description = "Dense captured atmospheric carbon and soot. Compress it into Graphite at an Assembler.";
                    dirty = true;
                }
                if (item.maxStack <= 0) { item.maxStack = 900; dirty = true; }
                if (item.massPerUnit <= 0f) { item.massPerUnit = 2f; dirty = true; }
                if (dirty) repaired++; else preserved++;
            }
            EditorUtility.SetDirty(item);
            return item;
        }

        private static ResourceItem EnsureSludgeItem(ref int created, ref int repaired, ref int preserved)
        {
            var item = AssetDatabase.LoadAssetAtPath<ResourceItem>(SludgePath);
            bool isNew = item == null;
            if (isNew)
            {
                item = ScriptableObject.CreateInstance<ResourceItem>();
                item.itemId = "remediation_sludge";
                item.displayName = "Remediation Sludge";
                item.description = "Captured soil and water contamination. Stabilize it at an Assembler before reuse.";
                item.category = "Resources";
                item.subcategory = ResourceCategory.Raw;
                item.maxStack = 300;
                item.massPerUnit = 4f;
                item.fuelSeconds = 0f;
                item.iconTint = new Color(0.39f, 0.46f, 0.20f);
                AssetDatabase.CreateAsset(item, SludgePath);
                created++;
            }
            else
            {
                bool dirty = false;
                if (item.itemId != "remediation_sludge") { item.itemId = "remediation_sludge"; dirty = true; }
                if (string.IsNullOrWhiteSpace(item.displayName)) { item.displayName = "Remediation Sludge"; dirty = true; }
                if (string.IsNullOrWhiteSpace(item.description))
                {
                    item.description = "Captured soil and water contamination. Stabilize it at an Assembler before reuse.";
                    dirty = true;
                }
                if (item.maxStack <= 0) { item.maxStack = 300; dirty = true; }
                if (item.massPerUnit <= 0f) { item.massPerUnit = 4f; dirty = true; }
                if (dirty) repaired++; else preserved++;
            }
            EditorUtility.SetDirty(item);
            return item;
        }

        private static GameObject EnsureHarvesterPrefab(ItemDefinition carbon, ItemDefinition sludge,
            ref int created, ref int repaired, ref int preserved)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(HarvesterPrefabPath);
            bool isNew = existing == null;
            GameObject root = isNew ? new GameObject("AtmosphericCarbonHarvester")
                : PrefabUtility.LoadPrefabContents(HarvesterPrefabPath);
            bool dirty = isNew;

            if (isNew) BuildHarvesterVisuals(root.transform);

            var collider = root.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 1.65f, 0f);
                collider.size = new Vector3(2.6f, 3.3f, 2.6f);
                dirty = true;
            }
            if (root.GetComponent<PowerConsumer>() == null) { root.AddComponent<PowerConsumer>(); dirty = true; }
            var ports = root.GetComponent<PortConfig>();
            if (ports == null) { ports = root.AddComponent<PortConfig>(); dirty = true; }
            ports.EnsureAllFaces();
            if (root.GetComponent<ItemPortRouting>() == null) { root.AddComponent<ItemPortRouting>(); dirty = true; }
            var harvester = root.GetComponent<AtmosphericCarbonHarvester>();
            if (harvester == null) { harvester = root.AddComponent<AtmosphericCarbonHarvester>(); dirty = true; }
            if (harvester.carbonConcentrate == null)
            {
                harvester.carbonConcentrate = carbon;
                dirty = true;
            }
            if (harvester.remediationSludge == null)
            {
                harvester.remediationSludge = sludge;
                dirty = true;
            }

            GameObject result = existing;
            if (dirty || isNew) result = PrefabUtility.SaveAsPrefabAsset(root, HarvesterPrefabPath);
            if (isNew) UnityEngine.Object.DestroyImmediate(root);
            else PrefabUtility.UnloadPrefabContents(root);

            if (isNew) created++;
            else if (dirty) repaired++;
            else preserved++;
            return result;
        }

        private static void BuildHarvesterVisuals(Transform root)
        {
            Material dark = MakeMaterial("Mat_CarbonHarvesterDark", new Color(0.13f, 0.15f, 0.17f));
            Material steel = MakeMaterial("Mat_CarbonHarvesterSteel", new Color(0.36f, 0.42f, 0.45f));
            Material filter = MakeMaterial("Mat_CarbonHarvesterFilter", new Color(0.18f, 0.36f, 0.34f));
            Material accent = MakeMaterial("Mat_CarbonHarvesterAccent", new Color(0.24f, 0.85f, 0.67f));

            Prim(root, PrimitiveType.Cube, "Base", new Vector3(0f, 0.18f, 0f),
                new Vector3(2.6f, 0.36f, 2.6f), dark);
            Prim(root, PrimitiveType.Cylinder, "CollectorBody", new Vector3(0f, 1.35f, 0f),
                new Vector3(1.35f, 1.15f, 1.35f), steel);
            Prim(root, PrimitiveType.Cylinder, "IntakeRing", new Vector3(0f, 2.55f, 0f),
                new Vector3(1.6f, 0.20f, 1.6f), accent);
            Prim(root, PrimitiveType.Cylinder, "FilterCore", new Vector3(0f, 2.76f, 0f),
                new Vector3(1.15f, 0.14f, 1.15f), filter);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 0.25f;
                Prim(root, PrimitiveType.Cube, "IntakeVane_" + i,
                    new Vector3(Mathf.Cos(angle) * 0.72f, 2.82f, Mathf.Sin(angle) * 0.72f),
                    new Vector3(0.62f, 0.10f, 0.16f), dark,
                    new Vector3(0f, -i * 45f, 0f));
            }
            Prim(root, PrimitiveType.Cube, "OutputHopper", new Vector3(0f, 1.0f, 1.45f),
                new Vector3(1.15f, 0.8f, 0.65f), dark);
        }

        private static BlockItem EnsureHarvesterBlock(GameObject prefab,
            ref int created, ref int repaired, ref int preserved)
        {
            var item = AssetDatabase.LoadAssetAtPath<BlockItem>(HarvesterBlockPath);
            bool isNew = item == null;
            if (isNew)
            {
                item = ScriptableObject.CreateInstance<BlockItem>();
                item.itemId = "atmospheric_carbon_harvester";
                item.displayName = "Atmospheric Carbon Harvester";
                item.description = "Powered collector that removes airborne smog and local soil/water contamination, outputting Carbon Concentrate and Remediation Sludge automatically.";
                item.category = "Machines";
                item.maxStack = 20;
                item.massPerUnit = 240f;
                item.gridSize = Vector3Int.one;
                item.allowStacking = false;
                item.blockHealth = 650;
                item.miningTier = 2;
                item.iconTint = new Color(0.24f, 0.85f, 0.67f);
                item.placedPrefab = prefab;
                AssetDatabase.CreateAsset(item, HarvesterBlockPath);
                created++;
            }
            else
            {
                bool dirty = false;
                if (item.itemId != "atmospheric_carbon_harvester") { item.itemId = "atmospheric_carbon_harvester"; dirty = true; }
                if (string.IsNullOrWhiteSpace(item.displayName)) { item.displayName = "Atmospheric Carbon Harvester"; dirty = true; }
                const string description = "Powered collector that removes airborne smog and local soil/water contamination, outputting Carbon Concentrate and Remediation Sludge automatically.";
                if (!string.Equals(item.description, description, StringComparison.Ordinal)) { item.description = description; dirty = true; }
                if (item.placedPrefab == null) { item.placedPrefab = prefab; dirty = true; }
                if (item.maxStack <= 0) { item.maxStack = 20; dirty = true; }
                if (item.blockHealth <= 0) { item.blockHealth = 650; dirty = true; }
                if (dirty) repaired++; else preserved++;
            }
            EditorUtility.SetDirty(item);
            return item;
        }

        private static RecipeDefinition EnsureRecipe(string path, string display,
            ItemDefinition output, int outputCount, float seconds, bool unlocked,
            RecipeIngredient[] inputs, RecipeRegistry registry,
            ref int created, ref int repaired, ref int preserved)
        {
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
            bool isNew = recipe == null;
            if (isNew)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = display;
                recipe.outputItem = output;
                recipe.outputCount = outputCount;
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = seconds;
                recipe.unlockedByDefault = unlocked;
                recipe.inputs = inputs;
                AssetDatabase.CreateAsset(recipe, path);
                created++;
            }
            else
            {
                bool dirty = false;
                if (recipe.outputItem == null) { recipe.outputItem = output; recipe.outputCount = outputCount; dirty = true; }
                if (recipe.inputs == null || recipe.inputs.Length == 0) { recipe.inputs = inputs; dirty = true; }
                if (!string.Equals(recipe.displayName, display, StringComparison.Ordinal)) { recipe.displayName = display; dirty = true; }
                if (dirty) repaired++; else preserved++;
            }
            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                repaired++;
            }
            EditorUtility.SetDirty(recipe);
            return recipe;
        }

        private static void EnsureResearch(RecipeDefinition harvester, RecipeDefinition graphite,
            RecipeDefinition sludge, ref int created, ref int repaired, ref int preserved)
        {
            var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(ResearchNodePath);
            bool isNew = node == null;
            if (isNew)
            {
                node = ScriptableObject.CreateInstance<ResearchNode>();
                node.nodeId = "res_atmospheric_carbon_capture";
                node.displayName = "Atmospheric Carbon Capture";
                node.description = "Powered local carbon capture, automatic concentrate handling, and material recovery from industrial smog.";
                node.category = ResearchCategory.Environment;
                node.subCategory = ResearchSubCategory.Chemistry;
                node.tier = 6;
                node.column = 6;
                node.researchSeconds = 75f;
                node.maxRanks = 1;
                node.cost = Array.Empty<ResearchNode.ScienceCost>();
                node.iconTint = new Color(0.24f, 0.85f, 0.67f);
                AssetDatabase.CreateAsset(node, ResearchNodePath);
                created++;
            }

            bool dirty = false;
            const string researchName = "Industrial Pollution Recovery";
            const string researchDescription = "Powered atmospheric capture, soil and water remediation, automatic recovered-material handling, Graphite production and sludge stabilization.";
            if (!string.Equals(node.displayName, researchName, StringComparison.Ordinal))
            {
                node.displayName = researchName;
                dirty = true;
            }
            if (!string.Equals(node.description, researchDescription, StringComparison.Ordinal))
            {
                node.description = researchDescription;
                dirty = true;
            }
            var unlocks = new List<RecipeDefinition>(node.unlocksRecipes ?? Array.Empty<RecipeDefinition>());
            if (harvester != null && !unlocks.Contains(harvester)) { unlocks.Add(harvester); dirty = true; }
            if (graphite != null && !unlocks.Contains(graphite)) { unlocks.Add(graphite); dirty = true; }
            if (sludge != null && !unlocks.Contains(sludge)) { unlocks.Add(sludge); dirty = true; }
            node.unlocksRecipes = unlocks.ToArray();

            var flareNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(NodesFolder + "/ResNode_FlareDisposal.asset");
            if (flareNode != null)
            {
                var prerequisites = new List<ResearchNode>(node.prerequisites ?? Array.Empty<ResearchNode>());
                if (!prerequisites.Contains(flareNode))
                {
                    prerequisites.Add(flareNode);
                    node.prerequisites = prerequisites.ToArray();
                    dirty = true;
                }
            }

            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(ResearchTreePath);
            if (tree != null)
            {
                tree.nodes ??= new List<ResearchNode>();
                if (!tree.nodes.Contains(node)) { tree.nodes.Add(node); EditorUtility.SetDirty(tree); dirty = true; }
            }

            if (!isNew)
            {
                if (dirty) repaired++;
                else preserved++;
            }
            EditorUtility.SetDirty(node);
        }

        private static int WireSourcePrefabs(PollutionSourceProfile combustion,
            PollutionSourceProfile process, PollutionSourceProfile flare,
            PollutionSourceProfile exhaust, PollutionSourceProfile smelting,
            ref int repaired, ref int preserved)
        {
            int audited = 0;
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { Root });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponentInChildren<AtmosphericCarbonHarvester>(true) != null) continue;

                PollutionSourceProfile selected = null;
                if (asset.GetComponentInChildren<GridExhaustPipe>(true) != null) selected = exhaust;
                else if (asset.GetComponentInChildren<FlareStack>(true) != null
                    || asset.GetComponentInChildren<GridFlareStack>(true) != null) selected = flare;
                else if (asset.GetComponentInChildren<CoalGeneratorFuel>(true) != null
                    || asset.GetComponentInChildren<Furnace>(true) != null) selected = combustion;
                else if (asset.GetComponentInChildren<ElectricFurnace>(true) != null) selected = smelting;
                else if (asset.GetComponentInChildren<OilRefinery>(true) != null
                    || asset.GetComponentInChildren<DistillationPlant>(true) != null
                    || asset.GetComponentInChildren<CatalyticCracker>(true) != null
                    || asset.GetComponentInChildren<StationaryChemicalPlant>(true) != null
                    || asset.GetComponentInChildren<GridRefinery>(true) != null
                    || asset.GetComponentInChildren<GridChemicalPlant>(true) != null) selected = process;
                if (selected == null) continue;

                audited++;
                var root = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;
                var emitter = root.GetComponent<PollutionEmitter>();
                if (emitter == null) { emitter = root.AddComponent<PollutionEmitter>(); dirty = true; }
                if (emitter.profile == null) { emitter.profile = selected; dirty = true; }
                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    repaired++;
                }
                else preserved++;
                PollutionSourceProfile authoredProfile = emitter.profile;
                float authoredMultiplier = emitter.emissionMultiplier;
                PrefabUtility.UnloadPrefabContents(root);

                AppendRatedEmissionsToLinkedItems(asset, authoredProfile, authoredMultiplier,
                    routedEngine: false, ref repaired);
            }

            // Engines route their emissions through an exhaust-pipe emitter. Their own
            // placement item still advertises the same before-capture full-load rating.
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponentInChildren<GridMaritimeEngine>(true) == null) continue;

                var root = PrefabUtility.LoadPrefabContents(path);
                var engine = root.GetComponentInChildren<GridMaritimeEngine>(true);
                bool dirty = engine != null && engine.routedPollutionProfile == null;
                if (dirty)
                {
                    engine.routedPollutionProfile = exhaust;
                    EditorUtility.SetDirty(engine);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    repaired++;
                }
                PollutionSourceProfile authoredProfile = engine != null
                    ? engine.routedPollutionProfile
                    : exhaust;
                PrefabUtility.UnloadPrefabContents(root);
                AppendRatedEmissionsToLinkedItems(asset, authoredProfile, 1f,
                    routedEngine: true, ref repaired);
            }
            return audited;
        }

        private static void AppendRatedEmissionsToLinkedItems(GameObject prefab,
            PollutionSourceProfile profile, float emissionMultiplier, bool routedEngine, ref int repaired)
        {
            float multiplier = Mathf.Max(0f, emissionMultiplier);
            float ratedAirborne = profile != null ? profile.perSecond.airborneSmog * multiplier : 0f;
            float ratedRunoff = profile != null ? profile.perSecond.runoff * multiplier : 0f;
            if (prefab == null || (ratedAirborne <= 0f && ratedRunoff <= 0f)) return;
            string[] itemGuids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { Root });
            string prefix = routedEngine ? "Routed air emissions: " : "Rated air emissions: ";
            string suffix = routedEngine
                ? " at full load before exhaust capture and scrubbing."
                : " at full load.";
            string authoredAir = ratedAirborne > 0f
                ? prefix + PollutionUnits.FormatRate(ratedAirborne) + suffix
                : string.Empty;
            string authoredRunoff = ratedRunoff > 0f
                ? "Rated soil/water effluent: " + PollutionUnits.FormatContaminantRate(ratedRunoff) + " at full load."
                : string.Empty;
            string authoredLines = string.IsNullOrEmpty(authoredAir) ? authoredRunoff
                : string.IsNullOrEmpty(authoredRunoff) ? authoredAir
                : authoredAir + "\n" + authoredRunoff;

            for (int i = 0; i < itemGuids.Length; i++)
            {
                string itemPath = AssetDatabase.GUIDToAssetPath(itemGuids[i]);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemPath);
                bool linked = (item is BlockItem block && block.placedPrefab == prefab)
                    || (item is GridBlockItem grid && grid.blockPrefab == prefab);
                if (!linked) continue;

                string description = item.description ?? string.Empty;
                var kept = new List<string>();
                string[] lines = description.Replace("\r", string.Empty).Split('\n');
                for (int line = 0; line < lines.Length; line++)
                {
                    if (lines[line].StartsWith("Rated air emissions: ", StringComparison.Ordinal)
                        || lines[line].StartsWith("Routed air emissions: ", StringComparison.Ordinal)
                        || lines[line].StartsWith("Rated soil/water effluent: ", StringComparison.Ordinal)) continue;
                    kept.Add(lines[line]);
                }
                string baseDescription = string.Join("\n", kept).TrimEnd();
                string updated = string.IsNullOrEmpty(baseDescription)
                    ? authoredLines
                    : baseDescription + "\n\n" + authoredLines;
                if (item.description == updated) continue;
                item.description = updated;
                EditorUtility.SetDirty(item);
                repaired++;
            }
        }

        private static RecipeIngredient[] Ingredients(ItemDefinition first, int firstCount,
            ItemDefinition second, int secondCount, ItemDefinition third, int thirdCount)
        {
            var result = new List<RecipeIngredient>(3);
            if (first != null) result.Add(new RecipeIngredient { item = first, count = firstCount });
            if (second != null) result.Add(new RecipeIngredient { item = second, count = secondCount });
            if (third != null) result.Add(new RecipeIngredient { item = third, count = thirdCount });
            return result.ToArray();
        }

        private static ItemDefinition FindItem(string itemId, string assetStem)
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { Root });
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

        private static void EnsurePersisted(ItemDefinition item)
        {
            if (item == null) return;
            EnsureFolder(Path.GetDirectoryName(CatalogPath).Replace('\\', '/'));
            var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.items ??= new List<ItemDefinition>();
            if (!catalog.items.Contains(item))
            {
                catalog.items.Add(item);
                EditorUtility.SetDirty(catalog);
            }
        }

        private static GameObject Prim(Transform parent, PrimitiveType type, string name,
            Vector3 position, Vector3 scale, Material material, Vector3? rotation = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            if (rotation.HasValue) go.transform.localEulerAngles = rotation.Value;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            var collider = go.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            return go;
        }

        private static Material MakeMaterial(string name, Color color)
        {
            string path = MaterialsFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = name, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureFolders()
        {
            EnsureFolder(PollutionRoot);
            EnsureFolder(ProfilesFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(MaterialsFolder);
            EnsureFolder(ItemsFolder);
            EnsureFolder(BlocksFolder);
            EnsureFolder(RecipesFolder);
            EnsureFolder(NodesFolder);
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
