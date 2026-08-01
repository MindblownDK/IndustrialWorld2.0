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
            if (_saveTimer >= interval) { _saveTimer = 0f; SaveAll(writeAutosaveSlot: true); }
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
        public void SaveAll(bool writeAutosaveSlot = false)
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
                if (writeAutosaveSlot)
                    WriteAutosaveSnapshot(path);
                Debug.Log($"[WorldState] Saved -> {path} (previous snapshot: {backupPath})");

                // 11.4 Offline survival — record logout time/pos/cryobed for O₂ consumption on next login
                try
                {
                    VoxelEngine.Player.OfflineSurvivalService.EnsureInstance();
                    var inv = FindPlayerInventory();
                    if (inv != null && VoxelEngine.Player.OfflineSurvivalService.Instance != null)
                        VoxelEngine.Player.OfflineSurvivalService.Instance.SaveOfflineState(inv.transform.position);
                }
                catch (Exception ex2) { Debug.LogWarning("[WorldState] Offline save: " + ex2.Message); }
            }
            catch (Exception ex) { Debug.LogError("[WorldState] Save failed: " + ex.Message); }
        }

        private static void WriteAutosaveSnapshot(string worldStatePath)
        {
            if (string.IsNullOrEmpty(worldStatePath) || !File.Exists(worldStatePath)) return;
            try
            {
                string folder = Path.GetDirectoryName(worldStatePath);
                if (string.IsNullOrEmpty(folder)) return;

                for (int slot = Menu.WorldSession.AutosaveSlotCount; slot >= 2; slot--)
                {
                    string previous = Path.Combine(folder, $"world_state.autosave{slot - 1}.json");
                    string current = Path.Combine(folder, $"world_state.autosave{slot}.json");
                    if (File.Exists(previous)) AtomicCopyFile(previous, current, current + ".previous");
                }

                AtomicCopyFile(worldStatePath, Path.Combine(folder, "world_state.autosave1.json"),
                    Path.Combine(folder, "world_state.autosave1.json.previous"));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WorldState] Autosave slot snapshot failed: " + ex.Message);
            }
        }

        private static void AtomicCopyFile(string source, string destination, string backupPath)
        {
            string folder = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string tmp = destination + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            File.Copy(source, tmp, overwrite: true);

            try
            {
                if (File.Exists(destination))
                {
                    if (!string.IsNullOrEmpty(backupPath) && File.Exists(backupPath)) File.Delete(backupPath);
                    File.Replace(tmp, destination, backupPath, ignoreMetadataErrors: true);
                }
                else
                    File.Move(tmp, destination);
            }
            catch
            {
                if (File.Exists(tmp))
                {
                    if (File.Exists(destination))
                    {
                        if (!string.IsNullOrEmpty(backupPath)) File.Copy(destination, backupPath, overwrite: true);
                        File.Delete(destination);
                    }
                    File.Move(tmp, destination);
                }
            }
        }

        private string WorldStatePath()
        {
            var session = Menu.WorldSession.Instance;
            string worldName = !string.IsNullOrEmpty(session != null ? session.worldName : null)
                ? session.worldName : "DefaultWorld";
            if (session != null)
            {
                string path = session.WorldStatePathFor(worldName);
                string stateFolder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(stateFolder)) Directory.CreateDirectory(stateFolder);
                return path;
            }

            string fallbackFolder = Path.Combine(Application.persistentDataPath, "VoxelWorlds",
                Menu.WorldSession.SanitizeWorldFolderName(worldName));
            Directory.CreateDirectory(fallbackFolder);
            return Path.Combine(fallbackFolder, "world_state.json");
        }

        private bool SavePlayer(SaveData save)
        {
            var inv = FindPlayerInventory();
            if (inv == null || !IsSafePlayerSavePosition(inv.transform.position)) return false;
            var equipment = inv.GetComponent<VoxelEngine.Player.PlayerEquipment>();
            save.player = new SavedPlayer
            {
                pos = inv.transform.position,
                rotY = inv.transform.eulerAngles.y,
                container = SerializeContainer(inv.container),
                jetpackSlots = equipment != null ? SerializeContainer(equipment.JetpackSlots) : null,
                helmetSlots = equipment != null ? SerializeContainer(equipment.HelmetSlots) : null,
                oxygenTankSlots = equipment != null ? SerializeContainer(equipment.OxygenTankSlots) : null,
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
                    rot = pb.transform.rotation,
                    rotY = pb.transform.eulerAngles.y,
                    hp = pb.Hp,
                    container = TryFindContainer(pb.gameObject)
                };
                var cryobed = pb.GetComponentInChildren<VoxelEngine.Building.Cryobed>(true);
                if (cryobed != null)
                {
                    entry.customName = cryobed.displayName;
                    entry.cryobedClaimed = cryobed.claimedByLocalPlayer;
                }

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
                    roundRobinIndex = splitter.RoundRobinIndex,
                    routingMode = splitter.RoutingMode.ToString()
                };
                for (int i = 0; i < splitter.OutputCount; i++)
                    ss.outputFilterItemIds.Add(splitter.GetOutputFilterItem(i) != null ? splitter.GetOutputFilterItem(i).itemId : string.Empty);
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

            CaptureMaritimePorts(go, entry);
            CaptureLightingRuntime(go, entry);
            CaptureDefenseRuntime(go, entry);
        }

        private static void CaptureDefenseRuntime(GameObject go, SavedPlacedBlock entry)
        {
            var art = go.GetComponentInChildren<VoxelEngine.Combat.Artillery>(true);
            if (art != null)
            {
                entry.defenseState = new SavedDefenseState
                {
                    filter = (int)art.filter,
                    autoMode = art.autoMode,
                    ammo = 0,
                    fuelSeconds = 0f
                };
                return;
            }

            var flame = go.GetComponentInChildren<VoxelEngine.Combat.FlamethrowerTurret>(true);
            if (flame != null)
            {
                entry.defenseState = new SavedDefenseState
                {
                    filter = (int)flame.filter,
                    autoMode = flame.autoMode,
                    ammo = 0,
                    fuelSeconds = flame.CaptureFuelSeconds()
                };
                return;
            }

            var mortar = go.GetComponentInChildren<VoxelEngine.Combat.MortarTurret>(true);
            if (mortar != null)
            {
                entry.defenseState = new SavedDefenseState
                {
                    filter = (int)mortar.filter,
                    autoMode = mortar.autoMode,
                    ammo = 0,
                    fuelSeconds = 0f
                };
                return;
            }

            var giant = go.GetComponentInChildren<VoxelEngine.Combat.GiantShellTurret>(true);
            if (giant != null)
            {
                entry.defenseState = new SavedDefenseState
                {
                    filter = (int)giant.filter,
                    autoMode = giant.autoMode,
                    ammo = 0,
                    fuelSeconds = 0f
                };
                return;
            }

            var tur = go.GetComponentInChildren<VoxelEngine.Combat.Turret>(true);
            if (tur != null)
            {
                entry.defenseState = new SavedDefenseState
                {
                    filter = (int)tur.filter,
                    autoMode = tur.autoMode,
                    ammo = tur.ammo,
                    fuelSeconds = 0f
                };
            }
        }

        /// <summary>Capture a maritime engine's player-installed variable service
        /// ports (color-coded fuel/coolant/oxygen/exhaust). Authored model ports are
        /// baked into the prefab and need no save data; only the additive dynamic
        /// ports are recorded here.</summary>
        private static void CaptureMaritimePorts(GameObject go, SavedPlacedBlock entry)
        {
            var engine = go.GetComponentInChildren<VoxelEngine.Maritime.GridMaritimeEngine>(true);
            if (engine != null)
            {
                var records = engine.CaptureVariablePorts();
                if (records == null || records.Count == 0) return;
                var saved = new SavedMaritimePorts();
                for (int i = 0; i < records.Count; i++)
                {
                    var r = records[i];
                    if (r == null) continue;
                    saved.ports.Add(new SavedVariablePort
                    {
                        service = r.service,
                        localPos = r.localPos,
                        localOutward = r.localOutward
                    });
                }
                entry.maritimePorts = saved;
                return;
            }

            // Reuse the same additive port payload for grid tank variable ports.
            // service stores GridTankPortFamily (0=Liquid, 1=Gas) for tank blocks.
            var tankPorts = go.GetComponentInChildren<VoxelEngine.GridSystem.GridTankVariablePorts>(true);
            if (tankPorts == null || !tankPorts.HasRecords) return;
            var tankRecords = tankPorts.CaptureRecords();
            if (tankRecords == null || tankRecords.Count == 0) return;
            var tankSaved = new SavedMaritimePorts();
            for (int i = 0; i < tankRecords.Count; i++)
            {
                var r = tankRecords[i];
                if (r == null) continue;
                tankSaved.ports.Add(new SavedVariablePort
                {
                    service = r.family,
                    localPos = r.localPos,
                    localOutward = r.localOutward
                });
            }
            entry.maritimePorts = tankSaved;
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
                    rot    = pb.transform.rotation,
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
                    rot = q.transform.rotation,
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

            var maritimeEngine = go.GetComponentInChildren<VoxelEngine.Maritime.GridMaritimeEngine>();
            if (maritimeEngine != null)
            {
                // Solid-fuel hopper FIRST, module sockets second — legacy saves
                // (fuel-only, no containerSizes) line up with the first container.
                var engineModules = maritimeEngine.GetModuleSlots();
                if (maritimeEngine.SolidFuelInput != null || engineModules != null)
                    return SerializeMulti(maritimeEngine.SolidFuelInput, engineModules);
                return null;
            }

            var maritimeGenerator = go.GetComponentInChildren<VoxelEngine.Maritime.GridMaritimeGenerator>();
            if (maritimeGenerator != null)
            {
                var generatorModules = maritimeGenerator.GetModuleSlots();
                if (generatorModules != null)
                    return SerializeContainer(generatorModules);
                return null;
            }

            // Defense magazines (artillery shells / flamethrower fuel). Additive —
            // legacy saves leave these null and magazines start empty.
            var artillery = go.GetComponentInChildren<VoxelEngine.Combat.Artillery>();
            if (artillery != null)
                return SerializeContainer(artillery.ShellMagazine);

            var flamethrower = go.GetComponentInChildren<VoxelEngine.Combat.FlamethrowerTurret>();
            if (flamethrower != null)
                return SerializeContainer(flamethrower.FuelMagazine);

            var mortar = go.GetComponentInChildren<VoxelEngine.Combat.MortarTurret>();
            if (mortar != null)
                return SerializeContainer(mortar.ShellMagazine);

            var giant = go.GetComponentInChildren<VoxelEngine.Combat.GiantShellTurret>();
            if (giant != null)
                return SerializeContainer(giant.ShellMagazine);

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
                // Keep the sizes array 1:1 with the containers array so a null
                // container never shifts later containers out of alignment.
                if (c == null) { sc.containerSizes.Add(0); continue; }
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
                        // Exact pose: ground-lifted machine bottoms and port-centred
                        // pipe/shaft snaps must restore identically after save/load.
                        hasLocalPose = true,
                        localPosition = block.transform.localPosition,
                        currentHP = block.currentHP,
                        enabled = block.Enabled,
                        isPrecision = block.IsPrecisionAttachment,
                        gridPos = block.GridPos,
                        precisionPos = block.PrecisionGridPos,
                        precisionHostPos = block.PrecisionHostGridPos,
                        container = TryFindContainer(block.gameObject)
                    };

                    if (block is GridCryobed cryoBlock)
                    {
                        savedBlock.customName = cryoBlock.blockName;
                        savedBlock.cryobedClaimed = cryoBlock.claimedByLocalPlayer;
                        savedBlock.cryobedOxygen = cryoBlock.oxygenStored;
                    }

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
                block.blockName = !string.IsNullOrEmpty(saved.customName) ? saved.customName : sourceItem.displayName;
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
                if (block is GridCryobed restoredGridCryo)
                {
                    restoredGridCryo.claimedByLocalPlayer = saved.cryobedClaimed;
                    restoredGridCryo.oxygenStored = Mathf.Clamp(saved.cryobedOxygen, 0f, restoredGridCryo.oxygenCapacity);
                }

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

                // Exact pose restore (ground lifts / port-centred ports snaps); old
                // saves lack the fields and keep the pure lattice pose instead.
                if (saved.hasLocalPose)
                    block.transform.localPosition = saved.localPosition;

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

            Vector3 restorePosition = ResolvePlayerRestorePosition(save.player.pos, out bool usedFallback);
            float restoreRotY = IsFinite(save.player.rotY) ? save.player.rotY : 0f;
            if (usedFallback)
            {
                Debug.LogWarning($"[WorldState] Saved player position was invalid; restored inventory at safe spawn {restorePosition} without rewriting the save file.");
            }

            // Teleport only after validating the coordinates. A corrupt NaN/Infinity
            // player pose must never touch the live transform because it can poison
            // physics, chunk streaming, and follow-up autosaves before PlayerSpawner
            // gets a chance to choose a fresh/bed spawn.
            var cc = inv.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            inv.transform.position = restorePosition;
            inv.transform.eulerAngles = new Vector3(0, restoreRotY, 0);
            if (cc != null) cc.enabled = true;
            // Inventory + equipment.
            if (save.player.container != null) DeserializeInto(inv.container, save.player.container);
            var equipment = inv.GetComponent<VoxelEngine.Player.PlayerEquipment>();
            if (equipment == null) equipment = inv.gameObject.AddComponent<VoxelEngine.Player.PlayerEquipment>();
            if (save.player.jetpackSlots != null) DeserializeInto(equipment.JetpackSlots, save.player.jetpackSlots);
            if (save.player.helmetSlots != null) DeserializeInto(equipment.HelmetSlots, save.player.helmetSlots);
            if (save.player.oxygenTankSlots != null) DeserializeInto(equipment.OxygenTankSlots, save.player.oxygenTankSlots);
            inv.SetActiveHotbar(save.player.activeHotbarIndex);
        }

        private static Vector3 ResolvePlayerRestorePosition(Vector3 savedPosition, out bool usedFallback)
        {
            if (IsSafePlayerSavePosition(savedPosition))
            {
                usedFallback = false;
                return savedPosition;
            }

            usedFallback = true;
            var session = Menu.WorldSession.Instance;
            if (session != null)
            {
                // Read-only refresh. This improves fallback quality when persistence
                // restores before PlayerSpawner has loaded the spawn sidecar.
                session.LoadSpawnSidecar();

                if (session.hasBedSpawn && IsSafePlayerSavePosition(session.bedSpawnPoint))
                    return session.bedSpawnPoint;
                if (session.worldSpawnInitialized && IsSafePlayerSavePosition(session.worldSpawnPoint))
                    return session.worldSpawnPoint;
                if (IsSafePlayerSavePosition(session.worldSpawnPoint))
                    return session.worldSpawnPoint;
            }

            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (body != null)
            {
                Vector3 up = body.transform != null ? body.transform.up : Vector3.up;
                if (session != null && IsFiniteVector(session.worldSpawnPoint))
                {
                    Vector3 fromCore = session.worldSpawnPoint - body.transform.position;
                    if (fromCore.sqrMagnitude > 0.001f)
                        up = fromCore.normalized;
                }
                return body.transform.position + up * (body.SurfaceRadius + 25f);
            }

            return new Vector3(0f, 250f, 0f);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFiniteVector(Vector3 pos)
        {
            return IsFinite(pos.x) && IsFinite(pos.y) && IsFinite(pos.z);
        }

        private void RestorePlacedBlocks(SaveData save)
        {
            foreach (var sb in save.placedBlocks)
            {
                if (!_blockById.TryGetValue(sb.itemId, out var blockItem) || blockItem.placedPrefab == null) continue;
                Quaternion finalRot = (sb.rot.w != 0f || sb.rot.x != 0f || sb.rot.y != 0f || sb.rot.z != 0f) ? sb.rot : Quaternion.Euler(0, sb.rotY, 0);
                var go = Instantiate(blockItem.placedPrefab, sb.pos, finalRot);
                go.name = blockItem.displayName + " (restored)";
                if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<BoxCollider>();
                var restoredCryobed = go.GetComponentInChildren<VoxelEngine.Building.Cryobed>(true);
                if (restoredCryobed != null)
                {
                    if (!string.IsNullOrEmpty(sb.customName)) restoredCryobed.displayName = sb.customName;
                    restoredCryobed.claimedByLocalPlayer = sb.cryobedClaimed;
                }
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

                var restoredFilters = new List<ItemDefinition>();
                if (saved.splitterState.outputFilterItemIds != null)
                {
                    foreach (var id in saved.splitterState.outputFilterItemIds)
                    {
                        if (string.IsNullOrEmpty(id) || !_itemById.TryGetValue(id, out var definition)) restoredFilters.Add(null);
                        else restoredFilters.Add(definition);
                    }
                }

                var restoredMode = VoxelEngine.Simulation.SplitterRoutingMode.RoundRobin;
                if (!string.IsNullOrEmpty(saved.splitterState.routingMode))
                {
                    try { restoredMode = (VoxelEngine.Simulation.SplitterRoutingMode)System.Enum.Parse(typeof(VoxelEngine.Simulation.SplitterRoutingMode), saved.splitterState.routingMode); }
                    catch { }
                }

                splitter.RestorePersistentState(restored, saved.splitterState.roundRobinIndex, restoredMode, restoredFilters);
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
                    screenBlock.dataSourcePositions.Clear();
                    screenBlock.dataSourceInstanceIds.Clear();
                    for (int si = 0; si < xs.Length && si < ys.Length && si < zs.Length; si++)
                    {
                        if (int.TryParse(xs[si], out int px) && int.TryParse(ys[si], out int py) && int.TryParse(zs[si], out int pz))
                        {
                            screenBlock.dataSourcePositions.Add(new Vector3Int(px, py, pz));
                            // Instance ids are session-local handles (older saves stored
                            // raw ints, 6.10+ stores EntityId handles) — neither survives a
                            // reload. Seed None and let ResolveAllProviders() re-bind live ids.
                            screenBlock.dataSourceInstanceIds.Add(EntityId.None);
                        }
                    }
                }
            }

            RestoreMaritimePorts(go, saved.maritimePorts);
            RestoreLightingRuntime(go, saved.lightingConfig);
            RestoreDefenseRuntime(go, saved.defenseState);
        }

        private static void RestoreDefenseRuntime(GameObject go, SavedDefenseState state)
        {
            if (state == null || go == null) return;

            var art = go.GetComponentInChildren<VoxelEngine.Combat.Artillery>(true);
            if (art != null)
            {
                art.filter = (VoxelEngine.Combat.TargetFilter)state.filter;
                art.autoMode = state.autoMode;
                return;
            }

            var flame = go.GetComponentInChildren<VoxelEngine.Combat.FlamethrowerTurret>(true);
            if (flame != null)
            {
                flame.filter = (VoxelEngine.Combat.TargetFilter)state.filter;
                flame.autoMode = state.autoMode;
                flame.RestoreFuelSeconds(state.fuelSeconds);
                return;
            }

            var mortar = go.GetComponentInChildren<VoxelEngine.Combat.MortarTurret>(true);
            if (mortar != null)
            {
                mortar.filter = (VoxelEngine.Combat.TargetFilter)state.filter;
                mortar.autoMode = state.autoMode;
                return;
            }

            var giant = go.GetComponentInChildren<VoxelEngine.Combat.GiantShellTurret>(true);
            if (giant != null)
            {
                giant.filter = (VoxelEngine.Combat.TargetFilter)state.filter;
                giant.autoMode = state.autoMode;
                return;
            }

            var tur = go.GetComponentInChildren<VoxelEngine.Combat.Turret>(true);
            if (tur != null)
            {
                tur.filter = (VoxelEngine.Combat.TargetFilter)state.filter;
                tur.autoMode = state.autoMode;
                tur.ammo = UnityEngine.Mathf.Max(0, state.ammo);
            }
        }

        /// <summary>Re-materialise a maritime engine's saved variable service ports.
        /// Idempotent — clears any existing dynamic ports first. Legacy saves pass a
        /// null <paramref name="saved"/> and the engine keeps its authored ports.</summary>
        private static void RestoreMaritimePorts(GameObject go, SavedMaritimePorts saved)
        {
            if (saved == null || saved.ports == null || saved.ports.Count == 0) return;
            var engine = go.GetComponentInChildren<VoxelEngine.Maritime.GridMaritimeEngine>(true);
            if (engine != null)
            {
                var records = new List<VoxelEngine.Maritime.VariablePortRecord>(saved.ports.Count);
                for (int i = 0; i < saved.ports.Count; i++)
                {
                    var p = saved.ports[i];
                    if (p == null) continue;
                    records.Add(new VoxelEngine.Maritime.VariablePortRecord(
                        (VoxelEngine.Maritime.PortService)p.service, p.localPos, p.localOutward));
                }
                engine.RestoreVariablePorts(records);
                return;
            }

            var tankBlock = go.GetComponentInChildren<VoxelEngine.GridSystem.GridBlock>(true);
            if (tankBlock == null) return;
            bool isTank = tankBlock is VoxelEngine.GridSystem.GridLiquidTank
                || tankBlock is VoxelEngine.GridSystem.GridGasTank;
            if (!isTank) return;

            var tankPorts = tankBlock.GetComponent<VoxelEngine.GridSystem.GridTankVariablePorts>();
            if (tankPorts == null) tankPorts = tankBlock.gameObject.AddComponent<VoxelEngine.GridSystem.GridTankVariablePorts>();
            var tankRecords = new List<VoxelEngine.GridSystem.GridTankPortRecord>(saved.ports.Count);
            for (int i = 0; i < saved.ports.Count; i++)
            {
                var p = saved.ports[i];
                if (p == null) continue;
                tankRecords.Add(new VoxelEngine.GridSystem.GridTankPortRecord(
                    (VoxelEngine.GridSystem.GridTankPortFamily)p.service, p.localPos, p.localOutward));
            }
            tankPorts.RebuildFromRecords(tankRecords);
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
                Quaternion finalRot = (ps.rot.w != 0f || ps.rot.x != 0f || ps.rot.y != 0f || ps.rot.z != 0f) ? ps.rot : Quaternion.Euler(0, ps.rotY, 0);
                var go = Instantiate(prefab, ps.pos, finalRot);
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
            {
                DeserializeMulti(sc, assembler.inputC, assembler.outputC, assembler.upgradeC);
                return;
            }

            var maritimeEngine = go.GetComponentInChildren<VoxelEngine.Maritime.GridMaritimeEngine>();
            if (maritimeEngine != null)
            {
                maritimeEngine.EnsureSolidFuelInput();
                maritimeEngine.EnsureModuleSlots();
                // Legacy saves hold only the fuel hopper (no containerSizes):
                // they fill the first container and leave the module slots empty.
                DeserializeMulti(sc, maritimeEngine.SolidFuelInput, maritimeEngine.ModuleSlots);
                return;
            }

            var maritimeGenerator = go.GetComponentInChildren<VoxelEngine.Maritime.GridMaritimeGenerator>();
            if (maritimeGenerator != null)
            {
                maritimeGenerator.EnsureModuleSlots();
                if (maritimeGenerator.ModuleSlots != null)
                    DeserializeInto(maritimeGenerator.ModuleSlots, sc);
                return;
            }

            var artillery = go.GetComponentInChildren<VoxelEngine.Combat.Artillery>();
            if (artillery != null)
            {
                DeserializeInto(artillery.ShellMagazine, sc);
                return;
            }

            var flamethrower = go.GetComponentInChildren<VoxelEngine.Combat.FlamethrowerTurret>();
            if (flamethrower != null)
            {
                DeserializeInto(flamethrower.FuelMagazine, sc);
                return;
            }

            var mortar = go.GetComponentInChildren<VoxelEngine.Combat.MortarTurret>();
            if (mortar != null)
            {
                DeserializeInto(mortar.ShellMagazine, sc);
                return;
            }

            var giant = go.GetComponentInChildren<VoxelEngine.Combat.GiantShellTurret>();
            if (giant != null)
                DeserializeInto(giant.ShellMagazine, sc);
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
                int wantSize = ci < sc.containerSizes.Count ? sc.containerSizes[ci] : (c != null ? c.Slots.Count : 0);
                // Skip a null container's recorded entry span so the following
                // containers stay aligned with their saved entries.
                if (c == null) { idx += wantSize; continue; }
                c.EnsureValid();
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
            // Exact block pose (present in saves written from game 6.14.0-dev on).
            public bool hasLocalPose;
            public Vector3 localPosition;
            public float currentHP;
            public bool enabled = true;
            public bool hasShapeVariant;
            public int shapeVariant;
            public string customName;
            public bool cryobedClaimed;
            public float cryobedOxygen;
            public SavedContainer container;
            public SavedPlacedBlock runtime;
        }
        [Serializable] private class SavedPlayer
        {
            public Vector3 pos; public float rotY;
            public SavedContainer container;
            // Additive in 6.22.1: two dedicated jetpack equipment slots.
            // Legacy saves leave this null and restore with empty slots.
            public SavedContainer jetpackSlots;
            // Additive in 6.23.1: sealed helmet + oxygen tank equipment slots.
            public SavedContainer helmetSlots;
            public SavedContainer oxygenTankSlots;
            public int activeHotbarIndex;
        }
        [Serializable] private class SavedPlacedBlock
        {
            public string itemId;
            public Vector3 pos; public Quaternion rot; public float rotY;
            public int hp;
            public SavedContainer container;
            public string customName;
            public bool cryobedClaimed;
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
            // Variable engine service ports (color-coded "connect from anywhere").
            // Null for every block except maritime engines that carry player-installed
            // ports. Additive — legacy saves leave it null and engines keep their
            // authored ports exactly as before.
            public SavedMaritimePorts maritimePorts;
            // Defense runtime (turret ammo / filter / autoMode / fuel buffer). Null for
            // non-defense blocks. Additive — legacy saves leave it null.
            public SavedDefenseState defenseState;
        }
        [Serializable] private class SavedDefenseState
        {
            public int filter;          // TargetFilter flags
            public bool autoMode = true;
            public int ammo;            // Auto Turret magazine count
            public float fuelSeconds;   // Flamethrower continuous fuel buffer
        }
        [Serializable] private class SavedMaritimePorts
        {
            public List<SavedVariablePort> ports = new();
        }
        [Serializable] private class SavedVariablePort
        {
            public int service;
            public Vector3 localPos;
            public Vector3 localOutward;
        }
        [Serializable] private class SavedFunnelState
        {
            public string mode; // "Import" or "Export"
            public System.Collections.Generic.List<SavedTransportItem> bufferItems = new System.Collections.Generic.List<SavedTransportItem>();
        }
        [Serializable] private class SavedSplitterState
        {
            public int roundRobinIndex;
            public string routingMode;
            public System.Collections.Generic.List<string> outputFilterItemIds = new System.Collections.Generic.List<string>();
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
            public Vector3 pos;   public Quaternion rot; public float rotY;
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
            public Vector3 pos; public Quaternion rot; public float rotY;
            public int currentDepth; public int cursorX; public int cursorZ;
            public int phase; public int rangeLvl; public int speedLvl; public int effLvl; // upgrade levels
            public SavedContainer outputContainer;
        }
    }
}
