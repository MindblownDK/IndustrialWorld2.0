// Assets/Scripts/VoxelEngine/Editor/GridContentGenerator.cs
// v1.2.2 - Fixed folder creation

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Research;

namespace VoxelEngine.Editor
{
    public static class GridContentGenerator
    {
        private const string BlockItemsPath = "Assets/Resources/GridBlocks";
        private const string ResearchPath = "Assets/Resources/Research/Grid";
        private const string RecipesPath = "Assets/Resources/Recipes/Grid";

        [MenuItem("Voxel Engine/Grid/Generate Full Grid System Content (v1.2.2)")]
        public static void GenerateEverything()
        {
            EnsureFolders();

            GenerateBlockItem<GridDrill>("Drill", 920f, 185f, "Powerful ship-mounted mining drill.");
            GenerateBlockItem<GridLandingGear>("Landing Gear", 480f, 95f, "Deploys for safe planetary landings.");
            GenerateBlockItem<GridDockingPort>("Docking Port", 410f, 82f, "Connects ships to stations or other vessels.");
            GenerateBlockItem<GridCockpit>("Cockpit", 350f, 70f, "Pilot interface for full ship control.");
            GenerateBlockItem<GridWeapon>("Weapon", 310f, 62f, "Ship-mounted combat weapon.");
            GenerateBlockItem<GridGrinder>("Grinder", 280f, 56f, "Efficient block disassembly tool.");

            GenerateResearchNode("res_grid_construction", "Grid Construction", "Unlocks all grid building technology.", 4, ResearchSubCategory.Building);
            GenerateResearchNode("res_liquid_fuel", "Liquid Fuel Processing", "Enables complex fuel production.", 5, ResearchSubCategory.Chemistry);
            GenerateResearchNode("res_ship_weapons", "Ship Armament", "Unlocks ship weapons and defense systems.", 4, ResearchSubCategory.Military);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GridContentGenerator] Full content generation complete!");
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(BlockItemsPath))
                AssetDatabase.CreateFolder("Assets/Resources", "GridBlocks");

            if (!AssetDatabase.IsValidFolder("Assets/Resources/Research"))
                AssetDatabase.CreateFolder("Assets/Resources", "Research");
            if (!AssetDatabase.IsValidFolder(ResearchPath))
                AssetDatabase.CreateFolder("Assets/Resources/Research", "Grid");

            if (!AssetDatabase.IsValidFolder(RecipesPath))
                AssetDatabase.CreateFolder("Assets/Resources/Recipes", "Grid");
        }

        private static void GenerateBlockItem<T>(string name, float largeMass, float smallMass, string description) where T : GridBlock
        {
            string safeName = name.Replace(" ", "_").Replace("/", "_");
            string path = $"{BlockItemsPath}/GBlock_{safeName}.asset";

            var item = ScriptableObject.CreateInstance<GridBlockItem>();
            item.displayName = name;
            item.blockMass = largeMass;
            item.blockHP = 650f;
            item.gridSize = GridSize.Large;
            AssetDatabase.CreateAsset(item, path);
        }

        private static void GenerateResearchNode(string id, string name, string desc, int tier, ResearchSubCategory category)
        {
            string path = $"{ResearchPath}/Research_{id}.asset";
            var node = ScriptableObject.CreateInstance<ResearchNode>();
            node.nodeId = id;
            node.displayName = name;
            node.description = desc;
            node.tier = tier;
            node.subCategory = category;
            AssetDatabase.CreateAsset(node, path);
        }
    }
}
#endif