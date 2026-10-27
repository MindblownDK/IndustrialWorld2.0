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
    /// Non-destructive Setup Wizard step (18.2.0-dev) that wires planter automation into
    /// the content that already exists. No new items, no new block ids, no new recipes: the
    /// round adds behaviour to the Seed Planter and the Harvester, so this step only makes
    /// sure the prefabs carry it and the assets that reference them still point at them.
    ///
    /// What it guarantees:
    ///   * Seed Planter prefab: PlacedBlock + BoxCollider + CropPlanter, the output-port
    ///     geometry appended if it is missing, and the new tuning filled in only where it
    ///     is unset (autoDumpWhenRipe, dumpRadius, dumpIntervalSeconds);
    ///   * Harvester prefab: PowerConsumer at the authored 40 W if the prefab has none, the
    ///     Harvester component, and the new coverage/push values filled in only where they
    ///     are unset - scan radius, scan interval and output slots are player tuning and are
    ///     never touched;
    ///   * Block_SeedPlanter links its prefab, and both block items are in the
    ///     ItemPersistenceCatalog and their recipes in RecipeRegistry. Reward values, recipe
    ///     inputs, station tiers and craft times are preserved exactly as authored.
    ///
    /// Idempotent: a second run reports everything as preserved.
    /// Editor-only: invoked from Tools -> Voxel Engine -> Setup Wizard.
    /// </summary>
    public static class PlanterAutomationSetup
    {
        private const string AssetRoot         = "Assets/VoxelEngineAssets";
        private const string FarmRoot          = AssetRoot + "/Farming";
        private const string FarmPrefabsFolder = FarmRoot + "/Prefabs";
        private const string FarmBlocksFolder  = FarmRoot + "/Blocks";
        private const string FarmRecipesFolder = FarmRoot + "/Recipes";
        private const string ForestryMats      = AssetRoot + "/Materials/Forestry";
        private const string RecipeRegistryPath = AssetRoot + "/RecipeRegistry.asset";
        private const string CatalogPath       = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        private const string PlanterPrefabPath  = FarmPrefabsFolder + "/SeedPlanter.prefab";
        private const string HarvesterPrefabPath = FarmPrefabsFolder + "/Harvester.prefab";
        private const string PlanterBlockPath   = FarmBlocksFolder + "/Block_SeedPlanter.asset";
        private const string HarvesterBlockPath = FarmBlocksFolder + "/Block_Harvester.asset";
        private const string PlanterRecipePath  = FarmRecipesFolder + "/Recipe_SeedPlanter.asset";
        private const string HarvesterRecipePath = FarmRecipesFolder + "/Recipe_Harvester.asset";

        public static void RunStep122()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Planter Automation Setup",
                    "Exit Play Mode before running setup.", "OK");
                return;
            }

            EnsureFolder(AssetRoot, "Farming");
            EnsureFolder(FarmRoot, "Prefabs");
            EnsureFolder(FarmRoot, "Blocks");
            EnsureFolder(FarmRoot, "Recipes");

            var created = new List<string>();
            var repaired = new List<string>();
            var preserved = new List<string>();

            var planterBlock = EnsureBlockItem(
                PlanterBlockPath,
                "block_seed_planter",
                "Seed Planter",
                "One-metre irrigated soil tray for a single crop. Right-click with a seed to plant, "
                + "right-click again when it is ripe to harvest, and fertilize once with biomass or "
                + "Organic Fertilizer for faster growth and a bonus unit. Water it with a Sprinkler "
                + "or a water source within six metres. Place a belt, chute or item pipe against its "
                + "output port and it empties itself when ripe. Reports its crop to any Farm screen "
                + "on the same construct.",
                new Color(0.32f, 0.66f, 0.30f),
                new Vector3Int(1, 1, 1),
                maxHp: 80,
                category: "Farming",
                created: created, repaired: repaired, preserved: preserved);

            var harvesterBlock = EnsureBlockItem(
                HarvesterBlockPath,
                "block_harvester",
                "Harvester",
                "Automated crop picker. Scans 8 m for mature Tilled Soil, Seed Planter trays and Tree "
                + "Farming Plots, harvests them, replants the same crop, clears a bed whose crop was "
                + "lost, and pushes the yield into adjacent belts, chutes or pipes. A full belt is a "
                + "wait, never a lost harvest. ~40 W.",
                new Color(0.55f, 0.45f, 0.20f),
                new Vector3Int(1, 1, 1),
                maxHp: 400,
                category: "Farming",
                created: created, repaired: repaired, preserved: preserved);

            // The authored descriptions predate the port. Extend them once, additively, so a
            // player reading the item card learns the tray can feed a belt - and never rewrite
            // text that is already there.
            AppendIfMissing(planterBlock,
                "output port",
                " Place a belt, chute, funnel or item pipe against its output port and the tray empties "
                + "itself when ripe, keeping one seed back to replant.",
                repaired);
            AppendIfMissing(harvesterBlock,
                "Seed Planter",
                " It also works Seed Planter trays and Tree Farming Plots, replants the same crop after "
                + "each harvest, and clears a bed whose crop was lost.",
                repaired);

            var planterPrefab = EnsurePlanterPrefab(planterBlock, created, repaired, preserved);
            EnsureHarvesterPrefab(harvesterBlock, created, repaired, preserved);

            EnsureRecipeLink(PlanterRecipePath, planterBlock, created, repaired, preserved);
            EnsureRecipeLink(HarvesterRecipePath, harvesterBlock, created, repaired, preserved);

            EnsureCatalogEntry(planterBlock);
            EnsureCatalogEntry(harvesterBlock);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Planter Automation Setup] 18.2.0-dev — "
                + $"created {created.Count}, repaired {repaired.Count}, preserved {preserved.Count}."
                + (planterPrefab != null ? " Seed Planter and Harvester prefabs verified." : string.Empty));

            EditorUtility.DisplayDialog(
                "Planter Automation Setup (18.2.0-dev)",
                "Planter automation is wired.\n\n"
                + $"Created: {created.Count}\n"
                + $"Repaired: {repaired.Count}\n"
                + $"Preserved as authored: {preserved.Count}\n\n"
                + "Seed Planter: output port geometry, automation defaults, crop persistence.\n"
                + "Harvester: tray and soil coverage, dead-bed clearing, push radius.\n"
                + "Recipes, station tiers, craft times and power values were not changed.\n\n"
                + "No new items were added, so no icons are needed. Re-run any time.",
                "OK");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SEED PLANTER
        // ════════════════════════════════════════════════════════════════════════

        private static GameObject EnsurePlanterPrefab(BlockItem blockItem,
            List<string> created, List<string> repaired, List<string> preserved)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlanterPrefabPath);

            if (existing == null)
            {
                var root = new GameObject("SeedPlanter");
                try
                {
                    var box = root.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.18f, 0f);
                    box.size = new Vector3(0.96f, 0.36f, 0.96f);

                    var placed = root.AddComponent<PlacedBlock>();
                    placed.Item = blockItem;
                    placed.Hp = blockItem != null ? blockItem.blockHealth : 80;

                    var planter = root.AddComponent<CropPlanter>();
                    planter.RebuildVisuals();
                    AssignPortMaterials(root);

                    var saved = PrefabUtility.SaveAsPrefabAsset(root, PlanterPrefabPath);
                    if (blockItem != null)
                    {
                        blockItem.placedPrefab = saved;
                        EditorUtility.SetDirty(blockItem);
                    }
                    created.Add("Seed Planter prefab");
                    return saved;
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }

            var contents = PrefabUtility.LoadPrefabContents(PlanterPrefabPath);
            try
            {
                bool dirty = false;

                if (contents.GetComponent<BoxCollider>() == null)
                {
                    var box = contents.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.18f, 0f);
                    box.size = new Vector3(0.96f, 0.36f, 0.96f);
                    repaired.Add("Seed Planter collider");
                    dirty = true;
                }

                if (contents.GetComponent<PlacedBlock>() == null)
                {
                    var placed = contents.AddComponent<PlacedBlock>();
                    placed.Item = blockItem;
                    placed.Hp = blockItem != null ? blockItem.blockHealth : 80;
                    repaired.Add("Seed Planter PlacedBlock");
                    dirty = true;
                }

                var planter = contents.GetComponent<CropPlanter>();
                if (planter == null)
                {
                    planter = contents.AddComponent<CropPlanter>();
                    repaired.Add("Seed Planter CropPlanter component");
                    dirty = true;
                }

                // Tuned values are left exactly as they are. Only an unset value is filled.
                if (planter.dumpRadius <= 0.01f)
                {
                    planter.dumpRadius = 1.6f;
                    repaired.Add("Seed Planter output-port reach");
                    dirty = true;
                }
                if (planter.dumpIntervalSeconds <= 0.01f)
                {
                    planter.dumpIntervalSeconds = 1f;
                    repaired.Add("Seed Planter output-port interval");
                    dirty = true;
                }
                if (planter.irrigationCheckRadius <= 0.01f)
                {
                    planter.irrigationCheckRadius = 6f;
                    repaired.Add("Seed Planter irrigation radius");
                    dirty = true;
                }

                if (EnsurePortGeometry(contents.transform))
                {
                    repaired.Add("Seed Planter output-port geometry");
                    AssignPortMaterials(contents);
                    dirty = true;
                }

                if (blockItem != null && blockItem.placedPrefab != existing)
                {
                    blockItem.placedPrefab = existing;
                    EditorUtility.SetDirty(blockItem);
                    repaired.Add("Seed Planter block-to-prefab link");
                }

                if (dirty)
                {
                    var saved = PrefabUtility.SaveAsPrefabAsset(contents, PlanterPrefabPath);
                    return saved;
                }

                preserved.Add("Seed Planter prefab");
                return existing;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Append the three discharge-port parts to the generated planter visuals if they are
        /// missing. Existing geometry is never rebuilt or rescaled - a project that tuned its
        /// own tray keeps it, and only the port is added.
        /// </summary>
        private static bool EnsurePortGeometry(Transform root)
        {
            var visuals = root.Find("Generated_PlanterVisuals");
            if (visuals == null) return false;

            bool changed = false;

            if (visuals.Find("OutputPort_Chute") == null)
            {
                MakePart(visuals, "OutputPort_Chute",
                    new Vector3(0.50f, 0.16f, 0f), new Vector3(0.22f, 0.14f, 0.34f), Vector3.zero);
                changed = true;
            }
            if (visuals.Find("OutputPort_Lip") == null)
            {
                MakePart(visuals, "OutputPort_Lip",
                    new Vector3(0.58f, 0.22f, 0f), new Vector3(0.06f, 0.02f, 0.30f), Vector3.zero);
                changed = true;
            }
            if (visuals.Find("OutputPort_Indicator") == null)
            {
                MakePart(visuals, "OutputPort_Indicator",
                    new Vector3(0.50f, 0.25f, 0.20f), new Vector3(0.06f, 0.03f, 0.06f), Vector3.zero);
                changed = true;
            }

            return changed;
        }

        private static void MakePart(Transform parent, string name, Vector3 pos, Vector3 scale, Vector3 euler)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;

            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
        }

        /// <summary>Give the port its steel, water and glow materials. Only these three names
        /// are touched, so a prefab's own material work is left alone.</summary>
        private static void AssignPortMaterials(GameObject root)
        {
            if (root == null) return;

            var steel = GetOrCreateMaterial($"{ForestryMats}/Mat_ForestryMidSteel.mat", new Color(0.30f, 0.34f, 0.38f));
            var water = GetOrCreateMaterial($"{ForestryMats}/Mat_ForestryWaterBlue.mat", new Color(0.20f, 0.62f, 0.88f));
            var glow  = GetOrCreateMaterial($"{ForestryMats}/Mat_ForestryBiofuelGlow.mat", new Color(0.52f, 0.86f, 0.22f), emissive: true);

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;

                switch (r.gameObject.name)
                {
                    case "OutputPort_Chute":     r.sharedMaterial = steel; break;
                    case "OutputPort_Lip":       r.sharedMaterial = water; break;
                    case "OutputPort_Indicator": r.sharedMaterial = glow;  break;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HARVESTER
        // ════════════════════════════════════════════════════════════════════════

        private static void EnsureHarvesterPrefab(BlockItem blockItem,
            List<string> created, List<string> repaired, List<string> preserved)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(HarvesterPrefabPath);

            if (existing == null)
            {
                var root = new GameObject("Harvester");
                try
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "Mesh";
                    cube.transform.SetParent(root.transform, false);
                    cube.transform.localScale = new Vector3(1.2f, 1.0f, 1.2f);

                    var power = root.AddComponent<PowerConsumer>();
                    power.wattsPerSecond = 40f;
                    power.connectRadius = 1.6f;

                    var harvester = root.AddComponent<Harvester>();
                    harvester.scanRadius = 8f;
                    harvester.scanInterval = 2f;
                    harvester.autoReplant = true;
                    harvester.clearDeadBeds = true;
                    harvester.pushRadius = 1.8f;
                    harvester.outputSlots = 6;

                    var placed = root.AddComponent<PlacedBlock>();
                    placed.Item = blockItem;
                    placed.Hp = blockItem != null ? blockItem.blockHealth : 400;

                    var saved = PrefabUtility.SaveAsPrefabAsset(root, HarvesterPrefabPath);
                    if (blockItem != null)
                    {
                        blockItem.placedPrefab = saved;
                        EditorUtility.SetDirty(blockItem);
                    }
                    created.Add("Harvester prefab");
                    return;
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }

            var contents = PrefabUtility.LoadPrefabContents(HarvesterPrefabPath);
            try
            {
                bool dirty = false;

                if (contents.GetComponent<PowerConsumer>() == null)
                {
                    var power = contents.AddComponent<PowerConsumer>();
                    power.wattsPerSecond = 40f;
                    power.connectRadius = 1.6f;
                    repaired.Add("Harvester power consumer (40 W)");
                    dirty = true;
                }

                var harvester = contents.GetComponent<Harvester>();
                if (harvester == null)
                {
                    harvester = contents.AddComponent<Harvester>();
                    repaired.Add("Harvester component");
                    dirty = true;
                }

                // Scan radius, interval and output slots are authored balance - untouched.
                if (harvester.pushRadius <= 0.01f)
                {
                    harvester.pushRadius = 1.8f;
                    repaired.Add("Harvester push radius");
                    dirty = true;
                }
                if (harvester.outputSlots <= 0)
                {
                    harvester.outputSlots = 6;
                    repaired.Add("Harvester output slots");
                    dirty = true;
                }
                if (contents.GetComponent<PlacedBlock>() == null)
                {
                    var placed = contents.AddComponent<PlacedBlock>();
                    placed.Item = blockItem;
                    placed.Hp = blockItem != null ? blockItem.blockHealth : 400;
                    repaired.Add("Harvester PlacedBlock");
                    dirty = true;
                }

                if (blockItem != null && blockItem.placedPrefab != existing)
                {
                    blockItem.placedPrefab = existing;
                    EditorUtility.SetDirty(blockItem);
                    repaired.Add("Harvester block-to-prefab link");
                }

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, HarvesterPrefabPath);
                    return;
                }

                preserved.Add("Harvester prefab");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ASSETS THAT MUST KEEP POINTING AT THOSE PREFABS
        // ════════════════════════════════════════════════════════════════════════

        private static void EnsureRecipeLink(string recipePath, ItemDefinition output,
            List<string> created, List<string> repaired, List<string> preserved)
        {
            if (output == null) return;

            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recipePath);
            if (recipe == null)
            {
                // No recipe authored for this block yet: make the smallest complete one so
                // the block is reachable from a station. An existing recipe's inputs, tier
                // and craft time are always preserved, so this branch never rebalances.
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, recipePath);
                recipe.displayName = output.displayName;
                recipe.outputItem = output;
                recipe.outputCount = 1;
                recipe.requiredStation = StationTier.CraftingBench;
                recipe.unlockedByDefault = false;
                recipe.craftSeconds = CraftTimeDefaults.Suggest(StationTier.CraftingBench, recipe.inputs);
                created.Add($"{output.displayName} recipe");
            }
            else
            {
                bool repairedHere = false;
                if (recipe.outputItem == null)
                {
                    recipe.outputItem = output;
                    repairedHere = true;
                }
                if (recipe.outputCount <= 0)
                {
                    recipe.outputCount = 1;
                    repairedHere = true;
                }
                if (repairedHere) repaired.Add($"{output.displayName} recipe output");
                else preserved.Add($"{output.displayName} recipe");
                EditorUtility.SetDirty(recipe);
            }

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(RecipeRegistryPath);
            if (registry == null) return;
            if (registry.recipes == null) registry.recipes = new List<RecipeDefinition>();
            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                repaired.Add($"{output.displayName} RecipeRegistry link");
            }
        }

        private static void EnsureCatalogEntry(ItemDefinition item)
        {
            if (item == null) return;

            var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(CatalogPath);
            if (catalog == null)
            {
                EnsureFolder("Assets", "Resources");
                EnsureFolder("Assets/Resources", "VoxelEngine");
                catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            if (catalog.items == null) catalog.items = new List<ItemDefinition>();
            if (catalog.items.Contains(item)) return;

            catalog.items.Add(item);
            EditorUtility.SetDirty(catalog);
        }

        private static BlockItem EnsureBlockItem(string path, string id, string displayName,
            string description, Color tint, Vector3Int footprint, int maxHp, string category,
            List<string> created, List<string> repaired, List<string> preserved)
        {
            var item = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool isNew = item == null;
            if (isNew)
            {
                item = ScriptableObject.CreateInstance<BlockItem>();
                AssetDatabase.CreateAsset(item, path);
                created.Add(displayName);
            }

            // Identity is repaired when missing; authored balance is preserved when present.
            if (string.IsNullOrEmpty(item.itemId)) { item.itemId = id; if (!isNew) repaired.Add($"{displayName} item id"); }
            if (string.IsNullOrEmpty(item.displayName)) { item.displayName = displayName; if (!isNew) repaired.Add($"{displayName} name"); }
            if (string.IsNullOrEmpty(item.description)) { item.description = description; if (!isNew) repaired.Add($"{displayName} description"); }
            if (string.IsNullOrEmpty(item.category)) { item.category = category; if (!isNew) repaired.Add($"{displayName} category"); }
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
            else
            {
                preserved.Add($"{displayName} tuning");
            }

            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>Append one sentence to an authored description when the idea is not in it yet.
        /// Existing text is never rewritten, so a hand-tuned description survives every run.</summary>
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

        private static Material GetOrCreateMaterial(string path, Color color, bool emissive = false)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

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

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
