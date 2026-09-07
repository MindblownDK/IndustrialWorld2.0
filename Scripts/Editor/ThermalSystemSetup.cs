// Assets/Scripts/VoxelEngine/Editor/ThermalSystemSetup.cs
//
// Step 61: BLOCK THERMAL SIMULATION, ATMOSPHERIC ENTRY & HEAT SHIELDS — non-destructive.
//
//   • Authors the HEAT SHIELD grid block (prefab, grid item, recipe) in Large and
//     Small grid sizes. Shields ablate to protect themselves and the block behind
//     them during atmospheric entry.
//   • Attaches GridThermalSystem to every existing grid prefab that can fly, so
//     already-built ships gain thermal simulation without being rebuilt.
//   • Links the recipes to the Grid Utilities research node and registers them in
//     the RecipeRegistry + ItemPersistenceCatalog.
//   • Fully re-runnable: nothing is deleted, nothing authored is overwritten.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Research;
using VoxelEngine.Thermal;

namespace VoxelEngine.EditorTools
{
    public static class ThermalSystemSetup
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";
        private const string GRID_ROOT  = ASSET_ROOT + "/GridSystem";
        private const string PREFABS    = GRID_ROOT + "/Prefabs";
        private const string MATS       = PREFABS + "/Mats";
        private const string ITEMS      = GRID_ROOT + "/Items";
        private const string RECIPES    = GRID_ROOT + "/Recipes";

        public static void RunStep61()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 61 — Thermal simulation, entry heating & heat shields started.");

            foreach (var f in new[] { GRID_ROOT, PREFABS, MATS, ITEMS, RECIPES })
                EnsureFolder(f);

            var ironPlate  = FindItem("Item_IronPlate");
            var steelPlate = FindItem("Item_SteelPlate");
            var circuit    = FindItem("Item_Circuit");

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            var utilNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(ASSET_ROOT + "/Research/Nodes/res_grid_utilities.asset");

            int created = 0, preserved = 0;
            var recipes = new List<RecipeDefinition>();
            var items = new List<ItemDefinition>();

            // ── Heat Shield (Large + Small grid) ───────────────────────────────
            AuthorHeatshield(GridSize.Large, "Grid_Heatshield_Large", "gitem_heatshield_large",
                "Heat Shield", 90f, 640f, 1000f,
                new (ItemDefinition, int)[] { (steelPlate, 5), (ironPlate, 4), (circuit, 1) },
                ref created, ref preserved, recipes, items);

            AuthorHeatshield(GridSize.Small, "Grid_Heatshield_Small", "gitem_heatshield_small",
                "Heat Shield (Small)", 24f, 180f, 320f,
                new (ItemDefinition, int)[] { (steelPlate, 2), (ironPlate, 2), (circuit, 1) },
                ref created, ref preserved, recipes, items);

            // ── Retrofit existing grid prefabs with thermal simulation ─────────
            // Any prefab carrying a GridEntity gets the component; it is inert and
            // effectively free until the hull actually heats up.
            int gridsWired = 0, gridsPreserved = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PREFABS }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponent<GridEntity>() == null) continue;

                if (asset.GetComponent<GridThermalSystem>() != null) { gridsPreserved++; continue; }

                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (contents.GetComponent<GridThermalSystem>() == null)
                    {
                        contents.AddComponent<GridThermalSystem>();
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        gridsWired++;
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }

            // ── Registry / research / persistence wiring ───────────────────────
            foreach (var recipe in recipes)
            {
                if (recipe == null) continue;
                if (registry != null && !registry.recipes.Contains(recipe))
                {
                    registry.recipes.Add(recipe);
                    EditorUtility.SetDirty(registry);
                }
            }

            if (utilNode != null)
            {
                var list = new List<RecipeDefinition>(utilNode.unlocksRecipes ?? new RecipeDefinition[0]);
                bool changed = false;
                foreach (var recipe in recipes)
                    if (recipe != null && !list.Contains(recipe)) { list.Add(recipe); changed = true; }
                if (changed)
                {
                    utilNode.unlocksRecipes = list.ToArray();
                    EditorUtility.SetDirty(utilNode);
                }
            }

            EnsureItemsPersisted(items);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Thermal Simulation & Heat Shields (Step 61)",
                "Thermal system authored.\n\n" +
                "• Assets created: " + created + " (" + preserved + " existing preserved)\n" +
                "• Grid prefabs given thermal simulation: " + gridsWired +
                " (" + gridsPreserved + " already wired)\n" +
                "• Recipes registered and linked to Grid Utilities research\n\n" +
                "Runtime: hull blocks now track a real temperature driven by planetary ambient, " +
                "atmospheric entry heating and running thrusters. Above 800 °C blocks burn. " +
                "Mount Heat Shields on the leading face of a re-entry vehicle: they ablate a " +
                "finite charge to protect themselves and the block directly behind them.\n\n" +
                "Existing balance values, block HP and power draws were preserved.",
                "OK");

            Debug.Log("[VoxelEngineSetupWindow] Step 61 complete.");
        }

        private static void AuthorHeatshield(GridSize size, string prefabName, string itemId,
            string displayName, float mass, float hp, float ablator,
            (ItemDefinition item, int count)[] inputs,
            ref int created, ref int preserved,
            List<RecipeDefinition> recipes, List<ItemDefinition> items)
        {
            string prefabPath = PREFABS + "/" + prefabName + ".prefab";
            bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            var root = existing ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(prefabName);
            root.name = prefabName;

            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                    Object.DestroyImmediate(child.gameObject);
            }

            int matIdx = 0;
            GridBlockMeshBuilder.MaterialPersister = (mat, _) =>
            {
                string mp = $"{MATS}/{prefabName}_{matIdx++}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(mp) != null) AssetDatabase.DeleteAsset(mp);
                AssetDatabase.CreateAsset(mat, mp);
                return AssetDatabase.LoadAssetAtPath<Material>(mp);
            };

            var visuals = new GameObject("Generated_Visuals");
            visuals.transform.SetParent(root.transform, false);
            try
            {
                GridBlockMeshBuilder.Build(visuals, GridBlockMeshBuilder.Style.Heatshield, size,
                    new Color(0.24f, 0.22f, 0.26f));
            }
            finally { GridBlockMeshBuilder.MaterialPersister = null; }

            float cs = size.CellSize();
            var col = root.GetComponent<BoxCollider>();
            if (col == null) col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, cs * 0.36f);
            col.size = new Vector3(cs, cs, cs * 0.28f);

            var shield = root.GetComponent<GridHeatshield>();
            if (shield == null) shield = root.AddComponent<GridHeatshield>();
            shield.blockName = displayName;
            // Preserve authored balance: only fill in unset values.
            if (shield.ablatorCapacity <= 0f) shield.ablatorCapacity = ablator;
            if (shield.ablatorRemaining < 0f) shield.ablatorRemaining = shield.ablatorCapacity;
            if (shield.heatTransmission <= 0f) shield.heatTransmission = ThermalRules.HeatshieldTransmission;
            if (shield.BlockMass <= 0f) shield.BlockMass = mass;
            if (shield.maxHP <= 0f) shield.maxHP = hp;

            var prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);

            string itemPath = ITEMS + "/GItem_" + prefabName.Replace("Grid_", string.Empty) + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(itemPath);
            bool newItem = item == null;
            if (newItem)
            {
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                AssetDatabase.CreateAsset(item, itemPath);
                created++;
            }
            else preserved++;

            item.itemId = itemId;
            item.displayName = displayName;
            item.description = "Ablative heat shield. Absorbs atmospheric entry heat for itself and the block directly behind it, burning away a finite ablator charge.";
            item.iconTint = new Color(0.85f, 0.42f, 0.24f);
            if (item.maxStack <= 0) item.maxStack = 99;
            if (item.massPerUnit <= 0f) item.massPerUnit = size == GridSize.Large ? 2f : 0.5f;
            item.category = "Grid";
            item.gridSize = size;
            item.blockPrefab = prefabAsset;
            if (item.blockMass <= 0f) item.blockMass = mass;
            if (item.blockHP <= 0f) item.blockHP = hp;
            EditorUtility.SetDirty(item);
            items.Add(item);

            string recipePath = RECIPES + "/Recipe_" + prefabName.Replace("Grid_", string.Empty) + ".asset";
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recipePath);
            bool newRecipe = recipe == null;
            if (newRecipe)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, recipePath);
                created++;
            }
            else preserved++;

            recipe.displayName = displayName;
            recipe.outputItem = item;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            if (newRecipe)
            {
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = size == GridSize.Large ? 7f : 3f;
                recipe.unlockedByDefault = false;
            }
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var list = new List<RecipeIngredient>();
                foreach (var (ing, count) in inputs)
                    if (ing != null) list.Add(new RecipeIngredient { item = ing, count = count });
                recipe.inputs = list.ToArray();
            }
            EditorUtility.SetDirty(recipe);
            recipes.Add(recipe);
        }

        private static ItemDefinition FindItem(string assetName)
        {
            foreach (string guid in AssetDatabase.FindAssets(assetName + " t:ItemDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != assetName) continue;
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item != null) return item;
            }
            return null;
        }

        private static void EnsureItemsPersisted(List<ItemDefinition> items)
        {
            const string catalogPath = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                EnsureFolder("Assets/Resources");
                EnsureFolder("Assets/Resources/VoxelEngine");
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }

            bool changed = false;
            foreach (var item in items)
            {
                if (item != null && !catalog.items.Contains(item))
                {
                    catalog.items.Add(item);
                    changed = true;
                }
            }
            if (changed) EditorUtility.SetDirty(catalog);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
#endif
