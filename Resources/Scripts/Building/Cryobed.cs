// Assets/Scripts/VoxelEngine/Building/Cryobed.cs
//
// Static cryobed foundation for offline survival. Current pass provides the
// spawn/offline-safe anchor; oxygen/power requirements are added by the later
// life-support pass.

using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Building
{
    public class Cryobed : MonoBehaviour
    {
        public string displayName = "Cryobed";
        public bool oxygenRequired = true;
        public bool poweredRequired = true;

        public void ClaimAsSpawn()
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session == null) return;
            Vector3 up = GravityProvider.GetUp(transform.position);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            session.bedSpawnPoint = transform.position + up.normalized * 1.35f;
            session.hasBedSpawn = true;
            session.SaveSpawnSidecar();
            VoxelEngine.UI.BuildFeedbackHud.Show("Cryobed Linked", "Respawn/offline safety point updated", null, new Color(0.45f, 0.85f, 1f));
            Debug.Log($"[Cryobed] Spawn point set to {session.bedSpawnPoint}");
        }
    }
}
