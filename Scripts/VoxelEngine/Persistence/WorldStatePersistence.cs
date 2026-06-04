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
        private const float AUTOSAVE_SECONDS = 30f;   // background autosave cadence

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
            _saveTimer += Time.deltaTime;
            if (_saveTimer >= AUTOSAVE_SECONDS) { _saveTimer = 0f; SaveAll(); }
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
#if UNITY_EDITOR
            // In-editor: scan every ItemDefinition / BlockItem / TieredBlockDefinition asset.
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item != null && !string.IsNullOrEmpty(item.itemId)) _itemById[item.itemId] = item;
                if (item is BlockItem bi && !string.IsNullOrEmpty(bi.itemId)) _blockById[bi.itemId] = bi;
            }
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:TieredBlockDefinition"))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var def = UnityEditor.AssetDatabase.LoadAssetAtPath<TieredBlockDefinition>(path);
                if (def != null) _tieredById[def.family.ToString()] = def;
            }
#else
            // Builds: rely on a Resources/Items + Resources/Tiered folder if the user copies them.
            foreach (var it in Resources.LoadAll<ItemDefinition>(""))
            {
                if (!string.IsNullOrEmpty(it.itemId)) _itemById[it.itemId] = it;
                if (it is BlockItem bi && !string.IsNullOrEmpty(bi.itemId)) _blockById[bi.itemId] = bi;
            }
            foreach (var def in Resources.LoadAll<TieredBlockDefinition>(""))
                _tieredById[def.family.ToString()] = def;
#endif
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
            string folder = Path.Combine(Application.persistentDataPath, "VoxelWorlds", Menu.WorldSession.Instance.worldName);
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
            var chest = go.GetComponentInChildren<Chest>();
            if (chest != null) return SerializeContainer(chest.container);

            var furnace = go.GetComponentInChildren<Furnace>();
            if (furnace != null) return SerializeMulti(furnace.inputC, furnace.fuelC, furnace.outputC);

            var efurn = go.GetComponentInChildren<ElectricFurnace>();
            if (efurn != null) return SerializeMulti(efurn.inputC, efurn.outputC, efurn.upgradeC);

            return null;
        }

        private SavedContainer SerializeContainer(ItemContainer c)
        {
            if (c == null) return null;
            c.EnsureValid();
            var sc = new SavedContainer();
            for (int i = 0; i < c.Slots.Count; i++)
            {
                var s = c.GetSlot(i);
                sc.entries.Add(new SavedStack
                {
                    itemId = s.IsEmpty ? "" : s.item.itemId,
                    count  = s.IsEmpty ? 0  : s.count,
                    durability = s.IsEmpty ? 0 : s.durability
                });
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
                    sc.entries.Add(new SavedStack
                    {
                        itemId = s.IsEmpty ? "" : s.item.itemId,
                        count  = s.IsEmpty ? 0  : s.count,
                        durability = s.IsEmpty ? 0 : s.durability
                    });
                }
            }
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
            var chest = go.GetComponentInChildren<Chest>();
            if (chest != null) { DeserializeInto(chest.container, sc); return; }

            var furnace = go.GetComponentInChildren<Furnace>();
            if (furnace != null)
            {
                furnace.EnsureContainers();
                DeserializeMulti(sc, furnace.inputC, furnace.fuelC, furnace.outputC);
                return;
            }
            var efurn = go.GetComponentInChildren<ElectricFurnace>();
            if (efurn != null)
            {
                efurn.EnsureContainers();
                DeserializeMulti(sc, efurn.inputC, efurn.outputC, efurn.upgradeC);
                return;
            }
        }

        private void DeserializeInto(ItemContainer c, SavedContainer sc)
        {
            if (c == null || sc == null) return;
            c.EnsureValid();
            int min = Mathf.Min(sc.entries.Count, c.Slots.Count);
            for (int i = 0; i < min; i++)
            {
                var e = sc.entries[i];
                if (string.IsNullOrEmpty(e.itemId) || e.count <= 0) { c.SetSlot(i, new ItemStack()); continue; }
                if (!_itemById.TryGetValue(e.itemId, out var item)) { c.SetSlot(i, new ItemStack()); continue; }
                c.SetSlot(i, new ItemStack { item = item, count = e.count, durability = e.durability });
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
                    if (string.IsNullOrEmpty(e.itemId) || e.count <= 0) { c.SetSlot(i, new ItemStack()); continue; }
                    if (!_itemById.TryGetValue(e.itemId, out var item)) { c.SetSlot(i, new ItemStack()); continue; }
                    c.SetSlot(i, new ItemStack { item = item, count = e.count, durability = e.durability });
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
        }
        [Serializable] private class SavedStack
        {
            public string itemId; public int count; public int durability;
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
