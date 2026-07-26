using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// A power pole that distributes electricity through low-voltage wire connections.
    /// Inherits from VoltageStationBase to participate in the unified grid UI and manual wiring.
    /// </summary>
    public class PowerPole : VoltageStationBase
    {
        [Header("Pole Configuration")]
        public float poleHeight = 3f;

        private PowerNode _powerNode;

        // IVoltageStation implementation (via VoltageStationBase)
        public override float TotalProduced => _powerNode != null && _powerNode.network != null ? _powerNode.network.producedThisTick : 0f;
        public override float TotalConsumed => _powerNode != null && _powerNode.network != null ? _powerNode.network.consumedThisTick : 0f;
        public override float MaxCapacity => 100000f;

        protected override void Awake()
        {
            base.Awake();
            _powerNode = GetComponent<PowerNode>();
            if (_powerNode == null) _powerNode = gameObject.AddComponent<PowerCable>();
            
            isHighVoltage = false;
            connectionPointOffset = new Vector3(0, poleHeight, 0);
            wireReach = 15f;
            maxConnections = 6;
            
            // Set visual properties for LV wires
            wireWidth = 0.03f;
        }

        public bool TryConnect(PowerPole target)
        {
            if (target == null || target == this) return false;
            if (!CanConnectMore || !target.CanConnectMore) return false;

            float dist = Vector3.Distance(transform.position, target.transform.position);
            if (dist > wireReach) return false;

            AddConnection(target);
            target.AddConnection(this);
            return true;
        }

        // The base class handles AddConnection/RemoveConnection and manualLinks.
        // It also handles UpdateWireVisuals (catenary curve).
    }
}
