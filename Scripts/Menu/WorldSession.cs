// Assets/Scripts/VoxelEngine/Menu/WorldSession.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Menu
{
    /// <summary>
    /// Persists across scene loads. Carries the selected world name + seed +
    /// other newly-created world settings from the main menu into the game scene.
    /// </summary>
    public class WorldSession : MonoBehaviour
    {
        public static WorldSession Instance { get; private set; }

        // The world the player is about to enter / is currently in.
        public string worldName = "DefaultWorld";
        public int    seed      = 1337;

        public const int DefaultMaxDroppedItems = 1000;
        public const float DefaultPlayerInventoryWeightKg = 450f;
        public const float DefaultContainerWeightKg = 5000f;
        public const int DefaultInventoryWeightPercent = 100;
        public const int DefaultContainerWeightPercent = 100;
        public const bool DefaultAllowRuinLootRespawn = true;
        public const float DefaultFullVoxelRadiusKm = 50f;
        public const bool DefaultFriendlyFire = false;

        /// <summary>Maximum simultaneous physical world drops. Conveyor packets use
        /// their own simulation and are deliberately never included in this limit.</summary>
        public int maxDroppedItems = DefaultMaxDroppedItems;

        /// <summary>
        /// Radius (km) around the player that renders as REAL voxel surface (not sampled
        /// LOD / coarse MID blocks) on the planet or moon they are near. 50 km covers an
        /// entire 8–16 km planet. 0 = legacy ring-only coverage. Persisted per-world in
        /// the world-settings sidecar; read by CosmosBootstrap at world load.
        /// </summary>
        public float fullVoxelRadiusKm = DefaultFullVoxelRadiusKm;

        public int inventoryWeightPercent = DefaultInventoryWeightPercent;
        public int containerWeightPercent = DefaultContainerWeightPercent;
        public bool showDropVoidWarning = true;
        public bool allowRuinLootRespawn = DefaultAllowRuinLootRespawn;

        /// <summary>World rule (14.34.0): may teammates damage each other?
        /// A SERVER/WORLD setting, never a team setting - fair ground for
        /// every player. The host enforces it on every hit intent.</summary>
        public bool friendlyFire = DefaultFriendlyFire;

        /// <summary>World rule (14.37.0): may banner editors use the in-game
        /// painting board? A WORLD setting so a host can keep a public server
        /// to curated gallery images. Default ON; gallery, texts and the
        /// default emblem are always available either way.</summary>
        public bool allowBannerPainting = true;

        /// <summary>World rule (14.60.3): may players teleport to their
        /// teammates from the Teams tab? A WORLD setting the host flips in
        /// Server Administration. The server owner's admin-tab teleport
        /// ignores this switch. Default ON.</summary>
        public bool allowTeammateTeleport = true;
        public float PlayerInventoryWeightLimitKg => DefaultPlayerInventoryWeightKg * Mathf.Clamp(inventoryWeightPercent, 25, 1000) / 100f;
        public float ContainerWeightLimitKg => DefaultContainerWeightKg * Mathf.Clamp(containerWeightPercent, 25, 1000) / 100f;

        // Spawn data. The world spawn is computed once on first load; bed spawn is per-bed.
        public Vector3 worldSpawnPoint = new Vector3(0, 200, 0);
        public bool    worldSpawnInitialized = false;
        public Vector3 bedSpawnPoint = Vector3.zero;
        public bool    hasBedSpawn = false;
        // 14.60.4 - the bed's frame-independent truth. The scene Vector3 above is
        // only valid in the reference frame where the bed was linked; a bed on
        // another planet respawned players into empty space. Doubles, in km.
        // When bedSpawnBodyName is set, X/Y/Z are a BODY-RELATIVE offset (km) from
        // that body's live centre; when empty they are absolute cosmic km (a bed in
        // deep space). Body-relative survives orbital motion and clock drift.
        public double  bedSpawnCosmicX, bedSpawnCosmicY, bedSpawnCosmicZ;
        public bool    bedSpawnHasCosmic = false;
        public string  bedSpawnBodyName = "";

        // ── Body-anchored world spawn (9.2.0) ─────────────────────────────
        // Scene positions go stale the moment the floating origin re-anchors (orbital
        // motion, visiting another planet). The world spawn is therefore ALSO stored as
        // body name + body-local offset, and respawn reconstructs the live scene position
        // from the body's CURRENT transform — never a point in empty space.
        public string  worldSpawnBodyName = "";
        public Vector3 worldSpawnLocalPos = Vector3.zero;

        /// <summary>Record the world spawn as scene position + body anchor in one call.
        /// Falls back to the nearest scene body when no active body is supplied yet.</summary>
        public void RecordWorldSpawn(Vector3 scenePos, VoxelEngine.Cosmos.CelestialBody body)
        {
            worldSpawnPoint = scenePos;
            worldSpawnInitialized = true;

            if (body == null)
            {
                // First spawn can run before the gravity frame is assigned — anchor to
                // the nearest body whose surface the point is actually near.
                var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;
                if (registry != null && registry.SceneBodies != null)
                {
                    float best = float.MaxValue;
                    foreach (var kv in registry.SceneBodies)
                    {
                        var candidate = kv.Value;
                        if (candidate == null || candidate.settings == null) continue;
                        float altitude = Vector3.Distance(scenePos, candidate.transform.position)
                                         - candidate.SurfaceRadius;
                        if (altitude < best && altitude < 2000f) { best = altitude; body = candidate; }
                    }
                }
            }

            if (body != null && body.settings != null)
            {
                worldSpawnBodyName = body.settings.bodyName;
                worldSpawnLocalPos = body.transform.InverseTransformPoint(scenePos);
            }
        }

        /// <summary>
        /// Resolve the CURRENT scene position of the world spawn. Prefers the body anchor
        /// (immune to floating-origin drift); falls back to the legacy scene point.
        /// </summary>
        public bool TryResolveWorldSpawn(out Vector3 scenePos)
        {
            if (!string.IsNullOrEmpty(worldSpawnBodyName))
            {
                var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;
                if (registry != null && registry.SceneBodies != null)
                {
                    foreach (var kv in registry.SceneBodies)
                    {
                        if (kv.Key == null || kv.Key.settings == null || kv.Value == null) continue;
                        if (!string.Equals(kv.Key.settings.bodyName, worldSpawnBodyName,
                                           System.StringComparison.OrdinalIgnoreCase)) continue;
                        scenePos = kv.Value.transform.TransformPoint(worldSpawnLocalPos);
                        return true;
                    }
                }
            }
            scenePos = worldSpawnPoint;
            return worldSpawnInitialized || worldSpawnPoint.sqrMagnitude > 0.1f;
        }

        public Vector3 GetActiveSpawn()
        {
            if (hasBedSpawn) return bedSpawnPoint;
            TryResolveWorldSpawn(out Vector3 resolved);
            return resolved;
        }
        public bool   isNewWorld = false;

        // (Legacy flat-world override fields removed — the sphere uses BodySettings.)

        // ── Cosmos (per-planet seeds + chosen solar system) ────────
        /// <summary>Name of the solar-system template the player selected at world creation.</summary>
        public string chosenSystemName = "";
        /// <summary>Per-planet seed table (one editable, randomized-by-default seed per planet).</summary>
        public SystemSeedState seedState;

        /// <summary>Orbit pace modes: realistic Keplerian periods vs fast arcade sweep.</summary>
        public const int OrbitPaceRealistic = 0;
        public const int OrbitPaceArcade = 1;

        /// <summary>
        /// This world's orbit pace, chosen at creation and stored in the cosmos
        /// sidecar. Default realistic: old saves without the key keep real periods.
        /// </summary>
        public int orbitPace = OrbitPaceRealistic;

        /// <summary>Index of the planet to spawn on (0 = first planet in the system).</summary>
        public int spawnPlanetIndex = 0;

        public const int AutosaveSlotCount = 3;

        // ── Remote join (14.23.0, milestone 8) ────────────────────
        //
        // Joining used to be an in-world action, which is why both players had
        // to already hold the same save. A join started from the main menu
        // instead carries an address through the scene load; the world itself
        // arrives from the host in the handshake and is adopted below.

        /// <summary>Host address a main-menu join is heading for ("" = none).</summary>
        [System.NonSerialized] public string pendingJoinAddress = "";

        /// <summary>True while this session is somebody else's world.</summary>
        public bool IsRemoteJoin => !string.IsNullOrEmpty(pendingJoinAddress);

        /// <summary>True once the host's world card has been applied.</summary>
        [System.NonSerialized] public bool hostWorldAdopted;

        /// <summary>The host's name for this world, for UI only. The local
        /// worldName is a throwaway cache folder on a remote join.</summary>
        [System.NonSerialized] public string hostWorldDisplayName = "";

        /// <summary>Begin a main-menu join. The scene is loaded immediately
        /// afterwards with world generation held.</summary>
        public void BeginRemoteJoin(string address)
        {
            pendingJoinAddress = string.IsNullOrWhiteSpace(address) ? "localhost" : address.Trim();
            hostWorldAdopted = false;
            hostWorldDisplayName = "";
            isNewWorld = false;
            WorldBootGate.Hold($"Connecting to {pendingJoinAddress}...");
        }

        /// <summary>Back to single player. Called when the join is abandoned
        /// or the session ends, so a later solo load is never treated as one.</summary>
        public void ClearRemoteJoin()
        {
            pendingJoinAddress = "";
            hostWorldAdopted = false;
            hostWorldDisplayName = "";
            WorldBootGate.Reset();
        }

        /// <summary>Everything a joining client needs to build the same world:
        /// the seed, the per-planet seed table, the chosen system, and the
        /// world rules. Sent by the host in the handshake. Serialized as JSON
        /// so adding a field later cannot break an older client's parse -
        /// missing fields simply keep their defaults.</summary>
        [System.Serializable]
        public class WorldCard
        {
            public string worldName;
            public int seed;
            public string cosmosJson;          // the cosmos sidecar, verbatim
            public int maxDroppedItems;
            public int inventoryWeightPercent;
            public int containerWeightPercent;
            public bool showDropVoidWarning;
            public bool allowRuinLootRespawn;
            public float fullVoxelRadiusKm;
            public bool friendlyFire;
            // Class initializer = the answer a legacy host's card gives.
            public bool allowBannerPainting = true;
            public bool allowTeammateTeleport = true;
        }

        /// <summary>Host side: describe this world for a joining client.</summary>
        public string ExportWorldCardJson()
        {
            var card = new WorldCard
            {
                worldName = worldName,
                seed = seed,
                cosmosJson = ExportCosmosJson(),
                maxDroppedItems = maxDroppedItems,
                inventoryWeightPercent = inventoryWeightPercent,
                containerWeightPercent = containerWeightPercent,
                showDropVoidWarning = showDropVoidWarning,
                allowRuinLootRespawn = allowRuinLootRespawn,
                fullVoxelRadiusKm = fullVoxelRadiusKm,
                friendlyFire = friendlyFire,
                allowBannerPainting = allowBannerPainting,
                allowTeammateTeleport = allowTeammateTeleport,
            };
            try { return JsonUtility.ToJson(card); }
            catch (Exception ex) { Debug.LogWarning("[WorldSession] ExportWorldCardJson: " + ex.Message); return ""; }
        }

        /// <summary>Client side: become the host's world. The local worldName
        /// is deliberately NOT the host's - a joined world is a cache, not a
        /// save, so it lives in its own folder that is wiped on every join and
        /// can never be mistaken for one of the player's own saves.</summary>
        public bool AdoptWorldCardJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            WorldCard card;
            try { card = JsonUtility.FromJson<WorldCard>(json); }
            catch (Exception ex) { Debug.LogWarning("[WorldSession] AdoptWorldCardJson: " + ex.Message); return false; }
            if (card == null) return false;

            hostWorldDisplayName = string.IsNullOrEmpty(card.worldName) ? "Host world" : card.worldName;
            seed = card.seed;
            maxDroppedItems = Mathf.Clamp(card.maxDroppedItems <= 0 ? DefaultMaxDroppedItems : card.maxDroppedItems, 1, 10000);
            inventoryWeightPercent = Mathf.Clamp(card.inventoryWeightPercent <= 0 ? DefaultInventoryWeightPercent : card.inventoryWeightPercent, 25, 1000);
            containerWeightPercent = Mathf.Clamp(card.containerWeightPercent <= 0 ? DefaultContainerWeightPercent : card.containerWeightPercent, 25, 1000);
            showDropVoidWarning = card.showDropVoidWarning;
            allowRuinLootRespawn = card.allowRuinLootRespawn;
            friendlyFire = card.friendlyFire;
            allowBannerPainting = card.allowBannerPainting;
            allowTeammateTeleport = card.allowTeammateTeleport;
            if (card.fullVoxelRadiusKm > 0f) fullVoxelRadiusKm = card.fullVoxelRadiusKm;

            worldName = JoinedCacheFolderName(hostWorldDisplayName);
            WipeJoinedCache();
            ImportCosmosJson(card.cosmosJson);

            hostWorldAdopted = true;
            Debug.Log($"[WorldSession] Adopted host world '{hostWorldDisplayName}' (seed {seed}) " +
                      $"into session cache '{worldName}'.");
            return true;
        }

        /// <summary>Cache folder for a joined world. The leading marker keeps
        /// it out of the saves list and says what it is at a glance on disk.</summary>
        public static string JoinedCacheFolderName(string hostWorld) =>
            "__joined_" + SanitizeWorldFolderName(hostWorld);

        /// <summary>A joined world is rebuilt from the host every time, so the
        /// cache starts empty - stale chunks from a previous visit would show
        /// terrain the host has since changed.</summary>
        private void WipeJoinedCache()
        {
            try
            {
                string folder = WorldFolderPath(worldName);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex) { Debug.LogWarning("[WorldSession] WipeJoinedCache: " + ex.Message); }
        }

        /// <summary>True for a folder this class created as a join cache, so
        /// the saves list can skip it.</summary>
        public static bool IsJoinedCacheFolder(string folderName) =>
            !string.IsNullOrEmpty(folderName) && folderName.StartsWith("__joined_", StringComparison.Ordinal);

        /// <summary>Delete every join cache left behind by previous sessions.
        /// Called from the main menu, where nothing is streaming and no writer
        /// thread is alive, so it cannot race a save. Keeps visited worlds from
        /// quietly piling up gigabytes the player never chose to keep.</summary>
        public void PurgeJoinedCaches()
        {
            try
            {
                if (!Directory.Exists(WorldsRoot)) return;
                foreach (var dir in Directory.GetDirectories(WorldsRoot))
                {
                    if (!IsJoinedCacheFolder(new DirectoryInfo(dir).Name)) continue;
                    try { Directory.Delete(dir, true); }
                    catch (Exception ex) { Debug.LogWarning("[WorldSession] PurgeJoinedCaches: " + ex.Message); }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[WorldSession] PurgeJoinedCaches: " + ex.Message); }
        }

        public string CosmosSidecarPath =>
            Path.Combine(WorldFolderPath(worldName), "cosmos.json");
        public string WorldSettingsPath => WorldSettingsPathFor(worldName);

        public string WorldsRoot =>
            Path.Combine(Application.persistentDataPath, "VoxelWorlds");

        public string WorldFolderPath(string name) =>
            Path.Combine(WorldsRoot, SanitizeWorldFolderName(name));

        public string WorldSettingsPathFor(string name) =>
            Path.Combine(WorldFolderPath(name), "world_settings.json");

        public string WorldStatePathFor(string name) =>
            Path.Combine(WorldFolderPath(name), "world_state.json");

        public string AutosaveSlotPath(string name, int slotIndex) =>
            Path.Combine(WorldFolderPath(name), $"world_state.autosave{Mathf.Clamp(slotIndex, 1, AutosaveSlotCount)}.json");

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        
        public string SpawnSidecarPath
        {
            get
            {
                string folder = WorldFolderPath(worldName);
                System.IO.Directory.CreateDirectory(folder);
                return System.IO.Path.Combine(folder, "spawn.json");
            }
        }

        public void SaveSpawnSidecar()
        {
            try
            {
                var data = new SpawnData
                {
                    worldSpawn = worldSpawnPoint, worldInit = worldSpawnInitialized,
                    bedSpawn   = bedSpawnPoint,   hasBed    = hasBedSpawn,
                    spawnBody  = worldSpawnBodyName, spawnLocal = worldSpawnLocalPos,
                    bedCosmicX = bedSpawnCosmicX, bedCosmicY = bedSpawnCosmicY,
                    bedCosmicZ = bedSpawnCosmicZ, bedHasCosmic = bedSpawnHasCosmic,
                    bedBodyName = bedSpawnBodyName ?? ""
                };
                System.IO.File.WriteAllText(SpawnSidecarPath, UnityEngine.JsonUtility.ToJson(data, true));
            }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WorldSession] SaveSpawnSidecar: " + ex.Message); }
        }

        public void LoadSpawnSidecar()
        {
            try
            {
                if (!System.IO.File.Exists(SpawnSidecarPath)) return;
                var data = UnityEngine.JsonUtility.FromJson<SpawnData>(System.IO.File.ReadAllText(SpawnSidecarPath));
                if (data == null) return;
                worldSpawnPoint       = data.worldSpawn;
                worldSpawnInitialized = data.worldInit;
                bedSpawnPoint         = data.bedSpawn;
                hasBedSpawn           = data.hasBed;
                bedSpawnCosmicX       = data.bedCosmicX;
                bedSpawnCosmicY       = data.bedCosmicY;
                bedSpawnCosmicZ       = data.bedCosmicZ;
                bedSpawnHasCosmic     = data.bedHasCosmic;
                bedSpawnBodyName      = data.bedBodyName ?? "";
                worldSpawnBodyName    = data.spawnBody ?? "";
                worldSpawnLocalPos    = data.spawnLocal;
            }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WorldSession] LoadSpawnSidecar: " + ex.Message); }
        }

        [System.Serializable]
        private class SpawnData
        {
            public Vector3 worldSpawn;
            public bool    worldInit;
            public Vector3 bedSpawn;
            public bool    hasBed;
            public double  bedCosmicX, bedCosmicY, bedCosmicZ;
            public bool    bedHasCosmic;
            public string  bedBodyName;
            public string  spawnBody;
            public Vector3 spawnLocal;
        }

        public List<WorldSummary> ListWorlds()
        {
            var result = new List<WorldSummary>();
            if (!Directory.Exists(WorldsRoot)) return result;
            foreach (var dir in Directory.GetDirectories(WorldsRoot))
            {
                var info = new DirectoryInfo(dir);

                // A joined world is a session cache, not a save - it is wiped
                // on every join and belongs to the host. Never list it.
                if (IsJoinedCacheFolder(info.Name)) continue;

                long size = 0;
                foreach (var f in info.GetFiles("*.dat", SearchOption.TopDirectoryOnly))
                    size += f.Length;
                int? savedSeed = TryReadSeed(dir);
                int savedMaxDrops = DefaultMaxDroppedItems;
                int savedInventoryWeightPercent = DefaultInventoryWeightPercent;
                int savedContainerWeightPercent = DefaultContainerWeightPercent;
                bool savedShowDropVoidWarning = true;
                bool savedAllowRuinLootRespawn = DefaultAllowRuinLootRespawn;
                bool savedFriendlyFire = DefaultFriendlyFire;
                bool savedAllowBannerPainting = true;
                if (TryReadWorldSettings(info.Name, out var maxDrops, out var invWeightPct, out var containerWeightPct, out var showDropVoidWarning, out var allowRuinLootRespawn, out var friendlyFireSetting, out var bannerPaintingSetting))
                {
                    savedMaxDrops = maxDrops;
                    savedInventoryWeightPercent = invWeightPct;
                    savedContainerWeightPercent = containerWeightPct;
                    savedShowDropVoidWarning = showDropVoidWarning;
                    savedAllowRuinLootRespawn = allowRuinLootRespawn;
                    savedFriendlyFire = friendlyFireSetting;
                    savedAllowBannerPainting = bannerPaintingSetting;
                }
                result.Add(new WorldSummary
                {
                    name        = info.Name,
                    folderPath  = dir,
                    sizeBytes   = size,
                    lastWrite   = info.LastWriteTime,
                    savedSeed   = savedSeed,
                    maxDroppedItems = savedMaxDrops,
                    inventoryWeightPercent = savedInventoryWeightPercent,
                    containerWeightPercent = savedContainerWeightPercent,
                    showDropVoidWarning = savedShowDropVoidWarning,
                    allowRuinLootRespawn = savedAllowRuinLootRespawn,
                    friendlyFire = savedFriendlyFire,
                    allowBannerPainting = savedAllowBannerPainting
                });
            }
            result.Sort((a, b) => b.lastWrite.CompareTo(a.lastWrite));
            return result;
        }

        // We persist a tiny JSON sidecar per world so the menu can show its seed
        // and so loading a world restores the same seed it was generated with.
        public void WriteSeedSidecar()
        {
            string folder = WorldFolderPath(worldName);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "world.json");
            File.WriteAllText(path,
                $"{{\"seed\":{seed}}}");
        }

        public bool TryReadSidecar(out int seedOut, out int seaLevelOut, out int baseHeightOut, out float continentScaleOut)
        {
            seedOut = seed;
            seaLevelOut = 96;
            baseHeightOut = 100;
            continentScaleOut = 0.0015f;

            string path = Path.Combine(WorldFolderPath(worldName), "world.json");
            if (!File.Exists(path)) return false;

            try
            {
                var txt = File.ReadAllText(path);
                seedOut          = ParseInt(txt, "seed", seed);
                seaLevelOut      = ParseInt(txt, "seaLevel", 96);
                baseHeightOut    = ParseInt(txt, "baseHeight", 100);
                continentScaleOut= ParseFloat(txt, "continentScale", 0.0015f);
                return true;
            }
            catch { return false; }
        }

        [Serializable]
        private class WorldSettingsData
        {
            public int maxDroppedItems = DefaultMaxDroppedItems;
            public int inventoryWeightPercent = DefaultInventoryWeightPercent;
            public int containerWeightPercent = DefaultContainerWeightPercent;
            public int showDropVoidWarning = 1;
            public int allowRuinLootRespawn = 1;
            public float fullVoxelRadiusKm = DefaultFullVoxelRadiusKm;
            // Tri-state like its siblings: 1 on, -1 off, 0 = legacy file
            // without the key, which reads as the default (off).
            public int friendlyFire = 0;
            // Tri-state; legacy 0 reads as the default (ON).
            public int allowBannerPainting = 0;
            // Tri-state; legacy 0 reads as the default (ON).
            public int allowTeammateTeleport = 0;
        }

        /// <summary>Non-generation settings only. This sidecar never changes seeds,
        /// terrain, planets, chunks, or any other world-generation parameter.</summary>
        public void SaveWorldSettings()
        {
            try
            {
                Directory.CreateDirectory(WorldFolderPath(worldName));
                maxDroppedItems = Mathf.Clamp(maxDroppedItems, 1, 10000);
                File.WriteAllText(WorldSettingsPath, JsonUtility.ToJson(new WorldSettingsData
                {
                    maxDroppedItems = maxDroppedItems,
                    inventoryWeightPercent = Mathf.Clamp(inventoryWeightPercent, 25, 1000),
                    containerWeightPercent = Mathf.Clamp(containerWeightPercent, 25, 1000),
                    showDropVoidWarning = this.showDropVoidWarning ? 1 : -1,
                    allowRuinLootRespawn = this.allowRuinLootRespawn ? 1 : -1,
                    fullVoxelRadiusKm = Mathf.Clamp(fullVoxelRadiusKm, 0f, 500f),
                    friendlyFire = this.friendlyFire ? 1 : -1,
                    allowBannerPainting = this.allowBannerPainting ? 1 : -1,
                    allowTeammateTeleport = this.allowTeammateTeleport ? 1 : -1
                }, true));
            }
            catch (Exception ex) { Debug.LogWarning("[WorldSession] SaveWorldSettings: " + ex.Message); }
        }

        public void LoadWorldSettings()
        {
            maxDroppedItems = DefaultMaxDroppedItems;
            inventoryWeightPercent = DefaultInventoryWeightPercent;
            containerWeightPercent = DefaultContainerWeightPercent;
            showDropVoidWarning = true;
            allowRuinLootRespawn = DefaultAllowRuinLootRespawn;
            fullVoxelRadiusKm = DefaultFullVoxelRadiusKm;
            friendlyFire = DefaultFriendlyFire;
            allowBannerPainting = true;
            allowTeammateTeleport = true;
            try
            {
                if (!File.Exists(WorldSettingsPath)) return;
                var data = JsonUtility.FromJson<WorldSettingsData>(File.ReadAllText(WorldSettingsPath));
                if (data != null)
                {
                    maxDroppedItems = Mathf.Clamp(data.maxDroppedItems, 1, 10000);
                    inventoryWeightPercent = data.inventoryWeightPercent <= 0 ? DefaultInventoryWeightPercent : Mathf.Clamp(data.inventoryWeightPercent, 25, 1000);
                    containerWeightPercent = data.containerWeightPercent <= 0 ? DefaultContainerWeightPercent : Mathf.Clamp(data.containerWeightPercent, 25, 1000);
                    showDropVoidWarning = data.showDropVoidWarning != -1;
                    allowRuinLootRespawn = data.allowRuinLootRespawn != -1;
                    fullVoxelRadiusKm = data.fullVoxelRadiusKm <= 0f ? DefaultFullVoxelRadiusKm : Mathf.Clamp(data.fullVoxelRadiusKm, 0f, 500f);
                    friendlyFire = data.friendlyFire == 1;
                    allowBannerPainting = data.allowBannerPainting != -1;
                    allowTeammateTeleport = data.allowTeammateTeleport != -1;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[WorldSession] LoadWorldSettings: " + ex.Message); }
        }

        public bool TryReadWorldSettings(string name, out int savedMaxDroppedItems)
        {
            return TryReadWorldSettings(name, out savedMaxDroppedItems, out _, out _, out _, out _);
        }

        public bool TryReadWorldSettings(string name, out int savedMaxDroppedItems,
            out int savedInventoryWeightPercent, out int savedContainerWeightPercent)
        {
            return TryReadWorldSettings(name, out savedMaxDroppedItems,
                out savedInventoryWeightPercent, out savedContainerWeightPercent, out _, out _);
        }

        public bool TryReadWorldSettings(string name, out int savedMaxDroppedItems,
            out int savedInventoryWeightPercent, out int savedContainerWeightPercent,
            out bool savedShowDropVoidWarning)
        {
            bool result = TryReadWorldSettings(name, out savedMaxDroppedItems, out savedInventoryWeightPercent, out savedContainerWeightPercent, out savedShowDropVoidWarning, out _);
            return result;
        }

        public bool TryReadWorldSettings(string name, out int savedMaxDroppedItems,
            out int savedInventoryWeightPercent, out int savedContainerWeightPercent,
            out bool savedShowDropVoidWarning, out bool savedAllowRuinLootRespawn)
        {
            return TryReadWorldSettings(name, out savedMaxDroppedItems,
                out savedInventoryWeightPercent, out savedContainerWeightPercent,
                out savedShowDropVoidWarning, out savedAllowRuinLootRespawn, out _);
        }

        public bool TryReadWorldSettings(string name, out int savedMaxDroppedItems,
            out int savedInventoryWeightPercent, out int savedContainerWeightPercent,
            out bool savedShowDropVoidWarning, out bool savedAllowRuinLootRespawn,
            out bool savedFriendlyFire)
        {
            return TryReadWorldSettings(name, out savedMaxDroppedItems,
                out savedInventoryWeightPercent, out savedContainerWeightPercent,
                out savedShowDropVoidWarning, out savedAllowRuinLootRespawn,
                out savedFriendlyFire, out _);
        }

        public bool TryReadWorldSettings(string name, out int savedMaxDroppedItems,
            out int savedInventoryWeightPercent, out int savedContainerWeightPercent,
            out bool savedShowDropVoidWarning, out bool savedAllowRuinLootRespawn,
            out bool savedFriendlyFire, out bool savedAllowBannerPainting)
        {
            savedMaxDroppedItems = DefaultMaxDroppedItems;
            savedInventoryWeightPercent = DefaultInventoryWeightPercent;
            savedContainerWeightPercent = DefaultContainerWeightPercent;
            savedShowDropVoidWarning = true;
            savedAllowRuinLootRespawn = DefaultAllowRuinLootRespawn;
            savedFriendlyFire = DefaultFriendlyFire;
            savedAllowBannerPainting = true;
            try
            {
                string path = WorldSettingsPathFor(name);
                if (!File.Exists(path)) return false;
                var data = JsonUtility.FromJson<WorldSettingsData>(File.ReadAllText(path));
                if (data == null) return false;
                savedMaxDroppedItems = Mathf.Clamp(data.maxDroppedItems, 1, 10000);
                savedInventoryWeightPercent = data.inventoryWeightPercent <= 0 ? DefaultInventoryWeightPercent : Mathf.Clamp(data.inventoryWeightPercent, 25, 1000);
                savedContainerWeightPercent = data.containerWeightPercent <= 0 ? DefaultContainerWeightPercent : Mathf.Clamp(data.containerWeightPercent, 25, 1000);
                savedShowDropVoidWarning = data.showDropVoidWarning != -1;
                savedAllowRuinLootRespawn = data.allowRuinLootRespawn != -1;
                savedFriendlyFire = data.friendlyFire == 1;
                savedAllowBannerPainting = data.allowBannerPainting != -1;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WorldSession] TryReadWorldSettings: " + ex.Message);
                return false;
            }
        }

        public bool SaveWorldSettingsFor(string name, int newMaxDroppedItems)
        {
            return SaveWorldSettingsFor(name, newMaxDroppedItems,
                inventoryWeightPercent > 0 ? inventoryWeightPercent : DefaultInventoryWeightPercent,
                containerWeightPercent > 0 ? containerWeightPercent : DefaultContainerWeightPercent,
                showDropVoidWarning, allowRuinLootRespawn);
        }

        public bool SaveWorldSettingsFor(string name, int newMaxDroppedItems, int newInventoryWeightPercent, int newContainerWeightPercent)
        {
            return SaveWorldSettingsFor(name, newMaxDroppedItems, newInventoryWeightPercent,
                newContainerWeightPercent, showDropVoidWarning, allowRuinLootRespawn);
        }

        public bool SaveWorldSettingsFor(string name, int newMaxDroppedItems, int newInventoryWeightPercent, int newContainerWeightPercent, bool newShowDropVoidWarning)
        {
            return SaveWorldSettingsFor(name, newMaxDroppedItems, newInventoryWeightPercent, newContainerWeightPercent, newShowDropVoidWarning, allowRuinLootRespawn);
        }

        public bool SaveWorldSettingsFor(string name, int newMaxDroppedItems, int newInventoryWeightPercent, int newContainerWeightPercent, bool newShowDropVoidWarning, bool newAllowRuinLootRespawn)
        {
            // Callers that predate the friendly-fire setting must not reset
            // it: carry the value already on disk (or the default) forward.
            TryReadWorldSettings(name, out _, out _, out _, out _, out _, out bool keepFriendlyFire);
            return SaveWorldSettingsFor(name, newMaxDroppedItems, newInventoryWeightPercent,
                newContainerWeightPercent, newShowDropVoidWarning, newAllowRuinLootRespawn, keepFriendlyFire);
        }

        public bool SaveWorldSettingsFor(string name, int newMaxDroppedItems, int newInventoryWeightPercent, int newContainerWeightPercent, bool newShowDropVoidWarning, bool newAllowRuinLootRespawn, bool newFriendlyFire)
        {
            // Callers that predate the banner-painting setting must not reset
            // it: carry the value already on disk (or the default) forward.
            TryReadWorldSettings(name, out _, out _, out _, out _, out _, out _, out bool keepBannerPainting);
            return SaveWorldSettingsFor(name, newMaxDroppedItems, newInventoryWeightPercent,
                newContainerWeightPercent, newShowDropVoidWarning, newAllowRuinLootRespawn,
                newFriendlyFire, keepBannerPainting);
        }

        public bool SaveWorldSettingsFor(string name, int newMaxDroppedItems, int newInventoryWeightPercent, int newContainerWeightPercent, bool newShowDropVoidWarning, bool newAllowRuinLootRespawn, bool newFriendlyFire, bool newAllowBannerPainting)
        {
            try
            {
                string folder = WorldFolderPath(name);
                Directory.CreateDirectory(folder);

                // Preserve fields this editor does not touch (the voxel-radius
                // choice made at creation would otherwise silently reset).
                float keepFullVoxelRadiusKm = DefaultFullVoxelRadiusKm;
                try
                {
                    string existingPath = WorldSettingsPathFor(name);
                    if (File.Exists(existingPath))
                    {
                        var existing = JsonUtility.FromJson<WorldSettingsData>(File.ReadAllText(existingPath));
                        if (existing != null && existing.fullVoxelRadiusKm > 0f)
                            keepFullVoxelRadiusKm = Mathf.Clamp(existing.fullVoxelRadiusKm, 0f, 500f);
                    }
                }
                catch { /* unreadable existing file - defaults stand */ }

                var data = new WorldSettingsData
                {
                    maxDroppedItems = Mathf.Clamp(newMaxDroppedItems, 1, 10000),
                    inventoryWeightPercent = Mathf.Clamp(newInventoryWeightPercent, 25, 1000),
                    containerWeightPercent = Mathf.Clamp(newContainerWeightPercent, 25, 1000),
                    showDropVoidWarning = newShowDropVoidWarning ? 1 : -1,
                    allowRuinLootRespawn = newAllowRuinLootRespawn ? 1 : -1,
                    fullVoxelRadiusKm = keepFullVoxelRadiusKm,
                    friendlyFire = newFriendlyFire ? 1 : -1,
                    allowBannerPainting = newAllowBannerPainting ? 1 : -1,
                    allowTeammateTeleport = this.allowTeammateTeleport ? 1 : -1
                };
                File.WriteAllText(WorldSettingsPathFor(name), JsonUtility.ToJson(data, true));
                if (SanitizeWorldFolderName(name) == SanitizeWorldFolderName(worldName))
                {
                    maxDroppedItems = data.maxDroppedItems;
                    inventoryWeightPercent = data.inventoryWeightPercent;
                    containerWeightPercent = data.containerWeightPercent;
                    showDropVoidWarning = data.showDropVoidWarning != -1;
                    allowRuinLootRespawn = data.allowRuinLootRespawn != -1;
                    friendlyFire = data.friendlyFire == 1;
                    allowBannerPainting = data.allowBannerPainting != -1;
                    allowTeammateTeleport = data.allowTeammateTeleport != -1;
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WorldSession] SaveWorldSettingsFor: " + ex.Message);
                return false;
            }
        }

        public void DeleteWorld(string name)
        {
            string folder = WorldFolderPath(name);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }

        /// <summary>
        /// Duplicate an existing world's FOLDER to a new name (a true save clone). Chunk saves
        /// and sidecars are copied byte-for-byte so the clone boots identically. Returns the
        /// clone's folder path, or null if the source didn't exist / name was taken.
        /// </summary>
        public string CloneWorld(string sourceName, string cloneName)
        {
            if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(cloneName)) return null;
            string src = WorldFolderPath(sourceName);
            string dst = WorldFolderPath(cloneName);
            if (!Directory.Exists(src)) return null;
            if (Directory.Exists(dst)) return null;

            CopyDirectoryRecursive(src, dst);
            return dst;
        }

        private static void CopyDirectoryRecursive(string sourceFolder, string destinationFolder)
        {
            Directory.CreateDirectory(destinationFolder);
            foreach (var file in Directory.GetFiles(sourceFolder, "*", SearchOption.TopDirectoryOnly))
            {
                string dstFile = Path.Combine(destinationFolder, Path.GetFileName(file));
                File.Copy(file, dstFile, overwrite: false);
            }
            foreach (var dir in Directory.GetDirectories(sourceFolder, "*", SearchOption.TopDirectoryOnly))
            {
                string child = Path.Combine(destinationFolder, Path.GetFileName(dir));
                CopyDirectoryRecursive(dir, child);
            }
        }

        public bool RenameWorld(string sourceName, string newName, out string message)
        {
            message = string.Empty;
            string cleanSource = SanitizeWorldFolderName(sourceName);
            string cleanNew = SanitizeWorldFolderName(newName);
            if (string.IsNullOrWhiteSpace(cleanSource) || string.IsNullOrWhiteSpace(cleanNew))
            {
                message = "World name cannot be empty.";
                return false;
            }
            if (cleanSource == cleanNew)
            {
                message = "World name unchanged.";
                return true;
            }

            string src = WorldFolderPath(cleanSource);
            string dst = WorldFolderPath(cleanNew);
            if (!Directory.Exists(src))
            {
                message = "Original world folder was not found.";
                return false;
            }
            if (Directory.Exists(dst))
            {
                message = "A world with that name already exists.";
                return false;
            }

            try
            {
                Directory.Move(src, dst);
                if (SanitizeWorldFolderName(worldName) == cleanSource)
                    worldName = cleanNew;
                message = "World renamed.";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Debug.LogWarning("[WorldSession] RenameWorld: " + ex.Message);
                return false;
            }
        }

        public List<AutosaveSlotSummary> GetAutosaveSlots(string name)
        {
            var result = new List<AutosaveSlotSummary>(AutosaveSlotCount);
            for (int i = 1; i <= AutosaveSlotCount; i++)
            {
                string path = AutosaveSlotPath(name, i);
                var summary = new AutosaveSlotSummary
                {
                    worldName = SanitizeWorldFolderName(name),
                    slotIndex = i,
                    path = path,
                    exists = File.Exists(path)
                };
                if (summary.exists)
                {
                    var info = new FileInfo(path);
                    summary.lastWrite = info.LastWriteTime;
                    summary.sizeBytes = info.Length;
                }
                result.Add(summary);
            }
            return result;
        }

        public bool RestoreAutosaveSlot(string name, int slotIndex, out string message)
        {
            message = string.Empty;
            if (slotIndex < 1 || slotIndex > AutosaveSlotCount)
            {
                message = "Autosave slot is out of range.";
                return false;
            }

            string source = AutosaveSlotPath(name, slotIndex);
            if (!File.Exists(source))
            {
                message = "Autosave slot is empty.";
                return false;
            }

            string destination = WorldStatePathFor(name);
            string backup = Path.Combine(WorldFolderPath(name), "world_state.before_autosave_restore.json");
            try
            {
                Directory.CreateDirectory(WorldFolderPath(name));
                bool hadCurrentSave = File.Exists(destination);
                AtomicCopyFile(source, destination, backup);
                message = hadCurrentSave
                    ? $"Restored autosave slot {slotIndex}. Previous current save was backed up."
                    : $"Restored autosave slot {slotIndex}.";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Debug.LogWarning("[WorldSession] RestoreAutosaveSlot: " + ex.Message);
                return false;
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

        // ── Cosmos sidecar (per-planet seeds + chosen system) ──────
        /// <summary>Persist the per-planet seed table + chosen system name to cosmos.json.</summary>
        public void SaveCosmosSidecar()
        {
            try
            {
                string folder = WorldFolderPath(worldName);
                Directory.CreateDirectory(folder);
                var payload = new CosmosSidecar
                {
                    chosenSystemName = chosenSystemName ?? "",
                    seedState        = seedState,
                    spawnPlanetIndex = spawnPlanetIndex,
                    orbitPace        = orbitPace,
                };
                File.WriteAllText(CosmosSidecarPath, JsonUtility.ToJson(payload, true));
            }
            catch (System.Exception ex) { Debug.LogWarning("[WorldSession] SaveCosmosSidecar: " + ex.Message); }
        }

        /// <summary>Load the per-planet seed table + chosen system name for the current world.</summary>
        public bool LoadCosmosSidecar()
        {
            try
            {
                if (!File.Exists(CosmosSidecarPath)) return false;
                var data = JsonUtility.FromJson<CosmosSidecar>(File.ReadAllText(CosmosSidecarPath));
                if (data == null) return false;
                chosenSystemName = data.chosenSystemName ?? "";
                seedState        = data.seedState;
                spawnPlanetIndex = data.spawnPlanetIndex;
                orbitPace        = data.orbitPace;
                return seedState != null;
            }
            catch (System.Exception ex) { Debug.LogWarning("[WorldSession] LoadCosmosSidecar: " + ex.Message); return false; }
        }

        /// <summary>The cosmos sidecar as JSON, without touching the disk -
        /// the handshake sends exactly what the file would have contained.</summary>
        public string ExportCosmosJson()
        {
            try
            {
                return JsonUtility.ToJson(new CosmosSidecar
                {
                    chosenSystemName = chosenSystemName ?? "",
                    seedState        = seedState,
                    spawnPlanetIndex = spawnPlanetIndex,
                    orbitPace        = orbitPace,
                });
            }
            catch (System.Exception ex) { Debug.LogWarning("[WorldSession] ExportCosmosJson: " + ex.Message); return ""; }
        }

        /// <summary>Apply a cosmos sidecar received over the wire. Without this
        /// the seed alone is not enough: the per-planet seed table and the
        /// chosen system decide what the terrain actually looks like, so a
        /// client with only the world seed would generate a different planet.</summary>
        public bool ImportCosmosJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                var data = JsonUtility.FromJson<CosmosSidecar>(json);
                if (data == null) return false;
                chosenSystemName = data.chosenSystemName ?? "";
                seedState        = data.seedState;
                spawnPlanetIndex = data.spawnPlanetIndex;
                orbitPace        = data.orbitPace;
                return seedState != null;
            }
            catch (System.Exception ex) { Debug.LogWarning("[WorldSession] ImportCosmosJson: " + ex.Message); return false; }
        }

        [System.Serializable]
        private class CosmosSidecar
        {
            public string chosenSystemName;
            public SystemSeedState seedState;
            public int spawnPlanetIndex;
            public int orbitPace;
        }

        public static string SanitizeWorldFolderName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "DefaultWorld";
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw.Trim())
                if (!invalid.Contains(c)) sb.Append(c);
            return sb.Length == 0 ? "DefaultWorld" : sb.ToString();
        }

        // ---- tiny JSON helpers (no Newtonsoft dep) ----
        private static int? TryReadSeed(string folder)
        {
            string path = Path.Combine(folder, "world.json");
            if (!File.Exists(path)) return null;
            try { return ParseInt(File.ReadAllText(path), "seed", 0); } catch { return null; }
        }

        private static int ParseInt(string txt, string key, int fallback)
        {
            int idx = txt.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (idx < 0) return fallback;
            idx = txt.IndexOf(':', idx) + 1;
            int end = idx;
            while (end < txt.Length && (char.IsDigit(txt[end]) || txt[end] == '-')) end++;
            return int.TryParse(txt.Substring(idx, end - idx).Trim(), out var v) ? v : fallback;
        }
        private static float ParseFloat(string txt, string key, float fallback)
        {
            int idx = txt.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (idx < 0) return fallback;
            idx = txt.IndexOf(':', idx) + 1;
            int end = idx;
            while (end < txt.Length && (char.IsDigit(txt[end]) || txt[end] == '-' || txt[end] == '.' || txt[end] == 'e' || txt[end] == 'E')) end++;
            return float.TryParse(txt.Substring(idx, end - idx).Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }
    }

    public struct WorldSummary
    {
        public string   name;
        public string   folderPath;
        public long     sizeBytes;
        public DateTime lastWrite;
        public int?     savedSeed;
        public int      maxDroppedItems;
        public int      inventoryWeightPercent;
        public int      containerWeightPercent;
        public bool     showDropVoidWarning;
        public bool     allowRuinLootRespawn;
        public bool     friendlyFire;
        public bool     allowBannerPainting;
    }

    public struct AutosaveSlotSummary
    {
        public string   worldName;
        public int      slotIndex;
        public string   path;
        public bool     exists;
        public long     sizeBytes;
        public DateTime lastWrite;
    }
}
