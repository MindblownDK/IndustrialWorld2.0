#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    /// <summary>Targeted Setup pass for one Grid Window item with independent shape and pane selectors.</summary>
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

        public static void RunStep122()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Grid Window Setup", "Exit Play Mode before running setup.", "OK");
                return;
            }

            foreach (string folder in new[] { GridRoot, PrefabFolder, ItemFolder, RecipeFolder, MaterialFolder })
                EnsureFolder(folder);

            Material frame = GetOrCreateMaterial("Mat_GridWindowFrame", new Color(0.075f, 0.09f, 0.12f), false);
            Material brace = GetOrCreateMaterial("Mat_GridWindowBrace", new Color(0.38f, 0.47f, 0.56f), false);
            // New asset names deliberately leave earlier authored glass materials intact.
            Material clearPane = GetOrCreateMaterial("Mat_GridWindowPaneClear_v2", new Color(0.40f, 0.80f, 0.92f, 0.12f), true);
            Material tintedPane = GetOrCreateMaterial("Mat_GridWindowPaneTinted_v2", new Color(0.018f, 0.065f, 0.15f, 0.48f), true);
            Material glassHighlight = GetOrCreateMaterial("Mat_GridWindowGlassHighlight", new Color(0.72f, 0.92f, 1f, 0.20f), true);
            if (frame == null || brace == null || clearPane == null || tintedPane == null || glassHighlight == null)
            {
                EditorUtility.DisplayDialog("Grid Window Setup",
                    "A supported Lit shader could not be found. No window prefab or item was changed.", "OK");
                return;
            }

            GameObject prefab = BuildOrRepairPrefab(frame, brace, clearPane, tintedPane, glassHighlight);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Grid Window Setup", "Unity could not save the generated Grid Window prefab. Check the Console; item and recipe links were left unchanged.", "OK");
                return;
            }

            GridBlockItem item = EnsureItem(prefab);
            VoxelEngineSetupWindow.EnsureItemPersistenceCatalog();
            RecipeDefinition recipe = EnsureRecipe(item);
            RegisterRecipe(recipe);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[VoxelEngineSetupWindow] Grid Window Setup complete — one item now offers eight shapes and Clear / Reinforced / Tinted glass. The two-cell long slope owns both rotated cells; existing item tuning, recipe costs, and custom prefab children were preserved.");
            EditorUtility.DisplayDialog("Voxel Engine — Grid Window Setup",
                "Grid Window ready.\n\nUse the placement wheel to switch between window shape and glass finish. The one-cell shallow long slope and true two-Large-cell long slope share the same item. Existing authored mass, health, recipe costs, and custom prefab children were preserved.",
                "OK");
        }

        private static GameObject BuildOrRepairPrefab(Material frame, Material brace, Material clearPane,
            Material tintedPane, Material glassHighlight)
        {
            bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            GameObject root = existed
                ? PrefabUtility.LoadPrefabContents(PrefabPath)
                : new GameObject("GridWindow_Large");

            try
            {
                root.name = "GridWindow_Large";
                // These are the exact groups emitted by the earlier Grid Window Setup pass.
                // Unrelated prefab children and root component tuning are left in place.
                RemoveGeneratedGroup(root.transform, "Generated_WindowFrame");
                RemoveGeneratedGroup(root.transform, "Generated_WindowVariants");
                RemoveGeneratedGroup(root.transform, "Generated_WindowShapes");

                float cell = GridSize.Large.CellSize();
                BuildWindowShapes(root.transform, cell, frame, brace, clearPane, tintedPane, glassHighlight);

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

                if (root.GetComponent<Renderer>() != null)
                    Debug.LogWarning("[GridWindowSetup] The existing Grid Window prefab has a root Renderer. It was preserved; verify that it does not cover the generated pane visuals.");

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log(existed
                    ? "[GridWindowSetup] Refreshed only the generated window geometry groups; unrelated prefab children and component tuning were preserved."
                    : "[GridWindowSetup] Created GridWindow_Large.prefab.");
                return saved;
            }
            finally
            {
                if (existed) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        private static void BuildWindowShapes(Transform parent, float cell, Material frame, Material brace,
            Material clearPane, Material tintedPane, Material glassHighlight)
        {
            Transform shapes = NewGroup(parent, "Generated_WindowShapes");
            GridWindowShape[] allShapes = GridWindowBlock.WindowShapes;
            for (int i = 0; i < allShapes.Length; i++)
            {
                GridWindowShape shape = allShapes[i];
                Transform shapeRoot = NewGroup(shapes, "Generated_WindowShape_" + shape);
                switch (shape)
                {
                    case GridWindowShape.Slope:
                        BuildWindowPanel(shapeRoot, "SlopePanel", cell, cell, cell, Vector3.zero, 26f, 0f,
                            frame, brace, clearPane, tintedPane, glassHighlight);
                        break;
                    case GridWindowShape.LongSlope:
                        BuildWindowPanel(shapeRoot, "LongSlopePanel", cell, cell, cell, Vector3.zero, 12f, 0f,
                            frame, brace, clearPane, tintedPane, glassHighlight);
                        break;
                    case GridWindowShape.LongSlopeTwoCell:
                        BuildWindowPanel(shapeRoot, "TwoCellLongSlopePanel", cell, cell * 2f, cell,
                            new Vector3(cell * 0.5f, 0f, 0f), 12f, 0f,
                            frame, brace, clearPane, tintedPane, glassHighlight);
                        break;
                    case GridWindowShape.Half:
                        BuildWindowPanel(shapeRoot, "HalfPanel", cell, cell, cell * 0.5f,
                            new Vector3(0f, -cell * 0.25f, 0f), 0f, 0f,
                            frame, brace, clearPane, tintedPane, glassHighlight);
                        break;
                    case GridWindowShape.HalfSlope:
                        BuildWindowPanel(shapeRoot, "HalfSlopePanel", cell, cell, cell * 0.55f,
                            new Vector3(0f, -cell * 0.22f, 0f), 24f, 0f,
                            frame, brace, clearPane, tintedPane, glassHighlight);
                        break;
                    case GridWindowShape.InvertedSlope:
                        BuildWindowPanel(shapeRoot, "InvertedSlopePanel", cell, cell, cell, Vector3.zero, -26f, 0f,
                            frame, brace, clearPane, tintedPane, glassHighlight);
                        break;
                    case GridWindowShape.Corner:
                        BuildWindowPanel(shapeRoot, "CornerFrontPanel", cell, cell * 0.56f, cell * 0.92f,
                            new Vector3(-cell * 0.22f, 0f, 0f), 0f, 0f,
                            frame, brace, clearPane, tintedPane, glassHighlight);
                        BuildWindowPanel(shapeRoot, "CornerSidePanel", cell, cell * 0.56f, cell * 0.92f,
                            new Vector3(cell * 0.25f, 0f, -cell * 0.22f), 0f, 90f,
                            frame, brace, clearPane, tintedPane, glassHighlight);
                        break;
                    default:
                        BuildWindowPanel(shapeRoot, "SquarePanel", cell, cell, cell, Vector3.zero, 0f, 0f,
                            frame, brace, clearPane, tintedPane, glassHighlight);
                        break;
                }
                shapeRoot.gameObject.SetActive(shape == GridWindowShape.Square);
            }
        }

        private static void BuildWindowPanel(Transform parent, string name, float cell, float width, float height,
            Vector3 position, float slopeDegrees, float yawDegrees, Material frame, Material brace,
            Material clearPane, Material tintedPane, Material glassHighlight)
        {
            Transform panel = NewGroup(parent, "Generated_" + name);
            panel.localPosition = position;
            panel.localRotation = Quaternion.Euler(slopeDegrees, yawDegrees, 0f);

            float rim = cell * 0.035f;
            float depth = cell * 0.10f;
            float paneDepth = cell * 0.012f;
            float apertureWidth = Mathf.Max(0.06f, width - rim * 2f - cell * 0.012f);
            float apertureHeight = Mathf.Max(0.06f, height - rim * 2f - cell * 0.012f);

            Transform frameGroup = NewGroup(panel, "Generated_WindowFrame");
            Cube(frameGroup, "Generated_WindowFrame_Left", new Vector3(-width * 0.5f + rim * 0.5f, 0f, 0f),
                new Vector3(rim, height, depth), frame);
            Cube(frameGroup, "Generated_WindowFrame_Right", new Vector3(width * 0.5f - rim * 0.5f, 0f, 0f),
                new Vector3(rim, height, depth), frame);
            Cube(frameGroup, "Generated_WindowFrame_Top", new Vector3(0f, height * 0.5f - rim * 0.5f, 0f),
                new Vector3(width, rim, depth), frame);
            Cube(frameGroup, "Generated_WindowFrame_Bottom", new Vector3(0f, -height * 0.5f + rim * 0.5f, 0f),
                new Vector3(width, rim, depth), frame);

            // A narrow centre join is structural on the actual two-cell glazing, not a
            // second item or a second placeable block.
            if (width > cell * 1.5f)
                Cube(frameGroup, "Generated_WindowFrame_CentreJoin", Vector3.zero,
                    new Vector3(cell * 0.022f, apertureHeight, depth * 1.06f), brace);

            float boltSize = cell * 0.022f;
            foreach (float x in new[] { -width * 0.5f + rim * 0.5f, width * 0.5f - rim * 0.5f })
            foreach (float y in new[] { -height * 0.5f + rim * 0.5f, height * 0.5f - rim * 0.5f })
                Sphere(frameGroup, "Generated_WindowFrame_Bolt", new Vector3(x, y, -depth * 0.51f),
                    Vector3.one * boltSize, brace);

            Transform variants = NewGroup(panel, "Generated_WindowPaneVariants");
            Vector3 glassSize = new Vector3(apertureWidth, apertureHeight, paneDepth);

            Transform clear = NewGroup(variants, "Generated_WindowPane_Clear");
            Cube(clear, "Generated_WindowPane_Clear_Glass", Vector3.zero, glassSize, clearPane);
            AddGlassHighlights(clear, apertureWidth, apertureHeight, paneDepth, glassHighlight, "Clear");
            clear.gameObject.SetActive(true);

            Transform reinforced = NewGroup(variants, "Generated_WindowPane_Reinforced");
            Cube(reinforced, "Generated_WindowPane_Reinforced_Glass", Vector3.zero, glassSize, clearPane);
            float braceWidth = cell * 0.018f;
            Cube(reinforced, "Generated_WindowPane_Reinforced_MullionV", new Vector3(0f, 0f, paneDepth * 0.62f),
                new Vector3(braceWidth, apertureHeight, paneDepth * 0.45f), brace);
            Cube(reinforced, "Generated_WindowPane_Reinforced_MullionH", new Vector3(0f, 0f, paneDepth * 0.62f),
                new Vector3(apertureWidth, braceWidth, paneDepth * 0.45f), brace);
            if (width > cell * 1.5f)
            {
                Cube(reinforced, "Generated_WindowPane_Reinforced_MullionL", new Vector3(-cell * 0.5f, 0f, paneDepth * 0.62f),
                    new Vector3(braceWidth, apertureHeight, paneDepth * 0.45f), brace);
                Cube(reinforced, "Generated_WindowPane_Reinforced_MullionR", new Vector3(cell * 0.5f, 0f, paneDepth * 0.62f),
                    new Vector3(braceWidth, apertureHeight, paneDepth * 0.45f), brace);
            }
            reinforced.gameObject.SetActive(false);

            Transform tinted = NewGroup(variants, "Generated_WindowPane_Tinted");
            Cube(tinted, "Generated_WindowPane_Tinted_Glass", Vector3.zero, glassSize, tintedPane);
            AddGlassHighlights(tinted, apertureWidth, apertureHeight, paneDepth, glassHighlight, "Tinted");
            tinted.gameObject.SetActive(false);
        }

        private static void AddGlassHighlights(Transform parent, float width, float height, float paneDepth,
            Material highlight, string suffix)
        {
            float stripeHeight = Mathf.Max(0.012f, height * 0.012f);
            float faceZ = paneDepth * 0.60f;
            Cube(parent, "Generated_WindowPane_" + suffix + "_ReflectionA",
                new Vector3(-width * 0.17f, height * 0.24f, faceZ),
                new Vector3(width * 0.56f, stripeHeight, paneDepth * 0.24f), highlight, new Vector3(0f, 0f, -17f));
            Cube(parent, "Generated_WindowPane_" + suffix + "_ReflectionB",
                new Vector3(width * 0.19f, -height * 0.24f, faceZ),
                new Vector3(width * 0.30f, stripeHeight * 0.75f, paneDepth * 0.24f), highlight, new Vector3(0f, 0f, -17f));
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

        private static GameObject Sphere(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
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

        private static Material GetOrCreateMaterial(string name, Color color, bool translucent)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            var material = new Material(shader) { name = name, color = color };
            if (translucent)
            {
                VoxelEngine.Rendering.RuntimeMaterials.ApplyTranslucent(material, color);
            }
            else
            {
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.62f);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.42f);
            }
            AssetDatabase.CreateAsset(material, path);
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
