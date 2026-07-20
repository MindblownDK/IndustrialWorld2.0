// Assets/Scripts/VoxelEngine/Persistence/WorldStatePersistence.cs
//
// Saves dynamic world state (player, placed blocks, containers, transport packets,
// and active factory processing) to a JSON sidecar next to the world's region files.
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
using VoxelEngine.GridSystem;

namespace VoxelEngine.Persistence
{
    public class WorldStatePersistence : MonoBehaviour
    {
        public static WorldStatePersistence Instance { get; private set; }

        // Item lookup cache for restore.
        private Dictionary<string, ItemDefinition>            _itemById   = new();
        private Dictionary<string, BlockItem>                 _blockById  = new();
        private Dictionary<string, TieredBlockDefinition>     _tieredById = new();
        private Dictionary<string, GridBlockItem>             _gridBlockById = new();

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
            // Do not write during scene teardown: Unity may already have destroyed
            // the player Inventory, which previously replaced a valid save with a
            // player-less sidecar whose first block position was read as the spawn.
            if (Instance == this) Instance = null;
        }

        // ============================================================
        //                       ITEM CACHE
        // ============================================================
        private void BuildItemCache()
        {
            _itemById.Clear(); _blockById.Clear(); _tieredById.Clear(); _gridBlockById.Clear();
            // Runtime-safe asset cache: rely on Resources-visible assets only.
            // This avoids hard dependencies on editor-only assemblies from the runtime asmdef.
            void CacheItem(ItemDefinition item)
            {
                if (item == null || string.IsNullOrEmpty(item.itemId)) return;
                _itemById[item.itemId] = item;
                if (item is BlockItem block) _blockById[item.itemId] = block;
                if (item is GridBlockItem gridBlock) _gridBlockById[item.itemId] = gridBlock;
            }

            foreach (var item in Resources.LoadAll<ItemDefinition>("")) CacheItem(item);
            // Setup-generated content is frequently referenced by scene registries
            // without living under a Resources folder. Include every loaded asset so
            // editor and player builds resolve the same stable item IDs.
            foreach (var item in Resources.FindObjectsOfTypeAll<ItemDefinition>()) CacheItem(item);

            foreach (var def in Resources.LoadAll<TieredBlockDefinition>(""))
                _tieredById[def.family.ToString()] = def;
            foreach (var def in Resources.FindObjectsOfTypeAll<TieredBlockDefinition>())
                if (def != null) _tieredById[def.family.ToString()] = def;
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
                if (!SavePlayer(save))
                {
                    Debug.LogWarning("[WorldState] Skipped save because the player inventory is unavailable; existing save was preserved.");
                    return;
                }
                SavePlacedBlocks(save);
                SavePlacedTiered(save);
                SaveGrids(save);
                SaveQuarries(save);
                string json = JsonUtility.ToJson(save, prettyPrint: true);
                string temporaryPath = path + ".tmp";
                string backupPath = path + ".previous";
                File.WriteAllText(temporaryPath, json);
                if (File.Exists(path))
                {
                    // Windows atomic replacement preserves the last known-good sidecar.
                    File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
                Debug.Log($"[WorldState] Saved -> {path} (previous snapshot: {backupPath})");
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

        private bool SavePlayer(SaveData save)
        {
            var inv = FindPlayerInventory();
            if (inv == null || !IsSafePlayerSavePosition(inv.transform.position)) return false;
            save.player = new SavedPlayer
            {
                pos = inv.transform.position,
                rotY = inv.transform.eulerAngles.y,
                container = SerializeContainer(inv.container),
                activeHotbarIndex = inv.activeHotbarIndex
            };
            return true;
        }

        private static Inventory FindPlayerInventory()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            return player != null ? player.GetComponentInChildren<Inventory>() : FindAnyObjectByType<Inventory>();
        }

        private static bool IsSafePlayerSavePosition(Vector3 pos)
        {
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)
                || float.IsInfinity(pos.x) || float.IsInfinity(pos.y) || float.IsInfinity(pos.z)) return false;
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (body == null) return Mathf.Abs(pos.x) < 100000f && Mathf.Abs(pos.y) < 100000f && Mathf.Abs(pos.z) < 100000f;
            // Space and high-atmosphere locations are valid disconnect positions.
            // Reject only locations buried deep inside the active planetary body.
            return Vector3.Distance(pos, body.transform.position) >= body.SurfaceRadius * 0.70f;
        }

        private void SavePlacedBlocks(SaveData save)
        {
            var placed = FindObjectsByType<PlacedBlock>(FindObjectsInactive.Exclude);
            foreach (var pb in placed)
            {
                if (pb == null || pb.Item == null) continue;
                // Grid-attached legacy blocks (such as unified pipes) belong to the
                // movable-grid payload and must never also restore as static world blocks.
                if (pb.GetComponent<GridBlock>()?.Grid != null) continue;
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
                CaptureFactoryRuntime(pb.gameObject, entry);
                save.placedBlocks.Add(entry);
            }
        }

        private static void CaptureFactoryRuntime(GameObject go, SavedPlacedBlock entry)
        {
            var belt = go.GetComponentInChildren<VoxelEngine.Simulation.ConveyorBelt>(true);
            if (belt != null)
            {
                foreach (var item in belt.Items)
                {
                    if (item.item == null || item.count <= 0) continue;
                    entry.conveyorItems.Add(new SavedTransportItem
                    {
                        itemId = item.item.itemId,
                        count = item.count,
                        progress = Mathf.Clamp01(item.progress),
                        lateralOffset = item.lateralOffset
                    });
                }
            }

            var chute = go.GetComponentInChildren<VoxelEngine.Simulation.ConveyorChute>(true);
            if (chute != null)
            {
                foreach (var item in chute.Items)
                {
                    if (item.item == null || item.count <= 0) continue;
                    entry.chuteItems.Add(new SavedTransportItem
                    {
                        itemId = item.item.itemId,
                        count = item.count,
                        progress = Mathf.Clamp01(item.slideProgress)
                    });
                }
            }

            var crusher = go.GetComponentInChildren<VoxelEngine.Simulation.Crusher>(true);
            if (crusher != null)
            {
                entry.machine = new SavedMachineState
                {
                    recipeId = crusher.CurrentRecipeId,
                    progressSeconds = crusher.ProcessProgressSeconds,
                    userEnabled = crusher.UserEnabled
                };
                return;
            }

            var assembler = go.GetComponentInChildren<VoxelEngine.Simulation.Assembler>(true);
            if (assembler != null)
            {
                entry.machine = new SavedMachineState
                {
                    recipeId = assembler.CurrentRecipeId,
                    progressSeconds = assembler.ProcessProgressSeconds,
                    userEnabled = assembler.UserEnabled
                };
            }

            var funnel = go.GetComponentInChildren<VoxelEngine.Simulation.Funnel>(true);
            if (funnel != null)
            {
                var fs = new SavedFunnelState
                {
                    mode = funnel.Mode.ToString()
                };
                var buf = funnel.Buffer;
                if (buf != null)
                {
                    for (int i = 0; i < buf.Size; i++)
                    {
                        var slot = buf.GetSlot(i);
                        if (slot == null || slot.IsEmpty || slot.item == null) continue;
                        fs.bufferItems.Add(new SavedTransportItem
                        {
                            itemId = slot.item.itemId,
                            count = slot.count,
                            progress = 0f
                        });
                    }
                }
                entry.funnelState = fs;
            }

            var splitter = go.GetComponentInChildren<VoxelEngine.Simulation.ConveyorSplitter>(true);
            if (splitter != null)
            {
                var ss = new SavedSplitterState
                {
                    roundRobinIndex = splitter.RoundRobinIndex
                };
                var bufferItems = splitter.BufferItems;
                if (bufferItems != null)
                {
                    for (int i = 0; i < bufferItems.Count; i++)
                    {
                        var packet = bufferItems[i];
                        if (packet.item == null || packet.count <= 0) continue;
                        ss.bufferItems.Add(new SavedTransportItem
                        {
                            itemId = packet.item.itemId,
                            count = packet.count,
                            progress = Mathf.Clamp01(packet.progress),
                            lateralOffset = packet.lateralOffset
                        });
                    }
                }
                entry.splitterState = ss;
            }

            // Capture screen block config
            var screenBlock = go.GetComponentInChildren<VoxelEngine.GridSystem.GridScreenBlock>(true);
            if (screenBlock != null)
            {
                var scfg = new SavedScreenConfig();
                scfg.dataMode = screenBlock.dataMode.ToString();
                scfg.customText = screenBlock.customText ?? "";
                scfg.textColorR = screenBlock.textColor.r;
                scfg.textColorG = screenBlock.textColor.g;
                scfg.textColorB = screenBlock.textColor.b;
                scfg.borderStyle = screenBlock.borderStyle;
                scfg.fontStyle = screenBlock.fontStyle;
                var xs = new System.Collections.Generic.List<string>();
                var ys = new System.Collections.Generic.List<string>();
                var zs = new System.Collections.Generic.List<string>();
                var ids = new System.Collections.Generic.List<string>();
                for (int si = 0; si < screenBlock.dataSourcePositions.Count; si++)
                {
                    xs.Add(screenBlock.dataSourcePositions[si].x.ToString());
                    ys.Add(screenBlock.dataSourcePositions[si].y.ToString());
                    zs.Add(screenBlock.dataSourcePositions[si].z.ToString());
                    if (si < screenBlock.dataSourceInstanceIds.Count)
                        ids.Add(screenBlock.dataSourceInstanceIds[si].ToString());
                }
                scfg.sourcePositionsX = string.Join(",", xs);
                scfg.sourcePositionsY = string.Join(",", ys);
                scfg.sourcePositionsZ = string.Join(",", zs);
                scfg.sourceInstanceIds = string.Join(",", ids);
                entry.screenConfig = scfg;
            }

            CaptureLightingRuntime(go, entry);
        }

        private static void CaptureLightingRuntime(GameObject go, SavedPlacedBlock entry)
        {
            var gridLight = go.GetComponentInChildren<VoxelEngine.Simulation.GridLightBlock>(true);
            var ledStrip = go.GetComponentInChildren<VoxelEngine.Simulation.LEDStrip>(true);
            if (gridLight == null && ledStrip == null) return;

            var cfg = new SavedLightingConfig();
            if (gridLight != null)
            {
                cfg.hasGridLight = true;
                cfg.lightColorR = gridLight.lightColor.r;
                cfg.lightColorG = gridLight.lightColor.g;
                cfg.lightColorB = gridLight.lightColor.b;
                cfg.lightRange = gridLight.range;
                cfg.lightSpotAngle = gridLight.spotAngle;
                cfg.lightIntensity = gridLight.intensity;
                cfg.lightType = gridLight.lightType.ToString();
                cfg.lightWattsDraw = gridLight.wattsDraw;
                cfg.lightMotionActivated = gridLight.motionActivated;
                cfg.lightMotionRadius = gridLight.motionRadius;
                cfg.lightMotionGraceSeconds = gridLight.motionGraceSeconds;
            }

            if (ledStrip != null)
            {
                cfg.hasLedStrip = true;
                cfg.ledColorR = ledStrip.stripColor.r;
                cfg.ledColorG = ledStrip.stripColor.g;
                cfg.ledColorB = ledStrip.stripColor.b;
                cfg.ledBrightness = ledStrip.brightness;
                cfg.ledLength = ledStrip.stripLength;
                cfg.ledSegmentCount = ledStrip.segmentCount;
                cfg.ledStripWidth = ledStrip.stripWidth;
                cfg.ledOffsetX = ledStrip.stripOffset.x;
                cfg.ledOffsetY = ledStrip.stripOffset.y;
                cfg.ledOffsetZ = ledStrip.stripOffset.z;
                cfg.ledShowSegments = ledStrip.showSegments;
                cfg.ledMode = ledStrip.mode.ToString();
                cfg.ledAnimSpeed = ledStrip.animSpeed;
                cfg.ledMotionActivated = ledStrip.motionActivated;
                cfg.ledMotionChaseOnActivation = ledStrip.motionChaseOnActivation;
                cfg.ledMotionRadius = ledStrip.motionRadius;
                cfg.ledMotionGraceSeconds = ledStrip.motionGraceSeconds;
                cfg.ledWattsDraw = ledStrip.wattsDraw;
            }

            entry.lightingConfig = cfg;
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

            var crusher = go.GetComponentInChildren<VoxelEngine.Simulation.Crusher>();
            if (crusher != null)
                return SerializeMulti(crusher.inputC, crusher.outputC, crusher.upgradeC);

            var assembler = go.GetComponentInChildren<VoxelEngine.Simulation.Assembler>();
            if (assembler != null)
                return SerializeMulti(assembler.inputC, assembler.outputC, assembler.upgradeC);

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

        private const int MaxPackedDrawerSaveDepth = 4;

        private SavedStack SerializeStack(ItemStack s, int depth = 0)
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
                if (payload.upgrades != null && depth < MaxPackedDrawerSaveDepth)
                    foreach (var up in payload.upgrades)
                        saved.drawerUpgrades.Add(SerializeStack(up, depth + 1));
                else if (payload.upgrades != null && payload.upgrades.Count > 0)
                    Debug.LogWarning("[WorldState] Packed drawer upgrade nesting exceeded the safe save limit; deeper upgrades were skipped.");
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
                RestoreGrids(save);
                RestorePlayer(save);
                RestoreQuarries(save);
                Debug.Log($"[WorldState] Loaded {save.placedTiered.Count} tiered + {save.placedBlocks.Count} blocks + {save.grids.Count} movable grids from {path}");
            }
            catch (Exception ex) { Debug.LogError("[WorldState] Load failed: " + ex.Message); }
            _loaded = true;
        }

        // ============================================================
        //                    MOVABLE GRID PERSISTENCE
        // ============================================================
        // Additive schema: legacy world_state.json files do not contain `grids` and
        // therefore continue through the normal static-world restore path unchanged.
        private void SaveGrids(SaveData save)
        {
            foreach (var grid in FindObjectsByType<GridEntity>(FindObjectsInactive.Exclude))
            {
                if (grid == null || grid.BlockCount == 0) continue;

                var entry = new SavedGrid
                {
                    pos = grid.transform.position,
                    rot = grid.transform.rotation,
                    gridSize = (int)grid.gridSize,
                    gravityScale = grid.gravityScale,
                    dampenersOn = grid.DampenersOn,
                    hydrogenStored = grid.HydrogenStored,
                    oxygenStored = grid.OxygenStored
                };
                if (grid.Body != null)
                {
                    entry.velocity = grid.Body.linearVelocity;
                    entry.angularVelocity = grid.Body.angularVelocity;
                }

                foreach (var block in grid.AllBlocks)
                {
                    if (block == null) continue;
                    var sourceItem = ResolveGridSourceItem(block);
                    if (sourceItem == null || string.IsNullOrEmpty(sourceItem.itemId))
                    {
                        Debug.LogWarning($"[WorldState] Skipped grid block '{block.name}' because its source item could not be identified safely.");
                        continue;
                    }

                    var savedBlock = new SavedGridBlock
                    {
                        itemId = sourceItem.itemId,
                        localRotation = block.transform.localRotation,
                        currentHP = block.currentHP,
                        enabled = block.Enabled,
                        isPrecision = block.IsPrecisionAttachment,
                        gridPos = block.GridPos,
                        precisionPos = block.PrecisionGridPos,
                        precisionHostPos = block.PrecisionHostGridPos,
                        container = TryFindContainer(block.gameObject)
                    };

                    var shape = block.GetComponent<GridShapeVariantBlock>();
                    if (shape != null)
                    {
                        savedBlock.hasShapeVariant = true;
                        savedBlock.shapeVariant = (int)shape.Variant;
                    }

                    // Existing machine, screen, and lighting state is deliberately
                    // stored through the same tested payload used by static blocks.
                    savedBlock.runtime = new SavedPlacedBlock();
                    CaptureFactoryRuntime(block.gameObject, savedBlock.runtime);
                    entry.blocks.Add(savedBlock);
                }
                save.grids.Add(entry);
            }
        }

        /// <summary>
        /// New placements retain their source directly. This conservative fallback also
        /// migrates existing in-memory grids created before 5.69.0 when their authored
        /// GridBlockItem display name has one unambiguous match.
        /// </summary>
        private ItemDefinition ResolveGridSourceItem(GridBlock block)
        {
            if (block == null) return null;
            if (block.SourceItem != null) return block.SourceItem;

            GridBlockItem match = null;
            foreach (var candidate in _gridBlockById.Values)
            {
                if (candidate == null || candidate.displayName != block.blockName) continue;
                if (match != null)
                {
                    // Never guess between two identically named authored items.
                    return null;
                }
                match = candidate;
            }
            if (match != null) block.SourceItem = match;
            return match;
        }

        private void RestoreGrids(SaveData save)
        {
            if (save.grids == null || save.grids.Count == 0) return;

            foreach (var savedGrid in save.grids)
            {
                if (savedGrid == null || savedGrid.blocks == null || savedGrid.blocks.Count == 0) continue;
                if (!System.Enum.IsDefined(typeof(GridSize), savedGrid.gridSize))
                {
                    Debug.LogWarning("[WorldState] Skipped a movable grid with an unknown grid size.");
                    continue;
                }

                var grid = GridEntity.Create(savedGrid.pos, (GridSize)savedGrid.gridSize);
                grid.name = "Grid (restored)";
                grid.transform.rotation = savedGrid.rot;
                grid.gravityScale = savedGrid.gravityScale > 0f ? savedGrid.gravityScale : grid.gravityScale;
                grid.DampenersOn = savedGrid.dampenersOn;
                grid.HydrogenStored = Mathf.Max(0f, savedGrid.hydrogenStored);
                grid.OxygenStored = Mathf.Max(0f, savedGrid.oxygenStored);

                // Structural blocks must be present before Detail blocks can restore
                // their host-cell relationship and attached pipe topology.
                RestoreGridBlocks(grid, savedGrid.blocks, false);
                RestoreGridBlocks(grid, savedGrid.blocks, true);

                if (grid.Body != null)
                {
                    grid.Body.linearVelocity = savedGrid.velocity;
                    grid.Body.angularVelocity = savedGrid.angularVelocity;
                }
                grid.RecalculateMass();
            }
        }

        private void RestoreGridBlocks(GridEntity grid, List<SavedGridBlock> blocks, bool precisionPass)
        {
            foreach (var saved in blocks)
            {
                if (saved == null || saved.isPrecision != precisionPass) continue;
                if (!_itemById.TryGetValue(saved.itemId, out var sourceItem) || sourceItem == null)
                {
                    Debug.LogWarning($"[WorldState] Skipped grid block '{saved.itemId}' because its source item is unavailable.");
                    continue;
                }

                GameObject prefab = sourceItem is GridBlockItem gridItem ? gridItem.blockPrefab
                    : sourceItem is BlockItem placedItem ? placedItem.placedPrefab : null;
                if (prefab == null)
                {
                    Debug.LogWarning($"[WorldState] Skipped grid block '{saved.itemId}' because its prefab is unavailable.");
                    continue;
                }

                var go = Instantiate(prefab);
                var block = go.GetComponent<GridBlock>() ?? go.AddComponent<GridBlock>();
                block.SourceItem = sourceItem;
                block.blockName = sourceItem.displayName;
                if (sourceItem is GridBlockItem authoredGridItem)
                {
                    block.BlockMass = authoredGridItem.blockMass;
                    block.maxHP = authoredGridItem.blockHP;
                }
                else if (sourceItem is BlockItem authoredPlacedItem)
                {
                    block.maxHP = authoredPlacedItem.blockHealth;
                }
                block.currentHP = saved.currentHP > 0f ? saved.currentHP : block.maxHP;
                block.Enabled = saved.enabled;

                if (sourceItem is BlockItem attachedItem)
                {
                    var placed = go.GetComponent<PlacedBlock>() ?? go.AddComponent<PlacedBlock>();
                    placed.Item = attachedItem;
                    placed.Hp = Mathf.RoundToInt(block.currentHP);
                    placed.onGrid = true;
                }

                if (saved.hasShapeVariant && System.Enum.IsDefined(typeof(VoxelEngine.UI.GridShapeVariant), saved.shapeVariant))
                {
                    var shape = block.GetComponent<GridShapeVariantBlock>() ?? block.gameObject.AddComponent<GridShapeVariantBlock>();
                    shape.Configure((VoxelEngine.UI.GridShapeVariant)saved.shapeVariant,
                        precisionPass ? GridSize.Small : grid.gridSize);
                }

                if (precisionPass)
                {
                    var layer = grid.GetComponent<GridPrecisionAttachmentLayer>() ?? grid.gameObject.AddComponent<GridPrecisionAttachmentLayer>();
                    if (!layer.AddBlock(saved.precisionPos, saved.precisionHostPos, block, saved.localRotation))
                    {
                        Destroy(go);
                        continue;
                    }
                }
                else
                {
                    if (grid.GetBlock(saved.gridPos) != null)
                    {
                        Destroy(go);
                        continue;
                    }
                    block.transform.rotation = grid.transform.rotation * saved.localRotation;
                    grid.AddBlock(saved.gridPos, block);
                }

                // OnPlaced initializes defaults, so reapply persisted state afterwards.
                block.currentHP = saved.currentHP > 0f ? saved.currentHP : block.maxHP;
                block.Enabled = saved.enabled;
                if (saved.container != null) RestoreContainer(go, saved.container);
                if (saved.runtime != null) RestoreFactoryRuntime(go, saved.runtime);
            }
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
                RestoreFactoryRuntime(go, sb);
            }
        }

        private void RestoreFactoryRuntime(GameObject go, SavedPlacedBlock saved)
        {
            var belt = go.GetComponentInChildren<VoxelEngine.Simulation.ConveyorBelt>(true);
            if (belt != null && saved.conveyorItems != null)
            {
                var restored = new List<VoxelEngine.Simulation.ConveyorItem>();
                foreach (var item in saved.conveyorItems)
                {
                    if (item == null || string.IsNullOrEmpty(item.itemId) || item.count <= 0) continue;
                    if (!_itemById.TryGetValue(item.itemId, out var definition)) continue;
                    restored.Add(new VoxelEngine.Simulation.ConveyorItem
                    {
                        item = definition,
                        count = item.count,
                        progress = Mathf.Clamp01(item.progress),
                        lateralOffset = item.lateralOffset
                    });
                }
                belt.RestoreItems(restored);
            }

            var chute = go.GetComponentInChildren<VoxelEngine.Simulation.ConveyorChute>(true);
            if (chute != null && saved.chuteItems != null)
            {
                var restored = new List<VoxelEngine.Simulation.ChuteItem>();
                foreach (var item in saved.chuteItems)
                {
                    if (item == null || string.IsNullOrEmpty(item.itemId) || item.count <= 0) continue;
                    if (!_itemById.TryGetValue(item.itemId, out var definition)) continue;
                    restored.Add(new VoxelEngine.Simulation.ChuteItem
                    {
                        item = definition,
                        count = item.count,
                        slideProgress = Mathf.Clamp01(item.progress)
                    });
                }
                chute.RestoreItems(restored);
            }

            if (saved.machine != null)
            {
                var crusher = go.GetComponentInChildren<VoxelEngine.Simulation.Crusher>(true);
                if (crusher != null)
                {
                    crusher.RestorePersistentState(
                        saved.machine.recipeId,
                        saved.machine.progressSeconds,
                        saved.machine.userEnabled);
                }
                else
                {
                    var assembler = go.GetComponentInChildren<VoxelEngine.Simulation.Assembler>(true);
                    if (assembler != null)
                    {
                        assembler.RestorePersistentState(
                            saved.machine.recipeId,
                            saved.machine.progressSeconds,
                            saved.machine.userEnabled);
                    }
                }
            }

            var funnel = go.GetComponentInChildren<VoxelEngine.Simulation.Funnel>(true);
            if (funnel != null && saved.funnelState != null)
            {
                // Restore mode
                if (saved.funnelState.mode == "Export")
                    funnel.SetMode(VoxelEngine.Simulation.FunnelMode.Export);
                else
                    funnel.SetMode(VoxelEngine.Simulation.FunnelMode.Import);

                // Restore buffered items
                if (saved.funnelState.bufferItems != null && saved.funnelState.bufferItems.Count > 0)
                {
                    var buf = funnel.Buffer;
                    if (buf != null)
                    {
                        for (int si = 0; si < buf.Size; si++)
                            buf.SetSlot(si, new ItemStack());
                        foreach (var item in saved.funnelState.bufferItems)
                        {
                            if (item == null || string.IsNullOrEmpty(item.itemId) || item.count <= 0) continue;
                            if (!_itemById.TryGetValue(item.itemId, out var definition)) continue;
                            buf.Insert(new ItemStack(definition, item.count));
                        }
                    }
                }
            }

            var splitter = go.GetComponentInChildren<VoxelEngine.Simulation.ConveyorSplitter>(true);
            if (splitter != null && saved.splitterState != null)
            {
                var restored = new List<VoxelEngine.Simulation.ConveyorItem>();
                if (saved.splitterState.bufferItems != null)
                {
                    foreach (var item in saved.splitterState.bufferItems)
                    {
                        if (item == null || string.IsNullOrEmpty(item.itemId) || item.count <= 0) continue;
                        if (!_itemById.TryGetValue(item.itemId, out var definition)) continue;
                        restored.Add(new VoxelEngine.Simulation.ConveyorItem
                        {
                            item = definition,
                            count = item.count,
                            progress = Mathf.Clamp01(item.progress),
                            lateralOffset = item.lateralOffset
                        });
                    }
                }
                splitter.RestorePersistentState(restored, saved.splitterState.roundRobinIndex);
            }
            // Restore screen block config
            var screenBlock = go.GetComponentInChildren<VoxelEngine.GridSystem.GridScreenBlock>(true);
            if (screenBlock != null && saved.screenConfig != null)
            {
                var sc = saved.screenConfig;
                if (!string.IsNullOrEmpty(sc.dataMode))
                {
                    try { screenBlock.dataMode = (VoxelEngine.GridSystem.ScreenDataMode)System.Enum.Parse(typeof(VoxelEngine.GridSystem.ScreenDataMode), sc.dataMode); } catch { }
                }
                screenBlock.customText = sc.customText ?? "";
                screenBlock.textColor = new Color(sc.textColorR, sc.textColorG, sc.textColorB);
                screenBlock.borderStyle = sc.borderStyle;
                screenBlock.fontStyle = sc.fontStyle;
                if (!string.IsNullOrEmpty(sc.sourcePositionsX))
                {
                    var xs = sc.sourcePositionsX.Split(',');
                    var ys = sc.sourcePositionsY.Split(',');
                    var zs = sc.sourcePositionsZ.Split(',');
                    var ids = sc.sourceInstanceIds.Split(',');
                    screenBlock.dataSourcePositions.Clear();
                    screenBlock.dataSourceInstanceIds.Clear();
                    for (int si = 0; si < xs.Length && si < ys.Length && si < zs.Length; si++)
                    {
                        if (int.TryParse(xs[si], out int px) && int.TryParse(ys[si], out int py) && int.TryParse(zs[si], out int pz))
                            screenBlock.dataSourcePositions.Add(new Vector3Int(px, py, pz));
                        if (si < ids.Length && int.TryParse(ids[si], out int id))
                            screenBlock.dataSourceInstanceIds.Add(id);
                    }
                }
            }

            RestoreLightingRuntime(go, saved.lightingConfig);
        }

        private static void RestoreLightingRuntime(GameObject go, SavedLightingConfig cfg)
        {
            if (go == null || cfg == null) return;

            if (cfg.hasGridLight)
            {
                var gridLight = go.GetComponentInChildren<VoxelEngine.Simulation.GridLightBlock>(true);
                if (gridLight != null)
                {
                    gridLight.SetColor(new Color(cfg.lightColorR, cfg.lightColorG, cfg.lightColorB));
                    gridLight.SetRange(cfg.lightRange > 0f ? cfg.lightRange : gridLight.range);
                    gridLight.SetIntensity(cfg.lightIntensity >= 0f ? cfg.lightIntensity : gridLight.intensity);
                    gridLight.spotAngle = cfg.lightSpotAngle > 0f ? cfg.lightSpotAngle : gridLight.spotAngle;
                    if (!string.IsNullOrEmpty(cfg.lightType))
                    {
                        try { gridLight.lightType = (LightType)System.Enum.Parse(typeof(LightType), cfg.lightType); } catch { }
                    }
                    if (cfg.lightWattsDraw > 0f) gridLight.wattsDraw = cfg.lightWattsDraw;
                    gridLight.motionActivated = cfg.lightMotionActivated;
                    if (cfg.lightMotionRadius > 0f) gridLight.motionRadius = cfg.lightMotionRadius;
                    if (cfg.lightMotionGraceSeconds > 0f) gridLight.motionGraceSeconds = cfg.lightMotionGraceSeconds;
                }
            }

            if (cfg.hasLedStrip)
            {
                var ledStrip = go.GetComponentInChildren<VoxelEngine.Simulation.LEDStrip>(true);
                if (ledStrip != null)
                {
                    ledStrip.stripColor = new Color(cfg.ledColorR, cfg.ledColorG, cfg.ledColorB);
                    ledStrip.brightness = cfg.ledBrightness > 0f ? cfg.ledBrightness : ledStrip.brightness;
                    ledStrip.segmentCount = cfg.ledSegmentCount > 0 ? cfg.ledSegmentCount : ledStrip.segmentCount;
                    ledStrip.stripWidth = cfg.ledStripWidth > 0f ? cfg.ledStripWidth : ledStrip.stripWidth;
                    ledStrip.stripOffset = new Vector3(cfg.ledOffsetX, cfg.ledOffsetY, cfg.ledOffsetZ);
                    ledStrip.showSegments = cfg.ledShowSegments;
                    if (!string.IsNullOrEmpty(cfg.ledMode))
                    {
                        try { ledStrip.mode = (VoxelEngine.Simulation.LEDMode)System.Enum.Parse(typeof(VoxelEngine.Simulation.LEDMode), cfg.ledMode); } catch { }
                    }
                    if (cfg.ledAnimSpeed > 0f) ledStrip.animSpeed = cfg.ledAnimSpeed;
                    ledStrip.motionActivated = cfg.ledMotionActivated;
                    ledStrip.motionChaseOnActivation = cfg.ledMotionChaseOnActivation;
                    if (cfg.ledMotionRadius > 0f) ledStrip.motionRadius = cfg.ledMotionRadius;
                    if (cfg.ledMotionGraceSeconds > 0f) ledStrip.motionGraceSeconds = cfg.ledMotionGraceSeconds;
                    if (cfg.ledWattsDraw > 0f) ledStrip.wattsDraw = cfg.ledWattsDraw;
                    ledStrip.SetLength(cfg.ledLength > 0f ? cfg.ledLength : ledStrip.stripLength);
                    ledStrip.SetColor(ledStrip.stripColor);
                }
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

            var crusher = go.GetComponentInChildren<VoxelEngine.Simulation.Crusher>();
            if (crusher != null)
            {
                DeserializeMulti(sc, crusher.inputC, crusher.outputC, crusher.upgradeC);
                return;
            }

            var assembler = go.GetComponentInChildren<VoxelEngine.Simulation.Assembler>();
            if (assembler != null)
                DeserializeMulti(sc, assembler.inputC, assembler.outputC, assembler.upgradeC);
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

        private ItemStack DeserializeStack(SavedStack e, int depth = 0)
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
                if (e.drawerUpgrades != null && depth < MaxPackedDrawerSaveDepth)
                    foreach (var up in e.drawerUpgrades)
                        payload.upgrades.Add(DeserializeStack(up, depth + 1));
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
            // Additive in 5.69.0: omitted by legacy saves and initialized by field default.
            public List<SavedGrid>          grids        = new();
        }
        [Serializable] private class SavedGrid
        {
            public Vector3 pos;
            public Quaternion rot;
            public Vector3 velocity;
            public Vector3 angularVelocity;
            public int gridSize;
            public float gravityScale;
            public bool dampenersOn = true;
            public float hydrogenStored;
            public float oxygenStored;
            public List<SavedGridBlock> blocks = new();
        }
        [Serializable] private class SavedGridBlock
        {
            public string itemId;
            public Vector3Int gridPos;
            public bool isPrecision;
            public Vector3Int precisionPos;
            public Vector3Int precisionHostPos;
            public Quaternion localRotation;
            public float currentHP;
            public bool enabled = true;
            public bool hasShapeVariant;
            public int shapeVariant;
            public SavedContainer container;
            public SavedPlacedBlock runtime;
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
            // Additive Factory Foundations runtime state. Legacy saves leave these
            // collections empty and machines resume from their restored containers.
            public List<SavedTransportItem> conveyorItems = new();
            public List<SavedTransportItem> chuteItems = new();
            public SavedMachineState machine;
            // Funnel state (mode + buffered items). Null for non-funnel blocks.
            public SavedFunnelState funnelState;
            // Splitter state (buffer + round-robin cursor). Null for non-splitter blocks.
            public SavedSplitterState splitterState;
            // Screen block config (GridScreenBlock display mode, sources, appearance). Null = no screen data.
            public SavedScreenConfig screenConfig;
            // Lighting config for GridLightBlock / LEDStrip. Null = not a configurable light.
            public SavedLightingConfig lightingConfig;
        }
        [Serializable] private class SavedFunnelState
        {
            public string mode; // "Import" or "Export"
            public System.Collections.Generic.List<SavedTransportItem> bufferItems = new System.Collections.Generic.List<SavedTransportItem>();
        }
        [Serializable] private class SavedSplitterState
        {
            public int roundRobinIndex;
            public System.Collections.Generic.List<SavedTransportItem> bufferItems = new System.Collections.Generic.List<SavedTransportItem>();
        }
        [Serializable] private class SavedScreenConfig
        {
            public string dataMode;
            public string customText;
            public float textColorR;
            public float textColorG;
            public float textColorB;
            public int borderStyle;
            public int fontStyle;
            // Source positions stored as comma-separated strings for JSON compatibility
            public string sourcePositionsX;
            public string sourcePositionsY;
            public string sourcePositionsZ;
            public string sourceInstanceIds;
        }
        [Serializable] private class SavedLightingConfig
        {
            public bool hasGridLight;
            public float lightColorR;
            public float lightColorG;
            public float lightColorB;
            public float lightRange;
            public float lightSpotAngle;
            public float lightIntensity;
            public string lightType;
            public float lightWattsDraw;
            public bool lightMotionActivated;
            public float lightMotionRadius;
            public float lightMotionGraceSeconds;

            public bool hasLedStrip;
            public float ledColorR;
            public float ledColorG;
            public float ledColorB;
            public float ledBrightness;
            public float ledLength;
            public int ledSegmentCount;
            public float ledStripWidth;
            public float ledOffsetX;
            public float ledOffsetY;
            public float ledOffsetZ;
            public bool ledShowSegments;
            public string ledMode;
            public float ledAnimSpeed;
            public bool ledMotionActivated;
            public bool ledMotionChaseOnActivation;
            public float ledMotionRadius;
            public float ledMotionGraceSeconds;
            public float ledWattsDraw;
        }
        [Serializable] private class SavedTransportItem
        {
            public string itemId;
            public int count;
            public float progress;
            public float lateralOffset;
        }
        [Serializable] private class SavedMachineState
        {
            public string recipeId;
            public float progressSeconds;
            public bool userEnabled;
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
