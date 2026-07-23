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

        /// <summary>Maximum simultaneous physical world drops. Conveyor packets use
        /// their own simulation and are deliberately never included in this limit.</summary>
        public int maxDroppedItems = 90;
        public const int DefaultMaxDroppedItems = 90;

        // Spawn data. The world spawn is computed once on first load; bed spawn is per-bed.
        public Vector3 worldSpawnPoint = new Vector3(0, 200, 0);
        public bool    worldSpawnInitialized = false;
        public Vector3 bedSpawnPoint = Vector3.zero;
        public bool    hasBedSpawn = false;

        public Vector3 GetActiveSpawn() => hasBedSpawn ? bedSpawnPoint : worldSpawnPoint;
        public bool   isNewWorld = false;

        // (Legacy flat-world override fields removed — the sphere uses BodySettings.)

        // ── Cosmos (per-planet seeds + chosen solar system) ────────
        /// <summary>Name of the solar-system template the player selected at world creation.</summary>
        public string chosenSystemName = "";
        /// <summary>Per-planet seed table (one editable, randomized-by-default seed per planet).</summary>
        public SystemSeedState seedState;

        /// <summary>Index of the planet to spawn on (0 = first planet in the system).</summary>
        public int spawnPlanetIndex = 0;

        public const int AutosaveSlotCount = 3;

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
                    bedSpawn   = bedSpawnPoint,   hasBed    = hasBedSpawn
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
        }

        public List<WorldSummary> ListWorlds()
        {
            var result = new List<WorldSummary>();
            if (!Directory.Exists(WorldsRoot)) return result;
            foreach (var dir in Directory.GetDirectories(WorldsRoot))
            {
                var info = new DirectoryInfo(dir);
                long size = 0;
                foreach (var f in info.GetFiles("*.dat", SearchOption.TopDirectoryOnly))
                    size += f.Length;
                int? savedSeed = TryReadSeed(dir);
                int savedMaxDrops = TryReadWorldSettings(info.Name, out var maxDrops)
                    ? maxDrops : DefaultMaxDroppedItems;
                result.Add(new WorldSummary
                {
                    name        = info.Name,
                    folderPath  = dir,
                    sizeBytes   = size,
                    lastWrite   = info.LastWriteTime,
                    savedSeed   = savedSeed,
                    maxDroppedItems = savedMaxDrops
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
                    maxDroppedItems = maxDroppedItems
                }, true));
            }
            catch (Exception ex) { Debug.LogWarning("[WorldSession] SaveWorldSettings: " + ex.Message); }
        }

        public void LoadWorldSettings()
        {
            maxDroppedItems = DefaultMaxDroppedItems;
            try
            {
                if (!File.Exists(WorldSettingsPath)) return;
                var data = JsonUtility.FromJson<WorldSettingsData>(File.ReadAllText(WorldSettingsPath));
                if (data != null) maxDroppedItems = Mathf.Clamp(data.maxDroppedItems, 1, 10000);
            }
            catch (Exception ex) { Debug.LogWarning("[WorldSession] LoadWorldSettings: " + ex.Message); }
        }

        public bool TryReadWorldSettings(string name, out int savedMaxDroppedItems)
        {
            savedMaxDroppedItems = DefaultMaxDroppedItems;
            try
            {
                string path = WorldSettingsPathFor(name);
                if (!File.Exists(path)) return false;
                var data = JsonUtility.FromJson<WorldSettingsData>(File.ReadAllText(path));
                if (data == null) return false;
                savedMaxDroppedItems = Mathf.Clamp(data.maxDroppedItems, 1, 10000);
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
            try
            {
                string folder = WorldFolderPath(name);
                Directory.CreateDirectory(folder);
                var data = new WorldSettingsData
                {
                    maxDroppedItems = Mathf.Clamp(newMaxDroppedItems, 1, 10000)
                };
                File.WriteAllText(WorldSettingsPathFor(name), JsonUtility.ToJson(data, true));
                if (SanitizeWorldFolderName(name) == SanitizeWorldFolderName(worldName))
                    maxDroppedItems = data.maxDroppedItems;
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
                return seedState != null;
            }
            catch (System.Exception ex) { Debug.LogWarning("[WorldSession] LoadCosmosSidecar: " + ex.Message); return false; }
        }

        [System.Serializable]
        private class CosmosSidecar
        {
            public string chosenSystemName;
            public SystemSeedState seedState;
            public int spawnPlanetIndex;
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
