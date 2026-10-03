#if UNITY_EDITOR
// Assets/Scripts/Editor/Storage/SecurityBlockSetup.cs
//
// Step 107 - Mass-storage Security Block (14.39.0).
// Authors, non-destructively:
//   - the Security Block placed-block prefab (cabinet + status light +
//     SecurityBlock component + PowerConsumer)
//   - the Security Block item (BlockItem) + Assembler recipe
// Create if missing, reconnect if existing; authored balance values are
// never overwritten. The status light is tinted at runtime by the
// SecurityBlock component (red = armed, dark = no power).

using UnityEngine;
using UnityEditor;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class SecurityBlockSetup
    {
        public static void RunStep107()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 107 - Mass-storage Security Block setup started.");

            const string ASSET_ROOT = "Assets/VoxelEngineAssets";
            const string ROOT = ASSET_ROOT + "/Storage/Security";
            const string ITEMS = ROOT + "/Items";
            const string RECIPES = ROOT + "/Recipes";
            const string PREFABS = ROOT + "/Prefabs";
            const string MATS = PREFABS + "/Mats";

            foreach (var folder in new[] { ASSET_ROOT + "/Storage", ROOT, ITEMS, RECIPES, PREFABS, MATS })
                EnsureFolder(folder);

            int created = 0, preserved = 0;

            Material GetMat(string name, Color color, float metallic = 0f, bool emissive = false)
            {
                string path = MATS + "/" + name + ".mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null) { preserved++; return existing; }
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat = new Material(shader) { name = name, color = color };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", metallic > 0.5f ? 0.6f : 0.35f);
                if (emissive)
                {
                    mat.EnableKeyword("_EMISSION");
                    if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 1.8f);
                }
                AssetDatabase.CreateAsset(mat, path);
                created++;
                return mat;
            }

            var registry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            var steelPlate = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_SteelPlate.asset");
            var circuit = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_Circuit.asset");
            var copperWire = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_CopperWire.asset");

            // ── shared helpers (BannerSetup pattern) ──────────────────────

            TItem GetItem<TItem>(string assetName) where TItem : ItemDefinition
            {
                string path = ITEMS + "/" + assetName + ".asset";
                var item = AssetDatabase.LoadAssetAtPath<TItem>(path);
                if (item != null) { preserved++; return item; }
                item = ScriptableObject.CreateInstance<TItem>();
                AssetDatabase.CreateAsset(item, path);
                created++;
                return item;
            }

            void EnsureRecipe(string assetName, ItemDefinition output, string displayName,
                (ItemDefinition item, int count)[] inputs, float seconds)
            {
                string path = RECIPES + "/" + assetName + ".asset";
                var recipe = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(path);
                bool isNew = recipe == null;
                if (isNew)
                {
                    recipe = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                    AssetDatabase.CreateAsset(recipe, path);
                    created++;
                }
                else preserved++;

                if (string.IsNullOrWhiteSpace(recipe.displayName)) recipe.displayName = displayName;
                recipe.outputItem = output;
                if (recipe.outputCount <= 0) recipe.outputCount = 1;
                if (isNew)
                {
                    recipe.requiredStation = VoxelEngine.Crafting.StationTier.Assembler;
                    recipe.craftSeconds = seconds;
                    recipe.unlockedByDefault = true;
                }
                else if (recipe.craftSeconds <= 0f) recipe.craftSeconds = seconds;

                if (recipe.inputs == null || recipe.inputs.Length == 0)
                {
                    var list = new System.Collections.Generic.List<VoxelEngine.Crafting.RecipeIngredient>();
                    foreach (var (item, count) in inputs)
                        if (item != null) list.Add(new VoxelEngine.Crafting.RecipeIngredient { item = item, count = count });
                    recipe.inputs = list.ToArray();
                }
                EditorUtility.SetDirty(recipe);
                if (registry != null && !registry.recipes.Contains(recipe))
                {
                    registry.recipes.Add(recipe);
                    EditorUtility.SetDirty(registry);
                }
            }

            GameObject LoadOrNewPrefabRoot(string prefabPath, string rootName, out bool existed)
            {
                existed = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
                var root = existed ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(rootName);
                root.name = rootName;
                // Regenerate only our own children; custom additions survive.
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var child = root.transform.GetChild(i);
                    if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                        Object.DestroyImmediate(child.gameObject);
                }
                return root;
            }

            void AddGeneratedPart(GameObject root, string name, PrimitiveType type,
                Vector3 pos, Vector3 scale, Material mat)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = pos;
                go.transform.localScale = scale;
                var renderer = go.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = mat;
                var collider = go.GetComponent<Collider>();
                if (collider != null) Object.DestroyImmediate(collider);
            }

            void SavePrefab(GameObject root, string prefabPath, bool existed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (existed) { PrefabUtility.UnloadPrefabContents(root); preserved++; }
                else { Object.DestroyImmediate(root); created++; }
            }

            // ── materials ─────────────────────────────────────────────────

            var cabinetMat = GetMat("Mat_SecurityCabinet", new Color(0.13f, 0.14f, 0.17f), metallic: 0.65f);
            var panelMat = GetMat("Mat_SecurityPanel", new Color(0.20f, 0.22f, 0.26f), metallic: 0.45f);
            var trimMat = GetMat("Mat_SecurityTrim", new Color(0.82f, 0.25f, 0.16f), metallic: 0.30f);
            var lightMat = GetMat("Mat_SecurityLight", new Color(0.95f, 0.16f, 0.12f), metallic: 0.10f, emissive: true);
            var keypadMat = GetMat("Mat_SecurityKeypad", new Color(0.05f, 0.06f, 0.08f), metallic: 0.20f);

            // ── Security Block (static placed block) ──────────────────────

            var root = LoadOrNewPrefabRoot(PREFABS + "/SecurityBlock.prefab", "SecurityBlock", out bool existed0);

            // Server-cabinet silhouette: body, inset front panel, warning
            // trim, keypad, and the runtime-tinted status light strip.
            AddGeneratedPart(root, "Generated_Cabinet", PrimitiveType.Cube,
                new Vector3(0f, 0.50f, 0f), new Vector3(0.58f, 0.98f, 0.48f), cabinetMat);
            AddGeneratedPart(root, "Generated_FrontPanel", PrimitiveType.Cube,
                new Vector3(0f, 0.52f, 0.235f), new Vector3(0.46f, 0.74f, 0.03f), panelMat);
            AddGeneratedPart(root, "Generated_TrimTop", PrimitiveType.Cube,
                new Vector3(0f, 0.96f, 0f), new Vector3(0.60f, 0.05f, 0.50f), trimMat);
            AddGeneratedPart(root, "Generated_TrimBase", PrimitiveType.Cube,
                new Vector3(0f, 0.035f, 0f), new Vector3(0.60f, 0.07f, 0.50f), trimMat);
            AddGeneratedPart(root, "Generated_StatusLight", PrimitiveType.Cube,
                new Vector3(0f, 0.82f, 0.252f), new Vector3(0.34f, 0.06f, 0.015f), lightMat);
            AddGeneratedPart(root, "Generated_Keypad", PrimitiveType.Cube,
                new Vector3(0.13f, 0.46f, 0.252f), new Vector3(0.12f, 0.17f, 0.015f), keypadMat);
            AddGeneratedPart(root, "Generated_LockSlot", PrimitiveType.Cube,
                new Vector3(-0.12f, 0.46f, 0.252f), new Vector3(0.14f, 0.05f, 0.015f), keypadMat);

            var col = root.GetComponent<BoxCollider>();
            if (col == null) col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.5f, 0f);
            col.size = new Vector3(0.6f, 1.0f, 0.5f);

            if (root.GetComponent<VoxelEngine.Storage.SecurityBlock>() == null)
                root.AddComponent<VoxelEngine.Storage.SecurityBlock>();
            var consumer = root.GetComponent<VoxelEngine.Power.PowerConsumer>();
            if (consumer == null)
            {
                consumer = root.AddComponent<VoxelEngine.Power.PowerConsumer>();
                consumer.wattsPerSecond = 40f; // only on create - balance tweaks survive
            }

            SavePrefab(root, PREFABS + "/SecurityBlock.prefab", existed0);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFABS + "/SecurityBlock.prefab");

            var item = GetItem<BlockItem>("Item_SecurityBlock");
            if (string.IsNullOrWhiteSpace(item.itemId)) item.itemId = "security_block";
            if (string.IsNullOrWhiteSpace(item.displayName)) item.displayName = "Security Block";
            if (string.IsNullOrWhiteSpace(item.description))
                item.description = "Guards the digital storage network while powered: every server rack and NAS " +
                                   "within 10 m refuses players the owner's access mode excludes - PRIVATE, TEAM " +
                                   "(default) or GLOBAL. Terminals on a guarded network are locked out too, wired " +
                                   "or wireless. There is no hacking: raiders must destroy the block or cut its power.";
            if (item.iconTint == Color.white) item.iconTint = new Color(0.90f, 0.25f, 0.18f);
            if (string.IsNullOrWhiteSpace(item.category) || item.category == "Misc") item.category = "Storage";
            if (item.maxStack <= 1) item.maxStack = 10;
            if (item.massPerUnit <= 0f || Mathf.Approximately(item.massPerUnit, 1f)) item.massPerUnit = 22f;
            item.placedPrefab = prefab;
            if (item.blockHealth <= 0 || item.blockHealth == 100) item.blockHealth = 400;
            EditorUtility.SetDirty(item);

            EnsureRecipe("Recipe_SecurityBlock", item, "Security Block",
                new[] { (steelPlate, 6), (circuit, 4), (copperWire, 8) }, seconds: 8f);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Step 107] Mass-storage Security Block complete - {created} created, {preserved} preserved. " +
                      "Place it within 10 m of a Server Rack, power it, and set the access mode in its panel.");
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
