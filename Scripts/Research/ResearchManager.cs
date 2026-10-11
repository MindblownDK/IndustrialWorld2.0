// Assets/Scripts/VoxelEngine/Research/ResearchManager.cs
//
// Handles research completion, recipe unlocking, and player upgrades.
// Follows IndustrialCrusaders guidelines: modular, clean, and complete.

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.Research
{
    [Serializable]
    public sealed class ResearchRankEntry
    {
        public string nodeId;
        public int rank;

        public ResearchRankEntry() { }
        public ResearchRankEntry(string nodeId, int rank)
        {
            this.nodeId = nodeId;
            this.rank = rank;
        }
    }

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
        private bool _rankLoadPending;

        public event Action OnChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _rankLoadPending = !LoadFromDisk();
            VoxelEngine.Exploration.BlueprintUnlockManager.EnsureInstance();
        }

        private void Update()
        {
            // WorldStatePersistence loads from Start(), after every Awake(). Retry
            // once it has finished so scene execution order cannot erase saved ranks.
            if (_rankLoadPending && LoadFromDisk()) _rankLoadPending = false;
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

            // Orbital gate (11.13.0): some nodes can only be researched aboard a satellite
            // in orbit. Checked here as well as in the UI, because the UI is not the only
            // caller and a gate enforced in one place is a gate that eventually leaks.
            string orbitalBlock = GetFacilityBlockReason(node);
            if (orbitalBlock != null)
            {
                Debug.Log($"[Research] Blocked: {node.displayName} \u2014 {orbitalBlock}");
                return;
            }

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
            // The instant-research path must honour the orbital gate too, or it would be a
            // free bypass around the satellite requirement.
            if (!AreFacilitiesMet(node)) return false;

            // Check affordability
            foreach (var c in node.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                int need = GetEffectiveCount(node, c.count);
                if (CountScience(inventory, c.pack) < need) return false;
            }

            // Consume
            foreach (var c in node.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                int need = GetEffectiveCount(node, c.count);
                RemoveScience(inventory, c.pack, need);
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
        private static int CountScience(ItemContainer inventory, ItemDefinition pack)
        {
            if (inventory == null || pack == null) return 0;
            if (string.IsNullOrEmpty(pack.itemId)) return inventory.CountOf(pack);
            return inventory.CountOfId(pack.itemId);
        }

        private static void RemoveScience(ItemContainer inventory, ItemDefinition pack, int count)
        {
            if (inventory == null || pack == null || count <= 0) return;
            if (string.IsNullOrEmpty(pack.itemId)) inventory.Remove(pack, count);
            else inventory.RemoveId(pack.itemId, count);
        }

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

        /// <summary>Rank of a node by id, for systems that only know the node id —
        /// the warp drive reads its Warp Coil Resonance bonus this way. Zero when the
        /// node is unresearched or the id is unknown.</summary>
        public int GetRank(string nodeId)
            => !string.IsNullOrEmpty(nodeId) && _nodeRanks.TryGetValue(nodeId, out int rank) ? rank : 0;

        /// <summary>
        /// Why this node cannot be researched right now, or null if it can. Covers both
        /// the orbital-lab gate and the boss-relic gate; both research entry points funnel
        /// through here, so a gate added here cannot be bypassed by the other path.
        /// </summary>
        public string GetFacilityBlockReason(ResearchNode node)
        {
            if (node == null) return null;

            // Relic gate (11.20.0). Checked before the orbital gate because it is the
            // harder requirement to satisfy - telling a player to go build a satellite
            // when they actually need to beat a boss would send them the wrong way.
            if (node.requiresRelic != VoxelEngine.Combat.BossRelicKind.None
                && !VoxelEngine.Combat.BossRelicLedger.Has(node.requiresRelic))
            {
                string relic = VoxelEngine.Combat.BossRelicLedger.Label(node.requiresRelic);
                string source = VoxelEngine.Combat.BossRelicLedger.SourceLabel(node.requiresRelic);
                return $"Requires a {relic}, recovered by defeating {source}.";
            }

            if (!node.requiresOrbitalLab) return null;
            return VoxelEngine.GridSystem.GridSatelliteLab.GlobalBlockedReason();
        }

        /// <summary>True when the node's facility requirements are satisfied.</summary>
        public bool AreFacilitiesMet(ResearchNode node) => GetFacilityBlockReason(node) == null;

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

        // ============================================================
        //                    DEBUG / DEV TESTING HELPERS
        // ============================================================
        /// <summary>
        /// Dev convenience driven by Tools ▸ Debug (Spawner): unlocks every node in
        /// the tree at rank 1. Repeatable upgrade nodes stay at rank 1 — use
        /// MaxAllRanks() to take them to their cap. Fires OnChanged once, so
        /// PlayerStats recomputes live; the Research screen rebuilds when it opens.
        /// Testing only: normal progression goes through the Research Lab.
        /// </summary>
        public int UnlockAll()
        {
            return SetRanksTo(maxRanks: false);
        }

        /// <summary>
        /// Dev convenience driven by Tools ▸ Debug (Spawner): takes every node
        /// straight to its maxRanks cap, so the repeatable upgrade nodes (HP,
        /// damage, stamina, sprint, inventory) read as fully stacked. Implies
        /// UnlockAll() for every node.
        /// </summary>
        public int MaxAllRanks()
        {
            return SetRanksTo(maxRanks: true);
        }

        /// <summary>
        /// Dev convenience driven by Tools ▸ Debug (Spawner): clears every rank, so
        /// a test session can start from a fresh tree again without leaving the
        /// editor. Fires OnChanged once so live stats drop back to base values.
        /// </summary>
        public int ResetAllRanks()
        {
            if (_nodeRanks.Count == 0) return 0;
            int changed = _nodeRanks.Count;
            _nodeRanks.Clear();

            // Drop any in-flight lab research without its own event: the single
            // OnChanged below already repaints every listener.
            if (ActiveResearch != null)
            {
                ActiveResearch = null;
                ActiveProgress01 = 0f;
                ActiveHasCost = false;
            }

            Debug.Log("[Research] Debug reset cleared " + changed + " node rank(s).");
            OnChanged?.Invoke();
            SaveToDisk();
            return changed;
        }

        private int SetRanksTo(bool maxRanks)
        {
            if (tree == null) return 0;

            int changed = 0;
            foreach (var n in tree.nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.nodeId)) continue;
                int target = maxRanks ? Mathf.Max(1, n.maxRanks) : 1;
                int rank = GetRank(n);
                if (rank >= target) continue;
                _nodeRanks[n.nodeId] = target;
                changed++;
            }
            if (changed == 0) return 0;

            // A node mid-research must not tick past its cap afterwards; drop it
            // without its own event — the single OnChanged below repaints everyone.
            if (ActiveResearch != null && GetRank(ActiveResearch) >= ActiveResearch.maxRanks)
            {
                ActiveResearch = null;
                ActiveProgress01 = 0f;
                ActiveHasCost = false;
            }

            Debug.Log("[Research] Debug unlock set " + changed + " node(s) to "
                + (maxRanks ? "max rank." : "rank 1."));
            OnChanged?.Invoke();
            SaveToDisk();
            return changed;
        }

        public bool IsRecipeUnlocked(RecipeDefinition recipe)
        {
            if (recipe == null) return false;
            if (recipe.unlockedByDefault) return true;

            // Blueprint unlocks (ruins) — 4.9.0
            try
            {
                if (VoxelEngine.Exploration.BlueprintUnlockManager.Instance != null &&
                    VoxelEngine.Exploration.BlueprintUnlockManager.Instance.IsUnlocked(recipe))
                    return true;
            }
            catch { }

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
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence != null)
                persistence.SaveResearchRanks(_nodeRanks);
            else
                Debug.LogWarning("[Research] WorldStatePersistence is not available; rank changes remain in memory until a save manager is present.");
            Debug.Log("[Research] Progress saved.");
        }

        /// <summary>Called by WorldStatePersistence after it has read this world's
        /// additive research-rank list. Returns false until the world sidecar is ready.</summary>
        public bool LoadSavedRanksFromPersistence()
        {
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null || !persistence.TryCopyResearchRanks(_nodeRanks)) return false;

            if (tree != null && tree.nodes != null)
            {
                foreach (var node in tree.nodes)
                {
                    if (node == null || string.IsNullOrEmpty(node.nodeId)) continue;
                    if (!_nodeRanks.TryGetValue(node.nodeId, out int rank)) continue;
                    int maxRank = Mathf.Max(1, node.maxRanks);
                    if (rank > maxRank) _nodeRanks[node.nodeId] = maxRank;
                }
            }
            _rankLoadPending = false;
            OnChanged?.Invoke();
            Debug.Log("[Research] Progress loaded (" + _nodeRanks.Count + " researched node(s)).");
            return true;
        }

        private bool LoadFromDisk()
        {
            return LoadSavedRanksFromPersistence();
        }
    }
}
