// Assets/Scripts/VoxelEngine/Storage/WirelessTransmitter.cs
//
// When the player researches "Wireless Terminal" and crafts this block,
// they can access the storage network from their inventory screen
// anywhere in the world. The transmitter must be placed and powered.
// Player selects which transmitter to use if they have multiple networks.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Power;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PowerConsumer))]
    public class WirelessTransmitter : MonoBehaviour
    {
        [Header("Wireless")]
        [Tooltip("Display name for this transmitter (player can rename).")]
        public string transmitterName = "Wireless Network";

        public ServerRack ConnectedRack { get; private set; }
        public bool IsOnline { get; private set; }

        private PowerConsumer _power;
        private float _timer;

        private void Awake() { _power = GetComponent<PowerConsumer>(); }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < 2f) return;
            _timer = 0;

            IsOnline = _power == null || _power.IsPowered;
            if (!IsOnline) { ConnectedRack = null; return; }

            // Find connected ServerRack within range.
            var racks = FindObjectsByType<ServerRack>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            ServerRack best = null; float bestD = 400f; // 20m range
            foreach (var r in racks)
            {
                if (!r.IsOnline) continue;
                float d = (r.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = r; }
            }
            ConnectedRack = best;
        }

        /// <summary>Get all online wireless transmitters in the world.</summary>
        public static WirelessTransmitter[] GetAllOnline()
        {
            var all = FindObjectsByType<WirelessTransmitter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var online = new System.Collections.Generic.List<WirelessTransmitter>();
            foreach (var t in all) if (t.IsOnline && t.ConnectedRack != null) online.Add(t);
            return online.ToArray();
        }
    }
}
