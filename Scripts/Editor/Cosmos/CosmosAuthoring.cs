// Assets/Scripts/Editor/Cosmos/CosmosAuthoring.cs
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

        public static void AuthorEarthTemplate()
        {
            EnsureFolder(PlanetsDir);

            const string path = PlanetsDir + "/Planet_Earth.asset";
            var existing = AssetDatabase.LoadAssetAtPath<PlanetTemplate>(path);

            bool isNew = existing == null;
            var planet = isNew ? ScriptableObject.CreateInstance<PlanetTemplate>() : existing;
            if (isNew || string.IsNullOrWhiteSpace(planet.name)) planet.name = "Planet_Earth";
            if (planet.body == null) planet.body = BodySettings.CreateEarthlike();
            if (planet.orbitalDistanceKm == Vector2.zero)
                planet.orbitalDistanceKm = new Vector2(2500f, 4000f);
            if (planet.orbitSpeed <= 0f) planet.orbitSpeed = 0.6f;

            if (isNew)
                AssetDatabase.CreateAsset(planet, path);
            else
                EditorUtility.SetDirty(planet);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = planet;
            Debug.Log("[Cosmos] Earth planet template created/repaired at " + path +
                      "; existing authored body and orbit tuning was preserved.");
        }

        /// <summary>
        /// Phase 3: update biome surface materials to use proper GRASS (green) instead of Clay
        /// (brown) for grass-like biomes. This is the key fix for "incredibly ugly" terrain —
        /// Plains/Forest were using Clay (brown dirt) as their surface, making the world look
        /// barren. Now they use Grass (natural green). Also ensures Desert = Sand, etc.
        /// </summary>
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
