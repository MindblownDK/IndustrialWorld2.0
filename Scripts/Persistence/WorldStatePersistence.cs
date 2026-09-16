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
        private readonly HashSet<string> _missingSavedItemWarnings = new();

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

            // The setup-authored catalog is the runtime-safe bridge for item assets
            // stored outside Resources. Without it, a portable battery/H₂ tank that
            // exists only inside a save can fail lookup at login and deserialize as an
            // empty stack even though its itemId and charge were saved correctly.
            foreach (var catalog in Resources.LoadAll<ItemPersistenceCatalog>(""))
            {
                if (catalog == null || catalog.items == null) continue;
                foreach (var item in catalog.items) CacheItem(item);
            }

            // Setup-generated content is frequently referenced by scene registries
            // without living under a Resources folder. Include every loaded asset so
            // editor and player builds resolve the same stable item IDs.
            foreach (var item in Resources.FindObjectsOfTypeAll<ItemDefinition>()) CacheItem(item);
            foreach (var catalog in Resources.FindObjectsOfTypeAll<ItemPersistenceCatalog>())
            {
                if (catalog == null || catalog.items == null) continue;
                foreach (var item in catalog.items) CacheItem(item);
            }

            // Retired item ids: a save written before an item was consolidated still names
            // the old id, and the asset behind it no longer exists. Alias the retired id to
            // its replacement so those stacks come back as the surviving item instead of
            // vanishing. An alias never overwrites a live id — if something still owns the
            // old id, that asset wins.
            foreach (var pair in ItemIdAliases.Retired)
            {
                if (_itemById.ContainsKey(pair.Key)) continue;
                if (!_itemById.TryGetValue(pair.Value, out var replacement) || replacement == null) continue;
                _itemById[pair.Key] = replacement;
                if (replacement is BlockItem aliasBlock) _blockById[pair.Key] = aliasBlock;
                if (replacement is GridBlockItem aliasGrid) _gridBlockById[pair.Key] = aliasGrid;
            }

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
                SaveRefuelPads(save);
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
            var origin = VoxelEngine.Cosmos.SpaceOrigin.Instance;
            var equipment = inv.GetComponent<VoxelEngine.Player.PlayerEquipment>();
            save.player = new SavedPlayer
            {
                pos = inv.transform.position,
                rotY = inv.transform.eulerAngles.y,
                cosmicPosX = origin != null ? origin.GetCosmicKm(inv.transform.position).x : 0d,
                cosmicPosY = origin != null ? origin.GetCosmicKm(inv.transform.position).y : 0d,
                cosmicPosZ = origin != null ? origin.GetCosmicKm(inv.transform.position).z : 0d,
                frameBody = origin != null && origin.FrameBody != null ? origin.FrameBody.DisplayName : null,
                container = SerializeContainer(inv.container),
                jetpackSlots = equipment != null ? SerializeContainer(equipment.JetpackSlots) : null,
                helmetSlots = equipment != null ? SerializeContainer(equipment.HelmetSlots) : null,
                oxygenTankSlots = equipment != null ? SerializeContainer(equipment.OxygenTankSlots) : null,
                // Additive armor slot save: ItemStack.durability contains installed
                // module tiers, so this preserves the exact upgraded armor piece.
                armorSlots = equipment != null ? SerializeContainer(equipment.ArmorSlots) : null,
                activeHotbarIndex = inv.activeHotbarIndex
            };

            // 9.57.1-dev: the scene origin is re-anchored as the world runs (orbits, rebases,
            // frame switches), so a raw scene coordinate is only meaningful in the frame it
            // was captured in. For a player standing on or flying near a body, the position
            // that survives a reload is the one taken RELATIVE to that body — the same
            // construction the world spawn already uses. That anchor is the position the
            // loader prefers; the scene coordinate and the cosmic coordinate stay in the
            // file as the fallback and the diagnostic.
            var frameBody = origin != null ? origin.FrameBody : null;
            if (frameBody != null && frameBody.settings != null)
            {
                Vector3 bodyLocal = frameBody.transform.InverseTransformPoint(inv.transform.position);
                save.player.hasAnchor = true;
                save.player.anchorBody = frameBody.settings.bodyName;
                save.player.anchorLocalX = bodyLocal.x;
                save.player.anchorLocalY = bodyLocal.y;
                save.player.anchorLocalZ = bodyLocal.z;
            }

            // 9.57.1-dev: the cosmic clock, so the solar system is where the save left it
            // (orbits, seasons and lighting all read this). Legacy saves have 0 and load at t=0.
            var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;
            save.cosmicSimulationSeconds = registry != null ? registry.SimulationSeconds : 0d;
            return true;
        }

        private static Inventory FindPlayerInventory()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            return player != null ? player.GetComponentInChildren<Inventory>() : FindAnyObjectByType<Inventory>();
        }

        private static bool IsSafePlayerSavePosition(Vector3 pos)
        {
            if (!IsFiniteVector(pos)) return false;
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (body == null)
            {
                // Deep space is a perfectly valid disconnect position (real-space flight) —
                // but it is a claim about the COSMIC position, not about the scene floats.
                // 9.57.1-dev: a scene coordinate that is inside a celestial body right now
                // is never a valid save, whatever the active gravity frame happens to be.
                // This was the entry point of the restore loop: a stale scene coordinate in
                // a deep-space frame was accepted, written on every quit, and read back the
                // next session as a spawn beside the star.
                if (IsInsideAnyBody(pos)) return false;
                if (VoxelEngine.Cosmos.GravityProvider.IsDeepSpace) return true;
                return Mathf.Abs(pos.x) < 100000f && Mathf.Abs(pos.y) < 100000f && Mathf.Abs(pos.z) < 100000f;
            }
            // Space and high-atmosphere locations are valid disconnect positions.
            // Reject only locations buried deep inside the active planetary body.
            return Vector3.Distance(pos, body.transform.position) >= body.SurfaceRadius * 0.70f;
        }

        /// <summary>
        /// True when a scene position lands inside any celestial body's crust (5% margin
        /// below its surface). Evaluated against the bodies' CURRENT scene positions using
        /// the live origin, so it must be called BEFORE any re-anchoring moves them.
        /// </summary>
        private static bool IsInsideAnyBody(Vector3 scenePos)
        {
            if (!IsFiniteVector(scenePos)) return true;
            var origin = VoxelEngine.Cosmos.SpaceOrigin.Instance;
            var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;
            if (origin == null || registry == null || !registry.IsReady) return false;

            var toCheck = new List<Vector3>();
            var radii = new List<float>();
            foreach (var kv in registry.SceneBodies)
            {
                if (kv.Key == null || kv.Key.settings == null || kv.Value == null) continue;
                toCheck.Add(kv.Value.transform.position);
                radii.Add(kv.Value.SurfaceRadius);
            }
            for (int i = 0; i < toCheck.Count; i++)
                if (Vector3.Distance(scenePos, toCheck[i]) < radii[i] * 0.95f) return true;
            return false;
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
                var paint = pb.GetComponent<VoxelEngine.Building.BlockPaint>();
                if (paint != null) entry.paintFinish = (int)paint.Finish;
                var road = pb.GetComponentInChildren<VoxelEngine.Building.AsphaltRoad>(true);
                if (road != null)
                {
                    entry.hasRoadWear = true;
                    entry.roadWear = Mathf.Clamp01(road.SavedWear);
                    entry.roadNetworkName = road.NetworkName;
                    // A culvert and a fixed bridge need nothing here: both reclassify from the
                    // ground on load. Only a drawbridge carries state the world cannot rederive —
                    // that the player asked for it to be able to open, and where the swing was.
                    var span = road.Span;
                    if (span != null && road.surfaceKind == VoxelEngine.Building.RoadSurfaceKind.Bridge)
                    {
                        entry.hasBridgeSpan = true;
                        entry.bridgeStructure = (int)span.Structure;
                        entry.bridgeOpen = Mathf.Clamp01(span.Open01);
                        entry.bridgeManualControl = !span.AutomationEnabled;
                        entry.hasBridgeCommand = true;
                        entry.bridgeWantsOpen = span.WantsOpen;
                        entry.bridgeAutoOwned = span.OpenedAutomatically;
                    }
                }
                var gst = pb.GetComponentInChildren<VoxelEngine.Gas.GasTank>();
                if (gst != null)
                {
                    entry.gasType = (int)gst.storedGasType;
                    entry.gasSelectedType = (int)gst.selectedGasType;
                    entry.gasStoredAmount = gst.storedAmount;
                }
                var worldBattery = pb.GetComponentInChildren<VoxelEngine.Power.PowerBattery>();
                if (worldBattery != null)
                {
                    entry.hasBatteryCharge = true;
                    entry.batteryCharge = worldBattery.charge;
                }
                var anchor = FindAnchoringBody(pb.transform.position);
                if (anchor != null)
                {
                    Vector3 local = anchor.transform.InverseTransformPoint(pb.transform.position);
                    if (IsFiniteVector(local))
                    {
                        entry.hasBodyAnchor = true;
                        entry.anchorBody     = anchor.settings.bodyName;
                        entry.anchorLocalX   = local.x;
                        entry.anchorLocalY   = local.y;
                        entry.anchorLocalZ   = local.z;
                    }
                }
                CaptureFactoryRuntime(pb.gameObject, entry);
                save.placedBlocks.Add(entry);
            }
        }

        private static void CaptureFactoryRuntime(GameObject go, SavedPlacedBlock entry)
        {
            var liquidTank = go.GetComponentInChildren<VoxelEngine.Fluids.WaterTank>(true);
            if (liquidTank != null)
            {
                entry.hasFluidTankState = true;
                entry.fluidTankType = (int)liquidTank.liquidType;
                entry.fluidTankLitres = Mathf.Clamp(liquidTank.StoredLitres, 0f, Mathf.Max(0f, liquidTank.capacityLitres));
            }

            var liquidPump = go.GetComponentInChildren<VoxelEngine.Fluids.WaterPump>(true);
            if (liquidPump != null)
            {
                entry.hasFluidPumpState = true;
                entry.fluidPumpType = (int)liquidPump.liquidType;
                entry.fluidPumpLitres = Mathf.Clamp(liquidPump.internalLitres, 0f, Mathf.Max(0f, liquidPump.internalCapacityLitres));
            }

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

            var armorUpgradeStation = go.GetComponentInChildren<VoxelEngine.Combat.ArmorUpgradeStation>(true);
            if (armorUpgradeStation != null)
            {
                entry.armorUpgradeStationState = new SavedArmorUpgradeStationState
                {
                    isUpgrading = armorUpgradeStation.IsUpgrading,
                    elapsedSeconds = armorUpgradeStation.ElapsedSeconds,
                    totalSeconds = armorUpgradeStation.TotalSeconds
                };
            }

            CaptureMaritimePorts(go, entry);
            CaptureLightingRuntime(go, entry);
            CaptureDefenseRuntime(go, entry);

            // 9.57.0-dev: one shared payload carries the live process state of any
            // machine that implements IMachineProcessState — the batch in progress,
            // the locked recipe, the fluid in every tank it owns and the handful of
            // numbers only that machine has (catalyst bed, reactor temperature, flare
            // totals). Written for static world machines and grid machines alike.
            var processMachine = go.GetComponentInChildren<VoxelEngine.Crafting.IMachineProcessState>(true);
            if (processMachine != null)
            {
                var processState = new VoxelEngine.Crafting.MachineProcessState();
                processMachine.CaptureProcessState(processState);
                entry.machineProcess = processState;
            }
        }


        private static void WriteAmmoPolicy(SavedDefenseState state, VoxelEngine.Combat.IDefenseFirePolicy p)
        {
            if (state == null || p == null) return;
            state.conserveAmmo = p.ConserveAmmo;
            state.reserveStock = p.ReserveStock;
            state.hasAmmoPolicy = true;
        }

        private static void ReadAmmoPolicy(SavedDefenseState state, VoxelEngine.Combat.IDefenseFirePolicy p)
        {
            if (state == null || p == null || !state.hasAmmoPolicy) return;
            p.ConserveAmmo = state.conserveAmmo;
            p.ReserveStock = state.reserveStock;
        }

        private static void WriteEngagement(SavedDefenseState state, VoxelEngine.Combat.IDefenseEngagement e)
        {
            if (state == null || e == null) return;
            state.engagementRange = e.EngagementRange;
            state.firingArcDegrees = e.FiringArcDegrees;
            state.hasEngagement = true;
        }

        private static void ReadEngagement(SavedDefenseState state, VoxelEngine.Combat.IDefenseEngagement e)
        {
            if (state == null || e == null || !state.hasEngagement) return;
            e.EngagementRange = state.engagementRange;
            e.FiringArcDegrees = state.firingArcDegrees;
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
                WriteAmmoPolicy(entry.defenseState, art);
                WriteEngagement(entry.defenseState, art);
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
                WriteAmmoPolicy(entry.defenseState, flame);
                WriteEngagement(entry.defenseState, flame);
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
                WriteAmmoPolicy(entry.defenseState, mortar);
                WriteEngagement(entry.defenseState, mortar);
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
                WriteAmmoPolicy(entry.defenseState, giant);
                WriteEngagement(entry.defenseState, giant);
                return;
            }

            var aa = go.GetComponentInChildren<VoxelEngine.Combat.AntiAirTurret>(true);
            if (aa != null)
            {
                entry.defenseState = new SavedDefenseState
                {
                    filter = (int)aa.filter,
                    autoMode = aa.autoMode,
                    ammo = 0,
                    fuelSeconds = 0f,
                    preferAerial = aa.preferAerialOnly,
                    hasPreferAerial = true
                };
                WriteAmmoPolicy(entry.defenseState, aa);
                WriteEngagement(entry.defenseState, aa);
                return;
            }

            var energy = go.GetComponentInChildren<VoxelEngine.Combat.EnergyRelicTurret>(true);
            if (energy != null)
            {
                entry.defenseState = new SavedDefenseState
                {
                    filter = (int)energy.filter,
                    autoMode = energy.autoMode,
                    ammo = 0,
                    fuelSeconds = 0f
                };
                WriteAmmoPolicy(entry.defenseState, energy);
                WriteEngagement(entry.defenseState, energy);
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
                WriteAmmoPolicy(entry.defenseState, tur);
                WriteEngagement(entry.defenseState, tur);
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

            var armorUpgradeStation = go.GetComponentInChildren<VoxelEngine.Combat.ArmorUpgradeStation>(true);
            if (armorUpgradeStation != null)
                return SerializeMulti(
                    armorUpgradeStation.ArmorSlot,
                    armorUpgradeStation.ModuleSlot,
                    armorUpgradeStation.OutputSlot);

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

            var jackPump = go.GetComponentInChildren<VoxelEngine.Crafting.Pumpjack>();
            if (jackPump != null)
            {
                // The pumpjack has been a tank-only machine since 11.0.0-dev: no input
                // barrel slot, no output slot. Its crude and its part-batch are carried
                // by the machine-process record, not by an item container. The empty
                // record is still written so this block keeps terminating the lookup —
                // without it the search would fall through to the generic container
                // handlers below and look for slots the machine no longer has.
                jackPump.EnsureContainers();
                return SerializeMulti();
            }

            var crusher = go.GetComponentInChildren<VoxelEngine.Simulation.Crusher>();
            if (crusher != null)
                return SerializeMulti(crusher.inputC, crusher.outputC, crusher.upgradeC);

            var assembler = go.GetComponentInChildren<VoxelEngine.Simulation.Assembler>();
            if (assembler != null)
                return SerializeMulti(assembler.inputC, assembler.outputC, assembler.upgradeC);

            // Petroleum-era machines (9.57.0-dev). Their item slots used to be dropped on
            // save, which meant a plant that had been fed coal and catalyst for a recipe
            // came back with empty slots. The container list of each machine is written in
            // the fixed order its restore branch mirrors.
            var distillation = go.GetComponentInChildren<VoxelEngine.Crafting.DistillationPlant>(true);
            if (distillation != null)
            {
                distillation.EnsureContainers();
                var sc = SerializeMulti(distillation.inputC, distillation.outputC);
                AttachPortSnapshot(go, sc);
                return sc;
            }

            var cracker = go.GetComponentInChildren<VoxelEngine.Crafting.CatalyticCracker>(true);
            if (cracker != null)
            {
                cracker.EnsureContainers();
                var sc = SerializeMulti(cracker.inputC, cracker.outputC);
                AttachPortSnapshot(go, sc);
                return sc;
            }

            var oilRefinery = go.GetComponentInChildren<VoxelEngine.Crafting.OilRefinery>(true);
            if (oilRefinery != null)
            {
                oilRefinery.EnsureContainers();
                // The upgrade slots matter as much as the recipe slots: refilling them
                // re-fires OnChanged, so the speed and efficiency multipliers come back
                // with the modules the player installed.
                var sc = SerializeMulti(oilRefinery.inputC, oilRefinery.outputC, oilRefinery.upgradeC);
                AttachPortSnapshot(go, sc);
                return sc;
            }

            var chemicalPlant = go.GetComponentInChildren<VoxelEngine.Industrial.StationaryChemicalPlant>(true);
            if (chemicalPlant != null)
            {
                chemicalPlant.EnsureContainers();
                var sc = SerializeMulti(chemicalPlant.inputC, chemicalPlant.outputC);
                AttachPortSnapshot(go, sc);
                return sc;
            }

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

            var aa = go.GetComponentInChildren<VoxelEngine.Combat.AntiAirTurret>();
            if (aa != null)
                return SerializeContainer(aa.AmmoMagazine);

            var energy = go.GetComponentInChildren<VoxelEngine.Combat.EnergyRelicTurret>();
            if (energy != null)
                return SerializeContainer(energy.CellMagazine);

            var gasTank = go.GetComponentInChildren<VoxelEngine.Gas.GasTank>();
            if (gasTank != null)
            {
                gasTank.EnsureContainers();
                var sc = SerializeContainer(gasTank.PortableSlot);
                // Encode bulk gas in custom fields via first empty - use SavedPlacedBlock instead.
                return sc;
            }

            // Grid batteries and world batteries both persist their one-item charger
            // dock through the existing container snapshot; bulk charge remains in
            // their dedicated saved state fields.
            var gridBattery = go.GetComponentInChildren<VoxelEngine.GridSystem.GridBattery>(true);
            if (gridBattery != null)
            {
                gridBattery.EnsureContainers();
                return SerializeContainer(gridBattery.ChargeSlot);
            }

            var powerBattery = go.GetComponentInChildren<VoxelEngine.Power.PowerBattery>();
            if (powerBattery != null)
            {
                powerBattery.EnsureContainers();
                return SerializeContainer(powerBattery.ChargeSlot);
            }

            var gridGas = go.GetComponentInChildren<VoxelEngine.GridSystem.GridGasTank>();
            if (gridGas != null)
            {
                gridGas.EnsureContainers();
                return SerializeContainer(gridGas.PortableSlot);
            }

            // Full-block ventilation unit: persist the docked suit tank (and its reserve).
            var airVent = go.GetComponentInChildren<VoxelEngine.Pressure.GridAirVent>();
            if (airVent != null && airVent.fullBlock)
            {
                airVent.EnsureContainers();
                return SerializeContainer(airVent.SuitDock);
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
                durability = s == null || s.IsEmpty ? 0 : s.durability,
                charge = s == null || s.IsEmpty ? 0 : s.charge
            };
            if (s != null && s.payload is VoxelEngine.Items.LiquidType liquidPayload)
            {
                saved.hasLiquidPayload = true;
                saved.liquidPayloadType = (int)liquidPayload;
            }
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
                int anchoredGrids = RestoreGrids(save);
                RestorePlayer(save);
                RestoreQuarries(save);
                RestoreRefuelPads(save);
                Debug.Log($"[WorldState] Loaded {save.placedTiered.Count} tiered + {save.placedBlocks.Count} blocks + {save.grids.Count} movable grids " +
                          $"({anchoredGrids} from a body anchor) from {path}");
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
                    // Rigidbody pose is authoritative for interpolated movable grids.
                    // Transform pose can lag a physics step and was responsible for
                    // some restored ships reopening at an unintended upright angle.
                    pos = grid.Body != null ? grid.Body.position : grid.transform.position,
                    rot = grid.Body != null ? grid.Body.rotation : grid.transform.rotation,
                    gridSize = (int)grid.gridSize,
                    gravityScale = grid.gravityScale,
                    dampenersOn = grid.DampenersOn,
                    wheelParkingBrake = grid.WheelControlHeld,
                    hydrogenStored = grid.HydrogenStored,
                    oxygenStored = grid.OxygenStored
                };

                // Additive 9.34.0: the ship's route book. A recorded haul run is player work,
                // so it rides the grid record rather than the recorder block's own state — the
                // book belongs to the vessel and survives the block being moved or replaced.
                var routeBook = grid.GetComponent<VoxelEngine.Navigation.RouteBook>();
                if (routeBook != null && routeBook.Count > 0)
                {
                    var shelf = routeBook.Snapshot();
                    for (int r = 0; r < shelf.Count; r++)
                    {
                        var route = shelf[r];
                        var savedRoute = new SavedRoute { name = route.routeName, speedProfile = route.speedProfileIndex,
                            travelMode = (int)route.travelMode, sceneCoordinates = route.sceneCoordinates };
                        if (route.waypoints != null)
                        {
                            for (int w = 0; w < route.waypoints.Count; w++)
                            {
                                var wp = route.waypoints[w];
                                savedRoute.waypoints.Add(new SavedWaypoint
                                {
                                    xKm = wp.positionKm.x, yKm = wp.positionKm.y, zKm = wp.positionKm.z,
                                    bodyId = wp.bodyId, label = wp.label, waymarkName = wp.waymarkName,
                                    // Additive: a pinned point without its offset would reload as a
                                    // point inside the planet.
                                    offXKm = wp.anchorOffsetKm.x, offYKm = wp.anchorOffsetKm.y,
                                    offZKm = wp.anchorOffsetKm.z,
                                });
                            }
                        }
                        entry.routes.Add(savedRoute);
                    }
                }

                // Additive 9.35.0: the ship's armed loop. A loop that was mid-service is written down as
                // *paused at the same leg*: the schedule survives, the flight does not resume at speed.
                var autopilot = grid.GetComponent<VoxelEngine.Navigation.GridRouteAutopilot>();
                if (autopilot != null && autopilot.IsArmed)
                {
                    entry.loops.Add(new SavedRouteLoop
                    {
                        routeName = autopilot.routeName ?? string.Empty,
                        startWaymark = autopilot.startWaymark ?? string.Empty,
                        endWaymark = autopilot.endWaymark ?? string.Empty,
                        mode = (int)autopilot.mode,
                        fixedRuns = autopilot.fixedRuns,
                        runsCompleted = autopilot.RunsCompleted,
                        targetCharge01 = autopilot.targetCharge01,
                        targetFuel01 = autopilot.targetFuel01,
                        targetHydrogen01 = autopilot.targetHydrogen01,
                        minimumReserve01 = autopilot.minimumReserve01,
                        haltOnWorstBlockHurt01 = autopilot.haltOnWorstBlockHurt01,
                        stopWhenCargoFull = autopilot.stopWhenCargoFull,
                        stopWhenCargoEmpty = autopilot.stopWhenCargoEmpty,
                    });
                }
                if (grid.Body != null)
                {
                    entry.velocity = grid.Body.linearVelocity;
                    entry.angularVelocity = grid.Body.angularVelocity;
                }

                // ── 10.1.0: anchor the grid to the body it belongs to ──────────────
                // Without this a hull reloads at a scene coordinate that only described
                // its place in the frame the save was written in: after the system has run
                // on, or after a frame switch, that coordinate can be thousands of
                // kilometres away from where the ship was parked — or inside a planet.
                // The scene pose stays in the file as the fallback and the diagnostic.
                CaptureGridBodyAnchor(grid, entry);

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

                    if (block is VoxelEngine.Gas.GasVent ventBlock)
                    {
                        // Louvre position and the lifetime counter are the only two things
                        // a vent remembers; the gas itself is already gone.
                        savedBlock.hasGasVentState = true;
                        savedBlock.gasVentOpen = ventBlock.open;
                        savedBlock.gasVentDumped = ventBlock.TotalDumped;
                        savedBlock.hasVentilationScaleState = true;
                        savedBlock.ventilationAutoScale = ventBlock.autoScaleFlow;
                    }
                    else if (block is VoxelEngine.Gas.GridFlareStack flareBlock)
                    {
                        savedBlock.hasFlareStackState = true;
                        savedBlock.flareStackOpen = flareBlock.open;
                        savedBlock.flareStackRecovery = flareBlock.wasteHeatRecovery;
                        savedBlock.flareStackGasDumped = flareBlock.TotalGasBurned;
                        savedBlock.flareStackLiquidDumped = flareBlock.TotalLiquidBurned;
                    }
                    else if (block is VoxelEngine.Maritime.GridMaritimeEngine airModeEngine)
                    {
                        // Which policy the engine follows when its plumbed line runs dry is a
                        // player decision, so it survives a reload like any other setting.
                        savedBlock.hasEngineAirModeState = true;
                        savedBlock.engineAirFallback = airModeEngine.allowAirFallbackOnStarvedLine;
                    }
                    else if (block is GridGasTank gasTankBlock)
                    {
                        savedBlock.hasGasTankState = true;
                        savedBlock.gasTankType = (int)gasTankBlock.gasType;
                        savedBlock.gasTankStored = gasTankBlock.stored;
                        savedBlock.gasTankMode = (int)gasTankBlock.mode;
                    }
                    else if (block is GridLiquidTank liquidTankBlock)
                    {
                        savedBlock.hasLiquidTankState = true;
                        savedBlock.liquidTankType = (int)liquidTankBlock.liquidType;
                        savedBlock.liquidTankStored = liquidTankBlock.stored;
                        savedBlock.liquidTankMode = (int)liquidTankBlock.mode;
                    }

                    if (block is VoxelEngine.Thermal.GridHeatshield heatshield)
                    {
                        savedBlock.hasHeatshieldState = true;
                        savedBlock.heatshieldAblator = heatshield.ablatorRemaining;
                    }

                    if (block is VoxelEngine.Pressure.GridAirVent autoVent
                        || block is VoxelEngine.Pressure.GridExhaustScrubber autoScrub
                        || block is VoxelEngine.Gas.GasVent autoGas)
                    {
                        // How hard a ventilation unit is allowed to work is a player decision
                        // taken on the panel, and on the scrubber it doubles as the rating of a
                        // gas line that no longer exists in the save, so both survive a reload.
                        savedBlock.hasVentilationScaleState = true;
                        savedBlock.ventilationAutoScale = block is VoxelEngine.Pressure.GridAirVent av
                            ? av.autoScaleFlow : true;
                        savedBlock.ventilationSupplyFlow = block is VoxelEngine.Pressure.GridExhaustScrubber as2
                            ? as2.supplyFlowLitresPerSecond : 0f;
                    }

                    if (block is GridBattery gridBattery)
                    {
                        savedBlock.hasGridBatteryState = true;
                        savedBlock.gridBatteryStoredWh = gridBattery.storedWh;
                        savedBlock.gridBatteryMode = (int)gridBattery.mode;
                    }

                    if (block is VoxelEngine.Maritime.GridGearbox gearbox)
                    {
                        savedBlock.hasGearboxState = true;
                        savedBlock.gearboxRatio = gearbox.EffectiveRatio;
                        savedBlock.gearboxSelectedGear = gearbox.selectedGear;
                    }

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

                // Belts are logical links rather than blocks, so they live on the
                // grid payload and restore after their shaft endpoints exist.
                var belts = grid.GetComponent<VoxelEngine.Maritime.MechanicalBeltNetwork>();
                if (belts != null)
                {
                    belts.PruneMissingEndpoints();
                    foreach (var link in belts.Links)
                    {
                        entry.mechanicalBelts.Add(new SavedMechanicalBelt
                        {
                            endpointA = link.endpointA,
                            endpointB = link.endpointB
                        });
                    }
                }

                // Sealed-room atmosphere: the oxygen charge plus whatever heat and foul
                // gas the volume has trapped (9.32.0). Room shapes are re-solved from the
                // restored hull, so a rebuilt ship stays valid.
                var pressure = grid.GetComponent<VoxelEngine.Pressure.GridPressureSystem>();
                if (pressure != null)
                {
                    foreach (var room in pressure.Rooms)
                    {
                        if (room == null) continue;
                        bool hasCharge = room.OxygenLitres > 0.01f;
                        bool hasAtmosphere = room.IsSealed
                            && (room.HeatLoadC > 0.01f || room.ExhaustHeatC > 0.01f);
                        if (!hasCharge && !hasAtmosphere) continue;
                        entry.roomCharges.Add(new SavedRoomCharge
                        {
                            anchor = room.Anchor,
                            oxygenLitres = room.OxygenLitres,
                            heatLoadC = room.IsSealed ? room.HeatLoadC : 0f,
                            exhaustLoadC = room.IsSealed ? room.ExhaustHeatC : 0f
                        });
                    }
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

        private int RestoreGrids(SaveData save)
        {
            int anchored = 0;
            if (save.grids == null || save.grids.Count == 0) return 0;

            foreach (var savedGrid in save.grids)
            {
                if (savedGrid == null || savedGrid.blocks == null || savedGrid.blocks.Count == 0) continue;
                if (!System.Enum.IsDefined(typeof(GridSize), savedGrid.gridSize))
                {
                    Debug.LogWarning("[WorldState] Skipped a movable grid with an unknown grid size.");
                    continue;
                }

                // 10.1.0: the anchor decides where the hull comes back. A grid with no anchor
                // — every save written before this round — resolves to its saved scene pose.
                ResolveSavedGridPose(savedGrid, out Vector3 gridPosition, out Quaternion gridRotation, out bool fromAnchor);
                if (fromAnchor) anchored++;

                var grid = GridEntity.Create(gridPosition, (GridSize)savedGrid.gridSize);
                grid.name = "Grid (restored)";
                grid.gravityScale = savedGrid.gravityScale > 0f ? savedGrid.gravityScale : grid.gravityScale;
                grid.DampenersOn = savedGrid.dampenersOn;
                grid.SetWheelParkingBrake(savedGrid.wheelParkingBrake);
                grid.RestorePersistentPose(gridPosition, gridRotation,
                    savedGrid.wheelParkingBrake ? Vector3.zero : savedGrid.velocity,
                    savedGrid.wheelParkingBrake ? Vector3.zero : savedGrid.angularVelocity);
                grid.HydrogenStored = Mathf.Max(0f, savedGrid.hydrogenStored);
                grid.OxygenStored = Mathf.Max(0f, savedGrid.oxygenStored);

                // Structural blocks must be present before Detail blocks can restore
                // their host-cell relationship and attached pipe topology.
                RestoreGridBlocks(grid, savedGrid.blocks, false);
                RestoreGridBlocks(grid, savedGrid.blocks, true);

                // Route books restore onto the grid component, created here if the save has one
                // and the ship somehow lost its recorder: losing a run because a block was
                // uninstalled would be a worse outcome than an orphaned shelf.
                if (savedGrid.routes != null && savedGrid.routes.Count > 0)
                {
                    var routeBook = VoxelEngine.Navigation.RouteBook.For(grid, create: true);
                    if (routeBook != null)
                    {
                        var restored = new List<VoxelEngine.Navigation.ShipRoute>(savedGrid.routes.Count);
                        for (int r = 0; r < savedGrid.routes.Count; r++)
                        {
                            var sr = savedGrid.routes[r];
                            if (sr == null || string.IsNullOrWhiteSpace(sr.name)) continue;
                            var route = new VoxelEngine.Navigation.ShipRoute
                            {
                                routeName = sr.name,
                                travelMode = (VoxelEngine.Navigation.RouteTravelMode)sr.travelMode,
                                sceneCoordinates = sr.sceneCoordinates,
                                speedProfileIndex = Mathf.Clamp(sr.speedProfile, 0, 2),
                            };
                            if (sr.waypoints != null)
                            {
                                for (int w = 0; w < sr.waypoints.Count; w++)
                                {
                                    var sw = sr.waypoints[w];
                                    if (sw == null) continue;
                                    var restoredWp = new VoxelEngine.Navigation.RouteWaypoint(
                                        new Unity.Mathematics.double3(sw.xKm, sw.yKm, sw.zKm), null, sw.label);
                                    if (!string.IsNullOrWhiteSpace(sw.waymarkName))
                                        restoredWp.waymarkName = sw.waymarkName;
                                    if (!string.IsNullOrWhiteSpace(sw.bodyId))
                                    {
                                        // The anchor is re-derived from the offset rather than trusted
                                        // from the record: the registry's live position is the truth,
                                        // and a body whose own place changed shape still gets a sane
                                        // point relative to it.
                                        var reg = VoxelEngine.Cosmos.CosmicRegistry.Instance;
                                        var host = VoxelEngine.Navigation.RouteWaypoint.FindBody(reg, sw.bodyId);
                                        if (host != null)
                                        {
                                            restoredWp.positionKm = reg.CosmicPositionOf(host)
                                                + new Unity.Mathematics.double3(sw.offXKm, sw.offYKm, sw.offZKm);
                                            restoredWp.bodyId = sw.bodyId;
                                            restoredWp.anchorOffsetKm = new Unity.Mathematics.double3(
                                                sw.offXKm, sw.offYKm, sw.offZKm);
                                        }
                                        else
                                        {
                                            restoredWp.bodyId = sw.bodyId;
                                            restoredWp.anchorOffsetKm = new Unity.Mathematics.double3(
                                                sw.offXKm, sw.offYKm, sw.offZKm);
                                        }
                                    }
                                    route.waypoints.Add(restoredWp);
                                }
                            }
                            if (route.waypoints.Count > 0) restored.Add(route);
                        }
                        routeBook.Restore(restored);
                    }
                }

                // Additive 9.35.0: reload the armed loop. Restored *paused*, on purpose: a schedule the
                // player set before quitting should never resume a burn on its own the moment the world
                // loads. One button press in the panel, and the ship has its job back.
                if (savedGrid.loops != null && savedGrid.loops.Count > 0)
                {
                    var savedLoop = savedGrid.loops[0];
                    if (savedLoop != null)
                    {
                        var ap = grid.GetComponent<VoxelEngine.Navigation.GridRouteAutopilot>();
                        if (ap == null) ap = grid.gameObject.AddComponent<VoxelEngine.Navigation.GridRouteAutopilot>();
                        ap.routeName = savedLoop.routeName ?? string.Empty;
                        ap.startWaymark = savedLoop.startWaymark ?? string.Empty;
                        ap.endWaymark = savedLoop.endWaymark ?? string.Empty;
                        ap.mode = (VoxelEngine.Navigation.AutoRunMode)Mathf.Clamp(savedLoop.mode, 0, 2);
                        ap.fixedRuns = Mathf.Max(1, savedLoop.fixedRuns);
                        ap.targetCharge01 = Mathf.Clamp01(savedLoop.targetCharge01);
                        ap.targetFuel01 = Mathf.Clamp01(savedLoop.targetFuel01);
                        ap.targetHydrogen01 = Mathf.Clamp01(savedLoop.targetHydrogen01);
                        ap.minimumReserve01 = Mathf.Clamp01(savedLoop.minimumReserve01);
                        ap.haltOnWorstBlockHurt01 = Mathf.Clamp01(savedLoop.haltOnWorstBlockHurt01);
                        ap.stopWhenCargoFull = savedLoop.stopWhenCargoFull;
                        ap.stopWhenCargoEmpty = savedLoop.stopWhenCargoEmpty;
                        ap.RestorePaused(savedLoop.runsCompleted);
                    }
                }

                if (savedGrid.mechanicalBelts != null && savedGrid.mechanicalBelts.Count > 0)
                {
                    var links = new List<VoxelEngine.Maritime.MechanicalBeltLink>(savedGrid.mechanicalBelts.Count);
                    foreach (var savedBelt in savedGrid.mechanicalBelts)
                    {
                        if (savedBelt == null) continue;
                        links.Add(new VoxelEngine.Maritime.MechanicalBeltLink(savedBelt.endpointA, savedBelt.endpointB));
                    }
                    VoxelEngine.Maritime.MechanicalBeltNetwork.GetOrAdd(grid)?.RestoreLinks(links);
                }

                if (savedGrid.roomCharges != null && savedGrid.roomCharges.Count > 0)
                {
                    var pressure = VoxelEngine.Pressure.GridPressureSystem.For(grid);
                    if (pressure != null)
                    {
                        pressure.Solve();
                        foreach (var charge in savedGrid.roomCharges)
                        {
                            if (charge == null) continue;
                            foreach (var room in pressure.Rooms)
                            {
                                if (room == null || room.Anchor != charge.anchor) continue;
                                room.OxygenLitres = Mathf.Clamp(charge.oxygenLitres, 0f, room.CapacityLitres);
                                // Legacy saves leave both at zero: a room that never
                                // stored an atmosphere loads as a room that has none.
                                room.HeatLoadC = Mathf.Clamp(charge.heatLoadC, 0f,
                                    VoxelEngine.Thermal.ThermalRules.RoomMaxRiseC);
                                room.ExhaustHeatC = Mathf.Clamp(charge.exhaustLoadC, 0f,
                                    VoxelEngine.Thermal.ThermalRules.RoomExhaustReferenceC);
                                break;
                            }
                        }
                    }
                }

                grid.RecalculateMass();
                // A powered/hydrogen-fuelled unlocked grid with dampeners on must
                // never resume a stale serialized drift vector in space.
                grid.StabilizeRestoredVelocityIfPossible();
                // Colliders now exist, so resolve only the small post-load terrain
                // interpenetration before the restore pose releases physics.
                grid.ResolvePersistentGroundClearance();
            }

            return anchored;
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
                if (saved.paintFinish != 0)
                {
                    var gp = block.GetComponent<VoxelEngine.Building.BlockPaint>() ?? block.gameObject.AddComponent<VoxelEngine.Building.BlockPaint>();
                    gp.Finish = (VoxelEngine.Building.PaintFinishId)saved.paintFinish;
                }
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
                // A battered hull reloads battered: cracks are derived from saved HP.
                block.RefreshDamageVisual();
                if (saved.paintFinish != 0)
                {
                    var gp = block.GetComponent<VoxelEngine.Building.BlockPaint>() ?? block.gameObject.AddComponent<VoxelEngine.Building.BlockPaint>();
                    gp.Finish = (VoxelEngine.Building.PaintFinishId)saved.paintFinish;
                }
                if (saved.hasEngineAirModeState
                    && block is VoxelEngine.Maritime.GridMaritimeEngine restoredEngine)
                    restoredEngine.allowAirFallbackOnStarvedLine = saved.engineAirFallback;
                else if (saved.hasGasVentState && block is VoxelEngine.Gas.GasVent restoredVent)
                {
                    restoredVent.open = saved.gasVentOpen;
                    restoredVent.TotalDumped = Mathf.Max(0f, saved.gasVentDumped);
                    if (saved.hasVentilationScaleState) restoredVent.autoScaleFlow = saved.ventilationAutoScale;
                }
                else if (saved.hasFlareStackState && block is VoxelEngine.Gas.GridFlareStack restoredFlare)
                {
                    restoredFlare.open = saved.flareStackOpen;
                    restoredFlare.wasteHeatRecovery = saved.flareStackRecovery;
                    restoredFlare.TotalGasBurned = Mathf.Max(0f, saved.flareStackGasDumped);
                    restoredFlare.TotalLiquidBurned = Mathf.Max(0f, saved.flareStackLiquidDumped);
                }
                else if (saved.hasVentilationScaleState
                    && (block is VoxelEngine.Pressure.GridAirVent
                        || block is VoxelEngine.Pressure.GridExhaustScrubber))
                {
                    // The air vent and the scrubber have no state of their own to restore, so
                    // this is a standalone branch: only the two tuning decisions ride on it.
                    if (block is VoxelEngine.Pressure.GridAirVent restoredAutoVent)
                        restoredAutoVent.autoScaleFlow = saved.ventilationAutoScale;
                    if (block is VoxelEngine.Pressure.GridExhaustScrubber restoredScrub)
                        restoredScrub.supplyFlowLitresPerSecond = Mathf.Max(0f, saved.ventilationSupplyFlow);
                }
                else if (saved.hasGasTankState && block is GridGasTank restoredGridGas)
                {
                    if (System.Enum.IsDefined(typeof(VoxelEngine.Gas.GasType), saved.gasTankType))
                        restoredGridGas.gasType = (VoxelEngine.Gas.GasType)saved.gasTankType;
                    if (System.Enum.IsDefined(typeof(GridTankMode), saved.gasTankMode))
                        restoredGridGas.mode = (GridTankMode)saved.gasTankMode;
                    restoredGridGas.stored = Mathf.Clamp(saved.gasTankStored, 0f, restoredGridGas.capacity);
                    restoredGridGas.blockName = $"{restoredGridGas.gasType} Tank";
                }
                else if (saved.hasLiquidTankState && block is GridLiquidTank restoredGridLiquid)
                {
                    if (System.Enum.IsDefined(typeof(LiquidType), saved.liquidTankType))
                        restoredGridLiquid.liquidType = (LiquidType)saved.liquidTankType;
                    if (System.Enum.IsDefined(typeof(GridTankMode), saved.liquidTankMode))
                        restoredGridLiquid.mode = (GridTankMode)saved.liquidTankMode;
                    restoredGridLiquid.stored = Mathf.Clamp(saved.liquidTankStored, 0f, restoredGridLiquid.capacity);
                }

                if (saved.hasHeatshieldState && block is VoxelEngine.Thermal.GridHeatshield restoredShield)
                    restoredShield.ablatorRemaining =
                        Mathf.Clamp(saved.heatshieldAblator, 0f, restoredShield.ablatorCapacity);

                if (saved.hasGridBatteryState && block is GridBattery restoredGridBattery)
                {
                    if (System.Enum.IsDefined(typeof(GridBatteryMode), saved.gridBatteryMode))
                        restoredGridBattery.mode = (GridBatteryMode)saved.gridBatteryMode;
                    restoredGridBattery.storedWh = Mathf.Clamp(saved.gridBatteryStoredWh, 0f, restoredGridBattery.capacityWh);
                }

                if (saved.hasGearboxState && block is VoxelEngine.Maritime.GridGearbox restoredGearbox)
                    restoredGearbox.RestorePersistentSettings(saved.gearboxRatio, saved.gearboxSelectedGear);

                if (saved.container != null) RestoreContainer(go, saved.container);
                if (saved.runtime != null) RestoreFactoryRuntime(go, saved.runtime);
            }
        }

        private void RestorePlayer(SaveData save)
        {
            if (save.player == null) return;
            var inv = FindAnyObjectByType<Inventory>();
            if (inv == null) return;

            // ── 9.57.1-dev: decide the restore pose BEFORE anything moves the origin ──
            // Priority: the body anchor (exact, frame-independent) → the raw scene
            // coordinate if it is coherent with the scene we just loaded → nothing at all,
            // which leaves the player to PlayerSpawner's bed/world-spawn path. A rejected
            // save is never rewritten and never guessed at.
            Vector3 restorePosition = default;
            bool hasRestorePosition = TryResolveSavedPlayerPosition(save.player, out restorePosition, out bool bodyAnchored);
            if (!hasRestorePosition && save.player.container != null)
            {
                Debug.LogWarning("[WorldState] Saved player position was not coherent with the loaded scene " +
                                 "(inside a celestial body, or stale relative to a re-anchored origin). " +
                                 "The player is placed by the spawn system at the bed/world spawn instead, and the " +
                                 "rejected position is not written back over the save.");
            }

            float restoreRotY = IsFinite(save.player.rotY) ? save.player.rotY : 0f;

            // ── Cosmic clock first (9.57.1-dev) ──
            // The system is generated at t=0 on every load, so bodies must be put back on
            // the reading the save was written at before any cosmic coordinate is resolved.
            var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;
            if (registry != null && registry.IsReady && save.cosmicSimulationSeconds > 0d)
                registry.RestoreSimulationSeconds(save.cosmicSimulationSeconds);

            bool deepSpaceRestored = false;
            if (hasRestorePosition)
            {
                // Teleport only after validating the coordinates. A corrupt NaN/Infinity
                // player pose must never touch the live transform because it can poison
                // physics, chunk streaming, and follow-up autosaves before PlayerSpawner
                // gets a chance to choose a fresh/bed spawn.
                var cc = inv.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                inv.transform.position = restorePosition;
                inv.transform.eulerAngles = new Vector3(0, restoreRotY, 0);
                if (cc != null) cc.enabled = true;

                // Real-space restore for a genuine deep-space / high-orbit logout: re-anchor
                // the floating origin at the saved cosmic position and re-enter the saved
                // frame. This is ONLY correct when the saved position is really out in space
                // — re-anchoring for a planet-side save moves every celestial body while the
                // static blocks and grids stay at their saved scene coordinates, which is
                // exactly how a base ends up floating in space beside a player who never
                // left the ground (9.57.1-dev).
                bool deepSpaceSave = !bodyAnchored && IsClearOfEveryBody(restorePosition);
                if (deepSpaceSave && HasUsableCosmicPosition(save.player)
                    && VoxelEngine.Cosmos.CosmicRegistry.Instance != null
                    && VoxelEngine.Cosmos.CosmicRegistry.Instance.IsReady)
                {
                    var bootstrap = VoxelEngine.Cosmos.CosmosBootstrap.Instance;
                    if (bootstrap != null)
                    {
                        bootstrap.RestoreCosmicState(
                            new Vector3((float)save.player.cosmicPosX, (float)save.player.cosmicPosY, (float)save.player.cosmicPosZ),
                            save.player.frameBody);
                        // Re-apply the scene position AFTER the anchor settled so the
                        // CharacterController sits exactly on the saved pose.
                        inv.transform.position = restorePosition;
                        inv.transform.eulerAngles = new Vector3(0, restoreRotY, 0);
                        deepSpaceRestored = true;
                    }
                }
                else if (bodyAnchored)
                {
                    Debug.Log($"[WorldState] Player restored from its body anchor '{save.player.anchorBody}' at {restorePosition} " +
                              "(no origin re-anchor — the scene already matches the body).");
                }
            }

            // 9.57.1-dev: the loaded frame can disagree with where the player actually is
            // (a stale 'SOL'/deep-space frame over a ground position is exactly what turned a
            // normal rejoin into a fall through an unstreamed world). Re-point the frame and
            // the voxel streamer at the body the restored position sits on before anything
            // else runs, so the surface exists when the player is handed control.
            if (hasRestorePosition && !deepSpaceRestored) EnsureStreamingBodyAt(restorePosition);

            // Inventory + equipment.
            if (save.player.container != null) DeserializeInto(inv.container, save.player.container);
            var equipment = inv.GetComponent<VoxelEngine.Player.PlayerEquipment>();
            if (equipment == null) equipment = inv.gameObject.AddComponent<VoxelEngine.Player.PlayerEquipment>();
            if (save.player.jetpackSlots != null) DeserializeInto(equipment.JetpackSlots, save.player.jetpackSlots);
            if (save.player.helmetSlots != null) DeserializeInto(equipment.HelmetSlots, save.player.helmetSlots);
            if (save.player.oxygenTankSlots != null) DeserializeInto(equipment.OxygenTankSlots, save.player.oxygenTankSlots);
            if (save.player.armorSlots != null) DeserializeInto(equipment.ArmorSlots, save.player.armorSlots);
            inv.SetActiveHotbar(save.player.activeHotbarIndex);
        }

        /// <summary>
        /// Resolve the pose a saved player record should be restored at, in this order:
        /// the body anchor (exact and frame-independent), then the raw scene coordinate
        /// when it is coherent with the freshly loaded scene. Returns false when neither
        /// can be trusted, which hands the decision to the bed/world-spawn path.
        /// </summary>
        private bool TryResolveSavedPlayerPosition(SavedPlayer player, out Vector3 position, out bool bodyAnchored)
        {
            position = default;
            bodyAnchored = false;
            if (player == null) return false;

            if (player.hasAnchor && !string.IsNullOrEmpty(player.anchorBody))
            {
                var body = FindSceneBodyByName(player.anchorBody);
                if (body != null)
                {
                    Vector3 anchored = body.transform.TransformPoint(
                        new Vector3(player.anchorLocalX, player.anchorLocalY, player.anchorLocalZ));
                    if (IsFiniteVector(anchored))
                    {
                        position = anchored;
                        bodyAnchored = true;
                        return true;
                    }
                }
                else
                {
                    Debug.LogWarning($"[WorldState] Saved body anchor '{player.anchorBody}' is not in this scene; " +
                                     "falling back to the saved scene coordinate.");
                }
            }

            if (IsFiniteVector(player.pos) && IsSafePlayerSavePosition(player.pos))
            {
                position = player.pos;
                return true;
            }
            return false;
        }

        /// <summary>The scene body whose settings name matches, or null. Used by the player and grid restores.</summary>
        private static VoxelEngine.Cosmos.CelestialBody FindSceneBodyByName(string bodyName)
        {
            var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;
            if (registry != null && registry.SceneBodies != null)
            {
                foreach (var kv in registry.SceneBodies)
                {
                    if (kv.Key == null || kv.Key.settings == null || kv.Value == null) continue;
                    if (string.Equals(kv.Key.settings.bodyName, bodyName, StringComparison.OrdinalIgnoreCase))
                        return kv.Value;
                }
            }
            var active = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (active != null && active.settings != null
                && string.Equals(active.settings.bodyName, bodyName, StringComparison.OrdinalIgnoreCase))
                return active;
            var home = VoxelEngine.Cosmos.CosmosBootstrap.Instance != null
                ? VoxelEngine.Cosmos.CosmosBootstrap.Instance.HomeBody : null;
            if (home != null && home.settings != null
                && string.Equals(home.settings.bodyName, bodyName, StringComparison.OrdinalIgnoreCase))
                return home;
            return null;
        }

        /// <summary>True when the position is clear of every body's surface (2 km of margin) — a real space restore, not a surface one.</summary>
        private static bool IsClearOfEveryBody(Vector3 scenePos)
        {
            var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;
            if (registry == null || !registry.IsReady) return true;
            bool sawBody = false;
            foreach (var kv in registry.SceneBodies)
            {
                if (kv.Key == null || kv.Key.settings == null || kv.Value == null) continue;
                sawBody = true;
                float altitude = Vector3.Distance(scenePos, kv.Value.transform.position) - kv.Value.SurfaceRadius;
                if (altitude < 2000f) return false;   // inside an atmosphere / low orbit
            }
            return sawBody;
        }

        /// <summary>
        /// Point the reference frame AND the voxel streamer at the body the given scene
        /// position sits on/near (2000 m window). A no-op when they already agree or when no
        /// body is close, so space positions are never dragged onto a planet.
        /// </summary>
        private static void EnsureStreamingBodyAt(Vector3 scenePos)
        {
            var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;
            var origin = VoxelEngine.Cosmos.SpaceOrigin.Instance;
            var bootstrap = VoxelEngine.Cosmos.CosmosBootstrap.Instance;
            if (registry == null || !registry.IsReady || origin == null || bootstrap == null) return;

            VoxelEngine.Cosmos.CelestialBody nearest = null;
            float bestAltitude = 2000f;
            foreach (var kv in registry.SceneBodies)
            {
                if (kv.Key == null || kv.Key.settings == null || kv.Value == null) continue;
                float altitude = Vector3.Distance(scenePos, kv.Value.transform.position) - kv.Value.SurfaceRadius;
                if (altitude < bestAltitude) { bestAltitude = altitude; nearest = kv.Value; }
            }
            if (nearest == null) return;
            if (origin.FrameBody == nearest && bootstrap.CurrentFrameBody == nearest) return;

            origin.SetFrame(nearest);
            bootstrap.ForceStreamingBody(nearest);
            Debug.LogWarning($"[WorldState] The loaded frame did not match the restored player position; " +
                             $"re-targeted streaming to '{nearest.DisplayName}' ({bestAltitude:0} m above its surface) " +
                             "so the ground is streamed under the player.");
        }

        /// <summary>True when the cosmic fields describe a real place and not a never-written record.</summary>
        private static bool HasUsableCosmicPosition(SavedPlayer player)
        {
            if (player == null) return false;
            if (!IsFinite((float)player.cosmicPosX) || !IsFinite((float)player.cosmicPosY)
                || !IsFinite((float)player.cosmicPosZ)) return false;
            return Mathf.Abs((float)player.cosmicPosX)
                 + Mathf.Abs((float)player.cosmicPosY)
                 + Mathf.Abs((float)player.cosmicPosZ) > 0.001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFiniteVector(Vector3 pos)
        {
            return IsFinite(pos.x) && IsFinite(pos.y) && IsFinite(pos.z);
        }

        private static bool IsFiniteQuaternion(Quaternion q)
        {
            return IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w);
        }

        private void RestorePlacedBlocks(SaveData save)
        {
            int restored = 0;
            int anchored = 0;
            foreach (var sb in save.placedBlocks)
            {
                if (!_blockById.TryGetValue(sb.itemId, out var blockItem) || blockItem.placedPrefab == null) continue;
                Quaternion finalRot = (sb.rot.w != 0f || sb.rot.x != 0f || sb.rot.y != 0f || sb.rot.z != 0f) ? sb.rot : Quaternion.Euler(0, sb.rotY, 0);
                Vector3 spawnPos = ResolvePlacedBlockPosition(sb, out bool fromAnchor);
                if (fromAnchor) anchored++;
                var go = Instantiate(blockItem.placedPrefab, spawnPos, finalRot);
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
                // A battered base still looks battered after a reload (9.30.0).
                VoxelEngine.Thermal.BlockDamageVisual.ReportDamage(pb, pb.Damage01);
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
                // Roads re-form their runs from adjacency as they come back, so the wear has to be
                // applied AFTER the cell exists: raising, never lowering, which is what lets the
                // last cell of a strip to restore set the condition for all of them.
                // Additive label: missing fields in legacy saves restore as unnamed.
                go.GetComponentInChildren<VoxelEngine.Building.AsphaltRoad>(true)
                    ?.SetNetworkName(sb.roadNetworkName);
                if (sb.hasRoadWear)
                {
                    var restoredRoad = go.GetComponentInChildren<VoxelEngine.Building.AsphaltRoad>(true);
                    restoredRoad?.ApplySavedWear(sb.roadWear);
                }
                if (sb.hasBridgeSpan)
                {
                    var restoredDeck = go.GetComponentInChildren<VoxelEngine.Building.AsphaltRoad>(true);
                    if (restoredDeck != null
                        && restoredDeck.surfaceKind == VoxelEngine.Building.RoadSurfaceKind.Bridge)
                    {
                        // The leaf meshes are surfaced from the deck block's own material, so a
                        // reloaded drawbridge swings the same plate it was built from rather than
                        // an untextured quad.
                        Material restoredDeckMat = null;
                        if (blockItem != null && blockItem.placedPrefab != null)
                        {
                            var deckRenderer = blockItem.placedPrefab
                                .GetComponentInChildren<MeshRenderer>(true);
                            if (deckRenderer != null) restoredDeckMat = deckRenderer.sharedMaterial;
                        }
                        VoxelEngine.Building.BridgeSpan.RestoreCell(
                            restoredDeck,
                            (VoxelEngine.Building.BridgeStructure)sb.bridgeStructure,
                            sb.bridgeOpen, restoredDeckMat);
                        restoredDeck.Span?.RestoreControl(sb.bridgeManualControl, sb.hasBridgeCommand,
                            sb.bridgeWantsOpen, sb.bridgeAutoOwned);
                    }
                }
                if (sb.paintFinish != 0)
                {
                    var paint = go.GetComponent<VoxelEngine.Building.BlockPaint>() ?? go.AddComponent<VoxelEngine.Building.BlockPaint>();
                    paint.Finish = (VoxelEngine.Building.PaintFinishId)sb.paintFinish;
                }
                var gst = go.GetComponentInChildren<VoxelEngine.Gas.GasTank>();
                if (gst != null)
                {
                    gst.EnsureContainers();
                    gst.selectedGasType = (VoxelEngine.Gas.GasType)sb.gasSelectedType;
                    gst.storedGasType = (VoxelEngine.Gas.GasType)sb.gasType;
                    gst.storedAmount = Mathf.Max(0f, sb.gasStoredAmount);
                    if (gst.storedAmount <= 0f && gst.selectedGasType != VoxelEngine.Gas.GasType.None)
                        gst.storedGasType = gst.selectedGasType;
                }
                if (sb.hasBatteryCharge)
                {
                    var worldBattery = go.GetComponentInChildren<VoxelEngine.Power.PowerBattery>();
                    if (worldBattery != null)
                        worldBattery.charge = Mathf.Clamp(sb.batteryCharge, 0f, Mathf.Max(1f, worldBattery.capacityWattHours));
                }
                restored++;
            }

            if (restored > 0)
            {
                Debug.Log($"[WorldState] Restored {restored} placed block(s) — {anchored} from a body anchor, " +
                          $"{restored - anchored} at their saved scene coordinate" +
                          (anchored == restored ? "." : " (a scene coordinate is only valid in the frame it was written in)."));
            }
        }

        /// <summary>
        /// Where a saved placed block belongs in THIS scene. The body anchor wins: it is the
        /// position taken relative to the body the block was standing on, so it survives the
        /// body moving, the origin rebasing and a frame switch. Without an anchor (legacy
        /// saves, or a block that was floating in deep space) the saved scene coordinate is
        /// used unchanged.
        /// </summary>
        private static Vector3 ResolvePlacedBlockPosition(SavedPlacedBlock block, out bool fromAnchor)
        {
            fromAnchor = false;
            if (block.hasBodyAnchor && !string.IsNullOrEmpty(block.anchorBody))
            {
                var body = FindSceneBodyByName(block.anchorBody);
                if (body != null)
                {
                    Vector3 anchored = body.transform.TransformPoint(
                        new Vector3(block.anchorLocalX, block.anchorLocalY, block.anchorLocalZ));
                    if (IsFiniteVector(anchored)) { fromAnchor = true; return anchored; }
                }
                else
                {
                    Debug.LogWarning($"[WorldState] Placed block '{block.itemId}' was anchored to '{block.anchorBody}', " +
                                     "which is not in this scene; restoring it at its saved scene coordinate instead.");
                }
            }
            return IsFiniteVector(block.pos) ? block.pos : Vector3.zero;
        }

        /// <summary>
        /// Where a saved movable grid belongs in THIS scene, and which way it faced. The body
        /// anchor wins for the same reason it wins for a placed block: it is the pose taken
        /// relative to the body the hull was standing on or orbiting, so it survives the body
        /// moving, the origin rebasing and a reference-frame switch. Without an anchor (legacy
        /// saves, or a hull drifting in deep space) the saved scene pose is used unchanged, so
        /// nothing this round changes how an older save loads.
        /// </summary>
        private static void ResolveSavedGridPose(SavedGrid g, out Vector3 position, out Quaternion rotation, out bool fromAnchor)
        {
            position = IsFiniteVector(g.pos) ? g.pos : Vector3.zero;
            rotation = IsFiniteQuaternion(g.rot) ? g.rot.normalized : Quaternion.identity;
            fromAnchor = false;

            if (!g.hasBodyAnchor || string.IsNullOrEmpty(g.anchorBody)) return;

            var body = FindSceneBodyByName(g.anchorBody);
            if (body == null)
            {
                Debug.LogWarning($"[WorldState] A movable grid was anchored to '{g.anchorBody}', which is not in this scene; " +
                                 "restoring it at its saved scene coordinate instead.");
                return;
            }

            Vector3 anchored = body.transform.TransformPoint(new Vector3(g.anchorLocalX, g.anchorLocalY, g.anchorLocalZ));
            Quaternion anchoredRot = body.transform.rotation
                * new Quaternion(g.anchorRotX, g.anchorRotY, g.anchorRotZ, g.anchorRotW);
            // The stored local rotation is normalized on its way back out, and a rotation too
            // short to be one is refused rather than trusted: an invalid quaternion handed to a
            // Rigidbody poisons every physics step that follows it.
            float rotLengthSqr = anchoredRot.x * anchoredRot.x + anchoredRot.y * anchoredRot.y
                               + anchoredRot.z * anchoredRot.z + anchoredRot.w * anchoredRot.w;
            if (IsFiniteVector(anchored) && IsFinite(rotLengthSqr) && rotLengthSqr > 0.0001f)
            {
                position = anchored;
                rotation = anchoredRot.normalized;
                fromAnchor = true;
                return;
            }

            Debug.LogWarning($"[WorldState] A movable grid's body anchor on '{g.anchorBody}' resolved to an invalid pose; " +
                             "restoring it at its saved scene coordinate instead.");
        }

        /// <summary>
        /// The body a scene object is standing on: the body whose surface is nearest to it,
        /// within half that body's radius so a base on the ground, a platform in the air and
        /// a ship on a pad all anchor while something parked in deep space does not.
        /// </summary>
        private static VoxelEngine.Cosmos.CelestialBody FindAnchoringBody(Vector3 scenePos)
        {
            if (!IsFiniteVector(scenePos)) return null;
            var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;
            VoxelEngine.Cosmos.CelestialBody best = null;
            float bestGap = float.MaxValue;
            if (registry != null && registry.SceneBodies != null)
            {
                foreach (var kv in registry.SceneBodies)
                {
                    if (kv.Key == null || kv.Key.settings == null || kv.Value == null) continue;
                    float gap = Mathf.Abs(Vector3.Distance(scenePos, kv.Value.transform.position) - kv.Value.SurfaceRadius);
                    if (gap < bestGap) { bestGap = gap; best = kv.Value; }
                }
            }
            if (best == null)
            {
                var active = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
                if (active != null && active.settings != null)
                {
                    best = active;
                    bestGap = Mathf.Abs(Vector3.Distance(scenePos, active.transform.position) - active.SurfaceRadius);
                }
            }
            if (best == null) return null;
            return bestGap <= best.SurfaceRadius * 0.5f ? best : null;
        }

        /// <summary>
        /// Record the grid's pose relative to the body it is standing on or orbiting, so a hull
        /// reloads at the place it was left rather than at a scene coordinate that only meant
        /// anything in the frame the save was written in. The body must also be the frame the
        /// scene is running in, or the local pose would be measured from a body that is not
        /// where the scene's coordinates are — in that case the grid saves unanchored and
        /// restores from its scene coordinate exactly as it did before this round.
        /// </summary>
        private static void CaptureGridBodyAnchor(GridEntity grid, SavedGrid entry)
        {
            if (grid == null || entry == null) return;

            if (!IsFiniteVector(entry.pos) || !IsFiniteQuaternion(entry.rot))
            {
                Debug.LogWarning("[WorldState] A movable grid's saved pose was not finite, so no body anchor was " +
                                 "written for it; the grid will restore from its saved scene coordinate instead.");
                return;
            }

            var origin = VoxelEngine.Cosmos.SpaceOrigin.Instance;
            var frameBody = origin != null ? origin.FrameBody : null;
            if (frameBody == null || frameBody.settings == null || string.IsNullOrEmpty(frameBody.settings.bodyName)) return;

            var anchor = FindAnchoringBody(entry.pos);
            if (anchor == null || anchor != frameBody || anchor.settings == null) return;

            Vector3 localPos = anchor.transform.InverseTransformPoint(entry.pos);
            if (!IsFiniteVector(localPos))
            {
                Debug.LogWarning($"[WorldState] A movable grid's body-local position on '{anchor.settings.bodyName}' " +
                                 "was not finite; the grid was saved at its scene coordinate instead.");
                return;
            }

            Quaternion localRot = Quaternion.Inverse(anchor.transform.rotation) * entry.rot;
            if (!IsFiniteQuaternion(localRot)) localRot = Quaternion.identity;
            localRot.Normalize();

            entry.hasBodyAnchor = true;
            entry.anchorBody    = anchor.settings.bodyName;
            entry.anchorLocalX  = localPos.x;
            entry.anchorLocalY  = localPos.y;
            entry.anchorLocalZ  = localPos.z;
            entry.anchorRotX    = localRot.x;
            entry.anchorRotY    = localRot.y;
            entry.anchorRotZ    = localRot.z;
            entry.anchorRotW    = localRot.w;
        }

        private void RestoreFactoryRuntime(GameObject go, SavedPlacedBlock saved)
        {
            if (saved.hasFluidTankState)
            {
                var liquidTank = go.GetComponentInChildren<VoxelEngine.Fluids.WaterTank>(true);
                if (liquidTank != null && System.Enum.IsDefined(typeof(VoxelEngine.Items.LiquidType), saved.fluidTankType))
                {
                    liquidTank.liquidType = (VoxelEngine.Items.LiquidType)saved.fluidTankType;
                    liquidTank.water = Mathf.Clamp(saved.fluidTankLitres, 0f, Mathf.Max(0f, liquidTank.capacityLitres));
                }
            }

            if (saved.hasFluidPumpState)
            {
                var liquidPump = go.GetComponentInChildren<VoxelEngine.Fluids.WaterPump>(true);
                if (liquidPump != null && System.Enum.IsDefined(typeof(VoxelEngine.Items.LiquidType), saved.fluidPumpType))
                {
                    liquidPump.liquidType = (VoxelEngine.Items.LiquidType)saved.fluidPumpType;
                    liquidPump.internalLitres = Mathf.Clamp(saved.fluidPumpLitres, 0f, Mathf.Max(0f, liquidPump.internalCapacityLitres));
                }
            }

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

            var armorUpgradeStation = go.GetComponentInChildren<VoxelEngine.Combat.ArmorUpgradeStation>(true);
            if (armorUpgradeStation != null && saved.armorUpgradeStationState != null)
            {
                armorUpgradeStation.RestoreProgress(
                    saved.armorUpgradeStationState.isUpgrading,
                    saved.armorUpgradeStationState.elapsedSeconds,
                    saved.armorUpgradeStationState.totalSeconds);
            }

            RestoreMaritimePorts(go, saved.maritimePorts);
            RestoreLightingRuntime(go, saved.lightingConfig);
            RestoreDefenseRuntime(go, saved.defenseState);

            // 9.57.0-dev: the machine's own process state goes last, so a resumed batch
            // reads the containers and tanks that were just refilled rather than the
            // empty ones the prefab spawned with. Machines are found by interface, so
            // this single hook covers the world machines and the grid machines alike,
            // and a save with no record leaves the machine exactly as it loads today.
            if (saved.machineProcess != null && !saved.machineProcess.IsEmpty)
            {
                var processMachine = go.GetComponentInChildren<VoxelEngine.Crafting.IMachineProcessState>(true);
                if (processMachine != null) processMachine.RestoreProcessState(saved.machineProcess);
            }
        }

        private static void RestoreDefenseRuntime(GameObject go, SavedDefenseState state)
        {
            if (state == null || go == null) return;

            var art = go.GetComponentInChildren<VoxelEngine.Combat.Artillery>(true);
            if (art != null)
            {
                art.filter = (VoxelEngine.Combat.TargetFilter)state.filter;
                art.autoMode = state.autoMode;
                ReadAmmoPolicy(state, art);
                ReadEngagement(state, art);
                return;
            }

            var flame = go.GetComponentInChildren<VoxelEngine.Combat.FlamethrowerTurret>(true);
            if (flame != null)
            {
                flame.filter = (VoxelEngine.Combat.TargetFilter)state.filter;
                flame.autoMode = state.autoMode;
                ReadAmmoPolicy(state, flame);
                ReadEngagement(state, flame);
                flame.RestoreFuelSeconds(state.fuelSeconds);
                return;
            }

            var mortar = go.GetComponentInChildren<VoxelEngine.Combat.MortarTurret>(true);
            if (mortar != null)
            {
                mortar.filter = (VoxelEngine.Combat.TargetFilter)state.filter;
                mortar.autoMode = state.autoMode;
                ReadAmmoPolicy(state, mortar);
                ReadEngagement(state, mortar);
                return;
            }

            var giant = go.GetComponentInChildren<VoxelEngine.Combat.GiantShellTurret>(true);
            if (giant != null)
            {
                giant.filter = (VoxelEngine.Combat.TargetFilter)state.filter;
                giant.autoMode = state.autoMode;
                ReadAmmoPolicy(state, giant);
                ReadEngagement(state, giant);
                return;
            }

            var aa = go.GetComponentInChildren<VoxelEngine.Combat.AntiAirTurret>(true);
            if (aa != null)
            {
                aa.filter = (VoxelEngine.Combat.TargetFilter)state.filter;
                aa.autoMode = state.autoMode;
                ReadAmmoPolicy(state, aa);
                ReadEngagement(state, aa);
                if (state.hasPreferAerial) aa.preferAerialOnly = state.preferAerial;
                return;
            }

            var energy = go.GetComponentInChildren<VoxelEngine.Combat.EnergyRelicTurret>(true);
            if (energy != null)
            {
                energy.filter = (VoxelEngine.Combat.TargetFilter)state.filter;
                energy.autoMode = state.autoMode;
                ReadAmmoPolicy(state, energy);
                ReadEngagement(state, energy);
                return;
            }

            var tur = go.GetComponentInChildren<VoxelEngine.Combat.Turret>(true);
            if (tur != null)
            {
                tur.filter = (VoxelEngine.Combat.TargetFilter)state.filter;
                tur.autoMode = state.autoMode;
                ReadAmmoPolicy(state, tur);
                ReadEngagement(state, tur);
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
                // Cracks from the saved HP (9.30.0).
                int maxHp = Mathf.Max(1, def.GetStats((BuildTier)ps.tier).hp);
                VoxelEngine.Thermal.BlockDamageVisual.ReportDamage(pb, 1f - Mathf.Clamp01(pb.hp / (float)maxHp));
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

            var gasTank = go.GetComponentInChildren<VoxelEngine.Gas.GasTank>();
            if (gasTank != null)
            {
                gasTank.EnsureContainers();
                DeserializeInto(gasTank.PortableSlot, sc);
                return;
            }

            var gridBattery = go.GetComponentInChildren<VoxelEngine.GridSystem.GridBattery>(true);
            if (gridBattery != null)
            {
                gridBattery.EnsureContainers();
                DeserializeInto(gridBattery.ChargeSlot, sc);
                return;
            }

            var powerBattery = go.GetComponentInChildren<VoxelEngine.Power.PowerBattery>();
            if (powerBattery != null)
            {
                powerBattery.EnsureContainers();
                DeserializeInto(powerBattery.ChargeSlot, sc);
                return;
            }

            var gridGas = go.GetComponentInChildren<VoxelEngine.GridSystem.GridGasTank>();
            if (gridGas != null)
            {
                gridGas.EnsureContainers();
                DeserializeInto(gridGas.PortableSlot, sc);
                return;
            }

            var airVent = go.GetComponentInChildren<VoxelEngine.Pressure.GridAirVent>();
            if (airVent != null && airVent.fullBlock)
            {
                airVent.EnsureContainers();
                DeserializeInto(airVent.SuitDock, sc);
                return;
            }

            var armorUpgradeStation = go.GetComponentInChildren<VoxelEngine.Combat.ArmorUpgradeStation>(true);
            if (armorUpgradeStation != null)
            {
                DeserializeMulti(sc,
                    armorUpgradeStation.ArmorSlot,
                    armorUpgradeStation.ModuleSlot,
                    armorUpgradeStation.OutputSlot);
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

            var jackPump = go.GetComponentInChildren<VoxelEngine.Crafting.Pumpjack>();
            if (jackPump != null)
            {
                // Tank-only machine (11.0.0-dev): nothing item-shaped to restore. Its
                // crude and part-batch come back through the machine-process record.
                // This block still has to terminate the lookup so the search does not
                // fall through to the generic container handlers below.
                jackPump.EnsureContainers();
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

            // Petroleum-era machines (9.57.0-dev). Same order as the capture branch.
            var distillation = go.GetComponentInChildren<VoxelEngine.Crafting.DistillationPlant>(true);
            if (distillation != null)
            {
                distillation.EnsureContainers();
                DeserializeMulti(sc, distillation.inputC, distillation.outputC);
                RestorePortSnapshot(go, sc);
                return;
            }

            var cracker = go.GetComponentInChildren<VoxelEngine.Crafting.CatalyticCracker>(true);
            if (cracker != null)
            {
                cracker.EnsureContainers();
                DeserializeMulti(sc, cracker.inputC, cracker.outputC);
                RestorePortSnapshot(go, sc);
                return;
            }

            var oilRefinery = go.GetComponentInChildren<VoxelEngine.Crafting.OilRefinery>(true);
            if (oilRefinery != null)
            {
                oilRefinery.EnsureContainers();
                DeserializeMulti(sc, oilRefinery.inputC, oilRefinery.outputC, oilRefinery.upgradeC);
                RestorePortSnapshot(go, sc);
                return;
            }

            var chemicalPlant = go.GetComponentInChildren<VoxelEngine.Industrial.StationaryChemicalPlant>(true);
            if (chemicalPlant != null)
            {
                chemicalPlant.EnsureContainers();
                DeserializeMulti(sc, chemicalPlant.inputC, chemicalPlant.outputC);
                RestorePortSnapshot(go, sc);
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
            {
                DeserializeInto(giant.ShellMagazine, sc);
                return;
            }

            var aa = go.GetComponentInChildren<VoxelEngine.Combat.AntiAirTurret>();
            if (aa != null)
            {
                DeserializeInto(aa.AmmoMagazine, sc);
                return;
            }

            var energy = go.GetComponentInChildren<VoxelEngine.Combat.EnergyRelicTurret>();
            if (energy != null)
                DeserializeInto(energy.CellMagazine, sc);
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
                drawer.upgradeSlots.SetSlot(i, new ItemStack { item = item, count = e.count, durability = e.durability, charge = e.charge });
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

            if (!_itemById.TryGetValue(e.itemId, out var item))
            {
                if (_missingSavedItemWarnings.Add(e.itemId))
                    Debug.LogWarning($"[WorldState] Saved item '{e.itemId}' was not present in the runtime item cache. Run the relevant Voxel Engine Setup step to repair the persistence catalog before saving again; this stack could not be restored this session.");
                return new ItemStack();
            }
            var stack = new ItemStack { item = item, count = e.count, durability = e.durability, charge = e.charge };
            if (e.hasLiquidPayload && System.Enum.IsDefined(typeof(VoxelEngine.Items.LiquidType), e.liquidPayloadType))
                stack.payload = (VoxelEngine.Items.LiquidType)e.liquidPayloadType;
            return stack;
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
            // 9.57.1-dev: the cosmic clock the save was written at. The solar system is
            // regenerated at t = 0 on every load, so without this every body sat at its
            // start-of-session phase while the player's saved coordinates described where
            // they were at the END of the session — the drift that turned a planet-side
            // logout into a spawn in open space. Legacy saves read 0 and stay at t = 0.
            public double cosmicSimulationSeconds;
            public List<SavedPlacedBlock>  placedBlocks  = new();
            public List<SavedPlacedTiered> placedTiered = new();
            public List<SavedQuarry>       quarries     = new();
            public List<SavedRefuelPad>      refuelPads   = new();   // 9.36.0-dev — the ground pads
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
            // 9.50.0-dev: active wheel runs reload parked, never restart unattended.
            public bool wheelParkingBrake;
            public float hydrogenStored;
            public float oxygenStored;
            // Additive 6.81.0: logical shaft-to-shaft belt links. Old saves omit
            // the collection and continue to restore with no belts.
            public List<SavedMechanicalBelt> mechanicalBelts = new();
            // Additive 9.27.0: oxygen charge of each sealed room, keyed by the room's
            // anchor cell. Old saves omit the collection and restore as vacuum, which
            // a running Air Vent refills — no save break.
            // Additive 9.32.0: the same records also carry trapped heat and foul gas.
            public List<SavedRoomCharge> roomCharges = new();
            // Additive 9.34.0: recorded routes, in cosmic km with an optional body anchor.
            public List<SavedRoute> routes = new();
            // Additive 9.35.0: armed auto-run loops. Saved on the grid because a loop is a promise the
            // *ship* made, not block state — the pad may be rebuilt out from under it and the schedule
            // should still reload, then fail honestly at its own reservation check rather than vanish.
            public List<SavedRouteLoop> loops = new();
            public List<SavedGridBlock> blocks = new();

            // ── Additive 10.1.0: the body anchor ────────────────────────────────
            // `pos` and `rot` above are scene coordinates: the celestial bodies move
            // through the scene as the system runs (orbits, origin rebases, reference-frame
            // switches), so a scene pose is only meaningful in the frame it was captured in.
            // These fields record the SAME pose relative to the body the grid was standing
            // on or orbiting, which is the pose that survives all three. The scene pose is
            // still written, and stays the restore path for a grid with no anchor and for
            // every save written before this round.
            public bool hasBodyAnchor;
            public string anchorBody;
            public float anchorLocalX; public float anchorLocalY; public float anchorLocalZ;
            /// <summary>Rotation in the anchor body's local space. Stored as the four
            /// quaternion components rather than Euler angles so the round-trip is exact
            /// and no gimbal case can flip a restored hull.</summary>
            public float anchorRotX; public float anchorRotY; public float anchorRotZ; public float anchorRotW = 1f;
        }
        [Serializable] private class SavedRoomCharge
        {
            public Vector3Int anchor;
            public float oxygenLitres;
            /// <summary>°C of waste heat the compartment has baked in, above the outside air.</summary>
            public float heatLoadC;
            /// <summary>°C of trapped exhaust stream held in the compartment air.</summary>
            public float exhaustLoadC;
        }
        [Serializable] private class SavedRoute
        {
            public string name;
            public int speedProfile;
            public int travelMode;
            public bool sceneCoordinates;
            public List<SavedWaypoint> waypoints = new();
        }

        [Serializable] private class SavedRouteLoop
        {
            public string routeName;
            public string startWaymark;
            public string endWaymark;
            public int mode;
            public int fixedRuns = 4;
            public int runsCompleted;
            public float targetCharge01 = 0.9f;
            public float targetFuel01 = 0.85f;
            public float targetHydrogen01 = 0.85f;
            public float minimumReserve01 = 0.25f;
            public float haltOnWorstBlockHurt01 = 0.25f;
            public bool stopWhenCargoFull = true;
            public bool stopWhenCargoEmpty = false;
        }

        [Serializable] private class SavedWaypoint
        {
            // Cosmic positions are kilometres at solar-system scale: a float is a metre of error
            // out here, so the record keeps the double and the waypoint model never converts.
            public double xKm;
            public double yKm;
            public double zKm;
            public string bodyId;
            public string label;
            // Additive 9.35.0: the name of the waymark this point stands for, when it stands for one.
            // The position is still written as well, so a waymark whose pad was destroyed reloads as
            // the point it was last seen at rather than vanishing from somebody else's route.
            public string waymarkName;
            // Additive 9.34.0: a pinned point is stored as an offset from the body it rides, so the
            // record survives the body moving. A legacy record has no offset and loads as absolute.
            public double offXKm;
            public double offYKm;
            public double offZKm;
        }

        [Serializable] private class SavedMechanicalBelt
        {
            public Vector3Int endpointA;
            public Vector3Int endpointB;
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
            // Cosmetic paint finish id (byte). 0 = none / legacy unpainted.
            public int paintFinish;
            public bool enabled = true;
            public bool hasShapeVariant;
            public int shapeVariant;
            public string customName;
            public bool cryobedClaimed;
            public float cryobedOxygen;
            // Additive grid-tank state. Legacy saves leave these flags false and
            // preserve prefab defaults; current saves retain type, amount, and mode.
            // Additive gas-vent state (9.32.0). Legacy saves leave the flag false and the
            // vent restores from its prefab default: louvres open, counter at zero.
            // Additive engine combustion-air policy (9.32.0). A legacy save has no flag and
            // the engine keeps its forgiving default: fall back to free air on a dry line.
            public bool hasEngineAirModeState;
            public bool engineAirFallback = true;
            public bool hasGasVentState;
            public bool gasVentOpen = true;
            public float gasVentDumped;
            public bool hasFlareStackState;
            public bool flareStackOpen = true;
            public bool flareStackRecovery;
            public float flareStackGasDumped;
            public float flareStackLiquidDumped;
            // Additive ventilation tuning (9.33.0). A legacy save has no flag, and every unit
            // keeps its prefab default, which is what it behaved as before the option existed.
            public bool hasVentilationScaleState;
            public bool ventilationAutoScale = true;
            public float ventilationSupplyFlow;
            public bool hasGasTankState;
            public int gasTankType;
            public float gasTankStored;
            public int gasTankMode;
            public bool hasLiquidTankState;
            public int liquidTankType;
            public float liquidTankStored;
            public int liquidTankMode;
            // Additive grid-battery state. Stored charge is required for autonomous
            // dampeners to hold a restored ship still before a generator spins up.
            // Additive heat-shield ablator charge. Legacy saves leave the flag false and
            // keep the prefab's full charge, so old ships are never restored pre-burnt.
            public bool hasHeatshieldState;
            public float heatshieldAblator;
            public bool hasGridBatteryState;
            public float gridBatteryStoredWh;
            public int gridBatteryMode;
            // Additive gearbox setting state. The exact free-form ratio is the
            // authoritative player choice; selectedGear preserves legacy UI slots.
            public bool hasGearboxState;
            public float gearboxRatio;
            public int gearboxSelectedGear;
            public SavedContainer container;
            public SavedPlacedBlock runtime;
        }
        [Serializable] private class SavedPlayer
        {
            public Vector3 pos; public float rotY;
            // Real-space (7.13.0): cosmic position + reference frame so logging out in
            // deep space / high orbit restores exactly where the player was. Legacy saves
            // omit these (0 + null) and restore through the old scene-anchored path.
            public double cosmicPosX; public double cosmicPosY; public double cosmicPosZ;
            public string frameBody;
            // Body-anchored position (9.57.1-dev). `pos` above is a SCENE coordinate, and the
            // scene origin is re-anchored as the world runs — so it is only valid in the
            // frame it was captured in. This is the same position stored relative to the
            // frame body's own transform, which is what actually survives a reload (the same
            // construction the world spawn has used since 9.2.0). Legacy saves omit it:
            // hasAnchor stays false and the loader falls back to the scene coordinate, then
            // to the bed/world spawn.
            public bool hasAnchor;
            public string anchorBody;
            public float anchorLocalX; public float anchorLocalY; public float anchorLocalZ;
            public SavedContainer container;
            // Additive in 6.22.1: two dedicated jetpack equipment slots.
            // Legacy saves leave this null and restore with empty slots.
            public SavedContainer jetpackSlots;
            // Additive in 6.23.1: sealed helmet + oxygen tank equipment slots.
            public SavedContainer helmetSlots;
            public SavedContainer oxygenTankSlots;
            // Additive armor equipment slot. Legacy saves omit this and keep an
            // empty armor slot; upgraded armor retains its packed durability state.
            public SavedContainer armorSlots;
            public int activeHotbarIndex;
        }
        [Serializable] private class SavedPlacedBlock
        {
            public string itemId;
            public Vector3 pos; public Quaternion rot; public float rotY;
            // Additive body anchor (9.58.2-dev). A placed block stands on a celestial body,
            // and that body moves through the scene as the system runs (orbits, rebases,
            // frame switches), so the scene coordinate above is only meaningful in the frame
            // it was captured in. The anchor is the frame-independent position the loader
            // prefers; `pos` stays as the fallback and as the diagnostic. Legacy saves have
            // hasBodyAnchor false and restore exactly as they did.
            public bool hasBodyAnchor;
            public string anchorBody;
            public float anchorLocalX; public float anchorLocalY; public float anchorLocalZ;
            public int hp;
            public SavedContainer container;
            public string customName;
            public bool cryobedClaimed;
            // Cosmetic paint finish id (byte). 0 = none / legacy unpainted.
            public int paintFinish;
            // World GasTank bulk contents (additive).
            public int gasType;
            public float gasStoredAmount;
            public int gasSelectedType;
            // World PowerBattery bulk charge (additive). Legacy saves leave
            // hasBatteryCharge false and the block keeps its prefab charge.
            public bool hasBatteryCharge;
            public float batteryCharge;
            // Additive native liquid persistence. Legacy saves leave the flags false and
            // keep prefab defaults, while new saves retain tank contents and pump buffers.
            public bool hasFluidTankState;
            public int fluidTankType;
            public float fluidTankLitres;
            public bool hasFluidPumpState;
            public int fluidPumpType;
            public float fluidPumpLitres;
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
            // Additive 9.57.0-dev: the live process + fluid state of a processing machine
            // (batch in progress, locked recipe, every tank it owns, machine-specific
            // numbers). Null for every other block, and null in every save written before
            // this round — a machine with no record restores empty and idle, exactly as it
            // did before this field existed, which is why no save schema bump is needed.
            public VoxelEngine.Crafting.MachineProcessState machineProcess;
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
            // Armor Upgrade Station process state. Inputs are stored in `container`; this
            // additive record only resumes elapsed time after those inputs restore.
            public SavedArmorUpgradeStationState armorUpgradeStationState;
            // Asphalt road wear (9.41.0). Written per block even though wear is a per-RUN number,
            // because run identity is session-scoped and a strip can merge or split between the
            // save and the load; on restore the run takes the worst value any of its cells reported.
            // Additive — legacy saves leave hasRoadWear false and the road restores brand new.
            public bool hasRoadWear;
            public float roadWear;
            // 9.48.0-dev: per-cell labels preserve names across load order, splits and merges.
            public string roadNetworkName;
            // Water-crossing structure (9.44.1-dev). Additive: legacy saves leave hasBridgeSpan
            // false and a restored deck cell classifies itself from the ground beneath it, exactly
            // as a freshly paved one does. Only the structure KIND and how far open it was need
            // storing — the deck level is already baked into `pos`, and the piers are rebuilt from
            // the clearance, so neither is state.
            public bool hasBridgeSpan;
            public int bridgeStructure;
            public float bridgeOpen;
            // 9.49.0-dev: absent manual flag means automatic for legacy saves.
            // Optional command preserves swing direction and manual-open ownership on reload.
            public bool bridgeManualControl;
            public bool hasBridgeCommand;
            public bool bridgeWantsOpen;
            public bool bridgeAutoOwned;
        }
        [Serializable] private class SavedArmorUpgradeStationState
        {
            public bool isUpgrading;
            public float elapsedSeconds;
            public float totalSeconds;
        }
        [Serializable] private class SavedDefenseState
        {
            public int filter;          // TargetFilter flags
            public bool autoMode = true;
            public int ammo;            // Auto Turret magazine count
            public float fuelSeconds;   // Flamethrower continuous fuel buffer
            public bool preferAerial = true; // Anti-Air aerial-only mode (additive)
            public bool hasPreferAerial;     // legacy saves leave this false
            public bool conserveAmmo;        // additive ammo policy
            public int reserveStock;         // additive reserve units
            public bool hasAmmoPolicy;       // legacy saves leave false
            public float engagementRange;    // additive engagement range
            public float firingArcDegrees;   // additive firing arc
            public bool hasEngagement;       // legacy saves leave false
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
            // Secondary per-instance pool (additive, save-compatible — legacy saves
            // deserialize as 0). Hybrid jetpacks store their power cell (Wh) here.
            public int charge;
            // Additive bucket/liquid payload. A filled bucket must remember whether it
            // carries water or crude oil across save/load.
            public bool hasLiquidPayload;
            public int liquidPayloadType;
            public bool isPackedDrawer;
            public string packedOriginalItemId;
            public string drawerInstanceId;
            public string drawerStoredItemId;
            public int drawerStoredCount;
            public List<SavedStack> drawerUpgrades = new();
        }
        // ── Static refuel pads (9.36.0-dev) ───────────────────────────────────
        // The pad has no block identity of its own in the save format — it is a world block, placed and
        // destroyed by the block system, and it lives at a position. So its own state (what the player
        // named it, what it draws, whether it is switched on) is stored by position and re-bound on load
        // by proximity, exactly the way a quarry's depth is. A pad that was destroyed therefore loses its
        // name and does not resurrect a ghost record, which is the same rule the waymark model runs on.
        private void SaveRefuelPads(SaveData save)
        {
            var pads = FindObjectsByType<VoxelEngine.Navigation.StaticRefuelPad>(FindObjectsInactive.Exclude);
            foreach (var pad in pads)
            {
                if (pad == null) continue;
                save.refuelPads.Add(new SavedRefuelPad
                {
                    pos = pad.transform.position,
                    rot = pad.transform.rotation,
                    rotY = pad.transform.eulerAngles.y,
                    waymarkName = pad.SavedName,
                    enabled = pad.enabled,
                    powerWatts = pad.powerWatts,
                    litresPerSecond = pad.litresPerSecond,
                    itemSlotsPerSecond = pad.itemSlotsPerSecond,
                    // Key names kept from the first cut of the pad (additive-key rule): what they hold now
                    // is the pad's own tank, which the base fills through the pipe graph.
                    drumLitres = pad.TankLitres,
                    drumType = (int)pad.TankType,
                    // The drum's contents and its six port faces ride this entry, the way a quarry's output
                    // rides `SavedQuarry.outputContainer`. The pad's two fluid/gas nodes do NOT need to be
                    // listed here at all: they are a world `WaterTank` and a world `GasTank` on a placed
                    // block, and `SavedPlacedBlock` already persists both — one owner per number.
                    drumContainer = CapturePadDrum(pad)
                });
            }
        }

        private void RestoreRefuelPads(SaveData save)
        {
            if (save.refuelPads == null || save.refuelPads.Count == 0) return;
            var pads = FindObjectsByType<VoxelEngine.Navigation.StaticRefuelPad>(FindObjectsInactive.Exclude);
            foreach (var sp in save.refuelPads)
            {
                VoxelEngine.Navigation.StaticRefuelPad best = null;
                float bestDist = 2f;                          // same tolerance the quarry restore uses
                foreach (var pad in pads)
                {
                    if (pad == null) continue;
                    float d = Vector3.Distance(pad.transform.position, sp.pos);
                    if (d < bestDist) { bestDist = d; best = pad; }
                }
                if (best == null) continue;                   // pad gone: its waymark simply is not any more
                best.enabled = sp.enabled;
                best.powerWatts = sp.powerWatts;
                best.litresPerSecond = sp.litresPerSecond;
                best.itemSlotsPerSecond = sp.itemSlotsPerSecond;
                best.RestoreFromSave(sp.waymarkName);
                best.RestoreTank(sp.drumLitres, (VoxelEngine.Items.LiquidType)sp.drumType);
                best.EnsureBuffersPublic();
                if (sp.drumContainer != null)
                {
                    DeserializeInto(best.Drum, sp.drumContainer);
                    RestorePortSnapshot(best.gameObject, sp.drumContainer);
                }
            }
        }

        [Serializable] private class SavedQuarry
        {
            public Vector3 pos; public Quaternion rot; public float rotY;
            public int currentDepth; public int cursorX; public int cursorZ;
            public int phase; public int rangeLvl; public int speedLvl; public int effLvl; // upgrade levels
            public SavedContainer outputContainer;
        }

        [Serializable] private class SavedRefuelPad
        {
            public Vector3 pos; public Quaternion rot; public float rotY;
            public string waymarkName = "";
            public bool enabled = true;
            public float powerWatts;
            public float litresPerSecond;
            public int itemSlotsPerSecond;
            public float drumLitres;
            public int drumType;
            public SavedContainer drumContainer;
        }

        /// <summary>Drum items plus the pad's port-face snapshot, in the one shape the container ladder
        /// already understands.</summary>
        private SavedContainer CapturePadDrum(VoxelEngine.Navigation.StaticRefuelPad pad)
        {
            if (pad == null) return null;
            pad.EnsureBuffersPublic();
            var sc = SerializeContainer(pad.Drum);
            if (sc != null) AttachPortSnapshot(pad.gameObject, sc);
            return sc;
        }
    }
}
