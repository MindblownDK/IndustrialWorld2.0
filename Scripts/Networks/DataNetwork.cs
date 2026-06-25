// Assets/Scripts/VoxelEngine/Networks/DataNetwork.cs
//
// Data cable network. Connects storage terminals, importers, exporters to server racks.
// No resource balancing — just connectivity. Any anchor on the network can see all others.

namespace VoxelEngine.Networks
{
    public class DataNetworkNew : ResourceNetwork<int>
    {
        public DataNetworkNew() : base(NetworkType.Data) { }

        public override bool CanAccept(ConnectionAnchor a) => a.networkType == NetworkType.Data;

        public override void Tick(float dt)
        {
            // Data networks don't balance resources — they just provide connectivity.
            // StorageTerminals, Importers, Exporters check if they share a DataNetwork
            // with a ServerRack to determine connectivity.
        }

        /// <summary>Find a ServerRack on this network (for storage access).</summary>
        public Storage.ServerRack FindServerRack()
        {
            foreach (var a in anchors)
            {
                if (a == null || a.owner == null) continue;
                var rack = a.owner.GetComponent<Storage.ServerRack>();
                if (rack != null && rack.IsOnline) return rack;
            }
            return null;
        }
    }
}
