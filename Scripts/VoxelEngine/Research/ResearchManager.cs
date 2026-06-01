// Assets/Scripts/VoxelEngine/Research/ResearchManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.Research
{
    /// <summary>
    /// Per-world unlock state + active research progress + ranks for repeatable nodes.
    /// Persists to {persistentDataPath}/VoxelWorlds/{world}/research.json.
    /// </summary>
    public class ResearchManager : MonoBehaviour
    {
        public static ResearchManager Instance { get; private set; }

        public ResearchTree tree;

        // Runtime state.
        public Dictionary<string, int> RankByNode { get; private set; } = new();
        public ResearchNode ActiveResearch { get; private set; }
        public float ActiveProgress01 { get; private set; }
        public bool  ActiveHasCost    { get; private set; }
        public event Action OnChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadFromDisk();
        }

        private void OnDestroy()
        {
            if (Instance == this) { SaveToDisk(); Instance = null; }
        }
        private void OnApplicationQuit() => SaveToDisk();

        // ============================================================
        //                          QUERIES
        // ============================================================
        public int GetRank(ResearchNode n)
            => n != null && RankByNode.TryGetValue(n.nodeId, out var r) ? r : 0;

        public bool IsUnlocked(ResearchNode n) => GetRank(n) > 0;
        public bool IsUnlocked(string id)
            => !string.IsNullOrEmpty(id) && RankByNode.TryGetValue(id, out var r) && r > 0;

        public bool ArePrerequisitesMet(ResearchNode n)
        {
            if (n?.prerequisites == null) return true;
            foreach (var p in n.prerequisites)
                if (p != null && !IsUnlocked(p)) return false;
            return true;
        }

        public bool IsRecipeUnlocked(RecipeDefinition r)
        {
            if (r == null) return false;
            if (r.unlockedByDefault) return true;
            if (tree == null) return false;
            foreach (var n in tree.nodes)
            {
                if (n == null || n.unlocksRecipes == null) continue;
                foreach (var rec in n.unlocksRecipes)
                    if (rec == r && IsUnlocked(n)) return true;
            }
            return false;
        }

        public bool CanResearch(ResearchNode n)
        {
            if (n == null) return false;
            if (!ArePrerequisitesMet(n)) return false;
            if (GetRank(n) >= n.maxRanks) return false;  // already at max rank
            return true;
        }

        public int GetEffectiveCount(ResearchNode n, int baseCount)
            => n != null && n.costScalesWithRank ? baseCount * (GetRank(n) + 1) : baseCount;

        // ============================================================
        //              INSTANT RESEARCH FROM INVENTORY
        // ============================================================
        /// <summary>
        /// Pay the cost directly from the player's inventory and IMMEDIATELY apply the rank.
        /// Used when researchSeconds == 0 (or when the user clicks the "Research Now" button
        /// from the UI without needing a lab).
        /// </summary>
        public bool TryResearchFromInventory(ResearchNode n, IItemContainer inventory)
        {
            if (!CanResearch(n) || inventory == null) return false;
            // Check we have enough.
            foreach (var c in n.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                int need = GetEffectiveCount(n, c.count);
                if (inventory.CountOf(c.pack) < need) return false;
            }
            // Pay.
            foreach (var c in n.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                int need = GetEffectiveCount(n, c.count);
                inventory.Remove(c.pack, need);
            }
            // Grant.
            string id = n.nodeId;
            RankByNode[id] = GetRank(n) + 1;
            Debug.Log($"[Research] {n.displayName} -> rank {RankByNode[id]}/{n.maxRanks}");
            OnChanged?.Invoke();
            SaveToDisk();
            return true;
        }

        // ============================================================
        //         LAB-DRIVEN RESEARCH (with science-pack ticks)
        // ============================================================
        public bool StartResearch(ResearchNode n)
        {
            if (!CanResearch(n)) return false;
            ActiveResearch  = n;
            ActiveProgress01 = 0f;
            ActiveHasCost   = false;
            OnChanged?.Invoke();
            return true;
        }

        public void CancelResearch()
        {
            ActiveResearch  = null;
            ActiveProgress01 = 0f;
            ActiveHasCost   = false;
            OnChanged?.Invoke();
        }

        public void MarkCostPaid()
        {
            ActiveHasCost = true;
            OnChanged?.Invoke();
        }

        public void TickProgress(float dtSeconds)
        {
            if (ActiveResearch == null || !ActiveHasCost) return;
            ActiveProgress01 += dtSeconds / Mathf.Max(0.1f, ActiveResearch.researchSeconds);
            if (ActiveProgress01 >= 1f)
            {
                string id = ActiveResearch.nodeId;
                RankByNode[id] = GetRank(ActiveResearch) + 1;
                Debug.Log($"[Research] {ActiveResearch.displayName} -> rank {RankByNode[id]}/{ActiveResearch.maxRanks}");
                ActiveResearch = null;
                ActiveProgress01 = 0f;
                ActiveHasCost = false;
                SaveToDisk();
            }
            OnChanged?.Invoke();
        }

        // ============================================================
        //                       PERSISTENCE
        // ============================================================
        private string GetSavePath()
        {
            string worldName = "DefaultWorld";
            var session = Menu.WorldSession.Instance;
            if (session != null && !string.IsNullOrEmpty(session.worldName))
                worldName = session.worldName;
            string folder = Path.Combine(Application.persistentDataPath, "VoxelWorlds", worldName);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "research.json");
        }

        public void SaveToDisk()
        {
            try
            {
                var data = new SaveData
                {
                    nodeIds  = new List<string>(),
                    nodeRanks= new List<int>(),
                    activeId = ActiveResearch != null ? ActiveResearch.nodeId : "",
                    progress = ActiveProgress01,
                    hasCost  = ActiveHasCost
                };
                foreach (var kv in RankByNode)
                {
                    data.nodeIds.Add(kv.Key);
                    data.nodeRanks.Add(kv.Value);
                }
                File.WriteAllText(GetSavePath(), JsonUtility.ToJson(data, prettyPrint: true));
            }
            catch (Exception ex) { Debug.LogWarning("[Research] Save failed: " + ex.Message); }
        }

        public void LoadFromDisk()
        {
            try
            {
                string path = GetSavePath();
                if (!File.Exists(path)) return;
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (data == null) return;
                RankByNode.Clear();
                if (data.nodeIds != null && data.nodeRanks != null)
                {
                    int count = Mathf.Min(data.nodeIds.Count, data.nodeRanks.Count);
                    for (int i = 0; i < count; i++)
                        RankByNode[data.nodeIds[i]] = data.nodeRanks[i];
                }
                ActiveProgress01 = data.progress;
                ActiveHasCost    = data.hasCost;
                if (tree != null && !string.IsNullOrEmpty(data.activeId))
                {
                    foreach (var n in tree.nodes)
                        if (n != null && n.nodeId == data.activeId) { ActiveResearch = n; break; }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[Research] Load failed: " + ex.Message); }
            OnChanged?.Invoke();
        }

        [Serializable]
        private class SaveData
        {
            // Pair of parallel lists (JsonUtility doesn't serialize Dictionaries).
            public List<string> nodeIds   = new();
            public List<int>    nodeRanks = new();
            public string activeId = "";
            public float  progress = 0f;
            public bool   hasCost  = false;
        }
    }
}
