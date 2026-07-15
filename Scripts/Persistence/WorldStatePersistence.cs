// Assets/Scripts/VoxelEngine/Persistence/WorldStatePersistence.cs
//
// Saves the dynamic world state (player + placed blocks + their containers) to a JSON
// sidecar next to the world's region files.
//
// Strategy:
//   - Placed blocks are identified at spawn time by their BlockItem (so we can reconstruct
//     by Instantiating the same prefab and re-applying tier/HP/contents).
//   - All ItemContainers are serialized as parallel lists of (itemAssetPath, count, durability).
//   - Player position + inventory live in their own SavedPlayer block.
//   - Save fires on quit/sceneUnload + when explicitly requested.
//
// Asset-path serialization: we use the item's AssetDatabase path in editor; in builds we
// fall back to ItemDefinition.itemId — for that to work, you must store every craftable
// item asset in Resources/ (or accept that builds lose items not in the registry).
// For now we use AssetDatabase + a Resources scan to remap on load.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Building.Tiered;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.Persistence
{
    public class WorldStatePersistence : MonoBehaviour
    {
        public static WorldStatePersistence Instance { get; private set; }

        // Item lookup cache for restore.
        private Dictionary<string, ItemDefinition>            _itemById   = new();
        private Dictionary<string, BlockItem>                 _blockById  = new();
        private Dictionary<string, TieredBlockDefinition>     _tieredById = new();

        private bool _loaded;
        private float _saveTimer;
        // Background autosave cadence now comes from GameSettings.AutosaveSeconds
        // (0 = disabled). Players change it live from the Settings → Saving tab.

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            BuildItemCache();
            // Restore IMMEDIATELY so that PlayerSpawner sees the saved position when it polls.
            LoadAll();
        }

        private void Update()
        {
            int interval = VoxelEngine.Settings.GameSettings.AutosaveSeconds;
            if (interval <= 0) { _saveTimer = 0f; return; } // autosave disabled
            _saveTimer += Time.deltaTime;
            if (_saveTimer >= interval) { _saveTimer = 0f; SaveAll(); }
        }

        private void OnApplicationQuit() => SaveAll();
        private void OnDestroy()
        {
            if (Instance == this) { SaveAll(); Instance = null; }
        }

        // ============================================================
        //                       ITEM CACHE
        // ============================================================
        private void BuildItemCache()
        {
            _itemById.Clear(); _blockById.Clear(); _tieredById.Clear();
            // Runtime-safe asset cache: rely on Resources-visible assets only.
            // This avoids hard dependencies on editor-only assemblies from the runtime asmdef.
            foreach (var it in Resources.LoadAll<ItemDefinition>(""))
            {
                if (!string.IsNullOrEmpty(it.itemId)) _itemById[it.itemId] = it;
                if (it is BlockItem bi && !string.IsNullOrEmpty(bi.itemId)) _blockById[bi.itemId] = bi;
            }
            foreach (var def in Resources.LoadAll<TieredBlockDefinition>(""))
                _tieredById[def.family.ToString()] = def;
        }

        // ============================================================
        //                          SAVE
        // ============================================================
        public void SaveAll()
        {
            if (Menu.WorldSession.Instance == null) return;
            string path = WorldStatePath();
            try
            {
                var save = new SaveData();
                SavePlayer(save);
                SavePlacedBlocks(save);
                SavePlacedTiered(save);
                SaveQuarries(save);
                File.WriteAllText(path, JsonUtility.ToJson(save, prettyPrint: true));
                Debug.Log($"[WorldState] Saved -> {path}");
            }
            catch (Exception ex) { Debug.LogError("[WorldState] Save failed: " + ex.Message); }
        }

        private string WorldStatePath()
        {
            var session = Menu.WorldSession.Instance;
            string worldName = !string.IsNullOrEmpty(session != null ? session.worldName : null)
                ? session.worldName : "DefaultWorld";
            string folder = Path.Combine(Application.persistentDataPath, "VoxelWorlds", worldName);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "world_state.json");
        }

        private void SavePlayer(SaveData save)
        {
            var inv = FindAnyObjectByType<Inventory>();
            if (inv == null) return;
            save.player = new SavedPlayer
            {
                pos = inv.transform.position,
                rotY = inv.transform.eulerAngles.y,
                container = SerializeContainer(inv.container),
                activeHotbarIndex = inv.activeHotbarIndex
            };
        }

        private void SavePlacedBlocks(SaveData save)
        {
            var placed = FindObjectsByType<PlacedBlock>(FindObjectsInactive.Exclude);
            foreach (var pb in placed)
            {
                if (pb == null || pb.Item == null) continue;
                var entry = new SavedPlacedBlock
                {
                    itemId = pb.Item.itemId,
                    pos = pb.transform.position,
                    rotY = pb.transform.eulerAngles.y,
                    hp = pb.Hp,
                    container = TryFindContainer(pb.gameObject)
                };
                var windPart = pb.GetComponent<VoxelEngine.Power.Wind.WindTurbinePart>();
                if (windPart != null) entry.windCondition = Mathf.Max(0.01f, windPart.condition);
                var conveyor = pb.GetComponentInChildren<VoxelEngine.Simulation.ConveyorBelt>(true);
                if (conveyor != null && !conveyor.autoShape)
                {
                    entry.hasExplicitConveyorShape = true;
                    entry.conveyorShape = (int)conveyor.shape;
                }
                save.placedBlocks.Add(entry);
            }
        }

        private void SavePlacedTiered(SaveData save)
        {
            var placed = FindObjectsByType<PlacedTieredBlock>(FindObjectsInactive.Exclude);
            foreach (var pb in placed)
            {
                if (pb == null || pb.definition == null) continue;
                save.placedTiered.Add(new SavedPlacedTiered
                {
                    family = pb.definition.family.ToString(),
                    tier   = (int)pb.tier,
                    pos    = pb.transform.position,
                    rotY   = pb.transform.eulerAngles.y,
                    hp     = pb.hp
                });
            }
        }

        private void SaveQuarries(SaveData save)
        {
            var quarries = FindObjectsByType<VoxelEngine.Transport.Quarry>(FindObjectsInactive.Exclude);
            foreach (var q in quarries)
            {
                if (q == null) continue;
                q.EnsureOutputPublic();
                save.quarries.Add(new SavedQuarry
                {
                    pos = q.transform.position,
                    rotY = q.transform.eulerAngles.y,
                    currentDepth = q.CurrentDepth,
                    cursorX = q.CursorX,
                    cursorZ = q.CursorZ,
                    phase = (int)q.Phase,
                    rangeLvl = q.InstalledRangeLevel,
                    speedLvl = q.InstalledSpeedLevel,
                    effLvl = q.InstalledEfficiencyLevel,
                    outputContainer = SerializeContainer(q.Output)
                });
            }
        }

        private void RestoreQuarries(SaveData save)
        {
            if (save.quarries == null) return;
            // Quarries are restored by finding already-placed quarry blocks (from RestorePlacedBlocks)
            // and applying their saved state.
            var quarries = FindObjectsByType<VoxelEngine.Transport.Quarry>(FindObjectsInactive.Exclude);
            foreach (var sq in save.quarries)
            {
                // Find the quarry closest to the saved position.
                VoxelEngine.Transport.Quarry best = null;
                float bestDist = 2f; // must be within 2m
                foreach (var q in quarries)
                {
                    float d = Vector3.Distance(q.transform.position, sq.pos);
                    if (d < bestDist) { bestDist = d; best = q; }
                }
                if (best != null)
                {
                    best.RestoreState(sq.currentDepth, sq.cursorX, sq.cursorZ, sq.phase, sq.rangeLvl, sq.speedLvl, sq.effLvl);
                    if (sq.outputContainer != null)
                    {
                        best.EnsureOutputPublic();
                        DeserializeInto(best.Output, sq.outputContainer);
                    }
                }
            }
        }

        // Multi-container blocks (furnace, electric furnace) store ALL their containers under one entry's `extraContainers`.
        // To keep the save schema simple we just check for the well-known component types.
        private SavedContainer TryFindContainer(GameObject go)
        {
            var drawer = go.GetComponentInChildren<VoxelEngine.Storage.StorageDrawer>();
            if (drawer != null)
            {
                var sc = SerializeMultiDrawer(drawer);
                AttachPortSnapshot(go, sc);
                return sc;
            }

            var display = go.GetComponentInChildren<VoxelEngine.Storage.StorageItemDisplayBlock>();
            if (display != null)
                return SerializeDisplayFilter(display);

            var drawerController = go.GetComponentInChildren<VoxelEngine.Storage.StorageDrawerController>();
            if (drawerController != null)
            {
                var sc = new SavedContainer();
                AttachPortSnapshot(go, sc);
                return sc;
            }

            var chest = go.GetComponentInChildren<Chest>();
            if (chest != null)
            {
                var sc = SerializeContainer(chest.container);
                if (sc != null) AttachPortSnapshot(go, sc);
                return sc;
            }

            var furnace = go.GetComponentInChildren<Furnace>();
            if (furnace != null)
            {
                var sc = SerializeMulti(furnace.inputC, furnace.fuelC, furnace.outputC);
                AttachPortSnapshot(go, sc);
                return sc;
            }

            var efurn = go.GetComponentInChildren<ElectricFurnace>();
            if (efurn != null)
            {
                var sc = SerializeMulti(efurn.inputC, efurn.outputC, efurn.upgradeC);
                AttachPortSnapshot(go, sc);
                return sc;
            }

            return null;
        }

        /// <summary>
        /// Capture the item-port config (faces + routing + filters) from any
        /// machine that carries an <see cref="VoxelEngine.Transport.ItemPortRouting"/>.
        /// </summary>
        private void AttachPortSnapshot(GameObject go, SavedContainer sc)
        {
            if (sc == null) return;
            var routing = go.GetComponentInChildren<VoxelEngine.Transport.ItemPortRouting>();
            if (routing == null) return;
            var snap = routing.CaptureSnapshot();
            if (snap != null && snap.HasData) sc.chestPort = snap;
        }

        private SavedContainer SerializeContainer(ItemContainer c)
        {
            if (c == null) return null;
            c.EnsureValid();
            var sc = new SavedContainer();
            for (int i = 0; i < c.Slots.Count; i++)
            {
                var s = c.GetSlot(i);
                sc.entries.Add(SerializeStack(s));
            }
            return sc;
        }

        private SavedContainer SerializeMulti(params ItemContainer[] containers)
        {
            var sc = new SavedContainer();
            foreach (var c in containers)
            {
                if (c == null) continue;
                c.EnsureValid();
                sc.containerSizes.Add(c.Slots.Count);
                for (int i = 0; i < c.Slots.Count; i++)
                {
                    var s = c.GetSlot(i);
                    sc.entries.Add(SerializeStack(s));
                }
            }
            return sc;
        }

        private SavedStack SerializeStack(ItemStack s)
        {
            var saved = new SavedStack
            {
                itemId = s == null || s.IsEmpty ? "" : s.item.itemId,
                count = s == null || s.IsEmpty ? 0 : s.count,
                durability = s == null || s.IsEmpty ? 0 : s.durability
            };
            if (s != null && s.payload is VoxelEngine.Storage.StorageDrawer.DrawerItemPayload payload)
            {
                saved.isPackedDrawer = true;
                saved.packedOriginalItemId = payload.originalItem != null ? payload.originalItem.itemId : saved.itemId;
                saved.drawerStoredItemId = payload.storedItem != null ? payload.storedItem.itemId : "";
                saved.drawerStoredCount = payload.storedCount;
                saved.drawerInstanceId = payload.instanceId;
                if (payload.upgrades != null)
                    foreach (var up in payload.upgrades)
                        saved.drawerUpgrades.Add(SerializeStack(up));
            }
            return saved;
        }

        private SavedContainer SerializeMultiDrawer(VoxelEngine.Storage.StorageDrawer drawer)
        {
            drawer.EnsureContainers();
            var sc = new SavedContainer();
            sc.containerSizes.Add(1);
            sc.entries.Add(new SavedStack
            {
                itemId = drawer.storedItem != null && drawer.storedCount > 0 ? drawer.storedItem.itemId : "",
                count = drawer.storedItem != null ? drawer.storedCount : 0,
                durability = 0
            });
            sc.containerSizes.Add(drawer.upgradeSlots.Size);
            for (int i = 0; i < drawer.upgradeSlots.Size; i++)
            {
                var s = drawer.upgradeSlots.GetSlot(i);
                sc.entries.Add(SerializeStack(s));
            }
            return sc;
        }

        private SavedContainer SerializeDisplayFilter(VoxelEngine.Storage.StorageItemDisplayBlock display)
        {
            var sc = new SavedContainer();
            sc.entries.Add(new SavedStack
            {
                itemId = display.filterItem != null ? display.filterItem.itemId : "",
                count = display.filterItem != null ? 1 : 0,
                durability = 0
            });
            return sc;
        }

        // ============================================================
        //                          LOAD
        // ============================================================
        public void LoadAll()
        {
            if (_loaded) return;
            string path = WorldStatePath();
            if (!File.Exists(path)) { _loaded = true; return; }

            try
            {
                var save = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (save == null) { _loaded = true; return; }

                RestorePlacedTiered(save);
                RestorePlacedBlocks(save);
                RestorePlayer(save);
                RestoreQuarries(save);
                Debug.Log($"[WorldState] Loaded {save.placedTiered.Count} tiered + {save.placedBlocks.Count} blocks from {path}");
            }
            catch (Exception ex) { Debug.LogError("[WorldState] Load failed: " + ex.Message); }
            _loaded = true;
        }

        private void RestorePlayer(SaveData save)
        {
            if (save.player == null) return;
            var inv = FindAnyObjectByType<Inventory>();
            if (inv == null) return;
            // Teleport.
            var cc = inv.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            inv.transform.position = save.player.pos;
            inv.transform.eulerAngles = new Vector3(0, save.player.rotY, 0);
            if (cc != null) cc.enabled = true;
            // Inventory.
            if (save.player.container != null) DeserializeInto(inv.container, save.player.container);
            inv.SetActiveHotbar(save.player.activeHotbarIndex);
        }

        private void RestorePlacedBlocks(SaveData save)
        {
            foreach (var sb in save.placedBlocks)
            {
                if (!_blockById.TryGetValue(sb.itemId, out var blockItem) || blockItem.placedPrefab == null) continue;
                var go = Instantiate(blockItem.placedPrefab, sb.pos, Quaternion.Euler(0, sb.rotY, 0));
                go.name = blockItem.displayName + " (restored)";
                if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<BoxCollider>();
                var pb = go.GetComponent<PlacedBlock>();
                if (pb == null) pb = go.AddComponent<PlacedBlock>();
                pb.Item = blockItem; pb.Hp = sb.hp;
                var conveyor = go.GetComponentInChildren<VoxelEngine.Simulation.ConveyorBelt>(true);
                if (conveyor != null && sb.hasExplicitConveyorShape
                    && System.Enum.IsDefined(typeof(VoxelEngine.Simulation.ConveyorShape), sb.conveyorShape))
                {
                    conveyor.SetBuildShape((VoxelEngine.Simulation.ConveyorShape)sb.conveyorShape);
                }
                var windPart = go.GetComponent<VoxelEngine.Power.Wind.WindTurbinePart>();
                if (windPart != null && sb.windCondition > 0f)
                    windPart.condition = Mathf.Clamp(sb.windCondition, 0f, 100f);
                if (blockItem.placedMaterial != null || blockItem.texture != null)
                {
                    var tex = go.AddComponent<BlockTexturizer>();
                    tex.overrideMaterial = blockItem.placedMaterial;
                    tex.overrideTexture  = blockItem.texture;
                }
                if (sb.container != null) RestoreContainer(go, sb.container);
            }
        }

        private void RestorePlacedTiered(SaveData save)
        {
            foreach (var ps in save.placedTiered)
            {
                if (!_tieredById.TryGetValue(ps.family, out var def)) continue;
                var prefab = def.GetPrefab((BuildTier)ps.tier);
                if (prefab == null) continue;
                var go = Instantiate(prefab, ps.pos, Quaternion.Euler(0, ps.rotY, 0));
                go.name = $"{def.displayName} ({(BuildTier)ps.tier}, restored)";
                var pb = go.GetComponent<PlacedTieredBlock>();
                if (pb == null) pb = go.AddComponent<PlacedTieredBlock>();
                pb.Initialize(def, (BuildTier)ps.tier);
                pb.hp = ps.hp > 0 ? ps.hp : pb.hp;
            }
        }

        private void RestoreContainer(GameObject go, SavedContainer sc)
        {
            var drawer = go.GetComponentInChildren<VoxelEngine.Storage.StorageDrawer>();
            if (drawer != null)
            {
                RestoreDrawer(drawer, sc);
                RestorePortSnapshot(go, sc);
                return;
            }

            var display = go.GetComponentInChildren<VoxelEngine.Storage.StorageItemDisplayBlock>();
            if (display != null)
            {
                if (sc != null && sc.entries.Count > 0 && !string.IsNullOrEmpty(sc.entries[0].itemId)
                    && _itemById.TryGetValue(sc.entries[0].itemId, out var item))
                    display.SetFilter(item);
                return;
            }

            var drawerController = go.GetComponentInChildren<VoxelEngine.Storage.StorageDrawerController>();
            if (drawerController != null)
            {
                RestorePortSnapshot(go, sc);
                return;
            }

            var chest = go.GetComponentInChildren<Chest>();
            if (chest != null)
            {
                DeserializeInto(chest.container, sc);
                RestorePortSnapshot(go, sc);
                return;
            }

            var furnace = go.GetComponentInChildren<Furnace>();
            if (furnace != null)
            {
                furnace.EnsureContainers();
                DeserializeMulti(sc, furnace.inputC, furnace.fuelC, furnace.outputC);
                RestorePortSnapshot(go, sc);
                return;
            }
            var efurn = go.GetComponentInChildren<ElectricFurnace>();
            if (efurn != null)
            {
                efurn.EnsureContainers();
                DeserializeMulti(sc, efurn.inputC, efurn.outputC, efurn.upgradeC);
                RestorePortSnapshot(go, sc);
                return;
            }
        }

        private void RestoreDrawer(VoxelEngine.Storage.StorageDrawer drawer, SavedContainer sc)
        {
            if (drawer == null || sc == null) return;
            drawer.EnsureContainers();
            if (sc.entries.Count > 0)
            {
                var e = sc.entries[0];
                if (!string.IsNullOrEmpty(e.itemId) && e.count > 0 && _itemById.TryGetValue(e.itemId, out var item))
                {
                    drawer.storedItem = item;
                    drawer.storedCount = e.count;
                }
                else
                {
                    drawer.storedItem = null;
                    drawer.storedCount = 0;
                }
            }
            int idx = sc.containerSizes.Count > 0 ? sc.containerSizes[0] : 1;
            for (int i = 0; i < drawer.upgradeSlots.Size && idx < sc.entries.Count; i++, idx++)
            {
                var e = sc.entries[idx];
                if (string.IsNullOrEmpty(e.itemId) || e.count <= 0) { drawer.upgradeSlots.SetSlot(i, new ItemStack()); continue; }
                if (!_itemById.TryGetValue(e.itemId, out var item)) { drawer.upgradeSlots.SetSlot(i, new ItemStack()); continue; }
                drawer.upgradeSlots.SetSlot(i, new ItemStack { item = item, count = e.count, durability = e.durability });
            }
            drawer.RefreshDisplay();
        }

        /// <summary>Restore item-port config onto any machine with ItemPortRouting.</summary>
        private void RestorePortSnapshot(GameObject go, SavedContainer sc)
        {
            if (sc == null || sc.chestPort == null || !sc.chestPort.HasData) return;
            var routing = go.GetComponentInChildren<VoxelEngine.Transport.ItemPortRouting>();
            if (routing == null) return;
            routing.ApplySnapshot(sc.chestPort,
                id => _itemById.TryGetValue(id, out var def) ? def : null);
        }

        private ItemStack DeserializeStack(SavedStack e)
        {
            if (e == null || string.IsNullOrEmpty(e.itemId) || e.count <= 0) return new ItemStack();

            if (e.isPackedDrawer)
            {
                string baseId = !string.IsNullOrEmpty(e.packedOriginalItemId) ? e.packedOriginalItemId : e.itemId;
                if (!_itemById.TryGetValue(baseId, out var baseDef)) return new ItemStack();
                var baseBlock = baseDef as BlockItem;
                if (baseBlock == null) return new ItemStack();
                var payload = new VoxelEngine.Storage.StorageDrawer.DrawerItemPayload
                {
                    instanceId = string.IsNullOrEmpty(e.drawerInstanceId) ? System.Guid.NewGuid().ToString("N") : e.drawerInstanceId,
                    originalItem = baseBlock,
                    storedItem = !string.IsNullOrEmpty(e.drawerStoredItemId) && _itemById.TryGetValue(e.drawerStoredItemId, out var stored) ? stored : null,
                    storedCount = e.drawerStoredCount,
                    upgrades = new List<ItemStack>()
                };
                if (e.drawerUpgrades != null)
                    foreach (var up in e.drawerUpgrades)
                        payload.upgrades.Add(DeserializeStack(up));
                return VoxelEngine.Storage.StorageDrawer.CreatePackedDrawerStack(baseBlock, payload);
            }

            if (!_itemById.TryGetValue(e.itemId, out var item)) return new ItemStack();
            return new ItemStack { item = item, count = e.count, durability = e.durability };
        }

        private void DeserializeInto(ItemContainer c, SavedContainer sc)
        {
            if (c == null || sc == null) return;
            c.EnsureValid();
            int min = Mathf.Min(sc.entries.Count, c.Slots.Count);
            for (int i = 0; i < min; i++)
            {
                var e = sc.entries[i];
                c.SetSlot(i, DeserializeStack(e));
            }
        }

        private void DeserializeMulti(SavedContainer sc, params ItemContainer[] containers)
        {
            if (sc == null) return;
            int idx = 0;
            for (int ci = 0; ci < containers.Length; ci++)
            {
                var c = containers[ci];
                if (c == null) continue;
                c.EnsureValid();
                int wantSize = ci < sc.containerSizes.Count ? sc.containerSizes[ci] : c.Slots.Count;
                int take = Mathf.Min(wantSize, c.Slots.Count);
                for (int i = 0; i < take && idx < sc.entries.Count; i++, idx++)
                {
                    var e = sc.entries[idx];
                    c.SetSlot(i, DeserializeStack(e));
                }
            }
        }

        // ============================================================
        //                       SAVE SCHEMA
        // ============================================================
        [Serializable] private class SaveData
        {
            public SavedPlayer player;
            public List<SavedPlacedBlock>  placedBlocks  = new();
            public List<SavedPlacedTiered> placedTiered = new();
            public List<SavedQuarry>       quarries     = new();
        }
        [Serializable] private class SavedPlayer
        {
            public Vector3 pos; public float rotY;
            public SavedContainer container;
            public int activeHotbarIndex;
        }
        [Serializable] private class SavedPlacedBlock
        {
            public string itemId;
            public Vector3 pos; public float rotY;
            public int hp;
            public SavedContainer container;
            // Wind turbine part condition (0..100). 0 = "not set" (legacy saves)
            // and restores as factory-new. Only written for WindTurbinePart blocks.
            public float windCondition;
            // Additive/backward-compatible conveyor shape state. Legacy saves leave
            // hasExplicitConveyorShape false and rebuild normal straight/corner topology.
            public bool hasExplicitConveyorShape;
            public int conveyorShape;
        }
        [Serializable] private class SavedPlacedTiered
        {
            public string family; public int tier;
            public Vector3 pos;   public float rotY;
            public int hp;
        }
        [Serializable] private class SavedContainer
        {
            public List<SavedStack> entries = new();
            public List<int>        containerSizes = new();   // for multi-container blocks
            // Advanced port config for chests (per-face direction + item filters).
            // Null/empty for blocks that don't carry one — fully backward compatible.
            public VoxelEngine.Transport.ItemPortSnapshot chestPort;
        }
        [Serializable] private class SavedStack
        {
            public string itemId; public int count; public int durability;
            public bool isPackedDrawer;
            public string packedOriginalItemId;
            public string drawerInstanceId;
            public string drawerStoredItemId;
            public int drawerStoredCount;
            public List<SavedStack> drawerUpgrades = new();
        }
        [Serializable] private class SavedQuarry
        {
            public Vector3 pos; public float rotY;
            public int currentDepth; public int cursorX; public int cursorZ;
            public int phase; public int rangeLvl; public int speedLvl; public int effLvl; // upgrade levels
            public SavedContainer outputContainer;
        }
    }
}
