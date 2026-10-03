#if UNITY_EDITOR
// Assets/Scripts/Editor/Storage/StorageNetworkPolishSetup.cs
//
// Step 109 - Storage network polish (14.41.0).
// Authors, non-destructively:
//   - the External Storage bridge block (ExternalStorageBlock component)
//     + BlockItem + Assembler recipe - chests and lone drawers join the
//     network through it
//   - a full visual overhaul of the Storage Importer, Storage Exporter and
//     External Storage prefabs: network-appliance bodies with faceplates,
//     RJ45-style port sockets, LED strips and direction chevrons replace
//     the old flat-colour cubes (only "Generated_" children are rebuilt;
//     custom additions survive, the legacy plain cube mesh is hidden)
//   - refreshed Data Pipe item description (shape wheel + plug-head lore)
// Create if missing, reconnect if existing.

using UnityEngine;
using UnityEditor;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class StorageNetworkPolishSetup
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";
        private const string ROOT = ASSET_ROOT + "/Storage/Network";
        private const string ITEMS = ROOT + "/Items";
        private const string RECIPES = ROOT + "/Recipes";
        private const string PREFABS = ROOT + "/Prefabs";
        private const string MATS = PREFABS + "/Mats";

        private static int _created, _touched;

        public static void RunStep109()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 109 - Storage network polish started.");
            _created = 0; _touched = 0;

            foreach (var folder in new[] { ASSET_ROOT + "/Storage", ROOT, ITEMS, RECIPES, PREFABS, MATS })
                EnsureFolder(folder);

            var registry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            var steelPlate = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_SteelPlate.asset");
            var circuit = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_Circuit.asset");
            var copperWire = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_CopperWire.asset");

            // ══════════════════════════════════════════════════════════
            //  1) EXTERNAL STORAGE - the chest/drawer bridge
            // ══════════════════════════════════════════════════════════

            string extPrefabPath = PREFABS + "/ExternalStorage.prefab";
            bool extExisted = AssetDatabase.LoadAssetAtPath<GameObject>(extPrefabPath) != null;
            var extRoot = extExisted
                ? PrefabUtility.LoadPrefabContents(extPrefabPath)
                : new GameObject("ExternalStorage");
            extRoot.name = "ExternalStorage";

            ClearGenerated(extRoot);
            BuildApplianceVisual(extRoot, DeviceStyle.ExternalStorage);

            var extCol = extRoot.GetComponent<BoxCollider>();
            if (extCol == null) extCol = extRoot.AddComponent<BoxCollider>();
            extCol.center = Vector3.zero;
            extCol.size = new Vector3(0.56f, 0.56f, 0.56f);

            if (extRoot.GetComponent<VoxelEngine.Storage.ExternalStorageBlock>() == null)
                extRoot.AddComponent<VoxelEngine.Storage.ExternalStorageBlock>();

            PrefabUtility.SaveAsPrefabAsset(extRoot, extPrefabPath);
            if (extExisted) { PrefabUtility.UnloadPrefabContents(extRoot); _touched++; }
            else { Object.DestroyImmediate(extRoot); _created++; }
            var extPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(extPrefabPath);

            var extItem = GetItem<BlockItem>("Item_ExternalStorage");
            if (string.IsNullOrWhiteSpace(extItem.itemId)) extItem.itemId = "external_storage";
            if (string.IsNullOrWhiteSpace(extItem.displayName)) extItem.displayName = "External Storage";
            extItem.description = "Bridges PHYSICAL containers into the mass-storage network. Place it flush " +
                                  "against a Chest or a lone Storage Drawer (or pipe it in) and terminals see, " +
                                  "count and use those items directly. Set the access mode (insert + extract, " +
                                  "extract only, insert only) and a fill priority on the block. Drawer " +
                                  "Controllers don't need it - they join the network on their own.";
            if (extItem.iconTint == Color.white) extItem.iconTint = new Color(0.35f, 0.65f, 0.95f);
            if (string.IsNullOrWhiteSpace(extItem.category) || extItem.category == "Misc") extItem.category = "Storage";
            if (extItem.maxStack <= 1 || extItem.maxStack == 900) extItem.maxStack = 10;
            if (extItem.massPerUnit <= 0f || Mathf.Approximately(extItem.massPerUnit, 1f)) extItem.massPerUnit = 12f;
            extItem.placedPrefab = extPrefab;
            if (extItem.blockHealth <= 0 || extItem.blockHealth == 100) extItem.blockHealth = 300;
            EditorUtility.SetDirty(extItem);

            EnsureRecipe(registry, "Recipe_ExternalStorage", extItem, "External Storage",
                new[] { (circuit, 2), (steelPlate, 2), (copperWire, 4) }, seconds: 5f);

            // ══════════════════════════════════════════════════════════
            //  2) IMPORTER / EXPORTER VISUAL OVERHAUL
            // ══════════════════════════════════════════════════════════

            OverhaulDeviceVisual(ASSET_ROOT + "/Survival/StorageBlocks/Block_StorageImporter.asset", DeviceStyle.Importer);
            OverhaulDeviceVisual(ASSET_ROOT + "/Survival/StorageBlocks/Block_StorageExporter.asset", DeviceStyle.Exporter);

            // ══════════════════════════════════════════════════════════
            //  3) DATA PIPE description refresh (shape wheel, 14.41.0)
            // ══════════════════════════════════════════════════════════

            var pipeItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ITEMS + "/Item_DataPipe.asset");
            if (pipeItem != null)
            {
                pipeItem.description = "Wired link of the mass-storage network, now a real data cable: slim " +
                                       "braided trunk, plug heads on every open end and phosphor pulse rings. " +
                                       "Hold it and press the build-wheel key to pick one of nine fittings - " +
                                       "straight runs (V + scroll for 1-5 m), elbows, risers, S-curves, compound " +
                                       "bends and 4/6-way hubs. Plug heads snap onto other pipes and link to any " +
                                       "touching storage device.";
                EditorUtility.SetDirty(pipeItem);
                _touched++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Step 109] Storage network polish complete - {_created} created, {_touched} touched. " +
                      "External Storage bridges chests/drawers into the network; importer, exporter and the " +
                      "bridge now wear proper network-appliance bodies; the Data Pipe gained the shape wheel.");
        }

        // ────────────────────────────────────────────────────────────
        //  DEVICE VISUAL KIT
        // ────────────────────────────────────────────────────────────

        private enum DeviceStyle { Importer, Exporter, ExternalStorage }

        private static void OverhaulDeviceVisual(string blockAssetPath, DeviceStyle style)
        {
            var blockItem = AssetDatabase.LoadAssetAtPath<BlockItem>(blockAssetPath);
            if (blockItem == null || blockItem.placedPrefab == null)
            {
                Debug.LogWarning($"[Step 109] Missing asset: {blockAssetPath} - run the earlier storage steps first.");
                return;
            }
            string prefabPath = AssetDatabase.GetAssetPath(blockItem.placedPrefab);
            if (string.IsNullOrEmpty(prefabPath)) return;

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                // Hide the legacy flat-colour cube (child "Mesh") instead of
                // destroying it - older saves and custom tweaks survive.
                var legacyMesh = root.transform.Find("Mesh");
                if (legacyMesh != null)
                {
                    var r = legacyMesh.GetComponent<Renderer>();
                    if (r != null) r.enabled = false;
                }
                ClearGenerated(root);
                BuildApplianceVisual(root, style);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                _touched++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>The shared "network appliance" look: graphite body, inset
        /// faceplate, RJ45-style port socket with glow insert, LED strip and a
        /// style-specific symbol (chevrons in/out, link brackets).</summary>
        private static void BuildApplianceVisual(GameObject root, DeviceStyle style)
        {
            Color accent = style switch
            {
                DeviceStyle.Importer        => new Color(0.30f, 0.85f, 0.45f),
                DeviceStyle.Exporter        => new Color(0.95f, 0.55f, 0.25f),
                _                           => new Color(0.35f, 0.65f, 0.95f)
            };

            var bodyMat  = GetMat("Mat_NetDevice_Body",  new Color(0.14f, 0.16f, 0.18f), metallic: 0.45f);
            var plateMat = GetMat("Mat_NetDevice_Plate", new Color(0.22f, 0.25f, 0.28f), metallic: 0.25f);
            var portMat  = GetMat("Mat_NetDevice_Port",  new Color(0.06f, 0.07f, 0.08f), metallic: 0.10f);
            var accentMat = GetMat($"Mat_NetDevice_Accent_{style}", accent, metallic: 0.30f, emissive: true);

            var vis = new GameObject("Generated_DeviceVisual");
            vis.transform.SetParent(root.transform, false);

            GameObject Box(string name, Vector3 pos, Vector3 size, Material mat, Transform parent = null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(parent != null ? parent : vis.transform, false);
                go.transform.localPosition = pos;
                go.transform.localScale = size;
                var rr = go.GetComponent<Renderer>();
                if (rr != null) rr.sharedMaterial = mat;
                var cc = go.GetComponent<Collider>();
                if (cc != null) Object.DestroyImmediate(cc);
                return go;
            }

            // Body + top/bottom accent trims.
            Box("Body", Vector3.zero, new Vector3(0.52f, 0.52f, 0.52f), bodyMat);
            Box("TrimTop", new Vector3(0f, 0.245f, 0f), new Vector3(0.54f, 0.035f, 0.54f), accentMat);
            Box("TrimBottom", new Vector3(0f, -0.245f, 0f), new Vector3(0.54f, 0.035f, 0.54f), accentMat);

            // Front faceplate (+Z) with LED strip.
            Box("Faceplate", new Vector3(0f, 0.02f, 0.267f), new Vector3(0.44f, 0.40f, 0.015f), plateMat);
            for (int i = 0; i < 3; i++)
                Box($"Led_{i}", new Vector3(-0.14f + i * 0.14f, 0.20f, 0.278f),
                    new Vector3(0.05f, 0.022f, 0.012f), accentMat);

            // RJ45-style port socket on the BACK (-Z) - where the pipe plugs in.
            Box("PortBezel", new Vector3(0f, 0f, -0.268f), new Vector3(0.22f, 0.18f, 0.02f), portMat);
            Box("PortGlow", new Vector3(0f, 0f, -0.279f), new Vector3(0.13f, 0.06f, 0.012f), accentMat);

            // Style symbol on the faceplate.
            switch (style)
            {
                case DeviceStyle.Importer:
                    // Chevrons pointing INTO the device (left to right).
                    Chevron(Box, new Vector3(-0.10f, -0.04f, 0.279f), accentMat, flip: false);
                    Chevron(Box, new Vector3(0.02f, -0.04f, 0.279f), accentMat, flip: false);
                    break;
                case DeviceStyle.Exporter:
                    // Chevrons pointing OUT of the device (right to left).
                    Chevron(Box, new Vector3(0.10f, -0.04f, 0.279f), accentMat, flip: true);
                    Chevron(Box, new Vector3(-0.02f, -0.04f, 0.279f), accentMat, flip: true);
                    break;
                default:
                    // Link brackets: [ = ] - the bridge symbol.
                    Box("LinkL", new Vector3(-0.11f, -0.04f, 0.279f), new Vector3(0.025f, 0.16f, 0.012f), accentMat);
                    Box("LinkR", new Vector3(0.11f, -0.04f, 0.279f), new Vector3(0.025f, 0.16f, 0.012f), accentMat);
                    Box("LinkBar1", new Vector3(0f, 0.00f, 0.279f), new Vector3(0.14f, 0.025f, 0.012f), accentMat);
                    Box("LinkBar2", new Vector3(0f, -0.08f, 0.279f), new Vector3(0.14f, 0.025f, 0.012f), accentMat);
                    break;
            }

            // Little rack feet.
            Box("FootL", new Vector3(-0.20f, -0.275f, 0f), new Vector3(0.08f, 0.03f, 0.46f), portMat);
            Box("FootR", new Vector3(0.20f, -0.275f, 0f), new Vector3(0.08f, 0.03f, 0.46f), portMat);
        }

        private static void Chevron(System.Func<string, Vector3, Vector3, Material, Transform, GameObject> box,
            Vector3 center, Material mat, bool flip)
        {
            float d = flip ? -1f : 1f;
            // Two angled bars forming a ">" (or "<" when flipped).
            var up = box($"Chev_{center.x}_a", center + new Vector3(0f, 0.045f, 0f),
                new Vector3(0.085f, 0.025f, 0.012f), mat, null);
            up.transform.localRotation = Quaternion.Euler(0f, 0f, -35f * d);
            var dn = box($"Chev_{center.x}_b", center + new Vector3(0f, -0.045f, 0f),
                new Vector3(0.085f, 0.025f, 0.012f), mat, null);
            dn.transform.localRotation = Quaternion.Euler(0f, 0f, 35f * d);
        }

        // ────────────────────────────────────────────────────────────
        //  SHARED HELPERS (step-108 conventions)
        // ────────────────────────────────────────────────────────────

        private static void ClearGenerated(GameObject root)
        {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                    Object.DestroyImmediate(child.gameObject);
            }
        }

        private static Material GetMat(string name, Color color, float metallic = 0f, bool emissive = false)
        {
            string path = MATS + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { _touched++; return existing; }
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
            _created++;
            return mat;
        }

        private static TItem GetItem<TItem>(string assetName) where TItem : ItemDefinition
        {
            string path = ITEMS + "/" + assetName + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<TItem>(path);
            if (item != null) { _touched++; return item; }
            item = ScriptableObject.CreateInstance<TItem>();
            AssetDatabase.CreateAsset(item, path);
            _created++;
            return item;
        }

        private static void EnsureRecipe(VoxelEngine.Crafting.RecipeRegistry registry,
            string assetName, ItemDefinition output, string displayName,
            (ItemDefinition item, int count)[] inputs, float seconds, int outputCount = 1)
        {
            string path = RECIPES + "/" + assetName + ".asset";
            var recipe = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(path);
            bool isNew = recipe == null;
            if (isNew)
            {
                recipe = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, path);
                _created++;
            }
            else _touched++;

            if (string.IsNullOrWhiteSpace(recipe.displayName)) recipe.displayName = displayName;
            recipe.outputItem = output;
            if (recipe.outputCount <= 0) recipe.outputCount = outputCount;
            if (isNew)
            {
                recipe.outputCount = outputCount;
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
