// ─────────────────────────────────────────────────────────────────────────────
//  GridStateSync - what is INSIDE the blocks on a ship.
//
//  Milestone 9 replicated three things about a grid: where it is, what shape it
//  is, and what its pilot is asking of it. It never replicated the contents.
//  A guest saw every battery flat, every cargo container empty, and every tank
//  dry, because the only thing that ever carried that information was a whole
//  structure record - and those are only resent when the SHAPE changes. Hence
//  the giveaway symptom: a guest's cockpit said "power offline" until somebody
//  welded a block on, at which point the ship's charge appeared out of nowhere.
//
//  That one gap caused four separate complaints, and a fifth that looked
//  unrelated: a guest could not work the landing gear. The cockpit locks out
//  every flight control when the ship has no power, so a battery that read as
//  flat took the gear, the dampeners and the drive with it. Fixing the state
//  fixes the controls; there was never anything wrong with the gear.
//
//  ── how ──────────────────────────────────────────────────────────────────
//
//  A block's state travels as the SAME JSON the save file holds for it, through
//  the same capture and apply methods the save system uses. There is no second
//  serializer, so the wire cannot drift from the format on disk, and anything
//  the save system learns about later syncs for free. Batteries, cargo, liquid,
//  gas, machines and screens all ride this seam already.
//
//  Addressed as (grid net id, integer cell), like everything else on a hull.
//  ContainerSync solved this problem for the static world years earlier and
//  keyed it by WORLD POSITION, which is exactly the thing that cannot work on a
//  ship - the address would change every time the ship moved.
//
//  ── authority ────────────────────────────────────────────────────────────
//
//  The host's state is truth and is broadcast on a slow round-robin poll, with
//  string equality on the captured JSON as the dirty check: the pass cadence is
//  itself the debounce, and no per-machine mutation hook has to be found and
//  maintained. That is ContainerSync's approach and it has held up.
//
//  A client announces a block only inside a short window after the local player
//  touched it, which is what lets a guest's deposit into a cargo container
//  reach the host without the guest's own idle simulation - a battery ticking,
//  a refinery turning - fighting the host's version of the same block every
//  pass. Player intent travels; local drift does not.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Networking
{
    /// <summary>One grid block's live contents on the wire.</summary>
    public struct GridBlockState
    {
        public string NetId;
        public Vector3Int Cell;
        public string Json;
    }

    public static class GridStateSync
    {
        /// <summary>True while a block is being brought up to date from the network,
        /// so an applied state is never announced straight back out.</summary>
        public static bool IsApplyingRemote { get; private set; }

        private static bool CanSync =>
            NetworkBootstrap.Instance != null && NetworkSession.Mode != SessionMode.Offline;

        // Blocks the local player recently touched. Only these are announced by a
        // client, so a guest's deposit replicates while their idle simulation does
        // not argue with the host every pass.
        private const float InteractWindow = 20f;
        private static readonly Dictionary<GridBlock, float> _interacted = new();

        /// <summary>Called when the local player opens or uses a block's UI.</summary>
        public static void NotifyLocalInteraction(GridBlock block)
        {
            if (block == null) return;
            _interacted[block] = Time.unscaledTime;
        }

        private static bool RecentlyInteracted(GridBlock block)
            => block != null
               && _interacted.TryGetValue(block, out float at)
               && Time.unscaledTime - at <= InteractWindow;

        // ── capture / apply through the save seam ────────────────────────────

        private static string Capture(GridBlock block)
        {
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null || block == null) return "";
            return persistence.CaptureGridBlockJson(block);
        }

        /// <summary>Bring one block up to date. Not a rebuild - the block already
        /// exists and only its contents and charge are being replaced.</summary>
        public static void ApplyState(GridBlockState state)
        {
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null || string.IsNullOrEmpty(state.Json)) return;

            var grid = GridSync.Find(state.NetId);
            if (grid == null) return;

            GridBlock target = null;
            foreach (var block in grid.AllBlocks)
                if (block != null && block.GridPos == state.Cell) { target = block; break; }
            if (target == null) return;

            IsApplyingRemote = true;
            try
            {
                persistence.ApplyGridBlockJson(target, state.Json);
                // The applied state becomes this machine's baseline, so what just
                // arrived is never echoed back as a local change.
                string key = Key(state.NetId, state.Cell);
                _baseline[key] = state.Json;
                _accepted[key] = Time.unscaledTime;
            }
            finally { IsApplyingRemote = false; }
        }

        // ── change detection ─────────────────────────────────────────────────

        private static readonly Dictionary<string, string> _baseline = new();

        // When a state arrives for a block, this machine holds its tongue about that
        // block for a moment. Two players with the same panel open were otherwise
        // able to contradict each other indefinitely - each machine announcing its
        // own version, the other adopting it and announcing back - which showed up as
        // a tank's liquid type snapping back unless the button was spammed. The hold
        // lets whoever spoke last actually be heard before anyone answers.
        private const float AcceptedHoldSeconds = 1.5f;
        private static readonly Dictionary<string, float> _accepted = new();

        private static string Key(string netId, Vector3Int cell)
            => $"{netId}|{cell.x},{cell.y},{cell.z}";

        /// <summary>Walk a BLOCK-budgeted slice of the fleet and announce whatever
        /// changed. Budgeting by block rather than by grid matters: capturing a block
        /// means serializing it, and "two grids per pass" is cheap for a shuttle and
        /// ruinous for a capital ship. A fixed number of blocks per pass costs the
        /// same whatever is parked in the world - a big ship just comes round less
        /// often, which is the right trade.</summary>
        internal static int BroadcastChanged(List<GridNetTag> tags, ref int tagCursor,
            ref int blockCursor, int blockBudget)
        {
            if (!CanSync || tags.Count == 0) return 0;

            bool isHost = NetworkSession.Mode == SessionMode.Host;
            int sent = 0, examined = 0, grids = 0;

            while (examined < blockBudget && grids <= tags.Count)
            {
                if (tagCursor >= tags.Count) { tagCursor = 0; blockCursor = 0; }
                var tag = tags[tagCursor];

                if (tag == null || tag.Grid == null || string.IsNullOrEmpty(tag.Id))
                {
                    tagCursor++; blockCursor = 0; grids++;
                    continue;
                }

                int index = 0;
                bool ranOut = true;
                foreach (var block in tag.Grid.AllBlocks)
                {
                    if (index++ < blockCursor) continue;
                    if (examined >= blockBudget) { ranOut = false; break; }

                    blockCursor++;
                    examined++;
                    if (block == null) continue;

                    // A client speaks only about what its player just touched.
                    if (!isHost && !RecentlyInteracted(block)) continue;

                    string json = Capture(block);
                    if (string.IsNullOrEmpty(json)) continue;

                    string key = Key(tag.Id, block.GridPos);
                    if (_baseline.TryGetValue(key, out string last) && last == json) continue;
                    if (_accepted.TryGetValue(key, out float at)
                        && Time.unscaledTime - at < AcceptedHoldSeconds) continue;
                    _baseline[key] = json;

                    var state = new GridBlockState { NetId = tag.Id, Cell = block.GridPos, Json = json };
                    if (isHost) NetworkBootstrap.Instance.SendGridBlockState(state);
                    else NetworkBootstrap.Instance.RequestGridBlockState(state);
                    sent++;
                }

                if (ranOut) { tagCursor++; blockCursor = 0; grids++; }
            }
            return sent;
        }

        /// <summary>Everything in every block, for a joining client. Lazy, so the
        /// caller can spend it against a frame budget rather than hitching the host.</summary>
        public static IEnumerable<GridBlockState> StreamSnapshot()
        {
            var tags = new List<GridNetTag>();
            GridSync.CollectTags(tags);

            foreach (var tag in tags)
            {
                if (tag == null || tag.Grid == null || string.IsNullOrEmpty(tag.Id)) continue;
                foreach (var block in tag.Grid.AllBlocks)
                {
                    if (block == null) continue;
                    string json = Capture(block);
                    if (string.IsNullOrEmpty(json)) continue;
                    _baseline[Key(tag.Id, block.GridPos)] = json;
                    yield return new GridBlockState { NetId = tag.Id, Cell = block.GridPos, Json = json };
                }
            }
        }

        /// <summary>Forget a hull's baselines. A rebuilt ship must be re-described from
        /// scratch rather than compared against the blocks it used to have.</summary>
        public static void Forget(string netId)
        {
            if (string.IsNullOrEmpty(netId)) return;
            string prefix = netId + "|";
            List<string> dead = null;
            foreach (var key in _baseline.Keys)
                if (key.StartsWith(prefix, System.StringComparison.Ordinal))
                    (dead ??= new List<string>()).Add(key);
            if (dead == null) return;
            foreach (var key in dead) _baseline.Remove(key);
        }

        public static void Clear()
        {
            _baseline.Clear();
            _interacted.Clear();
            _accepted.Clear();
        }
    }

    /// <summary>Drives the state poll. Deliberately slow and round-robin: contents
    /// change constantly on a working ship, and a battery that ticks every frame must
    /// not put a ship's worth of JSON on the wire every frame.</summary>
    public sealed class GridStateSyncManager : MonoBehaviour
    {
        public static GridStateSyncManager Instance { get; private set; }

        /// <summary>Blocks examined per pass, across the whole fleet. Capturing a
        /// block means serializing it, so this is the number that actually governs
        /// cost - and it does not grow with the size of the ships in the world.</summary>
        private const int BlocksPerPass = 40;
        private const float PassSeconds = 0.5f;

        private readonly List<GridNetTag> _tags = new();
        private float _nextPass;
        private int _tagCursor;
        private int _blockCursor;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            if (NetworkSession.Mode == SessionMode.Offline) return;
            if (Time.unscaledTime < _nextPass) return;
            _nextPass = Time.unscaledTime + PassSeconds;

            GridSync.CollectTags(_tags);
            GridStateSync.BroadcastChanged(_tags, ref _tagCursor, ref _blockCursor, BlocksPerPass);
        }
    }
}
