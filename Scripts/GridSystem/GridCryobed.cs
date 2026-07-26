// Assets/Scripts/VoxelEngine/GridSystem/GridCryobed.cs
//
// Grid-mounted cryobed foundation. Provides spawn/offline-survival anchor now;
// later oxygen/power/room-pressure systems can read the same block.

using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.GridSystem
{
    public class GridCryobed : GridBlock, IGridDataProvider
    {
        [Header("Cryobed")]
        public bool oxygenRequired = true;
        public bool poweredRequired = true;
        public float idleWatts = 35f;
        public bool claimedByLocalPlayer;
        public float oxygenCapacity = 120f;
        public float oxygenStored;
        public float offlineOxygenPerHour = 12f;

        public override float PowerDraw => Enabled && poweredRequired ? idleWatts : 0f;
        public bool IsPowered => !poweredRequired || Grid == null || Grid.HasPower;
        public bool HasOxygenEnvironment => !oxygenRequired || oxygenStored > 0.01f;
        public bool IsAvailableForRespawn => Enabled && IsPowered && HasOxygenEnvironment;
        public string AvailabilityText => IsAvailableForRespawn
            ? "ONLINE"
            : !Enabled ? "OFFLINE"
            : !IsPowered ? "NO POWER"
            : "NO OXYGEN";

        public Vector3 SpawnPoint
        {
            get
            {
                Vector3 up = GravityProvider.GetUp(transform.position);
                if (up.sqrMagnitude < 0.0001f) up = Grid != null ? Grid.transform.up : Vector3.up;
                return transform.position + up.normalized * 1.35f;
            }
        }

        public string PowerEstimateText
        {
            get
            {
                if (!poweredRequired) return "Power optional";
                float storedWh = 0f;
                if (Grid != null)
                    foreach (var block in Grid.AllBlocks)
                        if (block is GridBattery battery && battery.Enabled) storedWh += Mathf.Max(0f, battery.storedWh);
                if (idleWatts <= 0.01f) return IsPowered ? "Connected" : "No power";
                return storedWh > 0.01f
                    ? $"{storedWh:0} Wh stored · ~{storedWh / idleWatts:0.0} h at {idleWatts:0} W"
                    : IsPowered ? $"Grid powered · {idleWatts:0} W draw" : $"Needs {idleWatts:0} W";
            }
        }

        public string OxygenEstimateText
        {
            get
            {
                if (!oxygenRequired) return "Oxygen optional";
                return oxygenStored > 0.01f
                    ? $"{oxygenStored:0}/{oxygenCapacity:0} O₂ · ~{oxygenStored / Mathf.Max(0.01f, offlineOxygenPerHour):0.0} h reserve"
                    : "No piped oxygen in cryobed buffer";
            }
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block") blockName = "Grid Cryobed";
        }

        public float AddOxygen(float amount)
        {
            if (amount <= 0f) return 0f;
            float space = Mathf.Max(0f, oxygenCapacity - oxygenStored);
            float take = Mathf.Min(space, amount);
            oxygenStored += take;
            return take;
        }

        public void ClaimAsSpawn()
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session == null) return;
            claimedByLocalPlayer = true;
            session.bedSpawnPoint = SpawnPoint;
            session.hasBedSpawn = true;
            session.SaveSpawnSidecar();
            VoxelEngine.UI.BuildFeedbackHud.Show("Cryobed Linked", "Respawn/offline safety point updated", null, new Color(0.45f, 0.85f, 1f));
            Debug.Log($"[GridCryobed] Spawn point set to {session.bedSpawnPoint}");
        }

        public string SourceName => blockName;
        public string DataCategory => "Life Support";
        public string GetDisplayData()
        {
            return $"CRYOBED\n{AvailabilityText}\n{PowerEstimateText}\n{OxygenEstimateText}";
        }
    }
}
