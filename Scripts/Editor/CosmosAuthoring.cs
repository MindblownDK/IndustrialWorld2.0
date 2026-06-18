// Assets/Scripts/VoxelEngine/Editor/CosmosAuthoring.cs
//
// One-click authoring for the new Cosmos templates. Seeds an Earth planet asset from
// BodySettings.CreateEarthlike() so you can start playing immediately, then customise.
using UnityEditor;
using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.EditorTools
{
    public static class CosmosAuthoring
    {
        private const string PlanetsDir = "Assets/VoxelEngineAssets/Planets";

        [MenuItem("Tools/Voxel Engine/Author Earth Planet Template")]
        public static void AuthorEarthTemplate()
        {
            EnsureFolder(PlanetsDir);

            const string path = PlanetsDir + "/Planet_Earth.asset";
            var existing = AssetDatabase.LoadAssetAtPath<PlanetTemplate>(path);

            var planet = existing != null ? existing : ScriptableObject.CreateInstance<PlanetTemplate>();
            planet.name = "Planet_Earth";
            planet.body = BodySettings.CreateEarthlike();
            planet.orbitalDistanceKm = new Vector2(2500f, 4000f);
            planet.orbitSpeed = 0.6f;

            if (existing == null)
                AssetDatabase.CreateAsset(planet, path);
            else
                EditorUtility.SetDirty(planet);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = planet;
            Debug.Log("[Cosmos] Earth planet template authored at " + path +
                      " (gravity 1g, oxygen, grass, full ore catalogue incl. Lithium).");
        }

        [MenuItem("Tools/Voxel Engine/Create Solar System (Sol)")]
        public static void AuthorSolSystem()
        {
            EnsureFolder(PlanetsDir);
            const string path = PlanetsDir + "/System_Sol.asset";

            var sys = AssetDatabase.LoadAssetAtPath<SolarSystemTemplate>(path);
            if (sys == null)
            {
                sys = ScriptableObject.CreateInstance<SolarSystemTemplate>();
                AssetDatabase.CreateAsset(sys, path);
            }
            sys.name = "System_Sol";
            sys.systemName = "Sol System";
            sys.sun = new SunSettings { displayName = "Sol", sunCount = 1, intensity = 1.3f };
            sys.minPlanetSeparationKm = 500f;
            sys.maxPlanetSeparationKm = 10000f;
            sys.quasar = new QuasarSettings { enabled = true, brightness = 1.4f };

            EditorUtility.SetDirty(sys);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = sys;
            Debug.Log("[Cosmos] Sol system template created at " + path +
                      ". Assign your Planet_Earth to its 'planets' list to embed it.");
        }

        private static void EnsureFolder(string assetPath)
        {
            // assetPath like "Assets/VoxelEngineAssets/Planets"
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
