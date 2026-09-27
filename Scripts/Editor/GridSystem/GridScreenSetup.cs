#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using VoxelEngine.GridSystem;
using VoxelEngine.GridSystem.UI;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Non-destructive Step 19 setup for Grid Screens & Data Providers.
    /// Generates premium screen block prefabs (Small, Medium, Large, Wide)
    /// with GridScreenBlock components, materials, items, recipes, and the camera block.
    /// v5.51.2-dev refreshes only generated screen/camera visuals and required links;
    /// existing balance values, custom child objects, recipes, and authored tuning are preserved.
    /// </summary>
    public static class GridScreenSetup
    {
        public static void RunStep19()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 19 — Grid Screens setup started.");

            const string ASSET_ROOT = "Assets/VoxelEngineAssets";
            const string GRID_ROOT = ASSET_ROOT + "/GridSystem";
            const string PREFABS = GRID_ROOT + "/Prefabs";
            const string MATS = PREFABS + "/Mats";
            const string SCREEN_ITEMS = GRID_ROOT + "/ScreenItems";
            const string SCREEN_RECIPES = GRID_ROOT + "/ScreenRecipes";

            foreach (var f in new[] { GRID_ROOT, PREFABS, MATS, SCREEN_ITEMS, SCREEN_RECIPES })
                EnsureFolder(f);

            int created = 0, preserved = 0;

            // Materials
            Material GetMat(string name, Color color, bool emissive = false)
            {
                string path = MATS + "/" + name + ".mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null) { preserved++; return existing; }

                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat = new Material(shader) { name = name, color = color };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (emissive)
                {
                    mat.EnableKeyword("_EMISSION");
                    if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 0.6f);
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
                }
                AssetDatabase.CreateAsset(mat, path);
                created++;
                return mat;
            }

            var frameMat = GetMat("Mat_ScreenFrame", new Color(0.12f, 0.13f, 0.15f));
            var bezelMat = GetMat("Mat_ScreenBezel", new Color(0.035f, 0.04f, 0.05f));
            var screenGlow = GetMat("Mat_ScreenGlow", new Color(0.18f, 0.72f, 0.88f), true);
            var screenOff = GetMat("Mat_ScreenOff", new Color(0.02f, 0.022f, 0.03f));
            var accentMat = GetMat("Mat_ScreenAccent", new Color(0.20f, 0.55f, 0.95f), true);

            // Recipe registry
            var registry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            var ironPlate = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_IronPlate.asset");
            var copperWire = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_CopperWire.asset");
            var circuit = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_Circuit.asset");
            var glass = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_Glass.asset");
            var steelPlate = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_SteelPlate.asset");

            // Generate screen prefab with premium visuals.
            // Existing generated visuals are refreshed, while custom child objects and balance values are preserved.
            void CreateScreenPrefab(string name, ScreenSize size, float cs, Vector3 screenScale, string displayName, int hp)
            {
                string prefabPath = PREFABS + "/" + name + ".prefab";
                bool existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
                var root = existingPrefab ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(name);
                root.name = name;

                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var child = root.transform.GetChild(i);
                    if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                        Object.DestroyImmediate(child.gameObject);
                }

                GameObject Primitive(string childName, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material, Quaternion? localRotation = null, bool keepCollider = false)
                {
                    var go = GameObject.CreatePrimitive(type);
                    go.name = childName;
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = localPosition;
                    go.transform.localRotation = localRotation ?? Quaternion.identity;
                    go.transform.localScale = localScale;
                    var renderer = go.GetComponent<Renderer>();
                    if (renderer != null) renderer.sharedMaterial = material;
                    if (!keepCollider)
                    {
                        var collider = go.GetComponent<Collider>();
                        if (collider != null) Object.DestroyImmediate(collider);
                    }
                    return go;
                }

                // Dark frame/bezel
                Primitive("Generated_Frame", PrimitiveType.Cube, Vector3.zero, screenScale * 0.96f, frameMat);

                // Screen surface (dark, slightly recessed)
                Primitive("Generated_ScreenSurface", PrimitiveType.Cube,
                    new Vector3(0, 0, -screenScale.z * 0.02f), screenScale * 0.88f, screenOff);

                // Solid back panel (dark, fills the full cell so screens are never see-through)
                Primitive("Generated_BackPanel", PrimitiveType.Cube,
                    new Vector3(0, 0, screenScale.z * 0.02f), screenScale * 0.98f, frameMat);

                // Glow strip at bottom
                Primitive("Generated_GlowStrip", PrimitiveType.Cube,
                    new Vector3(0, -screenScale.y * 0.35f, -screenScale.z * 0.05f),
                    new Vector3(screenScale.x * 0.7f, screenScale.y * 0.04f, screenScale.z * 0.04f), screenGlow);

                // Accent corner dots
                float dotSize = cs * 0.04f;
                float cornerOffset = screenScale.x * 0.42f;
                foreach (var x in new[] { -cornerOffset, cornerOffset })
                {
                    foreach (var y in new[] { screenScale.y * 0.42f, -screenScale.y * 0.42f })
                    {
                        Primitive("Generated_CornerDot", PrimitiveType.Sphere,
                            new Vector3(x, y, -screenScale.z * 0.06f), Vector3.one * dotSize, accentMat);
                    }
                }

                var col = root.GetComponent<BoxCollider>();
                if (col == null) col = root.AddComponent<BoxCollider>();
                col.center = Vector3.zero;
                col.size = screenScale;

                var screenBlock = root.GetComponent<GridScreenBlock>();
                bool newScreenComponent = screenBlock == null;
                if (newScreenComponent) screenBlock = root.AddComponent<GridScreenBlock>();
                screenBlock.screenSize = size;
                if (string.IsNullOrWhiteSpace(screenBlock.blockName) || screenBlock.blockName == "Armor Block")
                    screenBlock.blockName = displayName;
                if (screenBlock.BlockMass <= 0f) screenBlock.BlockMass = hp * 0.5f;
                if (screenBlock.maxHP <= 0f) screenBlock.maxHP = hp;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (existingPrefab) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
                if (existingPrefab) preserved++; else created++;

                // Create/repair item without resetting existing balance values.
                var itemPath = SCREEN_ITEMS + "/" + name + ".asset";
                var item = AssetDatabase.LoadAssetAtPath<VoxelEngine.GridSystem.GridBlockItem>(itemPath);
                bool newItem = item == null;
                if (newItem)
                {
                    item = ScriptableObject.CreateInstance<VoxelEngine.GridSystem.GridBlockItem>();
                    AssetDatabase.CreateAsset(item, itemPath);
                    created++;
                }
                else preserved++;

                if (string.IsNullOrWhiteSpace(item.itemId)) item.itemId = name.ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(item.displayName)) item.displayName = displayName;
                if (string.IsNullOrWhiteSpace(item.description)) item.description = "Configurable " + size.ToString().ToLowerInvariant() + " digital screen. Right-click to configure data source and display mode.";
                if (newItem) item.iconTint = new Color(0.18f, 0.72f, 0.88f);
                if (item.maxStack <= 0) item.maxStack = 99;
                if (item.massPerUnit <= 0f) item.massPerUnit = 1f;
                if (string.IsNullOrWhiteSpace(item.category)) item.category = "Grid";
                item.gridSize = GridSize.Large;
                item.blockPrefab = prefab;
                if (item.blockMass <= 0f) item.blockMass = hp * 0.5f;
                if (item.blockHP <= 0f) item.blockHP = hp;
                EditorUtility.SetDirty(item);

                // Create/repair recipe while preserving authored cost/timing when already present.
                var recipePath = SCREEN_RECIPES + "/Recipe_" + name + ".asset";
                var recipe = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(recipePath);
                bool newRecipe = recipe == null;
                if (newRecipe)
                {
                    recipe = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                    AssetDatabase.CreateAsset(recipe, recipePath);
                    created++;
                }
                else preserved++;

                if (string.IsNullOrWhiteSpace(recipe.displayName)) recipe.displayName = displayName;
                recipe.outputItem = item;
                if (recipe.outputCount <= 0) recipe.outputCount = 1;
                if (newRecipe)
                {
                    recipe.requiredStation = VoxelEngine.Crafting.StationTier.Assembler;
                    recipe.craftSeconds = 6f;
                    recipe.unlockedByDefault = false;
                }
                else if (recipe.craftSeconds <= 0f)
                {
                    recipe.craftSeconds = 6f;
                }

                if (recipe.inputs == null || recipe.inputs.Length == 0)
                {
                    var inputs = new System.Collections.Generic.List<VoxelEngine.Crafting.RecipeIngredient>();
                    void AddInput(ItemDefinition def, int count)
                    {
                        if (def != null) inputs.Add(new VoxelEngine.Crafting.RecipeIngredient { item = def, count = count });
                    }
                    int mult = size == ScreenSize.Small ? 1 : size == ScreenSize.Medium ? 2 : size == ScreenSize.Large ? 4 : size == ScreenSize.ExtraLarge ? 8 : 2;
                    AddInput(ironPlate, 2 * mult);
                    AddInput(copperWire, 4 * mult);
                    AddInput(circuit, 1 * mult);
                    if (mult >= 2) AddInput(glass, 1 * mult);
                    recipe.inputs = inputs.ToArray();
                }
                EditorUtility.SetDirty(recipe);

                if (registry != null && !registry.recipes.Contains(recipe))
                    registry.recipes.Add(recipe);

                Debug.Log($"[Step 19] {(existingPrefab ? "✓ Refreshed generated visuals and preserved" : "+ Created")} screen prefab: {name}");
            }

            float cs = GridSize.Large.CellSize(); // 2.5 m — screens designed for large grid
            CreateScreenPrefab("Screen_Small", ScreenSize.Small, cs, new Vector3(cs * 0.95f, cs * 0.85f, cs * 0.08f), "Small Screen", 80);
            CreateScreenPrefab("Screen_Wide", ScreenSize.Wide, cs, new Vector3(cs * 1.85f, cs * 0.85f, cs * 0.08f), "Wide Screen", 120);
            CreateScreenPrefab("Screen_Medium", ScreenSize.Medium, cs, new Vector3(cs * 1.85f, cs * 1.85f, cs * 0.10f), "Medium Screen", 180);
            CreateScreenPrefab("Screen_ExtraLarge", ScreenSize.ExtraLarge, cs, new Vector3(cs * 7.40f, cs * 7.40f, cs * 0.14f), "Extra Large Screen", 800);
            CreateScreenPrefab("Screen_Large", ScreenSize.Large, cs, new Vector3(cs * 3.70f, cs * 3.70f, cs * 0.12f), "Large Screen", 400);

            // ---- Camera Block Prefab ----
            string camPrefabPath = PREFABS + "/CameraBlock.prefab";
            var csCam = cs;
            bool existingCameraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(camPrefabPath) != null;
            var camRoot = existingCameraPrefab ? PrefabUtility.LoadPrefabContents(camPrefabPath) : new GameObject("CameraBlock");
            camRoot.name = "CameraBlock";

            // Preserve any custom/non-generated child content and all existing component balance/tuning.
            for (int i = camRoot.transform.childCount - 1; i >= 0; i--)
            {
                var child = camRoot.transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                    Object.DestroyImmediate(child.gameObject);
            }

            var alloyMat = GetMat("Mat_CameraWarmAlloy", new Color(0.72f, 0.62f, 0.34f));
            var darkMat = GetMat("Mat_CameraGraphite", new Color(0.035f, 0.038f, 0.045f));
            var rubberMat = GetMat("Mat_CameraRubber", new Color(0.015f, 0.016f, 0.020f));
            var glassMat = GetMat("Mat_CameraLensDeepGlass", new Color(0.05f, 0.12f, 0.22f, 0.90f), true);
            var boltMat = GetMat("Mat_CameraBoltSteel", new Color(0.62f, 0.66f, 0.70f));
            var greenLedMat = GetMat("Mat_CameraLED_Green", new Color(0.18f, 0.95f, 0.38f), true);

            GameObject Primitive(string childName, PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, Quaternion? localRotation = null, bool keepCollider = false)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = childName;
                go.transform.SetParent(parent, false);
                go.transform.localPosition = localPosition;
                go.transform.localRotation = localRotation ?? Quaternion.identity;
                go.transform.localScale = localScale;
                var renderer = go.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = material;
                if (!keepCollider)
                {
                    var collider = go.GetComponent<Collider>();
                    if (collider != null) Object.DestroyImmediate(collider);
                }
                return go;
            }

            // Boxy premium camera body inspired by compact industrial inspection cameras:
            // warm alloy chassis, dark lens stack, visible bolts, side mounting ears, and a real status LED.
            Primitive("Generated_BackHousing", PrimitiveType.Cube, camRoot.transform,
                new Vector3(0f, csCam * 0.23f, csCam * 0.12f),
                new Vector3(csCam * 0.58f, csCam * 0.42f, csCam * 0.52f), alloyMat);

            Primitive("Generated_TopPlate", PrimitiveType.Cube, camRoot.transform,
                new Vector3(0f, csCam * 0.46f, csCam * 0.12f),
                new Vector3(csCam * 0.62f, csCam * 0.055f, csCam * 0.56f), alloyMat);

            Primitive("Generated_FrontFlange", PrimitiveType.Cube, camRoot.transform,
                new Vector3(0f, csCam * 0.23f, -csCam * 0.17f),
                new Vector3(csCam * 0.70f, csCam * 0.50f, csCam * 0.10f), alloyMat);

            Primitive("Generated_BarrelGold", PrimitiveType.Cylinder, camRoot.transform,
                new Vector3(0f, csCam * 0.23f, -csCam * 0.42f),
                new Vector3(csCam * 0.265f, csCam * 0.22f, csCam * 0.265f), alloyMat, Quaternion.Euler(90f, 0f, 0f));

            Primitive("Generated_RubberFocusRing", PrimitiveType.Cylinder, camRoot.transform,
                new Vector3(0f, csCam * 0.23f, -csCam * 0.62f),
                new Vector3(csCam * 0.34f, csCam * 0.17f, csCam * 0.34f), rubberMat, Quaternion.Euler(90f, 0f, 0f));

            Primitive("Generated_RibbedOuterLens", PrimitiveType.Cylinder, camRoot.transform,
                new Vector3(0f, csCam * 0.23f, -csCam * 0.76f),
                new Vector3(csCam * 0.39f, csCam * 0.08f, csCam * 0.39f), darkMat, Quaternion.Euler(90f, 0f, 0f));

            Primitive("Generated_LensGlass", PrimitiveType.Sphere, camRoot.transform,
                new Vector3(0f, csCam * 0.23f, -csCam * 0.83f),
                new Vector3(csCam * 0.22f, csCam * 0.22f, csCam * 0.08f), glassMat);

            // Subtle lens highlight dot so the prefab reads as glass even before the live feed starts.
            Primitive("Generated_LensHighlight", PrimitiveType.Sphere, camRoot.transform,
                new Vector3(-csCam * 0.065f, csCam * 0.30f, -csCam * 0.89f),
                Vector3.one * csCam * 0.025f, screenGlow);

            // Front bolts, matching the reference's rugged precision-machined face.
            foreach (var x in new[] { -csCam * 0.29f, csCam * 0.29f })
            {
                foreach (var y in new[] { csCam * 0.40f, csCam * 0.06f })
                {
                    Primitive("Generated_FrontBolt", PrimitiveType.Sphere, camRoot.transform,
                        new Vector3(x, y, -csCam * 0.235f), Vector3.one * csCam * 0.055f, boltMat);
                }
            }

            // Side mounting ears and lower feet for a more believable attach point on grids.
            foreach (var x in new[] { -csCam * 0.39f, csCam * 0.39f })
            {
                Primitive("Generated_SideMountEar", PrimitiveType.Cube, camRoot.transform,
                    new Vector3(x, csCam * 0.08f, csCam * 0.16f),
                    new Vector3(csCam * 0.16f, csCam * 0.14f, csCam * 0.34f), alloyMat);

                Primitive("Generated_RoundedFoot", PrimitiveType.Cylinder, camRoot.transform,
                    new Vector3(x, -csCam * 0.02f, csCam * 0.16f),
                    new Vector3(csCam * 0.095f, csCam * 0.18f, csCam * 0.095f), alloyMat, Quaternion.Euler(90f, 0f, 0f));
            }

            Primitive("Generated_MountRail", PrimitiveType.Cube, camRoot.transform,
                new Vector3(0f, -csCam * 0.08f, csCam * 0.18f),
                new Vector3(csCam * 0.52f, csCam * 0.08f, csCam * 0.42f), darkMat);

            var led = Primitive("Generated_StatusLED", PrimitiveType.Sphere, camRoot.transform,
                new Vector3(csCam * 0.23f, csCam * 0.50f, -csCam * 0.18f),
                Vector3.one * csCam * 0.045f, greenLedMat);
            led.tag = "Untagged";

            var ledLightGo = new GameObject("Generated_StatusLED_Light");
            ledLightGo.transform.SetParent(camRoot.transform, false);
            ledLightGo.transform.localPosition = new Vector3(csCam * 0.23f, csCam * 0.51f, -csCam * 0.20f);
            var ledLight = ledLightGo.AddComponent<Light>();
            ledLight.type = LightType.Point;
            ledLight.range = csCam * 0.85f;
            ledLight.intensity = 0.95f;
            ledLight.color = new Color(0.18f, 0.95f, 0.38f);

            var camCol = camRoot.GetComponent<BoxCollider>();
            if (camCol == null) camCol = camRoot.AddComponent<BoxCollider>();
            camCol.center = new Vector3(0f, csCam * 0.20f, -csCam * 0.20f);
            camCol.size = new Vector3(csCam * 0.88f, csCam * 0.70f, csCam * 0.96f);

            var camBlock = camRoot.GetComponent<GridCameraBlock>();
            bool newCameraComponent = camBlock == null;
            if (newCameraComponent) camBlock = camRoot.AddComponent<GridCameraBlock>();
            if (IsDefaultItemIdentity(camBlock.blockName) || camBlock.blockName == "Armor Block") camBlock.blockName = "Camera Block";
            if (camBlock.BlockMass <= 0f) camBlock.BlockMass = 50f;
            if (camBlock.maxHP <= 0f) camBlock.maxHP = 80f;
            if (camBlock.fieldOfView <= 1f) camBlock.fieldOfView = 70f;
            if (camBlock.cameraRange <= 1f) camBlock.cameraRange = 100f;
            bool legacyCameraOffset = Vector3.Distance(camBlock.cameraOffset, new Vector3(0f, 0.3f, 0f)) < 0.001f
                || Vector3.Distance(camBlock.cameraOffset, new Vector3(0f, csCam * 0.23f, -csCam * 0.84f)) < 0.001f;
            if (newCameraComponent || camBlock.cameraOffset == Vector3.zero || legacyCameraOffset)
                camBlock.cameraOffset = new Vector3(0f, csCam * 0.23f, -csCam * 0.96f);
            camBlock.lensLooksAlongNegativeZ = true;
            if (newCameraComponent)
            {
                camBlock.feedResolution = 512;
                camBlock.renderIntervalFrames = 2;
            }

            var camPrefab = PrefabUtility.SaveAsPrefabAsset(camRoot, camPrefabPath);
            if (existingCameraPrefab) PrefabUtility.UnloadPrefabContents(camRoot);
            else Object.DestroyImmediate(camRoot);
            if (existingCameraPrefab) preserved++; else created++;

            // Item — create missing assets and repair required links only. Existing balance/stack/mass values are preserved.
            var camItemPath = SCREEN_ITEMS + "/Block_CameraBlock.asset";
            var camItem = AssetDatabase.LoadAssetAtPath<VoxelEngine.GridSystem.GridBlockItem>(camItemPath);
            bool newCamItem = camItem == null;
            if (newCamItem)
            {
                camItem = ScriptableObject.CreateInstance<VoxelEngine.GridSystem.GridBlockItem>();
                AssetDatabase.CreateAsset(camItem, camItemPath);
                created++;
            }
            else preserved++;

            if (IsDefaultItemIdentity(camItem.itemId)) camItem.itemId = "camera_block";
            if (IsDefaultItemIdentity(camItem.displayName)) camItem.displayName = "Camera Block";
            if (string.IsNullOrWhiteSpace(camItem.description) || camItem.description == "Security camera. Captures live video for linked screens.")
                camItem.description = "Premium grid camera block. Streams a live view to linked screens, draws 30 W when enabled, and uses a status LED: green = feed in use, yellow = online idle, red = offline.";
            if (newCamItem) camItem.iconTint = new Color(0.72f, 0.62f, 0.34f);
            if (camItem.maxStack <= 0) camItem.maxStack = 99;
            if (camItem.massPerUnit <= 0f) camItem.massPerUnit = 1f;
            if (string.IsNullOrWhiteSpace(camItem.category)) camItem.category = "Grid";
            camItem.gridSize = GridSize.Large;
            camItem.blockPrefab = camPrefab;
            if (camItem.blockMass <= 0f) camItem.blockMass = 50f;
            if (camItem.blockHP <= 0f) camItem.blockHP = 80f;
            EditorUtility.SetDirty(camItem);

            // Recipe — preserve existing costs/timing if authored, repair output/registry links.
            var camRecipePath = SCREEN_RECIPES + "/Recipe_CameraBlock.asset";
            var camRecipe = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(camRecipePath);
            bool newCamRecipe = camRecipe == null;
            if (newCamRecipe)
            {
                camRecipe = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                AssetDatabase.CreateAsset(camRecipe, camRecipePath);
                created++;
            }
            else preserved++;

            if (string.IsNullOrWhiteSpace(camRecipe.displayName)) camRecipe.displayName = "Camera Block";
            camRecipe.outputItem = camItem;
            if (camRecipe.outputCount <= 0) camRecipe.outputCount = 1;
            if (newCamRecipe)
            {
                camRecipe.requiredStation = VoxelEngine.Crafting.StationTier.Assembler;
                camRecipe.craftSeconds = 8f;
                camRecipe.unlockedByDefault = false;
            }
            else if (camRecipe.craftSeconds <= 0f)
            {
                camRecipe.craftSeconds = 8f;
            }
            if (camRecipe.inputs == null || camRecipe.inputs.Length == 0)
            {
                var camInputs = new System.Collections.Generic.List<VoxelEngine.Crafting.RecipeIngredient>();
                if (ironPlate != null) camInputs.Add(new VoxelEngine.Crafting.RecipeIngredient { item = ironPlate, count = 4 });
                if (copperWire != null) camInputs.Add(new VoxelEngine.Crafting.RecipeIngredient { item = copperWire, count = 8 });
                if (circuit != null) camInputs.Add(new VoxelEngine.Crafting.RecipeIngredient { item = circuit, count = 3 });
                if (glass != null) camInputs.Add(new VoxelEngine.Crafting.RecipeIngredient { item = glass, count = 2 });
                camRecipe.inputs = camInputs.ToArray();
            }
            EditorUtility.SetDirty(camRecipe);
            if (registry != null && !registry.recipes.Contains(camRecipe)) registry.recipes.Add(camRecipe);
            Debug.Log($"[Step 19] {(existingCameraPrefab ? "✓ Rebuilt generated visuals and preserved" : "+ Created")} premium camera block prefab: CameraBlock");

            if (registry != null) EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Step 19",
                $"Grid Screens + Camera Generated\n\n" +
                $"Screens:\n• Small (1x1)\n• Wide (2x1)\n• Medium (2x2)\n• Large (4x4)\n• Extra Large (8x8)\n\n" +
                $"Camera:\n• Premium Camera Block (live feed, lens stack, mount, green/yellow/red status LED)\n\n" +
                $"Created/verified: {created + preserved} assets\n" +
                $"Items + Recipes added to GridSystem/ScreenItems and ScreenRecipes\n\n" +
                $"Non-destructive — no balance values were modified.",
                "OK");
        }

        private static bool IsDefaultItemIdentity(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                   || value == "Iron Ore"
                   || value == "iron_ore";
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
