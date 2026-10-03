#if UNITY_EDITOR
// Assets/Scripts/Editor/Storage/StorageOverhaulSetup.cs
//
// Step 108 - Mass-storage overhaul (14.40.0).
// Authors, non-destructively:
//   - the Data Pipe placed-block prefab (DataCable component - the wired
//     link of the storage network) + BlockItem + Assembler recipe
//   - the handheld Wireless Terminal item (WirelessTerminalItem) + recipe
//   - rebrands ordered by design: Server Rack -> "Server Controller",
//     Wireless Storage Terminal block -> "Wireless Transmitter"
//   - balance ordered by design: RAM modules stack to 8, CPUs to 16
//   - refreshed descriptions for Server Controller, NAS and Power Station
//   - zeroes the legacy PowerConsumer on the Server Controller prefab
//     (Power Stations are the system's only grid input now)
// Create if missing, reconnect if existing. Explicitly ordered rebrands and
// balance values are applied; everything else is only set when missing.

using UnityEngine;
using UnityEditor;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class StorageOverhaulSetup
    {
        public static void RunStep108()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 108 - Mass-storage overhaul setup started.");

            const string ASSET_ROOT = "Assets/VoxelEngineAssets";
            const string ROOT = ASSET_ROOT + "/Storage/Network";
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
                (ItemDefinition item, int count)[] inputs, float seconds, int outputCount = 1)
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

            // ══════════════════════════════════════════════════════════
            //  1) DATA PIPE - the wired link of the storage network
            // ══════════════════════════════════════════════════════════

            var pipeMat = GetMat("Mat_DataPipe", new Color(0.16f, 0.30f, 0.20f), metallic: 0.45f);

            string pipePrefabPath = PREFABS + "/DataPipe.prefab";
            bool pipeExisted = AssetDatabase.LoadAssetAtPath<GameObject>(pipePrefabPath) != null;
            var pipeRoot = pipeExisted
                ? PrefabUtility.LoadPrefabContents(pipePrefabPath)
                : new GameObject("DataPipe");
            pipeRoot.name = "DataPipe";

            // Regenerate only our own children; custom additions survive.
            for (int i = pipeRoot.transform.childCount - 1; i >= 0; i--)
            {
                var child = pipeRoot.transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                    Object.DestroyImmediate(child.gameObject);
            }

            // Ghost/preview core: slightly smaller than DataCable's runtime
            // core (0.35), so it hides inside the live visual - the player
            // still sees something while aiming the placement.
            {
                var core = GameObject.CreatePrimitive(PrimitiveType.Cube);
                core.name = "Generated_GhostCore";
                core.transform.SetParent(pipeRoot.transform, false);
                core.transform.localPosition = Vector3.zero;
                core.transform.localScale = new Vector3(0.30f, 0.30f, 0.30f);
                var r = core.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = pipeMat;
                var c = core.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }

            var pipeCol = pipeRoot.GetComponent<BoxCollider>();
            if (pipeCol == null) pipeCol = pipeRoot.AddComponent<BoxCollider>();
            pipeCol.center = Vector3.zero;
            pipeCol.size = new Vector3(0.38f, 0.38f, 0.38f);

            if (pipeRoot.GetComponent<VoxelEngine.Networks.DataCable>() == null)
                pipeRoot.AddComponent<VoxelEngine.Networks.DataCable>();

            PrefabUtility.SaveAsPrefabAsset(pipeRoot, pipePrefabPath);
            if (pipeExisted) { PrefabUtility.UnloadPrefabContents(pipeRoot); preserved++; }
            else { Object.DestroyImmediate(pipeRoot); created++; }
            var pipePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(pipePrefabPath);

            var pipeItem = GetItem<BlockItem>("Item_DataPipe");
            if (string.IsNullOrWhiteSpace(pipeItem.itemId)) pipeItem.itemId = "data_pipe";
            if (string.IsNullOrWhiteSpace(pipeItem.displayName)) pipeItem.displayName = "Data Pipe";
            pipeItem.description = "Wired link of the mass-storage network. Run Data Pipes between the Server " +
                                   "Controller, NAS shelves, Power Stations, terminals and the rest of the " +
                                   "system - storage blocks only connect through pipes or by touching each other. " +
                                   "Pipes auto-link to adjacent data hardware with a clear line of sight.";
            if (pipeItem.iconTint == Color.white) pipeItem.iconTint = new Color(0.30f, 0.85f, 0.40f);
            if (string.IsNullOrWhiteSpace(pipeItem.category) || pipeItem.category == "Misc") pipeItem.category = "Storage";
            if (pipeItem.maxStack <= 1) pipeItem.maxStack = 50;
            if (pipeItem.massPerUnit <= 0f || Mathf.Approximately(pipeItem.massPerUnit, 1f)) pipeItem.massPerUnit = 2f;
            pipeItem.placedPrefab = pipePrefab;
            if (pipeItem.blockHealth <= 0 || pipeItem.blockHealth == 100) pipeItem.blockHealth = 80;
            EditorUtility.SetDirty(pipeItem);

            EnsureRecipe("Recipe_DataPipe", pipeItem, "Data Pipe",
                new[] { (copperWire, 2), (circuit, 1) }, seconds: 2f, outputCount: 4);

            // ══════════════════════════════════════════════════════════
            //  2) HANDHELD WIRELESS TERMINAL
            // ══════════════════════════════════════════════════════════

            var handheld = GetItem<VoxelEngine.Storage.WirelessTerminalItem>("Item_WirelessTerminal");
            if (string.IsNullOrWhiteSpace(handheld.itemId)) handheld.itemId = "wireless_terminal";
            if (string.IsNullOrWhiteSpace(handheld.displayName)) handheld.displayName = "Wireless Terminal";
            handheld.description = "Handheld uplink to your mass-storage network. Carry it in your inventory and " +
                                   "stay inside a powered Wireless Transmitter's range to browse storage, " +
                                   "shift-click items into the network, craft from stored materials and build " +
                                   "straight from storage. Wireless access is never global: the network owner " +
                                   "always has it, teammates only when the owner shares it on the Security Block.";
            if (handheld.iconTint == Color.white) handheld.iconTint = new Color(0.35f, 0.80f, 0.95f);
            handheld.maxStack = 1;
            if (handheld.massPerUnit <= 0f || Mathf.Approximately(handheld.massPerUnit, 1f)) handheld.massPerUnit = 1.5f;
            EditorUtility.SetDirty(handheld);

            EnsureRecipe("Recipe_WirelessHandheldTerminal", handheld, "Wireless Terminal",
                new[] { (circuit, 4), (steelPlate, 2), (copperWire, 6) }, seconds: 6f);

            // ══════════════════════════════════════════════════════════
            //  3) ORDERED REBRANDS + BALANCE on existing assets
            // ══════════════════════════════════════════════════════════

            void Rebrand(string path, string newName, string newDescription)
            {
                var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (it == null) { Debug.LogWarning($"[Step 108] Missing asset: {path} - run the earlier storage steps first."); return; }
                it.displayName = newName;
                it.description = newDescription;
                EditorUtility.SetDirty(it);
                preserved++;
            }

            Rebrand(ASSET_ROOT + "/Survival/StorageBlocks/Block_ServerRack.asset",
                "Server Controller",
                "The brain of the mass-storage network - exactly ONE per system. Holds up to 4 RAM " +
                "modules (pattern slots) and 1 CPU (craft speed). Disks live in NAS shelves and PSUs " +
                "in Power Stations; connect everything with Data Pipes or by touching blocks. Two " +
                "controllers on one network conflict and the whole system goes dark.");

            Rebrand(ASSET_ROOT + "/Survival/StorageBlocks/Block_NASBlock.asset",
                "NAS",
                "Network-attached storage shelf with 8 drive bays. Insert Storage Disks to expand the " +
                "network's capacity; each bay shows its own fill level. Higher-priority shelves fill " +
                "first. Disks remember their contents when pulled out.");

            Rebrand(ASSET_ROOT + "/Survival/StorageBlocks/Block_Powerstation.asset",
                "Power Station",
                "The storage system's only grid input. Fit up to 4 PSU modules to set how many watts " +
                "this station can feed the network, then wire it to your power grid like any machine. " +
                "The Server Controller splits the real system draw across every linked station - too " +
                "little total PSU rating and the system browns out.");

            Rebrand(ASSET_ROOT + "/Industrial/Blocks/Block_WirelessStorageTerminal.asset",
                "Wireless Transmitter",
                "Broadcast relay for the mass-storage network. Link it to the system with Data Pipes " +
                "or touching blocks; players carrying a handheld Wireless Terminal can then reach the " +
                "network from anywhere inside its range. It relays access - it stores nothing itself.");

            void SetStack(string path, int stack)
            {
                var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (it == null) { Debug.LogWarning($"[Step 108] Missing asset: {path}"); return; }
                it.maxStack = stack;
                EditorUtility.SetDirty(it);
                preserved++;
            }

            // Ordered balance: RAM stacks to 8, CPUs to 16.
            SetStack(ASSET_ROOT + "/Survival/StorageItems/RAM_4.asset", 8);
            SetStack(ASSET_ROOT + "/Survival/StorageItems/RAM_16.asset", 8);
            SetStack(ASSET_ROOT + "/Survival/StorageItems/CPU_1.asset", 16);
            SetStack(ASSET_ROOT + "/Survival/StorageItems/CPU_2.asset", 16);
            SetStack(ASSET_ROOT + "/Survival/StorageItems/CPU_4.asset", 16);

            // ══════════════════════════════════════════════════════════
            //  4) PREFAB HYGIENE - controller pays no grid bill anymore
            // ══════════════════════════════════════════════════════════

            void ZeroLegacyPowerConsumer(string blockAssetPath)
            {
                var blockItem = AssetDatabase.LoadAssetAtPath<BlockItem>(blockAssetPath);
                if (blockItem == null || blockItem.placedPrefab == null) return;
                string prefabPath = AssetDatabase.GetAssetPath(blockItem.placedPrefab);
                if (string.IsNullOrEmpty(prefabPath)) return;
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var pc = root.GetComponentInChildren<VoxelEngine.Power.PowerConsumer>(true);
                    if (pc != null && pc.wattsPerSecond != 0f)
                    {
                        pc.wattsPerSecond = 0f;
                        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                preserved++;
            }

            ZeroLegacyPowerConsumer(ASSET_ROOT + "/Survival/StorageBlocks/Block_ServerRack.asset");
            ZeroLegacyPowerConsumer(ASSET_ROOT + "/Industrial/Blocks/Block_WirelessStorageTerminal.asset");

            // The transmitter is a pure relay now: guarantee the
            // WirelessTransmitter component and strip the legacy wireless
            // StorageTerminal the old design carried (editor-time prefab
            // surgery is safe, unlike at runtime).
            {
                var txItem = AssetDatabase.LoadAssetAtPath<BlockItem>(ASSET_ROOT + "/Industrial/Blocks/Block_WirelessStorageTerminal.asset");
                if (txItem != null && txItem.placedPrefab != null)
                {
                    string prefabPath = AssetDatabase.GetAssetPath(txItem.placedPrefab);
                    var root = PrefabUtility.LoadPrefabContents(prefabPath);
                    try
                    {
                        bool changed = false;
                        if (root.GetComponentInChildren<VoxelEngine.Storage.WirelessTransmitter>(true) == null)
                        {
                            root.AddComponent<VoxelEngine.Storage.WirelessTransmitter>();
                            changed = true;
                        }
                        var legacyTerm = root.GetComponentInChildren<VoxelEngine.Storage.StorageTerminal>(true);
                        if (legacyTerm != null)
                        {
                            Object.DestroyImmediate(legacyTerm, true);
                            changed = true;
                        }
                        if (changed) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                    preserved++;
                }
            }

            // Power Station NEEDS a PowerConsumer (it is the grid input).
            {
                var psItem = AssetDatabase.LoadAssetAtPath<BlockItem>(ASSET_ROOT + "/Survival/StorageBlocks/Block_Powerstation.asset");
                if (psItem != null && psItem.placedPrefab != null)
                {
                    string prefabPath = AssetDatabase.GetAssetPath(psItem.placedPrefab);
                    var root = PrefabUtility.LoadPrefabContents(prefabPath);
                    try
                    {
                        if (root.GetComponentInChildren<VoxelEngine.Power.PowerConsumer>(true) == null)
                        {
                            var pc = root.AddComponent<VoxelEngine.Power.PowerConsumer>();
                            pc.wattsPerSecond = 1f; // idle heartbeat; runtime assigns the real load
                            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                        }
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                    preserved++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Step 108] Mass-storage overhaul complete - {created} created, {preserved} touched. " +
                      "Layout: Server Controller (RAM+CPU) + NAS shelves (disks) + Power Stations (PSUs, grid-wired) " +
                      "linked by Data Pipes or touching blocks. Craft the handheld Wireless Terminal for remote access.");
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
