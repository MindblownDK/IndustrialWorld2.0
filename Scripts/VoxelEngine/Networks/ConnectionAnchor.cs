// Assets/Scripts/VoxelEngine/Networks/ConnectionAnchor.cs
//
// Placed on machine/pipe ports. Defines what network type it accepts,
// its tier, and holds runtime state (volume, power, etc).
// The WrenchTool connects/disconnects these.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Networks
{
    public class ConnectionAnchor : MonoBehaviour
    {
        [Header("Network Config")]
        public NetworkType networkType = NetworkType.Power;
        public PowerTier powerTier = PowerTier.Low;

        [Header("Fluid/Gas (if applicable)")]
        public float fluidCapacity = 100f; // litres this anchor can buffer
        public FluidType allowedFluid; // null = accepts any fluid

        [Header("Visual")]
        [Tooltip("If true, this pipe shows fluid inside (glass pipe).")]
        public bool isGlass;

        // ── Runtime State (read/written by the network) ──────────
        [System.NonSerialized] public object network; // ResourceNetwork<float> or ResourceNetwork<int>
        [System.NonSerialized] public List<ConnectionAnchor> connections = new();

        // Power state.
        [System.NonSerialized] public float powerOutput; // watts generated (set by machine)
        [System.NonSerialized] public float powerDraw;   // watts consumed (set by machine)
        [System.NonSerialized] public bool isPowered;

        // Fluid/Gas state.
        [System.NonSerialized] public float fluidVolume;
        [System.NonSerialized] public FluidType currentFluid;

        // Data state.
        [System.NonSerialized] public ResourceNetwork<int> dataNetwork;

        /// <summary>The GameObject this anchor is attached to (for finding machines).</summary>
        public GameObject owner => gameObject;

        private void OnEnable()
        {
            SimulationManager.EnsureInstance(); SimulationManager.Instance.RegisterAnchor(this);
        }

        private void OnDisable()
        {
            // Disconnect from all connections.
            foreach (var c in connections)
                c?.connections.Remove(this);
            connections.Clear();
            SimulationManager.Instance?.UnregisterAnchor(this);
        }

        /// <summary>Connect this anchor to another. Creates a network edge.</summary>
        public bool TryConnect(ConnectionAnchor other)
        {
            if (other == null || other == this) return false;
            if (other.networkType != networkType) return false;
            if (connections.Contains(other)) return false;

            connections.Add(other);
            other.connections.Add(this);

            // Tell the simulation manager to rebuild networks.
            SimulationManager.Instance?.SetDirty();
            return true;
        }

        /// <summary>Disconnect from another anchor.</summary>
        public void Disconnect(ConnectionAnchor other)
        {
            if (other == null) return;
            connections.Remove(other);
            other.connections.Remove(this);
            SimulationManager.Instance?.SetDirty();
        }

        /// <summary>Disconnect from ALL connections.</summary>
        public void DisconnectAll()
        {
            foreach (var c in connections)
                c?.connections.Remove(this);
            connections.Clear();
            SimulationManager.Instance?.SetDirty();
        }
    }
}
