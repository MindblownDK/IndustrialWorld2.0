// Assets/Scripts/VoxelEngine/Building/Bed.cs
//
// Placed bed = a respawn point. RMB while looking at it -> sets the player's spawn
// point to this bed's position (the previous bed, if any, loses ownership).
//
// Spawn-point state is stored on the persistent WorldSession singleton so it survives
// scene reloads, and is written into the world's bed.json sidecar at quit/save.

using UnityEngine;

namespace VoxelEngine.Building
{
    public class Bed : MonoBehaviour
    {
        public string displayName = "Bed";

        /// <summary>Mark THIS bed as the player's spawn point.</summary>
        public void ClaimAsSpawn()
        {
            var session = Menu.WorldSession.Instance;
            if (session == null) return;
            // World coords of this bed; player spawns slightly above to drop in.
            session.bedSpawnPoint   = transform.position + Vector3.up * 1.2f;
            session.hasBedSpawn     = true;
            session.SaveSpawnSidecar();
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
