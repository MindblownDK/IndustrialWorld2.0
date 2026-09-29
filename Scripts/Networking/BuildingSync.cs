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

using UnityEngine;
using VoxelEngine.Building.Tiered;

namespace VoxelEngine.Networking
{
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

        private static bool ShouldAnnounce()
            => !IsApplyingRemote
               && NetworkSession.Mode != SessionMode.Offline
               && NetworkBootstrap.Instance != null;

        // ─────────────── network -> local world (bootstrap calls these) ───────────────

        public static void ApplyPlaced(string family, int tier, Vector3 pos, Quaternion rot,
            float railingRise, float pillarHeight)
        {
            var def = ResolveDefinition(family);
            if (def == null) return;
            if (FindPieceAt(family, pos) != null) return;   // already present - duplicate-safe
            var prefab = def.GetPrefab((BuildTier)tier);
            if (prefab == null) return;

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
                // The neighbor's hammer is audible presence - same thunk, softer.
                VoxelEngine.FX.AudioManager.PlayAt(
                    VoxelEngine.FX.SfxLibrary.Get(VoxelEngine.FX.Sfx.Place), pos,
                    volume: 0.5f, pitch: 1f, maxDistance: 20f);
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
