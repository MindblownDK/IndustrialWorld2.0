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

        public override float PowerDraw => Enabled && poweredRequired ? idleWatts : 0f;
        public bool IsPowered => !poweredRequired || Grid == null || Grid.HasPower;
        public bool HasOxygenEnvironment => !oxygenRequired || (Grid != null && Grid.OxygenStored > 0.01f);
        public bool IsAvailableForRespawn => Enabled && IsPowered && HasOxygenEnvironment;
        public string AvailabilityText => IsAvailableForRespawn
            ? "ONLINE"
            : !Enabled ? "OFFLINE"
            : !IsPowered ? "NO POWER"
            : "NO OXYGEN";

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block") blockName = "Grid Cryobed";
        }

        public void ClaimAsSpawn()
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session == null) return;
            Vector3 up = GravityProvider.GetUp(transform.position);
            if (up.sqrMagnitude < 0.0001f) up = Grid != null ? Grid.transform.up : Vector3.up;
            session.bedSpawnPoint = transform.position + up.normalized * 1.35f;
            session.hasBedSpawn = true;
            session.SaveSpawnSidecar();
            VoxelEngine.UI.BuildFeedbackHud.Show("Cryobed Linked", "Respawn/offline safety point updated", null, new Color(0.45f, 0.85f, 1f));
            Debug.Log($"[GridCryobed] Spawn point set to {session.bedSpawnPoint}");
        }

        public string SourceName => blockName;
        public string DataCategory => "Life Support";
        public string GetDisplayData()
        {
            string power = poweredRequired ? $"POWER {idleWatts:0} W" : "POWER OPTIONAL";
            string oxygen = oxygenRequired ? "OXYGEN REQUIRED" : "OXYGEN OPTIONAL";
            return $"CRYOBED\n{AvailabilityText}\n{power}\n{oxygen}";
        }
    }
}
