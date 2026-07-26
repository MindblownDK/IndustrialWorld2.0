// Assets/Scripts/VoxelEngine/Building/Cryobed.cs
//
// Static cryobed foundation for offline survival. Current pass provides the
// spawn/offline-safe anchor; oxygen/power requirements are added by the later
// life-support pass.

using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Power;

namespace VoxelEngine.Building
{
    public class Cryobed : MonoBehaviour
    {
        public string displayName = "Cryobed";
        public bool oxygenRequired = true;
        public bool poweredRequired = true;
        public float idleWatts = 35f;

        public bool IsPowered
        {
            get
            {
                if (!poweredRequired) return true;
                var consumer = GetComponent<PowerConsumer>();
                return consumer == null || consumer.IsPowered;
            }
        }

        public bool HasOxygenEnvironment => true; // room/vent oxygen arrives in the later pressure pass
        public bool IsAvailableForRespawn => IsPowered && HasOxygenEnvironment;

        public Vector3 SpawnPoint
        {
            get
            {
                Vector3 up = GravityProvider.GetUp(transform.position);
                if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
                return transform.position + up.normalized * 1.35f;
            }
        }

        public string PowerEstimateText => poweredRequired
            ? IsPowered ? $"Connected · draws {idleWatts:0} W" : $"Needs {idleWatts:0} W"
            : "Power optional";
        public string OxygenEstimateText => oxygenRequired
            ? "Room oxygen checks pending pressure-system pass"
            : "Oxygen optional";

        public string AvailabilityText => IsAvailableForRespawn
            ? "ONLINE"
            : !IsPowered ? "NO POWER" : "NO OXYGEN";

        public void ClaimAsSpawn()
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session == null) return;
            session.bedSpawnPoint = SpawnPoint;
            session.hasBedSpawn = true;
            session.SaveSpawnSidecar();
            VoxelEngine.UI.BuildFeedbackHud.Show("Cryobed Linked", "Respawn/offline safety point updated", null, new Color(0.45f, 0.85f, 1f));
            Debug.Log($"[Cryobed] Spawn point set to {session.bedSpawnPoint}");
        }
    }
}
