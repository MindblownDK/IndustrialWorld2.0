// Assets/Scripts/VoxelEngine/Editor/GridBlockItemGenerator.cs
//
// Editor tool to auto-generate all GridBlockItem ScriptableObjects.

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Editor
{
    public static class GridBlockItemGenerator
    {
        private const string OutputPath = "Assets/Resources/GridBlocks";

        [MenuItem("Voxel Engine/Grid/Generate All Grid Block Items (v1.1.0)")]
        public static void GenerateAll()
        {
            if (!AssetDatabase.IsValidFolder(OutputPath))
                AssetDatabase.CreateFolder("Assets/Resources", "GridBlocks");

            // Generate all blocks
            Generate<GridArmorBlock>("Armor Block", 250f, 800f);
            Generate<GridGlassBlock>("Glass Block", 40f, 120f);
            Generate<GridH2O2Generator>("H2O2 Generator", 180f, 300f);
            Generate<GridWaterTank>("Water Tank", 220f, 400f);
            Generate<GridLiquidFuelTank>("Liquid Fuel Tank", 200f, 350f);
            Generate<GridItemPipe>("Item Pipe", 80f, 150f);
            Generate<GridRefinery>("Refinery", 450f, 600f);
            Generate<GridChemicalPlant>("Chemical Plant", 380f, 550f);
            Generate<GridGrinder>("Grinder", 160f, 280f);
            Generate<GridWeapon>("Weapon", 140f, 250f);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GridBlockItemGenerator] All items generated successfully.");
        }

        private static void Generate<T>(string name, float mass, float hp) where T : GridBlock
        {
            string path = $"{OutputPath}/GBlock_{name.Replace(" ", "_")}.asset";
            var item = ScriptableObject.CreateInstance<GridBlockItem>();
            item.displayName = name;
            item.blockMass = mass;
            item.blockHP = hp;
            AssetDatabase.CreateAsset(item, path);
        }
    }
}
#endif
