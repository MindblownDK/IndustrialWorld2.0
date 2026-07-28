// Assets/Scripts/VoxelEngine/Player/PlayerRaycastFilter.cs
//
// Shared local-player raycast filtering. Crosshair tools should never hit the
// player's own CharacterController/body when looking down, but future multiplayer
// still needs other players to remain targetable.

using UnityEngine;

namespace VoxelEngine.Player
{
    public static class PlayerRaycastFilter
    {
        public static bool IsOwnPlayerCollider(Collider collider, Transform requester)
        {
            if (collider == null || requester == null) return false;
            var hitPlayer = collider.GetComponentInParent<PlayerController>();
            if (hitPlayer == null) return false;

            var ownPlayer = requester.GetComponentInParent<PlayerController>();
            if (ownPlayer != null) return hitPlayer == ownPlayer;

            Transform root = requester.root;
            return root != null && hitPlayer.transform == root;
        }
    }
}
