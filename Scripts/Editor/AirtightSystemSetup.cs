// Assets/Scripts/VoxelEngine/Editor/AirtightSystemSetup.cs
//
// Step 60: AIRTIGHT ROOMS, PRESSURE & AIR VENTS — non-destructive authoring.
//
//   • Authors the AIR VENT grid block (prefab, grid item, recipe) in Large and Small
//     grid sizes. Vents pressurise or depressurise the sealed room they face using
//     oxygen from the grid gas network.
//   • Marks every existing sliding / vault door prefab airtight so already-built
//     ships gain pressure hulls without being rebuilt. Existing balance values,
//     slide tuning, motion settings and power draws are preserved verbatim.
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
using VoxelEngine.Pressure;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    public static class AirtightSystemSetup
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";
        private const string GRID_ROOT  = ASSET_ROOT + "/GridSystem";
        private const string PREFABS    = GRID_ROOT + "/Prefabs";
        private const string MATS       = PREFABS + "/Mats";
        private const string ITEMS      = GRID_ROOT + "/Items";
        private const string RECIPES    = GRID_ROOT + "/Recipes";

        public static void RunStep60()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 60 — Airtight Rooms, Pressure & Air Vents setup started.");

            foreach (var f in new[] { GRID_ROOT, PREFABS, MATS, ITEMS, RECIPES })
                EnsureFolder(f);

            var ironPlate  = FindItem("Item_IronPlate");
            var copperWire = FindItem("Item_CopperWire");
            var circuit    = FindItem("Item_Circuit");
            var steelPlate = FindItem("Item_SteelPlate");

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            var utilNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(ASSET_ROOT + "/Research/Nodes/res_grid_utilities.asset");

            int created = 0, preserved = 0;
            var recipes = new List<RecipeDefinition>();
            var items = new List<ItemDefinition>();

            // ── Air Vent (Large + Small grid) ──────────────────────────────────
            AuthorVent(GridSize.Large, "Grid_AirVent_Large", "gitem_air_vent_large",
                "Air Vent", 45f, 320f, 24f,
                new (ItemDefinition, int)[] { (ironPlate, 4), (copperWire, 4), (circuit, 1) },
                ref created, ref preserved, recipes, items);

            AuthorVent(GridSize.Small, "Grid_AirVent_Small", "gitem_air_vent_small",
                "Air Vent (Small)", 12f, 90f, 10f,
                new (ItemDefinition, int)[] { (ironPlate, 2), (copperWire, 2), (circuit, 1) },
                ref created, ref preserved, recipes, items);

            // ── Ventilation Unit (full-block chassis, Large + Small grid) ──────
            // Occupies a whole cell, services every adjacent compartment and carries a
            // suit-tank dock, so it is the hub of a proper life-support deck.
            AuthorVent(GridSize.Large, "Grid_VentilationUnit_Large", "gitem_vent_unit_large",
                "Ventilation Unit", 140f, 900f, 90f,
                new (ItemDefinition, int)[] { (steelPlate, 6), (ironPlate, 8), (copperWire, 6), (circuit, 3) },
                ref created, ref preserved, recipes, items, fullBlock: true);

            AuthorVent(GridSize.Small, "Grid_VentilationUnit_Small", "gitem_vent_unit_small",
                "Ventilation Unit (Small)", 38f, 260f, 36f,
                new (ItemDefinition, int)[] { (steelPlate, 2), (ironPlate, 3), (copperWire, 3), (circuit, 2) },
                ref created, ref preserved, recipes, items, fullBlock: true);

            // ── Retrofit existing doors as airtight bulkheads ──────────────────
            int doorsSealed = 0, doorsPreserved = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PREFABS }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponent<GridSlidingDoor>() == null) continue;

                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var door = contents.GetComponent<GridSlidingDoor>();
                    if (door != null && !door.airtight)
                    {
                        door.airtight = true;           // only the seal flag is touched
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        doorsSealed++;
                    }
                    else doorsPreserved++;
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

            EditorUtility.DisplayDialog("Voxel Engine — Airtight Rooms & Pressure (Step 60)",
                "Airtight rooms, pressure and air vents authored (non-destructive):\n\n" +
                "• Air Vent blocks: " + created + " created, " + preserved + " preserved\n" +
                "• Doors made airtight: " + doorsSealed + " (" + doorsPreserved + " already sealed)\n" +
                "• Recipes registered and linked to Grid Utilities research\n\n" +
                "Ventilation Units (full-block) service EVERY sealed room they touch and refill a "+
                "docked oxygen tank — place one at a junction between compartments.\n\n" +
                "Runtime: enclose a volume with airtight blocks and closed doors, place an Air Vent " +
                "in the wall, feed it oxygen from the grid gas network, and set it to PRESSURISE. " +
                "A pressurised room is breathable without a sealed suit — even in hard vacuum.\n\n" +
                "Existing balance values, slide tuning and power draws were preserved.",
                "OK");
        }

        private static void AuthorVent(GridSize size, string prefabName, string itemId,
            string displayName, float mass, float hp, float flow,
            (ItemDefinition item, int count)[] inputs,
            ref int created, ref int preserved,
            List<RecipeDefinition> recipes, List<ItemDefinition> items,
            bool fullBlock = false)
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
                GridBlockMeshBuilder.Build(visuals,
                    fullBlock ? GridBlockMeshBuilder.Style.AirVentFull : GridBlockMeshBuilder.Style.AirVent,
                    size, new Color(0.30f, 0.62f, 0.78f));
            }
            finally { GridBlockMeshBuilder.MaterialPersister = null; }

            float cs = size.CellSize();
            var col = root.GetComponent<BoxCollider>();
            if (col == null) col = root.AddComponent<BoxCollider>();
            col.center = Vector3.zero;
            // The panel vent is a thin wall insert; the plant occupies the whole cell.
            col.size = fullBlock ? new Vector3(cs, cs, cs) : new Vector3(cs, cs, cs * 0.32f);

            var vent = root.GetComponent<GridAirVent>();
            if (vent == null) vent = root.AddComponent<GridAirVent>();
            vent.blockName = displayName;
            vent.airtight = true;
            vent.fullBlock = fullBlock;
            if (fullBlock && vent.suitRefillLitresPerSecond <= 0f) vent.suitRefillLitresPerSecond = 40f;
            // Preserve authored balance: only fill in unset values.
            if (vent.flowLitresPerSecond <= 0f) vent.flowLitresPerSecond = flow;
            if (vent.targetPressureAtm <= 0f) vent.targetPressureAtm = 1.0f;
            if (vent.activeWatts <= 0f)
                vent.activeWatts = fullBlock ? (size == GridSize.Large ? 180f : 70f)
                                             : (size == GridSize.Large ? 60f : 25f);
            if (vent.idleWatts <= 0f)
                vent.idleWatts = fullBlock ? (size == GridSize.Large ? 12f : 5f)
                                           : (size == GridSize.Large ? 4f : 2f);
            if (vent.BlockMass <= 0f) vent.BlockMass = mass;
            if (vent.maxHP <= 0f) vent.maxHP = hp;

            // Gas ports: without these a gas pipe has nothing to magnet onto, so the
            // vent could never be plumbed. Oxygen reaches a vent ONLY through pipes.
            EnsureGasPorts(root, size);

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
            item.description = fullBlock
                ? "Full-cell ventilation plant. Pressurises or depressurises every sealed room touching it, and refills a docked oxygen tank from the grid gas network."
                : "Bulkhead air vent. Pressurises or depressurises the sealed room it faces using oxygen from the grid gas network.";
            item.iconTint = new Color(0.35f, 0.75f, 0.92f);
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
                recipe.craftSeconds = size == GridSize.Large ? 6f : 3f;
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

        /// <summary>
        /// Authors the six cardinal gas ports a pipe can snap to. Ports are named with
        /// the shared <c>Port_GasIO</c> prefix so the existing gas topology, pipe visual
        /// builder and wrench tooling all recognise them with no special-casing.
        /// </summary>
        private static void EnsureGasPorts(GameObject root, GridSize size)
        {
            const string prefix = "Port_GasIO";
            float cs = size.CellSize();
            float half = cs * 0.5f;
            float depth = cs * 0.16f;

            Material mat = GetOrCreatePortMaterial();

            var ports = new (string name, Vector3 pos, Vector3 euler, Vector3 outward)[]
            {
                (prefix + "_N",      new Vector3(0f, 0f, -depth), new Vector3(90f, 0f, 0f), Vector3.back),
                (prefix + "_S",      new Vector3(0f, 0f,  depth), new Vector3(90f, 0f, 0f), Vector3.forward),
                (prefix + "_E",      new Vector3( half * 0.92f, 0f, 0f), new Vector3(0f, 0f, 90f), Vector3.right),
                (prefix + "_W",      new Vector3(-half * 0.92f, 0f, 0f), new Vector3(0f, 0f, 90f), Vector3.left),
                (prefix + "_Top",    new Vector3(0f,  half * 0.92f, 0f), Vector3.zero,             Vector3.up),
                (prefix + "_Bottom", new Vector3(0f, -half * 0.92f, 0f), new Vector3(180f, 0f, 0f), Vector3.down),
            };

            foreach (var (name, pos, euler, outward) in ports)
            {
                var existing = root.transform.Find(name);
                GameObject port;
                if (existing != null)
                {
                    port = existing.gameObject;      // preserve any hand-moved port
                }
                else
                {
                    port = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    port.name = name;
                    port.transform.SetParent(root.transform, false);
                    port.transform.localScale = new Vector3(cs * 0.10f, cs * 0.022f, cs * 0.10f);
                    var col = port.GetComponent<Collider>();
                    if (col != null) Object.DestroyImmediate(col);
                    var renderer = port.GetComponent<Renderer>();
                    if (renderer != null && mat != null) renderer.sharedMaterial = mat;
                    port.transform.localPosition = pos;
                    port.transform.localRotation = Quaternion.Euler(euler);
                }

                var facing = port.GetComponent<VoxelEngine.Maritime.MaritimePortFacing>();
                if (facing == null) facing = port.AddComponent<VoxelEngine.Maritime.MaritimePortFacing>();
                facing.localOutward = outward;
            }
        }

        private static Material GetOrCreatePortMaterial()
        {
            EnsureFolder(MATS);
            const string matPath = MATS + "/AirVent_GasPortMarker.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null) return mat;

            var color = new Color(0.45f, 0.85f, 1.0f, 1f);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = "AirVent_GasPortMarker", color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.5f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.75f);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 0.45f);
            }
            AssetDatabase.CreateAsset(mat, matPath);
            return AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        private static ItemDefinition FindItem(string assetName)
        {
            foreach (string guid in AssetDatabase.FindAssets(assetName + " t:ItemDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/" + assetName + ".asset", System.StringComparison.Ordinal)) continue;
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
