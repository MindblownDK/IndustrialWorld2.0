// Assets/Scripts/VoxelEngine/Networking/MachineSync.cs
//
// 14.12.0-dev - Multiplayer milestone 5, the heart: machine runtime state
// over the wire.
//
// 14.10.0 proved the pattern on containers: the save system already owns ONE
// seam that captures and restores the live state of every factory block -
// CaptureFactoryRuntime / RestoreFactoryRuntime. That seam carries the
// active batch, the locked recipe, every tank's contents and the machine-
// specific numbers of any IMachineProcessState machine (furnaces, electric
// furnaces, refineries, distillation plants, catalytic crackers, pumpjacks,
// chemical plants, flare stacks), crusher and assembler progress, fluid
// tank/pump levels, funnel and splitter buffers, defense runtime, armor
// station progress, lighting and maritime port config. This class rides it
// as opaque JSON - identical architecture to ContainerSync, and any machine
// the save system learns about in the future syncs automatically.
//
// Authority - same rules as containers:
//   - The HOST announces every runtime change; the host's simulation is
//     truth. Clients keep simulating between updates (that keeps progress
//     bars smooth) and are periodically converged onto the host's outcome.
//   - A CLIENT announces a block's runtime only inside the interaction
//     window (recipe locking and machine toggles are panel actions), reusing
//     ContainerSync's window so one interaction covers both seams.
//   - Applies are echo-guarded by baseline tracking, exactly like containers.
//   - Join merge: host runtime always overwrites the joiner's; the joiner's
//     upload only lands on blocks whose host runtime is NOT busy (fresh
//     merged solo machines), and only accepted records are redistributed.
//
// Deliberate exclusions (documented):
//   - Belt/chute item lists churn every frame - live sync strips them and
//     only the one-time join snapshot carries them. Between syncs the
//     packets you SEE on a belt are local cosmetics; the item flow itself
//     converges through container + runtime sync at the endpoints.
//   - Power flow is not a payload at all: cables, machines and toggles
//     replicate, so each machine derives the same network locally.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Networking
{
    /// <summary>One block's machine runtime on the wire.</summary>
    public struct MachineRecord
    {
        public string ItemId;
        public Vector3 Position;
        public string Json;
    }

    public static class MachineSync
    {
        /// <summary>Raised while a remote runtime state is being applied, so
        /// gameplay hooks never announce an echo back into the network.</summary>
        public static bool IsApplyingRemote { get; private set; }

        // ─────────────── network -> local world ───────────────

        /// <summary>Whole-runtime overwrite. Returns true when a matching block
        /// was found and the state applied.</summary>
        public static bool ApplyState(string itemId, Vector3 pos, string json)
        {
            var block = BlockSync.FindBlockAt(itemId, pos);
            if (block == null) return false;
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) return false;
            IsApplyingRemote = true;
            try { persistence.RestoreMachineRuntimeJson(block.gameObject, json); }
            finally { IsApplyingRemote = false; }
            // Baseline in live (transport-stripped) form, re-captured rather than
            // trusted: restore may clamp or normalize.
            MachineSyncManager.Instance?.SetBaseline(block,
                persistence.CaptureMachineRuntimeJson(block.gameObject, includeTransport: false));
            // A panel the local player has open must repaint NOW - runtime fields
            // (recipe locks, toggles) carry no change events the way containers do.
            VoxelEngine.UI.GameUIController.Instance?.RefreshOpenPanels();
            return true;
        }

        /// <summary>Join merge, joining side: the host's machine runtime is truth.</summary>
        public static void ApplyHostSnapshot(List<MachineRecord> records)
        {
            if (records == null) return;
            foreach (var r in records) ApplyState(r.ItemId, r.Position, r.Json);
        }

        /// <summary>Join merge, server side: a joiner's upload only lands on blocks
        /// whose host runtime is not busy - the state of freshly merged solo
        /// machines - and only the accepted records are redistributed.</summary>
        public static void ApplyClientSnapshot(List<MachineRecord> records)
        {
            if (records == null) return;
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) return;
            foreach (var r in records)
            {
                var block = BlockSync.FindBlockAt(r.ItemId, r.Position);
                if (block == null) continue;
                if (persistence.MachineRuntimeBusy(block.gameObject)) continue;   // host truth wins
                if (!ApplyState(r.ItemId, r.Position, r.Json)) continue;
                NetworkBootstrap.Instance?.SendMachineState(r.ItemId, r.Position, r.Json);
            }
        }

        /// <summary>Every standing block that carries machine runtime, wire-ready.
        /// The join snapshot is the one place transport item lists ride along.</summary>
        public static List<MachineRecord> GatherSnapshot()
            => new List<MachineRecord>(StreamSnapshot());

        /// <summary>Lazy form of GatherSnapshot (14.24.1), yielding one record at a
        /// time so a join can serialize a few machines per frame instead of walking
        /// the whole world in one stalled frame. Enumerate it once.</summary>
        public static IEnumerable<MachineRecord> StreamSnapshot()
        {
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) yield break;
            foreach (var block in Object.FindObjectsByType<PlacedBlock>())
            {
                if (block == null || block.Item == null) continue;
                if (block.GetComponent<VoxelEngine.GridSystem.GridBlock>()?.Grid != null) continue;
                var json = persistence.CaptureMachineRuntimeJson(block.gameObject, includeTransport: true);
                if (json == null) continue;
                yield return new MachineRecord
                { ItemId = block.Item.itemId, Position = block.transform.position, Json = json };
            }
        }
    }

    /// <summary>Round-robin machine-runtime poller, structurally identical to
    /// ContainerSyncManager but on a slower cadence - runtime JSON changes every
    /// pass for every ACTIVE machine (progress ticks forward), so the pass length
    /// directly sets the steady-state bandwidth per machine.</summary>
    public sealed class MachineSyncManager : MonoBehaviour
    {
        public static MachineSyncManager Instance { get; private set; }

        private const int   BlocksPerFrame = 2;
        private const float MinPassSeconds = 2.5f;
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
            var json = persistence.CaptureMachineRuntimeJson(block.gameObject, includeTransport: false);
            if (json == null) return;                          // block has no machine runtime
            bool known = _baseline.TryGetValue(block, out var prev);
            if (known && prev == json) return;                 // unchanged
            _baseline[block] = json;

            // First sighting records the baseline (join snapshot carried the
            // catch-up) unless the local player is already working the panel.
            bool announce = known
                ? NetworkSession.Mode == SessionMode.Host || ContainerSync.RecentlyInteracted(block)
                : ContainerSync.RecentlyInteracted(block);
            if (!announce) return;
            NetworkBootstrap.Instance?.SendMachineState(
                block.Item.itemId, block.transform.position, json);
        }

        private void Rescan()
        {
            _nextRescan = Time.unscaledTime + RescanSeconds;
            _blocks.Clear();
            foreach (var block in FindObjectsByType<PlacedBlock>())
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
        }
    }
}
