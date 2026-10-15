// Assets/Scripts/Editor/Combat/MilitaryResearchSetup.cs
//
// 17.26.0-dev - MILITARY BRANCH + ARMAMENT GATING
//
// The Military branch of the research tree held exactly one node ("Ship Armament"),
// and every turret, gun and bomb in the project was craftable from the first minute
// because the content steps write unlockedByDefault = true on every run. The branch
// therefore read as empty while the weapons were already free - the two complaints
// are one bug.
//
// This step authors the branch (seven nodes, an era ladder from firearms to strategic
// ordnance) and re-states the gating law in the data: any recipe a research node lists
// stops being default-unlocked. ResearchManager.IsRecipeUnlocked enforces the same rule
// at runtime, so re-running a content step can no longer silently unlock the arsenal.
//
// NON-DESTRUCTIVE: nodes are created when missing and never overwritten afterwards -
// tier, column, prerequisites, cost and research time are only written while the node
// is still empty, so a hand-tuned ladder survives re-runs. Recipes are only ever
// LINKED, never created, retitled, re-costed or removed.
//
// Run: Tools > Voxel Engine > Voxel Engine Setup > Combat, Weapons & Defences.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    /// <summary>Authors the military research branch and gates the armament recipes.</summary>
    public static class MilitaryResearchSetup
    {
        private static readonly Color MilOlive = new Color(0.62f, 0.70f, 0.40f);
        private static readonly Color MilSteel = new Color(0.55f, 0.60f, 0.66f);
        private static readonly Color MilFire = new Color(0.86f, 0.45f, 0.22f);
        private static readonly Color MilVoid = new Color(0.58f, 0.36f, 0.78f);

        /// <summary>One node in the ladder: identity, position, prerequisites and the
        /// recipes it hands over.</summary>
        private sealed class NodeSpec
        {
            public string AssetName, NodeId, Display, Description;
            public ResearchSubCategory Sub = ResearchSubCategory.Military;
            public int Tier, Column;
            public float Seconds;
            public Color Tint;
            public string[] PrerequisiteIds;
            public string[] RecipeNames;
        }

        // The ladder. Columns 11-13 sit clear of everything the tree already occupies,
        // so the branch reads as one block of the map instead of weaving through it.
        private static readonly NodeSpec[] Ladder =
        {
            new NodeSpec
            {
                AssetName = "res_mil_ballistics", NodeId = "res_mil_ballistics",
                Display = "Ballistics & Small Arms",
                Description = "Rifled barrels, cased ammunition and the first proper firearms. " +
                              "Unlocks the Rifle, the iron pistol, ammunition casting and the training dummy.",
                Tier = 3, Column = 11, Seconds = 120f, Tint = MilSteel,
                PrerequisiteIds = new[] { "res_smelting" },
                RecipeNames = new[] { "Recipe_Rifle", "Recipe_Bullets", "Recipe_IronPistol", "Recipe_TrainingDummy" }
            },
            new NodeSpec
            {
                AssetName = "res_mil_turrets", NodeId = "res_mil_turrets",
                Display = "Automated Defences",
                Description = "Servo-driven mountings with their own targeting logic: the Auto Turret and the " +
                              "anti-air flak turret that watches the sky for you, plus their tracked ammunition.",
                Tier = 4, Column = 11, Seconds = 150f, Tint = MilOlive,
                PrerequisiteIds = new[] { "res_mil_ballistics", "res_electronics" },
                RecipeNames = new[] { "Recipe_Turret", "Recipe_AntiAirTurret", "Recipe_AARounds" }
            },
            new NodeSpec
            {
                AssetName = "res_mil_artillery", NodeId = "res_mil_artillery",
                Display = "Field Artillery",
                Description = "Indirect fire and siege pieces: the heavy cannon, the minigun turret, the Schwerer " +
                              "Gustav, the mortar and the giant-shell turret, with their shell and mortar rounds.",
                Tier = 5, Column = 11, Seconds = 180f, Tint = MilOlive,
                PrerequisiteIds = new[] { "res_mil_turrets", "res_plating" },
                RecipeNames = new[]
                {
                    "Recipe_ArtilleryCannon", "Recipe_ArtilleryMinigun", "Recipe_ArtilleryGustav",
                    "Recipe_MortarTurret", "Recipe_GiantShellTurret",
                    "Recipe_GiantShell", "Recipe_ShellStandard", "Recipe_ShellScatter"
                }
            },
            new NodeSpec
            {
                AssetName = "res_mil_explosives", NodeId = "res_mil_explosives",
                Display = "High Explosives",
                Description = "Manufactured demolition: grenades, the powder keg, and the breaching, smoke and " +
                              "high-explosive mortar rounds that break what the guns cannot.",
                Tier = 5, Column = 12, Seconds = 180f, Tint = MilFire,
                PrerequisiteIds = new[] { "res_mil_ballistics", "res_oil_refining" },
                RecipeNames = new[]
                {
                    "Recipe_Grenade", "Recipe_PowderKeg",
                    "Recipe_MortarExplosive", "Recipe_MortarSmoke", "Recipe_ShellExplosive"
                }
            },
            new NodeSpec
            {
                AssetName = "res_mil_incendiary", NodeId = "res_mil_incendiary",
                Display = "Incendiary Warfare",
                Description = "Pressurised flame fuel and the flamethrower turret that drinks it, plus illumination " +
                              "rounds that turn night into day over a battlefield.",
                Tier = 5, Column = 13, Seconds = 180f, Tint = MilFire,
                PrerequisiteIds = new[] { "res_mil_explosives" },
                RecipeNames = new[] { "Recipe_FlamethrowerTurret", "Recipe_FlameCanister", "Recipe_MortarIllum" }
            },
            new NodeSpec
            {
                AssetName = "res_mil_relic_armaments", NodeId = "res_mil_relic_armaments",
                Display = "Relic Armaments",
                Description = "Precursor weapon cores wired into modern mountings: the energy relic turret, its " +
                              "capacitor bank and the charged cells that feed them.",
                Tier = 6, Column = 11, Seconds = 240f, Tint = MilVoid,
                PrerequisiteIds = new[] { "res_ship_weapons" },
                RecipeNames = new[] { "Recipe_EnergyRelicTurret", "Recipe_RelicCapacitor", "Recipe_ChargedCell" }
            },
            new NodeSpec
            {
                AssetName = "res_mil_strategic", NodeId = "res_mil_strategic",
                Display = "Strategic Ordnance",
                Description = "Weapons of the last resort. The Tsar Bomb and the antimatter bomb end a siege - and " +
                              "most of the map around it - in one detonation.",
                Tier = 7, Column = 11, Seconds = 300f, Tint = MilVoid,
                PrerequisiteIds = new[] { "res_mil_explosives", "res_nuclear_fission" },
                RecipeNames = new[] { "Recipe_TsarBomb", "Recipe_AntimatterBomb" }
            },
        };

        public static void RunStep118()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Military Research Setup",
                    "Exit Play Mode before running setup.", "OK");
                return;
            }
            RunInternal(true);
        }

        public static void RunSilent() => RunInternal(false);

        private static void RunInternal(bool interactive)
        {
            var created = new List<string>();
            var linked = new List<string>();
            var missingRecipes = new List<string>();
            var missingPrereqs = new List<string>();

            foreach (var spec in Ladder)
            {
                var existing = ResearchAuthoring.FindNode(spec.NodeId);
                bool wasMissing = existing == null;

                var node = ResearchAuthoring.EnsureNode(
                    spec.AssetName, spec.NodeId, spec.Display, spec.Description,
                    spec.Sub, spec.Tier, spec.Column, spec.Seconds, spec.Tint);
                if (wasMissing) created.Add(node.nodeId);

                // Prerequisites: only written while the node has none.
                var prereqs = new List<ResearchNode>();
                foreach (string id in spec.PrerequisiteIds)
                {
                    var prereq = ResearchAuthoring.FindNode(id);
                    if (prereq == null) missingPrereqs.Add($"{spec.NodeId} -> {id}");
                    else prereqs.Add(prereq);
                }
                ResearchAuthoring.LinkPrerequisites(node, prereqs.ToArray());

                // One science pack per node, sized to where it sits in the ladder.
                var pack = ResearchAuthoring.FindSciencePack(spec.Tier <= 3 ? 1 : spec.Tier <= 5 ? 2 : 3);
                ResearchAuthoring.EnsureScienceCost(node, pack,
                    spec.Tier <= 4 ? 40 : spec.Tier <= 5 ? 60 : 60);

                foreach (string recipeName in spec.RecipeNames)
                {
                    var recipe = ResearchAuthoring.FindRecipe(recipeName);
                    if (recipe == null) { missingRecipes.Add($"{spec.NodeId} -> {recipeName}"); continue; }
                    if (ResearchAuthoring.AddRecipeUnlock(node, recipe)) linked.Add(recipe.name);
                }
            }

            // Give the existing ship-armament node the rest of the ship weapons, so the
            // whole arsenal lives in Military and nowhere else.
            var shipWeapons = ResearchAuthoring.FindNode("res_ship_weapons");
            if (shipWeapons != null)
            {
                foreach (string recipeName in new[] { "Recipe_GWeapon", "Recipe_GAmmo", "Recipe_GArmorSmall", "Recipe_GArmorLarge" })
                {
                    var recipe = ResearchAuthoring.FindRecipe(recipeName);
                    if (recipe == null) { missingRecipes.Add($"res_ship_weapons -> {recipeName}"); continue; }
                    if (ResearchAuthoring.AddRecipeUnlock(shipWeapons, recipe)) linked.Add(recipe.name);
                }
            }
            else missingPrereqs.Add("res_ship_weapons (node missing)");

            int registered = ResearchAuthoring.RegisterInTrees(
                System.Array.ConvertAll(Ladder, spec => ResearchAuthoring.FindNode(spec.NodeId)));

            // THE GATING LAW, in the data: a recipe a node lists is not free.
            int gated = ResearchAuthoring.EnforceLinkedRecipeGating(out List<string> reopened);

            ReportOrphans();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[MilitaryResearchSetup] 17.26.0-dev military branch: {Ladder.Length} nodes " +
                      $"({created.Count} created), {linked.Count} recipe link(s) added, {registered} tree registration(s), " +
                      $"{gated} recipe(s) pulled behind research" +
                      (gated > 0 ? $" [{ResearchAuthoring.Join(reopened)}]" : "") + ".");
            if (missingRecipes.Count > 0)
                Debug.LogWarning($"[MilitaryResearchSetup] Recipes not found (skipped, NOT created): " +
                                 ResearchAuthoring.Join(missingRecipes, 20));
            if (missingPrereqs.Count > 0)
                Debug.LogWarning($"[MilitaryResearchSetup] Prerequisites not found (node still works, just ungated " +
                                 $"by them until they exist): {ResearchAuthoring.Join(missingPrereqs, 20)}");

            if (interactive)
            {
                EditorUtility.DisplayDialog(
                    "Military Research Branch Complete",
                    "Authored and wired:\n" +
                    "• 7 Military nodes: Ballistics & Small Arms, Automated Defences, Field Artillery,\n" +
                    "   High Explosives, Incendiary Warfare, Relic Armaments, Strategic Ordnance\n" +
                    "• Every turret, auto turret, gun, shell, mortar round and bomb now sits behind one of them\n" +
                    "• Ship Armament now also covers ship guns, ammo and both armour plates\n" +
                    $"• {gated} recipe(s) that claimed a node while still being free are now gated\n\n" +
                    (created.Count > 0 ? $"Created: {ResearchAuthoring.Join(created, 8)}\n\n" : "") +
                    "The runtime rule in ResearchManager makes the node's unlock list the authority,\n" +
                    "so re-running a content step can no longer hand the arsenal out for free.",
                    "OK");
            }
        }

        /// <summary>
        /// Diagnostic, not a fix: a recipe flagged as NOT available by default that no
        /// node claims can never be learned by anyone. Those are always content from a
        /// later step that has not been run in this project yet, so the message names
        /// them instead of guessing a branch for them.
        /// </summary>
        private static void ReportOrphans()
        {
            var claimed = new HashSet<RecipeDefinition>(ResearchAuthoring.CollectClaimedRecipes());
            var orphans = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:RecipeDefinition"))
            {
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (recipe == null || recipe.unlockedByDefault) continue;
                if (claimed.Contains(recipe)) continue;
                orphans.Add(recipe.name);
            }
            if (orphans.Count == 0) return;

            Debug.LogWarning($"[MilitaryResearchSetup] {orphans.Count} recipe(s) are gated but no research node " +
                             "claims them, so nothing can ever unlock them yet: " +
                             ResearchAuthoring.Join(orphans, 25) +
                             " - run the matching content steps (Storage, Grids, Ventilation, Nuclear) once; " +
                             "they append these recipes to their own nodes.");
        }
    }
}
#endif
