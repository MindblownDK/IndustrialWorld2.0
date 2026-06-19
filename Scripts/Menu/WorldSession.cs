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

        // Spawn data. The world spawn is computed once on first load; bed spawn is per-bed.
        public Vector3 worldSpawnPoint = new Vector3(0, 200, 0);
        public bool    worldSpawnInitialized = false;
        public Vector3 bedSpawnPoint = Vector3.zero;
        public bool    hasBedSpawn = false;

        public Vector3 GetActiveSpawn() => hasBedSpawn ? bedSpawnPoint : worldSpawnPoint;
        public bool   isNewWorld = false;

        // Optional overrides for new worlds (applied to PlanetSettings on first load).
        public int   newSeaLevel       = 96;
        public int   newBaseHeight     = 100;
        public float newContinentScale = 0.0015f;

        // ── Cosmos (per-planet seeds + chosen solar system) ────────
        /// <summary>Name of the solar-system template the player selected at world creation.</summary>
        public string chosenSystemName = "";
        /// <summary>Per-planet seed table (one editable, randomized-by-default seed per planet).</summary>
        public SystemSeedState seedState;

        public string CosmosSidecarPath =>
            Path.Combine(WorldsRoot, worldName, "cosmos.json");

        public string WorldsRoot =>
            Path.Combine(Application.persistentDataPath, "VoxelWorlds");

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
                string folder = System.IO.Path.Combine(WorldsRoot, worldName);
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
                result.Add(new WorldSummary
                {
                    name        = info.Name,
                    folderPath  = dir,
                    sizeBytes   = size,
                    lastWrite   = info.LastWriteTime,
                    savedSeed   = savedSeed
                });
            }
            result.Sort((a, b) => b.lastWrite.CompareTo(a.lastWrite));
            return result;
        }

        // We persist a tiny JSON sidecar per world so the menu can show its seed
        // and so loading a world restores the same seed it was generated with.
        public void WriteSeedSidecar()
        {
            string folder = Path.Combine(WorldsRoot, worldName);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "world.json");
            File.WriteAllText(path,
                $"{{\"seed\":{seed},\"seaLevel\":{newSeaLevel},\"baseHeight\":{newBaseHeight}," +
                $"\"continentScale\":{newContinentScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
        }

        public bool TryReadSidecar(out int seedOut, out int seaLevelOut, out int baseHeightOut, out float continentScaleOut)
        {
            seedOut = seed;
            seaLevelOut = newSeaLevel;
            baseHeightOut = newBaseHeight;
            continentScaleOut = newContinentScale;

            string path = Path.Combine(WorldsRoot, worldName, "world.json");
            if (!File.Exists(path)) return false;

            try
            {
                var txt = File.ReadAllText(path);
                seedOut          = ParseInt(txt, "seed", seed);
                seaLevelOut      = ParseInt(txt, "seaLevel", newSeaLevel);
                baseHeightOut    = ParseInt(txt, "baseHeight", newBaseHeight);
                continentScaleOut= ParseFloat(txt, "continentScale", newContinentScale);
                return true;
            }
            catch { return false; }
        }

        public void DeleteWorld(string name)
        {
            string folder = Path.Combine(WorldsRoot, name);
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
            string src = Path.Combine(WorldsRoot, sourceName);
            string dst = Path.Combine(WorldsRoot, cloneName);
            if (!Directory.Exists(src)) return null;
            if (Directory.Exists(dst)) return null;

            Directory.CreateDirectory(dst);
            // Deep-copy every file (subfolders not currently used by the save format).
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.TopDirectoryOnly))
            {
                string dstFile = Path.Combine(dst, Path.GetFileName(f));
                File.Copy(f, dstFile, overwrite: false);
            }
            return dst;
        }

        // ── Cosmos sidecar (per-planet seeds + chosen system) ──────
        /// <summary>Persist the per-planet seed table + chosen system name to cosmos.json.</summary>
        public void SaveCosmosSidecar()
        {
            try
            {
                string folder = Path.Combine(WorldsRoot, worldName);
                Directory.CreateDirectory(folder);
                var payload = new CosmosSidecar
                {
                    chosenSystemName = chosenSystemName ?? "",
                    seedState        = seedState,
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
                return seedState != null;
            }
            catch (System.Exception ex) { Debug.LogWarning("[WorldSession] LoadCosmosSidecar: " + ex.Message); return false; }
        }

        [System.Serializable]
        private class CosmosSidecar
        {
            public string chosenSystemName;
            public SystemSeedState seedState;
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
    }
}
