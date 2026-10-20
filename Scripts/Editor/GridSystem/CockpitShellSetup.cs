#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.EditorTools
{
    /// <summary>Non-destructive, editor-only refresh for the authored cockpit shell subtree.</summary>
    public static class CockpitShellSetup
    {
        private const string AssetRoot = "Assets/VoxelEngineAssets";
        private const string PrefabFolder = AssetRoot + "/GridSystem/Prefabs";
        private const string PrefabPath = PrefabFolder + "/Cockpit_Large.prefab";
        private const string MaterialFolder = PrefabFolder + "/Mats/CockpitShell_17_41";

        public static void RunStep123()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Cockpit Shell Setup", "Exit Play Mode before running setup.", "OK");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                EditorUtility.DisplayDialog("Cockpit Shell Setup",
                    "Cockpit_Large.prefab was not found. Run the grid ship and vehicle setup first; this step only refreshes its generated shell subtree.",
                    "OK");
                return;
            }

            EnsureFolder(MaterialFolder);
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                EditorUtility.DisplayDialog("Cockpit Shell Setup", "Could not open Cockpit_Large.prefab. No content was changed.", "OK");
                return;
            }
            var cockpit = root.GetComponent<GridCockpit>();
            if (cockpit == null)
            {
                PrefabUtility.UnloadPrefabContents(root);
                EditorUtility.DisplayDialog("Cockpit Shell Setup",
                    "Cockpit_Large.prefab has no GridCockpit component. No content was changed.", "OK");
                return;
            }

            int style = cockpit.shellStyle == CockpitShellBuilder.StyleLegacy
                ? CockpitShellBuilder.StyleCrawler
                : cockpit.shellStyle;
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(AssetRoot + "/GridSystem/Items/GItem_Cockpit.asset");
            Color bodyColor = item != null ? item.iconTint : new Color(0.20f, 0.40f, 0.80f);
            int materialIndex = 0;
            var previousMaterialPersister = GridBlockMeshBuilder.MaterialPersister;

            GridBlockMeshBuilder.MaterialPersister = (material, _) =>
            {
                if (material == null) return null;
                string path = MaterialFolder + "/CockpitShell_17_41_" + materialIndex.ToString("D2") + ".mat";
                materialIndex++;
                Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null)
                {
                    Object.DestroyImmediate(material);
                    return existing;
                }

                material.name = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(material, path);
                return AssetDatabase.LoadAssetAtPath<Material>(path);
            };

            try
            {
                // Build replaces only Cabin, Shell_Crawler, Shell_Cab and SeatAnchor
                // inside Generated_CockpitShell. Other prefab children and root tuning remain.
                CockpitShellBuilder.Build(root, GridSize.Large.CellSize(), bodyColor, style);
                EditorUtility.SetDirty(cockpit);
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (savedPrefab == null)
                {
                    EditorUtility.DisplayDialog("Cockpit Shell Setup", "Unity could not save Cockpit_Large.prefab. Check the Console before retrying.", "OK");
                    return;
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[VoxelEngineSetupWindow] Cockpit Shell Setup complete — generated cockpit visuals refreshed; root component values and unrelated prefab children were preserved.");
                EditorUtility.DisplayDialog("Voxel Engine — Cockpit Shell Setup",
                    "Cockpit shells refreshed. The wraparound cockpit now has clearer glass and a floating three-screen console; the Armored Cab has a solid lower body and increased runtime integrity. Existing cockpit power, health item values, recipes and custom prefab children were preserved.",
                    "OK");
            }
            finally
            {
                GridBlockMeshBuilder.MaterialPersister = previousMaterialPersister;
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash <= 0) return;
            string parent = path.Substring(0, slash);
            string name = path.Substring(slash + 1);
            EnsureFolder(parent);
            if (AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
