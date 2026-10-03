// Assets/Scripts/VoxelEngine/Storage/WirelessTransmitter.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║                 WIRELESS TRANSMITTER (14.40.0)                   ║
// ║  A pure RELAY. It has no terminal view of its own - a player    ║
// ║  carrying a handheld Wireless Terminal within playerRange of    ║
// ║  an online transmitter can open the storage network remotely.   ║
// ║  Connects to the system by Data Pipe or touch; draws its 10 W   ║
// ║  from the system power budget like every other device.          ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class WirelessTransmitter : MonoBehaviour
    {
        [Header("Wireless")]
        [Tooltip("Display name for this transmitter (player can rename).")]
        public string transmitterName = "Wireless Network";

        [Tooltip("How far from this transmitter a handheld Wireless Terminal works.")]
        public float playerRange = 60f;

        public ServerRack ConnectedRack { get; private set; }
        public bool IsOnline { get; private set; }

        private float _timer;

        private void Awake()
        {
            // Legacy prefabs carried a PowerConsumer (own grid draw). The relay
            // is system-powered now - neutralize it instead of destroying the
            // component under the prefab's feet.
            var legacyPower = GetComponent<VoxelEngine.Power.PowerConsumer>();
            if (legacyPower != null) legacyPower.wattsPerSecond = 0f;
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < 1f) return;
            _timer = 0;

            // Piped or touching - network membership, never radius. Online
            // follows the controller: no controller or power-short = dark.
            ConnectedRack = StorageNetwork.ControllerOf(this);
            IsOnline = ConnectedRack != null && ConnectedRack.IsOnline;
        }

        /// <summary>True when the given world position is inside this
        /// transmitter's handheld range.</summary>
        public bool InPlayerRange(Vector3 worldPos)
            => (worldPos - transform.position).sqrMagnitude <= playerRange * playerRange;

        /// <summary>Get all online wireless transmitters in the world.</summary>
        public static WirelessTransmitter[] GetAllOnline()
        {
            var all = FindObjectsByType<WirelessTransmitter>(FindObjectsInactive.Exclude);
            var online = new System.Collections.Generic.List<WirelessTransmitter>();
            foreach (var t in all) if (t.IsOnline && t.ConnectedRack != null) online.Add(t);
            return online.ToArray();
        }
    }
}
