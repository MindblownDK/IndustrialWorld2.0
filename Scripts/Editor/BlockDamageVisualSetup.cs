// Assets/Scripts/VoxelEngine/Editor/BlockDamageVisualSetup.cs
//
// Step 62: VISIBLE BLOCK DAMAGE, THRUSTER PLUME HAZARD & SUIT TEMPERATURE — non-destructive.
//
//   • Creates the Resources template material for the BlockDamageOverlayURP shader so
//     the procedural crack/scorch/glow shell survives standalone builds (the runtime
//     clones it; Shader.Find is only a fallback). If the material exists, only a broken
//     shader link is repaired — authored property tweaks are preserved.
//   • Re-verifies that every grid prefab carries GridThermalSystem (Step 61 did this;
//     re-running here covers prefabs authored since).
//   • Everything else in 9.30.0 is runtime-only: BlockDamageVisual, PlacedBlockHeat,
//     ThrusterPlumeHazard and PlayerSuitThermal attach themselves on demand.
//   • Fully re-runnable: nothing is deleted, nothing authored is overwritten.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Thermal;

namespace VoxelEngine.EditorTools
{
    public static class BlockDamageVisualSetup
    {
        private const string RuntimeResourceFolder = "Assets/Resources/VoxelEngineRuntime";
        private const string OverlayMaterialName = "BlockDamageOverlay";
        private const string OverlayShaderName = "VoxelEngine/BlockDamageOverlayURP";
        private const string GridPrefabs = "Assets/VoxelEngineAssets/GridSystem/Prefabs";

        public static void RunStep62()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 62 — Visible block damage, plume hazard & suit temperature started.");

            EnsureFolder(RuntimeResourceFolder);
            string materialStatus = EnsureRuntimeMaterial(OverlayMaterialName, OverlayShaderName);

            // ── Re-verify thermal wiring on grid prefabs (idempotent) ─────────
            int gridsWired = 0, gridsPreserved = 0;
            if (AssetDatabase.IsValidFolder(GridPrefabs))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { GridPrefabs }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (asset == null || asset.GetComponent<GridEntity>() == null) continue;
                    if (asset.GetComponent<GridThermalSystem>() != null) { gridsPreserved++; continue; }

                    var contents = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        if (contents.GetComponent<GridThermalSystem>() == null)
                        {
                            contents.AddComponent<GridThermalSystem>();
                            PrefabUtility.SaveAsPrefabAsset(contents, path);
                            gridsWired++;
                        }
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[VoxelEngineSetupWindow] Step 62 — overlay material: {materialStatus}; grid prefabs wired: {gridsWired} (preserved {gridsPreserved}).");

            EditorUtility.DisplayDialog("Voxel Engine — Visible Damage, Plume Hazard & Suit Temperature (Step 62)",
                "Visible damage system authored.\n\n" +
                "• Damage overlay material: " + materialStatus + "\n" +
                "• Grid prefabs given thermal simulation: " + gridsWired + " (" + gridsPreserved + " already wired)\n\n" +
                "Runtime (no assets needed):\n" +
                "• Every block now shows cracks as it loses HP, soot after it has been hot, and an\n" +
                "  incandescent red-orange-white glow with temperature, plus smoke and embers while burning.\n" +
                "• Thruster plumes heat and damage anything in the exhaust cone: the ship's own hull,\n" +
                "  other grids, placed base blocks, building pieces, creatures and the player.\n" +
                "• Hull cooling is now slow (steel holds heat for minutes) and the suit has a real\n" +
                "  temperature with inertia, shown as the TMP strip in Suit Status.\n\n" +
                "Existing balance values, block HP and power draws were preserved.",
                "OK");

            Debug.Log("[VoxelEngineSetupWindow] Step 62 complete.");
        }

        private static string EnsureRuntimeMaterial(string assetName, string shaderName)
        {
            string path = RuntimeResourceFolder + "/" + assetName + ".mat";
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError("[VoxelEngineSetup] Required shader '" + shaderName + "' is missing; '" + assetName + "' was not changed. Make sure Scripts/Rendering/BlockDamageOverlayURP.shader compiled.");
                return "ERROR — shader missing";
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(material, path);
                return "created";
            }

            if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
                return "shader link repaired";
            }

            return "preserved";
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
#endif
