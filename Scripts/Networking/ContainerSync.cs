// Assets/Scripts/VoxelEngine/Networking/ContainerSync.cs
//
// 14.10.0-dev - Multiplayer milestone 5, simulation half begins: container
// contents over the wire.
//
// Every well-known container - chests, storage drawers, furnace fuel/input/
// output, assemblers, crushers, armor stations and the rest - already
// serializes through ONE save-system seam: WorldStatePersistence's
// TryFindContainer/RestoreContainer pair. This class rides that seam as
// opaque JSON, so every container type syncs with full fidelity (durability,
// charge, drawer payloads, chest port config) without a single per-machine
// special case, and any container the save system learns about in the
// future syncs automatically.
//
// Authority (the 14.8.1 lesson applied to items):
//   - The HOST announces every container change. Machines simulate on all
//     peers for now, but only the host's outcome is truth.
//   - A CLIENT announces a block's container only in a short window after
//     the local player interacted with that block (the window keeps
//     refreshing while a UI panel stays open), so player-driven deposits
//     and withdrawals replicate without the client's own sim churn
//     fighting the host.
//   - Applies are whole-container overwrites behind IsApplyingRemote, and
//     the applied state becomes the poller's new baseline so it is never
//     echoed back.
//   - Join merge: the host's containers always overwrite the joiner's; the
//     joiner's upload only fills containers the host has EMPTY (fresh
//     merged blocks), and only those accepted records are redistributed.
//
// Change detection is a slow round-robin poll of the capture JSON: the
// pass cadence itself is the debounce and string equality is the dirty
// check. No per-container event plumbing, no missed mutation paths.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Networking
{
    /// <summary>One block's container contents on the wire.</summary>
    public struct ContainerRecord
    {
        public string ItemId;
        public Vector3 Position;
        public string Json;
    }

    public static class ContainerSync
    {
        /// <summary>Raised while a remote container state is being applied, so
        /// gameplay hooks never announce an echo back into the network.</summary>
        public static bool IsApplyingRemote { get; private set; }

        // Blocks the local player recently interacted with (client announce gate).
        private static readonly Dictionary<PlacedBlock, float> _interacted = new();
        private const float InteractWindow = 20f;

        // ─────────────── local action -> network ───────────────

        /// <summary>Called by the interaction tool whenever the local player uses a
        /// block: marks that block's container as player-edited for a short window,
        /// which keeps refreshing while any UI panel stays open - so long sorting
        /// sessions never fall out of it.</summary>
        public static void NotifyLocalInteraction(PlacedBlock block)
        {
            if (block == null) return;
            _interacted[block] = Time.unscaledTime;
        }

        internal static bool RecentlyInteracted(PlacedBlock block)
        {
            if (block == null || !_interacted.TryGetValue(block, out var t)) return false;
            if (Time.unscaledTime - t > InteractWindow) { _interacted.Remove(block); return false; }
            if (VoxelEngine.UI.UIState.IsBlocking) _interacted[block] = Time.unscaledTime;
            return true;
        }

        internal static void PruneInteractions()
        {
            List<PlacedBlock> dead = null;
            foreach (var kv in _interacted)
                if (kv.Key == null) (dead ??= new List<PlacedBlock>()).Add(kv.Key);
            if (dead != null) foreach (var b in dead) _interacted.Remove(b);
        }

        // ─────────────── network -> local world ───────────────

        /// <summary>Whole-container overwrite. The applied state becomes the poller's
        /// baseline so it is never mistaken for a local change and echoed back.
        /// Returns true when a matching block was found.</summary>
        public static bool ApplyState(string itemId, Vector3 pos, string json)
        {
            var block = BlockSync.FindBlockAt(itemId, pos);
            if (block == null) return false;
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) return false;
            IsApplyingRemote = true;
            try { persistence.RestoreContainerJson(block.gameObject, json); }
            finally { IsApplyingRemote = false; }
            // Re-capture rather than trust the wire string: restore may normalize.
            ContainerSyncManager.Instance?.SetBaseline(block,
                persistence.CaptureContainerJson(block.gameObject));
            // Slot writes repaint through OnChanged, but chest port config and
            // drawer state land silently - repaint any open panel (14.12.1).
            VoxelEngine.UI.GameUIController.Instance?.RefreshOpenPanels();
            return true;
        }

        /// <summary>Join merge, joining side: the host's containers are truth and
        /// overwrite local state wholesale (14.8.1 rule).</summary>
        public static void ApplyHostSnapshot(List<ContainerRecord> records)
        {
            if (records == null) return;
            foreach (var r in records) ApplyState(r.ItemId, r.Position, r.Json);
        }

        /// <summary>Join merge, server side: a joiner's upload only fills containers
        /// the host has EMPTY - the contents of freshly merged solo blocks - and only
        /// the accepted records are redistributed to the other clients.</summary>
        public static void ApplyClientSnapshot(List<ContainerRecord> records)
        {
            if (records == null) return;
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) return;
            foreach (var r in records)
            {
                var block = BlockSync.FindBlockAt(r.ItemId, r.Position);
                if (block == null) continue;
                if (persistence.ContainerHasItems(block.gameObject)) continue;   // host truth wins
                if (!ApplyState(r.ItemId, r.Position, r.Json)) continue;
                NetworkBootstrap.Instance?.SendContainerState(r.ItemId, r.Position, r.Json);
            }
        }

        /// <summary>Every standing block that carries a container, wire-ready.
        /// Empty containers are included on purpose - an empty host container
        /// must clear a joiner's stale copy.</summary>
        public static List<ContainerRecord> GatherSnapshot()
        {
            var list = new List<ContainerRecord>();
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) return list;
            foreach (var block in Object.FindObjectsByType<PlacedBlock>(FindObjectsSortMode.None))
            {
                if (block == null || block.Item == null) continue;
                if (block.GetComponent<VoxelEngine.GridSystem.GridBlock>()?.Grid != null) continue;
                var json = persistence.CaptureContainerJson(block.gameObject);
                if (json == null) continue;
                list.Add(new ContainerRecord
                { ItemId = block.Item.itemId, Position = block.transform.position, Json = json });
            }
            return list;
        }
    }

    /// <summary>Round-robin container poller. NetworkBootstrap attaches one beside
    /// itself; it idles while offline. Each pass walks every standing block, captures
    /// its container JSON and announces on change per the authority rules above. A
    /// pass takes at least MinPassSeconds, which is the per-block debounce.</summary>
    public sealed class ContainerSyncManager : MonoBehaviour
    {
        public static ContainerSyncManager Instance { get; private set; }

        private const int   BlocksPerFrame = 2;
        private const float MinPassSeconds = 0.75f;
        private const float RescanSeconds  = 5f;

        private readonly List<PlacedBlock> _blocks = new();
        private readonly Dictionary<PlacedBlock, string> _baseline = new();
        private int _cursor;
        private float _nextRescan;
        private float _passStart;

        private void Awake()     { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Remember an applied remote state so the next poll does not
        /// mistake it for a local change and echo it back.</summary>
        public void SetBaseline(PlacedBlock block, string json)
        {
            if (block == null || json == null) return;
            _baseline[block] = json;
        }

        private void Update()
        {
            if (NetworkSession.Mode == SessionMode.Offline) return;
            var boot = NetworkBootstrap.Instance;
            if (boot == null || !boot.IsOnline || boot.WorldMismatch) return;
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) return;

            if (Time.unscaledTime >= _nextRescan) Rescan();
            if (_blocks.Count == 0) return;

            for (int n = 0; n < BlocksPerFrame; n++)
            {
                if (_cursor >= _blocks.Count)
                {
                    // Full pass done - the pass length is the debounce.
                    if (Time.unscaledTime - _passStart < MinPassSeconds) return;
                    _cursor = 0;
                    _passStart = Time.unscaledTime;
                }
                Poll(_blocks[_cursor++], persistence);
            }
        }

        private void Poll(PlacedBlock block, VoxelEngine.Persistence.WorldStatePersistence persistence)
        {
            if (block == null || block.Item == null) return;
            var json = persistence.CaptureContainerJson(block.gameObject);
            if (json == null) return;                          // block has no container
            bool known = _baseline.TryGetValue(block, out var prev);
            if (known && prev == json) return;                 // unchanged
            _baseline[block] = json;

            // First sighting normally just records the baseline (the join snapshot
            // already carried the catch-up). But a brand-new block the local player
            // is already filling must not swallow its first deposit silently.
            bool announce = known
                ? NetworkSession.Mode == SessionMode.Host || ContainerSync.RecentlyInteracted(block)
                : ContainerSync.RecentlyInteracted(block);
            if (!announce) return;
            NetworkBootstrap.Instance?.SendContainerState(
                block.Item.itemId, block.transform.position, json);
        }

        private void Rescan()
        {
            _nextRescan = Time.unscaledTime + RescanSeconds;
            _blocks.Clear();
            foreach (var block in FindObjectsByType<PlacedBlock>(FindObjectsSortMode.None))
            {
                if (block == null || block.Item == null) continue;
                if (block.GetComponent<VoxelEngine.GridSystem.GridBlock>()?.Grid != null) continue;
                _blocks.Add(block);
            }
            if (_cursor > _blocks.Count) _cursor = _blocks.Count;

            List<PlacedBlock> dead = null;
            foreach (var kv in _baseline)
                if (kv.Key == null) (dead ??= new List<PlacedBlock>()).Add(kv.Key);
            if (dead != null) foreach (var b in dead) _baseline.Remove(b);
            ContainerSync.PruneInteractions();
        }
    }
}
