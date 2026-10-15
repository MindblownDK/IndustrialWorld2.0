// Assets/Scripts/VoxelEngine/Scattering/TreeHarvestPersistence.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using VoxelEngine.Biomes;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;
using VoxelEngine.Environment;

namespace VoxelEngine.Scattering
{
    /// <summary>
    /// Persists harvested or cleared deterministic biome trees and non-mob scatter
    /// separately from voxel edits, and manages their in-game regrowth timer.
    /// Scatter never regrows on save load; entries only count down during active
    /// gameplay when the area is not too polluted and no player structures are nearby.
    /// </summary>
    public static class TreeHarvestPersistence
    {
        /// <summary>Default in-game regrowth time for felled trees (15 minutes of clean gameplay).</summary>
        public const float TreeRegrowSeconds = 900f;

        /// <summary>Default in-game regrowth time for non-tree, non-mob scatter props (10 minutes of clean gameplay).</summary>
        public const float ScatterRegrowSeconds = 600f;

        /// <summary>Radius around player structures within which scatter regrowth is blocked.</summary>
        public const float StructureBlockRegrowthRadius = 8.0f;

        /// <summary>Maximum pollution pressure (0..1) that still allows scatter regrowth.</summary>
        public const float MaxRegrowthPollutionPressure01 = 0.35f;

        /// <summary>Spatial tolerance (body-local metres) used alongside voxel identity to prevent regrowing on load.</summary>
        private const float SpatialMatchRadius = 2.25f;
        private const float SpatialMatchRadiusSq = SpatialMatchRadius * SpatialMatchRadius;

        [Serializable]
        public sealed class HarvestEntry
        {
            public string id;
            public float remainingSeconds;
            public Vector3 localPos;
            public bool hasLocalPos;
            public bool isTree;
        }

        [Serializable]
        private sealed class SaveData
        {
            public List<string> harvested = new();
            public List<HarvestEntry> entries = new();
        }

        private sealed class StoreState
        {
            public readonly HashSet<string> ids = new(StringComparer.Ordinal);
            public readonly Dictionary<string, HarvestEntry> byId = new(StringComparer.Ordinal);
            public readonly List<HarvestEntry> list = new();
            public bool dirty;
            public float saveCooldown;
        }

        private static readonly Dictionary<string, StoreState> s_storesByPath = new(StringComparer.Ordinal);

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

        public static bool TryParseIdentity(string identity, out Vector3Int voxel)
        {
            voxel = default;
            if (string.IsNullOrWhiteSpace(identity)) return false;
            string[] parts = identity.Split(':');
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) return false;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z)) return false;
            voxel = new Vector3Int(x, y, z);
            return true;
        }

        public static bool IsHarvested(string storePath, string identity)
        {
            if (string.IsNullOrWhiteSpace(storePath) || string.IsNullOrWhiteSpace(identity)) return false;
            StoreState state = LoadStore(storePath);
            if (state.ids.Contains(identity)) return true;
            if (TryParseIdentity(identity, out Vector3Int voxel))
            {
                Vector3 approxLocal = ((Vector3)voxel + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE;
                return IsHarvestedNearLocal(state, approxLocal);
            }
            return false;
        }

        public static bool IsHarvested(string storePath, string identity, Vector3 localSurface)
        {
            if (string.IsNullOrWhiteSpace(storePath)) return false;
            StoreState state = LoadStore(storePath);
            if (!string.IsNullOrWhiteSpace(identity) && state.ids.Contains(identity))
                return true;
            return IsHarvestedNearLocal(state, localSurface);
        }

        private static bool IsHarvestedNearLocal(StoreState state, Vector3 localPos)
        {
            if (state == null || localPos.sqrMagnitude < 0.0001f) return false;
            for (int i = 0; i < state.list.Count; i++)
            {
                HarvestEntry entry = state.list[i];
                if (entry == null || !entry.hasLocalPos) continue;
                if ((entry.localPos - localPos).sqrMagnitude <= SpatialMatchRadiusSq)
                    return true;
            }
            return false;
        }

        /// <summary>Record immediately when the player fells a generated tree or clears non-mob scatter.</summary>
        public static void RecordHarvested(string storePath, string identity)
        {
            Vector3 localPos = Vector3.zero;
            bool hasLocal = false;
            if (TryParseIdentity(identity, out Vector3Int voxel))
            {
                localPos = ((Vector3)voxel + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE;
                hasLocal = true;
            }
            RecordHarvested(storePath, identity, localPos, hasLocal, isTree: true);
        }

        public static void RecordHarvested(string storePath, string identity, Vector3 localPos, bool isTree)
        {
            RecordHarvested(storePath, identity, localPos, localPos.sqrMagnitude > 0.0001f, isTree);
        }

        public static void RecordHarvested(string storePath, string identity, Vector3 localPos, bool hasLocalPos, bool isTree)
        {
            if (string.IsNullOrWhiteSpace(storePath) || string.IsNullOrWhiteSpace(identity)) return;
            StoreState state = LoadStore(storePath);
            float duration = isTree ? TreeRegrowSeconds : ScatterRegrowSeconds;

            if (state.byId.TryGetValue(identity, out HarvestEntry existing))
            {
                existing.remainingSeconds = Mathf.Max(existing.remainingSeconds, duration);
                if (hasLocalPos)
                {
                    existing.localPos = localPos;
                    existing.hasLocalPos = true;
                }
                existing.isTree |= isTree;
                SaveStore(storePath, state);
                return;
            }

            if (!hasLocalPos && TryParseIdentity(identity, out Vector3Int voxel))
            {
                localPos = ((Vector3)voxel + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE;
                hasLocalPos = true;
            }

            var entry = new HarvestEntry
            {
                id = identity,
                remainingSeconds = duration,
                localPos = localPos,
                hasLocalPos = hasLocalPos,
                isTree = isTree
            };
            state.ids.Add(identity);
            state.byId[identity] = entry;
            state.list.Add(entry);
            SaveStore(storePath, state);
        }

        /// <summary>
        /// Advances in-game regrowth timers for the active planet body.
        /// Timers pause if the area is polluted and hold if player structures are built nearby.
        /// </summary>
        public static void TickRegrowth(SphereWorld world, BiomeRegistry registry, int seed, float dt)
        {
            if (world == null || world.body == null || dt <= 0f) return;
            string storePath = GetStorePath(world);
            if (string.IsNullOrWhiteSpace(storePath)) return;

            StoreState state = LoadStore(storePath);
            if (state.list.Count == 0) return;

            Transform bodyTransform = world.body.transform;
            bool anyRemoved = false;

            for (int i = state.list.Count - 1; i >= 0; i--)
            {
                HarvestEntry entry = state.list[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                {
                    state.list.RemoveAt(i);
                    anyRemoved = true;
                    continue;
                }

                Vector3 localPos = entry.hasLocalPos
                    ? entry.localPos
                    : (TryParseIdentity(entry.id, out Vector3Int v)
                        ? ((Vector3)v + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE
                        : Vector3.zero);

                if (localPos.sqrMagnitude < 0.0001f) continue;

                Vector3 worldPos = bodyTransform.TransformPoint(localPos);
                Vector3 worldUp = (worldPos - bodyTransform.position).sqrMagnitude > 0.0001f
                    ? (worldPos - bodyTransform.position).normalized
                    : Vector3.up;

                // Never regrow trees or scatter if a player structure is nearby.
                if (ChunkScatter.IsNearPlayerStructure(worldPos, worldUp, StructureBlockRegrowthRadius))
                {
                    entry.remainingSeconds = Mathf.Max(entry.remainingSeconds, 60f);
                    continue;
                }

                // Pause regrowth if the area is too polluted.
                EcologyReading ecology = EcologyPressure.Sample(worldPos);
                if (ecology.Pressure01 >= MaxRegrowthPollutionPressure01)
                    continue;

                float vitalityScale = Mathf.Clamp(ecology.Vitality01, 0.35f, 1f);
                entry.remainingSeconds -= dt * vitalityScale;
                state.dirty = true;

                if (entry.remainingSeconds > 0f)
                    continue;

                // Timer completed: remove from harvested list and respawn in place if chunk is loaded.
                state.ids.Remove(entry.id);
                state.byId.Remove(entry.id);
                state.list.RemoveAt(i);
                anyRemoved = true;

                if (TryParseIdentity(entry.id, out Vector3Int localVoxel))
                    ChunkScatter.TryRespawnAtVoxel(world, registry, seed, localVoxel);
            }

            if (anyRemoved)
            {
                SaveStore(storePath, state);
                return;
            }

            if (state.dirty)
            {
                state.saveCooldown += dt;
                if (state.saveCooldown >= 15f)
                {
                    state.saveCooldown = 0f;
                    SaveStore(storePath, state);
                }
            }
        }

        /// <summary>Flushes any dirty in-memory timer state to disk and clears cached state if requested.</summary>
        public static void FlushAll(bool clearMemoryCache = false)
        {
            foreach (var kv in s_storesByPath)
            {
                if (kv.Value != null && kv.Value.dirty)
                    SaveStore(kv.Key, kv.Value);
            }
            if (clearMemoryCache)
                s_storesByPath.Clear();
        }

        private static StoreState LoadStore(string storePath)
        {
            if (s_storesByPath.TryGetValue(storePath, out StoreState cached) && cached != null)
                return cached;

            var state = new StoreState();
            s_storesByPath[storePath] = state;
            try
            {
                if (!File.Exists(storePath)) return state;
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(storePath));
                if (data == null) return state;

                if (data.entries != null)
                {
                    for (int i = 0; i < data.entries.Count; i++)
                    {
                        HarvestEntry e = data.entries[i];
                        if (e == null || string.IsNullOrWhiteSpace(e.id)) continue;
                        if (state.byId.ContainsKey(e.id)) continue;

                        if (e.remainingSeconds <= 0.1f)
                            e.remainingSeconds = e.isTree ? TreeRegrowSeconds : ScatterRegrowSeconds;

                        if (!e.hasLocalPos && TryParseIdentity(e.id, out Vector3Int voxel))
                        {
                            e.localPos = ((Vector3)voxel + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE;
                            e.hasLocalPos = true;
                        }

                        state.ids.Add(e.id);
                        state.byId[e.id] = e;
                        state.list.Add(e);
                    }
                }

                if (data.harvested != null)
                {
                    for (int i = 0; i < data.harvested.Count; i++)
                    {
                        string id = data.harvested[i];
                        if (string.IsNullOrWhiteSpace(id) || state.byId.ContainsKey(id)) continue;

                        Vector3 localPos = Vector3.zero;
                        bool hasLocalPos = false;
                        if (TryParseIdentity(id, out Vector3Int voxel))
                        {
                            localPos = ((Vector3)voxel + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE;
                            hasLocalPos = true;
                        }

                        var migrated = new HarvestEntry
                        {
                            id = id,
                            remainingSeconds = TreeRegrowSeconds,
                            localPos = localPos,
                            hasLocalPos = hasLocalPos,
                            isTree = true
                        };
                        state.ids.Add(id);
                        state.byId[id] = migrated;
                        state.list.Add(migrated);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[TreeHarvestPersistence] Could not load harvested scatter state from '"
                    + storePath + "': " + exception.Message);
            }
            return state;
        }

        private static void SaveStore(string storePath, StoreState state)
        {
            if (string.IsNullOrWhiteSpace(storePath) || state == null) return;
            try
            {
                string directory = Path.GetDirectoryName(storePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var data = new SaveData
                {
                    harvested = new List<string>(state.ids),
                    entries = new List<HarvestEntry>(state.list)
                };
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
                state.dirty = false;
                state.saveCooldown = 0f;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[TreeHarvestPersistence] Could not save harvested scatter state; "
                    + "removed scatter may return when its world is reopened. " + exception.Message);
            }
        }
    }
}
