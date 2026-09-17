#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Combat;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 88 (11.20.0-dev): Boss Relic Cores.
    ///
    ///   Relic items      - one per named boss, the proof that encounter was beaten.
    ///   Boss variants    - tougher versions of the existing creatures, saved as separate
    ///                      prefabs so the ordinary creature is left completely untouched.
    ///   Relic research   - late-game nodes that cannot be started without the relic.
    ///
    /// Non-destructive: the base creature prefabs are never modified; boss variants are
    /// new assets beside them. Existing relic items and nodes only have missing links
    /// repaired. Safe to re-run. Logs with the [Setup 88] prefix.
    /// </summary>
    public static class BossRelicSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string RelicFolder = Root + "/Relics";
        private const string NodeFolder = Root + "/Research/Nodes";
        private const string EnemyFolder = "Assets/Resources/Enemies";

        private readonly struct BossSpec
        {
            public readonly string SourcePrefab, BossPrefab, DisplayName;
            public readonly BossRelicKind Relic;
            public readonly float HealthMultiplier, Scale;

            public BossSpec(string sourcePrefab, string bossPrefab, string displayName,
                BossRelicKind relic, float healthMultiplier, float scale)
            {
                SourcePrefab = sourcePrefab; BossPrefab = bossPrefab; DisplayName = displayName;
                Relic = relic; HealthMultiplier = healthMultiplier; Scale = scale;
            }
        }

        private static readonly BossSpec[] Bosses =
        {
            new("Basilisk",  "Boss_Basilisk",  "Elder Basilisk",   BossRelicKind.PetrifiedCore, 6f,   1.6f),
            new("Ifrit",     "Boss_Ifrit",     "Ifrit Sultan",     BossRelicKind.EmberCore,     5f,   1.5f),
            new("Roc",       "Boss_Roc",       "Storm Roc",        BossRelicKind.SkyCore,       5f,   1.7f),
            new("Karkadann", "Boss_Karkadann", "Karkadann Tyrant", BossRelicKind.BruteCore,     6.5f, 1.6f),
        };

        private readonly struct RelicSpec
        {
            public readonly BossRelicKind Kind;
            public readonly string Display, Description;
            public readonly Color Tint;

            public RelicSpec(BossRelicKind kind, string display, string description, Color tint)
            {
                Kind = kind; Display = display; Description = description; Tint = tint;
            }
        }

        private static readonly RelicSpec[] Relics =
        {
            new(BossRelicKind.PetrifiedCore, "Petrified Core",
                "A stone heart still warm from the Elder Basilisk. Proof of an encounter " +
                "survived, and the key to petrification research.",
                new Color(0.62f, 0.72f, 0.52f)),

            new(BossRelicKind.EmberCore, "Ember Core",
                "A coal that will not go out, cut from an Ifrit Sultan. Required for the " +
                "highest-temperature research.",
                new Color(0.96f, 0.52f, 0.22f)),

            new(BossRelicKind.SkyCore, "Sky Core",
                "A hollow, weightless stone taken from a Storm Roc. Required for research " +
                "into large-scale flight and orbital construction.",
                new Color(0.58f, 0.82f, 0.96f)),

            new(BossRelicKind.BruteCore, "Brute Core",
                "The dense horn-root of a Karkadann Tyrant. Required for heavy structural " +
                "research.",
                new Color(0.82f, 0.68f, 0.44f)),

            new(BossRelicKind.AbyssCore, "Abyss Core",
                "A pressure-forged pearl from the Leviathan. Reserved for the deepest " +
                "maritime research.",
                new Color(0.42f, 0.52f, 0.86f)),
        };

        public static void RunStep88()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Boss Relics", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var relicItems = EnsureRelicItems(out int relicsCreated);
                int bossesBuilt = EnsureBossVariants(relicItems, out int missingSources);
                int nodesBuilt = EnsureRelicNodes();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                string note = missingSources > 0
                    ? $"\n\nNOTE: {missingSources} base creature prefab(s) were missing.\n" +
                      "Run the enemy content steps first, then re-run this step."
                    : "";

                EditorUtility.DisplayDialog("Step 88 - Boss Relic Cores",
                    "Boss relics authored.\n\n" +
                    $"  {relicsCreated} relic item(s) created\n" +
                    $"  {bossesBuilt} boss variant prefab(s) in Resources/Enemies\n" +
                    $"  {nodesBuilt} relic-gated research node(s)\n\n" +
                    "Boss variants are SEPARATE prefabs - the ordinary creatures are\n" +
                    "completely untouched and still spawn as before.\n\n" +
                    "A relic is granted the moment its boss dies and is recorded\n" +
                    "permanently. Research checks that record rather than consuming\n" +
                    "the item, so one relic can gate several nodes and a unique boss\n" +
                    "never has to be killed twice." + note,
                    "OK");

                Debug.Log($"[Setup 88] Complete. {relicsCreated} relic(s), {bossesBuilt} boss(es), " +
                          $"{nodesBuilt} node(s).");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 88] Aborted: " + ex);
                EditorUtility.DisplayDialog("Boss Relics",
                    "Setup stopped: " + ex.Message + "\n\nNothing further was written.", "OK");
            }
        }

        // ============================================================
        //                       Relic items
        // ============================================================
        private static System.Collections.Generic.Dictionary<BossRelicKind, ItemDefinition>
            EnsureRelicItems(out int created)
        {
            created = 0;
            var map = new System.Collections.Generic.Dictionary<BossRelicKind, ItemDefinition>();

            foreach (var spec in Relics)
            {
                string id = BossRelicLedger.ItemIdFor(spec.Kind);
                string path = RelicFolder + "/Item_" + spec.Kind + ".asset";

                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                bool isNew = false;

                if (item == null)
                {
                    EnsureFolder(RelicFolder);
                    item = ScriptableObject.CreateInstance<ResourceItem>();
                    isNew = true;
                }

                bool dirty = isNew;
                if (item.itemId != id) { item.itemId = id; dirty = true; }
                if (item.displayName != spec.Display) { item.displayName = spec.Display; dirty = true; }
                // Relics are unique trophies, so they never stack into a pile.
                if (item.maxStack != 1) { item.maxStack = 1; dirty = true; }
                if (item.massPerUnit <= 0f) { item.massPerUnit = 2f; dirty = true; }
                if (item.category != "Relics") { item.category = "Relics"; dirty = true; }
                if (string.IsNullOrEmpty(item.description)) { item.description = spec.Description; dirty = true; }
                if (item.icon == null) item.iconTint = spec.Tint;

                if (dirty)
                {
                    if (!AssetDatabase.Contains(item)) AssetDatabase.CreateAsset(item, path);
                    EditorUtility.SetDirty(item);
                    if (isNew) { created++; Debug.Log("[Setup 88] Created " + path + "."); }
                }

                map[spec.Kind] = item;
            }

            return map;
        }

        // ============================================================
        //                      Boss variants
        // ============================================================
        private static int EnsureBossVariants(
            System.Collections.Generic.Dictionary<BossRelicKind, ItemDefinition> relicItems,
            out int missingSources)
        {
            int built = 0;
            missingSources = 0;

            foreach (var spec in Bosses)
            {
                string sourcePath = EnemyFolder + "/" + spec.SourcePrefab + ".prefab";
                string bossPath = EnemyFolder + "/" + spec.BossPrefab + ".prefab";

                var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if (source == null)
                {
                    missingSources++;
                    Debug.LogWarning("[Setup 88] Base creature not found: " + sourcePath + ".");
                    continue;
                }

                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(bossPath);
                if (existing != null)
                {
                    // Repair only: never restyle a boss the user may have tuned.
                    var contents = PrefabUtility.LoadPrefabContents(bossPath);
                    var encounter = contents.GetComponent<BossEncounter>();
                    bool dirty = false;

                    if (encounter == null)
                    {
                        encounter = contents.AddComponent<BossEncounter>();
                        encounter.tier = EnemyTier.Boss;
                        encounter.relic = spec.Relic;
                        encounter.healthMultiplier = spec.HealthMultiplier;
                        dirty = true;
                        Debug.Log("[Setup 88] " + spec.BossPrefab + " had no BossEncounter; added one.");
                    }
                    if (encounter.relicItem == null
                        && relicItems.TryGetValue(spec.Relic, out var repairItem))
                    {
                        encounter.relicItem = repairItem;
                        dirty = true;
                    }

                    if (dirty) PrefabUtility.SaveAsPrefabAsset(contents, bossPath);
                    PrefabUtility.UnloadPrefabContents(contents);
                    continue;
                }

                // Build the variant from an instance of the base creature, so it inherits
                // the authored mesh, colliders, AI and ordinary drops exactly.
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);

                instance.name = spec.BossPrefab;
                instance.transform.localScale = source.transform.localScale * spec.Scale;

                var boss = instance.AddComponent<BossEncounter>();
                boss.tier = EnemyTier.Boss;
                boss.relic = spec.Relic;
                boss.healthMultiplier = spec.HealthMultiplier;
                if (relicItems.TryGetValue(spec.Relic, out var item)) boss.relicItem = item;

                PrefabUtility.SaveAsPrefabAsset(instance, bossPath);
                UnityEngine.Object.DestroyImmediate(instance);

                built++;
                Debug.Log("[Setup 88] Created " + bossPath + " (" + spec.DisplayName + ").");
            }

            return built;
        }

        // ============================================================
        //                     Relic research
        // ============================================================
        private static int EnsureRelicNodes()
        {
            var tree = FindTree();
            if (tree == null)
            {
                Debug.LogWarning("[Setup 88] No ResearchTree found; relic nodes skipped.");
                return 0;
            }

            var sci3 = FindScience("Item_ScienceT3");
            int built = 0;

            // The roadmap names the Star Builder and Dyson Sphere as relic-gated. Both are
            // enormous orbital projects, so both sit behind the Sky Core and the orbital
            // lab - the relic proves the encounter, the lab proves the infrastructure.
            if (EnsureNode(tree, "relic_stellar_engineering", "Stellar Engineering",
                "Construction at stellar scale. Requires a Sky Core recovered from a Storm " +
                "Roc, and must be researched aboard an orbiting satellite laboratory.",
                tier: 8, column: 1, seconds: 360f, sci3, 40,
                BossRelicKind.SkyCore, requiresOrbitalLab: true)) built++;

            if (EnsureNode(tree, "relic_petrification", "Petrification Studies",
                "The Basilisk's gaze, understood and reproduced. Requires a Petrified Core.",
                tier: 7, column: 2, seconds: 240f, sci3, 22,
                BossRelicKind.PetrifiedCore, requiresOrbitalLab: false)) built++;

            if (EnsureNode(tree, "relic_forge_mastery", "Ember Forge Mastery",
                "Sustained temperatures no ordinary furnace can hold. Requires an Ember Core " +
                "cut from an Ifrit Sultan.",
                tier: 7, column: 3, seconds: 240f, sci3, 22,
                BossRelicKind.EmberCore, requiresOrbitalLab: false)) built++;

            return built;
        }

        private static bool EnsureNode(ResearchTree tree, string id, string name, string description,
            int tier, int column, float seconds, ScienceItem pack, int packCount,
            BossRelicKind relic, bool requiresOrbitalLab)
        {
            string path = NodeFolder + "/Research_" + id + ".asset";
            var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(path);
            bool created = false;

            if (node == null)
            {
                EnsureFolder(NodeFolder);
                node = ScriptableObject.CreateInstance<ResearchNode>();
                created = true;
            }

            bool dirty = created;
            if (node.nodeId != id) { node.nodeId = id; dirty = true; }
            if (created)
            {
                node.displayName = name;
                node.description = description;
                node.category = ResearchCategory.Environment;
                node.tier = tier;
                node.column = column;
                node.researchSeconds = seconds;
                node.maxRanks = 1;
                node.costScalesWithRank = false;
                if (pack != null && packCount > 0)
                {
                    node.cost = new[] { new ResearchNode.ScienceCost { pack = pack, count = packCount } };
                }
            }

            // The gates are repaired even on an existing node: they are the whole point of
            // this step, and a node missing its gate would be quietly free.
            if (node.requiresRelic != relic) { node.requiresRelic = relic; dirty = true; }
            if (requiresOrbitalLab && !node.requiresOrbitalLab)
            {
                node.requiresOrbitalLab = true;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(node)) AssetDatabase.CreateAsset(node, path);
                EditorUtility.SetDirty(node);
                Debug.Log("[Setup 88] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            if (!tree.nodes.Contains(node))
            {
                tree.nodes.Add(node);
                EditorUtility.SetDirty(tree);
                Debug.Log("[Setup 88] Added " + id + " to the research tree.");
            }

            return created;
        }

        // ============================================================
        //                        Helpers
        // ============================================================
        private static ResearchTree FindTree()
        {
            var guids = AssetDatabase.FindAssets("t:ResearchTree");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<ResearchTree>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static ScienceItem FindScience(string stem)
        {
            var guids = AssetDatabase.FindAssets(stem + " t:ScienceItem");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == stem)
                    return AssetDatabase.LoadAssetAtPath<ScienceItem>(p);
            }
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            var leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
