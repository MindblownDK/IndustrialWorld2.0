// Assets/Scripts/Editor/Progression/ResearchAuthoring.cs
//
// 17.26.0-dev - shared authoring helpers for the research steps.
//
// Three wizard steps author research content (military armament, forestry/biofuel
// gating, alloy/metallurgy gating). They all need the same four things: find a node
// by id, find a recipe by asset name, put a node into every ResearchTree asset, and
// make the "a node's unlock list is the gate" law true in the data.
//
// Everything here is NON-DESTRUCTIVE: creations fill only missing fields, links are
// appended, and nothing that already exists is rewritten. Balance values a designer
// has touched by hand survive every run.
//
// Editor-only. Nothing in this file is compiled into the player.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    /// <summary>Shared, idempotent authoring helpers for the research steps.</summary>
    public static class ResearchAuthoring
    {
        public const string NodeFolder = "Assets/VoxelEngineAssets/Research/Nodes";
        private const string FactoryRecipeFolder = "Assets/VoxelEngineAssets/Recipes";

        // ── Lookups ─────────────────────────────────────────────────────────────

        /// <summary>Every ResearchNode asset in the project.</summary>
        public static List<ResearchNode> AllNodes()
        {
            var found = new List<ResearchNode>();
            foreach (string guid in AssetDatabase.FindAssets("t:ResearchNode"))
            {
                var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(AssetDatabase.GUIDToAssetPath(guid));
                if (node != null && !found.Contains(node)) found.Add(node);
            }
            return found;
        }

        /// <summary>Node by nodeId, or null when this project has no such node yet.</summary>
        public static ResearchNode FindNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;
            foreach (var node in AllNodes())
                if (string.Equals(node.nodeId, nodeId, System.StringComparison.OrdinalIgnoreCase))
                    return node;
            return null;
        }

        /// <summary>
        /// Recipe by asset name, e.g. "Recipe_Turret". Looks in the factory recipe
        /// folder first (where most of them live), then anywhere in the project.
        /// </summary>
        public static RecipeDefinition FindRecipe(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return null;

            var direct = AssetDatabase.LoadAssetAtPath<RecipeDefinition>($"{FactoryRecipeFolder}/{assetName}.asset");
            if (direct != null) return direct;

            foreach (string guid in AssetDatabase.FindAssets($"{assetName} t:RecipeDefinition"))
            {
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (recipe == null) continue;
                if (string.Equals(recipe.name, assetName, System.StringComparison.OrdinalIgnoreCase)) return recipe;
            }
            // Last resort: any asset whose file name matches, whatever folder it is in.
            foreach (string guid in AssetDatabase.FindAssets($"{assetName} t:RecipeDefinition"))
                return AssetDatabase.LoadAssetAtPath<RecipeDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            return null;
        }

        /// <summary>Science pack of a tier (1..3). Null when the item is missing.</summary>
        public static ScienceItem FindSciencePack(int tier)
        {
            int clamped = Mathf.Clamp(tier, 1, 3);
            return AssetDatabase.LoadAssetAtPath<ScienceItem>(
                $"Assets/VoxelEngineAssets/Items/Item_ScienceT{clamped}.asset");
        }

        // ── Node authoring ──────────────────────────────────────────────────────

        /// <summary>
        /// Create-or-load a node. On creation the identity fields are filled; on an
        /// existing node only a blank nodeId is repaired. Design values (tier, column,
        /// cost, prerequisites) are NEVER overwritten here - the callers ask for them
        /// explicitly and only when the node is still empty.
        /// </summary>
        public static ResearchNode EnsureNode(
            string assetName, string nodeId, string displayName, string description,
            ResearchSubCategory subCategory, int tier, int column, float researchSeconds, Color iconTint)
        {
            EnsureFolder("Assets/VoxelEngineAssets", "Research");
            EnsureFolder("Assets/VoxelEngineAssets/Research", "Nodes");

            string path = $"{NodeFolder}/{assetName}.asset";
            var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(path);
            bool created = node == null;
            if (created)
            {
                node = ScriptableObject.CreateInstance<ResearchNode>();
                AssetDatabase.CreateAsset(node, path);
            }

            if (created || string.IsNullOrEmpty(node.nodeId)) node.nodeId = nodeId;
            if (created || string.IsNullOrEmpty(node.displayName)) node.displayName = displayName;
            if (created || string.IsNullOrEmpty(node.description)) node.description = description;
            if (created)
            {
                node.category = ResearchCategory.Environment;
                node.subCategory = subCategory;
                node.tier = Mathf.Clamp(tier, 1, 10);
                node.column = column;
                node.researchSeconds = researchSeconds;
                node.iconTint = iconTint;
            }
            EditorUtility.SetDirty(node);
            return node;
        }

        /// <summary>Append a recipe to the node's unlock list (never duplicates).</summary>
        public static bool AddRecipeUnlock(ResearchNode node, RecipeDefinition recipe)
        {
            if (node == null || recipe == null) return false;

            var list = new List<RecipeDefinition>(node.unlocksRecipes ?? System.Array.Empty<RecipeDefinition>());
            foreach (var existing in list)
                if (existing == recipe || (existing != null && existing.name == recipe.name)) return false;

            list.Add(recipe);
            node.unlocksRecipes = list.ToArray();
            EditorUtility.SetDirty(node);
            return true;
        }

        /// <summary>Set prerequisites, but only while the node has none.</summary>
        public static bool LinkPrerequisites(ResearchNode node, params ResearchNode[] prerequisites)
        {
            if (node == null) return false;
            if (node.prerequisites != null && node.prerequisites.Length > 0) return false;

            var list = new List<ResearchNode>();
            foreach (var p in prerequisites)
                if (p != null && p != node && !list.Contains(p)) list.Add(p);
            if (list.Count == 0) return false;

            node.prerequisites = list.ToArray();
            EditorUtility.SetDirty(node);
            return true;
        }

        /// <summary>Set the science cost, but only while the node's cost is empty.</summary>
        public static bool EnsureScienceCost(ResearchNode node, ScienceItem pack, int count)
        {
            if (node == null || pack == null || count <= 0) return false;
            if (node.cost != null && node.cost.Length > 0) return false;

            node.cost = new[] { new ResearchNode.ScienceCost { pack = pack, count = count } };
            EditorUtility.SetDirty(node);
            return true;
        }

        /// <summary>Register nodes in every ResearchTree asset in the project.</summary>
        public static int RegisterInTrees(params ResearchNode[] nodes)
        {
            int added = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:ResearchTree"))
            {
                var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(AssetDatabase.GUIDToAssetPath(guid));
                if (tree == null) continue;
                if (tree.nodes == null) tree.nodes = new List<ResearchNode>();

                foreach (var node in nodes)
                {
                    if (node == null || tree.nodes.Contains(node)) continue;
                    tree.nodes.Add(node);
                    added++;
                }
                EditorUtility.SetDirty(tree);
            }
            return added;
        }

        // ── The gating law ──────────────────────────────────────────────────────

        /// <summary>
        /// 17.26.0-dev - THE GATING LAW, applied to the data: a recipe that any
        /// research node lists is NOT available by default.
        ///
        /// The content steps in Tools > Voxel Engine > Voxel Engine Setup write
        /// unlockedByDefault = true on every run, so a recipe could sit in a research
        /// node's unlock list and still be craftable from the first minute - which is
        /// how "the Military branch is empty" and "nothing is actually gated" happen at
        /// once. The runtime rule in ResearchManager.IsRecipeUnlocked already refuses to
        /// hand out a claimed recipe, and this pass makes the assets agree with it so
        /// the crafting screens, tooltips and the research cards all tell the same
        /// story.
        ///
        /// Only that one flag is written. Costs, times, stations and item links are
        /// never touched.
        /// </summary>
        public static int EnforceLinkedRecipeGating(out List<string> reopened)
        {
            reopened = new List<string>();
            var claimed = CollectClaimedRecipes();
            if (claimed.Count == 0) return 0;

            int changed = 0;
            foreach (var recipe in claimed)
            {
                if (recipe == null || !recipe.unlockedByDefault) continue;
                recipe.unlockedByDefault = false;
                EditorUtility.SetDirty(recipe);
                reopened.Add(recipe.name);
                changed++;
            }
            return changed;
        }

        /// <summary>Every recipe listed by at least one research node.</summary>
        public static List<RecipeDefinition> CollectClaimedRecipes()
        {
            var claimed = new List<RecipeDefinition>();
            foreach (var node in AllNodes())
            {
                if (node.unlocksRecipes == null) continue;
                foreach (var recipe in node.unlocksRecipes)
                {
                    if (recipe == null || claimed.Contains(recipe)) continue;
                    claimed.Add(recipe);
                }
            }
            return claimed;
        }

        // ── Small utilities ─────────────────────────────────────────────────────

        public static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
                AssetDatabase.CreateFolder(parent, child);
        }

        /// <summary>Human-readable "a, b, c" for the completion dialog.</summary>
        public static string Join(List<string> parts, int max = 12)
        {
            if (parts == null || parts.Count == 0) return "none";
            int shown = Mathf.Min(parts.Count, max);
            string text = string.Join(", ", parts.GetRange(0, shown));
            if (parts.Count > shown) text += $" (+{parts.Count - shown} more)";
            return text;
        }
    }
}
#endif
