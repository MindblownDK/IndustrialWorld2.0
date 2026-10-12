#if UNITY_EDITOR
// Assets/Scripts/VoxelEngine/Editor/Biomes/ScatterDuplicateCleanup.cs
//
// 17.20.2-dev one-time repair: doubled creature scatter entries.
//
// Older setup eras wrote per-biome creature copies under
// VoxelEngineAssets/Scatter/<Biome>/Enemies (and /Passive); a later era added
// the canonical Resources/Enemies prefabs to the same biome definitions
// without removing the legacy entries. The affected biomes (Desert, Forest,
// Mountains, Steppes at the time of writing) therefore spawned the SAME
// creature from two byte-identical assets at adjacent surface points - the
// doubled, overlapping enemies seen in the 17.20.0 test report.
//
// ChunkScatter now also dedupes at runtime, so worlds are safe either way;
// this tool repairs the data itself so the biome inspector matches reality.
//
// Non-destructive: only duplicate creature entries are removed. The kept
// entry preserves its authored density and scale ranges untouched, canonical
// Resources prefabs win over legacy Scatter copies, and no prefab asset is
// ever deleted or modified. Safe to re-run. Logs with the [Scatter Cleanup]
// prefix.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Biomes;

namespace VoxelEngine.EditorTools
{
    public static class ScatterDuplicateCleanup
    {
        [MenuItem("Tools/Voxel Engine/Repair Duplicate Creature Scatter Entries")]
        public static void Run()
        {
            string[] guids = AssetDatabase.FindAssets("t:BiomeDefinition");
            int biomesTouched = 0;
            int entriesRemoved = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var biome = AssetDatabase.LoadAssetAtPath<BiomeDefinition>(path);
                if (biome == null) continue;

                int before = entriesRemoved;
                Undo.RecordObject(biome, "Repair duplicate creature scatter entries");
                biome.enemyScatter = Dedup(biome.enemyScatter, biome, "enemyScatter", ref entriesRemoved);
                biome.passiveScatter = Dedup(biome.passiveScatter, biome, "passiveScatter", ref entriesRemoved);
                if (entriesRemoved != before)
                {
                    EditorUtility.SetDirty(biome);
                    biomesTouched++;
                }
            }

            if (entriesRemoved > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[Scatter Cleanup] Removed {entriesRemoved} duplicate creature " +
                    $"scatter entr{(entriesRemoved == 1 ? "y" : "ies")} across {biomesTouched} " +
                    "biome definition(s). Kept entries and their authored values are untouched; " +
                    "legacy Scatter prefab assets were left on disk.");
            }
            else
            {
                Debug.Log("[Scatter Cleanup] No duplicate creature scatter entries found - nothing to do.");
            }
        }

        /// <summary>Keeps one entry per creature name: the canonical Resources
        /// prefab wins, then the highest density, then array order. Entries with
        /// a missing prefab are preserved exactly as they are.</summary>
        private static BiomeDefinition.ScatterEntry[] Dedup(BiomeDefinition.ScatterEntry[] entries,
            BiomeDefinition biome, string fieldName, ref int removedCount)
        {
            if (entries == null || entries.Length < 2) return entries;

            var byName = new Dictionary<string, List<int>>();
            for (int i = 0; i < entries.Length; i++)
            {
                var prefab = entries[i].prefab;
                if (prefab == null) continue;
                if (!byName.TryGetValue(prefab.name, out var list))
                    byName[prefab.name] = list = new List<int>();
                list.Add(i);
            }

            var drop = new HashSet<int>();
            foreach (var pair in byName)
            {
                if (pair.Value.Count < 2) continue;
                int keep = -1;
                bool keepIsCanonical = false;
                foreach (int i in pair.Value)
                {
                    bool canonical = IsCanonical(AssetDatabase.GetAssetPath(entries[i].prefab));
                    if (keep < 0
                        || (canonical && !keepIsCanonical)
                        || (canonical == keepIsCanonical && entries[i].density > entries[keep].density))
                    {
                        keep = i;
                        keepIsCanonical = canonical;
                    }
                }
                foreach (int i in pair.Value)
                {
                    if (i == keep) continue;
                    drop.Add(i);
                    removedCount++;
                    Debug.Log($"[Scatter Cleanup] {biome.name}.{fieldName}: removed duplicate " +
                        $"'{pair.Key}' entry {AssetDatabase.GetAssetPath(entries[i].prefab)} " +
                        $"(kept {AssetDatabase.GetAssetPath(entries[keep].prefab)}, " +
                        $"density {entries[keep].density:0.####})");
                }
            }

            if (drop.Count == 0) return entries;
            var result = new List<BiomeDefinition.ScatterEntry>(entries.Length);
            for (int i = 0; i < entries.Length; i++)
                if (!drop.Contains(i)) result.Add(entries[i]);
            return result.ToArray();
        }

        /// <summary>The repo root is Assets/, so canonical creature prefabs live
        /// under Resources/Enemies and resolve as "Resources/..." asset paths.</summary>
        private static bool IsCanonical(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Resources/");
        }
    }
}
#endif
