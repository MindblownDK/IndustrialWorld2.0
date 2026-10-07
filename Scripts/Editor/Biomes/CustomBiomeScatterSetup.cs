// Assets/Scripts/VoxelEngine/Editor/Biomes/CustomBiomeScatterSetup.cs
//
// Central Setup Wizard support for folder-driven biome scatter authoring.
// Designers add prefabs under Assets/VoxelEngineAssets/Scatter/<Biome>/ and rerun
// the wizard action. Existing ScatterEntry values are never overwritten.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Biomes;

namespace VoxelEngine.EditorTools
{
    public enum BiomeScatterCategory
    {
        Environment = 0,
        Enemies = 1,
        Passive = 2,
        Buildings = 3,
    }

    public static class CustomBiomeScatterSetup
    {
        private const string BiomeRoot = "Assets/VoxelEngineAssets/Biomes";
        private const string ScatterRoot = "Assets/VoxelEngineAssets/Scatter";
        private const string LegacyThemedRoot = ScatterRoot + "/ThemedWorlds";

        private readonly struct PlaceholderBinding
        {
            public readonly string PrefabName;
            public readonly string BiomeFolder;

            public PlaceholderBinding(string prefabName, string biomeFolder)
            {
                PrefabName = prefabName;
                BiomeFolder = biomeFolder;
            }
        }

        private static readonly PlaceholderBinding[] ThemedPlaceholders =
        {
            new("Prop_LunarCraterRock", "LunarHighlands"),
            new("Prop_MartianBoulder", "MartianDust"),
            new("Prop_MartianDryShrub", "MartianDust"),
            new("Prop_VenusSulfurVent", "VenusianAsh"),
            new("Prop_VenusAshBoulder", "VenusianAsh"),
            new("Prop_AcidGlowFungus", "AcidBog"),
            new("Prop_AcidReeds", "AcidBog"),
            new("Prop_PirateScrapPile", "PirateScrap"),
            new("Prop_PirateErodedRock", "PirateScrap"),
            new("Prop_OlympusCypress", "GreekMarble"),
            new("Prop_OlympusMarbleFragments", "GreekMarble"),
            new("Prop_IceSpire", "FrozenGlacier"),
            new("Prop_FrostBoulder", "FrozenGlacier"),
            new("Prop_OceanReeds", "OceanShelf"),
            new("Prop_OceanPalm", "OceanShelf"),
            new("Prop_DesolateDeadShrub", "DesolateWastes"),
            new("Prop_DesolateErodedRock", "DesolateWastes"),
            new("Prop_VolcanicBasaltColumns", "VolcanicBasalt"),
            new("Prop_VolcanicVent", "VolcanicBasalt"),
            new("Prop_CrystalCluster", "CrystalGeode"),
            new("Prop_CrystalShard", "CrystalGeode"),
        };

        public static void Run()
        {
            EnsureFolder(ScatterRoot);
            int movedPlaceholders = MigrateKnownThemedPlaceholders();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int biomes = 0;
            int environmentAdded = 0;
            int enemiesAdded = 0;
            int passiveAdded = 0;
            int buildingsAdded = 0;
            int migrated = 0;
            int nullEntriesRemoved = 0;

            string[] biomeGuids = AssetDatabase.FindAssets("t:BiomeDefinition", new[] { BiomeRoot });
            Array.Sort(biomeGuids, (a, b) => string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b)));

            foreach (string guid in biomeGuids)
            {
                string biomePath = AssetDatabase.GUIDToAssetPath(guid);
                var biome = AssetDatabase.LoadAssetAtPath<BiomeDefinition>(biomePath);
                if (biome == null) continue;

                string assetName = Path.GetFileNameWithoutExtension(biomePath);
                string key = assetName.StartsWith("Biome_", StringComparison.Ordinal)
                    ? assetName.Substring("Biome_".Length)
                    : assetName;
                string folderKey = ResolveFolderKey(key);
                string biomeFolder = ScatterRoot + "/" + folderKey;
                EnsureFolder(biomeFolder);
                EnsureFolder(biomeFolder + "/Enemies");
                EnsureFolder(biomeFolder + "/Passive");
                EnsureFolder(biomeFolder + "/Buildings");

                nullEntriesRemoved += RemoveNullEntries(biome, BiomeScatterCategory.Environment);
                nullEntriesRemoved += RemoveNullEntries(biome, BiomeScatterCategory.Enemies);
                nullEntriesRemoved += RemoveNullEntries(biome, BiomeScatterCategory.Passive);
                nullEntriesRemoved += RemoveNullEntries(biome, BiomeScatterCategory.Buildings);
                migrated += MigrateLegacyEnvironmentEntries(biome);

                environmentAdded += SyncFolder(biome, biomeFolder, folderKey,
                    BiomeScatterCategory.Environment);
                enemiesAdded += SyncFolder(biome, biomeFolder, folderKey,
                    BiomeScatterCategory.Enemies);
                passiveAdded += SyncFolder(biome, biomeFolder, folderKey,
                    BiomeScatterCategory.Passive);
                buildingsAdded += SyncFolder(biome, biomeFolder, folderKey,
                    BiomeScatterCategory.Buildings);
                biomes++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Voxel Engine — Custom Biome Scatter",
                "Folder-driven biome scatter synchronized non-destructively:\n\n" +
                "• Biomes scanned: " + biomes + "\n" +
                "• Environment prefabs added: " + environmentAdded + "\n" +
                "• Enemy prefabs added: " + enemiesAdded + "\n" +
                "• Passive prefabs added: " + passiveAdded + "\n" +
                "• Building prefabs added: " + buildingsAdded + "\n" +
                "• Legacy entries moved to the correct category: " + migrated + "\n" +
                "• Missing prefab entries removed: " + nullEntriesRemoved + "\n" +
                "• Themed placeholders moved from ThemedWorlds: " + movedPlaceholders + "\n\n" +
                "Existing density, scale and height values were preserved. New prefabs received category and biome-appropriate defaults.",
                "OK");
        }

        public static bool EnsureEntry(BiomeDefinition biome, BiomeScatterCategory category,
            GameObject prefab, float density, float minScale, float maxScale,
            float minHeight = 0f, float maxHeight = 9999f)
        {
            if (biome == null || prefab == null) return false;

            var target = ToList(GetEntries(biome, category));
            if (ContainsPrefab(target, prefab))
            {
                RemovePrefabFromOtherCategories(biome, category, prefab);
                return false;
            }

            bool migrated = false;
            foreach (BiomeScatterCategory other in Enum.GetValues(typeof(BiomeScatterCategory)))
            {
                if (other == category) continue;
                var source = ToList(GetEntries(biome, other));
                int index = source.FindIndex(entry => entry.prefab == prefab);
                if (index < 0) continue;
                target.Add(source[index]);
                source.RemoveAll(entry => entry.prefab == prefab);
                SetEntries(biome, other, source.ToArray());
                migrated = true;
                break;
            }

            if (!migrated)
            {
                target.Add(new BiomeDefinition.ScatterEntry
                {
                    prefab = prefab,
                    density = density,
                    minScale = minScale,
                    maxScale = maxScale,
                    minHeight = minHeight,
                    maxHeight = maxHeight,
                });
            }

            SetEntries(biome, category, target.ToArray());
            RemovePrefabFromOtherCategories(biome, category, prefab);
            EditorUtility.SetDirty(biome);
            return true;
        }

        public static GameObject TryMigrateThemedPlaceholder(string prefabName, string biomeFolder)
        {
            if (string.IsNullOrWhiteSpace(prefabName) || string.IsNullOrWhiteSpace(biomeFolder))
                return null;

            string targetFolder = ScatterRoot + "/" + biomeFolder;
            EnsureFolder(targetFolder);
            string targetPrefab = targetFolder + "/" + prefabName + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(targetPrefab);
            if (existing != null) return existing;

            MoveIfPossible(LegacyThemedRoot + "/Mat_" + prefabName + ".mat",
                targetFolder + "/Mat_" + prefabName + ".mat");
            MoveIfPossible(LegacyThemedRoot + "/Mat_" + prefabName + "_Accent.mat",
                targetFolder + "/Mat_" + prefabName + "_Accent.mat");
            MoveIfPossible(LegacyThemedRoot + "/" + prefabName + ".prefab", targetPrefab);
            return AssetDatabase.LoadAssetAtPath<GameObject>(targetPrefab);
        }

        private static int MigrateKnownThemedPlaceholders()
        {
            int moved = 0;
            foreach (PlaceholderBinding binding in ThemedPlaceholders)
            {
                string source = LegacyThemedRoot + "/" + binding.PrefabName + ".prefab";
                bool existedAtSource = AssetDatabase.LoadMainAssetAtPath(source) != null;
                if (TryMigrateThemedPlaceholder(binding.PrefabName, binding.BiomeFolder) != null
                    && existedAtSource) moved++;
            }

            if (AssetDatabase.IsValidFolder(LegacyThemedRoot)
                && AssetDatabase.FindAssets(string.Empty, new[] { LegacyThemedRoot }).Length == 0)
                AssetDatabase.DeleteAsset(LegacyThemedRoot);
            return moved;
        }

        private static int SyncFolder(BiomeDefinition biome, string biomeFolder, string biomeKey,
            BiomeScatterCategory category)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { biomeFolder });
            var paths = new List<string>(guids.Length);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!BelongsToCategory(path, biomeFolder, category)) continue;
                paths.Add(path);
            }
            paths.Sort(StringComparer.Ordinal);

            int added = 0;
            foreach (string path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                GetDefaults(biomeKey, category, out float density, out float minScale,
                    out float maxScale);
                if (EnsureEntry(biome, category, prefab, density, minScale, maxScale)) added++;
            }
            return added;
        }

        private static bool BelongsToCategory(string assetPath, string biomeFolder,
            BiomeScatterCategory category)
        {
            if (!assetPath.StartsWith(biomeFolder + "/", StringComparison.OrdinalIgnoreCase))
                return false;
            string relative = assetPath.Substring(biomeFolder.Length + 1);
            bool enemies = relative.StartsWith("Enemies/", StringComparison.OrdinalIgnoreCase);
            bool passive = relative.StartsWith("Passive/", StringComparison.OrdinalIgnoreCase);
            bool buildings = relative.StartsWith("Buildings/", StringComparison.OrdinalIgnoreCase);
            return category switch
            {
                BiomeScatterCategory.Enemies => enemies,
                BiomeScatterCategory.Passive => passive,
                BiomeScatterCategory.Buildings => buildings,
                _ => !enemies && !passive && !buildings,
            };
        }

        private static int MigrateLegacyEnvironmentEntries(BiomeDefinition biome)
        {
            var environment = ToList(biome.scatter);
            int migrated = 0;
            for (int i = environment.Count - 1; i >= 0; i--)
            {
                GameObject prefab = environment[i].prefab;
                if (prefab == null) continue;
                BiomeScatterCategory? destination = IsPassive(prefab)
                    ? BiomeScatterCategory.Passive
                    : IsEnemy(prefab) ? BiomeScatterCategory.Enemies
                    : IsBuilding(prefab) ? BiomeScatterCategory.Buildings
                    : null;
                if (!destination.HasValue) continue;

                var target = ToList(GetEntries(biome, destination.Value));
                if (!ContainsPrefab(target, prefab)) target.Add(environment[i]);
                SetEntries(biome, destination.Value, target.ToArray());
                environment.RemoveAt(i);
                migrated++;
            }

            if (migrated > 0)
            {
                biome.scatter = environment.ToArray();
                EditorUtility.SetDirty(biome);
            }
            return migrated;
        }

        private static int RemoveNullEntries(BiomeDefinition biome, BiomeScatterCategory category)
        {
            var entries = ToList(GetEntries(biome, category));
            int before = entries.Count;
            entries.RemoveAll(entry => entry.prefab == null);
            if (entries.Count == before) return 0;
            SetEntries(biome, category, entries.ToArray());
            EditorUtility.SetDirty(biome);
            return before - entries.Count;
        }

        private static void RemovePrefabFromOtherCategories(BiomeDefinition biome,
            BiomeScatterCategory keep, GameObject prefab)
        {
            foreach (BiomeScatterCategory category in Enum.GetValues(typeof(BiomeScatterCategory)))
            {
                if (category == keep) continue;
                var entries = ToList(GetEntries(biome, category));
                int removed = entries.RemoveAll(entry => entry.prefab == prefab);
                if (removed == 0) continue;
                SetEntries(biome, category, entries.ToArray());
                EditorUtility.SetDirty(biome);
            }
        }

        private static BiomeDefinition.ScatterEntry[] GetEntries(BiomeDefinition biome,
            BiomeScatterCategory category)
        {
            return category switch
            {
                BiomeScatterCategory.Enemies => biome.enemyScatter,
                BiomeScatterCategory.Passive => biome.passiveScatter,
                BiomeScatterCategory.Buildings => biome.buildingScatter,
                _ => biome.scatter,
            };
        }

        private static void SetEntries(BiomeDefinition biome, BiomeScatterCategory category,
            BiomeDefinition.ScatterEntry[] entries)
        {
            switch (category)
            {
                case BiomeScatterCategory.Enemies: biome.enemyScatter = entries; break;
                case BiomeScatterCategory.Passive: biome.passiveScatter = entries; break;
                case BiomeScatterCategory.Buildings: biome.buildingScatter = entries; break;
                default: biome.scatter = entries; break;
            }
        }

        private static List<BiomeDefinition.ScatterEntry> ToList(
            BiomeDefinition.ScatterEntry[] entries)
            => entries != null
                ? new List<BiomeDefinition.ScatterEntry>(entries)
                : new List<BiomeDefinition.ScatterEntry>();

        private static bool ContainsPrefab(List<BiomeDefinition.ScatterEntry> entries,
            GameObject prefab)
            => entries.Exists(entry => entry.prefab == prefab);

        private static bool IsPassive(GameObject prefab)
            => prefab.GetComponentInChildren<VoxelEngine.Fauna.PassiveAnimal>(true) != null;

        private static bool IsEnemy(GameObject prefab)
        {
            MonoBehaviour[] behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null) continue;
                Type type = behaviour.GetType();
                if (type.Namespace == "VoxelEngine.Combat"
                    && type.Name.StartsWith("Enemy", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool IsBuilding(GameObject prefab)
        {
            if (prefab.name.StartsWith("Ruin_", StringComparison.OrdinalIgnoreCase)) return true;
            return prefab.GetComponentInChildren<VoxelEngine.Exploration.RuinChest>(true) != null
                || prefab.GetComponentInChildren<VoxelEngine.Exploration.RuinBlockDrop>(true) != null;
        }

        private static void GetDefaults(string biomeKey, BiomeScatterCategory category,
            out float density, out float minScale, out float maxScale)
        {
            minScale = 0.85f;
            maxScale = 1.25f;
            switch (category)
            {
                case BiomeScatterCategory.Enemies:
                    density = 0.001f;
                    minScale = 0.9f;
                    maxScale = 1.1f;
                    return;
                case BiomeScatterCategory.Passive:
                    density = 0.003f;
                    minScale = 0.9f;
                    maxScale = 1.1f;
                    return;
                case BiomeScatterCategory.Buildings:
                    density = 0.00008f;
                    minScale = 0.95f;
                    maxScale = 1.15f;
                    return;
            }

            density = biomeKey switch
            {
                "Forest" => 0.045f,
                "Beach" => 0.025f,
                "Plains" => 0.020f,
                "Steppes" => 0.012f,
                "Desert" => 0.030f,
                "WasteLand" => 0.025f,
                "Tundra" => 0.035f,
                "Mountains" => 0.025f,
                "SnowyPeaks" => 0.030f,
                "AcidBog" => 0.050f,
                "CrystalGeode" => 0.050f,
                "DesolateWastes" => 0.025f,
                "FrozenGlacier" => 0.040f,
                "GreekMarble" => 0.040f,
                "LunarHighlands" => 0.025f,
                "MartianDust" => 0.035f,
                "Ocean" => 0.035f,
                "OceanShelf" => 0.045f,
                "PirateScrap" => 0.035f,
                "VenusianAsh" => 0.035f,
                "VolcanicBasalt" => 0.040f,
                _ => 0.025f,
            };
        }

        private static string ResolveFolderKey(string biomeAssetKey)
            => biomeAssetKey.Equals("Wasteland", StringComparison.OrdinalIgnoreCase)
                ? "WasteLand"
                : biomeAssetKey;

        private static void MoveIfPossible(string source, string target)
        {
            if (AssetDatabase.LoadMainAssetAtPath(source) == null) return;
            if (AssetDatabase.LoadMainAssetAtPath(target) != null)
            {
                Debug.LogWarning("[CustomBiomeScatter] Preserved both assets because the migration target already exists: " + target);
                return;
            }
            string error = AssetDatabase.MoveAsset(source, target);
            if (!string.IsNullOrEmpty(error))
                Debug.LogWarning("[CustomBiomeScatter] Could not move '" + source + "': " + error);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
