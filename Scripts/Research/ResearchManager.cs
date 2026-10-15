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
        public const int MaxQueueSlots = 5;

        public ResearchNode ActiveResearch { get; private set; }
        public float ActiveProgress01 { get; private set; }
        public bool ActiveHasCost { get; private set; }

        private readonly List<ResearchNode> _researchQueue = new List<ResearchNode>();
        public IReadOnlyList<ResearchNode> ResearchQueue => _researchQueue;
        public int QueuedWaitingCount => _researchQueue.Count;
        public int TotalQueuedCount => (ActiveResearch != null ? 1 : 0) + _researchQueue.Count;

        public bool IsResearchQueueEnabled
        {
            get
            {
                return VoxelEngine.Menu.WorldSession.Instance != null
                    ? VoxelEngine.Menu.WorldSession.Instance.researchQueueEnabled
                    : VoxelEngine.Settings.GameSettings.ResearchQueueEnabled;
            }
        }

        public bool IsTeamSharedResearchEnabled
        {
            get
            {
                return VoxelEngine.Menu.WorldSession.Instance != null
                    ? VoxelEngine.Menu.WorldSession.Instance.teamSharedResearch
                    : VoxelEngine.Settings.GameSettings.TeamSharedResearch;
            }
        }

        // Persistent data: nodeId -> current rank achieved
        private Dictionary<string, int> _nodeRanks = new Dictionary<string, int>();
        private bool _rankLoadPending;

        public event Action OnChanged;
        /// <summary>Fires when known recipes can change, so an open crafting panel can refresh its filters.</summary>
        public event Action OnRecipeUnlocksChanged;

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

            // Remove one matching entry from the waiting queue if this node was promoted from it.
            int qIdx = _researchQueue.IndexOf(node);
            if (qIdx >= 0 && GetRank(node) + 1 >= node.maxRanks)
                _researchQueue.RemoveAt(qIdx);

            Debug.Log($"[Research] Started: {node.displayName}");
            OnChanged?.Invoke();
        }

        public bool EnqueueOrStartResearch(ResearchNode node)
        {
            if (node == null) return false;
            if (GetFacilityBlockReason(node) != null) return false;

            if (ActiveResearch == null)
            {
                if (GetRank(node) >= node.maxRanks || !ArePrerequisitesMet(node)) return false;
                StartResearch(node);
                return ActiveResearch == node;
            }

            if (!IsResearchQueueEnabled)
            {
                if (ActiveResearch == node) return false;
                if (GetRank(node) >= node.maxRanks || !ArePrerequisitesMet(node)) return false;
                StartResearch(node);
                return ActiveResearch == node;
            }

            if (!CanEnqueue(node)) return false;
            _researchQueue.Add(node);
            Debug.Log($"[Research] Queued ({TotalQueuedCount}/{MaxQueueSlots}): {node.displayName}");
            OnChanged?.Invoke();
            return true;
        }

        public bool CanEnqueue(ResearchNode node)
        {
            if (node == null) return false;
            if (GetFacilityBlockReason(node) != null) return false;
            if (GetRankIncludingQueue(node) >= node.maxRanks) return false;
            if (ActiveResearch == null)
                return ArePrerequisitesMet(node);
            if (!IsResearchQueueEnabled)
                return ActiveResearch != node && ArePrerequisitesMet(node);
            if (TotalQueuedCount >= MaxQueueSlots) return false;
            return ArePrerequisitesMetOrQueued(node);
        }

        public int CountInQueue(ResearchNode node)
        {
            if (node == null) return 0;
            int count = ActiveResearch == node ? 1 : 0;
            for (int i = 0; i < _researchQueue.Count; i++)
                if (_researchQueue[i] == node) count++;
            return count;
        }

        public int GetRankIncludingQueue(ResearchNode node, int upToWaitingIndex = int.MaxValue)
        {
            if (node == null) return 0;
            int rank = GetRank(node);
            if (ActiveResearch == node) rank++;
            int limit = Mathf.Min(_researchQueue.Count, upToWaitingIndex);
            for (int i = 0; i < limit; i++)
                if (_researchQueue[i] == node) rank++;
            return rank;
        }

        public bool ArePrerequisitesMetOrQueued(ResearchNode node, int upToWaitingIndex = int.MaxValue)
        {
            if (node == null || node.prerequisites == null) return true;
            foreach (var p in node.prerequisites)
            {
                if (p == null) continue;
                if (GetRankIncludingQueue(p, upToWaitingIndex) <= 0) return false;
            }
            return true;
        }

        public bool IsQueued(ResearchNode node) => node != null && _researchQueue.Contains(node);

        /// <summary>Returns 1-based slot index (1 = ActiveResearch, 2..5 = waiting queue), or 0 if not queued.</summary>
        public int GetQueueSlotNumber(ResearchNode node)
        {
            if (node == null) return 0;
            if (ActiveResearch == node) return 1;
            int idx = _researchQueue.IndexOf(node);
            return idx >= 0 ? idx + 2 : 0;
        }

        public ResearchNode GetQueueSlotNode(int zeroBasedSlot)
        {
            if (zeroBasedSlot == 0) return ActiveResearch;
            int waitIdx = zeroBasedSlot - 1;
            return (waitIdx >= 0 && waitIdx < _researchQueue.Count) ? _researchQueue[waitIdx] : null;
        }

        public void RemoveQueueSlotAt(int zeroBasedSlot)
        {
            if (zeroBasedSlot == 0)
            {
                CancelResearch();
                return;
            }
            int waitIdx = zeroBasedSlot - 1;
            if (waitIdx < 0 || waitIdx >= _researchQueue.Count) return;
            _researchQueue.RemoveAt(waitIdx);
            PruneInvalidQueuedNodes();
            OnChanged?.Invoke();
        }

        public void RemoveFromQueue(ResearchNode node)
        {
            if (node == null) return;
            for (int i = _researchQueue.Count - 1; i >= 0; i--)
            {
                if (_researchQueue[i] == node)
                {
                    _researchQueue.RemoveAt(i);
                    PruneInvalidQueuedNodes();
                    OnChanged?.Invoke();
                    return;
                }
            }
            if (ActiveResearch == node)
                CancelResearch();
        }

        private void PruneInvalidQueuedNodes()
        {
            for (int i = 0; i < _researchQueue.Count; i++)
            {
                var n = _researchQueue[i];
                if (n == null
                    || GetRankIncludingQueue(n, i) >= n.maxRanks
                    || !ArePrerequisitesMetOrQueued(n, i)
                    || GetFacilityBlockReason(n) != null)
                {
                    _researchQueue.RemoveAt(i);
                    i--;
                }
            }
        }

        private void PromoteNextQueuedResearch()
        {
            if (ActiveResearch != null) return;
            if (!IsResearchQueueEnabled)
            {
                _researchQueue.Clear();
                return;
            }
            PruneInvalidQueuedNodes();
            while (_researchQueue.Count > 0)
            {
                var next = _researchQueue[0];
                _researchQueue.RemoveAt(0);
                if (next == null) continue;
                if (GetRank(next) >= next.maxRanks) continue;
                if (!ArePrerequisitesMet(next)) continue;
                if (GetFacilityBlockReason(next) != null) continue;
                ActiveResearch = next;
                ActiveProgress01 = 0f;
                ActiveHasCost = false;
                Debug.Log($"[Research] Auto-started next queued node: {next.displayName}");
                break;
            }
        }

        public void OnResearchSettingsChanged()
        {
            if (!IsResearchQueueEnabled && _researchQueue.Count > 0)
                _researchQueue.Clear();

            if (IsTeamSharedResearchEnabled)
            {
                PushLocalResearchToNetwork();
                VoxelEngine.Networking.NetworkBootstrap.Instance?.SyncAllTeamsResearch();
            }

            OnChanged?.Invoke();
        }

        public void CancelResearch()
        {
            if (ActiveResearch == null) return;

            Debug.Log($"[Research] Canceled: {ActiveResearch.displayName}");
            ActiveResearch = null;
            ActiveProgress01 = 0f;
            ActiveHasCost = false;
            PruneInvalidQueuedNodes();
            PromoteNextQueuedResearch();
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
            PromoteNextQueuedResearch();

            OnChanged?.Invoke();
            if ((node.unlocksRecipes != null && node.unlocksRecipes.Length > 0)
                || string.Equals(node.nodeId, "res_smelting", StringComparison.OrdinalIgnoreCase))
                OnRecipeUnlocksChanged?.Invoke();
            SaveToDisk();
            PushLocalResearchToNetwork();
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
            PruneInvalidQueuedNodes();
            PromoteNextQueuedResearch();
            OnChanged?.Invoke();
            OnRecipeUnlocksChanged?.Invoke();
            SaveToDisk();
            PushLocalResearchToNetwork();
            return changed;
        }

        private static bool IsCanonicalFurnaceRecipe(RecipeDefinition recipe)
        {
            if (recipe == null) return false;
            if (string.Equals(recipe.name, "Recipe_Furnace", StringComparison.OrdinalIgnoreCase)) return true;
            return recipe.outputItem != null
                && string.Equals(recipe.outputItem.itemId, "block_furnace", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsRecipeUnlocked(RecipeDefinition recipe)
        {
            if (recipe == null) return false;
            if (recipe.unlockedByDefault) return true;

            // The canonical furnace recipe is progression-gated by Smelting. This
            // runtime rule keeps an existing Smelting rank effective even when an
            // older ResearchTree asset points at a stale/duplicate recipe reference.
            if (IsCanonicalFurnaceRecipe(recipe) && IsUnlocked("res_smelting")) return true;

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

        [Serializable]
        private sealed class ResearchRankListWrapper
        {
            public List<ResearchRankEntry> ranks = new List<ResearchRankEntry>();
        }

        public List<ResearchRankEntry> ExportRankEntries()
        {
            var list = new List<ResearchRankEntry>(_nodeRanks.Count);
            foreach (var kv in _nodeRanks)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value <= 0) continue;
                list.Add(new ResearchRankEntry(kv.Key, kv.Value));
            }
            return list;
        }

        public string ExportRankJson()
        {
            return JsonUtility.ToJson(new ResearchRankListWrapper { ranks = ExportRankEntries() });
        }

        public static List<ResearchRankEntry> ParseRankJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return new List<ResearchRankEntry>();
            try
            {
                var wrapper = JsonUtility.FromJson<ResearchRankListWrapper>(json);
                return wrapper?.ranks ?? new List<ResearchRankEntry>();
            }
            catch
            {
                return new List<ResearchRankEntry>();
            }
        }

        public static string SerializeRankEntries(IEnumerable<ResearchRankEntry> entries)
        {
            var list = entries != null ? new List<ResearchRankEntry>(entries) : new List<ResearchRankEntry>();
            return JsonUtility.ToJson(new ResearchRankListWrapper { ranks = list });
        }

        public bool MergeRankEntries(IEnumerable<ResearchRankEntry> entries, bool replaceExisting = false)
        {
            bool changed = false;
            if (replaceExisting && _nodeRanks.Count > 0)
            {
                _nodeRanks.Clear();
                changed = true;
            }
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.nodeId) || entry.rank <= 0) continue;
                    int clampedRank = entry.rank;
                    if (tree != null && tree.nodes != null)
                    {
                        foreach (var node in tree.nodes)
                        {
                            if (node != null && string.Equals(node.nodeId, entry.nodeId, StringComparison.Ordinal))
                            {
                                clampedRank = Mathf.Min(clampedRank, Mathf.Max(1, node.maxRanks));
                                break;
                            }
                        }
                    }
                    if (!_nodeRanks.TryGetValue(entry.nodeId, out int current) || clampedRank > current)
                    {
                        _nodeRanks[entry.nodeId] = clampedRank;
                        changed = true;
                    }
                }
            }
            if (changed)
            {
                if (ActiveResearch != null && GetRank(ActiveResearch) >= ActiveResearch.maxRanks)
                {
                    ActiveResearch = null;
                    ActiveProgress01 = 0f;
                    ActiveHasCost = false;
                }
                PruneInvalidQueuedNodes();
                PromoteNextQueuedResearch();
                OnChanged?.Invoke();
                OnRecipeUnlocksChanged?.Invoke();
                if (VoxelEngine.Networking.NetworkSession.Mode != VoxelEngine.Networking.SessionMode.Client)
                    SaveToDisk();
            }
            return changed;
        }

        public bool ApplySyncedRanksJson(string json)
        {
            var entries = ParseRankJson(json);
            return MergeRankEntries(entries, replaceExisting: false);
        }

        public void PushLocalResearchToNetwork()
        {
            var net = VoxelEngine.Networking.NetworkBootstrap.Instance;
            if (net == null || !net.IsOnline) return;
            if (VoxelEngine.Networking.NetworkSession.Mode == VoxelEngine.Networking.SessionMode.Client)
                net.SendResearchRanks(ExportRankJson());
            else if (VoxelEngine.Networking.NetworkSession.Mode == VoxelEngine.Networking.SessionMode.Host)
                net.SyncAllTeamsResearch();
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
            OnRecipeUnlocksChanged?.Invoke();
            Debug.Log("[Research] Progress loaded (" + _nodeRanks.Count + " researched node(s)).");
            return true;
        }

        private bool LoadFromDisk()
        {
            return LoadSavedRanksFromPersistence();
        }
    }
}
