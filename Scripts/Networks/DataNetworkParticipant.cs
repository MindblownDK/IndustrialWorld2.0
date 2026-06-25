// Assets/Scripts/VoxelEngine/Networks/DataNetworkParticipant.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          DATA NETWORK PARTICIPANT — auto-anchor helper          ║
// ║   Stick this on any storage block (ServerRack, StorageTerminal,  ║
// ║   StorageImporter / Exporter, NASBlock) and it spawns a Data-    ║
// ║   typed ConnectionAnchor next to its centre so DataCables can    ║
// ║   discover and connect to it without manual wrenching.           ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Networks
{
    [DisallowMultipleComponent]
    public class DataNetworkParticipant : MonoBehaviour
    {
        public ConnectionAnchor anchor;

        private void Awake()
        {
            anchor = GetComponent<ConnectionAnchor>();
            if (anchor == null) anchor = gameObject.AddComponent<ConnectionAnchor>();
            anchor.networkType = NetworkType.Data;
        }
    }
}
