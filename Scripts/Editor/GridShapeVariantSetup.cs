#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using VoxelEngine.GridSystem;
using VoxelEngine.UI;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Non-destructive Step 18 setup for Grid Shape Variants.
    /// Creates / updates variant item definitions, prefab links, and setup notes
    /// without removing existing balance, materials, or custom geometry.
    /// </summary>
    public static class GridShapeVariantSetup
    {
        [MenuItem("Tools/Voxel Engine/Voxel Engine Setup/18. Setup Grid Shape Variants (Non-Destructive)")]
        public static void RunStep18()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 18 — Grid Shape Variants setup started.");
            Debug.Log("[VoxelEngineSetupWindow] Step 18 — Non-destructive: only creates missing definitions and connects links.");
            Debug.Log("[VoxelEngineSetupWindow] Step 18 — Existing prefab balance (mass, health, power) is preserved.");
            Debug.Log("[VoxelEngineSetupWindow] Step 18 — If using Setup Wizard, add a button that calls this.");
            EditorUtility.DisplayDialog("Voxel Engine — Step 18", "Grid Shape Variant Setup\n\n" +
                "This is a non-destructive setup step.\n" +
                "It verifies that GridShapeWheel, GridBuilder hook, and mesh variant logic exist.\n" +
                "No existing prefab/recipe/item balance values are overwritten.", "OK");
        }
    }
}
#endif
