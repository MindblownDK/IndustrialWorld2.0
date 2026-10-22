#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    /// <summary>Non-destructive, editor-only setup/refresh for the 2x2x2 industrial cockpit shells, item and recipe.</summary>
    public static class CockpitShellSetup
    {
        private const string AssetRoot = "Assets/VoxelEngineAssets";
        private const string GridRoot = AssetRoot + "/GridSystem";
        private const string PrefabFolder = GridRoot + "/Prefabs";
        private const string ItemFolder = GridRoot + "/Items";
        private const string RecipeFolder = GridRoot + "/Recipes";
        private const string PrefabPath = PrefabFolder + "/Cockpit_Large.prefab";
        private const string ItemPath = ItemFolder + "/GItem_Cockpit.asset";
        private const string RecipePath = RecipeFolder + "/Recipe_GCockpit.asset";
        private const string MaterialFolder = PrefabFolder + "/Mats/CockpitShell_17_41";

        public static void RunStep123()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Cockpit Shell Setup", "Exit Play Mode before running setup.", "OK");
                return;
            }

            foreach (string folder in new[] { GridRoot, PrefabFolder, ItemFolder, RecipeFolder, MaterialFolder })
                EnsureFolder(folder);

            bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            GameObject root = existed
                ? PrefabUtility.LoadPrefabContents(PrefabPath)
                : new GameObject("Cockpit_Large");

            var previousMaterialPersister = GridBlockMeshBuilder.MaterialPersister;
            int materialIndex = 0;

            GridBlockMeshBuilder.MaterialPersister = (material, _) =>
            {
                if (material == null) return null;
                string path = MaterialFolder + "/CockpitShell_17_41_" + materialIndex.ToString("D2") + ".mat";
                materialIndex++;
                Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null)
                {
                    if (material.shader != null && existing.shader != material.shader)
                        existing.shader = material.shader;
                    existing.CopyPropertiesFromMaterial(material);
                    existing.shaderKeywords = material.shaderKeywords;
                    existing.renderQueue = material.renderQueue;
                    EditorUtility.SetDirty(existing);
                    Object.DestroyImmediate(material);
                    return existing;
                }

                material.name = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(material, path);
                return AssetDatabase.LoadAssetAtPath<Material>(path);
            };

            try
            {
                root.name = "Cockpit_Large";
                float cellSize = GridSize.Large.CellSize();

                BoxCollider box = root.GetComponent<BoxCollider>();
                if (box == null) box = root.AddComponent<BoxCollider>();

                GridCockpit cockpit = root.GetComponent<GridCockpit>();
                if (cockpit == null)
                {
                    cockpit = root.AddComponent<GridCockpit>();
                    cockpit.idleWatts = 50f;
                    cockpit.shellStyle = CockpitShellBuilder.StyleCrawler;
                }
                else
                {
                    if (cockpit.idleWatts <= 0f) cockpit.idleWatts = 50f;
                    if (cockpit.shellStyle == CockpitShellBuilder.StyleLegacy)
                        cockpit.shellStyle = CockpitShellBuilder.StyleCrawler;
                }

                var existingItem = AssetDatabase.LoadAssetAtPath<GridBlockItem>(ItemPath);
                Color bodyColor = existingItem != null ? existingItem.iconTint : new Color(0.20f, 0.40f, 0.80f);

                // Non-destructive rebuild: replaces only Cabin, Shell_Crawler, Shell_Cab and SeatAnchor
                // inside Generated_CockpitShell while leaving all custom prefab children untouched.
                CockpitShellBuilder.Build(root, cellSize, bodyColor, cockpit.shellStyle);
                CockpitShellBuilder.SyncColliderBounds(root, cellSize, cockpit.shellStyle);
                EditorUtility.SetDirty(cockpit);

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (savedPrefab == null)
                {
                    EditorUtility.DisplayDialog("Cockpit Shell Setup", "Unity could not save Cockpit_Large.prefab. Check the Console before retrying.", "OK");
                    return;
                }

                GridBlockItem item = EnsureCockpitItem(savedPrefab);
                VoxelEngineSetupWindow.EnsureItemPersistenceCatalog();
                EnsureCockpitRecipe(item);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[VoxelEngineSetupWindow] Cockpit Shell Setup complete — refreshed 2x2x2 Wraparound Harvester and Reinforced Heavy Cab shells + materials while preserving custom power, item, recipe, and child settings.");
                EditorUtility.DisplayDialog("Voxel Engine — Cockpit Shell Setup",
                    "2x2x2 Cockpits ready.\n\n• Wraparound Cockpit: harvester panoramic front, side & bottom floor glass with low curved MFD pod and lighter hull.\n• Reinforced Cockpit: heavy-duty machinery cab with bolted glacis armor, FOPS cage, unobstructed upper windows and high integrity.\n\nExisting custom power, item stats, recipes and custom prefab children were preserved.",
                    "OK");
            }
            finally
            {
                GridBlockMeshBuilder.MaterialPersister = previousMaterialPersister;
                if (existed) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        private static GridBlockItem EnsureCockpitItem(GameObject prefab)
        {
            GridBlockItem item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(ItemPath);
            bool created = item == null;
            if (created)
            {
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                AssetDatabase.CreateAsset(item, ItemPath);
                item.itemId = "cockpit_large";
                item.displayName = "Cockpit";
                item.description = "Spacious 2x2x2 seated industrial cockpit with two selectable shells on the build wheel: the Wraparound Harvester cab (front, side & floor observation glass) or the Reinforced Heavy Cab (bolted armor glacis, FOPS cage and high integrity).";
                item.iconTint = new Color(0.55f, 0.72f, 0.92f);
                item.maxStack = 10;
                item.gridSize = GridSize.Large;
                item.blockMass = 1500f;
                item.blockHP = 2000f;
                item.category = "Grid Blocks";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(item.itemId)) item.itemId = "cockpit_large";
                if (string.IsNullOrWhiteSpace(item.displayName)) item.displayName = "Cockpit";
                if (string.IsNullOrWhiteSpace(item.description))
                    item.description = "Spacious 2x2x2 seated industrial cockpit with selectable Wraparound Harvester and Reinforced Heavy Cab shells.";
                if (item.maxStack <= 0) item.maxStack = 10;
                if (item.blockMass <= 0f) item.blockMass = 1500f;
                if (item.blockHP <= 0f) item.blockHP = 2000f;
                if (string.IsNullOrWhiteSpace(item.category)) item.category = "Grid Blocks";
                item.gridSize = GridSize.Large;
            }

            item.blockPrefab = prefab;
            EditorUtility.SetDirty(item);
            return item;
        }

        private static void EnsureCockpitRecipe(GridBlockItem item)
        {
            RecipeDefinition recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(RecipePath);
            bool created = recipe == null;
            if (created)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, RecipePath);
                recipe.displayName = "Cockpit";
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 8f;
                recipe.unlockedByDefault = false;
            }

            if (string.IsNullOrWhiteSpace(recipe.displayName)) recipe.displayName = "Cockpit";
            recipe.outputItem = item;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var inputs = new List<RecipeIngredient>();
                var steel = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetRoot + "/Industrial/Items/Item_SteelPlate.asset");
                var circuit = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetRoot + "/Industrial/Items/Item_CircuitBoard.asset");
                var glass = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetRoot + "/Industrial/Items/Item_Glass.asset");
                if (steel != null) inputs.Add(new RecipeIngredient { item = steel, count = 10 });
                if (circuit != null) inputs.Add(new RecipeIngredient { item = circuit, count = 6 });
                if (glass != null) inputs.Add(new RecipeIngredient { item = glass, count = 6 });
                recipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(recipe);

            RecipeRegistry registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(AssetRoot + "/RecipeRegistry.asset");
            if (registry != null && !registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }
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
