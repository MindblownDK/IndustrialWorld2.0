// Assets/Scripts/VoxelEngine/Networking/BlockSync.cs
//
// 14.9.0-dev - Multiplayer milestone 5 begins: item-block replication.
//
// The third seam alongside BuildingSync (tiered pieces) and TerrainSync
// (voxels): static world blocks placed from the hotbar - machines, chests,
// furnaces, conveyors, cables, roads, lights. Gameplay announces at its
// authority points, NetworkBootstrap carries, this class applies. No
// Fish-Net types here.
//
// Identity is positional like tiered pieces: (itemId, position within 25 cm).
// Remote blocks instantiate along the persistence-restore pattern and carry
// the placement-time cosmetic choices (conveyor build shape, cable variant
// and length) so they LOOK identical everywhere.
//
// Phase 1 scope (deliberate): the block itself, its pose, its hp/cracks and
// its wiring-level visuals. Container contents ride the sibling seam
// (ContainerSync, 14.10.0). NOT yet synced: machine runtime state (recipes,
// progress, power flow - that is the heart of milestone 5), grid-ship
// attached blocks (grids are their own milestone), and placement payloads
// (e.g. a pre-filled tank).

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Networking
{
    /// <summary>One placed item-block on the wire (live placement and join snapshot).</summary>
    public struct BlockSnapshot
    {
        public string ItemId;
        public Vector3 Position;
        public Quaternion Rotation;
        public int Hp;
        public int ConveyorShape;   // -1 = not a conveyor
        public int CableVariant;    // -1 = not a cable
        public int CableLength;
        public string BannerTeamId; // null/empty = not a banner (or teamless placer's default emblem)
        public bool IsBanner;       // distinguishes "no banner" from "banner with default emblem"
        public string BedOwnerId;   // 14.38.0 - who placed this bed (empty = unowned legacy bed)
        public bool IsBed;          // distinguishes "no bed" from "bed with no owner"
        public string SecurityOwnerId; // 14.39.0 - who placed this security block
        public int SecurityMode;       // 0 Private, 1 Team, 2 Global
        public bool IsSecurity;        // distinguishes "no guard" from "unowned guard"
    }

    public static class BlockSync
    {
        /// <summary>Raised while a remote edit is being applied locally, so the
        /// gameplay hooks never announce an echo back into the network.</summary>
        public static bool IsApplyingRemote { get; private set; }

        // ─────────────── local action -> network ───────────────

        public static void AnnouncePlaced(PlacedBlock block)
        {
            if (!CanAnnounce(block)) return;
            // Placement is a player action: open the interaction window so the
            // container and machine pollers announce any payload the block was
            // born with (a pre-filled tank, a packed drawer's contents) on their
            // first sighting - even from a client (14.12.0).
            ContainerSync.NotifyLocalInteraction(block);
            NetworkBootstrap.Instance.SendBlockPlaced(Capture(block));
        }

        public static void AnnounceDamaged(PlacedBlock block)
        {
            if (!CanAnnounce(block)) return;
            NetworkBootstrap.Instance.SendBlockDamaged(block.Item.itemId, block.transform.position, block.Hp);
        }

        public static void AnnounceRemoved(PlacedBlock block)
        {
            if (!CanAnnounce(block)) return;
            NetworkBootstrap.Instance.SendBlockRemoved(block.Item.itemId, block.transform.position);
        }

        private static bool CanAnnounce(PlacedBlock block)
        {
            if (block == null || block.Item == null) return false;
            // Grid-attached blocks belong to the movable-grid payload (own milestone) -
            // same exclusion the save system applies.
            if (block.GetComponent<VoxelEngine.GridSystem.GridBlock>()?.Grid != null) return false;
            return !IsApplyingRemote
                   && NetworkSession.Mode != SessionMode.Offline
                   && NetworkBootstrap.Instance != null
                   && !NetworkBootstrap.Instance.WorldMismatch;
        }

        // ─────────────── network -> local world ───────────────

        public static void ApplyPlaced(BlockSnapshot snap)
        {
            if (FindBlockAt(snap.ItemId, snap.Position) != null) return;   // duplicate-safe
            SpawnRemote(snap, playSound: true);
        }

        public static void ApplyDamaged(string itemId, Vector3 pos, int hp)
        {
            var block = FindBlockAt(itemId, pos);
            if (block == null) return;
            block.Hp = hp;
            VoxelEngine.Thermal.BlockDamageVisual.ReportDamage(block, block.Damage01);
        }

        public static void ApplyRemoved(string itemId, Vector3 pos)
        {
            var block = FindBlockAt(itemId, pos);
            if (block == null) return;
            IsApplyingRemote = true;
            try { Object.Destroy(block.gameObject); }
            finally { IsApplyingRemote = false; }
        }

        /// <summary>Join merge: spawn what is missing, converge hp on what exists.</summary>
        public static void ApplySnapshot(List<BlockSnapshot> blocks)
        {
            if (blocks == null) return;
            foreach (var snap in blocks)
            {
                var existing = FindBlockAt(snap.ItemId, snap.Position);
                if (existing != null)
                {
                    if (snap.Hp > 0 && snap.Hp != existing.Hp)
                    {
                        existing.Hp = snap.Hp;
                        VoxelEngine.Thermal.BlockDamageVisual.ReportDamage(existing, existing.Damage01);
                    }
                    continue;
                }
                SpawnRemote(snap, playSound: false);
            }
        }

        /// <summary>Everything standing (non-grid, item-backed), wire-ready.</summary>
        public static List<BlockSnapshot> GatherSnapshot()
            => new List<BlockSnapshot>(StreamSnapshot());

        /// <summary>Lazy form of GatherSnapshot (14.24.1), yielding one block at a
        /// time so a join can capture a few per frame instead of walking the whole
        /// world in one stalled frame. Enumerate it once.</summary>
        public static IEnumerable<BlockSnapshot> StreamSnapshot()
        {
            foreach (var block in Object.FindObjectsByType<PlacedBlock>(FindObjectsSortMode.None))
            {
                if (block == null || block.Item == null) continue;
                if (block.GetComponent<VoxelEngine.GridSystem.GridBlock>()?.Grid != null) continue;
                yield return Capture(block);
            }
        }

        // ─────────────── helpers ───────────────

        private static BlockSnapshot Capture(PlacedBlock block)
        {
            var snap = new BlockSnapshot
            {
                ItemId = block.Item.itemId,
                Position = block.transform.position,
                Rotation = block.transform.rotation,
                Hp = block.Hp,
                ConveyorShape = -1,
                CableVariant = -1
            };
            var belt = block.GetComponentInChildren<VoxelEngine.Simulation.ConveyorBelt>(true);
            if (belt != null) snap.ConveyorShape = (int)belt.shape;
            var cable = block.GetComponentInChildren<VoxelEngine.Power.PowerCable>(true);
            if (cable != null)
            {
                snap.CableVariant = (int)cable.variant;
                snap.CableLength = cable.straightLength;
            }
            var banner = block.GetComponentInChildren<VoxelEngine.Combat.BannerDisplay>(true);
            if (banner != null)
            {
                snap.IsBanner = true;
                snap.BannerTeamId = banner.bannerTeamId ?? "";
            }
            var bed = block.GetComponentInChildren<VoxelEngine.Building.Bed>(true);
            if (bed != null)
            {
                snap.IsBed = true;
                snap.BedOwnerId = bed.ownerId ?? "";
            }
            var guard = block.GetComponentInChildren<VoxelEngine.Storage.SecurityBlock>(true);
            if (guard != null)
            {
                snap.IsSecurity = true;
                snap.SecurityOwnerId = guard.ownerId ?? "";
                snap.SecurityMode = guard.accessMode;
            }
            return snap;
        }

        /// <summary>Restore-style instantiation shared by live placement and the join
        /// merge - the same essentials the save system rebuilds a block with.</summary>
        private static void SpawnRemote(BlockSnapshot snap, bool playSound)
        {
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            var item = persistence != null ? persistence.FindBlockById(snap.ItemId) : null;
            if (item == null || item.placedPrefab == null) return;

            IsApplyingRemote = true;
            try
            {
                var go = Object.Instantiate(item.placedPrefab, snap.Position, snap.Rotation);
                go.name = item.displayName + " (remote)";
                if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<BoxCollider>();

                var block = go.GetComponent<PlacedBlock>();
                if (block == null) block = go.AddComponent<PlacedBlock>();
                block.Item = item;
                block.Hp = snap.Hp > 0 ? snap.Hp : item.blockHealth;
                VoxelEngine.Thermal.BlockDamageVisual.ReportDamage(block, block.Damage01);

                // Placement-time cosmetic choices travel with the block.
                var belt = go.GetComponentInChildren<VoxelEngine.Simulation.ConveyorBelt>(true);
                if (belt != null && snap.ConveyorShape >= 0
                    && System.Enum.IsDefined(typeof(VoxelEngine.Simulation.ConveyorShape), snap.ConveyorShape))
                {
                    belt.SetBuildShape((VoxelEngine.Simulation.ConveyorShape)snap.ConveyorShape);
                    belt.RefreshTopologyImmediate();
                }
                var cable = go.GetComponentInChildren<VoxelEngine.Power.PowerCable>(true);
                if (cable != null && snap.CableVariant >= 0)
                {
                    cable.variant = (VoxelEngine.Power.EnergyPipeVariant)snap.CableVariant;
                    cable.straightLength = Mathf.Clamp(snap.CableLength, 1, 5);
                    cable.RebuildVisuals();
                    VoxelEngine.Power.PowerCable.RefreshNearbyCables(snap.Position, 6f);
                }
                var road = go.GetComponentInChildren<VoxelEngine.Building.AsphaltRoad>(true);
                if (road != null) road.RefreshAfterPlacement();
                if (snap.IsBanner)
                {
                    var banner = go.GetComponentInChildren<VoxelEngine.Combat.BannerDisplay>(true);
                    // Always explicit - an empty id is the placer's "no team"
                    // answer, not an invitation for this machine to guess.
                    if (banner != null) banner.SetTeam(snap.BannerTeamId ?? "");
                }
                if (snap.IsBed)
                {
                    var bedRemote = go.GetComponentInChildren<VoxelEngine.Building.Bed>(true);
                    // Same rule as the banner: the placer's identity travels
                    // with the block, this machine never guesses an owner.
                    if (bedRemote != null) bedRemote.SetOwner(snap.BedOwnerId ?? "");
                }
                if (snap.IsSecurity)
                {
                    var guardRemote = go.GetComponentInChildren<VoxelEngine.Storage.SecurityBlock>(true);
                    // Placer identity + mode travel with the block (14.39.0);
                    // the Awake guess on this machine never wins.
                    if (guardRemote != null)
                    {
                        guardRemote.SetOwner(snap.SecurityOwnerId ?? "");
                        guardRemote.SetMode(snap.SecurityMode);
                    }
                }
                if (item.placedMaterial != null || item.texture != null)
                {
                    var tex = go.AddComponent<BlockTexturizer>();
                    tex.overrideMaterial = item.placedMaterial;
                    tex.overrideTexture = item.texture;
                }
                if (go.GetComponentInChildren<VoxelEngine.Power.PowerNode>(true) != null)
                    VoxelEngine.Power.PowerNetworkManager.Instance?.SetDirty();

                if (playSound)
                    VoxelEngine.FX.AudioManager.PlayAt(
                        VoxelEngine.FX.SfxLibrary.Get(VoxelEngine.FX.Sfx.Place), snap.Position,
                        volume: 0.5f, pitch: 1f, maxDistance: 20f);
            }
            finally { IsApplyingRemote = false; }
        }

        /// <summary>Positional identity: nearest same-item block within 25 cm.</summary>
        internal static PlacedBlock FindBlockAt(string itemId, Vector3 pos)
        {
            PlacedBlock best = null;
            float bestSq = 0.25f * 0.25f;
            foreach (var block in Object.FindObjectsByType<PlacedBlock>(FindObjectsSortMode.None))
            {
                if (block == null || block.Item == null) continue;
                if (block.Item.itemId != itemId) continue;
                float d = (block.transform.position - pos).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = block; }
            }
            return best;
        }
    }
}
