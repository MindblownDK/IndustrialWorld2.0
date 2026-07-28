// Assets/Scripts/VoxelEngine/Building/Cryobed.cs
//
// Static cryobed foundation for offline survival. Now checks for piped/room oxygen
// via nearby biofarms, O₂ tanks, and grid cryobeds — completing 11.4 room-pressure pass.

using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Power;
using VoxelEngine.GridSystem;
using VoxelEngine.Fluids;
using VoxelEngine.Gas;

namespace VoxelEngine.Building
{
    public class Cryobed : MonoBehaviour
    {
        public string displayName = "Cryobed";
        public bool oxygenRequired = true;
        public bool poweredRequired = true;
        public float idleWatts = 35f;
        public bool claimedByLocalPlayer;

        public bool IsPowered
        {
            get
            {
                if (!poweredRequired) return true;
                var consumer = GetComponent<PowerConsumer>();
                return consumer == null || consumer.IsPowered;
            }
        }

        // Room oxygen: powered biofarm producing nearby, O₂ tank with O₂, or grid cryobed with O₂
        public bool HasOxygenEnvironment
        {
            get
            {
                if (!oxygenRequired) return true;
                return IsOxygenRichAt(transform.position);
            }
        }

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

        public string OxygenEstimateText
        {
            get
            {
                if (!oxygenRequired) return "Oxygen optional";
                return HasOxygenEnvironment
                    ? "Room O₂ OK · biofarm / tank / cryobed nearby"
                    : "No room O₂ · needs piped biofarm / O₂ tank within 8m";
            }
        }

        public string AvailabilityText => IsAvailableForRespawn
            ? "ONLINE"
            : !IsPowered ? "NO POWER" : "NO OXYGEN";

        public void ClaimAsSpawn()
        {
            var session = Menu.WorldSession.Instance;
            if (session == null) return;
            claimedByLocalPlayer = true;
            session.bedSpawnPoint = SpawnPoint;
            session.hasBedSpawn = true;
            session.SaveSpawnSidecar();
            VoxelEngine.UI.BuildFeedbackHud.Show("Cryobed Linked", "Respawn/offline safety point updated", null, new Color(0.45f, 0.85f, 1f));
            Debug.Log($"[Cryobed] Spawn point set to {session.bedSpawnPoint}");
        }

        // Shared oxygen-rich check used by cryobed and offline service
        public static bool IsOxygenRichAt(Vector3 pos)
        {
            const float biofarmRadius = 10f;
            const float tankRadius = 7f;
            const float cryobedRadius = 6f;

            // Static biofarms producing
            foreach (var bf in Object.FindObjectsByType<Biofarm>(FindObjectsInactive.Exclude))
            {
                if (bf == null || !bf.IsRunning) continue;
                if ((bf.transform.position - pos).sqrMagnitude <= biofarmRadius * biofarmRadius)
                    return true;
            }
            // Grid biofarms producing
            foreach (var bf in Object.FindObjectsByType<GridBiofarm>(FindObjectsInactive.Exclude))
            {
                if (bf == null || !bf.IsProducing) continue;
                if ((bf.transform.position - pos).sqrMagnitude <= biofarmRadius * biofarmRadius)
                    return true;
            }
            // Static gas tanks with O₂
            foreach (var tank in Object.FindObjectsByType<GasTank>(FindObjectsInactive.Exclude))
            {
                if (tank == null || tank.storedGasType != GasType.Oxygen || tank.storedAmount < 5f) continue;
                if ((tank.transform.position - pos).sqrMagnitude <= tankRadius * tankRadius)
                    return true;
            }
            // Grid gas tanks with O₂
            foreach (var tank in Object.FindObjectsByType<GridGasTank>(FindObjectsInactive.Exclude))
            {
                if (tank == null || tank.gasType != GasType.Oxygen || tank.stored < 5f) continue;
                if ((tank.transform.position - pos).sqrMagnitude <= tankRadius * tankRadius)
                    return true;
            }
            // Grid cryobeds with O₂ (even if not claimed)
            foreach (var cryo in Object.FindObjectsByType<GridCryobed>(FindObjectsInactive.Exclude))
            {
                if (cryo == null || !cryo.HasOxygenEnvironment || !cryo.IsPowered) continue;
                if ((cryo.transform.position - pos).sqrMagnitude <= cryobedRadius * cryobedRadius)
                    return true;
            }
            return false;
        }
    }
}
