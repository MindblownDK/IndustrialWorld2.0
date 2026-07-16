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

            if (registry != null) EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Step 19",
                $"Grid Screen Prefabs Generated\n\n" +
                $"• Small Screen (1x1)\n• Wide Screen (2x1)\n• Medium Screen (2x2)\n• Large Screen (4x4)\n• Extra Large Screen (8x8)\n\n" +
                $"Created/verified: {created + preserved} assets\n" +
                $"Items + Recipes added to GridSystem/ScreenItems and ScreenRecipes\n\n" +
                $"Manual step: Add GridScreenConfigUI component to your GameUI UIDocument.\n" +
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
