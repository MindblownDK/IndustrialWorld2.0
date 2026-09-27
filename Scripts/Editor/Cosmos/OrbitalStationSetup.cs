#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building.Tiered;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 89 (11.22.0-dev): the Orbital Station hammer family.
    ///
    /// Eight new build families - hull, deck, corridor, junction, viewport, airlock,
    /// dock and dome - each with the usual four tier prefabs, plus the
    /// `orbital_construction` research node that puts them on the hammer wheel.
    ///
    /// Non-destructive: existing definitions and prefabs keep every authored value and
    /// only missing links are repaired. Safe to re-run. Logs with the [Setup 89] prefix.
    /// </summary>
    public static class OrbitalStationSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string TieredFolder = Root + "/Tiered";
        private const string PrefabFolder = TieredFolder + "/Prefabs";
        private const string DefFolder = TieredFolder + "/Definitions";
        private const string MatFolder = TieredFolder + "/Materials";
        private const string NodeFolder = Root + "/Research/Nodes";

        // Habitat panelling rather than raw metal: the station set should read as a clean
        // pressurised interior, visually distinct from the rough structural tiers.
        private static readonly Color HullTint = new(0.82f, 0.84f, 0.87f);
        private static readonly Color TrimTint = new(0.30f, 0.38f, 0.46f);
        private static readonly Color GlassTint = new(0.46f, 0.72f, 0.88f);

        private readonly struct FamilySpec
        {
            public readonly BuildFamily Family;
            public readonly string Display;
            public readonly int Steel, Glass;
            public readonly Action<GameObject, Material, Material, Material> Build;

            public FamilySpec(BuildFamily family, string display, int steel, int glass,
                Action<GameObject, Material, Material, Material> build)
            {
                Family = family; Display = display; Steel = steel; Glass = glass; Build = build;
            }
        }

        public static void RunStep89()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Orbital Station", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = FindRegistry();
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Orbital Station",
                        "No TieredBlockRegistry found. Run the tiered building content step first.", "OK");
                    return;
                }

                var steel = FindItem("Item_SteelIngot");
                var glass = FindItem("Item_Glass") ?? FindItem("Item_Silicon");
                if (steel == null)
                {
                    EditorUtility.DisplayDialog("Orbital Station",
                        "Steel Ingot not found. Run the earlier crafting-content steps first.", "OK");
                    return;
                }

                var hullMat = MakeMat("Mat_StationHull", HullTint);
                var trimMat = MakeMat("Mat_StationTrim", TrimTint);
                var glassMat = MakeMat("Mat_StationGlass", GlassTint);

                var specs = Specs();
                int built = 0;
                foreach (var spec in specs)
                {
                    if (EnsureFamily(spec, registry, steel, glass, hullMat, trimMat, glassMat)) built++;
                }

                bool nodeCreated = EnsureResearchNode();

                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("Step 89 - Orbital Station Family",
                    "Orbital Station hammer family authored.\n\n" +
                    $"  {built} family/families written (of {specs.Length})\n" +
                    (nodeCreated ? "  Research node 'Orbital Construction' created\n" : "") +
                    "\nHULL  DECK  CORRIDOR  JUNCTION\n" +
                    "VIEWPORT  AIRLOCK  DOCK  DOME\n\n" +
                    "To use them:\n" +
                    "  1. Research Orbital Construction.\n" +
                    "  2. Hold the Hammer and open the build wheel.\n" +
                    "  3. Press TAB to switch to the ORBITAL STATION set.\n\n" +
                    "Station pieces only snap to other station pieces, so a\n" +
                    "pressure hull cannot be closed with a wooden wall.",
                    "OK");

                Debug.Log($"[Setup 89] Complete. {built} family/families written.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 89] Aborted: " + ex);
                EditorUtility.DisplayDialog("Orbital Station",
                    "Setup stopped: " + ex.Message + "\n\nNothing further was written.", "OK");
            }
        }

        private static FamilySpec[] Specs() => new[]
        {
            new FamilySpec(BuildFamily.StationHull, "StationHull", 6, 0, BuildHull),
            new FamilySpec(BuildFamily.StationFloor, "StationDeck", 4, 0, BuildDeck),
            new FamilySpec(BuildFamily.StationCorridor, "StationCorridor", 5, 0, BuildCorridor),
            new FamilySpec(BuildFamily.StationJunction, "StationJunction", 7, 0, BuildJunction),
            new FamilySpec(BuildFamily.StationWindow, "StationViewport", 5, 4, BuildViewport),
            new FamilySpec(BuildFamily.StationAirlock, "StationAirlock", 10, 0, BuildAirlock),
            new FamilySpec(BuildFamily.StationDock, "StationDock", 12, 0, BuildDock),
            new FamilySpec(BuildFamily.StationDome, "StationDome", 8, 6, BuildDome),
        };

        private static void Panel(GameObject root, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
        }

        private static void BuildHull(GameObject root, Material hull, Material trim, Material glass)
        {
            // A panel with structural ribs: reads as a pressure wall rather than a slab.
            Panel(root, "Panel", new Vector3(0f, 1.25f, 0f), new Vector3(2f, 2.5f, 0.16f), hull);
            Panel(root, "RibL", new Vector3(-0.9f, 1.25f, 0f), new Vector3(0.14f, 2.5f, 0.22f), trim);
            Panel(root, "RibR", new Vector3(0.9f, 1.25f, 0f), new Vector3(0.14f, 2.5f, 0.22f), trim);
            Panel(root, "Sill", new Vector3(0f, 0.08f, 0f), new Vector3(2f, 0.16f, 0.24f), trim);
        }

        private static void BuildDeck(GameObject root, Material hull, Material trim, Material glass)
        {
            Panel(root, "Deck", new Vector3(0f, 0.06f, 0f), new Vector3(2f, 0.12f, 2f), hull);
            Panel(root, "Channel", new Vector3(0f, 0.13f, 0f), new Vector3(0.3f, 0.03f, 2f), trim);
        }

        private static void BuildCorridor(GameObject root, Material hull, Material trim, Material glass)
        {
            // Open along Z so corridors chain into a tube.
            Panel(root, "WallL", new Vector3(-0.85f, 1.1f, 0f), new Vector3(0.16f, 2.2f, 2f), hull);
            Panel(root, "WallR", new Vector3(0.85f, 1.1f, 0f), new Vector3(0.16f, 2.2f, 2f), hull);
            Panel(root, "Ceiling", new Vector3(0f, 2.2f, 0f), new Vector3(1.86f, 0.14f, 2f), hull);
            Panel(root, "Deck", new Vector3(0f, 0.06f, 0f), new Vector3(1.86f, 0.12f, 2f), hull);
            Panel(root, "RibTop", new Vector3(0f, 2.28f, 0f), new Vector3(1.9f, 0.06f, 0.2f), trim);
        }

        private static void BuildJunction(GameObject root, Material hull, Material trim, Material glass)
        {
            // Open on all four sides: the piece that lets a station branch.
            Panel(root, "Deck", new Vector3(0f, 0.06f, 0f), new Vector3(2f, 0.12f, 2f), hull);
            Panel(root, "Ceiling", new Vector3(0f, 2.2f, 0f), new Vector3(2f, 0.14f, 2f), hull);
            Panel(root, "PillarNE", new Vector3(0.9f, 1.1f, 0.9f), new Vector3(0.2f, 2.2f, 0.2f), trim);
            Panel(root, "PillarNW", new Vector3(-0.9f, 1.1f, 0.9f), new Vector3(0.2f, 2.2f, 0.2f), trim);
            Panel(root, "PillarSE", new Vector3(0.9f, 1.1f, -0.9f), new Vector3(0.2f, 2.2f, 0.2f), trim);
            Panel(root, "PillarSW", new Vector3(-0.9f, 1.1f, -0.9f), new Vector3(0.2f, 2.2f, 0.2f), trim);
        }

        private static void BuildViewport(GameObject root, Material hull, Material trim, Material glass)
        {
            Panel(root, "FrameTop", new Vector3(0f, 2.3f, 0f), new Vector3(2f, 0.4f, 0.18f), hull);
            Panel(root, "FrameBottom", new Vector3(0f, 0.2f, 0f), new Vector3(2f, 0.4f, 0.18f), hull);
            Panel(root, "FrameL", new Vector3(-0.9f, 1.25f, 0f), new Vector3(0.2f, 2.5f, 0.18f), hull);
            Panel(root, "FrameR", new Vector3(0.9f, 1.25f, 0f), new Vector3(0.2f, 2.5f, 0.18f), hull);
            Panel(root, "Glass", new Vector3(0f, 1.25f, 0f), new Vector3(1.6f, 1.7f, 0.06f), glass);
        }

        private static void BuildAirlock(GameObject root, Material hull, Material trim, Material glass)
        {
            Panel(root, "Frame", new Vector3(0f, 1.25f, 0f), new Vector3(2f, 2.5f, 0.3f), hull);
            Panel(root, "Hatch", new Vector3(0f, 1.05f, 0.14f), new Vector3(1.1f, 1.8f, 0.12f), trim);
            Panel(root, "SealRing", new Vector3(0f, 1.05f, 0.2f), new Vector3(1.3f, 2f, 0.05f), trim);
        }

        private static void BuildDock(GameObject root, Material hull, Material trim, Material glass)
        {
            // An open collar. Deliberately not sealed: a ship mates into it.
            Panel(root, "CollarTop", new Vector3(0f, 2.1f, 0f), new Vector3(2.2f, 0.25f, 0.5f), hull);
            Panel(root, "CollarBottom", new Vector3(0f, 0.15f, 0f), new Vector3(2.2f, 0.25f, 0.5f), hull);
            Panel(root, "CollarL", new Vector3(-1.05f, 1.1f, 0f), new Vector3(0.25f, 2.2f, 0.5f), hull);
            Panel(root, "CollarR", new Vector3(1.05f, 1.1f, 0f), new Vector3(0.25f, 2.2f, 0.5f), hull);
            Panel(root, "Guide", new Vector3(0f, 1.1f, 0.3f), new Vector3(2.3f, 0.08f, 0.08f), trim);
        }

        private static void BuildDome(GameObject root, Material hull, Material trim, Material glass)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Dome";
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            sphere.transform.localScale = new Vector3(2f, 1.6f, 2f);
            var r = sphere.GetComponent<Renderer>();
            if (r != null && glass != null) r.sharedMaterial = glass;

            Panel(root, "Base", new Vector3(0f, 0.1f, 0f), new Vector3(2.1f, 0.2f, 2.1f), trim);
        }

        private static bool EnsureFamily(FamilySpec spec, TieredBlockRegistry registry,
            ItemDefinition steel, ItemDefinition glass,
            Material hullMat, Material trimMat, Material glassMat)
        {
            string defPath = DefFolder + "/TBlock_" + spec.Display + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<TieredBlockDefinition>(defPath);
            bool created = false;

            if (def == null)
            {
                EnsureFolder(DefFolder);
                def = ScriptableObject.CreateInstance<TieredBlockDefinition>();
                created = true;
            }

            if (created)
            {
                def.family = spec.Family;
                def.displayName = spec.Display;
                def.placeCost = Cost(steel, spec.Steel, glass, spec.Glass);

                // A station is already an end-game structure, so the tier ladder is flat:
                // the pieces exist at all four tiers for compatibility with the hammer,
                // but upgrading one is cheap rather than a second full build.
                def.woodToStone = Cost(steel, 2, null, 0);
                def.stoneToIron = Cost(steel, 2, null, 0);
                def.ironToSteel = Cost(steel, 2, null, 0);
            }

            EnsureFolder(PrefabFolder);
            for (int t = 0; t < 4; t++)
            {
                var tier = (BuildTier)t;
                string prefabPath = PrefabFolder + "/" + spec.Display + "_" + tier + ".prefab";

                if (AssetDatabase.LoadMainAssetAtPath(prefabPath) == null)
                {
                    var root = new GameObject(spec.Display + "_" + tier);
                    spec.Build(root, hullMat, trimMat, glassMat);

                    var collider = root.AddComponent<BoxCollider>();
                    collider.center = new Vector3(0f, 1.1f, 0f);
                    collider.size = new Vector3(2f, 2.4f, 2f);

                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    UnityEngine.Object.DestroyImmediate(root);
                    Debug.Log("[Setup 89] Created " + prefabPath + ".");
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                // Only fill a MISSING slot; an authored prefab override is never replaced.
                switch (tier)
                {
                    case BuildTier.Wood: if (def.woodPrefab == null) def.woodPrefab = prefab; break;
                    case BuildTier.Stone: if (def.stonePrefab == null) def.stonePrefab = prefab; break;
                    case BuildTier.Iron: if (def.ironPrefab == null) def.ironPrefab = prefab; break;
                    default: if (def.steelPrefab == null) def.steelPrefab = prefab; break;
                }
            }

            if (!AssetDatabase.Contains(def)) AssetDatabase.CreateAsset(def, defPath);
            EditorUtility.SetDirty(def);

            if (!registry.definitions.Contains(def))
            {
                registry.definitions.Add(def);
                Debug.Log("[Setup 89] Added " + spec.Display + " to the TieredBlockRegistry.");
            }

            return created;
        }

        private static TierCost Cost(ItemDefinition a, int ca, ItemDefinition b, int cb)
        {
            var list = new System.Collections.Generic.List<Ingredient>();
            if (a != null && ca > 0) list.Add(new Ingredient { item = a, count = ca });
            if (b != null && cb > 0) list.Add(new Ingredient { item = b, count = cb });
            return new TierCost { items = list.ToArray() };
        }

        private static bool EnsureResearchNode()
        {
            var tree = FindTree();
            if (tree == null)
            {
                Debug.LogWarning("[Setup 89] No ResearchTree found; the node was skipped.");
                return false;
            }

            const string id = "orbital_construction";
            string path = NodeFolder + "/Research_" + id + ".asset";
            var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(path);
            bool created = false;

            if (node == null)
            {
                EnsureFolder(NodeFolder);
                node = ScriptableObject.CreateInstance<ResearchNode>();
                created = true;

                node.nodeId = id;
                node.displayName = "Orbital Construction";
                node.description =
                    "Pressurised habitat construction. Adds the Orbital Station family to the " +
                    "Hammer wheel: hull, decks, corridors, junctions, viewports, airlocks, " +
                    "docking collars and observation domes.";
                node.category = ResearchCategory.Environment;
                node.tier = 6;
                node.column = 2;
                node.researchSeconds = 200f;
                node.maxRanks = 1;
                node.costScalesWithRank = false;

                var sci3 = FindScience("Item_ScienceT3");
                if (sci3 != null)
                {
                    node.cost = new[] { new ResearchNode.ScienceCost { pack = sci3, count = 18 } };
                }

                AssetDatabase.CreateAsset(node, path);
                EditorUtility.SetDirty(node);
                Debug.Log("[Setup 89] Created " + path + ".");
            }

            // The id is load-bearing: HammerBuildWheel matches on it to decide whether the
            // station group is available, so a renamed node would silently lock the family.
            if (node.nodeId != id)
            {
                node.nodeId = id;
                EditorUtility.SetDirty(node);
                Debug.Log("[Setup 89] Repaired the node id to '" + id + "'.");
            }

            if (!tree.nodes.Contains(node))
            {
                tree.nodes.Add(node);
                EditorUtility.SetDirty(tree);
                Debug.Log("[Setup 89] Added " + id + " to the research tree.");
            }

            return created;
        }

        private static TieredBlockRegistry FindRegistry()
        {
            var guids = AssetDatabase.FindAssets("t:TieredBlockRegistry");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<TieredBlockRegistry>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static ResearchTree FindTree()
        {
            var guids = AssetDatabase.FindAssets("t:ResearchTree");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<ResearchTree>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static ItemDefinition FindItem(string stem)
        {
            var guids = AssetDatabase.FindAssets(stem + " t:ItemDefinition");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == stem)
                    return AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
            }
            return null;
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

        private static Material MakeMat(string name, Color c)
        {
            EnsureFolder(MatFolder);
            string path = MatFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                Debug.LogError("[Setup 89] Preserved conflicting asset at '" + path + "'.");
                return null;
            }
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(sh) { name = name, color = c };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
#endif
