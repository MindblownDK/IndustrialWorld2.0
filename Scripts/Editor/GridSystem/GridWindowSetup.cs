#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    /// <summary>Non-destructive Setup pass for the unified Grid Window item with all 8 precision-aligned shapes and 3 glass finishes.</summary>
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

        private struct WindowMaterials
        {
            public Material Frame;
            public Material Trim;
            public Material ClearPane;
            public Material TintedPane;
            public Material Highlight;
        }

        private struct CheekMeshes
        {
            public Mesh SlopeSide;
            public Mesh InvertedSlopeSide;
            public Mesh HalfSlopeSide;
            public Mesh LongSlopeUpperQuad;
            public Mesh TwoCellFlank;
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
            float h = cell * 0.5f;
            var cheeks = BuildPersistentCheekMeshes(h);

            GameObject prefab = BuildOrRepairPrefab(cell, mats, cheeks);
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
            Debug.Log("[VoxelEngineSetupWindow] Grid Window Setup complete — rebuilt all 8 precision-aligned shapes (including the sealed 3-pane Corner Wrap) and refreshed Clear / Reinforced / Tinted materials non-destructively.");
            EditorUtility.DisplayDialog("Voxel Engine — Grid Window Setup",
                "Grid Window refreshed.\n\nAll 8 shapes now use flush industrial steel frames, true cell-aligned slope angles with sealed triangular cheek glazing, and a solid 3-pane chamfered Corner Wrap with zero gaps. Existing item stats, recipes, and custom prefab children were preserved.",
                "OK");
        }

        private static CheekMeshes BuildPersistentCheekMeshes(float h)
        {
            return new CheekMeshes
            {
                // Right triangle in YZ plane (x = 0): (-h, -h) -> (+h, -h) -> (+h, +h)
                SlopeSide = GetOrUpdatePolygonMesh("Mesh_GridWindow_CheekSlope",
                    new Vector3(0f, -h, -h),
                    new Vector3(0f, -h,  h),
                    new Vector3(0f,  h,  h)),

                // Inverted right triangle in YZ plane: (-h, +h) -> (-h, -h) -> (+h, +h) -> wait: bottom z=+h, top z=-h..+h:
                // Matches GridShapeVariantBlock.InvertedSlope: (-h,-h,+h) -- (-h,+h,+h) -- (-h,+h,-h)
                InvertedSlopeSide = GetOrUpdatePolygonMesh("Mesh_GridWindow_CheekInvertedSlope",
                    new Vector3(0f, -h,  h),
                    new Vector3(0f,  h,  h),
                    new Vector3(0f,  h, -h)),

                // Lower 1:2 right triangle in YZ plane: (0, -h, -h) -> (0, -h, +h) -> (0, 0, +h)
                HalfSlopeSide = GetOrUpdatePolygonMesh("Mesh_GridWindow_CheekHalfSlope",
                    new Vector3(0f, -h, -h),
                    new Vector3(0f, -h,  h),
                    new Vector3(0f,  0f, h)),

                // Upper 1:2 trapezoid in YZ plane: (0, -h, -h) -> (0, -h, +h) -> (0, +h, +h) -> (0, 0, -h)
                LongSlopeUpperQuad = GetOrUpdatePolygonMesh("Mesh_GridWindow_CheekLongSlope",
                    new Vector3(0f, -h, -h),
                    new Vector3(0f, -h,  h),
                    new Vector3(0f,  h,  h),
                    new Vector3(0f,  0f, -h)),

                // 2-cell flank right triangle in XY plane (z = 0): (-h, -h, 0) -> (+3h, -h, 0) -> (+3h, +h, 0)
                TwoCellFlank = GetOrUpdatePolygonMesh("Mesh_GridWindow_CheekTwoCell",
                    new Vector3(-h,    -h, 0f),
                    new Vector3(3f * h, -h, 0f),
                    new Vector3(3f * h,  h, 0f)),
            };
        }

        private static GameObject BuildOrRepairPrefab(float cell, WindowMaterials mats, CheekMeshes cheeks)
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

                BuildWindowShapes(root.transform, cell, mats, cheeks);

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

        private static void BuildWindowShapes(Transform parent, float cell, WindowMaterials mats, CheekMeshes cheeks)
        {
            float h = cell * 0.5f;
            float depth = cell * 0.085f;
            float frontPlaneZ = -h + depth * 0.5f; // -0.915 m on Large (2m) grid: flush front face!

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
                        // Heavy protective top window sill cap
                        Cube(shapeRoot, "Generated_HalfSillCap",
                            new Vector3(0f, 0.005f, frontPlaneZ),
                            new Vector3(cell, cell * 0.032f, depth * 1.32f), mats.Trim);
                        break;

                    case GridWindowShape.Slope:
                    {
                        float diag = Mathf.Sqrt(2f) * cell;
                        BuildWindowPanel(shapeRoot, "SlopePanel", cell, cell, diag,
                            Vector3.zero, new Vector3(-45f, 0f, 0f), mats);
                        BuildSideCheeks(shapeRoot, cell, cheeks.SlopeSide, h - depth * 0.35f, false, mats);
                        AddBottomAndRearSeals(shapeRoot, cell, h, depth, mats.Frame);
                        break;
                    }

                    case GridWindowShape.InvertedSlope:
                    {
                        float diag = Mathf.Sqrt(2f) * cell;
                        BuildWindowPanel(shapeRoot, "InvertedSlopePanel", cell, cell, diag,
                            Vector3.zero, new Vector3(45f, 0f, 0f), mats);
                        BuildSideCheeks(shapeRoot, cell, cheeks.InvertedSlopeSide, h - depth * 0.35f, false, mats);
                        Cube(shapeRoot, "Generated_InvFrontTopBar", new Vector3(0f, h - cell * 0.025f, -h + cell * 0.04f),
                            new Vector3(cell, cell * 0.05f, cell * 0.08f), mats.Frame);
                        Cube(shapeRoot, "Generated_InvRearBotBar", new Vector3(0f, -h + cell * 0.025f, h - cell * 0.04f),
                            new Vector3(cell, cell * 0.05f, cell * 0.08f), mats.Frame);
                        break;
                    }

                    case GridWindowShape.HalfSlope:
                    {
                        float slopeLen = Mathf.Sqrt(5f) * h; // sqrt(1.25) * cell
                        BuildWindowPanel(shapeRoot, "HalfSlopePanel", cell, cell, slopeLen,
                            new Vector3(0f, -h * 0.5f, 0f), new Vector3(-63.43495f, 0f, 0f), mats);
                        BuildSideCheeks(shapeRoot, cell, cheeks.HalfSlopeSide, h - depth * 0.35f, false, mats);
                        Cube(shapeRoot, "Generated_HalfSlopeBotSeal", new Vector3(0f, -h + cell * 0.022f, -h + cell * 0.045f),
                            new Vector3(cell, cell * 0.045f, cell * 0.09f), mats.Frame);
                        Cube(shapeRoot, "Generated_HalfSlopeRearSeal", new Vector3(0f, -cell * 0.022f, h - cell * 0.045f),
                            new Vector3(cell, cell * 0.045f, cell * 0.09f), mats.Frame);
                        break;
                    }

                    case GridWindowShape.LongSlope:
                    {
                        float slopeLen = Mathf.Sqrt(5f) * h; // upper 1:2 half-slope, matches HalfSlope end-to-end
                        BuildWindowPanel(shapeRoot, "LongSlopePanel", cell, cell, slopeLen,
                            new Vector3(0f, h * 0.5f, 0f), new Vector3(-63.43495f, 0f, 0f), mats);
                        // Front lower riser window pane (y in [-h, 0] at z = frontPlaneZ) so single-block use is also sealed
                        BuildWindowPanel(shapeRoot, "LongSlopeFrontRiser", cell, cell, h,
                            new Vector3(0f, -h * 0.5f, frontPlaneZ), Vector3.zero, mats);
                        BuildSideCheeks(shapeRoot, cell, cheeks.LongSlopeUpperQuad, h - depth * 0.35f, false, mats);
                        break;
                    }

                    case GridWindowShape.LongSlopeTwoCell:
                    {
                        // True continuous 2-cell canopy slope across x in [-h, +3h] rising from y = -h to y = +h:
                        // chord length = sqrt((2*cell)^2 + cell^2) = sqrt(5) * cell at 26.565 degrees.
                        float spanLen = Mathf.Sqrt(5f) * cell;
                        BuildWindowPanel(shapeRoot, "TwoCellLongSlopePanel", cell, spanLen, cell,
                            new Vector3(h, 0f, 0f), new Vector3(-90f, 0f, 0f), mats,
                            postRotateEuler: new Vector3(0f, 0f, 26.56505f));
                        BuildSideCheeks(shapeRoot, cell, cheeks.TwoCellFlank, h - depth * 0.35f, true, mats);
                        Cube(shapeRoot, "Generated_TwoCellLowToe", new Vector3(-h + cell * 0.045f, -h + cell * 0.025f, 0f),
                            new Vector3(cell * 0.09f, cell * 0.05f, cell), mats.Frame);
                        Cube(shapeRoot, "Generated_TwoCellHighCrest", new Vector3(3f * h - cell * 0.045f, h - cell * 0.025f, 0f),
                            new Vector3(cell * 0.09f, cell * 0.05f, cell), mats.Frame);
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
            float sidePlaneX = -frontPlaneZ; // +0.915 m on Large (2m) grid — exact mirror of frontPlaneZ!
            Vector2 p0 = new Vector2(-h, frontPlaneZ);
            Vector2 p1 = new Vector2(0.16f * h, frontPlaneZ);
            Vector2 p2 = new Vector2(sidePlaneX, -0.16f * h);
            Vector2 p3 = new Vector2(sidePlaneX, h);

            BuildVerticalSpanSegment(shapeRoot, "CornerFrontPane", cell, p0, p1, cell, mats);
            BuildVerticalSpanSegment(shapeRoot, "CornerChamferPane", cell, p1, p2, cell, mats);
            BuildVerticalSpanSegment(shapeRoot, "CornerSidePane", cell, p2, p3, cell, mats);

            // Solid full-height vertical steel corner mullions at both inner joints (p1 and p2)
            // plus end posts at p0 and p3, sealing every seam completely with zero holes.
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
            // Tangent runs along +X of the panel; outward normal is (-sin(yaw), -cos(yaw)) facing exterior (-Z -> +X)
            float yawDeg = -Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            BuildWindowPanel(parent, name, cell, segWidth, height,
                new Vector3(mid.x, 0f, mid.y), new Vector3(0f, yawDeg, 0f), mats);
        }

        private static void BuildSideCheeks(Transform shapeRoot, float cell, Mesh cheekMesh,
            float offsetDistance, bool alongZAxis, WindowMaterials mats)
        {
            if (cheekMesh == null) return;
            for (int side = -1; side <= 1; side += 2)
            {
                Transform cheekGroup = NewGroup(shapeRoot, "Generated_SideCheek_" + (side < 0 ? "Neg" : "Pos"));
                cheekGroup.localPosition = alongZAxis
                    ? new Vector3(0f, 0f, side * offsetDistance)
                    : new Vector3(side * offsetDistance, 0f, 0f);

                Transform variants = NewGroup(cheekGroup, "Generated_WindowPaneVariants");

                Transform clear = NewGroup(variants, "Generated_WindowPane_Clear");
                MeshPart(clear, "Generated_Cheek_Clear_Glass", cheekMesh, mats.ClearPane);
                clear.gameObject.SetActive(true);

                Transform reinforced = NewGroup(variants, "Generated_WindowPane_Reinforced");
                MeshPart(reinforced, "Generated_Cheek_Reinforced_Glass", cheekMesh, mats.ClearPane);
                reinforced.gameObject.SetActive(false);

                Transform tinted = NewGroup(variants, "Generated_WindowPane_Tinted");
                MeshPart(tinted, "Generated_Cheek_Tinted_Glass", cheekMesh, mats.TintedPane);
                tinted.gameObject.SetActive(false);
            }
        }

        private static void AddBottomAndRearSeals(Transform shapeRoot, float cell, float h, float depth, Material frameMat)
        {
            Cube(shapeRoot, "Generated_SlopeFrontToeBar",
                new Vector3(0f, -h + cell * 0.024f, -h + cell * 0.042f),
                new Vector3(cell, cell * 0.048f, cell * 0.084f), frameMat);
            Cube(shapeRoot, "Generated_SlopeRearCrestBar",
                new Vector3(0f, h - cell * 0.024f, h - cell * 0.042f),
                new Vector3(cell, cell * 0.048f, cell * 0.084f), frameMat);
            for (int s = -1; s <= 1; s += 2)
            {
                Cube(shapeRoot, "Generated_SlopeBottomRail_" + s,
                    new Vector3(s * (h - depth * 0.4f), -h + cell * 0.022f, 0f),
                    new Vector3(depth * 0.8f, cell * 0.044f, cell), frameMat);
                Cube(shapeRoot, "Generated_SlopeRearPost_" + s,
                    new Vector3(s * (h - depth * 0.4f), 0f, h - depth * 0.4f),
                    new Vector3(depth * 0.8f, cell, depth * 0.8f), frameMat);
            }
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

            Transform frameGroup = NewGroup(panel, "Generated_WindowFrame");
            Cube(frameGroup, "Generated_WindowFrame_Left", new Vector3(-width * 0.5f + rim * 0.5f, 0f, 0f),
                new Vector3(rim, height, depth), mats.Frame);
            Cube(frameGroup, "Generated_WindowFrame_Right", new Vector3(width * 0.5f - rim * 0.5f, 0f, 0f),
                new Vector3(rim, height, depth), mats.Frame);
            Cube(frameGroup, "Generated_WindowFrame_Top", new Vector3(0f, height * 0.5f - rim * 0.5f, 0f),
                new Vector3(width, rim, depth), mats.Frame);
            Cube(frameGroup, "Generated_WindowFrame_Bottom", new Vector3(0f, -height * 0.5f + rim * 0.5f, 0f),
                new Vector3(width, rim, depth), mats.Frame);

            // Brushed metallic inner glazing gasket seal around the glass aperture
            Cube(frameGroup, "Generated_WindowGasket_Left", new Vector3(-apertureWidth * 0.5f - innerSeal * 0.5f, 0f, 0f),
                new Vector3(innerSeal, apertureHeight + innerSeal * 2f, depth * 0.62f), mats.Trim);
            Cube(frameGroup, "Generated_WindowGasket_Right", new Vector3(apertureWidth * 0.5f + innerSeal * 0.5f, 0f, 0f),
                new Vector3(innerSeal, apertureHeight + innerSeal * 2f, depth * 0.62f), mats.Trim);
            Cube(frameGroup, "Generated_WindowGasket_Top", new Vector3(0f, apertureHeight * 0.5f + innerSeal * 0.5f, 0f),
                new Vector3(apertureWidth, innerSeal, depth * 0.62f), mats.Trim);
            Cube(frameGroup, "Generated_WindowGasket_Bottom", new Vector3(0f, -apertureHeight * 0.5f - innerSeal * 0.5f, 0f),
                new Vector3(apertureWidth, innerSeal, depth * 0.62f), mats.Trim);

            // Clean low-profile industrial corner gussets instead of bulky spheres
            float gusset = cell * 0.048f;
            foreach (float x in new[] { -width * 0.5f + gusset * 0.62f, width * 0.5f - gusset * 0.62f })
            foreach (float y in new[] { -height * 0.5f + gusset * 0.62f, height * 0.5f - gusset * 0.62f })
            {
                Cube(frameGroup, "Generated_WindowCorner_Gusset", new Vector3(x, y, 0f),
                    new Vector3(gusset, gusset, depth * 1.06f), mats.Trim);
            }

            // Slender structural center rib on double-cell spans
            if (width > cell * 1.5f)
                Cube(frameGroup, "Generated_WindowFrame_CentreJoin", Vector3.zero,
                    new Vector3(cell * 0.024f, apertureHeight, depth * 0.92f), mats.Trim);

            Transform variants = NewGroup(panel, "Generated_WindowPaneVariants");
            Vector3 glassSize = new Vector3(apertureWidth + innerSeal, apertureHeight + innerSeal, paneDepth);

            Transform clear = NewGroup(variants, "Generated_WindowPane_Clear");
            Cube(clear, "Generated_WindowPane_Clear_Glass", Vector3.zero, glassSize, mats.ClearPane);
            AddGlassHighlights(clear, apertureWidth, apertureHeight, paneDepth, mats.Highlight, "Clear");
            clear.gameObject.SetActive(true);

            Transform reinforced = NewGroup(variants, "Generated_WindowPane_Reinforced");
            Cube(reinforced, "Generated_WindowPane_Reinforced_Glass", Vector3.zero, glassSize, mats.ClearPane);
            AddGlassHighlights(reinforced, apertureWidth, apertureHeight, paneDepth, mats.Highlight, "Reinforced");
            float braceWidth = cell * 0.015f;
            float diagLen = Mathf.Min(apertureWidth, apertureHeight) * 0.34f;
            Cube(reinforced, "Generated_WindowPane_Reinforced_MullionH", Vector3.zero,
                new Vector3(apertureWidth, braceWidth, paneDepth * 1.6f), mats.Trim);
            Cube(reinforced, "Generated_WindowPane_Reinforced_MullionV", Vector3.zero,
                new Vector3(braceWidth, apertureHeight, paneDepth * 1.6f), mats.Trim);
            Cube(reinforced, "Generated_WindowPane_Reinforced_CenterHub", Vector3.zero,
                new Vector3(braceWidth * 2.4f, braceWidth * 2.4f, paneDepth * 2.0f), mats.Frame,
                new Vector3(0f, 0f, 45f));
            if (diagLen > 0.08f)
            {
                Cube(reinforced, "Generated_WindowPane_Reinforced_CornerBraceA",
                    new Vector3(-apertureWidth * 0.5f + diagLen * 0.36f, apertureHeight * 0.5f - diagLen * 0.36f, 0f),
                    new Vector3(diagLen, braceWidth * 0.85f, paneDepth * 1.5f), mats.Trim, new Vector3(0f, 0f, 45f));
                Cube(reinforced, "Generated_WindowPane_Reinforced_CornerBraceB",
                    new Vector3(apertureWidth * 0.5f - diagLen * 0.36f, -apertureHeight * 0.5f + diagLen * 0.36f, 0f),
                    new Vector3(diagLen, braceWidth * 0.85f, paneDepth * 1.5f), mats.Trim, new Vector3(0f, 0f, 45f));
            }
            reinforced.gameObject.SetActive(false);

            Transform tinted = NewGroup(variants, "Generated_WindowPane_Tinted");
            Cube(tinted, "Generated_WindowPane_Tinted_Glass", Vector3.zero, glassSize, mats.TintedPane);
            AddGlassHighlights(tinted, apertureWidth, apertureHeight, paneDepth, mats.Highlight, "Tinted");
            tinted.gameObject.SetActive(false);
        }

        private static void AddGlassHighlights(Transform parent, float width, float height, float paneDepth,
            Material highlight, string suffix)
        {
            float stripeHeight = Mathf.Max(0.011f, height * 0.011f);
            float faceZ = -paneDepth * 0.62f;
            Cube(parent, "Generated_WindowPane_" + suffix + "_ReflectionA",
                new Vector3(-width * 0.16f, height * 0.22f, faceZ),
                new Vector3(width * 0.52f, stripeHeight, paneDepth * 0.22f), highlight, new Vector3(0f, 0f, -19f));
            Cube(parent, "Generated_WindowPane_" + suffix + "_ReflectionB",
                new Vector3(width * 0.18f, -height * 0.22f, faceZ),
                new Vector3(width * 0.28f, stripeHeight * 0.72f, paneDepth * 0.22f), highlight, new Vector3(0f, 0f, -19f));
        }

        private static Mesh GetOrUpdatePolygonMesh(string assetName, params Vector3[] contour)
        {
            string path = MaterialFolder + "/" + assetName + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = assetName };
            else mesh.Clear();

            // Double-sided flat-shaded polygon so side cheeks look solid and crisp from both inside and outside!
            var verts = new List<Vector3>((contour.Length - 2) * 6);
            var tris = new List<int>((contour.Length - 2) * 6);
            var uvs = new List<Vector2>((contour.Length - 2) * 6);

            for (int i = 1; i + 1 < contour.Length; i++)
            {
                int baseFwd = verts.Count;
                verts.Add(contour[0]);
                verts.Add(contour[i]);
                verts.Add(contour[i + 1]);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                tris.Add(baseFwd);
                tris.Add(baseFwd + 1);
                tris.Add(baseFwd + 2);

                int baseBack = verts.Count;
                verts.Add(contour[0]);
                verts.Add(contour[i + 1]);
                verts.Add(contour[i]);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                tris.Add(baseBack);
                tris.Add(baseBack + 1);
                tris.Add(baseBack + 2);
            }

            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);

            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        private static GameObject MeshPart(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return go;
        }

        private static Transform NewGroup(Transform parent, string name)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            group.transform.localPosition = Vector3.zero;
            group.transform.localRotation = Quaternion.identity;
            group.transform.localScale = Vector3.one;
            return group.transform;
        }

        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 scale,
            Material material, Vector3 euler = default)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            return go;
        }

        private static void RemoveGeneratedGroup(Transform root, string name)
        {
            Transform group = root.Find(name);
            if (group != null) Object.DestroyImmediate(group.gameObject);
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
                item.description = "One Grid Window item with selectable flush square, 45-degree sloped, half, sealed 3-pane corner wrap and two-cell long-slope shapes, plus Clear, Reinforced and Tinted glass.";
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
                if (string.IsNullOrWhiteSpace(item.description))
                    item.description = "One Grid Window item with selectable flush square, sloped, half, 3-pane corner wrap and two-cell long-slope shapes, plus Clear, Reinforced and Tinted glass.";
                if (item.maxStack <= 0) item.maxStack = 20;
                if (item.blockMass <= 0f) item.blockMass = 400f;
                if (item.blockHP <= 0f) item.blockHP = 320f;
                if (string.IsNullOrWhiteSpace(item.category)) item.category = "Grid Blocks";
                item.gridSize = GridSize.Large;
            }

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
                Debug.LogWarning("[GridWindowSetup] RecipeRegistry.asset was not found. The recipe asset was created and can be registered after core crafting setup.");
                return;
            }
            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }
        }

        private static Material GetOrUpdateMaterial(string name, Color color, bool translucent,
            float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;

            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created)
                material = new Material(shader) { name = name };
            else if (material.shader != shader)
                material.shader = shader;

            material.color = color;
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);

            if (translucent)
            {
                VoxelEngine.Rendering.RuntimeMaterials.ApplyTranslucent(material, color);
            }
            else
            {
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
                material.renderQueue = -1;
            }

            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);

            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash <= 0) return;
            string parent = path.Substring(0, slash);
            string name = path.Substring(slash + 1);
            EnsureFolder(parent);
            if (AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
