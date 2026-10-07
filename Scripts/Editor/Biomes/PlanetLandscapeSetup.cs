#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.Biomes;

namespace IndustrialWorld.EditorTools
{
    public static class PlanetLandscapeSetup
    {
        private const string Marker = "IndustrialWorld.PlanetLandscape.15.0.0";

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
                + "Terrain generation changed. Use a NEW world for 15.0.0-dev.", "OK");
        }
    }
}
#endif
