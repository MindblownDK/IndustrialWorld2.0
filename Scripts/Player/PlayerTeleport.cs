// Assets/Scripts/VoxelEngine/Player/PlayerTeleport.cs
//
// 14.60.3 - teleport to a player. One static entry point used by the Teams
// tab (teammates, gated by the allowTeammateTeleport world rule) and by the
// Server Administration tab (owner only, no rule gate). The move itself is
// LOCAL: this machine relocates its own player to the target's replicated
// avatar, through the same cosmic-teleport path portals use, so streaming
// and the reference frame re-pick correctly even across long distances.
//
// Limits, by design:
//   - the target must have a live avatar in the scene (a player on a far
//     unstreamed world cannot be resolved to a trustworthy position);
//   - teleporting while piloting is refused - leave the seat first.

using UnityEngine;
using Unity.Mathematics;

namespace VoxelEngine.Player
{
    public static class PlayerTeleport
    {
        /// <summary>Teleport the LOCAL player to the given player's avatar.
        /// Shows its own HUD feedback; returns true when the move happened.</summary>
        public static bool ToPlayer(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return false;

            var avatar = Networking.PlayerAvatar.Find(playerId);
            if (avatar == null)
            {
                Deny("Player is not in range right now - their avatar is not loaded here.");
                return false;
            }
            if (avatar.HealthPercent <= 0)
            {
                Deny("That player is down - nothing to stand next to.");
                return false;
            }

            var pc = Object.FindAnyObjectByType<PlayerController>();
            if (pc == null) { Deny("No local player to move."); return false; }

            if (GridSystem.GridCockpit.ActiveControlPilot == pc)
            {
                Deny("Leave the seat first - a pilot cannot abandon the stick mid-flight.");
                return false;
            }

            // Arrive BESIDE the teammate, not inside them: half a metre up for
            // ground clearance, a body's width to the side.
            Vector3 targetPos = avatar.transform.position;
            Vector3 up = Cosmos.GravityProvider.GetUp(targetPos);
            Vector3 side = Vector3.Cross(up, avatar.transform.forward);
            if (side.sqrMagnitude < 0.01f) side = avatar.transform.right;
            Vector3 dest = targetPos + up * 0.5f + side.normalized * 1.2f;

            var origin = Cosmos.SpaceOrigin.Instance;
            var registry = Cosmos.CosmicRegistry.Instance;
            if (origin != null && registry != null && registry.IsReady)
            {
                // The portal path: re-anchors the cosmos around the subject, so a
                // long move re-picks frame and streaming instead of breaking floats.
                var subject = pc.transform;
                origin.RegisterRoot(subject);
                origin.TeleportSubjectToCosmic(subject, origin.GetCosmicKm(dest));
            }
            else
            {
                // Flat/bootstrap fallback: plain controller-safe move.
                var cc = pc.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                pc.transform.position = dest;
                if (cc != null) cc.enabled = true;
            }
            pc.ResetVelocity();

            string name = string.IsNullOrEmpty(avatar.PlayerName) ? "player" : avatar.PlayerName;
            UI.BuildFeedbackHud.Show("Teleport", $"Teleported to {name}", null,
                new Color(0.55f, 0.85f, 1f));
            return true;
        }

        private static void Deny(string reason)
            => UI.BuildFeedbackHud.Show("Teleport", reason, null, new Color(1f, 0.7f, 0.25f));
    }
}
