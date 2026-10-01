// Assets/Scripts/VoxelEngine/Networking/BuildingSync.cs
//
// 14.4.0-dev - Multiplayer milestone 3, phase 1: building replication.
//
// The seam between the building system and the network. Gameplay code calls
// the Announce* methods at its authority points (place / upgrade / destroy);
// NetworkBootstrap carries them over the wire and calls the Apply* methods on
// the other machines. This file deliberately contains NO Fish-Net types -
// gameplay stays transport-free (README section 4), and the bootstrap stays
// the only class that talks to the network.
//
// Piece identity is positional: pieces sit on snapped construction points, so
// (family, position-within-25cm) names a piece uniquely on every machine -
// the same assumption world saves already make. Remote pieces instantiate
// exactly like save-restored pieces: unarmed, stable, no decay audits of
// their own. Cascade convergence comes from the ORIGIN machine announcing
// every collapse its audits decide (StructuralLoadState hooks).
//
// Phase 1 scope: live tiered pieces only. Not yet synced: pre-session bases
// (join-in-progress snapshot), code locks on pieces, voxel edits, machines.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building.Tiered;

namespace VoxelEngine.Networking
{
    /// <summary>One placed piece on the wire (join-in-progress snapshot, 14.5.0).
    /// Plain data - Fish-Net generates the serializer where NetworkBootstrap
    /// embeds it in a broadcast.</summary>
    public struct PieceSnapshot
    {
        public string Family;
        public int Tier;
        public Vector3 Position;
        public Quaternion Rotation;
        public int Hp;
        public float RailingRise;
        public float PillarHeight;
        // Door/hatch state (14.6.0).
        public bool DoorOpen;
        public float DoorSide;
        // Code lock (14.6.0).
        public bool HasLock;
        public string LockCode;
        public bool LockLocked;
        public List<string> LockAuthorized;
    }

    public static class BuildingSync
    {
        /// <summary>Raised while a remote edit is being applied locally, so
        /// the gameplay hooks never announce an echo back into the network.</summary>
        public static bool IsApplyingRemote { get; private set; }

        // ─────────────── local action -> network (gameplay calls these) ───────────────

        public static void AnnouncePlaced(TieredBlockDefinition def, BuildTier tier,
            Vector3 pos, Quaternion rot, float railingRise, float pillarHeight)
        {
            if (def == null || !ShouldAnnounce()) return;
            NetworkBootstrap.Instance.SendPiecePlaced(def.family.ToString(), (int)tier,
                pos, rot, railingRise, pillarHeight);
        }

        public static void AnnounceRemoved(BuildFamily family, Vector3 pos)
        {
            if (!ShouldAnnounce()) return;
            NetworkBootstrap.Instance.SendPieceRemoved(family.ToString(), pos);
        }

        public static void AnnounceUpgraded(BuildFamily family, Vector3 pos, BuildTier newTier)
        {
            if (!ShouldAnnounce()) return;
            NetworkBootstrap.Instance.SendPieceUpgraded(family.ToString(), pos, (int)newTier);
        }

        /// <summary>Surviving damage (decay tick, partial hit): replicate hp so
        /// cracks bloom on every machine, not just where the audit ran (14.5.1).</summary>
        public static void AnnounceDamaged(BuildFamily family, Vector3 pos, int hp)
        {
            if (!ShouldAnnounce()) return;
            NetworkBootstrap.Instance.SendPieceDamaged(family.ToString(), pos, hp);
        }

        /// <summary>Door, gate, garage or hatch toggled (14.6.0). Called by the
        /// components themselves; sideSign keeps the leaf swinging the same way.</summary>
        public static void AnnounceDoorState(Component doorRoot, bool open, float sideSign)
        {
            if (doorRoot == null || !ShouldAnnounce()) return;
            var pb = doorRoot.GetComponentInParent<PlacedTieredBlock>();
            if (pb == null || pb.definition == null) return;
            NetworkBootstrap.Instance.SendDoorState(pb.definition.family.ToString(),
                pb.transform.position, open, sideSign);
        }

        /// <summary>Full lock state (fit, code set, guest authorized, lock toggle) - one
        /// idempotent message covers every keypad authority point (14.6.0).</summary>
        public static void AnnounceLockState(CodeLock codeLock)
        {
            if (codeLock == null || !ShouldAnnounce()) return;
            var pb = codeLock.GetComponentInParent<PlacedTieredBlock>();
            if (pb == null || pb.definition == null) return;
            NetworkBootstrap.Instance.SendLockState(pb.definition.family.ToString(),
                pb.transform.position, codeLock.code ?? "", codeLock.isLocked,
                new List<string>(codeLock.authorizedIds));
        }

        public static void AnnounceLockRemoved(CodeLock codeLock)
        {
            if (codeLock == null || !ShouldAnnounce()) return;
            var pb = codeLock.GetComponentInParent<PlacedTieredBlock>();
            if (pb == null || pb.definition == null) return;
            NetworkBootstrap.Instance.SendLockRemoved(pb.definition.family.ToString(),
                pb.transform.position);
        }

        private static bool ShouldAnnounce()
            => !IsApplyingRemote
               && NetworkSession.Mode != SessionMode.Offline
               && NetworkBootstrap.Instance != null
               && !NetworkBootstrap.Instance.WorldMismatch;   // wrong terrain: stay silent

        // ─────────────── network -> local world (bootstrap calls these) ───────────────

        public static void ApplyPlaced(string family, int tier, Vector3 pos, Quaternion rot,
            float railingRise, float pillarHeight)
        {
            var def = ResolveDefinition(family);
            if (def == null) return;
            if (FindPieceAt(family, pos) != null) return;   // already present - duplicate-safe
            SpawnRemote(def, tier, pos, rot, railingRise, pillarHeight, hp: 0, playSound: true);
        }

        /// <summary>Join-in-progress merge (14.5.0): apply a chunk of pieces the
        /// other side already had. Silent, duplicate-safe, cracks from hp.</summary>
        public static void ApplySnapshot(List<PieceSnapshot> pieces)
        {
            if (pieces == null) return;
            foreach (var p in pieces)
            {
                var def = ResolveDefinition(p.Family);
                if (def == null) continue;
                var piece = FindPieceAt(p.Family, p.Position);
                if (piece == null)
                    piece = SpawnRemote(def, p.Tier, p.Position, p.Rotation, p.RailingRise,
                        p.PillarHeight, p.Hp, playSound: false);
                else if (p.Hp > 0 && p.Hp != piece.hp)
                {
                    // Rejoin convergence: an already-present piece adopts the
                    // origin's hp so cracks and remaining hits stay in step.
                    piece.hp = p.Hp;
                    int maxHp = Mathf.Max(1, def.GetStats((BuildTier)p.Tier).hp);
                    VoxelEngine.Thermal.BlockDamageVisual.ReportDamage(
                        piece, 1f - Mathf.Clamp01(piece.hp / (float)maxHp));
                }
                if (piece == null) continue;
                ApplyDoorStateTo(piece, p.DoorOpen, p.DoorSide);
                if (p.HasLock) ApplyLockStateTo(piece, p.LockCode, p.LockLocked, p.LockAuthorized);
            }
        }

        /// <summary>Everything standing right now, as wire-ready snapshot data.</summary>
        public static List<PieceSnapshot> GatherSnapshot()
            => new List<PieceSnapshot>(StreamSnapshot());

        /// <summary>Lazy form of GatherSnapshot (14.24.1), yielding one piece at a
        /// time so a join can capture a few per frame instead of walking the whole
        /// base in one stalled frame. Enumerate it once.</summary>
        public static IEnumerable<PieceSnapshot> StreamSnapshot()
        {
            foreach (var pb in Object.FindObjectsByType<PlacedTieredBlock>(FindObjectsSortMode.None))
            {
                if (pb == null || pb.definition == null) continue;   // ghosts carry no definition
                float rise = 0f, height = 0f;
                if (pb.TryGetComponent<TieredRailing>(out var railing)) rise = railing.AppliedRise;
                if (pb.TryGetComponent<AdjustablePillar>(out var pillar)) height = pillar.currentHeight;
                bool doorOpen = false; float doorSide = 0f;
                if (pb.TryGetComponent<TieredDoor>(out var door)) { doorOpen = door.IsOpen; doorSide = door.OpenSideSign; }
                else if (pb.TryGetComponent<TieredHatch>(out var hatch)) { doorOpen = hatch.IsOpen; doorSide = 1f; }
                var snap = new PieceSnapshot
                {
                    Family = pb.definition.family.ToString(),
                    Tier = (int)pb.tier,
                    Position = pb.transform.position,
                    Rotation = pb.transform.rotation,
                    Hp = pb.hp,
                    RailingRise = rise,
                    PillarHeight = height,
                    DoorOpen = doorOpen,
                    DoorSide = doorSide
                };
                var codeLock = pb.GetComponentInChildren<CodeLock>(true);
                if (codeLock != null)
                {
                    snap.HasLock = true;
                    snap.LockCode = codeLock.code ?? "";
                    snap.LockLocked = codeLock.isLocked;
                    snap.LockAuthorized = new List<string>(codeLock.authorizedIds);
                }
                yield return snap;
            }
        }

        /// <summary>Restore-style instantiation shared by live placement and the
        /// snapshot merge. Remote pieces stay UNARMED - see file header.</summary>
        private static PlacedTieredBlock SpawnRemote(TieredBlockDefinition def, int tier, Vector3 pos,
            Quaternion rot, float railingRise, float pillarHeight, int hp, bool playSound)
        {
            var prefab = def.GetPrefab((BuildTier)tier);
            if (prefab == null) return null;

            IsApplyingRemote = true;
            try
            {
                var go = Object.Instantiate(prefab, pos, rot);
                go.name = $"{def.displayName} ({(BuildTier)tier}, remote)";
                if (go.TryGetComponent<TieredRailing>(out var railing)) railing.Configure(railingRise);
                if (go.TryGetComponent<AdjustablePillar>(out var pillar) && pillarHeight > 0f)
                    pillar.Configure(pillarHeight);
                var pb = go.GetComponent<PlacedTieredBlock>();
                if (pb == null) pb = go.AddComponent<PlacedTieredBlock>();
                pb.Initialize(def, (BuildTier)tier);
                if (hp > 0)
                {
                    // Carry damage across the join - cracks match the origin world.
                    pb.hp = hp;
                    int maxHp = Mathf.Max(1, def.GetStats((BuildTier)tier).hp);
                    VoxelEngine.Thermal.BlockDamageVisual.ReportDamage(
                        pb, 1f - Mathf.Clamp01(pb.hp / (float)maxHp));
                }
                if (playSound)
                    // The neighbor's hammer is audible presence - same thunk, softer.
                    VoxelEngine.FX.AudioManager.PlayAt(
                        VoxelEngine.FX.SfxLibrary.Get(VoxelEngine.FX.Sfx.Place), pos,
                        volume: 0.5f, pitch: 1f, maxDistance: 20f);
                return pb;
            }
            finally { IsApplyingRemote = false; }
        }

        public static void ApplyDamaged(string family, Vector3 pos, int hp)
        {
            var piece = FindPieceAt(family, pos);
            if (piece == null || piece.definition == null) return;
            piece.hp = hp;
            int maxHp = Mathf.Max(1, piece.definition.GetStats(piece.tier).hp);
            VoxelEngine.Thermal.BlockDamageVisual.ReportDamage(
                piece, 1f - Mathf.Clamp01(piece.hp / (float)maxHp));
        }

        public static void ApplyDoorState(string family, Vector3 pos, bool open, float sideSign)
        {
            var piece = FindPieceAt(family, pos);
            if (piece != null) ApplyDoorStateTo(piece, open, sideSign);
        }

        public static void ApplyLockState(string family, Vector3 pos, string code,
            bool locked, List<string> authorizedIds)
        {
            var piece = FindPieceAt(family, pos);
            if (piece != null) ApplyLockStateTo(piece, code, locked, authorizedIds);
        }

        public static void ApplyLockRemoved(string family, Vector3 pos)
        {
            var piece = FindPieceAt(family, pos);
            var codeLock = piece != null ? piece.GetComponentInChildren<CodeLock>(true) : null;
            if (codeLock == null) return;
            IsApplyingRemote = true;
            try { Object.Destroy(codeLock.gameObject); }
            finally { IsApplyingRemote = false; }
        }

        private static void ApplyDoorStateTo(PlacedTieredBlock piece, bool open, float sideSign)
        {
            if (piece.TryGetComponent<TieredDoor>(out var door)) { door.SetOpenState(open, sideSign); return; }
            if (piece.TryGetComponent<TieredHatch>(out var hatch)) hatch.SetOpen(open);
        }

        private static void ApplyLockStateTo(PlacedTieredBlock piece, string code,
            bool locked, List<string> authorizedIds)
        {
            IsApplyingRemote = true;
            try
            {
                var codeLock = piece.GetComponentInChildren<CodeLock>(true);
                if (codeLock == null) codeLock = CodeLock.Attach(piece.gameObject);
                if (codeLock == null) return;
                codeLock.code = code ?? "";
                codeLock.isLocked = locked;
                codeLock.authorizedIds = authorizedIds != null
                    ? new List<string>(authorizedIds) : new List<string>();
                codeLock.RefreshLed();
            }
            finally { IsApplyingRemote = false; }
        }

        public static void ApplyRemoved(string family, Vector3 pos)
        {
            var piece = FindPieceAt(family, pos);
            if (piece == null) return;
            IsApplyingRemote = true;
            try { Object.Destroy(piece.gameObject); }
            finally { IsApplyingRemote = false; }
            // OnDestroy still calls StructuralLoadState.NotifySupportRemoved, so
            // LOCAL armed pieces react - and their collapses are announced by
            // the machine whose audit decides them, keeping worlds converged.
        }

        public static void ApplyUpgraded(string family, Vector3 pos, int newTier)
        {
            var piece = FindPieceAt(family, pos);
            var def = piece != null && piece.definition != null ? piece.definition : ResolveDefinition(family);
            if (def == null) return;
            var prefab = def.GetPrefab((BuildTier)newTier);
            if (prefab == null) return;

            IsApplyingRemote = true;
            try
            {
                Quaternion rot = piece != null ? piece.transform.rotation : Quaternion.identity;
                Vector3 exactPos = piece != null ? piece.transform.position : pos;
                float pillarHeight = 0f;
                if (piece != null && piece.TryGetComponent<AdjustablePillar>(out var oldPillar))
                    pillarHeight = oldPillar.currentHeight;
                if (piece != null) Object.Destroy(piece.gameObject);

                var go = Object.Instantiate(prefab, exactPos, rot);
                go.name = $"{def.displayName} ({(BuildTier)newTier}, remote)";
                if (go.TryGetComponent<AdjustablePillar>(out var newPillar) && pillarHeight > 0f)
                    newPillar.Configure(pillarHeight);
                var pb = go.GetComponent<PlacedTieredBlock>();
                if (pb == null) pb = go.AddComponent<PlacedTieredBlock>();
                pb.Initialize(def, (BuildTier)newTier);
            }
            finally { IsApplyingRemote = false; }
        }

        // ─────────────── helpers ───────────────

        private static TieredBlockDefinition ResolveDefinition(string family)
        {
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            return persistence != null ? persistence.FindTieredByFamily(family) : null;
        }

        /// <summary>Positional identity: nearest same-family piece within 25 cm.</summary>
        private static PlacedTieredBlock FindPieceAt(string family, Vector3 pos)
        {
            PlacedTieredBlock best = null;
            float bestSq = 0.25f * 0.25f;
            foreach (var pb in Object.FindObjectsByType<PlacedTieredBlock>(FindObjectsSortMode.None))
            {
                if (pb == null || pb.definition == null) continue;
                if (pb.definition.family.ToString() != family) continue;
                float d = (pb.transform.position - pos).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = pb; }
            }
            return best;
        }
    }
}
