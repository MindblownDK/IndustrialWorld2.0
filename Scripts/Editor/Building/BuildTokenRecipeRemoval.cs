// Assets/Scripts/Editor/Building/BuildTokenRecipeRemoval.cs
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building.Tiered;
using VoxelEngine.Crafting;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Removes craftable build-token recipes. Token items stay, so an old
    /// inventory can still hold one, and the hammer wheel remains the way to
    /// choose a family. The hammer recipe is not a token recipe and is kept.
    /// </summary>
    public static class BuildTokenRecipeRemoval
    {
        public static void Run()
        {
            int deleted = Remove(out int unregistered, out int researchStripped);
            AssetDatabase.SaveAssets();
            string message =
                "Build-token crafting recipes removed.\n\n" +
                $"Recipe assets deleted: {deleted}\n" +
                $"Registry links removed: {unregistered}\n" +
                $"Research unlocks cleared: {researchStripped}\n\n" +
                "Token items and the Hammer recipe were kept. Place pieces from the hammer wheel.";
            Debug.Log("[BuildTokenRecipeRemoval] " + message.Replace("\n", " "));
            EditorUtility.DisplayDialog("Voxel Engine Setup", message, "OK");
        }

        public static int Remove(out int unregistered, out int researchStripped)
        {
            unregistered = 0;
            researchStripped = 0;
            var doomed = new List<RecipeDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:RecipeDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
                if (IsBuildTokenRecipe(recipe, path)) doomed.Add(recipe);
            }

            foreach (var registry in LoadAssets<RecipeRegistry>())
            {
                if (registry.recipes == null) continue;
                int before = registry.recipes.Count;
                registry.recipes.RemoveAll(recipe =>
                    recipe != null && IsBuildTokenRecipe(recipe, AssetDatabase.GetAssetPath(recipe)));
                int delta = before - registry.recipes.Count;
                if (delta <= 0) continue;
                EditorUtility.SetDirty(registry);
                unregistered += delta;
            }

            foreach (var node in LoadAssets<ResearchNode>())
            {
                if (node.unlocksRecipes == null || node.unlocksRecipes.Length == 0) continue;
                var kept = new List<RecipeDefinition>(node.unlocksRecipes.Length);
                int stripped = 0;
                foreach (var recipe in node.unlocksRecipes)
                {
                    if (recipe != null && IsBuildTokenRecipe(recipe, AssetDatabase.GetAssetPath(recipe)))
                    {
                        stripped++;
                        continue;
                    }
                    kept.Add(recipe);
                }
                if (stripped == 0) continue;
                node.unlocksRecipes = kept.ToArray();
                EditorUtility.SetDirty(node);
                researchStripped += stripped;
            }

            int deleted = 0;
            foreach (var recipe in doomed)
            {
                string path = AssetDatabase.GetAssetPath(recipe);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.DeleteAsset(path)) deleted++;
            }
            return deleted;
        }

        private static bool IsBuildTokenRecipe(RecipeDefinition recipe, string path)
        {
            if (recipe == null) return false;
            if (recipe.outputItem is BuildToken) return true;
            if (string.IsNullOrEmpty(path)) return false;
            return Path.GetFileNameWithoutExtension(path)
                .StartsWith("Recipe_Tok_", StringComparison.Ordinal);
        }

        private static List<T> LoadAssets<T>() where T : UnityEngine.Object
        {
            var list = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) list.Add(asset);
            }
            return list;
        }
    }
}
#endif
