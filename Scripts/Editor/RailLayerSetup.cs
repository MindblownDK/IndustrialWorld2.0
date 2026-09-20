#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 93 (11.33.0-dev): the Rail Layer — Train System v2, phase 3.
    ///
    /// A carried tool that lays whole rail runs by dragging, at a chosen gauge, instead of
    /// placing hundreds of cells by hand. Reuses the road system's corridor solver, so
    /// curves and multi-track widths come from code that is already proven.
    ///
    /// Non-destructive: an existing tool asset or recipe keeps every authored value and
    /// only missing links are repaired. Safe to re-run. Logs with the [Setup 93] prefix.
    /// </summary>
    public static class RailLayerSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string ItemsFolder = Root + "/Items";
        private const string RecipesFolder = Root + "/Recipes";

        private static readonly Color ToolTint = new(0.58f, 0.62f, 0.68f);

        public static void RunStep93()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Rail Layer", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Rail Layer",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.",
                        "OK");
                    return;
                }

                var steel = FindItem("Item_SteelIngot");
                var wire = FindItem("Item_CopperWire");
                if (steel == null)
                {
                    EditorUtility.DisplayDialog("Rail Layer",
                        "Steel Ingot not found. Run the earlier crafting-content steps first.", "OK");
                    return;
                }

                // The tool must lay the SAME block the player places by hand, or a run and a
                // hand-laid cell would be two different things that happen to look alike.
                var trackBlock = FindBlock("Block_RailTrack");
                if (trackBlock == null)
                {
                    EditorUtility.DisplayDialog("Rail Layer",
                        "Rail Track block not found.\n\nRun step 85 (Build the Rail System) first, " +
                        "then re-run this step.", "OK");
                    return;
                }

                var stone = FindItem("Item_Stone");
                if (stone == null)
                    Debug.LogWarning("[Setup 93] Stone item not found; the ballast bed will be free " +
                                     "until the base crafting content step has been run.");

                // The rail ITEM the player crafts. `BlockItem` derives from `ItemDefinition`,
                // and step 85 authors rail track as a BlockItem, so the block IS the item -
                // which is exactly what we want: a laid run consumes the same Rail Track the
                // player would have placed by hand.
                ItemDefinition trackItem = trackBlock;

                var ballast = EnsureBallastBlock(stone, out bool ballastChanged);
                var tool = EnsureTool(trackBlock, trackItem, stone, ballast, out bool toolChanged);
                toolChanged |= ballastChanged;
                bool recipeChanged = EnsureRecipe(registry, tool, steel, wire);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("Step 93 - Rail Layer",
                    "Rail Layer authored.\n\n" +
                    "  RAIL LAYER   Steel x25" + (wire != null ? " + Wire x15" : "") + "\n\n" +
                    "Costs per cell laid:\n" +
                    "  1 x Rail Track  +  2 x Stone (ballast bed)\n" +
                    "The tool saves effort, not materials - a laid run costs\n" +
                    "the same as laying it by hand.\n\n" +
                    "How to use it:\n" +
                    "  1. Hold the Rail Layer.\n" +
                    "  2. Click once where the run should start.\n" +
                    "  3. Aim at the far end and click again to lay it.\n" +
                    "  Right-click cancels a run in progress.\n" +
                    "  Ctrl + scroll picks the gauge (1-3 parallel tracks).\n\n" +
                    "Curves are solved automatically. A run that is too steep,\n" +
                    "or a corner too tight for the chosen gauge, is REFUSED with\n" +
                    "the reason rather than laid as track no train can use.\n\n" +
                    "It lays the same Rail Track block you place by hand, so runs\n" +
                    "and hand-laid cells join up normally.\n\n" +
                    ((toolChanged || recipeChanged)
                        ? "Changes were written. See the Console."
                        : "Everything was already in place."),
                    "OK");

                Debug.Log("[Setup 93] Rail Layer setup complete.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 93] Aborted: " + ex);
                EditorUtility.DisplayDialog("Rail Layer",
                    "Setup stopped: " + ex.Message + "\n\nNothing further was written.", "OK");
            }
        }

        /// <summary>
        /// The raised stone bed under the track. A separate block rather than part of the
        /// rail prefab, because the player should be able to see it, mine it, and because a
        /// bed and a rail wear out for different reasons.
        /// </summary>
        private static BlockItem EnsureBallastBlock(ItemDefinition stone, out bool changed)
        {
            string path = Root + "/Blocks/Block_RailBallast.asset";
            var block = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool created = false;

            if (block == null)
            {
                EnsureFolder(Root + "/Blocks");
                block = ScriptableObject.CreateInstance<BlockItem>();
                created = true;
            }
            bool dirty = created;

            if (block.itemId != "railballast") { block.itemId = "railballast"; dirty = true; }
            if (block.displayName != "Rail Ballast") { block.displayName = "Rail Ballast"; dirty = true; }
            if (block.maxStack <= 0) { block.maxStack = 200; dirty = true; }
            if (block.massPerUnit <= 0f) { block.massPerUnit = 12f; dirty = true; }
            if (block.blockHealth <= 0) { block.blockHealth = 60; dirty = true; }
            if (block.miningTier <= 0) { block.miningTier = 1; dirty = true; }
            if (block.category != "Rail") { block.category = "Rail"; dirty = true; }
            if (string.IsNullOrEmpty(block.description))
            {
                block.description =
                    "Crushed stone bed laid under rail. Raises the track clear of the ground " +
                    "so a line reads as a railway crossing terrain rather than a stripe on it.";
                dirty = true;
            }
            if (block.icon == null) block.iconTint = new Color(0.44f, 0.42f, 0.40f);

            if (block.placedPrefab == null)
            {
                block.placedPrefab = EnsureBallastPrefab();
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(block)) AssetDatabase.CreateAsset(block, path);
                EditorUtility.SetDirty(block);
                Debug.Log("[Setup 93] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            changed = dirty;
            return block;
        }

        private static GameObject EnsureBallastPrefab()
        {
            const string folder = Root + "/StationPrefabs";
            string path = folder + "/RailBallast.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                // 12.2.0: the first cobble scatter was 26 loose cubes on a dark slab, and
                // in the world it read as scattered boxes, not crushed stone. The layer is
                // now ONE combined mesh per tone, dense and angular. Prefabs authored
                // before that get their stone layer rebuilt once; the V2 child is the
                // marker, so a re-run leaves a good bed alone.
                if (existing.transform.Find("StoneLayerV2_0") == null)
                    return RebuildBallastStone(path, existing);

                // The bed carries the formation: when step 85 triples the deck, a bed left at
                // the old width would peek out as a ribbon under wide sleepers.
                RegaugeBallast(path, existing);
                return existing;
            }

            EnsureFolder(folder);
            var root = new GameObject("RailBallast");

            // COBBLESTONE, not a slab.
            //
            // The first version was one smooth cube, which reads as poured concrete however
            // it is tinted - a flat surface has no shadows, so the eye gets no texture cue.
            // Ballast looks like ballast because it is many small stones at slightly
            // different heights and angles, and the shadows between them are the texture.
            //
            // So the bed is a base plus a scatter of jittered cobbles. Deterministic jitter,
            // not random: a prefab must be identical every time it is authored, or two runs
            // of setup produce visibly different track.
            var baseSlab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseSlab.name = "Bed";
            baseSlab.transform.SetParent(root.transform, false);
            // Trapezoid shoulder: wider than the sleepers it carries, as real ballast is.
            baseSlab.transform.localScale = new Vector3(BedWidth, 0.26f, 1.0f);
            baseSlab.transform.localPosition = new Vector3(0f, 0.13f, 0f);

            var darkMat = MakeMat("Mat_RailBallast", new Color(0.30f, 0.29f, 0.27f));
            var stoneMat = MakeMat("Mat_RailBallastStone", new Color(0.46f, 0.44f, 0.41f));
            var paleMat = MakeMat("Mat_RailBallastPale", new Color(0.56f, 0.54f, 0.50f));

            var baseRenderer = baseSlab.GetComponent<Renderer>();
            if (baseRenderer != null && darkMat != null) baseRenderer.sharedMaterial = darkMat;

            // A deterministic PRNG seeded by a constant: same prefab every authoring run.
            var rng = new System.Random(20260119);
            BuildStoneLayer(root, rng);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log("[Setup 93] Created " + path + ".");
            return prefab;
        }

        /// <summary>Ballast bed width in metres: wider than the 4.5 m sleepers it carries, as
        /// real ballast shoulders are. Tripled with the formation in 11.41.0.</summary>
        private const float BedWidth = 6.3f;

        /// <summary>
        /// Crushed stone, the way a ballast bed actually looks: a dense field of angular
        /// grey stones packed shoulder to shoulder across the whole bed, sitting slightly
        /// proud of the base slab so the sleepers bed INTO stone rather than onto a shelf.
        ///
        /// Built as one combined mesh per tone (three draw calls for the whole layer,
        /// where the first version spent 26) because a long line instantiates this prefab
        /// once per cell and per-child draw calls multiply fast.
        /// </summary>
        private static void BuildStoneLayer(GameObject root, System.Random rng)
        {
            var darkMat  = MakeMat("Mat_RailBallast",   new Color(0.26f, 0.26f, 0.28f));
            var stoneMat = MakeMat("Mat_RailBallastStone", new Color(0.40f, 0.40f, 0.43f));
            var paleMat  = MakeMat("Mat_RailBallastPale",  new Color(0.54f, 0.54f, 0.57f));
            var mats = new[] { darkMat, stoneMat, paleMat };

            var verts = new[] { new List<Vector3>(), new List<Vector3>(), new List<Vector3>() };
            var norms = new[] { new List<Vector3>(), new List<Vector3>(), new List<Vector3>() };
            var tris  = new[] { new List<int>(), new List<int>(), new List<int>() };

            // Jittered GRID, not pure scatter: scatter leaves holes and clumps, and a
            // ballast bed is packed, not sprinkled. Columns across the bed, rows along
            // it, each stone knocked off centre and off angle.
            for (float cx = -BedWidth * 0.5f + 0.18f; cx <= BedWidth * 0.5f - 0.12f; cx += 0.30f)
            {
                for (float cz = -0.44f; cz <= 0.44f; cz += 0.24f)
                {
                    float x = cx + (float)(rng.NextDouble() * 0.16 - 0.08);
                    float z = cz + (float)(rng.NextDouble() * 0.12 - 0.06);
                    float size = 0.17f + (float)rng.NextDouble() * 0.11f;

                    // Shoulders: real ballast slopes down at the edges of the bed, so
                    // stones near the rim sit lower and tilt outward.
                    float edge = Mathf.Clamp01((Mathf.Abs(x) - (BedWidth * 0.5f - 0.9f)) / 0.9f);
                    float lift = 0.235f + (float)rng.NextDouble() * 0.05f - edge * 0.10f;

                    var rot = Quaternion.Euler(
                        (float)(rng.NextDouble() * 50.0 - 25.0),
                        (float)(rng.NextDouble() * 360.0),
                        (float)(rng.NextDouble() * 50.0 - 25.0));
                    var scale = new Vector3(size, size * 0.72f, size * (0.85f + (float)rng.NextDouble() * 0.3f));

                    AddStone(verts[rng.Next(3)], norms[rng.Next(3)], tris[rng.Next(3)],
                        new Vector3(x, lift, z), rot, scale);
                }
            }

            for (int t = 0; t < 3; t++)
            {
                if (tris[t].Count == 0) continue;
                var go = new GameObject("StoneLayerV2_" + t);
                go.transform.SetParent(root.transform, false);
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                var mesh = new Mesh { name = "BallastStoneV2_" + t };
                mesh.SetVertices(verts[t]);
                mesh.SetNormals(norms[t]);
                mesh.SetTriangles(tris[t], 0);
                mf.sharedMesh = mesh;
                if (mats[t] != null) mr.sharedMaterial = mats[t];
            }
        }

        /// <summary>Appends one angular stone (a randomly rotated, flattened cube) to a
        /// tone bucket. Vertices and normals are transformed here so the stored mesh
        /// needs no per-stone GameObject at runtime.</summary>
        private static void AddStone(List<Vector3> verts, List<Vector3> norms, List<int> tris,
            Vector3 pos, Quaternion rot, Vector3 scale)
        {
            // Six quad faces of a unit cube, wound outward.
            float[][] faces =
            {
                new[] { -1f,-1f, 1f,  1f,-1f, 1f,  1f, 1f, 1f, -1f, 1f, 1f,  0f, 0f, 1f },
                new[] {  1f,-1f,-1f, -1f,-1f,-1f, -1f, 1f,-1f, 1f, 1f,-1f,  0f, 0f,-1f },
                new[] {  1f,-1f, 1f,  1f,-1f,-1f,  1f, 1f,-1f, 1f, 1f, 1f,  1f, 0f, 0f },
                new[] { -1f,-1f,-1f, -1f,-1f, 1f, -1f, 1f, 1f,-1f, 1f,-1f, -1f, 0f, 0f },
                new[] { -1f, 1f, 1f,  1f, 1f, 1f,  1f, 1f,-1f,-1f, 1f,-1f,  0f, 1f, 0f },
                new[] { -1f,-1f,-1f,  1f,-1f,-1f,  1f,-1f, 1f,-1f,-1f, 1f,  0f,-1f, 0f },
            };

            foreach (var f in faces)
            {
                int b = verts.Count;
                var n = rot * new Vector3(f[12], f[13], f[14]);
                for (int k = 0; k < 4; k++)
                {
                    var corner = new Vector3(f[k * 3], f[k * 3 + 1], f[k * 3 + 2]) * 0.5f;
                    verts.Add(pos + rot * Vector3.Scale(corner, scale));
                    norms.Add(n);
                }
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
        }

        /// <summary>Strips the old loose-cube scatter off an authored bed and builds the
        /// V2 stone layer in its place. The base slab survives: its width and collider
        /// are already correct.</summary>
        private static GameObject RebuildBallastStone(string path, GameObject asset)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            var rootT = contents.transform;
            for (int i = rootT.childCount - 1; i >= 0; i--)
            {
                string n = rootT.GetChild(i).name;
                if (n.StartsWith("Cobble") || n.StartsWith("StoneLayer"))
                    UnityEngine.Object.DestroyImmediate(rootT.GetChild(i).gameObject);
            }
            BuildStoneLayer(contents, new System.Random(20260119));
            var saved = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            Debug.Log("[Setup 93] Rebuilt the ballast stone layer on " + path + ".");
            return saved;
        }

        /// <summary>
        /// Widens an existing bed to the current formation. Idempotent: the bed width IS the
        /// marker, so a prefab already at width is untouched, and only the bed slab and the
        /// cobbles' lateral spread move - tones, jitter pattern and collider setup stay.
        /// </summary>
        private static void RegaugeBallast(string path, GameObject asset)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            var bed = contents.transform.Find("Bed");
            if (bed == null || Mathf.Abs(bed.localScale.x - BedWidth) < 0.01f)
            {
                PrefabUtility.UnloadPrefabContents(contents);
                return;
            }

            float factor = BedWidth / Mathf.Max(0.1f, bed.localScale.x);
            var bsc = bed.localScale;
            bsc.x = BedWidth;
            bed.localScale = bsc;

            for (int i = 0; i < contents.transform.childCount; i++)
            {
                var child = contents.transform.GetChild(i);
                if (!child.name.StartsWith("Cobble", System.StringComparison.Ordinal)) continue;
                var pos = child.localPosition;
                pos.x *= factor;
                child.localPosition = pos;
            }

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            Debug.Log("[Setup 93] Widened the rail ballast bed to " + BedWidth.ToString("0.0") + " m.");
        }

        private static Material MakeMat(string name, Color c)
        {
            const string folder = Root + "/StationPrefabs";
            EnsureFolder(folder);
            string path = folder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) return null;

            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(sh) { name = name, color = c };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static RailLayerTool EnsureTool(BlockItem trackBlock, ItemDefinition trackItem,
            ItemDefinition stone, BlockItem ballast, out bool changed)
        {
            string path = ItemsFolder + "/Item_RailLayer.asset";
            var tool = AssetDatabase.LoadAssetAtPath<RailLayerTool>(path);
            bool created = false;

            if (tool == null)
            {
                EnsureFolder(ItemsFolder);
                tool = ScriptableObject.CreateInstance<RailLayerTool>();
                created = true;
            }
            bool dirty = created;

            if (tool.itemId != "raillayer") { tool.itemId = "raillayer"; dirty = true; }
            if (tool.displayName != "Rail Layer") { tool.displayName = "Rail Layer"; dirty = true; }
            if (tool.maxDurability <= 0) { tool.maxDurability = 400; dirty = true; }
            if (tool.massPerUnit <= 0f) { tool.massPerUnit = 6f; dirty = true; }
            if (tool.category != "Tools") { tool.category = "Tools"; dirty = true; }
            if (string.IsNullOrEmpty(tool.description))
            {
                tool.description =
                    "Lays whole rail runs by dragging. Click a start, aim at the far end, click " +
                    "again. Curves are solved for you; a slope a train could not pull is refused " +
                    "rather than laid. Ctrl + scroll sets the gauge, up to three parallel tracks.";
                dirty = true;
            }
            if (tool.icon == null) tool.iconTint = ToolTint;
            if (created) tool.defaultGauge = 1;

            // Repair the links even on an existing asset: a tool with no track block is a
            // tool that silently does nothing.
            if (tool.trackBlock == null) { tool.trackBlock = trackBlock; dirty = true; }
            if (tool.ballastBlock == null && ballast != null) { tool.ballastBlock = ballast; dirty = true; }
            if (tool.trackItem == null && trackItem != null) { tool.trackItem = trackItem; dirty = true; }
            if (tool.ballastMaterial == null && stone != null) { tool.ballastMaterial = stone; dirty = true; }
            if (tool.trackPerCell <= 0) { tool.trackPerCell = 1; dirty = true; }
            if (tool.ballastPerCell <= 0) { tool.ballastPerCell = 2; dirty = true; }

            if (dirty)
            {
                if (!AssetDatabase.Contains(tool)) AssetDatabase.CreateAsset(tool, path);
                EditorUtility.SetDirty(tool);
                Debug.Log("[Setup 93] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            changed = dirty;
            return tool;
        }

        private static bool EnsureRecipe(RecipeRegistry registry, RailLayerTool tool,
            ItemDefinition steel, ItemDefinition wire)
        {
            const string stem = "Recipe_RailLayer";
            var recipe = FindRecipe(stem);
            bool changed = false;

            RecipeIngredient[] Inputs()
            {
                if (wire == null)
                    return new[] { new RecipeIngredient { item = steel, count = 25 } };
                return new[]
                {
                    new RecipeIngredient { item = steel, count = 25 },
                    new RecipeIngredient { item = wire, count = 15 },
                };
            }

            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = "Rail Layer";
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 16f;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = tool;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, RecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 93] Created " + stem + ".");
            }
            else
            {
                if (recipe.outputItem == null && tool != null)
                {
                    recipe.outputItem = tool;
                    recipe.outputCount = 1;
                    EditorUtility.SetDirty(recipe);
                    changed = true;
                }
                if (recipe.inputs == null || recipe.inputs.Length == 0)
                {
                    recipe.inputs = Inputs();
                    EditorUtility.SetDirty(recipe);
                    changed = true;
                }
            }

            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                Debug.Log("[Setup 93] Added " + stem + " to RecipeRegistry.");
                changed = true;
            }

            return changed;
        }

        // ============================================================
        //                        Helpers
        // ============================================================
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

        private static BlockItem FindBlock(string stem)
        {
            var guids = AssetDatabase.FindAssets(stem + " t:BlockItem");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == stem)
                    return AssetDatabase.LoadAssetAtPath<BlockItem>(p);
            }
            return null;
        }

        private static RecipeDefinition FindRecipe(string stem)
        {
            var guids = AssetDatabase.FindAssets(stem + " t:RecipeDefinition");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == stem)
                    return AssetDatabase.LoadAssetAtPath<RecipeDefinition>(p);
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
