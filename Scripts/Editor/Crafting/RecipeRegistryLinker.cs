// Assets/Scripts/Editor/Crafting/RecipeRegistryLinker.cs
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Append-only recovery for a cleared or incomplete RecipeRegistry. It links only
    /// complete, craftable RecipeDefinition assets, preserves authored recipe data and
    /// ResearchNode unlock arrays, and leaves ambiguous duplicate assets for review.
    /// </summary>
    public static class RecipeRegistryLinker
    {
        private const string AssetRoot = "Assets/VoxelEngineAssets";
        private const string RegistryPath = AssetRoot + "/RecipeRegistry.asset";
        private const string RetiredSmallGridDoorPath =
            AssetRoot + "/GridSystem/Recipes/Recipe_SmallGridSlidingDoor.asset";

        private static readonly HashSet<string> RetiredRecipeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Recipe_EmptyBarrel",
            "Recipe_WaterBucket",
            "Recipe_FireIgniter",
            "Recipe_GThrustLarge",
            "Recipe_Locomotive",
            "Recipe_GItemPipe",
            "Recipe_GGasPipe",
            "Recipe_GGasPipeLarge",
            "Recipe_GLiquidPipe",
            "Recipe_GLiquidPipeLarge",
            "Recipe_MEncasedChainDrive",
        };

        private sealed class Candidate
        {
            public RecipeDefinition recipe;
            public string path;
        }

        public static void LinkMissingCraftingRecipes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Recipe Registry Linker",
                    "Exit Play Mode before linking recipe assets.", "OK");
                return;
            }

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(RegistryPath);
            bool createRegistry = registry == null;
            if (createRegistry)
            {
                var conflictingAsset = AssetDatabase.LoadMainAssetAtPath(RegistryPath);
                if (conflictingAsset != null)
                {
                    EditorUtility.DisplayDialog("Recipe Registry Linker",
                        $"'{RegistryPath}' exists but is not a RecipeRegistry. It was left untouched.", "OK");
                    return;
                }

                var otherRegistryPaths = FindAssetPaths<RecipeRegistry>()
                    .Where(path => !string.Equals(path, RegistryPath, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (otherRegistryPaths.Length > 0)
                {
                    EditorUtility.DisplayDialog("Recipe Registry Linker",
                        "The canonical RecipeRegistry asset is missing, but another registry asset exists:\n\n" +
                        string.Join("\n", otherRegistryPaths.Take(8)) +
                        (otherRegistryPaths.Length > 8 ? $"\n... and {otherRegistryPaths.Length - 8} more" : "") +
                        "\n\nNo competing registry was created. Restore the canonical asset or decide which registry should be authoritative first.",
                        "OK");
                    return;
                }

                if (!AssetDatabase.IsValidFolder(AssetRoot))
                {
                    EditorUtility.DisplayDialog("Recipe Registry Linker",
                        $"The canonical asset folder '{AssetRoot}' is missing. No registry was created.", "OK");
                    return;
                }
            }

            var existingLinks = new HashSet<RecipeDefinition>();
            if (registry != null && registry.recipes != null)
                foreach (var recipe in registry.recipes)
                    if (recipe != null) existingLinks.Add(recipe);

            CollectResearchReferences(out var researchLinks, out int researchReferenceSlots,
                out int nullResearchReferenceSlots);

            var candidates = new List<Candidate>();
            int scannedAssets = 0;
            int skippedInvalid = 0;
            int skippedRetired = 0;
            int skippedBuildTokens = 0;
            int skippedDuplicateScience = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:RecipeDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
                if (recipe == null) continue;
                scannedAssets++;

                if (IsRetiredRecipe(recipe, path))
                {
                    skippedRetired++;
                    continue;
                }
                if (IsBuildTokenRecipe(recipe, path))
                {
                    skippedBuildTokens++;
                    continue;
                }
                if (IsNonCanonicalScienceRecipe(recipe, path))
                {
                    skippedDuplicateScience++;
                    continue;
                }
                if (!IsCompleteCraftingRecipe(recipe))
                {
                    skippedInvalid++;
                    continue;
                }

                candidates.Add(new Candidate { recipe = recipe, path = path });
            }

            var additions = new HashSet<RecipeDefinition>();
            int ambiguousDuplicateAssets = 0;
            var ambiguousExamples = new List<string>();
            foreach (var group in candidates.GroupBy(candidate => candidate.recipe.name,
                         StringComparer.OrdinalIgnoreCase))
            {
                var members = group.ToList();
                foreach (var candidate in members)
                {
                    if (!researchLinks.Contains(candidate.recipe) || existingLinks.Contains(candidate.recipe)) continue;
                    additions.Add(candidate.recipe);
                }

                var unlinkedUnreferenced = members
                    .Where(candidate => !existingLinks.Contains(candidate.recipe)
                        && !researchLinks.Contains(candidate.recipe))
                    .ToList();

                if (members.Count == 1)
                {
                    if (unlinkedUnreferenced.Count == 1)
                        additions.Add(unlinkedUnreferenced[0].recipe);
                    continue;
                }

                // Same-name assets can be stale copies from earlier Setup runs. Existing
                // registry membership and serialized ResearchNode references are the only
                // safe signals for selecting among them; never guess or overwrite a link.
                ambiguousDuplicateAssets += unlinkedUnreferenced.Count;
                foreach (var candidate in unlinkedUnreferenced.Take(12 - ambiguousExamples.Count))
                    ambiguousExamples.Add(candidate.path);
            }

            int researchReferencesNotRestored = researchLinks.Count(recipe =>
                !existingLinks.Contains(recipe) && !additions.Contains(recipe));
            int lockedRecipesWithoutResearchNode = candidates
                .Select(candidate => candidate.recipe)
                .Where(recipe => (existingLinks.Contains(recipe) || additions.Contains(recipe))
                    && !recipe.unlockedByDefault && !researchLinks.Contains(recipe))
                .Distinct()
                .Count();
            int existingNonNullLinks = registry != null && registry.recipes != null
                ? registry.recipes.Count(recipe => recipe != null)
                : 0;

            string ambiguityDetails = ambiguousExamples.Count == 0
                ? string.Empty
                : "\nAmbiguous recipe paths left for manual review:\n - "
                    + string.Join("\n - ", ambiguousExamples);

            string preview =
                $"Canonical registry: {RegistryPath}\n" +
                $"Registry will be {(createRegistry ? "created" : "updated")}; existing links: {existingNonNullLinks}\n" +
                $"Crafting recipe assets scanned: {scannedAssets}\n" +
                $"Complete recipe assets eligible: {candidates.Count}\n" +
                $"Missing links to append: {additions.Count}\n" +
                $"Skipped - invalid/incomplete: {skippedInvalid}\n" +
                $"Skipped - retired: {skippedRetired}\n" +
                $"Skipped - build-token: {skippedBuildTokens}\n" +
                $"Skipped - duplicate science-pack assets: {skippedDuplicateScience}\n" +
                $"Ambiguous same-name assets left unlinked: {ambiguousDuplicateAssets}\n" +
                ambiguityDetails + "\n" +
                $"Research recipe references found: {researchReferenceSlots} ({researchLinks.Count} unique)\n" +
                $"Research references not restored: {researchReferencesNotRestored}\n" +
                $"Locked recipes without a ResearchNode reference: {lockedRecipesWithoutResearchNode}\n" +
                $"Null entries in ResearchNode unlock arrays: {nullResearchReferenceSlots}\n\n" +
                "This appends links only. It does not edit recipes, remove existing registry entries, or rewrite research nodes.";

            if (additions.Count == 0)
            {
                string noChange = createRegistry
                    ? "No complete, unambiguous recipes were found; no empty registry was created.\n\n"
                    : "No missing, unambiguous recipe links were found.\n\n";
                EditorUtility.DisplayDialog("Recipe Registry Linker", noChange + preview, "OK");
                Debug.Log("[RecipeRegistryLinker] No changes required.\n" + preview);
                return;
            }

            if (!EditorUtility.DisplayDialog("Link Crafting Recipes?", preview,
                    "Append missing links", "Cancel"))
                return;

            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<RecipeRegistry>();
                AssetDatabase.CreateAsset(registry, RegistryPath);
            }

            Undo.RecordObject(registry, "Link crafting recipes to RecipeRegistry");
            if (registry.recipes == null) registry.recipes = new List<RecipeDefinition>();
            int added = 0;
            foreach (var recipe in additions.OrderBy(recipe => AssetDatabase.GetAssetPath(recipe),
                         StringComparer.OrdinalIgnoreCase))
            {
                if (recipe == null || registry.recipes.Contains(recipe)) continue;
                registry.recipes.Add(recipe);
                added++;
            }

            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary =
                $"RecipeRegistry links appended: {added}\n" +
                $"Existing links preserved: {existingNonNullLinks}\n" +
                $"Research unlock arrays changed: 0\n" +
                $"Ambiguous same-name assets left unlinked: {ambiguousDuplicateAssets}\n" +
                $"Locked recipes without a ResearchNode reference: {lockedRecipesWithoutResearchNode}\n\n" +
                "Run the Recipe Graph Validator to review the resulting active graph.";
            Debug.Log("[RecipeRegistryLinker] Complete.\n" + preview + "\n" + summary);
            EditorUtility.DisplayDialog("Recipe Registry Linker Complete", summary, "OK");
        }

        private static void CollectResearchReferences(out HashSet<RecipeDefinition> recipes,
            out int referenceSlots, out int nullSlots)
        {
            recipes = new HashSet<RecipeDefinition>();
            referenceSlots = 0;
            nullSlots = 0;
            foreach (string path in FindAssetPaths<ResearchNode>())
            {
                var node = AssetDatabase.LoadAssetAtPath<ResearchNode>(path);
                if (node == null || node.unlocksRecipes == null) continue;
                foreach (var recipe in node.unlocksRecipes)
                {
                    if (recipe == null)
                    {
                        nullSlots++;
                        continue;
                    }
                    referenceSlots++;
                    recipes.Add(recipe);
                }
            }
        }

        private static IEnumerable<string> FindAssetPaths<T>() where T : UnityEngine.Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct();
        }

        private static bool IsCompleteCraftingRecipe(RecipeDefinition recipe)
        {
            if (recipe == null || recipe.outputItem == null || recipe.outputCount <= 0
                || recipe.inputs == null || recipe.inputs.Length == 0)
                return false;
            foreach (var input in recipe.inputs)
                if (input.item == null || input.count <= 0) return false;
            return true;
        }

        private static bool IsBuildTokenRecipe(RecipeDefinition recipe, string path)
        {
            if (recipe.outputItem is VoxelEngine.Building.Tiered.BuildToken) return true;
            string fileName = Path.GetFileNameWithoutExtension(path);
            return fileName.StartsWith("Recipe_Tok_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNonCanonicalScienceRecipe(RecipeDefinition recipe, string path)
        {
            if (!(recipe.outputItem is ScienceItem science)) return false;
            string canonicalPath =
                $"{AssetRoot}/Recipes/Recipe_ScienceT{science.EffectiveTier}.asset";
            return !string.Equals(path.Replace('\\', '/'), canonicalPath,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRetiredRecipe(RecipeDefinition recipe, string path)
        {
            string normalizedPath = (path ?? string.Empty).Replace('\\', '/');
            if (string.Equals(normalizedPath, RetiredSmallGridDoorPath,
                    StringComparison.OrdinalIgnoreCase))
                return true;
            return RetiredRecipeNames.Contains(recipe.name);
        }
    }
}
#endif
