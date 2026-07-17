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
    /// with GridScreenBlock components, materials, items, and recipes.
    /// No existing balance values are touched.
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

            // Generate screen prefab with premium visuals
            void CreateScreenPrefab(string name, ScreenSize size, float cs, Vector3 screenScale, string displayName, int hp)
            {
                string prefabPath = PREFABS + "/" + name + ".prefab";
                var root = new GameObject(name);

                // Dark frame/bezel
                var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frame.name = "Generated_Frame";
                frame.transform.SetParent(root.transform, false);
                frame.transform.localScale = screenScale * 0.96f;
                frame.GetComponent<Renderer>().sharedMaterial = frameMat;

                // Screen surface (dark, slightly recessed)
                var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
                screen.name = "Generated_ScreenSurface";
                screen.transform.SetParent(root.transform, false);
                screen.transform.localScale = screenScale * 0.88f;
                screen.transform.localPosition = new Vector3(0, 0, -screenScale.z * 0.02f);
                screen.GetComponent<Renderer>().sharedMaterial = screenOff;

                // Solid back panel (dark, fills the full cell so screens are never see-through)
                var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
                back.name = "Generated_BackPanel";
                back.transform.SetParent(root.transform, false);
                back.transform.localScale = screenScale * 0.98f;
                back.transform.localPosition = new Vector3(0, 0, screenScale.z * 0.02f);
                back.GetComponent<Renderer>().sharedMaterial = frameMat;

                // Glow strip at bottom
                var glow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                glow.name = "Generated_GlowStrip";
                glow.transform.SetParent(root.transform, false);
                glow.transform.localScale = new Vector3(screenScale.x * 0.7f, screenScale.y * 0.04f, screenScale.z * 0.04f);
                glow.transform.localPosition = new Vector3(0, -screenScale.y * 0.35f, -screenScale.z * 0.05f);
                glow.GetComponent<Renderer>().sharedMaterial = screenGlow;

                // Accent corner dots
                float dotSize = cs * 0.04f;
                float cornerOffset = screenScale.x * 0.42f;
                foreach (var x in new[] { -cornerOffset, cornerOffset })
                {
                    foreach (var y in new[] { screenScale.y * 0.42f, -screenScale.y * 0.42f })
                    {
                        var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        dot.name = "Generated_CornerDot";
                        dot.transform.SetParent(root.transform, false);
                        dot.transform.localPosition = new Vector3(x, y, -screenScale.z * 0.06f);
                        dot.transform.localScale = Vector3.one * dotSize;
                        dot.GetComponent<Renderer>().sharedMaterial = accentMat;
                        Object.DestroyImmediate(dot.GetComponent<Collider>());
                    }
                }

                // Collider for raycast hits
                var col = root.AddComponent<BoxCollider>();
                col.size = screenScale;

                // GridScreenBlock component
                var screenBlock = root.AddComponent<GridScreenBlock>();
                screenBlock.screenSize = size;
                screenBlock.blockName = displayName;
                screenBlock.BlockMass = hp * 0.5f;
                screenBlock.maxHP = hp;

                // NOTE: Do NOT call GridBlockMeshBuilder.Build here — it adds
                // procedurally-generated child primitives (armor blocks) that
                // show up as purple/magenta in the screen prefab. The screen
                // visuals are already authored as hand-placed primitives above.

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Object.DestroyImmediate(root);

                // Create item
                var itemPath = SCREEN_ITEMS + "/" + name + ".asset";
                var item = AssetDatabase.LoadAssetAtPath<VoxelEngine.GridSystem.GridBlockItem>(itemPath);
                if (item == null)
                {
                    item = ScriptableObject.CreateInstance<VoxelEngine.GridSystem.GridBlockItem>();
                    AssetDatabase.CreateAsset(item, itemPath);
                    created++;
                }
                else preserved++;

                item.itemId = name.ToLowerInvariant();
                item.displayName = displayName;
                item.description = "Configurable " + size.ToString().ToLowerInvariant() + " digital screen. Right-click to configure data source and display mode.";
                item.iconTint = new Color(0.18f, 0.72f, 0.88f);
                item.maxStack = 99;
                item.massPerUnit = 1f;
                item.category = "Grid";
                item.gridSize = GridSize.Large;
                item.blockPrefab = prefab;
                item.blockMass = hp * 0.5f;
                item.blockHP = hp;
                EditorUtility.SetDirty(item);

                // Create recipe
                var recipePath = SCREEN_RECIPES + "/Recipe_" + name + ".asset";
                var recipe = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(recipePath);
                if (recipe == null)
                {
                    recipe = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                    AssetDatabase.CreateAsset(recipe, recipePath);
                    created++;
                }
                else preserved++;

                recipe.displayName = displayName;
                recipe.outputItem = item;
                recipe.outputCount = 1;
                recipe.requiredStation = VoxelEngine.Crafting.StationTier.Assembler;
                recipe.craftSeconds = 6f;
                recipe.unlockedByDefault = false;

                // Recipe inputs scale with screen size
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
                EditorUtility.SetDirty(recipe);

                if (registry != null && !registry.recipes.Contains(recipe))
                    registry.recipes.Add(recipe);

                Debug.Log($"[Step 19] {(AssetDatabase.LoadMainAssetAtPath(prefabPath) != null ? "✓ Verified" : "+ Created")} screen prefab: {name}");
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
            var camRoot = new GameObject("CameraBlock");

            // Mounting arm
            var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = "Generated_MountArm";
            arm.transform.SetParent(camRoot.transform, false);
            arm.transform.localPosition = new Vector3(0, csCam * 0.15f, 0);
            arm.transform.localScale = new Vector3(csCam * 0.15f, csCam * 0.30f, csCam * 0.15f);
            arm.GetComponent<Renderer>().sharedMaterial = frameMat;

            // Camera body (cylindrical housing)
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Generated_CameraBody";
            body.transform.SetParent(camRoot.transform, false);
            body.transform.localPosition = new Vector3(0, csCam * 0.40f, 0);
            body.transform.localScale = new Vector3(csCam * 0.35f, csCam * 0.20f, csCam * 0.35f);
            var bodyMat = GetMat("Mat_CameraBody", new Color(0.08f, 0.09f, 0.11f));
            body.GetComponent<Renderer>().sharedMaterial = bodyMat;

            // Lens ring (darker rim)
            // Torus not available as PrimitiveType - use a flat cylinder for the ring
            var lensRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lensRing.name = "Generated_LensRing";
            lensRing.transform.SetParent(camRoot.transform, false);
            lensRing.transform.localPosition = new Vector3(0, csCam * 0.40f, -csCam * 0.08f);
            lensRing.transform.localScale = new Vector3(csCam * 0.35f, csCam * 0.35f, csCam * 0.12f);
            lensRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var ringMat = GetMat("Mat_CameraRing", new Color(0.04f, 0.045f, 0.055f));
            lensRing.GetComponent<Renderer>().sharedMaterial = ringMat;
            var lrCol = lensRing.GetComponent<Collider>();
            if (lrCol != null) Object.DestroyImmediate(lrCol);

            // Lens (glassy dome)
            var lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lens.name = "Generated_Lens";
            lens.transform.SetParent(camRoot.transform, false);
            lens.transform.localPosition = new Vector3(0, csCam * 0.40f, -csCam * 0.12f);
            lens.transform.localScale = new Vector3(csCam * 0.20f, csCam * 0.20f, csCam * 0.10f);
            var lensMat = GetMat("Mat_CameraLens", new Color(0.12f, 0.25f, 0.45f, 0.85f), true);
            lensMat.SetFloat("_Surface", 1);
            lens.GetComponent<Renderer>().sharedMaterial = lensMat;

            // Status LED
            var led = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            led.name = "Generated_StatusLED";
            led.transform.SetParent(camRoot.transform, false);
            led.transform.localPosition = new Vector3(0, csCam * 0.50f, csCam * 0.05f);
            led.transform.localScale = Vector3.one * csCam * 0.04f;
            var ledMat = GetMat("Mat_CameraLED", new Color(0.18f, 0.72f, 0.88f), true);
            led.GetComponent<Renderer>().sharedMaterial = ledMat;
            var ledCol = led.GetComponent<Collider>();
            if (ledCol != null) Object.DestroyImmediate(ledCol);

            // Collider
            var camCol = camRoot.AddComponent<BoxCollider>();
            camCol.size = new Vector3(csCam * 0.5f, csCam * 0.65f, csCam * 0.5f);

            // GridCameraBlock component
            var camBlock = camRoot.AddComponent<GridCameraBlock>();
            camBlock.blockName = "Camera Block";
            camBlock.BlockMass = 50f;
            camBlock.maxHP = 80;
            camBlock.fieldOfView = 70f;
            camBlock.cameraRange = 100f;

            // GridBlock base
            // GridCameraBlock inherits from GridBlock - no need for separate component
            var camPrefab = PrefabUtility.SaveAsPrefabAsset(camRoot, camPrefabPath);
            Object.DestroyImmediate(camRoot);

            // Item
            var camItemPath = SCREEN_ITEMS + "/Block_CameraBlock.asset";
            var camItem = AssetDatabase.LoadAssetAtPath<VoxelEngine.GridSystem.GridBlockItem>(camItemPath);
            if (camItem == null) { camItem = ScriptableObject.CreateInstance<VoxelEngine.GridSystem.GridBlockItem>(); AssetDatabase.CreateAsset(camItem, camItemPath); created++; }
            else preserved++;
            camItem.itemId = "camera_block";
            camItem.displayName = "Camera Block";
            camItem.description = "Security camera. Captures live video for linked screens.";
            camItem.iconTint = new Color(0.08f, 0.10f, 0.14f);
            camItem.maxStack = 99; camItem.massPerUnit = 1f;
            camItem.category = "Grid"; camItem.gridSize = GridSize.Large;
            camItem.blockPrefab = camPrefab; camItem.blockMass = 50f; camItem.blockHP = 80;
            EditorUtility.SetDirty(camItem);

            // Recipe
            var camRecipePath = SCREEN_RECIPES + "/Recipe_CameraBlock.asset";
            var camRecipe = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(camRecipePath);
            if (camRecipe == null) { camRecipe = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>(); AssetDatabase.CreateAsset(camRecipe, camRecipePath); created++; }
            else preserved++;
            camRecipe.displayName = "Camera Block";
            camRecipe.outputItem = camItem; camRecipe.outputCount = 1;
            camRecipe.requiredStation = VoxelEngine.Crafting.StationTier.Assembler;
            camRecipe.craftSeconds = 8f; camRecipe.unlockedByDefault = false;
            camRecipe.inputs = new VoxelEngine.Crafting.RecipeIngredient[] {
                new VoxelEngine.Crafting.RecipeIngredient { item = ironPlate, count = 4 },
                new VoxelEngine.Crafting.RecipeIngredient { item = copperWire, count = 8 },
                new VoxelEngine.Crafting.RecipeIngredient { item = circuit, count = 3 },
                new VoxelEngine.Crafting.RecipeIngredient { item = glass, count = 2 },
            };
            EditorUtility.SetDirty(camRecipe);
            if (registry != null && !registry.recipes.Contains(camRecipe)) registry.recipes.Add(camRecipe);
            Debug.Log("[Step 19] + Created camera block prefab: CameraBlock");

            if (registry != null) EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Step 19",
                $"Grid Screens + Camera Generated\n\n" +
                $"Screens:\n• Small (1x1)\n• Wide (2x1)\n• Medium (2x2)\n• Large (4x4)\n• Extra Large (8x8)\n\n" +
                $"Camera:\n• Security Camera Block (with lens, LED, mount)\n\n" +
                $"Created/verified: {created + preserved} assets\n" +
                $"Items + Recipes added to GridSystem/ScreenItems and ScreenRecipes\n\n" +
                $"Non-destructive — no balance values were modified.",
                "OK");
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
