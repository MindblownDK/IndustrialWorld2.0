// Assets/Scripts/VoxelEngine/Editor/GridBlockItemGenerator.cs
//
// Editor tool to automatically generate all GridBlockItem ScriptableObjects
// for the new grid blocks (Phase 2+). Run from menu to avoid manual mistakes.
// Uses the VoxelEngine setup and follows production quality standards.

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Editor
{
    public static class GridBlockItemGenerator
    {
        private const string OutputPath = "Assets/Resources/GridBlocks";

        [MenuItem("Voxel Engine/Grid/Generate All Grid Block Items")]
        public static void GenerateAllGridBlockItems()
        {
            if (!AssetDatabase.IsValidFolder(OutputPath))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "GridBlocks");
            }

            // Generate items for all new and existing grid blocks
            GenerateGridBlockItem<GridArmorBlock>("Armor Block", 250f, 800f, GridSize.Large, new Color(0.4f, 0.4f, 0.45f));
            GenerateGridBlockItem<GridGlassBlock>("Glass Block", 40f, 120f, GridSize.Large, new Color(0.7f, 0.85f, 1f, 0.6f));
            GenerateGridBlockItem<GridH2O2Generator>("H2/O2 Generator", 180f, 300f, GridSize.Large, new Color(0.2f, 0.6f, 0.9f));
            GenerateGridBlockItem<GridWaterTank>("Water Tank", 220f, 400f, GridSize.Large, new Color(0.3f, 0.6f, 0.9f));
            GenerateGridBlockItem<GridLiquidFuelTank>("Liquid Fuel Tank", 200f, 350f, GridSize.Large, new Color(0.9f, 0.5f, 0.1f));
            GenerateGridBlockItem<GridItemPipe>("Item Pipe", 80f, 150f, GridSize.Large, new Color(0.5f, 0.5f, 0.6f));
            GenerateGridBlockItem<GridRefinery>("Refinery", 450f, 600f, GridSize.Large, new Color(0.6f, 0.4f, 0.2f));

            // Existing blocks (regenerate for consistency)
            GenerateGridBlockItem<GridPortableReactor>("Portable Reactor", 350f, 500f, GridSize.Large, new Color(0.8f, 0.3f, 0.3f));
            GenerateGridBlockItem<GridSolarPanel>("Solar Panel", 120f, 200f, GridSize.Large, new Color(0.3f, 0.7f, 0.3f));
            GenerateGridBlockItem<GridBattery>("Battery", 280f, 450f, GridSize.Large, new Color(0.4f, 0.6f, 0.9f));
            GenerateGridBlockItem<GridGasTank>("Gas Tank", 150f, 250f, GridSize.Large, new Color(0.2f, 0.8f, 0.6f));
            GenerateGridBlockItem<GridCargoContainer>("Cargo Container", 180f, 300f, GridSize.Large, new Color(0.5f, 0.45f, 0.4f));
            GenerateGridBlockItem<GridThruster>("Atmospheric Thruster", 160f, 280f, GridSize.Large, new Color(1f, 0.6f, 0.2f));
            GenerateGridBlockItem<GridThruster>("Hydrogen Thruster", 170f, 290f, GridSize.Large, new Color(0.3f, 0.6f, 1f));
            GenerateGridBlockItem<GridThruster>("Ion Thruster", 140f, 240f, GridSize.Large, new Color(0.5f, 0.3f, 1f));
            GenerateGridBlockItem<GridThruster>("Liquid Fuel Thruster", 190f, 320f, GridSize.Large, new Color(1f, 0.4f, 0.1f));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[GridBlockItemGenerator] All Grid Block Items generated successfully in " + OutputPath);
        }

        private static void GenerateGridBlockItem<T>(string displayName, float mass, float hp, GridSize size, Color tint) where T : GridBlock
        {
            string fileName = $"GBlock_{displayName.Replace(" ", "_").Replace("/", "_")}";
            string path = $"{OutputPath}/{fileName}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<GridBlockItem>(path);
            if (existing != null)
            {
                // Update existing
                existing.displayName = displayName;
                existing.blockMass = mass;
                existing.blockHP = hp;
                existing.gridSize = size;
                existing.iconTint = tint;
                EditorUtility.SetDirty(existing);
                return;
            }

            var item = ScriptableObject.CreateInstance<GridBlockItem>();
            item.displayName = displayName;
            item.blockMass = mass;
            item.blockHP = hp;
            item.gridSize = size;
            item.iconTint = tint;
            item.blockPrefab = null; // User can assign custom prefab later

            AssetDatabase.CreateAsset(item, path);
        }
    }
}
#endif
