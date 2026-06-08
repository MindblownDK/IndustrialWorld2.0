// Assets/Scripts/VoxelEngine/Research/ResearchManager.cs
//
// Handles research completion and recipe unlocking.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;

namespace VoxelEngine.Research
{
    public class ResearchManager : MonoBehaviour
    {
        public static ResearchManager Instance { get; private set; }

        private HashSet<string> _unlockedResearch = new HashSet<string>();

        private void Awake()
        {
            if (Instance != null && Instance != this) Destroy(gameObject);
            else Instance = this;
        }

        public void CompleteResearch(string researchId)
        {
            _unlockedResearch.Add(researchId);
            Debug.Log($"[Research] Unlocked: {researchId}");

            // Unlock associated recipes
            var recipes = ResearchRecipeLinker.GetUnlockedRecipes(researchId);
            foreach (var recipe in recipes)
            {
                // TODO: Add to player's known recipes
                Debug.Log($"[Research] Unlocked recipe: {recipe.name}");
            }
        }

        public bool IsUnlocked(string researchId)
        {
            return _unlockedResearch.Contains(researchId);
        }
    }
}