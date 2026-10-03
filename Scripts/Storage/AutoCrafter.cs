// Assets/Scripts/VoxelEngine/Storage/AutoCrafter.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║            NETWORK AUTO-CRAFTING ENGINE (14.43.0)                ║
// ║  Lives on the Server Controller. Crafts from encoded patterns   ║
// ║  filed in the pattern bank (capacity = installed RAM units).    ║
// ║  • The CONTROLLER does the work: craft time = recipe seconds /  ║
// ║    CPU speed, and an active craft adds watts to the system.     ║
// ║  • STATION GATE: a recipe that needs e.g. an Assembler only     ║
// ║    runs while a station of that tier is physically on the       ║
// ║    storage network (touching or Data-Piped).                    ║
// ║  • FULL-CHAIN RECURSION: missing ingredients that have their    ║
// ║    own filed pattern are auto-queued as child jobs first, the   ║
// ║    whole dependency tree deep (cycle + depth guarded).          ║
// ║  • One item crafts at a time; ingredients are taken per craft,  ║
// ║    never up-front, and a cancel refunds the in-flight inputs.   ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(ServerRack))]
    public class AutoCrafter : MonoBehaviour
    {
        public const int BANK_SLOTS      = 64;   // 4 RAM slots × RAM 16 max
        public const int MAX_JOBS        = 16;
        public const int MAX_CHAIN_DEPTH = 8;

        /// <summary>Physical home of the encoded patterns. Fixed 64 slots;
        /// only the first <see cref="ActiveCapacity"/> are powered by RAM -
        /// patterns sitting past that line are INERT until RAM is added.</summary>
        public ItemContainer patternBank;

        /// <summary>Live craft queue, first job first. Children a chain
        /// spawned sit BEFORE their parent so intermediates finish first.</summary>
        public List<CraftJob> jobs = new();

        /// <summary>Id of the job whose single craft is currently running
        /// (ingredients already taken from the network), or -1.</summary>
        [System.NonSerialized] public int InFlightJobId = -1;

        public bool IsActivelyCrafting => InFlightJobId >= 0;

        private ServerRack _rack;
        private int _nextJobId = 1;
        private float _planTimer;

        // Refund ledger of the in-flight craft (exactly one craft's inputs).
        private readonly List<(ItemDefinition item, int count)> _inFlightInputs = new();

        // Output that did not fit the network yet - flushed before anything
        // else runs, so a full network stalls instead of deleting items.
        private ItemDefinition _pendingOutputItem;
        private int _pendingOutputCount;

        // Scratch buffers (reused every planner pass).
        private readonly List<CraftingStation> _stationBuf = new();
        private readonly List<(RecipeIngredient ing, int shortfall)> _missingBuf = new();

        // ── Recipe resolution ──────────────────────────────────────
        // Patterns carry the stable RecipeDefinition asset name; the live
        // registry maps it back. Cache rebuilds are rate-limited so an
        // unknown id can never turn the planner into a per-tick scan.
        private static Dictionary<string, RecipeDefinition> _recipeCache;
        private static float _recipeCacheRetryAt = -1f;

        public static RecipeDefinition ResolveRecipe(string recipeId)
        {
            if (string.IsNullOrEmpty(recipeId)) return null;
            if (_recipeCache != null && _recipeCache.TryGetValue(recipeId, out var hit) && hit != null)
                return hit;
            if (_recipeCache != null && Time.realtimeSinceStartup < _recipeCacheRetryAt)
                return null;
            _recipeCacheRetryAt = Time.realtimeSinceStartup + 2f;

            var registry = VoxelEngine.UI.GameUIController.Instance != null
                ? VoxelEngine.UI.GameUIController.Instance.recipeRegistry : null;
            if (registry == null)
            {
                var all = Resources.FindObjectsOfTypeAll<RecipeRegistry>();
                if (all != null && all.Length > 0) registry = all[0];
            }
            if (registry == null || registry.recipes == null) return null;

            _recipeCache ??= new Dictionary<string, RecipeDefinition>();
            _recipeCache.Clear();
            foreach (var r in registry.recipes)
                if (r != null) _recipeCache[r.name] = r;
            return _recipeCache.TryGetValue(recipeId, out var found) ? found : null;
        }

        // ── Lifecycle ──────────────────────────────────────────────
        private void Awake()
        {
            _rack = GetComponent<ServerRack>();
            EnsureContainers();
        }

        public void EnsureContainers()
        {
            if (patternBank == null || patternBank.Size < BANK_SLOTS)
                patternBank = new ItemContainer("Pattern Bank", BANK_SLOTS);
            // Generic inserts are rejected: patterns are filed through
            // TryFilePattern so the RAM-capacity line stays authoritative.
            patternBank.AcceptFilter = (item, wanted) => 0;
        }

        // ── Pattern bank API ───────────────────────────────────────

        /// <summary>Bank slots currently powered by RAM.</summary>
        public int ActiveCapacity
            => _rack != null ? Mathf.Clamp(_rack.PatternSlots, 0, BANK_SLOTS) : 0;

        public int EncodedCount
        {
            get
            {
                EnsureContainers();
                int n = 0;
                for (int i = 0; i < patternBank.Size; i++)
                    if (PatternItems.IsEncoded(patternBank.GetSlot(i))) n++;
                return n;
            }
        }

        public struct PatternEntry
        {
            public int slot;
            public ItemStack stack;
            public PatternData data;
            public RecipeDefinition recipe;
            public bool active;   // slot < ActiveCapacity → RAM-backed
        }

        public void GetPatterns(List<PatternEntry> into)
        {
            into.Clear();
            EnsureContainers();
            int cap = ActiveCapacity;
            for (int i = 0; i < patternBank.Size; i++)
            {
                var s = patternBank.GetSlot(i);
                if (!PatternItems.IsEncoded(s)) continue;
                var data = PatternItems.DataOf(s);
                into.Add(new PatternEntry
                {
                    slot = i,
                    stack = s,
                    data = data,
                    recipe = ResolveRecipe(data.recipeId),
                    active = i < cap
                });
            }
        }

        public bool HasPatternFor(string recipeId, bool activeOnly = true)
        {
            EnsureContainers();
            int limit = activeOnly ? ActiveCapacity : patternBank.Size;
            for (int i = 0; i < limit; i++)
            {
                var data = PatternItems.DataOf(patternBank.GetSlot(i));
                if (data != null && data.recipeId == recipeId) return true;
            }
            return false;
        }

        /// <summary>First RAM-backed pattern whose recipe outputs itemId.</summary>
        public RecipeDefinition ActivePatternProducing(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            EnsureContainers();
            int cap = ActiveCapacity;
            for (int i = 0; i < cap; i++)
            {
                var data = PatternItems.DataOf(patternBank.GetSlot(i));
                if (data == null) continue;
                var recipe = ResolveRecipe(data.recipeId);
                if (recipe != null && recipe.outputItem != null &&
                    recipe.outputItem.itemId == itemId) return recipe;
            }
            return null;
        }

        /// <summary>File an encoded pattern into the first free RAM-backed
        /// slot. Fails when memory is full or the recipe is already filed.</summary>
        public bool TryFilePattern(ItemStack encoded, out string reason)
        {
            reason = "";
            EnsureContainers();
            if (!PatternItems.IsEncoded(encoded)) { reason = "NOT A PATTERN"; return false; }
            var data = PatternItems.DataOf(encoded);
            if (HasPatternFor(data.recipeId, activeOnly: false))
            { reason = "PATTERN ALREADY FILED"; return false; }
            int cap = ActiveCapacity;
            if (cap <= 0) { reason = "NO RAM INSTALLED"; return false; }
            for (int i = 0; i < cap; i++)
            {
                if (!patternBank.GetSlot(i).IsEmpty) continue;
                patternBank.SetSlot(i, encoded);
                return true;
            }
            reason = "PATTERN MEMORY FULL";
            return false;
        }

        /// <summary>Pull a pattern out of the bank (physical item back).</summary>
        public ItemStack EjectPattern(int slot)
        {
            EnsureContainers();
            if (slot < 0 || slot >= patternBank.Size) return new ItemStack();
            var s = patternBank.GetSlot(slot);
            if (!PatternItems.IsEncoded(s)) return new ItemStack();
            patternBank.SetSlot(slot, new ItemStack());
            return s;
        }

        // ── Queue API ──────────────────────────────────────────────

        public bool TryQueueCraft(RecipeDefinition recipe, int count, out string reason)
        {
            reason = "";
            if (recipe == null || count <= 0) { reason = "BAD REQUEST"; return false; }
            if (_rack == null || !_rack.IsOnline) { reason = "SYSTEM OFFLINE"; return false; }
            if (!HasPatternFor(recipe.name)) { reason = "NO PATTERN FILED"; return false; }

            // Player requests of the same recipe merge into one job.
            foreach (var j in jobs)
            {
                if (j.parentId != -1 || j.recipeId != recipe.name) continue;
                j.requested += count;
                return true;
            }
            if (jobs.Count >= MAX_JOBS) { reason = "QUEUE FULL"; return false; }
            jobs.Add(new CraftJob
            {
                id = _nextJobId++,
                recipeId = recipe.name,
                requested = count
            });
            return true;
        }

        /// <summary>Cancel a job and every child job spawned under it. The
        /// in-flight craft's ingredients are refunded to the network.</summary>
        public void CancelJob(int jobId)
        {
            var doomed = new HashSet<int> { jobId };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var j in jobs)
                    if (j.parentId != -1 && doomed.Contains(j.parentId) && doomed.Add(j.id))
                        grew = true;
            }
            if (doomed.Contains(InFlightJobId)) AbortInFlight(refund: true);
            jobs.RemoveAll(j => doomed.Contains(j.id));
        }

        public CraftJob FindJob(int id)
        {
            foreach (var j in jobs) if (j.id == id) return j;
            return null;
        }

        // ── Engine ─────────────────────────────────────────────────
        private void Update()
        {
            if (_rack == null) return;
            EnsureContainers();
            bool online = _rack.IsOnline;

            // 1) Advance the in-flight craft.
            if (InFlightJobId >= 0)
            {
                var job = FindJob(InFlightJobId);
                var recipe = job != null ? ResolveRecipe(job.recipeId) : null;
                if (job == null || recipe == null || recipe.outputItem == null)
                {
                    AbortInFlight(refund: true);
                }
                else if (online)
                {
                    float secs = Mathf.Max(0.1f,
                        recipe.craftSeconds > 0f ? recipe.craftSeconds : 1f);
                    job.progress += Time.deltaTime
                        * Mathf.Max(1f, _rack.CraftSpeedMultiplier) / secs;
                    if (job.progress >= 1f)
                    {
                        int leftover = _rack.NetworkInsert(recipe.outputItem, recipe.outputCount);
                        if (leftover > 0)
                        {
                            _pendingOutputItem = recipe.outputItem;
                            _pendingOutputCount = leftover;
                        }
                        job.done++;
                        job.progress = 0f;
                        job.inFlight = false;
                        _inFlightInputs.Clear();
                        InFlightJobId = -1;
                        if (job.done >= job.requested) jobs.Remove(job);
                    }
                }
            }

            // 2) Planner pass, rate limited.
            _planTimer += Time.deltaTime;
            if (_planTimer < 0.25f) return;
            _planTimer = 0f;
            Plan(online);
        }

        private void Plan(bool online)
        {
            if (!online)
            {
                foreach (var j in jobs) j.SetState(JobState.Blocked, "SYSTEM OFFLINE");
                return;
            }

            // Flush output that found no disk space before starting more work.
            if (_pendingOutputItem != null && _pendingOutputCount > 0)
            {
                _pendingOutputCount = _rack.NetworkInsert(_pendingOutputItem, _pendingOutputCount);
                if (_pendingOutputCount > 0)
                {
                    foreach (var j in jobs) j.SetState(JobState.Blocked, "NETWORK FULL");
                    return;
                }
                _pendingOutputItem = null;
            }

            if (InFlightJobId >= 0)
            {
                foreach (var j in jobs)
                    j.SetState(j.id == InFlightJobId ? JobState.Crafting : JobState.Waiting,
                               j.id == InFlightJobId ? "CRAFTING" : "QUEUED");
                return;
            }

            // Find the first job that can start one craft right now. Blocked
            // or starved jobs are skipped so independent work keeps flowing.
            for (int i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                var recipe = ResolveRecipe(job.recipeId);
                if (recipe == null || recipe.outputItem == null)
                { job.SetState(JobState.Blocked, "UNKNOWN RECIPE"); continue; }

                if (!HasStationFor(recipe))
                {
                    job.SetState(JobState.Blocked,
                        "NEEDS " + StationName(recipe.requiredStation) + " ON NETWORK");
                    continue;
                }

                // Check one craft's ingredients.
                _missingBuf.Clear();
                if (recipe.inputs != null)
                {
                    foreach (var ing in recipe.inputs)
                    {
                        if (ing.item == null || ing.count <= 0) continue;
                        int have = _rack.NetworkCount(ing.item.itemId);
                        if (have < ing.count)
                            _missingBuf.Add((ing, ing.count - have));
                    }
                }

                if (_missingBuf.Count == 0)
                {
                    StartCraft(job, recipe);
                    job.SetState(JobState.Crafting, "CRAFTING");
                    return;
                }

                // Full-chain recursion: spawn child jobs for shortfalls that
                // have a RAM-backed pattern; wait on the rest.
                string note = PlanChildren(job, i, out bool spawned);
                job.SetState(JobState.Waiting, note);
                if (spawned) return;   // queue changed - replan next pass
            }
        }

        private void StartCraft(CraftJob job, RecipeDefinition recipe)
        {
            _inFlightInputs.Clear();
            if (recipe.inputs != null)
            {
                foreach (var ing in recipe.inputs)
                {
                    if (ing.item == null || ing.count <= 0) continue;
                    _rack.NetworkExtract(ing.item.itemId, ing.count);
                    _inFlightInputs.Add((ing.item, ing.count));
                }
            }
            job.progress = 0f;
            job.inFlight = true;
            InFlightJobId = job.id;
        }

        private string PlanChildren(CraftJob parent, int parentIndex, out bool spawned)
        {
            spawned = false;
            string firstMissing = "";

            foreach (var (ing, shortfall) in _missingBuf)
            {
                if (string.IsNullOrEmpty(firstMissing))
                    firstMissing = ing.item.displayName;

                var childRecipe = ActivePatternProducing(ing.item.itemId);
                if (childRecipe == null) continue;   // no pattern → just wait

                // Cycle guard: never queue a recipe already on the ancestor chain.
                if (IsOnAncestorChain(parent, childRecipe.name)) continue;
                if (ChainDepth(parent) + 1 >= MAX_CHAIN_DEPTH) continue;

                // Production already pending for this ingredient?
                int pending = 0;
                foreach (var j in jobs)
                {
                    var r = ResolveRecipe(j.recipeId);
                    if (r == null || r.outputItem == null ||
                        r.outputItem.itemId != ing.item.itemId) continue;
                    pending += (j.requested - j.done) * Mathf.Max(1, r.outputCount);
                }
                if (pending >= shortfall) continue;

                if (jobs.Count >= MAX_JOBS)
                    return "QUEUE FULL - CANNOT CHAIN " + ing.item.displayName.ToUpperInvariant();

                int need = shortfall - pending;
                int runs = Mathf.CeilToInt(need / (float)Mathf.Max(1, childRecipe.outputCount));
                jobs.Insert(parentIndex, new CraftJob
                {
                    id = _nextJobId++,
                    recipeId = childRecipe.name,
                    requested = runs,
                    parentId = parent.id
                });
                spawned = true;
                return "WAITING FOR " + ing.item.displayName.ToUpperInvariant();
            }

            return string.IsNullOrEmpty(firstMissing)
                ? "WAITING"
                : "MISSING " + firstMissing.ToUpperInvariant();
        }

        private bool IsOnAncestorChain(CraftJob job, string recipeId)
        {
            int guard = 0;
            var cur = job;
            while (cur != null && guard++ < MAX_CHAIN_DEPTH + 2)
            {
                if (cur.recipeId == recipeId) return true;
                cur = cur.parentId == -1 ? null : FindJob(cur.parentId);
            }
            return false;
        }

        private int ChainDepth(CraftJob job)
        {
            int depth = 0;
            var cur = job;
            while (cur != null && cur.parentId != -1 && depth < MAX_CHAIN_DEPTH + 2)
            { depth++; cur = FindJob(cur.parentId); }
            return depth;
        }

        private void AbortInFlight(bool refund)
        {
            if (refund)
            {
                foreach (var (item, count) in _inFlightInputs)
                {
                    if (item == null || count <= 0) continue;
                    int leftover = _rack != null ? _rack.NetworkInsert(item, count) : count;
                    if (leftover > 0)
                        Items.DroppedItem.Spawn(new ItemStack(item, leftover),
                            transform.position + Vector3.up * 1.2f, Vector3.up);
                }
            }
            var job = FindJob(InFlightJobId);
            if (job != null) { job.inFlight = false; job.progress = 0f; }
            _inFlightInputs.Clear();
            InFlightJobId = -1;
        }

        // ── Station gate ───────────────────────────────────────────

        /// <summary>True when a crafting station able to run this recipe is a
        /// member of the storage network (touching or Data-Piped).</summary>
        public bool HasStationFor(RecipeDefinition recipe)
        {
            if (recipe == null) return false;
            if (recipe.requiredStation == StationTier.None) return true;
            if (_rack == null) return false;
            StorageNetwork.MembersOf(_rack, _stationBuf);
            foreach (var st in _stationBuf)
            {
                if (st == null) continue;
                if (st.exclusiveRecipes
                    ? st.tier == recipe.requiredStation
                    : st.tier >= recipe.requiredStation) return true;
            }
            return false;
        }

        public static string StationName(StationTier tier) => tier switch
        {
            StationTier.CraftingBench => "CRAFTING BENCH",
            StationTier.Furnace       => "FURNACE",
            StationTier.Assembler     => "ASSEMBLER",
            StationTier.ArmorStation  => "ARMOR STATION",
            _                         => "STATION"
        };

        // ── Persistence / sync (factory-runtime seam) ──────────────

        [System.Serializable]
        private class SavedQueue
        {
            public int nextId = 1;
            public int inFlightJobId = -1;
            public List<CraftJob> jobs = new();
        }

        public string CaptureQueueJson()
        {
            var sq = new SavedQueue
            {
                nextId = _nextJobId,
                inFlightJobId = InFlightJobId,
                jobs = jobs
            };
            return JsonUtility.ToJson(sq);
        }

        public void RestoreQueueJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var sq = JsonUtility.FromJson<SavedQueue>(json);
                if (sq == null) return;
                jobs = sq.jobs ?? new List<CraftJob>();
                _nextJobId = Mathf.Max(1, sq.nextId);
                _inFlightInputs.Clear();
                InFlightJobId = -1;
                if (sq.inFlightJobId >= 0)
                {
                    var job = FindJob(sq.inFlightJobId);
                    var recipe = job != null ? ResolveRecipe(job.recipeId) : null;
                    if (job != null && recipe != null)
                    {
                        // The craft's inputs were extracted before this state
                        // was captured; rebuild the refund ledger from the
                        // recipe so a later cancel still restores them.
                        InFlightJobId = job.id;
                        job.inFlight = true;
                        if (recipe.inputs != null)
                            foreach (var ing in recipe.inputs)
                                if (ing.item != null && ing.count > 0)
                                    _inFlightInputs.Add((ing.item, ing.count));
                    }
                }
            }
            catch
            {
                // A corrupt payload empties the queue; it must never take the
                // load or a client join down.
                jobs = new List<CraftJob>();
                _inFlightInputs.Clear();
                InFlightJobId = -1;
            }
        }
    }

    /// <summary>One queued production order. Serializable: the queue rides the
    /// factory-runtime seam (save file + MachineSync convergence).</summary>
    [System.Serializable]
    public class CraftJob
    {
        public int id;
        public string recipeId = "";
        public int requested;
        public int done;
        public int parentId = -1;      // -1 = player request, else chained child
        public float progress;         // 0..1 of the craft currently running
        public bool inFlight;

        [System.NonSerialized] public JobState state = JobState.Waiting;
        [System.NonSerialized] public string statusNote = "QUEUED";

        public void SetState(JobState s, string note) { state = s; statusNote = note; }
    }

    public enum JobState { Waiting, Crafting, Blocked }
}
