// Assets/Scripts/VoxelEngine/Scattering/TreeHarvestPersistence.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Scattering
{
    /// <summary>
    /// Persists harvested deterministic biome trees separately from voxel edits.
    /// Scatter is regenerated from the body's seed when a world opens, so tree
    /// removals need a small per-world/body record of their stable surface cells.
    /// </summary>
    public static class TreeHarvestPersistence
    {
        [Serializable]
        private sealed class SaveData
        {
            public List<string> harvested = new();
        }

        private static readonly Dictionary<string, HashSet<string>> s_harvestedByPath = new();

        /// <summary>Returns the seed-scoped sidecar path for a persistent sphere world.</summary>
        public static string GetStorePath(IChunkScatterWorld world)
        {
            if (world is not SphereWorld sphere || !sphere.enablePersistence
                || sphere.body == null || sphere.body.settings == null)
                return null;

            string folder = sphere.StoreFolderOfBody(sphere.body.settings.bodyName);
            if (string.IsNullOrWhiteSpace(folder))
            {
                string worldName = string.IsNullOrWhiteSpace(sphere.worldName)
                    ? "DefaultSphereWorld" : sphere.worldName;
                string bodyName = (sphere.body.settings.bodyName ?? "Body").Replace(" ", string.Empty);
                folder = Path.Combine(Application.persistentDataPath, "VoxelWorlds", worldName,
                    "Bodies", bodyName);
            }

            return Path.Combine(folder, "tree_harvest_" + sphere.body.genParams.seed + ".json");
        }

        /// <summary>Stable identity for one deterministic scatter candidate on a body surface.</summary>
        public static string MakeIdentity(Vector3Int surfaceVoxel)
            => surfaceVoxel.x + ":" + surfaceVoxel.y + ":" + surfaceVoxel.z;

        public static bool IsHarvested(string storePath, string identity)
        {
            if (string.IsNullOrWhiteSpace(storePath) || string.IsNullOrWhiteSpace(identity)) return false;
            return Load(storePath).Contains(identity);
        }

        /// <summary>Record immediately when the player fells a generated tree.</summary>
        public static void RecordHarvested(string storePath, string identity)
        {
            if (string.IsNullOrWhiteSpace(storePath) || string.IsNullOrWhiteSpace(identity)) return;
            HashSet<string> harvested = Load(storePath);
            if (!harvested.Add(identity)) return;

            try
            {
                string directory = Path.GetDirectoryName(storePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var data = new SaveData { harvested = new List<string>(harvested) };
                string temporaryPath = storePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(data));
                if (File.Exists(storePath))
                {
                    File.Copy(temporaryPath, storePath, true);
                    File.Delete(temporaryPath);
                }
                else
                {
                    File.Move(temporaryPath, storePath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[TreeHarvestPersistence] Could not save harvested tree state; "
                    + "this tree may return when its world is reopened. " + exception.Message);
            }
        }

        private static HashSet<string> Load(string storePath)
        {
            if (s_harvestedByPath.TryGetValue(storePath, out HashSet<string> cached)) return cached;

            var harvested = new HashSet<string>(StringComparer.Ordinal);
            s_harvestedByPath[storePath] = harvested;
            try
            {
                if (!File.Exists(storePath)) return harvested;
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(storePath));
                if (data?.harvested == null) return harvested;
                foreach (string identity in data.harvested)
                    if (!string.IsNullOrWhiteSpace(identity)) harvested.Add(identity);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[TreeHarvestPersistence] Could not load harvested tree state from '"
                    + storePath + "': " + exception.Message);
            }
            return harvested;
        }
    }
}
