// Assets/Scripts/VoxelEngine/Networks/ResourceNetwork.cs
//
// Abstract base for all network types. Pure C# — no MonoBehaviour.
// Holds a graph of ConnectionAnchors and performs resource balancing.

using System.Collections.Generic;

namespace VoxelEngine.Networks
{
    /// <summary>
    /// A connected component of anchors sharing a resource.
    /// T = the resource payload type (float for power/fluid, or a struct).
    /// </summary>
    public abstract class ResourceNetwork<T>
    {
        public readonly int id;
        public readonly NetworkType type;
        public readonly List<ConnectionAnchor> anchors = new();
        public bool isDirty; // needs rebalance

        private static int _nextId;

        protected ResourceNetwork(NetworkType type)
        {
            this.type = type;
            id = _nextId++;
        }

        /// <summary>Add an anchor to this network.</summary>
        public void AddAnchor(ConnectionAnchor a)
        {
            if (!anchors.Contains(a)) { anchors.Add(a); isDirty = true; }
        }

        /// <summary>Remove an anchor. Returns true if the network is now empty.</summary>
        public bool RemoveAnchor(ConnectionAnchor a)
        {
            anchors.Remove(a);
            isDirty = true;
            return anchors.Count == 0;
        }

        /// <summary>Called by SimulationManager each tick. Override to balance resources.</summary>
        public abstract void Tick(float dt);

        /// <summary>Check if an anchor can join this network (tier/type compatibility).</summary>
        public abstract bool CanAccept(ConnectionAnchor a);
    }
}
