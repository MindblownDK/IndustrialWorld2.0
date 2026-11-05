// Assets/Scripts/VoxelEngine/Networking/DropSync.cs
//
// 14.11.0-dev - Multiplayer milestone 5 continues: dropped items over the wire.
//
// Physical world drops - mined block spills, chest contents on destruction,
// manual drops, creature loot, inventory overflow - all funnel through TWO
// seams in DroppedItem: Spawn (creation) and Despawn (every consumption
// path: expiry, full pickup, belt insert). This class rides those seams.
//
// Identity: drops MOVE (they are rigidbodies), so positional identity would
// break the moment one rolled downhill. Every drop instead carries a wire id
// of the form "<playerId>:<serial>", assigned fresh on every pooled spawn.
//
// Authority: every caller of DroppedItem.Spawn is a local player action or
// a local destruction event - there is no background simulation spawning
// drops on multiple machines at once - so EVERY machine announces the drops
// it spawns and the pickups it performs. Toss physics stays local per
// machine (cheap, and close enough for a tumbling cube); the one-time
// settle announcement from the spawning machine converges the rest
// position everywhere. Stack payloads travel as save-format JSON
// (WorldStatePersistence.CaptureStackJson), so durability, charge, liquid
// payloads and packed drawers arrive intact.
//
// Accepted risk (documented): two players grabbing the SAME drop in the
// same instant can each receive it - the removal broadcasts simply cross on
// the wire. Co-op stakes, vanishingly small window.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Networking
{
    /// <summary>One live world drop on the wire (join snapshot).</summary>
    public struct DropRecord
    {
        public string Id;
        public string StackJson;
        public Vector3 Position;
        public bool Settled;
    }

    public static class DropSync
    {
        /// <summary>Raised while a remote drop event is being applied locally, so
        /// the DroppedItem hooks never announce an echo back into the network.</summary>
        public static bool IsApplyingRemote { get; private set; }

        // Live drops by wire id - both our own and remote copies. Entries whose
        // instance died with a scene unload stay null-guarded, never announced.
        private static readonly Dictionary<string, DroppedItem> _byId = new();
        private static long _nextSerial;

        public static string NewId() => PlayerIdentity.LocalId + ":" + _nextSerial++;

        private static bool ShouldAnnounce =>
            !IsApplyingRemote
            && NetworkSession.Mode != SessionMode.Offline
            && NetworkBootstrap.Instance != null
            && !NetworkBootstrap.Instance.WorldMismatch;

        // ─────────────── local action -> network ───────────────

        public static void AnnounceSpawned(DroppedItem drop, Vector3 tossDir)
        {
            // A remote apply re-tags the entity itself - never register or
            // announce the fresh local id it was born with.
            if (IsApplyingRemote) return;
            if (drop == null || string.IsNullOrEmpty(drop.NetId)) return;
            _byId[drop.NetId] = drop;
            if (!ShouldAnnounce) return;
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            var json = persistence != null ? persistence.CaptureStackJson(drop.stack) : null;
            if (json == null) return;
            NetworkBootstrap.Instance.SendDropSpawned(drop.NetId, json,
                drop.transform.position, tossDir);
        }

        public static void AnnounceSettled(DroppedItem drop)
        {
            if (drop == null || !drop.NetOwned || string.IsNullOrEmpty(drop.NetId)) return;
            if (!ShouldAnnounce) return;
            NetworkBootstrap.Instance.SendDropSettled(drop.NetId, drop.transform.position);
        }

        public static void AnnounceUpdated(DroppedItem drop)
        {
            if (drop == null || string.IsNullOrEmpty(drop.NetId) || drop.stack == null) return;
            if (!ShouldAnnounce) return;
            NetworkBootstrap.Instance.SendDropUpdated(drop.NetId, drop.stack.count);
        }

        /// <summary>Called from the single removal seam. Announces ownership-blind:
        /// whoever consumed the drop (pickup, belt) reports it gone. Remote applies
        /// pass through silently thanks to IsApplyingRemote.</summary>
        public static void HandleDespawn(DroppedItem drop)
        {
            if (drop == null || string.IsNullOrEmpty(drop.NetId)) return;
            if (_byId.TryGetValue(drop.NetId, out var known) && ReferenceEquals(known, drop))
                _byId.Remove(drop.NetId);
            if (ShouldAnnounce) NetworkBootstrap.Instance.SendDropRemoved(drop.NetId);
            drop.NetId = null;
            drop.NetOwned = false;
        }

        // ─────────────── network -> local world ───────────────

        public static void ApplySpawned(string id, string stackJson, Vector3 pos, Vector3 toss)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_byId.TryGetValue(id, out var existing) && existing != null) return;   // duplicate-safe
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) return;
            var stack = persistence.RestoreStackJson(stackJson);
            if (stack == null) return;
            IsApplyingRemote = true;
            try
            {
                var drop = DroppedItem.SpawnReplicated(stack, pos, toss);
                if (drop == null) return;   // this machine's world-drop budget is full
                drop.NetId = id;
                drop.NetOwned = false;
                _byId[id] = drop;
            }
            finally { IsApplyingRemote = false; }
        }

        public static void ApplySettled(string id, Vector3 pos)
        {
            if (_byId.TryGetValue(id, out var drop) && drop != null) drop.ForceSettle(pos);
        }

        public static void ApplyUpdated(string id, int count)
        {
            if (!_byId.TryGetValue(id, out var drop) || drop == null) return;
            IsApplyingRemote = true;
            try { drop.NetSetCount(count); }
            finally { IsApplyingRemote = false; }
        }

        public static void ApplyRemoved(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!_byId.TryGetValue(id, out var drop) || drop == null) { _byId.Remove(id); return; }
            IsApplyingRemote = true;
            try { drop.Despawn(); }
            finally { IsApplyingRemote = false; }
        }

        // ── server-validated pickup claims (18.10.0-dev) ───────────────
        //
        // A guest never consumes a world drop locally any more: TryRequestPickup
        // routes the attempt to the host as a claim intent, and only the verdict
        // (ApplyClaimResult) applies it. The host holds one pending claim per drop,
        // so two players grabbing the same drop in the same instant can no longer
        // both receive it.

        /// <summary>How long a granted claim stays reserved before a lost verdict
        /// releases the drop for someone else.</summary>
        public const float ClaimPendingSeconds = 5f;

        // dropId -> Time.time of the grant. Host-side only.
        private static readonly Dictionary<string, float> _pendingClaims = new();

        public static bool TryGet(string id, out DroppedItem drop)
            => _byId.TryGetValue(id, out drop);

        public static bool IsClaimPending(string id)
        {
            PruneClaims();
            return !string.IsNullOrEmpty(id) && _pendingClaims.ContainsKey(id);
        }

        public static void MarkClaimPending(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            PruneClaims();
            _pendingClaims[id] = Time.time;
        }

        private static void PruneClaims()
        {
            if (_pendingClaims.Count == 0) return;
            List<string> stale = null;
            foreach (var entry in _pendingClaims)
                if (Time.time - entry.Value > ClaimPendingSeconds)
                    (stale ??= new List<string>()).Add(entry.Key);
            if (stale == null) return;
            foreach (var key in stale) _pendingClaims.Remove(key);
        }

        /// <summary>Entry point for every player-driven pickup (interact ray and
        /// proximity trigger). Returns true when the attempt was routed to the host
        /// as a claim and the caller must NOT apply it locally; false means the
        /// caller keeps the immediate local path (offline session, host session, or
        /// a stack that cannot fit anyway).</summary>
        public static bool TryRequestPickup(DroppedItem drop, Inventory inv)
        {
            if (drop == null || inv == null) return false;
            if (NetworkSession.Mode != SessionMode.Client) return false;
            var bootstrap = NetworkBootstrap.Instance;
            if (bootstrap == null || bootstrap.WorldMismatch) return false;
            if (string.IsNullOrEmpty(drop.NetId) || drop.stack == null || drop.stack.IsEmpty) return false;
            // The host cannot see this guest's live inventory, so the guest only asks
            // when at least one unit actually fits; anything left over at apply time
            // is re-spawned as a fresh drop by ApplyClaimResult.
            if (inv.container == null || !inv.container.HasSpace(drop.stack.item, 1)) return false;
            bootstrap.SendDropClaim(drop.NetId);
            return true;
        }

        /// <summary>Host verdict, applied on every client. Granted: the drop despawns
        /// everywhere and the claimant alone inserts the approved stack into its own
        /// inventory. Denied: only the claimant gets feedback.</summary>
        public static void ApplyClaimResult(string dropId, string playerId, bool granted, string stackJson)
        {
            if (string.IsNullOrEmpty(dropId)) return;
            if (!granted)
            {
                if (playerId == PlayerIdentity.LocalId)
                    VoxelEngine.UI.BuildFeedbackHud.Show("Already taken",
                        "Another player claimed it first", null, new Color(1f, 0.72f, 0.22f));
                return;
            }
            bool mine = playerId == PlayerIdentity.LocalId;
            ItemStack leftover = null;
            IsApplyingRemote = true;
            try
            {
                if (_byId.TryGetValue(dropId, out var drop) && drop != null) drop.Despawn();
                else _byId.Remove(dropId);
                if (mine)
                {
                    var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
                    var stack = persistence != null ? persistence.RestoreStackJson(stackJson) : null;
                    var inv = VoxelEngine.Player.PlayerStats.Instance != null
                        ? VoxelEngine.Player.PlayerStats.Instance.GetComponent<Inventory>() : null;
                    if (stack != null && !stack.IsEmpty && inv != null && inv.container != null)
                    {
                        leftover = inv.container.Insert(stack);
                        VoxelEngine.UI.BuildFeedbackHud.Show($"Picked up {stack.item.displayName}",
                            $"+{stack.count - (leftover != null ? leftover.count : 0)}", stack.item.icon,
                            new Color(0.30f, 0.75f, 0.40f));
                        VoxelEngine.FX.AudioManager.PlayUI(VoxelEngine.FX.SfxLibrary.Get(VoxelEngine.FX.Sfx.Pickup),
                            0.45f, UnityEngine.Random.Range(0.97f, 1.06f));
                    }
                }
            }
            finally { IsApplyingRemote = false; }
            // Overflow (the inventory filled between the pre-check and the verdict)
            // leaves as a fresh announced drop - outside the remote-apply guard on
            // purpose so the host learns about it.
            if (leftover != null && leftover.count > 0)
            {
                var inv = VoxelEngine.Player.PlayerStats.Instance != null
                    ? VoxelEngine.Player.PlayerStats.Instance.GetComponent<Inventory>() : null;
                if (inv != null)
                    DroppedItem.Spawn(leftover, inv.transform.position + Vector3.up * 0.6f, Vector3.up);
            }
        }

        /// <summary>Join merge: spawn what is missing, by id. Snapshot drops arrive
        /// without toss physics; settled ones freeze at their exact rest position.</summary>
        public static void ApplySnapshot(List<DropRecord> records)
        {
            if (records == null) return;
            foreach (var r in records)
            {
                if (string.IsNullOrEmpty(r.Id)) continue;
                if (_byId.TryGetValue(r.Id, out var existing) && existing != null) continue;
                ApplySpawned(r.Id, r.StackJson, r.Position, Vector3.zero);
                if (r.Settled) ApplySettled(r.Id, r.Position);
            }
        }

        /// <summary>Every live drop, wire-ready. Pre-session drops that never met
        /// the network get an id here so the snapshot can carry them.</summary>
        public static List<DropRecord> GatherSnapshot()
            => new List<DropRecord>(StreamSnapshot());

        /// <summary>Lazy form of GatherSnapshot (14.24.1), yielding one record at a
        /// time so a join can serialize a few drops per frame instead of walking the
        /// whole world in one stalled frame. Enumerate it once.</summary>
        public static IEnumerable<DropRecord> StreamSnapshot()
        {
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) yield break;
            foreach (var drop in Object.FindObjectsByType<DroppedItem>())
            {
                if (drop == null || drop.stack == null || drop.stack.IsEmpty) continue;
                if (string.IsNullOrEmpty(drop.NetId))
                {
                    drop.NetId = NewId();
                    drop.NetOwned = true;
                    _byId[drop.NetId] = drop;
                }
                var json = persistence.CaptureStackJson(drop.stack);
                if (json == null) continue;
                yield return new DropRecord
                {
                    Id = drop.NetId,
                    StackJson = json,
                    Position = drop.transform.position,
                    Settled = drop.IsSettled
                };
            }
        }
    }
}
