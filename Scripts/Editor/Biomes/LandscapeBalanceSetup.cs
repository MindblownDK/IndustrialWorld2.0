#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.Biomes;

namespace IndustrialWorld.EditorTools
{
    /// <summary>Explicit, one-time scenery balance; subsequent runs preserve designer edits.</summary>
    public static class LandscapeBalanceSetup
    {
        private const string Marker = "IndustrialWorld.LandscapeBalance.14.74.1";

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Landscape Balance", "Exit Play Mode before balancing scenery.", "OK");
                return;
            }
            const string root = "Assets/VoxelEngineAssets/Biomes";
            if (!AssetDatabase.IsValidFolder(root))
            {
                EditorUtility.DisplayDialog("Landscape Balance", "Build base and celestial biomes first.", "OK");
                return;
            }
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:BiomeDefinition", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var biome = AssetDatabase.LoadAssetAtPath<BiomeDefinition>(path);
                var importer = AssetImporter.GetAtPath(path);
                if (biome == null || importer == null || (importer.userData ?? "").Contains(Marker)) continue;
                Undo.RecordObject(biome, "Balance landscape coverage");
                if (biome.scatter != null)
                {
                    for (int i = 0; i < biome.scatter.Length; i++)
                    {
                        var entry = biome.scatter[i];
                        // Zero is explicitly disabled. Tiny positive values are restored by request.
                        if (entry.prefab == null || entry.density <= 0f) continue;
                        if (entry.prefab.GetComponentInChildren<VoxelEngine.Exploration.RuinChest>(true) != null
                            || entry.prefab.GetComponentInChildren<VoxelEngine.Exploration.RuinBlockDrop>(true) != null
                            || entry.prefab.GetComponentInChildren<VoxelEngine.Fauna.PassiveAnimal>(true) != null
                            || entry.prefab.name.StartsWith("Ruin_", System.StringComparison.OrdinalIgnoreCase)) continue;
                        bool enemy = false;
                        foreach (var behaviour in entry.prefab.GetComponentsInChildren<MonoBehaviour>(true))
                        {
                            if (behaviour == null) continue;
                            var type = behaviour.GetType();
                            if (type.Namespace == "VoxelEngine.Combat" && type.Name.StartsWith("Enemy"))
                            { enemy = true; break; }
                        }
                        if (enemy) continue;
                        bool tree = entry.prefab.GetComponentInChildren<VoxelEngine.Trees.Tree>(true) != null
                            || entry.prefab.name.ToLowerInvariant().Contains("tree")
                            || entry.prefab.name.ToLowerInvariant().Contains("palm")
                            || entry.prefab.name.ToLowerInvariant().Contains("cypress");
                        float floor = tree ? 0.07f : 0.10f;
                        string key = biome.name.ToLowerInvariant();
                        if (key.Contains("lunar") || key.Contains("desolate")) floor *= 0.55f;
                        else if (key.Contains("forest") || key.Contains("acid") || key.Contains("ocean")) floor *= 1.3f;
                        entry.density = entry.prefab.name.IndexOf("palm", System.StringComparison.OrdinalIgnoreCase) >= 0
                            ? Mathf.Min(entry.density, 0.001f) : Mathf.Max(entry.density, floor);
                        biome.scatter[i] = entry;
                    }
                }
                EditorUtility.SetDirty(biome);
                // A versioned authoring marker, not a save field; do not replace unrelated metadata.
                importer.userData = (importer.userData ?? "") + "\n" + Marker;
                EditorUtility.SetDirty(importer);
                AssetDatabase.WriteImportSettingsIfDirty(path);
                changed++;
            }
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Landscape Balance", "Balanced " + changed
                + " biomes. Zero-density entries, creature tuning, scales and heights were preserved.\n"
                + "Future runs preserve subsequent edits. Ruin rarity is handled at runtime, including legacy scatter entries.", "OK");
        }
    }
}
#endif
