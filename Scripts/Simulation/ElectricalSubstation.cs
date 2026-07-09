using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    public class ElectricalSubstation : MonoBehaviour, IVoltageStation
    {
        [Header("Substation Configuration")]
        public float relayDistance = 150f;
        public float maxThroughputWatts = 50000f;
        public float structureHeight = 5f;

        private PowerPole _inputPole;
        private PowerPole _outputPole;
        private PowerCable _powerNode;

        // IVoltageStation implementation
        public Vector3 ConnectionPoint => transform.position + Vector3.up * structureHeight;
        public Transform StationTransform => transform;
        public bool CanConnectMore => true;
        public bool IsHighVoltage => false; // User requested LV for substation

        public float TotalProduced => _powerNode != null && _powerNode.network != null ? _powerNode.network.producedThisTick : 0f;
        public float TotalConsumed => _powerNode != null && _powerNode.network != null ? _powerNode.network.consumedThisTick : 0f;
        public float MaxCapacity => _powerNode != null && _powerNode.network != null ? _powerNode.network.bottleneckWatts : 0f;
        public float CurrentPower => TotalProduced;

        private void Awake()
        {
            CreateInternalPoles();
        }

        private void CreateInternalPoles()
        {
            var inputGo = new GameObject("SubstationInput");
            inputGo.transform.SetParent(transform, false);
            inputGo.transform.localPosition = Vector3.left * 1.5f + Vector3.up * structureHeight;
            _inputPole = inputGo.AddComponent<PowerPole>();
            _inputPole.maxConnections = 2;
            _inputPole.wireReach = relayDistance;

            var outputGo = new GameObject("SubstationOutput");
            outputGo.transform.SetParent(transform, false);
            outputGo.transform.localPosition = Vector3.right * 1.5f + Vector3.up * structureHeight;
            _outputPole = outputGo.AddComponent<PowerPole>();
            _outputPole.maxConnections = 2;
            _outputPole.wireReach = relayDistance;

            _powerNode = gameObject.AddComponent<PowerCable>();
            _powerNode.connectRadius = 3f;
        }

        public bool ConnectInput(PowerPole sourcePole) => _inputPole.TryConnect(sourcePole);
        public bool ConnectOutput(PowerPole destPole) => _outputPole.TryConnect(destPole);

        public void AddConnection(IVoltageStation other) { /* Handled by internal poles */ }
        public void RemoveConnection(IVoltageStation other) { /* Handled by internal poles */ }
    }
}
