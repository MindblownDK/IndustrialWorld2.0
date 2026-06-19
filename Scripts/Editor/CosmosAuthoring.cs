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

        /// <summary>
        /// Phase 3: update biome surface materials to use proper GRASS (green) instead of Clay
        /// (brown) for grass-like biomes. This is the key fix for "incredibly ugly" terrain —
        /// Plains/Forest were using Clay (brown dirt) as their surface, making the world look
        /// barren. Now they use Grass (natural green). Also ensures Desert = Sand, etc.
        /// </summary>
        [MenuItem("Tools/Voxel Engine/Normalize Biome Surface Materials (Grass)")]
        public static void NormalizeBiomeSurfaces()
        {
            // Find all biome assets in the project.
            string[] guids = AssetDatabase.FindAssets("t:BiomeDefinition");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[Cosmos] No BiomeDefinition assets found.");
                return;
            }
            int fixedCount = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var biome = AssetDatabase.LoadAssetAtPath<VoxelEngine.Biomes.BiomeDefinition>(path);
                if (biome == null) continue;

                bool changed = false;
                string name = biome.biomeName.ToLowerInvariant();

                // Grass biomes (Plains, Forest, Steppes, Tundra grass) → Grass material.
                if (name.Contains("plains") || name.Contains("forest") || name.Contains("steppe"))
                {
                    if (biome.surfaceMaterial != VoxelEngine.Materials.MaterialId.Grass)
                    {
                        biome.surfaceMaterial = VoxelEngine.Materials.MaterialId.Grass;
                        changed = true;
                    }
                }
                // Desert/Wasteland → Sand surface.
                if (name.Contains("desert") || name.Contains("wasteland"))
                {
                    if (biome.surfaceMaterial != VoxelEngine.Materials.MaterialId.Sand)
                    {
                        biome.surfaceMaterial = VoxelEngine.Materials.MaterialId.Sand;
                        biome.subsurfaceMaterial = VoxelEngine.Materials.MaterialId.Sand;
                        changed = true;
                    }
                }
                // Tundra → keep Clay (frozen dirt look) but make it lighter.
                // Beach → Sand (should already be).
                // Mountains → Stone (should already be).
                // SnowyPeaks → Ice (should already be).

                if (changed)
                {
                    EditorUtility.SetDirty(biome);
                    fixedCount++;
                    Debug.Log("[Cosmos] " + biome.biomeName + ": surface -> " + biome.surfaceMaterial);
                }
            }
            if (fixedCount > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[Cosmos] Updated " + fixedCount + " biome(s) to use proper surface materials (Grass for plains/forest, Sand for desert).");
            }
            else
            {
                Debug.Log("[Cosmos] All biome surfaces already correct.");
            }
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

            // Auto-attach the Earth planet if it exists, so a fresh system is immediately playable.
            var earth = AssetDatabase.LoadAssetAtPath<PlanetTemplate>(PlanetsDir + "/Planet_Earth.asset");
            if (earth != null)
            {
                sys.planets = new PlanetTemplate[] { earth };
            }

            EditorUtility.SetDirty(sys);

            // Ensure the runtime library (Resources) knows about this system so the main-menu
            // system picker can list it.
            EnsureLibraryRegistered(sys);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = sys;
            Debug.Log("[Cosmos] Sol system template created at " + path +
                      " (Earth auto-attached if present, CosmosTemplateLibrary updated).");
        }

        /// <summary>
        /// Make sure Resources/CosmosTemplateLibrary.asset exists and contains the given system,
        /// so the main-menu New World page can offer it in the solar-system picker.
        /// </summary>
        private static void EnsureLibraryRegistered(SolarSystemTemplate system)
        {
            if (system == null) return;
            const string libDir  = "Assets/Resources";
            const string libPath = libDir + "/CosmosTemplateLibrary.asset";
            EnsureFolder(libDir);

            var library = AssetDatabase.LoadAssetAtPath<CosmosTemplateLibrary>(libPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CosmosTemplateLibrary>();
                AssetDatabase.CreateAsset(library, libPath);
            }

            if (library.systems == null) library.systems = new System.Collections.Generic.List<SolarSystemTemplate>();
            if (!library.systems.Contains(system))
                library.systems.Add(system);

            EditorUtility.SetDirty(library);
            // Invalidate the runtime cache so the next Load() picks up the updated asset.
            CosmosTemplateLibrary.InvalidateCache();
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
