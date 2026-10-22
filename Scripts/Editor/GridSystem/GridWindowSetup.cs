#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    /// <summary>Non-destructive Setup pass for the unified Grid Window item with all 8 clean modular single-sheet shapes and 3 glass finishes.</summary>
    public static class GridWindowSetup
    {
        private const string AssetRoot = "Assets/VoxelEngineAssets";
        private const string GridRoot = AssetRoot + "/GridSystem";
        private const string PrefabFolder = GridRoot + "/Prefabs";
        private const string ItemFolder = GridRoot + "/Items";
        private const string RecipeFolder = GridRoot + "/Recipes";
        private const string MaterialFolder = PrefabFolder + "/Mats";
        private const string PrefabPath = PrefabFolder + "/GridWindow_Large.prefab";
        private const string ItemPath = ItemFolder + "/GItem_GridWindow.asset";
        private const string RecipePath = RecipeFolder + "/Recipe_GGridWindow.asset";

        private static readonly string[] LegacyCheekAssetNames =
        {
            "Mesh_GridWindow_CheekSlope.asset",
            "Mesh_GridWindow_CheekInvertedSlope.asset",
            "Mesh_GridWindow_CheekHalfSlope.asset",
            "Mesh_GridWindow_CheekLongSlope.asset",
            "Mesh_GridWindow_CheekTwoCell.asset",
        };

        private struct WindowMaterials
        {
            public Material Frame;
            public Material Trim;
            public Material ClearPane;
            public Material TintedPane;
            public Material Highlight;
        }

        public static void RunStep122()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Grid Window Setup", "Exit Play Mode before running setup.", "OK");
                return;
            }

            foreach (string folder in new[] { GridRoot, PrefabFolder, ItemFolder, RecipeFolder, MaterialFolder })
                EnsureFolder(folder);

            CleanupLegacyCheekMeshes();

            var mats = new WindowMaterials
            {
                Frame = GetOrUpdateMaterial("Mat_GridWindowFrame", new Color(0.085f, 0.10f, 0.115f), false, 0.72f, 0.44f),
                Trim = GetOrUpdateMaterial("Mat_GridWindowBrace", new Color(0.42f, 0.48f, 0.54f), false, 0.84f, 0.58f),
                ClearPane = GetOrUpdateMaterial("Mat_GridWindowPaneClear_v2", new Color(0.66f, 0.90f, 0.98f, 0.085f), true, 0.08f, 0.96f),
                TintedPane = GetOrUpdateMaterial("Mat_GridWindowPaneTinted_v2", new Color(0.03f, 0.09f, 0.18f, 0.42f), true, 0.12f, 0.96f),
                Highlight = GetOrUpdateMaterial("Mat_GridWindowGlassHighlight", new Color(0.84f, 0.96f, 1.00f, 0.16f), true, 0.05f, 0.98f),
            };
            if (mats.Frame == null || mats.Trim == null || mats.ClearPane == null || mats.TintedPane == null || mats.Highlight == null)
            {
                EditorUtility.DisplayDialog("Grid Window Setup",
                    "A supported Lit shader could not be found. No window prefab or item was changed.", "OK");
                return;
            }

            float cell = GridSize.Large.CellSize();
            GameObject prefab = BuildOrRepairPrefab(cell, mats);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Grid Window Setup",
                    "Unity could not save the generated Grid Window prefab. Check the Console; item and recipe links were left unchanged.",
                    "OK");
                return;
            }

            GridBlockItem item = EnsureItem(prefab);
            VoxelEngineSetupWindow.EnsureItemPersistenceCatalog();
            RecipeDefinition recipe = EnsureRecipe(item);
            RegisterRecipe(recipe);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[VoxelEngineSetupWindow] Grid Window Setup complete — rebuilt all 8 clean single-sheet shapes (no extra side panes) and refreshed Clear / Reinforced / Tinted materials non-destructively.");
            EditorUtility.DisplayDialog("Voxel Engine — Grid Window Setup",
                "Grid Window refreshed.\n\nAll 8 shapes now build clean single-surface modular window panes (with no extra side cheeks or box faces), plus the sealed 3-pane Corner Wrap. Existing item stats, recipes, and custom prefab children were preserved.",
                "OK");
        }

        private static void CleanupLegacyCheekMeshes()
        {
            for (int i = 0; i < LegacyCheekAssetNames.Length; i++)
            {
                string path = MaterialFolder + "/" + LegacyCheekAssetNames[i];
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                    AssetDatabase.DeleteAsset(path);
            }
        }

        private static GameObject BuildOrRepairPrefab(float cell, WindowMaterials mats)
        {
            bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            GameObject root = existed
                ? PrefabUtility.LoadPrefabContents(PrefabPath)
                : new GameObject("GridWindow_Large");

            try
            {
                root.name = "GridWindow_Large";
                RemoveGeneratedGroup(root.transform, "Generated_WindowFrame");
                RemoveGeneratedGroup(root.transform, "Generated_WindowVariants");
                RemoveGeneratedGroup(root.transform, "Generated_WindowShapes");

                BuildWindowShapes(root.transform, cell, mats);

                BoxCollider collider = root.GetComponent<BoxCollider>();
                if (collider == null)
                {
                    collider = root.AddComponent<BoxCollider>();
                    collider.center = Vector3.zero;
                    collider.size = Vector3.one * cell;
                }

                GridWindowBlock block = root.GetComponent<GridWindowBlock>();
                if (block == null) block = root.AddComponent<GridWindowBlock>();
                if (string.IsNullOrWhiteSpace(block.blockName) || block.blockName == "Armor Block")
                    block.blockName = "Grid Window";
                if (block.BlockMass <= 0f) block.BlockMass = 400f;
                if (block.maxHP <= 0f) block.maxHP = 320f;
                block.SetWindowShape(GridWindowBlock.NormalizeShape((int)block.windowShape));
                block.SetPaneStyle(GridWindowBlock.Normalize(block.paneStyle));
                EditorUtility.SetDirty(block);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log(existed
                    ? "[GridWindowSetup] Refreshed generated window geometry groups; custom children and component tuning were preserved."
                    : "[GridWindowSetup] Created GridWindow_Large.prefab.");
                return saved;
            }
            finally
            {
                if (existed) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        private static void BuildWindowShapes(Transform parent, float cell, WindowMaterials mats)
        {
            float h = cell * 0.5f;
            float depth = cell * 0.085f;
            float frontPlaneZ = -h + depth * 0.5f; // -0.915 m on Large (2m) grid: flush front face

            Transform shapes = NewGroup(parent, "Generated_WindowShapes");
            GridWindowShape[] allShapes = GridWindowBlock.WindowShapes;
            for (int i = 0; i < allShapes.Length; i++)
            {
                GridWindowShape shape = allShapes[i];
                Transform shapeRoot = NewGroup(shapes, "Generated_WindowShape_" + shape);
                switch (shape)
                {
                    case GridWindowShape.Square:
                        BuildWindowPanel(shapeRoot, "SquarePanel", cell, cell, cell,
                            new Vector3(0f, 0f, frontPlaneZ), Vector3.zero, mats);
                        break;

                    case GridWindowShape.Half:
                        BuildWindowPanel(shapeRoot, "HalfPanel", cell, cell, h,
                            new Vector3(0f, -h * 0.5f, frontPlaneZ), Vector3.zero, mats);
                        Cube(shapeRoot, "Generated_HalfSillCap",
                            new Vector3(0f, 0.005f, frontPlaneZ),
                            new Vector3(cell, cell * 0.032f, depth * 1.32f), mats.Trim);
                        break;

                    case GridWindowShape.Slope:
                    {
                        float diag = Mathf.Sqrt(2f) * cell;
                        BuildWindowPanel(shapeRoot, "SlopePanel", cell, cell, diag,
                            Vector3.zero, new Vector3(-45f, 0f, 0f), mats);
                        break;
                    }

                    case GridWindowShape.InvertedSlope:
                    {
                        float diag = Mathf.Sqrt(2f) * cell;
                        BuildWindowPanel(shapeRoot, "InvertedSlopePanel", cell, cell, diag,
                            Vector3.zero, new Vector3(45f, 0f, 0f), mats);
                        break;
                    }

                    case GridWindowShape.HalfSlope:
                    {
                        // Single clean 1:2 lower sloped pane from (y = -h, z = -h) to (y = 0, z = +h)
                        float slopeLen = Mathf.Sqrt(5f) * h;
                        BuildWindowPanel(shapeRoot, "HalfSlopePanel", cell, cell, slopeLen,
                            new Vector3(0f, -h * 0.5f, 0f), new Vector3(-63.43495f, 0f, 0f), mats);
                        break;
                    }

                    case GridWindowShape.LongSlope:
                    {
                        // Single clean 1:2 upper sloped pane from (y = 0, z = -h) to (y = +h, z = +h)
                        float slopeLen = Mathf.Sqrt(5f) * h;
                        BuildWindowPanel(shapeRoot, "LongSlopePanel", cell, cell, slopeLen,
                            new Vector3(0f, h * 0.5f, 0f), new Vector3(-63.43495f, 0f, 0f), mats);
                        break;
                    }

                    case GridWindowShape.LongSlopeTwoCell:
                    {
                        // Single continuous 2-cell sloped canopy pane across x in [-h, +3h] rising from y = -h to y = +h
                        float spanLen = Mathf.Sqrt(5f) * cell;
                        BuildWindowPanel(shapeRoot, "TwoCellLongSlopePanel", cell, spanLen, cell,
                            new Vector3(h, 0f, 0f), new Vector3(-90f, 0f, 0f), mats,
                            postRotateEuler: new Vector3(0f, 0f, 26.56505f));
                        break;
                    }

                    case GridWindowShape.Corner:
                        BuildPanoramicCornerWrap(shapeRoot, cell, h, depth, frontPlaneZ, mats);
                        break;
                }
                shapeRoot.gameObject.SetActive(shape == GridWindowShape.Square);
            }
        }

        /// <summary>
        /// Connected 3-pane panoramic chamfer wrap (Front Pane -> 45-degree Chamfer Pane -> Side Pane).
        /// Meets adjacent Square windows at (-h, frontPlaneZ) and (-frontPlaneZ, +h) with zero gaps.
        /// </summary>
        private static void BuildPanoramicCornerWrap(Transform shapeRoot, float cell, float h, float depth,
            float frontPlaneZ, WindowMaterials mats)
        {
            float sidePlaneX = -frontPlaneZ; // +0.915 m on Large (2m) grid — exact mirror of frontPlaneZ
            Vector2 p0 = new Vector2(-h, frontPlaneZ);
            Vector2 p1 = new Vector2(0.16f * h, frontPlaneZ);
            Vector2 p2 = new Vector2(sidePlaneX, -0.16f * h);
            Vector2 p3 = new Vector2(sidePlaneX, h);

            BuildVerticalSpanSegment(shapeRoot, "CornerFrontPane", cell, p0, p1, cell, mats);
            BuildVerticalSpanSegment(shapeRoot, "CornerChamferPane", cell, p1, p2, cell, mats);
            BuildVerticalSpanSegment(shapeRoot, "CornerSidePane", cell, p2, p3, cell, mats);

            // Solid full-height vertical steel corner mullions at both inner joints (p1 and p2)
            float postWidth = depth * 1.24f;
            Cube(shapeRoot, "Generated_CornerMullion_SeamA", new Vector3(p1.x, 0f, p1.y),
                new Vector3(postWidth, cell, postWidth), mats.Frame, new Vector3(0f, -22.5f, 0f));
            Cube(shapeRoot, "Generated_CornerMullion_TrimA", new Vector3(p1.x - depth * 0.16f, 0f, p1.y - depth * 0.42f),
                new Vector3(cell * 0.022f, cell * 0.94f, cell * 0.022f), mats.Trim, new Vector3(0f, -22.5f, 0f));

            Cube(shapeRoot, "Generated_CornerMullion_SeamB", new Vector3(p2.x, 0f, p2.y),
                new Vector3(postWidth, cell, postWidth), mats.Frame, new Vector3(0f, -67.5f, 0f));
            Cube(shapeRoot, "Generated_CornerMullion_TrimB", new Vector3(p2.x + depth * 0.42f, 0f, p2.y - depth * 0.16f),
                new Vector3(cell * 0.022f, cell * 0.94f, cell * 0.022f), mats.Trim, new Vector3(0f, -67.5f, 0f));

            // Continuous top & bottom architectural sill/header tie bars across both 45-degree bends
            float capThickness = cell * 0.042f;
            float capY = h - capThickness * 0.5f;
            for (int sy = -1; sy <= 1; sy += 2)
            {
                float y = sy * capY;
                Cube(shapeRoot, "Generated_CornerCap_Front_" + sy,
                    new Vector3((p0.x + p1.x) * 0.5f, y, frontPlaneZ),
                    new Vector3(Vector2.Distance(p0, p1) + depth * 0.6f, capThickness, depth * 1.18f), mats.Frame);
                Cube(shapeRoot, "Generated_CornerCap_Chamfer_" + sy,
                    new Vector3((p1.x + p2.x) * 0.5f, y, (p1.y + p2.y) * 0.5f),
                    new Vector3(Vector2.Distance(p1, p2) + depth * 0.7f, capThickness, depth * 1.18f),
                    mats.Frame, new Vector3(0f, -45f, 0f));
                Cube(shapeRoot, "Generated_CornerCap_Side_" + sy,
                    new Vector3(sidePlaneX, y, (p2.y + p3.y) * 0.5f),
                    new Vector3(Vector2.Distance(p2, p3) + depth * 0.6f, capThickness, depth * 1.18f),
                    mats.Frame, new Vector3(0f, -90f, 0f));
            }
        }

        private static void BuildVerticalSpanSegment(Transform parent, string name, float cell,
            Vector2 startXZ, Vector2 endXZ, float height, WindowMaterials mats)
        {
            Vector2 delta = endXZ - startXZ;
            float segWidth = delta.magnitude;
            Vector2 mid = (startXZ + endXZ) * 0.5f;
            float yawDeg = -Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            BuildWindowPanel(parent, name, cell, segWidth, height,
                new Vector3(mid.x, 0f, mid.y), new Vector3(0f, yawDeg, 0f), mats);
        }

        private static void BuildWindowPanel(Transform parent, string name, float cell, float width, float height,
            Vector3 position, Vector3 euler, WindowMaterials mats, Vector3 postRotateEuler = default)
        {
            Transform panel = NewGroup(parent, "Generated_" + name);
            panel.localPosition = position;
            panel.localRotation = postRotateEuler == default
                ? Quaternion.Euler(euler)
                : Quaternion.Euler(postRotateEuler) * Quaternion.Euler(euler);

            float rim = cell * 0.036f;
            float innerSeal = cell * 0.011f;
            float depth = cell * 0.085f;
            float paneDepth = cell * 0.014f;
            float apertureWidth = Mathf.Max(0.06f, width - (rim + innerSeal) * 2f);
            float apertureHeight = Mathf.Max(0.06f, height - (rim + innerSeal) * 2f);
            float paneWidth = Mathf.Max(0.08f, width - rim * 1.35f);
            float paneHeight = Mathf.Max(0.08f, height - rim * 1.35f);

            // Outer flush steel structural frame
            Cube(panel, "Generated_Frame_Top", new Vector3(0f, height * 0.5f - rim * 0.5f, 0f),
                new Vector3(width, rim, depth), mats.Frame);
            Cube(panel, "Generated_Frame_Bottom", new Vector3(0f, -height * 0.5f + rim * 0.5f, 0f),
                new Vector3(width, rim, depth), mats.Frame);
            Cube(panel, "Generated_Frame_Left", new Vector3(-width * 0.5f + rim * 0.5f, 0f, 0f),
                new Vector3(rim, Mathf.Max(0.04f, height - rim * 2f), depth), mats.Frame);
            Cube(panel, "Generated_Frame_Right", new Vector3(width * 0.5f - rim * 0.5f, 0f, 0f),
                new Vector3(rim, Mathf.Max(0.04f, height - rim * 2f), depth), mats.Frame);

            // Crisp machined metallic inner gasket seal with zero overhang into the aperture
            float gasketDepth = depth * 0.72f;
            Cube(panel, "Generated_Seal_Top", new Vector3(0f, apertureHeight * 0.5f + innerSeal * 0.5f, 0f),
                new Vector3(apertureWidth + innerSeal * 2f, innerSeal, gasketDepth), mats.Trim);
            Cube(panel, "Generated_Seal_Bottom", new Vector3(0f, -apertureHeight * 0.5f - innerSeal * 0.5f, 0f),
                new Vector3(apertureWidth + innerSeal * 2f, innerSeal, gasketDepth), mats.Trim);
            Cube(panel, "Generated_Seal_Left", new Vector3(-apertureWidth * 0.5f - innerSeal * 0.5f, 0f, 0f),
                new Vector3(innerSeal, apertureHeight, gasketDepth), mats.Trim);
            Cube(panel, "Generated_Seal_Right", new Vector3(apertureWidth * 0.5f + innerSeal * 0.5f, 0f, 0f),
                new Vector3(innerSeal, apertureHeight, gasketDepth), mats.Trim);

            // Four tiny flush corner gusset blocks cleanly inside the frame corners
            float boltSize = rim * 0.52f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    Cube(panel, $"Generated_CornerGusset_{(sx < 0 ? "L" : "R")}{(sy < 0 ? "B" : "T")}",
                        new Vector3(sx * (width * 0.5f - rim * 0.5f), sy * (height * 0.5f - rim * 0.5f), 0f),
                        new Vector3(boltSize, boltSize, depth * 1.06f), mats.Trim);
                }
            }

            Transform variants = NewGroup(panel, "Generated_WindowPaneVariants");

            // 1. Clear Glass — crystal-clear low-opacity glazing + subtle specular highlight streak
            Transform clear = NewGroup(variants, "Generated_WindowPane_Clear");
            Cube(clear, "Generated_Clear_Glass", Vector3.zero, new Vector3(paneWidth, paneHeight, paneDepth), mats.ClearPane);
            BuildGlassReflectionHighlight(clear, apertureWidth, apertureHeight, paneDepth, mats.Highlight);
            clear.gameObject.SetActive(true);

            // 2. Reinforced Glass — clear glazing with fine slim perimeter & mullion brace bars
            Transform reinforced = NewGroup(variants, "Generated_WindowPane_Reinforced");
            Cube(reinforced, "Generated_Reinforced_Glass", Vector3.zero, new Vector3(paneWidth, paneHeight, paneDepth), mats.ClearPane);
            BuildGlassReflectionHighlight(reinforced, apertureWidth, apertureHeight, paneDepth, mats.Highlight);

            float mullionThick = Mathf.Clamp(cell * 0.013f, 0.016f, 0.032f);
            float mullionDepth = paneDepth * 1.65f;
            if (apertureWidth >= cell * 0.55f)
            {
                Cube(reinforced, "Generated_Reinforced_MullionV_L",
                    new Vector3(-apertureWidth * 0.30f, 0f, 0f),
                    new Vector3(mullionThick, apertureHeight, mullionDepth), mats.Trim);
                Cube(reinforced, "Generated_Reinforced_MullionV_R",
                    new Vector3(apertureWidth * 0.30f, 0f, 0f),
                    new Vector3(mullionThick, apertureHeight, mullionDepth), mats.Trim);
            }
            if (apertureHeight >= cell * 0.55f)
            {
                Cube(reinforced, "Generated_Reinforced_Transom_Upper",
                    new Vector3(0f, apertureHeight * 0.34f, 0f),
                    new Vector3(apertureWidth, mullionThick * 0.85f, mullionDepth), mats.Trim);
                Cube(reinforced, "Generated_Reinforced_Transom_Lower",
                    new Vector3(0f, -apertureHeight * 0.34f, 0f),
                    new Vector3(apertureWidth, mullionThick * 0.85f, mullionDepth), mats.Trim);
            }
            reinforced.gameObject.SetActive(false);

            // 3. Tinted Glass — rich dark solar-control tint with crisp reflection highlight
            Transform tinted = NewGroup(variants, "Generated_WindowPane_Tinted");
            Cube(tinted, "Generated_Tinted_Glass", Vector3.zero, new Vector3(paneWidth, paneHeight, paneDepth), mats.TintedPane);
            BuildGlassReflectionHighlight(tinted, apertureWidth, apertureHeight, paneDepth, mats.Highlight);
            tinted.gameObject.SetActive(false);
        }

        private static void BuildGlassReflectionHighlight(Transform parent, float apertureWidth, float apertureHeight,
            float paneDepth, Material highlightMat)
        {
            float streakLen = Mathf.Min(apertureWidth, apertureHeight) * 0.42f;
            if (streakLen < 0.08f) return;
            Cube(parent, "Generated_GlassSheen_A",
                new Vector3(-apertureWidth * 0.22f, apertureHeight * 0.20f, paneDepth * 0.62f),
                new Vector3(streakLen, Mathf.Max(0.012f, streakLen * 0.055f), 0.004f),
                highlightMat, new Vector3(0f, 0f, -35f));
        }

        private static GridBlockItem EnsureItem(GameObject prefab)
        {
            GridBlockItem item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(ItemPath);
            bool created = item == null;
            if (created)
            {
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                AssetDatabase.CreateAsset(item, ItemPath);
                item.itemId = "grid_window";
                item.displayName = "Grid Window";
                item.description = "One Grid Window item with selectable square, sloped, half, corner and two-cell long-slope shapes, plus Clear, Reinforced and Tinted glass.";
                item.iconTint = new Color(0.30f, 0.72f, 0.88f);
                item.maxStack = 20;
                item.gridSize = GridSize.Large;
                item.blockMass = 400f;
                item.blockHP = 320f;
                item.category = "Grid Blocks";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(item.itemId)) item.itemId = "grid_window";
                if (string.IsNullOrWhiteSpace(item.displayName)) item.displayName = "Grid Window";
                if (string.IsNullOrWhiteSpace(item.description)
                    || item.description == "A structural grid window. Hold the placement-variant wheel to choose a clear, reinforced, or tinted pane."
                    || item.description == "A structural grid window with selectable clear, reinforced, and tinted panes.")
                    item.description = "One Grid Window item with independently selectable shape and Clear, Reinforced or Tinted glass.";
                if (item.maxStack <= 0) item.maxStack = 20;
                if (item.blockMass <= 0f) item.blockMass = 400f;
                if (item.blockHP <= 0f) item.blockHP = 320f;
                if (string.IsNullOrWhiteSpace(item.category)) item.category = "Grid Blocks";
                item.gridSize = GridSize.Large;
            }

            // Reconnect identity only; authored mass, health, recipe costs and display text survive.
            item.blockPrefab = prefab;
            EditorUtility.SetDirty(item);
            return item;
        }

        private static RecipeDefinition EnsureRecipe(GridBlockItem item)
        {
            RecipeDefinition recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(RecipePath);
            bool created = recipe == null;
            if (created)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, RecipePath);
                recipe.displayName = "Grid Window";
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 6f;
                recipe.unlockedByDefault = true;
            }

            if (string.IsNullOrWhiteSpace(recipe.displayName)) recipe.displayName = "Grid Window";
            recipe.outputItem = item;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var inputs = new List<RecipeIngredient>();
                AddInput(inputs, AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetRoot + "/Industrial/Items/Item_SteelPlate.asset"), 4);
                AddInput(inputs, AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetRoot + "/Industrial/Items/Item_Glass.asset"), 6);
                AddInput(inputs, AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetRoot + "/Industrial/Items/Item_CopperWire.asset"), 2);
                recipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(recipe);
            return recipe;
        }

        private static void AddInput(List<RecipeIngredient> inputs, ItemDefinition item, int count)
        {
            if (item != null) inputs.Add(new RecipeIngredient { item = item, count = count });
        }

        private static void RegisterRecipe(RecipeDefinition recipe)
        {
            if (recipe == null) return;
            RecipeRegistry registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(AssetRoot + "/RecipeRegistry.asset");
            if (registry == null)
            {
                Debug.LogWarning("[GridWindowSetup] RecipeRegistry.asset was not found. The recipe asset was created but must be registered after the core crafting setup.");
                return;
            }
            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }
        }

        private static Material GetOrUpdateMaterial(string assetName, Color color, bool translucent, float metallic, float smoothness)
        {
            string path = MaterialFolder + "/" + assetName + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;

            if (mat == null)
            {
                mat = new Material(shader) { name = assetName, color = color };
                AssetDatabase.CreateAsset(mat, path);
                mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) return null;
            }
            else if (mat.shader == null)
            {
                mat.shader = shader;
            }

            if (translucent)
            {
                VoxelEngine.Rendering.RuntimeMaterials.ApplyTranslucent(mat, color);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            }
            else
            {
                mat.color = color;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Transform NewGroup(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        private static void Cube(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat,
            Vector3 euler = default)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = euler == default ? Quaternion.identity : Quaternion.Euler(euler);
            go.transform.localScale = scale;

            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && mat != null)
            {
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private static void RemoveGeneratedGroup(Transform parent, string name)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                    Object.DestroyImmediate(child.gameObject);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
