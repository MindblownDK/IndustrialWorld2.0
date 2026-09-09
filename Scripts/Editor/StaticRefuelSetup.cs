// Assets/Scripts/VoxelEngine/Editor/StaticRefuelSetup.cs
//
// Step 67 — the STATIC REFUEL PAD (9.36.0-dev): a world-placed refuel island for bases that are not
// grids. Its own file for the same reason Step 66 is: this round is navigation, and the shared helpers
// in the engine-room setup are private to a class that has its own step numbers to protect.
//
// The step does not only author a model and an item. A pad that could not be fed is a decoration, so the
// step wires it into all four graphs the base itself runs on: a `PowerConsumer` on the wires, a
// `WaterTank` node on the pipes, a `GasTank` endpoint on the gas run, and a drum behind `PortConfig`
// faces that belts and item pipes push into and pull out of.
//
// Non-destructive, like every other authored step:
//   • an existing prefab is opened, has only missing components added, and keeps every tuned number;
//   • an existing block item keeps its stack size, mass, health and mining tier;
//   • an existing recipe keeps its station, craft time and ingredient list (inputs are filled once,
//     when empty, because an ingredient-less recipe is what a half-finished step leaves behind);
//   • an existing research node keeps its cost, label and tier; the step only adds the missing
//     prerequisite link and appends to its unlock list rather than replacing it.
// A second run creates nothing.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    public static class StaticRefuelSetup
    {
        private const string ASSET_ROOT    = "Assets/VoxelEngineAssets";
        private const string SURVIVAL_ROOT = ASSET_ROOT + "/Survival";
        private const string MISC_PREFABS  = SURVIVAL_ROOT + "/MiscPrefabs";
        private const string MISC_BLOCKS   = SURVIVAL_ROOT + "/MiscBlocks";
        private const string MISC_RECIPES  = SURVIVAL_ROOT + "/MiscRecipes";
        private const string MATS          = MISC_PREFABS + "/Mats";
        private const string NODES         = ASSET_ROOT + "/Research/Nodes";
        private const string TREE_PATH     = ASSET_ROOT + "/Research/ResearchTree.asset";
        private const string PAD_NODE_PATH = NODES + "/res_refuel_pads.asset";
        private const string AUTO_NODE_PATH = NODES + "/res_auto_run_pilot.asset";
        private const string UTILS_NODE_PATH = NODES + "/res_grid_utilities.asset";
        private const string CATALOG       = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        [MenuItem("Tools/Voxel Engine/Setup Step 67 — Static Refuel Pad")]
        public static void RunStep67Menu() => RunStep67();

        public static void RunStep67()
        {
            Debug.Log("[StaticRefuelSetup] Step 67 — static refuel pad started.");

            foreach (var f in new[] { SURVIVAL_ROOT, MISC_PREFABS, MATS, MISC_BLOCKS, MISC_RECIPES, NODES })
                EnsureFolder(f);

            var ironPlate  = FindItem("Item_IronPlate");
            var steelPlate = FindItem("Item_SteelPlate");
            var circuit    = FindItem("Item_Circuit");
            var rubber     = FindItem("Item_Rubber");
            var copperWire = FindItem("Item_CopperWire");
            var glass      = FindItem("Item_Glass");
            var registry   = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");

            int created = 0, preserved = 0;

            // ── The prefab: a low deck plate, a nozzle stub, and the pad's own components ──
            string prefabPath = MISC_PREFABS + "/RefuelPad.prefab";
            bool hadPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            var root = hadPrefab ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject("RefuelPad");
            root.name = "RefuelPad";

            if (!hadPrefab)
            {
                var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
                deck.name = "Deck";
                deck.transform.SetParent(root.transform, false);
                deck.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                deck.transform.localScale = new Vector3(2.4f, 0.12f, 2.4f);
                deck.GetComponent<Renderer>().sharedMaterial = MakeMat(MATS, "Mat_RefuelPad_Deck",
                    new Color(0.30f, 0.32f, 0.34f));

                var rim = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rim.name = "HazardRim";
                rim.transform.SetParent(root.transform, false);
                rim.transform.localPosition = new Vector3(0f, 0.13f, 0f);
                rim.transform.localScale = new Vector3(2.5f, 0.03f, 2.5f);
                rim.GetComponent<Renderer>().sharedMaterial = MakeMat(MATS, "Mat_RefuelPad_Rim",
                    new Color(0.90f, 0.58f, 0.12f));
                Object.DestroyImmediate(rim.GetComponent<Collider>());   // paint, not a wall

                var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pedestal.name = "NozzlePedestal";
                pedestal.transform.SetParent(root.transform, false);
                pedestal.transform.localPosition = new Vector3(0.86f, 0.52f, -0.86f);
                pedestal.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
                pedestal.GetComponent<Renderer>().sharedMaterial = MakeMat(MATS, "Mat_RefuelPad_Pedestal",
                    new Color(0.42f, 0.48f, 0.44f));

                // A collider the player can actually click, sized to the deck rather than to the
                // union of every child — a pad you cannot walk past is a wall with a label.
                var col = root.GetComponent<BoxCollider>();
                if (col == null) col = root.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 0.30f, 0f);
                col.size = new Vector3(2.4f, 0.62f, 2.4f);
            }

            if (root.GetComponent<VoxelEngine.Building.PlacedBlock>() == null)
                root.AddComponent<VoxelEngine.Building.PlacedBlock>();
            if (root.GetComponent<VoxelEngine.Power.PowerConsumer>() == null)
            {
                var pc = root.AddComponent<VoxelEngine.Power.PowerConsumer>();
                pc.wattsPerSecond = 0f;                 // idle: the pad asks only while it is pumping
                pc.connectRadius = 5f;
            }
            var pad = root.GetComponent<VoxelEngine.Navigation.StaticRefuelPad>();
            if (pad == null) pad = root.AddComponent<VoxelEngine.Navigation.StaticRefuelPad>();
            // Defaults are filled only when untouched, so a tuned pad keeps its tuning.
            if (pad.powerWatts <= 0.01f) pad.powerWatts = 24000f;
            if (pad.litresPerSecond <= 0.01f) pad.litresPerSecond = 40f;
            if (pad.itemSlotsPerSecond <= 0) pad.itemSlotsPerSecond = 2;
            if (pad.captureRadiusMetres <= 0.5f) pad.captureRadiusMetres = 3.5f;
            if (pad.visitTimeoutSeconds < 0f) pad.visitTimeoutSeconds = 600f;
            if (pad.drumSlots <= 0) pad.drumSlots = 6;
            if (pad.tankCapacityLitres <= 1f) pad.tankCapacityLitres = 4000f;
            if (pad.gasCapacity <= 1f) pad.gasCapacity = 4000f;

            // -- The pad's four supply lines, authored once so a placed pad is a member of all of them --
            // Item faces, in the quarry's shape: one face takes items off belts and pipes into the drum,
            // one face gives them back. Written only when the pad has no faces at all, because which way a
            // face points is exactly the kind of thing a player retunes in the panel.
            var ports = root.GetComponent<VoxelEngine.Transport.PortConfig>();
            if (ports == null) ports = root.AddComponent<VoxelEngine.Transport.PortConfig>();
            if (NoFaces(ports))
            {
                ports.ports = new VoxelEngine.Transport.PortConfig.FacePort[]
                {
                    new() { face = VoxelEngine.Transport.CubeFace.PosX, direction = VoxelEngine.Transport.PortDirection.Output, networkType = VoxelEngine.Transport.PortNetworkType.Any,   enabled = true },
                    new() { face = VoxelEngine.Transport.CubeFace.NegX, direction = VoxelEngine.Transport.PortDirection.Input,  networkType = VoxelEngine.Transport.PortNetworkType.Any,   enabled = true },
                    new() { face = VoxelEngine.Transport.CubeFace.PosZ, direction = VoxelEngine.Transport.PortDirection.None,   networkType = VoxelEngine.Transport.PortNetworkType.Any,   enabled = true },
                    new() { face = VoxelEngine.Transport.CubeFace.NegZ, direction = VoxelEngine.Transport.PortDirection.Input,  networkType = VoxelEngine.Transport.PortNetworkType.Power, enabled = true },
                    new() { face = VoxelEngine.Transport.CubeFace.PosY, direction = VoxelEngine.Transport.PortDirection.None,   networkType = VoxelEngine.Transport.PortNetworkType.Any,   enabled = true },
                    new() { face = VoxelEngine.Transport.CubeFace.NegY, direction = VoxelEngine.Transport.PortDirection.None,   networkType = VoxelEngine.Transport.PortNetworkType.Any,   enabled = true },
                };
                EditorUtility.SetDirty(ports);
            }
            if (root.GetComponent<VoxelEngine.Transport.ItemPortRouting>() == null)
            {
                var routing = root.AddComponent<VoxelEngine.Transport.ItemPortRouting>();
                routing.pushInterval = 0.5f;            // the engine's default, written out so this step's
                routing.pushPerTick = 8;                // intent survives a future change of that default
            }

            // The two buffer nodes live in the prefab, not only in code, so the fluid and gas graphs can see
            // a pad from the frame it is placed on, and so their positions are visible and editable.
            var tankNode = FindOrAddNode(root, "PadFuelTank", pad.tankNodeOffset);
            if (tankNode.GetComponent<VoxelEngine.Fluids.WaterTank>() == null)
            {
                var wt = tankNode.AddComponent<VoxelEngine.Fluids.WaterTank>();
                wt.capacityLitres = pad.tankCapacityLitres;
                EditorUtility.SetDirty(wt);
            }
            var gasNode = FindOrAddNode(root, "PadGasTank", pad.gasNodeOffset);
            if (gasNode.GetComponent<VoxelEngine.Gas.GasTank>() == null)
            {
                // The collider IS the registration: GasPipe finds world endpoints by probing colliders and
                // asking their parents for a GasTank, so a tank without one just holds gas.
                if (gasNode.GetComponent<Collider>() == null)
                {
                    var gc = gasNode.AddComponent<BoxCollider>();
                    gc.center = Vector3.zero;
                    gc.size = new Vector3(0.6f, 0.7f, 0.6f);
                }
                var gt = gasNode.AddComponent<VoxelEngine.Gas.GasTank>();
                gt.capacity = pad.gasCapacity;
                gt.selectedGasType = VoxelEngine.Gas.GasType.Hydrogen;
                EditorUtility.SetDirty(gt);
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (hadPrefab) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
            if (hadPrefab) preserved++; else created++;

            // ── The block item ───────────────────────────────────────────────
            string itemPath = MISC_BLOCKS + "/Block_RefuelPad.asset";
            var block = GetOrCreate<BlockItem>(itemPath, ref created, ref preserved);
            block.itemId = "block_refuel_pad";
            block.displayName = "Refuel Pad";
            block.description = "A ground refuel island for a base that is not a ship. Name it and it becomes a waymark: "
                + "shuttles and ground rigs fly or drive to that name, queue one at a time, and take power, hydrogen, "
                + "liquid fuel and cargo at the pad's rating. It is a member of the base's own graphs: a power "
                + "consumer on the wires, a tank node on the pipes, a gas endpoint on the run, and a drum behind "
                + "port faces that belts and item pipes fill and empty. A base that cannot sustain the load "
                + "refuses instead of trickle-charging.";
            block.iconTint = new Color(0.86f, 0.62f, 0.22f);
            if (block.maxStack <= 0) block.maxStack = 50;
            if (block.massPerUnit <= 0f) block.massPerUnit = 6f;
            block.category = "Logistics";
            block.placedPrefab = prefab;
            if (block.blockHealth <= 0) block.blockHealth = 900;
            if (block.miningTier <= 0) block.miningTier = 2;
            EditorUtility.SetDirty(block);

            // ── The recipe ───────────────────────────────────────────────────
            string recipePath = MISC_RECIPES + "/Recipe_RefuelPad.asset";
            var recipe = GetOrCreate<RecipeDefinition>(recipePath, ref created, ref preserved);
            recipe.displayName = "Refuel Pad";
            recipe.outputItem = block;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            if (recipe.requiredStation == StationTier.None && recipe.craftSeconds <= 0f)
            {
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 6f;
                recipe.unlockedByDefault = false;
            }
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var list = new List<RecipeIngredient>();
                Add(list, steelPlate, 3);
                Add(list, ironPlate, 2);
                Add(list, circuit, 1);
                Add(list, rubber, 2);          // a hose is a hose: optional content, added when it exists
                Add(list, copperWire, 2);
                Add(list, glass, 1);
                recipe.inputs = list.ToArray();
            }
            EditorUtility.SetDirty(recipe);
            if (registry != null && !registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }

            // ── Research: the pad is earned, and the auto-run pilot leans on it ──
            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(TREE_PATH);
            var utilsNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(UTILS_NODE_PATH);
            var autoNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(AUTO_NODE_PATH);

            var padNode = GetOrCreate<ResearchNode>(PAD_NODE_PATH, ref created, ref preserved);
            padNode.nodeId = "res_refuel_pads";
            if (string.IsNullOrEmpty(padNode.displayName) || padNode.displayName == "New Research")
                padNode.displayName = "Ground Refuel Pads";
            if (string.IsNullOrEmpty(padNode.description))
                padNode.description = "Concrete, a nozzle and a meter: refuel and recharge rigs that are standing on your base's own power, pipe and gas runs — no grid required.";
            if (padNode.category == ResearchCategory.Environment && padNode.subCategory == ResearchSubCategory.General)
            {
                padNode.subCategory = ResearchSubCategory.Logistics;
                padNode.tier = 3;
                padNode.iconTint = new Color(0.86f, 0.62f, 0.22f);
            }
            if (padNode.researchSeconds <= 0.01f) padNode.researchSeconds = 60f;

            var pre = new List<ResearchNode>(padNode.prerequisites ?? new ResearchNode[0]);
            if (utilsNode != null && !pre.Contains(utilsNode)) { pre.Add(utilsNode); padNode.prerequisites = pre.ToArray(); }
            var unlocks = new List<RecipeDefinition>(padNode.unlocksRecipes ?? new RecipeDefinition[0]);
            if (!unlocks.Contains(recipe)) { unlocks.Add(recipe); padNode.unlocksRecipes = unlocks.ToArray(); }
            EditorUtility.SetDirty(padNode);
            if (tree != null && !tree.nodes.Contains(padNode)) { tree.nodes.Add(padNode); EditorUtility.SetDirty(tree); }

            // The auto-run loop can now point at a ground pad, so that node asks for this one first.
            // Append-only, and only if the 9.35 node exists at all — a fresh project without Step 66
            // run yet must not get a dangling prerequisite.
            if (autoNode != null)
            {
                var autoPre = new List<ResearchNode>(autoNode.prerequisites ?? new ResearchNode[0]);
                if (!autoPre.Contains(padNode))
                {
                    autoPre.Add(padNode);
                    autoNode.prerequisites = autoPre.ToArray();
                    EditorUtility.SetDirty(autoNode);
                }
            }

            EnsureItemPersisted(block);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[StaticRefuelSetup] Step 67 complete — created " + created + ", preserved " + preserved + ".");
            EditorUtility.DisplayDialog("Voxel Engine — Static Refuel Pad (Step 67)",
                "Ground pads authored.\n\n" +
                "• Assets created: " + created + " (" + preserved + " existing preserved)\n" +
                "• REFUEL PAD — a world block (like the Quarry, not a grid block): deck plate, hazard rim,\n" +
                "  nozzle pedestal, and a name field that makes it a waymark\n" +
                "• It is a real consumer on the base's power network — 0 W idle, rated draw while pumping,\n" +
                "  and it refuses to serve if the base cannot sustain the load\n" +
                "• PadFuelTank (a fluid-network tank node) and PadGasTank (a gas endpoint, collider and all) are\n" +
                "  authored into the prefab, and the drum sits behind two faces: NegX takes from belts and pipes,\n" +
                "  PosX gives back\n" +
                "• GROUND REFUEL PADS research node, under Grid Utilities, and Auto-Run Pilot now requires it\n" +
                "• Nothing you have tuned was rewritten: pad watts, litres per second, reach, tank size and\n" +
                "  port faces all keep their authored values on a re-run", "OK");
        }

        // ── Small local helpers (the shared ones are private to another class) ──
        static T GetOrCreate<T>(string path, ref int created, ref int preserved) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) { preserved++; return a; }
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            created++;
            return a;
        }

        /// <summary>True when a `PortConfig` has nothing worth preserving: no array at all, or every face
        /// still `None`, which is what a freshly added component carries.</summary>
        static bool NoFaces(VoxelEngine.Transport.PortConfig ports)
        {
            var arr = ports.ports;
            if (arr == null || arr.Length == 0) return true;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i].direction != VoxelEngine.Transport.PortDirection.None) return false;
            return true;
        }

        static GameObject FindOrAddNode(GameObject root, string name, Vector3 localPosition)
        {
            var t = root.transform.Find(name);
            if (t != null) return t.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPosition;
            return go;
        }

        static void Add(List<RecipeIngredient> list, ItemDefinition item, int count)
        {
            if (item != null) list.Add(new RecipeIngredient { item = item, count = count });
        }

        static Material MakeMat(string folder, string name, Color c)
        {
            EnsureFolder(folder);
            string path = $"{folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { color = c };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash <= 0) return;
            string parent = path.Substring(0, slash), leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, leaf);
        }

        static ItemDefinition FindItem(string assetName)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { ASSET_ROOT }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == assetName)
                    return AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
            }
            return null;
        }

        static void EnsureItemPersisted(ItemDefinition item)
        {
            if (item == null) return;
            var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(CATALOG);
            if (catalog == null)
            {
                EnsureFolder("Assets/Resources");
                EnsureFolder("Assets/Resources/VoxelEngine");
                catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                AssetDatabase.CreateAsset(catalog, CATALOG);
            }
            if (!catalog.items.Contains(item))
            {
                catalog.items.Add(item);
                EditorUtility.SetDirty(catalog);
            }
        }
    }
}
