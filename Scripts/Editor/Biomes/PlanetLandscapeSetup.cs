#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.Biomes;
using VoxelEngine.Cosmos;
using System.Collections.Generic;

namespace IndustrialWorld.EditorTools
{
    public static class PlanetLandscapeSetup
    {
        private const string Marker = "IndustrialWorld.PlanetLandscape.16.0.0";

        private static void EnsureTemperateBiomes(string root)
        {
            string taigaPath = root + "/Biome_Taiga.asset";
            var taiga = AssetDatabase.LoadAssetAtPath<BiomeDefinition>(taigaPath);
            if (taiga == null)
            {
                var tundra = AssetDatabase.LoadAssetAtPath<BiomeDefinition>(root + "/Biome_Tundra.asset");
                taiga = tundra != null ? Object.Instantiate(tundra) : ScriptableObject.CreateInstance<BiomeDefinition>();
                taiga.name = "Biome_Taiga"; taiga.biomeName = "Taiga";
                taiga.minTemperature = 0.12f; taiga.maxTemperature = 0.43f;
                taiga.minHumidity = 0.32f; taiga.maxHumidity = 0.85f;
                taiga.surfaceMaterial = VoxelEngine.Materials.MaterialId.Grass;
                taiga.subsurfaceMaterial = VoxelEngine.Materials.MaterialId.Clay;
                AssetDatabase.CreateAsset(taiga, taigaPath);
            }
            var requiredBiomes = new List<BiomeDefinition>();
            foreach (string name in new[] { "Plains", "Forest", "Taiga", "Tundra", "Desert", "Beach" })
            {
                var biome = AssetDatabase.LoadAssetAtPath<BiomeDefinition>(root + "/Biome_" + name + ".asset");
                if (biome != null) requiredBiomes.Add(biome);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:BiomeRegistry"))
            {
                var registry = AssetDatabase.LoadAssetAtPath<BiomeRegistry>(AssetDatabase.GUIDToAssetPath(guid));
                if (registry == null) continue;
                if (registry.biomes == null) registry.biomes = new List<BiomeDefinition>();
                // Only augment registries already containing temperate biomes.
                if (!registry.biomes.Exists(b => b != null && (b.biomeName == "Plains" || b.biomeName == "Forest"))) continue;
                Undo.RecordObject(registry, "Connect temperate biomes");
                foreach(var biome in requiredBiomes) if (!registry.biomes.Contains(biome)) registry.biomes.Add(biome);
                EditorUtility.SetDirty(registry);
            }
            foreach(string guid in AssetDatabase.FindAssets("t:PlanetTemplate"))
            {
                var planet=AssetDatabase.LoadAssetAtPath<PlanetTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if(planet == null || planet.body == null || (planet.body.bodyName != "Earth" && planet.name != "Planet_Earth")) continue;
                // Empty allowed list already means use the registry. Preserve that convention.
                if(planet.body.allowedBiomes == null || planet.body.allowedBiomes.Length == 0) continue;
                Undo.RecordObject(planet,"Connect Earth biome variety");
                var allowed = new List<BiomeDefinition>(planet.body.allowedBiomes);
                foreach(var biome in requiredBiomes) if(!allowed.Contains(biome)) allowed.Add(biome);
                planet.body.allowedBiomes=allowed.ToArray();EditorUtility.SetDirty(planet);
            }
        }

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Planet Landscape", "Exit Play Mode before running setup.", "OK");
                return;
            }
            const string root = "Assets/VoxelEngineAssets/Biomes";
            if (!AssetDatabase.IsValidFolder(root))
            {
                EditorUtility.DisplayDialog("Planet Landscape", "Build the base and celestial biomes first.", "OK");
                return;
            }
            EnsureTemperateBiomes(root);
            // Reconnect actual prefab assets, including stale desert object references.
            // The synchronizer removes missing entries and resolves live folder assets.
            VoxelEngine.EditorTools.CustomBiomeScatterSetup.Run();
            int updated = 0;
            int emptyDeserts = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:BiomeDefinition", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var biome = AssetDatabase.LoadAssetAtPath<BiomeDefinition>(path);
                var importer = AssetImporter.GetAtPath(path);
                if (biome == null || importer == null) continue;
                bool desert = biome.name.IndexOf("desert", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (desert && (biome.scatter == null || biome.scatter.Length == 0)) emptyDeserts++;
                if ((importer.userData ?? "").Contains(Marker)) continue;
                Undo.RecordObject(biome, "Repair planet landscape balance");
                if (biome.scatter != null)
                {
                    for (int i = 0; i < biome.scatter.Length; i++)
                    {
                        var entry = biome.scatter[i];
                        if (entry.prefab == null || entry.density <= 0f) continue;
                        if (entry.prefab.name.IndexOf("palm", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            entry.density = Mathf.Min(entry.density, 0.001f);
                        else if (desert)
                            entry.density = Mathf.Max(entry.density, 0.025f);
                        biome.scatter[i] = entry;
                    }
                }
                EditorUtility.SetDirty(biome);
                importer.userData = (importer.userData ?? "") + "\n" + Marker;
                EditorUtility.SetDirty(importer);
                AssetDatabase.WriteImportSettingsIfDirty(path);
                updated++;
            }
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Planet Landscape", "Updated " + updated
                + " biomes. Existing scales, terrain settings, disabled entries and creature tuning were preserved.\n"
                + "Desert biomes without scenery: " + emptyDeserts
                + ". If nonzero, add your desert prefabs to Assets/VoxelEngineAssets/Scatter/Desert and rerun.\n"
                + "Terrain generation changed. Use a NEW world for 16.0.0-dev.", "OK");
        }
    }
}
#endif
