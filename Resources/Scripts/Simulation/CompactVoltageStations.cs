using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    // Compatibility MonoBehaviour for old prefabs that briefly referenced this
    // multi-class script before station classes were split into matching files.
    public sealed class CompactVoltageStations : MonoBehaviour { }

    public abstract class CompactVoltageStation : VoltageStationBase
    {
        private PowerNode _powerNode;
        public override float TotalProduced => _powerNode != null && _powerNode.network != null ? _powerNode.network.producedThisTick : 0f;
        public override float TotalConsumed => _powerNode != null && _powerNode.network != null ? _powerNode.network.consumedThisTick : 0f;
        public override float MaxCapacity => isHighVoltage ? float.PositiveInfinity : 100000f;

        protected override void Awake()
        {
            base.Awake();

            // Compact relays/connectors should not use PowerCable directly because
            // PowerCable owns the chunky energy-pipe visual and hides authored
            // child meshes. Use a plain cable-kind node instead so the compact
            // wall/foundation device keeps its own generated model.
            var oldCable = GetComponent<PowerCable>();
            if (oldCable != null) Destroy(oldCable);

            var compactNode = GetComponent<CompactPowerNode>();
            if (compactNode == null) compactNode = gameObject.AddComponent<CompactPowerNode>();
            compactNode.maxAutoConnections = maxConnections;
            compactNode.connectRadius = Mathf.Max(3f, wireReach * 0.15f);
            compactNode.requireGridAlignedNeighbours = false;
            _powerNode = compactNode;

            connectionPointOffset = new Vector3(0f, 0.55f, 0f);
            wireWidth = isHighVoltage ? 0.05f : 0.03f;
        }
    }
}
