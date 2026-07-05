// Assets/Scripts/VoxelEngine/Storage/StorageTerminal.cs
//
// The player interacts with this to access the storage network.
// Shows all items across all connected ServerRacks, with search,
// insert/extract, and crafting grid.

using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class StorageTerminal : MonoBehaviour
    {
        [Header("Connection")]
        [Tooltip("Max distance to find a ServerRack.")]
        public float searchRadius = 10f;
        [Tooltip("If true, this is a wireless terminal (longer range).")]
        public bool isWireless;
        [Tooltip("Wireless range.")]
        public float wirelessRange = 50f;

        /// <summary>The connected server rack (cached).</summary>
        public ServerRack ConnectedRack { get; private set; }

        private float _searchTimer;

        private void Update()
        {
            _searchTimer += Time.deltaTime;
            if (_searchTimer < 2f) return;
            _searchTimer = 0;
            FindRack();
        }

        private void FindRack()
        {
            float range = isWireless ? wirelessRange : searchRadius;
            var racks = FindObjectsByType<ServerRack>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            ServerRack best = null;
            float bestDist = range * range;
            foreach (var r in racks)
            {
                if (!r.IsOnline) continue;
                float d = (r.transform.position - transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = r; }
            }
            ConnectedRack = best;
        }
    }
}
