using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    public abstract class CompactVoltageStation : VoltageStationBase
    {
        private PowerNode _powerNode;
        public override float TotalProduced => _powerNode != null && _powerNode.network != null ? _powerNode.network.producedThisTick : 0f;
        public override float TotalConsumed => _powerNode != null && _powerNode.network != null ? _powerNode.network.consumedThisTick : 0f;
        public override float MaxCapacity => _powerNode != null && _powerNode.network != null ? _powerNode.network.bottleneckWatts : 0f;

        protected override void Awake()
        {
            base.Awake();
            _powerNode = GetComponent<PowerNode>();
            if (_powerNode == null) _powerNode = gameObject.AddComponent<PowerCable>();
            connectionPointOffset = new Vector3(0f, 0.55f, 0f);
            wireWidth = isHighVoltage ? 0.05f : 0.03f;
        }
    }
}
