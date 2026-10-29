// Assets/Scripts/VoxelEngine/Networking/SharedOwnership.cs
//
// 18.9.0-dev — Milestone 11: Teams & Shared Ownership.
//
// One canonical resolution rule for grid ownership, static block ownership,
// tiered base pieces, code locks, cryobeds, and automated defense IFF.
// Everything resolves live through TeamRegistry.SameTeam(ownerId, playerId)
// so joining or leaving a team updates permissions across ships, stations,
// machines, doors, and turrets immediately without touching individual blocks.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Building.Tiered;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Networking
{
    public enum AccessShareMode
    {
        Private = 0,
        Team    = 1,
        Global  = 2
    }

    public static class SharedOwnership
    {
        private static float _nextDenyToastAt;

        public static AccessShareMode ClampMode(int raw)
        {
            if (raw <= 0) return AccessShareMode.Private;
            if (raw >= 2) return AccessShareMode.Global;
            return AccessShareMode.Team;
        }

        public static bool IsOwner(string ownerId, string playerId)
        {
            if (string.IsNullOrEmpty(ownerId)) return false;
            string checkId = string.IsNullOrEmpty(playerId) ? NetworkSession.LocalPlayerId : playerId;
            return !string.IsNullOrEmpty(checkId) && ownerId == checkId;
        }

        /// <summary>True when a piece is unowned (legacy save), owned by playerId,
        /// or owned by a current teammate of playerId.</summary>
        public static bool AllowsOwnerOrTeam(string ownerId, string playerId = null)
        {
            if (string.IsNullOrEmpty(ownerId)) return true;
            string checkId = string.IsNullOrEmpty(playerId) ? NetworkSession.LocalPlayerId : playerId;
            if (string.IsNullOrEmpty(checkId)) return true;
            if (ownerId == checkId) return true;
            return TeamRegistry.SameTeam(ownerId, checkId);
        }

        /// <summary>True when playerId is permitted to operate or modify an entity
        /// with the given ownerId and AccessShareMode.</summary>
        public static bool AllowsAccess(string ownerId, AccessShareMode mode, string playerId = null)
        {
            if (string.IsNullOrEmpty(ownerId)) return true;
            string checkId = string.IsNullOrEmpty(playerId) ? NetworkSession.LocalPlayerId : playerId;
            if (string.IsNullOrEmpty(checkId)) return true;
            if (ownerId == checkId) return true;

            return mode switch
            {
                AccessShareMode.Global  => true,
                AccessShareMode.Team    => TeamRegistry.SameTeam(ownerId, checkId),
                AccessShareMode.Private => false,
                _                       => TeamRegistry.SameTeam(ownerId, checkId)
            };
        }

        public static bool AllowsAccess(string ownerId, int modeInt, string playerId = null)
            => AllowsAccess(ownerId, ClampMode(modeInt), playerId);

        public static string FormatModeLabel(AccessShareMode mode) => mode switch
        {
            AccessShareMode.Private => "PRIVATE",
            AccessShareMode.Team    => "TEAM",
            AccessShareMode.Global  => "GLOBAL",
            _                       => "TEAM"
        };

        public static string FormatOwnerDisplay(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId)) return "Unclaimed";
            string localId = NetworkSession.LocalPlayerId;
            var team = TeamRegistry.TeamOf(ownerId);
            string teamSuffix = team != null && !string.IsNullOrEmpty(team.name)
                ? $" [{team.name}]"
                : "";

            if (!string.IsNullOrEmpty(localId) && ownerId == localId)
                return "You" + teamSuffix;

            string display = ResolvePlayerName(ownerId);
            return display + teamSuffix;
        }

        public static string ResolvePlayerName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return "Unclaimed";
            if (playerId == NetworkSession.LocalPlayerId)
                return PlayerIdentity.LocalName;

            var presence = NetworkSession.GetPlayer(playerId);
            if (presence != null && !string.IsNullOrEmpty(presence.displayName))
                return presence.displayName;

            foreach (var avatar in PlayerAvatar.All)
            {
                if (avatar != null && avatar.PlayerId == playerId && !string.IsNullOrEmpty(avatar.PlayerName))
                    return avatar.PlayerName;
            }

            return playerId.Length > 8 ? playerId.Substring(0, 8) : playerId;
        }

        /// <summary>Collects the player IDs of all teammates of localPlayerId (excluding localPlayerId).</summary>
        public static List<string> GetTeammateIds(string localPlayerId = null)
        {
            var result = new List<string>();
            string me = string.IsNullOrEmpty(localPlayerId) ? NetworkSession.LocalPlayerId : localPlayerId;
            if (string.IsNullOrEmpty(me)) return result;
            var team = TeamRegistry.TeamOf(me);
            if (team == null || team.memberIds == null) return result;
            for (int i = 0; i < team.memberIds.Count; i++)
            {
                string id = team.memberIds[i];
                if (!string.IsNullOrEmpty(id) && id != me && !result.Contains(id))
                    result.Add(id);
            }
            return result;
        }

        /// <summary>Cycles ownership to the next teammate on the owner's team. Returns true if transferred.</summary>
        public static bool TryCycleTeammateTransfer(ref string ownerId, string subjectTitle)
        {
            string me = NetworkSession.LocalPlayerId;
            var mates = GetTeammateIds(me);
            if (mates.Count == 0)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    subjectTitle,
                    "Join or create a team in Pause -> TEAMS to transfer ownership to a teammate.",
                    null,
                    new Color(0.95f, 0.62f, 0.18f));
                return false;
            }

            int idx = mates.IndexOf(ownerId);
            string nextId = mates[(idx + 1) % mates.Count];
            ownerId = nextId;
            string nextName = ResolvePlayerName(nextId);
            VoxelEngine.UI.BuildFeedbackHud.Show(
                subjectTitle,
                $"Ownership transferred to teammate {nextName}.",
                null,
                new Color(0.35f, 0.88f, 0.65f));
            return true;
        }

        public static void ShowAccessDeniedToast(string subject, string ownerId, AccessShareMode mode)
        {
            if (Time.unscaledTime < _nextDenyToastAt) return;
            _nextDenyToastAt = Time.unscaledTime + 1.25f;

            string ownerLabel = FormatOwnerDisplay(ownerId);
            string reason = mode == AccessShareMode.Private
                ? $"Private — owned by {ownerLabel}"
                : $"Team only — owned by {ownerLabel}";

            VoxelEngine.UI.BuildFeedbackHud.Show(
                string.IsNullOrEmpty(subject) ? "Access Denied" : subject,
                reason,
                null,
                new Color(0.92f, 0.32f, 0.25f));
        }

        /// <summary>Resolves the ownerId of any defense turret or weapon component.</summary>
        public static string ResolveOwnerId(Component source)
        {
            if (source == null) return "";
            var pb = source.GetComponentInParent<PlacedBlock>();
            if (pb != null && !string.IsNullOrEmpty(pb.ownerId))
                return pb.ownerId;
            var ptb = source.GetComponentInParent<PlacedTieredBlock>();
            if (ptb != null && !string.IsNullOrEmpty(ptb.ownerId))
                return ptb.ownerId;
            var grid = source.GetComponentInParent<GridEntity>();
            if (grid != null && !string.IsNullOrEmpty(grid.ownerId))
                return grid.ownerId;
            return "";
        }

        /// <summary>True when a turret with TargetFilter.Players should treat the local
        /// player as a hostile target. An unowned turret in singleplayer/offline still
        /// fires when the player explicitly turns Players filter on for testing, but an
        /// owned turret NEVER shoots its own owner or its owner's teammates.</summary>
        public static bool IsLocalPlayerHostileToDefense(Component defenseSource, VoxelEngine.Player.PlayerStats localPlayer)
        {
            if (localPlayer == null || localPlayer.Health <= 0f) return false;
            string ownerId = ResolveOwnerId(defenseSource);
            if (string.IsNullOrEmpty(ownerId)) return true;
            return !AllowsOwnerOrTeam(ownerId, NetworkSession.LocalPlayerId);
        }

        /// <summary>Finds the nearest hostile remote PlayerAvatar in range for automated turrets.</summary>
        public static bool TryFindHostileAvatarTarget(
            Component defenseSource,
            Vector3 from,
            float maxRangeSqr,
            out Transform bestTransform,
            out PlayerAvatar bestAvatar,
            out float bestSqr)
        {
            bestTransform = null;
            bestAvatar = null;
            bestSqr = maxRangeSqr;

            if (NetworkSession.Mode == SessionMode.Offline) return false;

            string ownerId = ResolveOwnerId(defenseSource);
            foreach (var avatar in PlayerAvatar.All)
            {
                if (avatar == null || avatar.HealthPercent <= 0) continue;
                string targetId = avatar.PlayerId;
                if (string.IsNullOrEmpty(targetId)) continue;
                if (!string.IsNullOrEmpty(ownerId) && AllowsOwnerOrTeam(ownerId, targetId))
                    continue;

                float sqr = (avatar.transform.position - defenseSource.transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                Vector3 chest = avatar.transform.position + avatar.transform.up * 1.0f;
                Vector3 dir = chest - from;
                float dist = dir.magnitude;
                if (dist > 0.05f && Physics.Raycast(from, dir / dist, out var hit, dist, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.GetComponentInParent<PlayerAvatar>() != avatar)
                        continue;
                }

                bestSqr = sqr;
                bestAvatar = avatar;
                bestTransform = avatar.transform;
            }
            return bestTransform != null;
        }
    }
}
