// Assets/Scripts/Editor/Progression/ResearchGatingSetup.cs
//
// 17.26.0-dev - BIOMASS / FORESTRY AND ALLOY GATING
//
// Two chains were reachable from the first minute even though the tree had a node for
// them:
//
//   * Biomass, tree plots and the Automatic Tree Farm. The Biofuel chain step
//     (17.25.0-dev) created the node AND linked the recipes to it, but left every one
//     of those recipes flagged unlockedByDefault = true, so the node changed nothing.
//     The node also sat at tier 1 in General with no prerequisites and no science cost,
//     which is why the branch never read as part of the ladder.
//
//   * Alloying. Steel and brass were smeltable the moment a Furnace existed: SmeltingRecipe
//     had no progression hook at all, and the Coke Kiln - the machine that makes the coke
//     a steel charge needs - was linked to a node yet still free.
//
// This step re-homes the forestry node onto the production ladder, points the alloy node
// at the two alloy smelts, and re-states the gating law in the data.
//
// NON-DESTRUCTIVE: the forestry node is only MOVED while it is still the untouched
// scaffold (General / tier 1 / no prerequisites / no cost). Once it has been designed,
// it is left exactly as it is. Existing costs, times and hand-edited tiers are never
// overwritten, and nothing is ever deleted or unlinked.
//
// Run: Tools > Voxel Engine > Voxel Engine Setup > Factory, Industry & Power.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    /// <summary>Research gating for the biomass/forestry chain and the alloy chain.</summary>
    public static class ResearchGatingSetup
    {
        private const string ForestryNodeId = "research_biofuel_forestry";
        private const string ForestryAssetName = "Research_BiofuelAndForestry";
        private const string AlloyNodeId = "res_steel_alloy";

        // ── Step: biomass, tree plots and the tree farm ─────────────────────────

        public static void RunStep119()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Biomass & Forestry Gating",
                    "Exit Play Mode before running setup.", "OK");
                return;
            }

            var notes = new List<string>();
            var missing = new List<string>();

            var node = ResearchAuthoring.FindNode(ForestryNodeId);
            if (node == null)
            {
                // The chain step has not been run in this project. Author the node here
                // with the same id, so re-running the chain step later adopts it.
                node = ResearchAuthoring.EnsureNode(
                    ForestryAssetName, ForestryNodeId,
                    "Industrial Forestry & Biofuel Refining",
                    "Unlocks the Automatic Tree Farm, Tree Farming Plots and the Biomass Press for sustainable " +
                    "timber harvesting and liquid/solid Biofuel refining.",
                    ResearchSubCategory.Production, 3, 12, 120f, new Color(0.36f, 0.62f, 0.32f));
                notes.Add("node created (the biofuel chain step had not been run here yet)");
            }

            // Re-home the untouched scaffold onto the production ladder. Players who
            // already researched it keep the rank - the nodeId never changes.
            bool scaffold = node.subCategory == ResearchSubCategory.General
                            && node.tier <= 1
                            && (node.prerequisites == null || node.prerequisites.Length == 0)
                            && (node.cost == null || node.cost.Length == 0);
            if (scaffold)
            {
                node.subCategory = ResearchSubCategory.Production;
                node.tier = 3;
                node.column = 12;
                if (node.researchSeconds < 120f) node.researchSeconds = 120f;
                EditorUtility.SetDirty(node);
                notes.Add("node moved to Production tier 3 (it was General tier 1 with no prerequisites)");
            }

            var farming = ResearchAuthoring.FindNode("res_farming");
            if (farming == null) missing.Add("res_farming (prerequisite)");
            else if (ResearchAuthoring.LinkPrerequisites(node, farming))
                notes.Add("prerequisite added: Farming");

            var pack = ResearchAuthoring.FindSciencePack(1);
            if (pack == null) missing.Add("Item_ScienceT1 (cost)");
            else if (ResearchAuthoring.EnsureScienceCost(node, pack, 40))
                notes.Add("science cost added: 40x Science Pack I");

            // Repair the recipe links the chain step intends, in case it was never run
            // or ran against older assets.
            var chain = new[]
            {
                "Recipe_TreeSapling", "Recipe_TreeFarmPlot", "Recipe_OrganicFertilizer",
                "Recipe_BiofuelBriquette", "Recipe_AutoTreeFarm", "Recipe_BiomassPress",
            };
            int relinked = 0;
            foreach (string recipeName in chain)
            {
                var recipe = ResearchAuthoring.FindRecipe(recipeName);
                if (recipe == null) { missing.Add(recipeName); continue; }
                if (ResearchAuthoring.AddRecipeUnlock(node, recipe)) relinked++;
            }
            if (relinked > 0) notes.Add($"{relinked} missing recipe link(s) repaired");

            ResearchAuthoring.RegisterInTrees(node);

            int gated = ResearchAuthoring.EnforceLinkedRecipeGating(out List<string> reopened);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ResearchGatingSetup] 17.26.0-dev biomass & forestry: node '{ForestryNodeId}' at " +
                      $"{(node.subCategory == ResearchSubCategory.Production ? "Production" : node.subCategory.ToString())} " +
                      $"tier {node.tier}, {gated} recipe(s) behind research" +
                      (gated > 0 ? $" [{ResearchAuthoring.Join(reopened)}]" : "") + ".");
            if (missing.Count > 0)
                Debug.LogWarning($"[ResearchGatingSetup] Not found (skipped): {ResearchAuthoring.Join(missing, 20)}");

            EditorUtility.DisplayDialog(
                "Biomass & Forestry Gating Complete",
                "Authored and wired:\n" +
                "• Industrial Forestry & Biofuel Refining now sits on the Production ladder\n" +
                "• Biomass, Tree Farming Plots, Organic Fertilizer, Biofuel Briquettes,\n" +
                "   the Biomass Press and the Automatic Tree Farm all require it\n" +
                $"• {gated} recipe(s) that were linked to a node while still being free are now gated\n\n" +
                (notes.Count > 0 ? "Changes: " + ResearchAuthoring.Join(notes, 6) + "\n\n" : "") +
                "Players who already researched the node keep it: the id never changed.",
                "OK");
        }

        // ── Step: alloying and metallurgy ───────────────────────────────────────

        public static void RunStep120()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Alloy Research Gating",
                    "Exit Play Mode before running setup.", "OK");
                return;
            }

            var notes = new List<string>();
            var missing = new List<string>();

            var alloy = ResearchAuthoring.FindNode(AlloyNodeId);
            if (alloy == null) missing.Add($"{AlloyNodeId} (node)");

            // Steel and brass are the alloy smelts. Until now SmeltingRecipe had no
            // progression hook, so both were on the menu the moment a Furnace existed.
            var gatedSmelts = new List<string>();
            foreach (string smeltName in new[] { "Smelt_Steel", "Smelt_Brass" })
            {
                var smelt = FindSmelt(smeltName);
                if (smelt == null) { missing.Add(smeltName); continue; }
                if (smelt.requiresResearch == null && alloy != null)
                {
                    smelt.requiresResearch = alloy;
                    EditorUtility.SetDirty(smelt);
                    gatedSmelts.Add(smeltName);
                }
            }
            if (gatedSmelts.Count > 0)
                notes.Add($"alloy smelts gated: {ResearchAuthoring.Join(gatedSmelts, 4)}");

            // The alloy node also owns the finished steel goods that were still free.
            if (alloy != null)
            {
                int added = 0;
                foreach (string recipeName in new[] { "Recipe_PickSteel", "Recipe_SteelChest" })
                {
                    var recipe = ResearchAuthoring.FindRecipe(recipeName);
                    if (recipe == null) { missing.Add(recipeName); continue; }
                    if (ResearchAuthoring.AddRecipeUnlock(alloy, recipe)) added++;
                }
                if (added > 0) notes.Add($"{added} steel recipe link(s) added");
                ResearchAuthoring.RegisterInTrees(alloy);
            }

            int gated = ResearchAuthoring.EnforceLinkedRecipeGating(out List<string> reopened);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ResearchGatingSetup] 17.26.0-dev alloy gating: {gatedSmelts.Count} smelt(s) pointed at " +
                      $"'{AlloyNodeId}', {gated} recipe(s) behind research" +
                      (gated > 0 ? $" [{ResearchAuthoring.Join(reopened)}]" : "") + ".");
            if (missing.Count > 0)
                Debug.LogWarning($"[ResearchGatingSetup] Not found (skipped): {ResearchAuthoring.Join(missing, 20)}");

            EditorUtility.DisplayDialog(
                "Alloy Research Gating Complete",
                "Authored and wired:\n" +
                "• Smelt_Steel and Smelt_Brass now require Steel Alloy - no steel or brass\n" +
                "   comes out of a furnace before that node is done\n" +
                "• Steel Alloy also covers the steel pickaxe and the steel chest\n" +
                $"• {gated} recipe(s) that claimed a node while still being free are now gated\n" +
                "   (the Coke Kiln and Arc Foundry sit under Electric Arc Metallurgy)\n\n" +
                (notes.Count > 0 ? "Changes: " + ResearchAuthoring.Join(notes, 6) : "No changes were needed."),
                "OK");
        }

        public static void RunSilentAll()
        {
            RunStep119();
            RunStep120();
        }

        private static SmeltingRecipe FindSmelt(string assetName)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{assetName} t:SmeltingRecipe"))
            {
                var smelt = AssetDatabase.LoadAssetAtPath<SmeltingRecipe>(AssetDatabase.GUIDToAssetPath(guid));
                if (smelt != null && string.Equals(smelt.name, assetName, System.StringComparison.OrdinalIgnoreCase))
                    return smelt;
            }
            return null;
        }
    }
}
#endif
