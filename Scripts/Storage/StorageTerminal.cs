// Assets/Scripts/VoxelEngine/Storage/StorageTerminal.cs
//
// The player interacts with this to access the storage network.
// 14.40.0: connectivity is NETWORK membership - the terminal must be
// piped (Data Pipe) or physically touching the system; radius search is
// gone. The handheld Wireless Terminal uses a hidden proxy instance with
// an explicitly bound controller (OverrideRack) instead.

using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class StorageTerminal : MonoBehaviour
    {
        [Tooltip("True on the hidden handheld-wireless proxy only.")]
        public bool isWireless;

        // 14.40.0: legacy radius fields kept for prefab/setup compatibility -
        // connectivity is network membership (Data Pipes / touching) now.
        [HideInInspector] public float searchRadius = 10f;
        [HideInInspector] public float wirelessRange = 60f;

        /// <summary>Explicit controller binding used by the handheld wireless
        /// proxy - set by the UI, bypasses network resolution.</summary>
        public ServerRack OverrideRack { get; set; }

        /// <summary>The connected Server Controller (network-resolved).</summary>
        public ServerRack ConnectedRack
        {
            get
            {
                if (OverrideRack != null) return OverrideRack;
                if (Time.time - _resolveTime > 1f)
                {
                    _resolved = StorageNetwork.ControllerOf(this);
                    _resolveTime = Time.time;
                }
                return _resolved;
            }
        }

        private ServerRack _resolved;
        private float _resolveTime = -999f;
    }
}
