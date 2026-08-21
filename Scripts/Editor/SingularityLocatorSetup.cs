// Assets/Scripts/VoxelEngine/Editor/SingularityLocatorSetup.cs
//
// Step 55 (Phase 5): ASTRAL NAVIGATOR — navigation grid block authoring.
// Non-destructive creation of:
//
//   • The Astral Navigator grid block prefab (dish + emitter + glowing ring)
//   • Its grid block item
//   • Assembler recipe
//   • "Astral Navigator" research node (tier 6, Logistics) — the early-game
//     bridge to interplanetary + deep-space travel
//
// Re-runnable. Idempotent. Existing authored balance is preserved; only
// missing content is created and broken links are re-wired.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    public static class SingularityLocatorSetup
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";
        private const string GRID_ROOT  = ASSET_ROOT + "/GridSystem";
        private const string ITEMS      = GRID_ROOT + "/Items";
        private const string PREFABS    = GRID_ROOT + "/Prefabs";
        private const string MATS       = PREFABS + "/Mats";
        private const string RECIPES    = GRID_ROOT + "/Recipes";
        private const string NODES      = ASSET_ROOT + "/Research/Nodes";

        public static void RunStep55()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 55 — Astral Navigator started.");

            foreach (var f in new[] { GRID_ROOT, ITEMS, PREFABS, MATS, RECIPES }) EnsureFolder(f);

            // ── Prefab (non-destructive) ───────────────────────────
            string prefabPath = PREFABS + "/Prefab_StarLocator.prefab";
            var prefab = GetOrCreatePrefab(prefabPath, "Prefab_StarLocator", (root) =>
            {
                var locator = root.GetComponent<GridLocatorBlock>();
                if (locator == null) locator = root.AddComponent<GridLocatorBlock>();

                locator.blockName = "Astral Navigator";
                if (locator.powerDrawWatts <= 0f) locator.powerDrawWatts = 6000f;
                if (locator.waypointMarkerSize <= 0f) locator.waypointMarkerSize = 1.4f;

                if (root.transform.childCount == 0 && root.GetComponent<MeshFilter>() == null)
                    BuildLocatorVisuals(root);

                var bcol = root.GetComponent<BoxCollider>();
                if (bcol == null) bcol = root.AddComponent<BoxCollider>();
                bcol.size = Vector3.one * GridSizeExt.CellSize(GridSize.Large);
            });

            // ── Item ───────────────────────────────────────────────
            var item = GetOrCreateAsset<GridBlockItem>(ITEMS + "/GItem_StarLocator.asset");
            item.itemId = "gitem_starlocator";
            item.displayName = "Astral Navigator";
            if (string.IsNullOrEmpty(item.description))
                item.description = "A powered navigation block that pinpoints any celestial body — planets, moons, the sun, the black hole and the quasar. Projects a real waypoint marker to fly toward; aim at the marker and engage the warp drive to jump straight to the target.";
            item.iconTint = new Color(0.20f, 0.85f, 0.95f);
            if (item.maxStack <= 0) item.maxStack = 20;
            item.gridSize = GridSize.Large;
            item.blockPrefab = prefab;
            if (item.blockMass <= 0f) item.blockMass = 900f;
            if (item.blockHP <= 0f) item.blockHP = 1400f;
            item.category = "Grid Blocks";
            EditorUtility.SetDirty(item);

            // ── Recipe (created fully when missing; inputs preserved when authored) ──
            var steelPlate = LoadItem(ASSET_ROOT + "/Industrial/Items/Item_SteelPlate.asset");
            var circuit    = LoadItem(ASSET_ROOT + "/Industrial/Items/Item_Circuit.asset");
            var glass      = LoadItem(ASSET_ROOT + "/Industrial/Items/Item_Glass.asset");
            var platinum   = LoadItem(ASSET_ROOT + "/Items/Item_Platinum.asset");

            var recipe = GetOrCreateAsset<RecipeDefinition>(RECIPES + "/Recipe_GStarLocator.asset");
            recipe.displayName = "Astral Navigator";
            recipe.outputItem = item;
            recipe.outputCount = 1;
            recipe.requiredStation = StationTier.Assembler;
            if (recipe.craftSeconds <= 0f) recipe.craftSeconds = 25f;
            recipe.unlockedByDefault = false;
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var inputs = new List<RecipeIngredient>();
                if (steelPlate != null) inputs.Add(new RecipeIngredient { item = steelPlate, count = 20 });
                if (circuit != null)    inputs.Add(new RecipeIngredient { item = circuit, count = 8 });
                if (glass != null)      inputs.Add(new RecipeIngredient { item = glass, count = 4 });
                if (platinum != null)   inputs.Add(new RecipeIngredient { item = platinum, count = 2 });
                recipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(recipe);

            var recipeRegistry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            if (recipeRegistry != null && !recipeRegistry.recipes.Contains(recipe))
            {
                recipeRegistry.recipes.Add(recipe);
                EditorUtility.SetDirty(recipeRegistry);
            }

            // ── Research (tier 6, Logistics — the early travel bridge) ──
            var sciT2 = LoadItem(ASSET_ROOT + "/Items/Item_ScienceT2.asset");
            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(ASSET_ROOT + "/Research/ResearchTree.asset");
            if (tree != null)
            {
                var node = FindNode(tree, "res_starlocator");
                if (node == null)
                {
                    node = ScriptableObject.CreateInstance<ResearchNode>();
                    node.nodeId = "res_starlocator";
                    node.displayName = "Astral Navigator";
                    node.description = "Unlocks the Astral Navigator grid block — pinpoints planets, moons, the sun, the black hole and the quasar, and projects a warp-lockable waypoint. The bridge to real interplanetary and deep-space travel.";
                    node.category = ResearchCategory.Environment;
                    node.subCategory = ResearchSubCategory.Logistics;
                    node.tier = 6;
                    node.column = 5;
                    node.iconTint = new Color(0.20f, 0.85f, 0.95f);
                    node.researchSeconds = 600f;
                    node.cost = new[]
                    {
                        new ResearchNode.ScienceCost { pack = sciT2 as ScienceItem, count = 60 },
                    };
                    AssetDatabase.CreateAsset(node, NODES + "/res_starlocator.asset");
                    tree.nodes.Add(node);
                }
                node.unlocksRecipes = new[] { recipe };
                EditorUtility.SetDirty(node);
                EditorUtility.SetDirty(tree);
                ResearchRecipeLinker.Register("res_starlocator", recipe);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Astral Navigator",
                "Astral Navigator wired (non-destructive):\n\n" +
                "• Grid block: Astral Navigator (powered navigation — 6 kW)\n" +
                "• AUTO tracks the nearest body; SPECIFIC locks a target (panel ◀ ▶)\n" +
                "• Projects a true waypoint marker; aim at it + warp drive = jump\n" +
                "• Targets: black hole, quasar, sun, every planet and moon\n" +
                "• Research: Astral Navigator (tier 6, Logistics)",
                "OK");
        }

        // ── Visual build (prefab content only) ─────────────────────
        private static void BuildLocatorVisuals(GameObject root)
        {
            Material baseMat  = MakeColoredMat("Mat_LocatorBase", new Color(0.10f, 0.12f, 0.15f), emissive: false, metallic: 0.7f);
            Material ringMat  = MakeColoredMat("Mat_LocatorRing", new Color(0.20f, 0.85f, 0.95f), emissive: true, metallic: 0.4f);
            Material dishMat  = MakeColoredMat("Mat_LocatorDish", new Color(0.55f, 0.65f, 0.75f), emissive: false, metallic: 0.9f);
            Material tipMat   = MakeColoredMat("Mat_LocatorTip", new Color(0.30f, 0.95f, 1.0f), emissive: true, metallic: 0.4f);

            // Base pedestal.
            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pedestal.name = "Base";
            pedestal.transform.SetParent(root.transform, false);
            pedestal.transform.localScale = new Vector3(1.30f, 0.22f, 1.30f);
            pedestal.transform.localPosition = new Vector3(0f, -0.85f, 0f);
            pedestal.GetComponent<Renderer>().sharedMaterial = baseMat;
            Object.DestroyImmediate(pedestal.GetComponent<Collider>());

            // Radar dish (upside-down cone reads as a dish; squash it flat).
            var dish = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dish.name = "Dish";
            dish.transform.SetParent(root.transform, false);
            dish.transform.localScale = new Vector3(0.90f, 0.10f, 0.90f);
            dish.transform.localPosition = new Vector3(0f, -0.62f, 0f);
            dish.GetComponent<Renderer>().sharedMaterial = dishMat;
            Object.DestroyImmediate(dish.GetComponent<Collider>());

            // Emitter column.
            var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            column.name = "EmitterColumn";
            column.transform.SetParent(root.transform, false);
            column.transform.localScale = new Vector3(0.12f, 0.36f, 0.12f);
            column.transform.localPosition = new Vector3(0f, -0.38f, 0f);
            column.GetComponent<Renderer>().sharedMaterial = baseMat;
            Object.DestroyImmediate(column.GetComponent<Collider>());

            // Emitter tip.
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "EmitterTip";
            tip.transform.SetParent(root.transform, false);
            tip.transform.localScale = Vector3.one * 0.16f;
            tip.transform.localPosition = new Vector3(0f, -0.18f, 0f);
            tip.GetComponent<Renderer>().sharedMaterial = tipMat;
            Object.DestroyImmediate(tip.GetComponent<Collider>());

            // Glowing nav ring around the pedestal.
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "NavRing";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localScale = new Vector3(1.44f, 0.04f, 1.44f);
            ring.transform.localPosition = new Vector3(0f, -0.70f, 0f);
            ring.GetComponent<Renderer>().sharedMaterial = ringMat;
            Object.DestroyImmediate(ring.GetComponent<Collider>());
        }

        // ── Helpers ────────────────────────────────────────────────
        private static ItemDefinition LoadItem(string path)
            => AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

        private static ResearchNode FindNode(ResearchTree tree, string id)
        {
            if (tree == null || tree.nodes == null) return null;
            for (int i = 0; i < tree.nodes.Count; i++)
                if (tree.nodes[i] != null && tree.nodes[i].nodeId == id) return tree.nodes[i];
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

        private static T GetOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static Material MakeColoredMat(string name, Color c, bool emissive, float metallic)
        {
            string path = MATS + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.5f);
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", c * 1.6f);
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static GameObject GetOrCreatePrefab(string path, string name, System.Action<GameObject> onUpdate)
        {
            GameObject root = null;
            bool loadedPrefabContents = false;
            try
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    try
                    {
                        root = PrefabUtility.LoadPrefabContents(path);
                        loadedPrefabContents = true;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[SingularityLocatorSetup] Could not load prefab contents at '{path}'. " +
                                         $"The asset will be recreated. Unity said: {ex.Message}");
                        AssetDatabase.DeleteAsset(path);
                    }
                }
                if (root == null) root = new GameObject(name);

                onUpdate?.Invoke(root);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                if (root != null)
                {
                    if (loadedPrefabContents) PrefabUtility.UnloadPrefabContents(root);
                    else Object.DestroyImmediate(root);
                }
            }
        }
    }
}
#endif
