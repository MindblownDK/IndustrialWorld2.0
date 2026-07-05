// Assets/Scripts/Editor/WindPowerSetupWindow.cs
// Editor utility to generate beautiful windmill prefabs + create/register all wind power items, recipes, and update research.
// MAX EFFORT: one-click setup for the complete wind power system.
// Run: Window > IndustrialWorld > Wind Power Setup

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using VoxelEngine.Items;
using VoxelEngine.Crafting;
using VoxelEngine.Power.Wind;
using VoxelEngine.Research;

namespace VoxelEngine.Editor
{
    public class WindPowerSetupWindow : EditorWindow
    {
        [MenuItem("IndustrialWorld/Wind/Wind Power Setup (MAX EFFORT)")]
        public static void ShowWindow()
        {
            GetWindow<WindPowerSetupWindow>("Wind Power Setup");
        }

        private string status = "Ready. Click buttons below.";

        private void OnGUI()
        {
            GUILayout.Label("IndustrialWorld 2.0 — Wind Power 3.0 Setup", EditorStyles.boldLabel);
            GUILayout.Space(8);

            EditorGUILayout.HelpBox("This will:\n• Generate 5 beautiful, fully componentized, customizable windmill prefabs\n• Create/overwrite ItemDefinitions and BlockItems for windmills + assembly parts\n• Create Recipes (Assembler)\n• Link everything to the 3 existing Wind Research nodes\n• Ensure WindSystem + WindmillAssembly are ready\n\nAll windmills = stationary, non-grid. Helix cross-size supported.", MessageType.Info);

            if (GUILayout.Button("1. GENERATE ALL BEAUTIFUL WINDMILL PREFABS", GUILayout.Height(38)))
            {
                WindmillPrefabGenerator.GenerateAllFromMenu();
                status = "Prefabs generated into Assets/VoxelEngineAssets/WindPower/Prefabs/";
            }

            GUILayout.Space(6);

            if (GUILayout.Button("2. CREATE / UPDATE ALL WIND ITEMS + RECIPES", GUILayout.Height(34)))
            {
                CreateWindItemsAndRecipes();
                status = "Items + Recipes created/updated in VoxelEngineAssets/WindPower/";
            }

            GUILayout.Space(6);

            if (GUILayout.Button("3. LINK WIND RECIPES TO EXISTING RESEARCH NODES", GUILayout.Height(34)))
            {
                LinkRecipesToResearch();
                status = "Research nodes updated with new wind recipes.";
            }

            GUILayout.Space(12);
            EditorGUILayout.LabelField("Status:", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(status, MessageType.None);

            GUILayout.Space(10);
            EditorGUILayout.HelpBox("After running:\n1. Open each new BlockItem and assign the generated placedPrefab.\n2. Assign proper icons/sprites.\n3. Run the game — windmills will be fully functional, beautiful, and assembleable.\n4. Make sure WindSystem is in the scene.", MessageType.Warning);
        }

        private void CreateWindItemsAndRecipes()
        {
            string basePath = "Assets/VoxelEngineAssets/WindPower";

            // Ensure folders
            EnsureFolder(basePath + "/Items");
            EnsureFolder(basePath + "/Recipes");

            WindmillItems.EnsureRegistered();

            // Save the items as assets
            SaveItemAsset(WindmillItems.SmallStandardBlock, basePath + "/Items/Block_SWind_Small.asset");
            SaveItemAsset(WindmillItems.MediumStandardBlock, basePath + "/Items/Block_SWind_Medium.asset");
            SaveItemAsset(WindmillItems.LargeStandardBlock, basePath + "/Items/Block_SWind_Large.asset");
            SaveItemAsset(WindmillItems.SmallHelixBlock, basePath + "/Items/Block_HWind_Small.asset");
            SaveItemAsset(WindmillItems.LargeHelixBlock, basePath + "/Items/Block_HWind_Large.asset");
            SaveItemAsset(WindmillItems.MonopoleBlock, basePath + "/Items/Block_WindmillMonopole.asset");

            // Save parts
            SaveItemAsset(WindmillItems.TowerSegmentItem, basePath + "/Items/Item_Wind_Tower.asset");
            SaveItemAsset(WindmillItems.NacelleItem, basePath + "/Items/Item_Wind_Nacelle.asset");
            SaveItemAsset(WindmillItems.GearboxItem, basePath + "/Items/Item_Wind_Gearbox.asset");
            SaveItemAsset(WindmillItems.GeneratorItem, basePath + "/Items/Item_Wind_Generator.asset");
            SaveItemAsset(WindmillItems.HubItem, basePath + "/Items/Item_Wind_Hub.asset");
            SaveItemAsset(WindmillItems.BladeItem, basePath + "/Items/Item_Wind_Blade.asset");

            SaveItemAsset(WindmillItems.HelixGeneratorSmall, basePath + "/Items/Item_HelixGen_Small.asset");
            SaveItemAsset(WindmillItems.HelixGeneratorLarge, basePath + "/Items/Item_HelixGen_Large.asset");
            SaveItemAsset(WindmillItems.HelixWingSmall, basePath + "/Items/Item_HelixWing_Small.asset");
            SaveItemAsset(WindmillItems.HelixWingLarge, basePath + "/Items/Item_HelixWing_Large.asset");

            // Save recipes
            SaveRecipe(WindmillItems.Recipe_Tower, basePath + "/Recipes/Recipe_Wind_Tower.asset");
            SaveRecipe(WindmillItems.Recipe_Nacelle, basePath + "/Recipes/Recipe_Wind_Nacelle.asset");
            SaveRecipe(WindmillItems.Recipe_Gearbox, basePath + "/Recipes/Recipe_Wind_Gearbox.asset");
            SaveRecipe(WindmillItems.Recipe_Generator, basePath + "/Recipes/Recipe_Wind_Generator.asset");
            SaveRecipe(WindmillItems.Recipe_Hub, basePath + "/Recipes/Recipe_Wind_Hub.asset");
            SaveRecipe(WindmillItems.Recipe_Blade, basePath + "/Recipes/Recipe_Wind_Blade.asset");

            SaveRecipe(WindmillItems.Recipe_HelixGenSmall, basePath + "/Recipes/Recipe_HelixGen_Small.asset");
            SaveRecipe(WindmillItems.Recipe_HelixGenLarge, basePath + "/Recipes/Recipe_HelixGen_Large.asset");
            SaveRecipe(WindmillItems.Recipe_HelixWingSmall, basePath + "/Recipes/Recipe_HelixWing_Small.asset");
            SaveRecipe(WindmillItems.Recipe_HelixWingLarge, basePath + "/Recipes/Recipe_HelixWing_Large.asset");

            SaveRecipe(WindmillItems.Recipe_SmallStandard, basePath + "/Recipes/Recipe_SWindSmall.asset");
            SaveRecipe(WindmillItems.Recipe_MediumStandard, basePath + "/Recipes/Recipe_SWindMed.asset");
            SaveRecipe(WindmillItems.Recipe_LargeStandard, basePath + "/Recipes/Recipe_SWindLarge.asset");
            SaveRecipe(WindmillItems.Recipe_SmallHelix, basePath + "/Recipes/Recipe_HWindSmall.asset");
            SaveRecipe(WindmillItems.Recipe_LargeHelix, basePath + "/Recipes/Recipe_HWindLarge.asset");
            SaveRecipe(WindmillItems.Recipe_Monopole, basePath + "/Recipes/Recipe_WindMono.asset");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void LinkRecipesToResearch()
        {
            // Load existing research nodes
            string nodesPath = "Assets/VoxelEngineAssets/Research/Nodes/";

            ResearchNode wind1 = AssetDatabase.LoadAssetAtPath<ResearchNode>(nodesPath + "res_wind_1.asset");
            ResearchNode wind2 = AssetDatabase.LoadAssetAtPath<ResearchNode>(nodesPath + "res_wind_2.asset");
            ResearchNode wind3 = AssetDatabase.LoadAssetAtPath<ResearchNode>(nodesPath + "res_wind_3.asset");

            if (wind1 == null || wind2 == null || wind3 == null)
            {
                Debug.LogError("Could not find the 3 wind research nodes. Please ensure they exist.");
                return;
            }

            // Update descriptions to match user spec + add Power Generation filter hint
            wind1.displayName = "Wind Power I — Small Standard";
            wind1.description = "Unlocks the Small Standard Windmill (Vestas-inspired). Stationary. Efficiency based on wind speed & height.";
            wind1.subCategory = ResearchSubCategory.Power;

            wind2.displayName = "Wind Power II";
            wind2.description = "Unlocks Small Vertical Helix Windmill + Medium Standard Windmill. Cross-size helix wings supported.";
            wind2.subCategory = ResearchSubCategory.Power;

            wind3.displayName = "Wind Power III";
            wind3.description = "Unlocks Large Vertical Helix + Large V236 Standard Windmill (15MW, offshore capable). Monopole support.";
            wind3.subCategory = ResearchSubCategory.Power;

            // Load the recipes we just created
            string recPath = "Assets/VoxelEngineAssets/WindPower/Recipes/";

            RecipeDefinition rSmallStd = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recPath + "Recipe_SWindSmall.asset");
            RecipeDefinition rMedStd = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recPath + "Recipe_SWindMed.asset");
            RecipeDefinition rLargeStd = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recPath + "Recipe_SWindLarge.asset");
            RecipeDefinition rSmallHelix = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recPath + "Recipe_HWindSmall.asset");
            RecipeDefinition rLargeHelix = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recPath + "Recipe_HWindLarge.asset");
            RecipeDefinition rMono = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recPath + "Recipe_WindMono.asset");

            // Link to nodes (append, keep previous)
            AddRecipeToNode(wind1, rSmallStd);
            AddRecipeToNode(wind1, rMono); // monopole early

            AddRecipeToNode(wind2, rSmallHelix);
            AddRecipeToNode(wind2, rMedStd);

            AddRecipeToNode(wind3, rLargeStd);
            AddRecipeToNode(wind3, rLargeHelix);

            // Also ensure nuclear stays in Power
            ResearchNode nuclear = AssetDatabase.LoadAssetAtPath<ResearchNode>(nodesPath + "res_nuclear_fission.asset");
            if (nuclear != null)
            {
                nuclear.subCategory = ResearchSubCategory.Power;
            }

            EditorUtility.SetDirty(wind1);
            EditorUtility.SetDirty(wind2);
            EditorUtility.SetDirty(wind3);
            if (nuclear) EditorUtility.SetDirty(nuclear);

            AssetDatabase.SaveAssets();
        }

        private void AddRecipeToNode(ResearchNode node, RecipeDefinition recipe)
        {
            if (node == null || recipe == null) return;

            var list = new System.Collections.Generic.List<RecipeDefinition>(node.unlocksRecipes);
            if (!list.Contains(recipe))
                list.Add(recipe);
            node.unlocksRecipes = list.ToArray();
        }

        private void SaveItemAsset(ItemDefinition item, string path)
        {
            if (item == null) return;
            AssetDatabase.CreateAsset(item, path);
        }

        private void SaveRecipe(RecipeDefinition recipe, string path)
        {
            if (recipe == null) return;
            AssetDatabase.CreateAsset(recipe, path);
        }

        private void EnsureFolder(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }
        }
    }
}
#endif
