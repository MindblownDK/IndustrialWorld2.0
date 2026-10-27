// Assets/Scripts/Editor/Progression/DisplayAndModuleUnlockSetup.cs
//
// 18.2.1-dev — two families of content existed but nobody could ever reach them.
//
// The Recipe Graph Validator and the research audit both report the same kind of
// orphan: a recipe flagged "not available by default" that no research node
// claims. Nothing in the game can ever unlock it, so the item exists, has an
// icon, has a recipe, sits in the registry - and is dead content. Eight items
// were in that state: the Camera Block, all seven glass screens (Small, Medium,
// Large, Wide, Extra Large, Detail, Structural) and the two universal machine
// modules (Speed, Efficiency).
//
// Why it happened: the content steps that author those blocks set
// unlockedByDefault = false on the recipe - correctly, because nothing in this
// project is free - but they belong to families whose unlock was never authored.
// The screen step owns six prefabs and no node; the machine-module step owns two
// items and no node.
//
// What this step does, and does not do:
//   * authors ONE node for the display family (Grid Displays & Optics) and puts
//     the two machine modules on the existing Factory Automation node;
//   * only ever APPENDS to an unlock list, never rewrites one, never removes a
//     recipe from another node, never re-titles, re-costs or re-creates a recipe;
//   * only claims recipes that are currently claimed by nobody, so it can never
//     steal a recipe from the branch that owns it;
//   * never touches an authored node design value (tier, column, prerequisites,
//     science cost) on a node that already has one;
//   * finishes by naming anything still orphaned instead of guessing a branch for
//     it, because a wrong guess is worse than a report.
//
// Editor-only. Run: Tools > Voxel Engine > Setup Wizard.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    public static class DisplayAndModuleUnlockSetup
    {
        private const string DisplaysNodeId = "res_grid_displays";
        private const string ModulesNodeId = "res_factory_automation";

        private static readonly Color DisplayTint = new Color(0.24f, 0.72f, 0.90f);
        private static readonly Color ModuleTint = new Color(0.18f, 0.72f, 0.88f);

        public static void RunStep123()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Display & Module Unlocks",
                    "Exit Play Mode before running setup.", "OK");
                return;
            }

            var createdNodes = new List<string>();
            var linkedRecipes = new List<string>();
            var homes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // ── 1. The display family gets a node of its own ────────────────────
            var displays = ResearchAuthoring.FindNode(DisplaysNodeId);
            bool displaysWasMissing = displays == null;
            displays = ResearchAuthoring.EnsureNode(
                DisplaysNodeId, DisplaysNodeId,
                "Grid Displays & Optics",
                "Mounted instrumentation for a hull or a factory wall. Unlocks the Camera Block's live "
                + "feed and every glass panel, from the small readout to the extra-large wall display, "
                + "so a construct can report its own state instead of the player guessing at it.",
                ResearchSubCategory.Logistics, tier: 3, column: 8, researchSeconds: 70f, iconTint: DisplayTint);
            if (displaysWasMissing) createdNodes.Add(displays.nodeId);

            // Prerequisites: only written while the node has none.
            ResearchAuthoring.LinkPrerequisites(displays,
                ResearchAuthoring.FindNode("res_electronics"),
                ResearchAuthoring.FindNode("res_grid_utilities"));

            // Cost: one science pack, sized to where the node sits.
            ResearchAuthoring.EnsureScienceCost(displays, ResearchAuthoring.FindSciencePack(1), 40);

            // ── 2. The machine modules ride the factory-automation node ─────────
            var modules = ResearchAuthoring.FindNode(ModulesNodeId);
            bool modulesWasMissing = modules == null;
            if (modules == null)
            {
                // Only reached on a project where the factory branch was never authored.
                // The identity matches the node the factory content step creates, so a
                // later run of that step adopts this one instead of making a second.
                modules = ResearchAuthoring.EnsureNode(
                    ModulesNodeId, ModulesNodeId,
                    "Factory Automation",
                    "Automate production with the first powered assembler tier, faster material handling "
                    + "and the universal machine modules that speed a machine up or make it cheaper to run.",
                    ResearchSubCategory.Logistics, tier: 4, column: 5, researchSeconds: 90f, iconTint: ModuleTint);
                if (modulesWasMissing) createdNodes.Add(modules.nodeId);
                ResearchAuthoring.LinkPrerequisites(modules, ResearchAuthoring.FindNode("res_electricity"));
                ResearchAuthoring.EnsureScienceCost(modules, ResearchAuthoring.FindSciencePack(2), 15);
            }

            // ── 3. Claim every display and module recipe nobody owns ────────────
            var claimed = new HashSet<RecipeDefinition>(ResearchAuthoring.CollectClaimedRecipes());

            foreach (string guid in AssetDatabase.FindAssets("t:RecipeDefinition"))
            {
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (recipe == null || recipe.outputItem == null) continue;

                // Never take a recipe from the branch that already owns it.
                if (claimed.Contains(recipe)) continue;

                var node = Classify(recipe.outputItem);
                if (node == null) continue;

                if (ResearchAuthoring.AddRecipeUnlock(node, recipe))
                {
                    linkedRecipes.Add(recipe.name);
                    homes[node.nodeId] = homes.TryGetValue(node.nodeId, out int n) ? n + 1 : 1;
                }
            }

            // ── 4. Register the nodes and restate the gating law in the data ─────
            int registered = ResearchAuthoring.RegisterInTrees(displays, modules);
            int gated = ResearchAuthoring.EnforceLinkedRecipeGating(out List<string> reopened);

            // ── 5. Name whatever is STILL orphaned instead of guessing ──────────
            var stillOrphaned = ReportOrphans();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[DisplayAndModuleUnlockSetup] 18.2.1-dev: "
                + $"{linkedRecipes.Count} orphaned recipe(s) claimed "
                + $"({Homes(homes)}), {createdNodes.Count} node(s) created, "
                + $"{registered} tree registration(s), {gated} recipe(s) pulled behind research, "
                + $"{stillOrphaned.Count} still orphaned.");

            if (linkedRecipes.Count > 0)
                Debug.Log("[DisplayAndModuleUnlockSetup] Claimed: "
                    + ResearchAuthoring.Join(linkedRecipes, 30));

            if (stillOrphaned.Count > 0)
                Debug.LogWarning("[DisplayAndModuleUnlockSetup] These recipes are still gated with no "
                    + "research node claiming them, so nothing can unlock them yet. They are NOT guessed "
                    + "at here - run the content step that authors their family, or name a branch for "
                    + "them: " + ResearchAuthoring.Join(stillOrphaned, 40));

            EditorUtility.DisplayDialog(
                "Display & Module Unlocks (18.2.1-dev)",
                "Every orphaned display and machine-module recipe is now reachable.\n\n"
                + $"Recipes claimed: {linkedRecipes.Count}\n"
                + $"  {Homes(homes)}\n"
                + $"Nodes created: {(createdNodes.Count == 0 ? "none (already authored)" : ResearchAuthoring.Join(createdNodes, 6))}\n"
                + $"Recipe(s) pulled behind research: {gated}\n\n"
                + "Grid Displays & Optics: Camera Block and every screen.\n"
                + "Factory Automation: Machine Speed and Machine Efficiency Modules.\n\n"
                + (stillOrphaned.Count > 0
                    ? $"Still unreachable after this pass ({stillOrphaned.Count}): "
                      + ResearchAuthoring.Join(stillOrphaned, 8) + "\nSee the Console for the full list.\n\n"
                    : "No recipe in the project is gated without an owner.\n\n")
                + "Nothing was renamed, re-costed or removed; existing branches keep their recipes.",
                "OK");
        }

        /// <summary>
        /// Which node a display or module recipe belongs to, decided from the ITEM it
        /// produces - never from the asset name, because the same family is spread over
        /// several folders and a rename must not silently unhook it. Returns null for
        /// anything this step does not own, which is most recipes.
        /// </summary>
        private static ResearchNode Classify(ItemDefinition output)
        {
            if (output == null) return null;

            string id = output.itemId ?? string.Empty;
            string name = output.displayName ?? string.Empty;

            // Rail signage is a display by name and a rail feature by job. It belongs to
            // the signalling branch, so this step never claims it even when that branch
            // has not been authored yet.
            if (Contains(name, "departure") || Contains(name, "nixie")
                || Contains(name, "brass display") || Contains(name, "schedule")
                || Contains(id, "rail"))
                return null;

            if (Contains(id, "screen") || Contains(name, "screen")
                || Contains(id, "camera") || Contains(name, "camera"))
                return ResearchAuthoring.FindNode(DisplaysNodeId);

            if (Contains(id, "upgrade_machine") || Contains(name, "machine speed module")
                || Contains(name, "machine efficiency module")
                || (Contains(name, "module") && Contains(name, "machine")))
                return ResearchAuthoring.FindNode(ModulesNodeId);

            return null;
        }

        private static bool Contains(string haystack, string needle)
            => !string.IsNullOrEmpty(haystack)
               && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string Homes(Dictionary<string, int> homes)
        {
            if (homes == null || homes.Count == 0) return "nothing needed a new home";
            var parts = new List<string>();
            foreach (var pair in homes) parts.Add($"{pair.Key}: {pair.Value}");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Diagnostic, not a fix: every recipe that is gated but claimed by no node.
        /// Returned so the dialog and the Console can name them.
        /// </summary>
        private static List<string> ReportOrphans()
        {
            var claimed = new HashSet<RecipeDefinition>(ResearchAuthoring.CollectClaimedRecipes());
            var orphans = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:RecipeDefinition"))
            {
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (recipe == null || recipe.unlockedByDefault) continue;
                if (claimed.Contains(recipe)) continue;
                orphans.Add(recipe.name);
            }

            orphans.Sort(StringComparer.OrdinalIgnoreCase);
            return orphans;
        }
    }
}
#endif
