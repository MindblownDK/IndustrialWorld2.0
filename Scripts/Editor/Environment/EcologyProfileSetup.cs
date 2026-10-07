#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Cosmos;
using IndustrialWorld.Simulation;

namespace IndustrialWorld.EditorTools
{
    public static class EcologyProfileSetup
    {
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Planet Ecology", "Exit Play Mode before running setup.", "OK");
                return;
            }
            const string folder = "Assets/Resources/Ecology";
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/Resources", "Ecology");
            int created = 0;
            foreach (PlanetSkyKind kind in Enum.GetValues(typeof(PlanetSkyKind)))
            {
                string path = folder + "/Profile_" + kind + ".asset";
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) continue;
                var profile = ScriptableObject.CreateInstance<EcologyProfile>();
                profile.nativeAbundance = kind switch
                {
                    PlanetSkyKind.Olympus => 1.30f,
                    PlanetSkyKind.Ocean or PlanetSkyKind.Water => 1.15f,
                    PlanetSkyKind.Temperate => 1f,
                    PlanetSkyKind.Acid => 0.85f,
                    PlanetSkyKind.Pirate => 0.48f,
                    PlanetSkyKind.Crystal => 0.34f,
                    PlanetSkyKind.Ice => 0.20f,
                    _ => 0f
                };
                profile.pollutionSensitivity = kind switch
                {
                    PlanetSkyKind.Acid => 0.68f,
                    PlanetSkyKind.Crystal => 0.76f,
                    PlanetSkyKind.Ice => 0.88f,
                    _ => 1f
                };
                profile.allowsConventionalLivestock = kind == PlanetSkyKind.Temperate
                    || kind == PlanetSkyKind.Ocean || kind == PlanetSkyKind.Water
                    || kind == PlanetSkyKind.Olympus || kind == PlanetSkyKind.Pirate;
                AssetDatabase.CreateAsset(profile, path);
                created++;
            }
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Planet Ecology",
                "Created " + created + " missing profiles. Existing profiles and tuning were preserved.\n"
                + "Runtime systems resolve these profiles automatically from each body's theme.", "OK");
        }
    }
}
#endif
