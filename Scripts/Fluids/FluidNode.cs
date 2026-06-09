// Assets/Scripts/VoxelEngine/Fluids/FluidNode.cs
//
// Base for anything that participates in the fluid network: tanks, pumps, and pipes.
// Auto-registers/unregisters with FluidNetworkManager.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Fluids
{
    public enum FluidNodeKind { Pipe, Tank, Pump }

    public abstract class FluidNode : MonoBehaviour
    {
        public abstract FluidNodeKind Kind { get; }

        [Tooltip("Distance at which this node will auto-connect to neighbouring nodes/pipes.")]
        public float connectRadius = 3.0f;

        [System.NonSerialized] public FluidNetwork network;
        [System.NonSerialized] public List<FluidNode> neighbours = new();

        protected virtual void OnEnable()  { FluidNetworkManager.EnsureInstance(); FluidNetworkManager.Instance?.Register(this); }
        protected virtual void OnDisable() { FluidNetworkManager.Instance?.Unregister(this); }
    }
}
