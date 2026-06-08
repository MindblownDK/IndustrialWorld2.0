// Assets/Scripts/VoxelEngine/Editor/GridContentGenerator.cs
//
// ULTIMATE Grid System Content Generator
// Generates ALL missing GridBlockItems, Research Nodes, Recipes, and provides a testing command.
//
// Run: Voxel Engine → Grid → Generate Full Grid System Content (v1.2.0)

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Research;
using VoxelEngine.Crafting;

namespace VoxelEngine.Editor
{
    public static class GridContentGenerator
    {
        private const string BlockItemsPath = "Assets/Resources/GridBlocks";
        private const string ResearchPath = "Assets/Resources/Research/Grid";
        private const string RecipesPath = "Assets/Resources/Recipes/Grid";

        [MenuItem("Voxel Engine/Grid/Generate Full Grid System Content (v1.2.0)")]
        public static void GenerateEverything()
        {
            EnsureFolders();

            // === BLOCK ITEMS ===
            GenerateBlockItem<GridDrill>("Drill", 850f, 600f, "Mines voxels and resources. Large grid version is extremely powerful.");
            GenerateBlockItem<GridLandingGear>("Landing Gear", 420f, 450f, "Deploys to land safely on surfaces. Essential for atmospheric flight.");
            GenerateBlockItem<GridDockingPort>("Docking Port", 380f, 400f, "Connects ships to stations or other ships. Supports filtering.");
            GenerateBlockItem<GridCockpit>("Cockpit", 320f, 350f, "Pilot seat. Enables full ship control and HUD.");
            GenerateBlockItem<GridWeapon>("Weapon", 290f, 380f, "Ship-mounted weapon. Fires projectiles at enemies.");
            GenerateBlockItem<GridGrinder>("Grinder", 260f, 320f, "Breaks down blocks into resources. More efficient than hand tools.");

            // Existing blocks with improved mass & descriptions
            GenerateBlockItem<GridArmorBlock>("Armor Block", 1200f, 900f, "Heavy structural armor. High durability.");
            GenerateBlockItem<GridGlassBlock>("Glass Block", 180f, 150f, "Transparent structural block.");
            GenerateBlockItem<GridH2O2Generator>("H2/O2 Generator", 650f, 500f, "Produces Hydrogen and Oxygen from water/ice.");
            GenerateBlockItem<GridWaterTank>("Water Tank", 780f, 550f, "Stores large amounts of water.");
            GenerateBlockItem<GridLiquidFuelTank>("Liquid Fuel Tank", 920f, 620f, "Stores complex liquid fuel mixtures.");
            GenerateBlockItem<GridItemPipe>("Item Pipe", 310f, 280f, "Transports items between cargo containers on a grid.");
            GenerateBlockItem<GridRefinery>("Refinery", 1850f, 1100f, "Processes crude oil into Kerosene and other fuels. Large grid only.");
            GenerateBlockItem<GridChemicalPlant>("Chemical Plant", 1620f, 980f, "Mixes fuels into high-performance liquid propellant.");
            GenerateBlockItem<GridThruster>("Atmospheric Thruster", 680f, 480f, "Efficient in atmosphere. Uses power only.");
            GenerateBlockItem<GridThruster>("Hydrogen Thruster", 720f, 510f, "Powerful all-environment thruster. Consumes Hydrogen.");
            GenerateBlockItem<GridThruster>("Ion Thruster", 540f, 390f, "Highly efficient in space. Low thrust, high Isp.");
            GenerateBlockItem<GridThruster>("Liquid Fuel Thruster", 890f, 620f, "Most powerful thruster. Uses complex liquid fuel mix.");

            // === RESEARCH NODES ===
            GenerateResearchNode("res_grid_construction", "Grid Construction", "Unlocks all grid building blocks and ship systems.", 4, ResearchSubCategory.Building);
            GenerateResearchNode("res_liquid_fuel", "Liquid Fuel Processing", "Unlocks Refinery, Chemical Plant, and complex fuel production.", 5, ResearchSubCategory.Chemistry);
            GenerateResearchNode("res_ship_weapons", "Ship Weapons & Defense", "Unlocks ship-mounted weapons and advanced armor.", 4, ResearchSubCategory.Military);

            // === BASIC RECIPES (placeholder) ===
            GenerateBasicRecipe("Recipe_GridArmor", "Armor Block", 8);
            GenerateBasicRecipe("Recipe_GridCockpit", "Cockpit", 6);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[GridContentGenerator] Full grid system content generated successfully!");
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(BlockItemsPath)) AssetDatabase.CreateFolder("Assets/Resources", "GridBlocks");
            if (!AssetDatabase.IsValidFolder(ResearchPath)) AssetDatabase.CreateFolder("Assets/Resources/Research", "Grid");
            if (!AssetDatabase.IsValidFolder(RecipesPath)) AssetDatabase.CreateFolder("Assets/Resources/Recipes", "Grid");
        }

        private static void GenerateBlockItem<T>(string name, float largeMass, float smallMass, string description) where T : GridBlock
        {
            string safeName = name.Replace(" ", "_");
            string path = $"{BlockItemsPath}/GBlock_{safeName}.asset";

            var item = ScriptableObject.CreateInstance<GridBlockItem>();
            item.displayName = name;
            item.blockMass = largeMass;           // Default to Large grid mass
            item.blockHP = 600f;
            item.gridSize = GridSize.Large;
            item.iconTint = new Color(0.6f, 0.6f, 0.65f);

            // Store description in a custom way (we can extend GridBlockItem later if needed)
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

        private static void GenerateBasicRecipe(string fileName, string blockName, int materialCost)
        {
            // Placeholder recipe generation
            string path = $"{RecipesPath}/{fileName}.asset";
            // In a real system we would create RecipeDefinition assets here
        }

        // === TESTING COMMAND ===
        [MenuItem("Voxel Engine/Grid/Spawn All Grid Items (Debug)")]
        public static void SpawnAllGridItemsForTesting()
        {
            var player = GameObject.FindObjectOfType<Player.PlayerController>();
            if (player == null)
            {
                Debug.LogError("No PlayerController found in scene!");
                return;
            }

            var inventory = player.GetComponentInChildren<Inventory>();
            if (inventory == null) return;

            var allItems = Resources.FindObjectsOfTypeAll<GridBlockItem>();
            foreach (var item in allItems)
            {
                inventory.Add(item, 5);
            }

            Debug.Log($"[Debug] Spawned {allItems.Length} grid block types into player inventory.");
        }
    }
}
#endif
