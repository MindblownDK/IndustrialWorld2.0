using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    public class HighVoltageTransmissionTower : VoltageStationBase
    {
        public override float TotalProduced => network != null ? network.producedThisTick : 0f;
        public override float TotalConsumed => network != null ? network.consumedThisTick : 0f;
        public override float MaxCapacity => network != null ? network.bottleneckWatts : 0f;

        private PowerNetwork network => _powerNode != null ? _powerNode.network : null;
        private PowerNode _powerNode;

        protected override void Awake()
        {
            base.Awake();
            _powerNode = GetComponent<PowerNode>();
            if (_powerNode == null) _powerNode = gameObject.AddComponent<PowerCable>();
            
            isHighVoltage = true;
            connectionPointOffset = new Vector3(0, 15f, 0); // Towers are tall
            wireReach = 200f;
        }
    }
}
