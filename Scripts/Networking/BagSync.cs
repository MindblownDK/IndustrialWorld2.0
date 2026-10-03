// Assets/Scripts/VoxelEngine/Networking/BagSync.cs
//
// 14.36.0-dev - Death loot bags over the wire, riding the exact DropSync
// pattern: the machine where the death happened spawns and announces the
// bag, every mutation re-announces the whole payload (bags are rare and
// small - whole-state beats deltas at 2-8 players), removal is announced by
// whoever emptied it, and a join snapshot carries all live bags.
//
// Authority note: a death is applied on the VICTIM's machine (PlayerCombat
// forwards server-validated damage orders there), so the victim's machine is
// the one true origin of its own loot bag - the same "announce what you
// spawned" rule every DroppedItem already follows.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Networking
{
    /// <summary>One live loot bag on the wire (join snapshot).</summary>
    public struct BagRecord
    {
        public string Id;
        public string OwnerId;
        public string OwnerName;
        public Vector3 Position;
        public string Json;
    }

    public static class BagSync
    {
        /// <summary>Raised while a remote bag event is being applied locally, so
        /// the DeathLootBag hooks never announce an echo back into the network.</summary>
        public static bool IsApplyingRemote { get; private set; }

        private static bool ShouldAnnounce =>
            !IsApplyingRemote
            && NetworkSession.Mode != SessionMode.Offline
            && NetworkBootstrap.Instance != null
            && !NetworkBootstrap.Instance.WorldMismatch;

        public static void AnnounceSpawned(DeathLootBag bag)
        {
            if (bag == null || !ShouldAnnounce) return;
            NetworkBootstrap.Instance.SendBagSpawned(
                bag.BagId, bag.OwnerId, bag.OwnerName, bag.transform.position, bag.ToPayloadJson());
        }

        public static void AnnounceUpdated(DeathLootBag bag)
        {
            if (bag == null || !ShouldAnnounce) return;
            NetworkBootstrap.Instance.SendBagUpdated(bag.BagId, bag.ToPayloadJson());
        }

        /// <summary>Every removal path funnels here (emptied, remote removal).</summary>
        public static void HandleDespawn(DeathLootBag bag)
        {
            if (bag == null || !ShouldAnnounce) return;
            NetworkBootstrap.Instance.SendBagRemoved(bag.BagId);
        }

        public static void ApplySpawned(string id, string ownerId, string ownerName,
            Vector3 pos, string json)
        {
            IsApplyingRemote = true;
            try { DeathLootBag.SpawnExisting(id, ownerId, ownerName, pos, json); }
            finally { IsApplyingRemote = false; }
        }

        public static void ApplyUpdated(string id, string json)
        {
            var bag = DeathLootBag.Find(id);
            if (bag == null) return;
            IsApplyingRemote = true;
            try { bag.ApplyPayloadJson(json); }
            finally { IsApplyingRemote = false; }
        }

        public static void ApplyRemoved(string id)
        {
            var bag = DeathLootBag.Find(id);
            if (bag == null) return;
            IsApplyingRemote = true;
            try { bag.Despawn(); }
            finally { IsApplyingRemote = false; }
        }

        /// <summary>Join merge: spawn what is missing, update what exists - by id.</summary>
        public static void ApplySnapshot(List<BagRecord> records)
        {
            if (records == null) return;
            foreach (var r in records)
                ApplySpawned(r.Id, r.OwnerId, r.OwnerName, r.Position, r.Json);
        }

        public static IEnumerable<BagRecord> StreamSnapshot()
        {
            foreach (var bag in DeathLootBag.All)
            {
                if (bag == null) continue;
                yield return new BagRecord
                {
                    Id = bag.BagId,
                    OwnerId = bag.OwnerId,
                    OwnerName = bag.OwnerName,
                    Position = bag.transform.position,
                    Json = bag.ToPayloadJson()
                };
            }
        }
    }
}
