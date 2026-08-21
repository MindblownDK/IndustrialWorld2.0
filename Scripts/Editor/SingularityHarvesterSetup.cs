// Assets/Scripts/VoxelEngine/Editor/SingularityHarvesterSetup.cs
//
// Step 53 (Phase 5): SINGULARITY HARVESTER — the grid block that turns the black
// hole into a resource node. Non-destructive authoring of:
//
//   • Singularity Matter — the harvested endgame resource (future Star Crafter fuel)
//   • Prefab_SingularityHarvester — a grand Large grid block with a contained mini
//     black hole (procedural visuals: frame cage, glowing containment rings, spinning
//     accretion disc around a lensed horizon sphere)
//   • GItem_SingularityHarvester — Grid Blocks catalogue entry
//   • Recipe_GSingularityHarvester — expensive Assembler recipe
//   • res_singularityharvester — tier-8 research node after Warp Drive
//
// Re-runnable. Idempotent. Existing authored balance (power draw, rates, recipe
// inputs, research costs) is preserved; only missing content is created and broken
// links are re-wired.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    public static class SingularityHarvesterSetup
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";
        private const string GRID_ROOT  = ASSET_ROOT + "/GridSystem";
        private const string ITEMS      = GRID_ROOT + "/Items";
        private const string PREFABS    = GRID_ROOT + "/Prefabs";
        private const string MATS       = PREFABS + "/Mats";
        private const string RECIPES    = GRID_ROOT + "/Recipes";
        private const string NODES      = ASSET_ROOT + "/Research/Nodes";

        public static void RunStep53()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 53 — Singularity Harvester started.");

            foreach (var f in new[] { GRID_ROOT, ITEMS, PREFABS, MATS, RECIPES }) EnsureFolder(f);

            // ── The harvested resource ─────────────────────────────
            ResourceItem matter = EnsureSingularityMatter();

            // ── Dependencies (existing setup-owned assets) ─────────
            var steelPlate = LoadItem(ASSET_ROOT + "/Industrial/Items/Item_SteelPlate.asset");
            var advCircuit = LoadItem(ASSET_ROOT + "/Industrial/Items/Item_AdvCircuit.asset");
            var uranium    = LoadItem(ASSET_ROOT + "/Items/Item_Uranium.asset");
            var lithium    = LoadItem(ASSET_ROOT + "/Industrial/Items/Item_Lithium.asset");
            var platinum   = LoadItem(ASSET_ROOT + "/Items/Item_Platinum.asset");
            var sciT2      = LoadItem(ASSET_ROOT + "/Items/Item_ScienceT2.asset");
            var sciT3      = LoadItem(ASSET_ROOT + "/Items/Item_ScienceT3.asset");

            // ── Prefab (non-destructive: existing tuning/visuals preserved) ──
            string prefabPath = PREFABS + "/Prefab_SingularityHarvester.prefab";
            var prefab = GetOrCreatePrefab(prefabPath, "Prefab_SingularityHarvester", (root) =>
            {
                var harvester = root.GetComponent<GridSingularityHarvester>();
                if (harvester == null) harvester = root.AddComponent<GridSingularityHarvester>();

                // Identity + links are ALWAYS re-wired; numbers only when at defaults
                // so designer balance tweaks survive re-runs.
                harvester.blockName = "Singularity Harvester";
                if (harvester.producedItem == null) harvester.producedItem = matter;
                if (harvester.harvestRangeKm <= 0f) harvester.harvestRangeKm = 2500f;
                if (harvester.harvestRatePerSecond <= 0f) harvester.harvestRatePerSecond = 0.06f;
                if (harvester.powerDrawWatts <= 0f) harvester.powerDrawWatts = 25000f;
                if (harvester.quasarMultiplier <= 0f) harvester.quasarMultiplier = 1.5f;

                // Visuals are rebuilt when the prefab has no visual yet, or when it
                // still carries the original small generated frame (9.14.0 grand upgrade) —
                // designer-customised geometry survives re-runs.
                bool hasVisual = root.transform.childCount > 0 || root.GetComponent<MeshFilter>() != null;
                var oldFrame = root.transform.Find("FrameBase");
                bool hasOldGenerated = oldFrame != null && oldFrame.localScale.x < 1.9f;
                if (!hasVisual || hasOldGenerated)
                {
                    var children = new System.Collections.Generic.List<Transform>();
                    foreach (Transform child in root.transform) children.Add(child);
                    foreach (var child in children) Object.DestroyImmediate(child.gameObject);
                    BuildHarvesterVisuals(root);
                }

                var bcol = root.GetComponent<BoxCollider>();
                if (bcol == null) bcol = root.AddComponent<BoxCollider>();
                bcol.size = Vector3.one * GridSizeExt.CellSize(GridSize.Large);
            });

            // ── Item (connect identity always; balance only when default) ──
            string itemPath = ITEMS + "/GItem_SingularityHarvester.asset";
            var item = GetOrCreateAsset<GridBlockItem>(itemPath);
            item.itemId = "gitem_singularityharvester";
            item.displayName = "Singularity Harvester";
            if (string.IsNullOrEmpty(item.description))
                item.description = "A containment-grade grid block that harvests Singularity Matter straight from the event horizon. Yield climbs the closer you dare park to the black hole — the quasar pays 1.5× but its jets shear ships apart. Requires vacuum, heavy grid power, and cargo space.";
            item.iconTint = new Color(0.48f, 0.30f, 0.85f);
            if (item.maxStack <= 0) item.maxStack = 20;
            item.gridSize = GridSize.Large;
            item.blockPrefab = prefab;
            if (item.blockMass <= 0f) item.blockMass = 3200f;
            if (item.blockHP <= 0f) item.blockHP = 3200f;
            item.category = "Grid Blocks";
            EditorUtility.SetDirty(item);

            // ── Recipe (created fully when missing; inputs preserved when authored) ──
            string recipePath = RECIPES + "/Recipe_GSingularityHarvester.asset";
            var recipe = GetOrCreateAsset<RecipeDefinition>(recipePath);
            recipe.displayName = "Singularity Harvester";
            recipe.outputItem = item;
            recipe.outputCount = 1;
            recipe.requiredStation = StationTier.Assembler;
            if (recipe.craftSeconds <= 0f) recipe.craftSeconds = 45f;
            recipe.unlockedByDefault = false;
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var inputs = new List<RecipeIngredient>();
                if (steelPlate != null) inputs.Add(new RecipeIngredient { item = steelPlate, count = 60 });
                if (advCircuit != null) inputs.Add(new RecipeIngredient { item = advCircuit, count = 20 });
                if (uranium != null)    inputs.Add(new RecipeIngredient { item = uranium, count = 12 });
                if (lithium != null)    inputs.Add(new RecipeIngredient { item = lithium, count = 8 });
                if (platinum != null)   inputs.Add(new RecipeIngredient { item = platinum, count = 10 });
                recipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(recipe);

            var recipeRegistry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            if (recipeRegistry != null && !recipeRegistry.recipes.Contains(recipe))
            {
                recipeRegistry.recipes.Add(recipe);
                EditorUtility.SetDirty(recipeRegistry);
            }

            // ── Research (created fully when missing; unlocks always re-wired) ──
            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(ASSET_ROOT + "/Research/ResearchTree.asset");
            if (tree != null)
            {
                var node = FindNode(tree, "res_singularityharvester");
                if (node == null)
                {
                    node = ScriptableObject.CreateInstance<ResearchNode>();
                    node.nodeId = "res_singularityharvester";
                    node.displayName = "Singularity Harvester";
                    node.description = "Unlocks the Singularity Harvester grid block — containment tech that draws Singularity Matter from a real black hole's horizon. The future Star Crafter will hunger for it.";
                    node.category = ResearchCategory.Environment;
                    node.subCategory = ResearchSubCategory.Building;
                    node.tier = 8;
                    node.column = 7;
                    node.iconTint = new Color(0.48f, 0.30f, 0.85f);
                    node.researchSeconds = 1200f;
                    node.cost = new[]
                    {
                        new ResearchNode.ScienceCost { pack = sciT3 as ScienceItem, count = 100 },
                        new ResearchNode.ScienceCost { pack = sciT2 as ScienceItem, count = 150 },
                    };
                    var warp = FindNode(tree, "res_warpdrive");
                    if (warp != null) node.prerequisites = new[] { warp };
                    AssetDatabase.CreateAsset(node, NODES + "/res_singularityharvester.asset");
                    tree.nodes.Add(node);
                }
                node.unlocksRecipes = new[] { recipe };
                EditorUtility.SetDirty(node);
                EditorUtility.SetDirty(tree);
                ResearchRecipeLinker.Register("res_singularityharvester", recipe);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Singularity Harvester",
                "Singularity Harvester wired (non-destructive):\n\n" +
                "• Resource: Singularity Matter (stackable, endgame)\n" +
                "• Prefab: " + prefabPath + "\n" +
                "• Item: GItem_SingularityHarvester (Grid Blocks)\n" +
                "• Recipe: 60 Steel Plate + 20 Advanced Circuit + 12 Uranium + 8 Lithium + 10 Platinum @ Assembler\n" +
                "• Research: Singularity Harvester (tier 8) after Warp Drive\n\n" +
                "In-game: build it on a powered ship, fly within 2,500 km of the black hole's horizon — the closer you park, the faster it harvests. Connect cargo containers to collect the Singularity Matter (auto-pushes). The quasar yields 1.5×.\n\n" +
                "NOTE: Ensure the system template has its black hole enabled (Step 52).",
                "OK");
        }

        // ── Resource: Singularity Matter ───────────────────────────
        private static ResourceItem EnsureSingularityMatter()
        {
            var matter = GetOrCreateAsset<ResourceItem>(ASSET_ROOT + "/Items/Item_SingularityMatter.asset");
            matter.itemId = "item_singularity_matter";
            matter.displayName = "Singularity Matter";
            if (string.IsNullOrEmpty(matter.description))
                matter.description = "Exotic matter skimmed from a real event horizon. The densest resource known — reserved for the highest-tier constructions, including the future Star Crafter (planet & star system authoring).";
            matter.iconTint = new Color(0.55f, 0.22f, 0.78f);
            if (matter.maxStack <= 0) matter.maxStack = 999;
            if (matter.massPerUnit <= 0f) matter.massPerUnit = 2.5f;
            matter.category = "Resources";
            matter.subcategory = ResourceCategory.Misc;
            matter.fuelSeconds = 0f;
            EditorUtility.SetDirty(matter);
            return matter;
        }

        // ── Visual build (prefab content only) ─────────────────────
        private static void BuildHarvesterVisuals(GameObject root)
        {
            Material frameMat = MakeColoredMat("Mat_HarvesterFrame", new Color(0.085f, 0.09f, 0.11f), emissive: false);
            Material coilMat  = MakeColoredMat("Mat_HarvesterCoil", new Color(0.20f, 0.65f, 0.90f), emissive: true);
            Material tipMat   = MakeColoredMat("Mat_HarvesterTip", new Color(1.0f, 0.55f, 0.14f), emissive: true);

            // Contained singularity shaders (shipped with the Phase 5 code).
            Material horizonMat = MakeShaderMat("Mat_HarvesterHorizon", "VoxelEngine/SingularityHorizon");
            Material discMat = MakeShaderMat("Mat_HarvesterDisc", "VoxelEngine/BlackHoleAccretionDisc");
            if (discMat != null)
            {
                discMat.SetColor("_CoreColor", new Color(1.0f, 0.92f, 0.78f));
                discMat.SetColor("_MidColor", new Color(0.95f, 0.45f, 0.18f));
                discMat.SetColor("_OuterColor", new Color(0.55f, 0.10f, 0.06f));
                discMat.SetFloat("_Brightness", 1.5f);
            }

            // ── 9.14.0 GRAND build: a full-cell monument (~2.2 m frame) ──
            // Base + top plates.
            var basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePlate.name = "FrameBase";
            basePlate.transform.SetParent(root.transform, false);
            basePlate.transform.localScale = new Vector3(2.20f, 0.18f, 2.20f);
            basePlate.transform.localPosition = new Vector3(0f, -0.72f, 0f);
            basePlate.GetComponent<Renderer>().sharedMaterial = frameMat;
            Object.DestroyImmediate(basePlate.GetComponent<Collider>());

            var topPlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            topPlate.name = "FrameTop";
            topPlate.transform.SetParent(root.transform, false);
            topPlate.transform.localScale = new Vector3(2.20f, 0.18f, 2.20f);
            topPlate.transform.localPosition = new Vector3(0f, 0.72f, 0f);
            topPlate.GetComponent<Renderer>().sharedMaterial = frameMat;
            Object.DestroyImmediate(topPlate.GetComponent<Collider>());

            // Four corner pillars.
            foreach (var corner in new[] { new Vector3(-1.00f, 0f, -1.00f), new Vector3(1.00f, 0f, -1.00f),
                                           new Vector3(-1.00f, 0f, 1.00f), new Vector3(1.00f, 0f, 1.00f) })
            {
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pillar.name = "Pillar";
                pillar.transform.SetParent(root.transform, false);
                pillar.transform.localScale = new Vector3(0.18f, 1.44f, 0.18f);
                pillar.transform.localPosition = corner;
                pillar.GetComponent<Renderer>().sharedMaterial = frameMat;
                Object.DestroyImmediate(pillar.GetComponent<Collider>());
            }

            // The contained event horizon — pure black sphere with a lensed rim.
            var horizon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            horizon.name = "SingularityHorizon";
            horizon.transform.SetParent(root.transform, false);
            horizon.transform.localScale = Vector3.one * 0.74f;
            horizon.GetComponent<Renderer>().sharedMaterial = horizonMat;
            Object.DestroyImmediate(horizon.GetComponent<Collider>());

            // Accretion disc — spins at runtime via GridSingularityHarvester.
            var disc = new GameObject("SingularityDisc");
            disc.transform.SetParent(root.transform, false);
            disc.transform.localRotation = Quaternion.Euler(72f, 0f, 0f);
            disc.transform.localScale = Vector3.one * 1.62f;
            var discMF = disc.AddComponent<MeshFilter>();
            discMF.sharedMesh = CreateDiscAnnulus(48);
            var discMR = disc.AddComponent<MeshRenderer>();
            discMR.sharedMaterial = discMat;
            discMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            discMR.receiveShadows = false;

            // Glowing containment coils around the core.
            for (int i = 0; i < 2; i++)
            {
                var coil = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                coil.name = "CoilRing";
                coil.transform.SetParent(root.transform, false);
                coil.transform.localScale = new Vector3(1.88f, 0.06f, 1.88f);
                coil.transform.localPosition = new Vector3(0f, i == 0 ? -0.14f : 0.14f, 0f);
                coil.transform.localRotation = Quaternion.Euler(90f + (i == 0 ? 12f : -12f), 0f, 0f);
                coil.GetComponent<Renderer>().sharedMaterial = coilMat;
                Object.DestroyImmediate(coil.GetComponent<Collider>());
            }

            // Amber collector tips on the base plate (energy conduits).
            foreach (var corner in new[] { new Vector3(-0.64f, -0.60f, -0.64f), new Vector3(0.64f, -0.60f, -0.64f),
                                           new Vector3(-0.64f, -0.60f, 0.64f), new Vector3(0.64f, -0.60f, 0.64f) })
            {
                var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                tip.name = "CollectorTip";
                tip.transform.SetParent(root.transform, false);
                tip.transform.localScale = Vector3.one * 0.17f;
                tip.transform.localPosition = corner;
                tip.GetComponent<Renderer>().sharedMaterial = tipMat;
                Object.DestroyImmediate(tip.GetComponent<Collider>());
            }
        }

        // Flat annulus with polar UVs for the BlackHoleAccretionDisc shader (x = radius, y = angle).
        private static Mesh CreateDiscAnnulus(int segments)
        {
            const float InnerFraction = 0.30f;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int i = 0; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                verts.Add(new Vector3(ca * InnerFraction, 0f, sa * InnerFraction));
                verts.Add(new Vector3(ca, 0f, sa));
                uvs.Add(new Vector2(0f, i / (float)segments));
                uvs.Add(new Vector2(1f, i / (float)segments));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.Add(a); tris.Add(b); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(c);
            }

            var mesh = new Mesh { name = "SingularityHarvesterDiscMesh" };
            mesh.vertices = verts.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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

        private static Material MakeColoredMat(string name, Color c, bool emissive)
        {
            string path = MATS + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.65f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.45f);
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", c * 1.8f);
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Material MakeShaderMat(string name, string shaderName)
        {
            string path = MATS + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[SingularityHarvesterSetup] Shader '{shaderName}' not found — using URP/Lit fallback.");
                shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }
            var mat = new Material(shader) { name = name };
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
                        Debug.LogWarning($"[SingularityHarvesterSetup] Could not load prefab contents at '{path}'. " +
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
