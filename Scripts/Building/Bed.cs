// Assets/Scripts/VoxelEngine/Building/Bed.cs
//
// Placed bed = a respawn point. RMB while looking at it -> sets the player's spawn
// point to this bed's position (the previous bed, if any, loses ownership).
//
// Spawn-point state is stored on the persistent WorldSession singleton so it survives
// scene reloads, and is written into the world's bed.json sidecar at quit/save. In
// multiplayer the claimed spawn ALSO rides the per-player record the host keeps
// (14.38.0), so a guest's bed survives a rejoin.
//
// 14.38.0 - beds are PERSONAL. A bed remembers who placed it (stable player id,
// captured once at placement exactly like a banner's team) and only its owner and
// the owner's teammates may claim it or respawn at it. Legacy beds restore with no
// owner and stay usable by everyone - an old save never locks anyone out.

using UnityEngine;

namespace VoxelEngine.Building
{
    public class Bed : MonoBehaviour
    {
        public string displayName = "Bed";

        /// <summary>Stable player id of whoever placed this bed. Empty = an
        /// unowned (legacy) bed anyone may use.</summary>
        public string ownerId = "";

        private bool _explicitOwner;

        private void Awake()
        {
            // Fresh LOCAL placement: capture the placer right now, before the
            // block is announced to the network. A remote spawn or a save
            // restore overrides this through SetOwner immediately after
            // Instantiate, so a wrong local guess never survives.
            if (!_explicitOwner && string.IsNullOrEmpty(ownerId)
                && !Networking.BlockSync.IsApplyingRemote)
            {
                ownerId = Networking.NetworkSession.LocalPlayerId ?? "";
            }
        }

        /// <summary>Authoritative owner assignment from the network spawn or
        /// the save restore - always wins over the Awake guess. An empty id is
        /// an explicit "unowned", never an invitation to guess again.</summary>
        public void SetOwner(string playerId)
        {
            _explicitOwner = true;
            ownerId = playerId ?? "";
        }

        /// <summary>May this player claim or respawn at this bed? Owner and
        /// the owner's teammates yes; an unowned legacy bed welcomes anyone.</summary>
        public bool UsableBy(string playerId)
        {
            if (string.IsNullOrEmpty(ownerId)) return true;
            if (ownerId == playerId) return true;
            return Networking.TeamRegistry.SameTeam(ownerId, playerId);
        }

        /// <summary>Mark THIS bed as the player's spawn point.</summary>
        public void ClaimAsSpawn()
        {
            var session = Menu.WorldSession.Instance;
            if (session == null) return;

            if (!UsableBy(Networking.NetworkSession.LocalPlayerId ?? ""))
            {
                UI.BuildFeedbackHud.Show("Not Your Bed",
                    "This bed belongs to another crusader - only its owner and their team sleep here.",
                    null, new Color(0.95f, 0.45f, 0.25f));
                return;
            }

            // World coords of this bed; player spawns slightly above to drop in.
            session.bedSpawnPoint   = transform.position + Vector3.up * 1.2f;
            session.hasBedSpawn     = true;
            // 14.60.6 - cosmic record via the ONE central writer (body-relative
            // when a body is near): every link path must refresh it, or a stale
            // record from an earlier link hijacks the next death respawn.
            session.RefreshBedCosmic();
            session.SaveSpawnSidecar();
            UI.BuildFeedbackHud.Show("Bed Linked",
                "Respawn point updated - you will wake up here.",
                null, new Color(0.30f, 0.95f, 0.62f));
            Debug.Log($"[Bed] Spawn point set to {session.bedSpawnPoint}");
        }

        private void OnDestroy()
        {
            // If THIS bed was the active spawn, clear it so the player falls back to world spawn.
            var session = Menu.WorldSession.Instance;
            if (session != null && session.hasBedSpawn)
            {
                if (Vector3.Distance(session.bedSpawnPoint, transform.position + Vector3.up * 1.2f) < 0.5f)
                {
                    session.hasBedSpawn = false;
                    session.SaveSpawnSidecar();
                    Debug.Log("[Bed] Player's bed was destroyed — falling back to world spawn.");
                }
            }
        }
    }
}
