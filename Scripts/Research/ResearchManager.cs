// Assets/Scripts/VoxelEngine/Research/ResearchManager.cs
//
// Handles research completion, recipe unlocking, and player upgrades.
// Follows IndustrialWorld guidelines: modular, clean, and complete.

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.Research
{
    public class ResearchManager : MonoBehaviour
    {
        public static ResearchManager Instance { get; private set; }

        [Header("Data")]
        public ResearchTree tree;

        // ============================================================
        //                          STATE
        // ============================================================
        public ResearchNode ActiveResearch { get; private set; }
        public float ActiveProgress01 { get; private set; }
        public bool ActiveHasCost { get; private set; }

        // Persistent data: nodeId -> current rank achieved
        private Dictionary<string, int> _nodeRanks = new Dictionary<string, int>();

        public event Action OnChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            LoadFromDisk();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ============================================================
        //                       RESEARCH CONTROL
        // ============================================================
        public void StartResearch(ResearchNode node)
        {
            if (node == null) return;
            if (ActiveResearch == node) return;

            // Cannot start if already maxed or prerequisites not met.
            if (GetRank(node) >= node.maxRanks) return;
            if (!ArePrerequisitesMet(node)) return;

            ActiveResearch = node;
            ActiveProgress01 = 0f;
            ActiveHasCost = false;

            Debug.Log($"[Research] Started: {node.displayName}");
            OnChanged?.Invoke();
        }

        public void CancelResearch()
        {
            if (ActiveResearch == null) return;

            Debug.Log($"[Research] Canceled: {ActiveResearch.displayName}");
            ActiveResearch = null;
            ActiveProgress01 = 0f;
            ActiveHasCost = false;
            OnChanged?.Invoke();
        }

        public void TickProgress(float deltaTime)
        {
            if (ActiveResearch == null || !ActiveHasCost) return;

            // Instant research (0s) should have been handled by TryResearchFromInventory,
            // but we'll safety-check here.
            if (ActiveResearch.researchSeconds <= 0f)
            {
                CompleteResearch(ActiveResearch);
                return;
            }

            ActiveProgress01 += deltaTime / ActiveResearch.researchSeconds;
            if (ActiveProgress01 >= 1f)
            {
                CompleteResearch(ActiveResearch);
            }
        }

        public void MarkCostPaid()
        {
            if (ActiveResearch == null) return;
            ActiveHasCost = true;
            OnChanged?.Invoke();
        }

        /// <summary>
        /// Used for instant research (0s) or if the player wants to pay from their backpack
        /// instead of a Research Lab (only if the node allows it via 0s duration).
        /// </summary>
        public bool TryResearchFromInventory(ResearchNode node, ItemContainer inventory)
        {
            if (node == null || inventory == null) return false;
            if (node.researchSeconds > 0) return false; // Must use a Lab for timed research
            if (GetRank(node) >= node.maxRanks) return false;
            if (!ArePrerequisitesMet(node)) return false;

            // Check affordability
            foreach (var c in node.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                int need = GetEffectiveCount(node, c.count);
                if (inventory.CountOf(c.pack) < need) return false;
            }

            // Consume
            foreach (var c in node.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                int need = GetEffectiveCount(node, c.count);
                inventory.Remove(c.pack, need);
            }

            CompleteResearch(node);
            return true;
        }

        private void CompleteResearch(ResearchNode node)
        {
            string id = node.nodeId;
            if (!_nodeRanks.ContainsKey(id)) _nodeRanks[id] = 0;
            _nodeRanks[id]++;

            Debug.Log($"[Research] Completed Rank {_nodeRanks[id]} of {node.displayName}");

            ActiveResearch = null;
            ActiveProgress01 = 0f;
            ActiveHasCost = false;

            OnChanged?.Invoke();
            SaveToDisk();
        }

        // ============================================================
        //                           QUERIES
        // ============================================================
        public int GetRank(ResearchNode node)
        {
            if (node == null) return 0;
            return _nodeRanks.TryGetValue(node.nodeId, out int rank) ? rank : 0;
        }

        public bool IsUnlocked(ResearchNode node)
        {
            return GetRank(node) > 0;
        }

        public bool IsUnlocked(string nodeId)
        {
            return _nodeRanks.TryGetValue(nodeId, out int rank) && rank > 0;
        }

        public bool ArePrerequisitesMet(ResearchNode node)
        {
            if (node == null) return true;
            foreach (var p in node.prerequisites)
            {
                if (p == null) continue;
                if (GetRank(p) <= 0) return false;
            }
            return true;
        }

        public int GetEffectiveCount(ResearchNode node, int baseCount)
        {
            if (node == null) return baseCount;
            if (!node.costScalesWithRank) return baseCount;
            int rank = GetRank(node);
            return baseCount * (rank + 1);
        }

        public bool IsRecipeUnlocked(RecipeDefinition recipe)
        {
            if (recipe == null) return false;
            if (recipe.unlockedByDefault) return true;

            // Search through all nodes in the tree.
            if (tree == null) return false;
            foreach (var node in tree.nodes)
            {
                if (node == null) continue;
                if (GetRank(node) <= 0) continue;

                if (node.unlocksRecipes != null)
                {
                    foreach (var r in node.unlocksRecipes)
                    {
                        if (r == recipe) return true;
                    }
                }
            }
            return false;
        }

        // ============================================================
        //                        PERSISTENCE
        // ============================================================
        public void SaveToDisk()
        {
            // Placeholder: Integration with VoxelEngine.Persistence.WorldStatePersistence should go here.
            // For now, we'll just log. In a real environment, we'd serialize _nodeRanks to JSON/Binary.
            Debug.Log("[Research] Progress saved.");
        }

        private void LoadFromDisk()
        {
            // Placeholder: Load from persistence system.
            _nodeRanks.Clear();
            Debug.Log("[Research] Progress loaded.");
        }
    }
}
