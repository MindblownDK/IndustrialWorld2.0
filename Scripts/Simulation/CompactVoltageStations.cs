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
        private bool _overloadTriggered;

        /// <summary>True from the overload flash until this connector is destroyed.</summary>
        internal bool IsOverloading => _overloadTriggered;

        public override float TotalProduced => _powerNode != null && _powerNode.network != null ? _powerNode.network.producedThisTick : 0f;
        public override float TotalConsumed => _powerNode != null && _powerNode.network != null ? _powerNode.network.consumedThisTick : 0f;
        public override float MaxCapacity => isHighVoltage ? float.PositiveInfinity : 100000f;

        /// <summary>
        /// Compact connectors have one shared budget for automatic energy-pipe taps
        /// and player-drawn wires. A relay is the intentional multi-link component.
        /// Pending station links are counted too, so two quick wire clicks cannot
        /// slip past the topology rebuild delay.
        /// </summary>
        public override bool CanConnectMore
        {
            get
            {
                if (!base.CanConnectMore || _powerNode == null) return base.CanConnectMore;
                int linkCount = _powerNode.neighbours != null ? _powerNode.neighbours.Count : 0;
                for (int i = 0; i < _connectedStations.Count; i++)
                {
                    var pending = _connectedStations[i];
                    var pendingNode = pending != null && pending.StationTransform != null
                        ? pending.StationTransform.GetComponent<PowerNode>()
                        : null;
                    if (pendingNode != null && (_powerNode.neighbours == null
                        || !_powerNode.neighbours.Contains(pendingNode)))
                        linkCount++;
                }
                return linkCount < _powerNode.MaxAutoConnections;
            }
        }

        /// <summary>
        /// Trips this compact connector and starts the attached finite-capacity
        /// energy pipes burning. The flash is intentionally runtime-only; a new
        /// connector must be placed after the damaged parts are destroyed.
        /// </summary>
        internal bool TriggerOverload(float throughWatts, float capacityWatts,
                                    PowerCable firstCable, PowerCable secondCable)
        {
            if (_overloadTriggered || !isActiveAndEnabled) return false;
            _overloadTriggered = true;

            const float BurnSeconds = 2.0f;
            firstCable?.BeginOverloadBurn(BurnSeconds);
            if (secondCable != null && secondCable != firstCable)
                secondCable.BeginOverloadBurn(BurnSeconds);
            for (int i = 0; i < _connectedStations.Count; i++)
            {
                var other = _connectedStations[i];
                BeginManualWireOverload(other, BurnSeconds);
                if (other is VoltageStationBase otherBase)
                    otherBase.BeginManualWireOverload(this, BurnSeconds);
            }

            var flash = new GameObject("ConnectorOverloadFlash");
            flash.transform.position = ConnectionPoint;
            var light = flash.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.10f, 0.02f, 1f);
            light.intensity = 8f;
            light.range = 4f;
            Destroy(flash, 0.18f);

            Debug.LogWarning($"[Power] Connector overload: {throughWatts:0} W exceeds {capacityWatts:0} W. Connector and attached cable(s) are destroyed.", this);
            PowerNetworkManager.Instance?.SetDirty();
            StartCoroutine(DestroyAfterOverloadFlash());
            return true;
        }

        private System.Collections.IEnumerator DestroyAfterOverloadFlash()
        {
            yield return new WaitForSeconds(0.06f);
            Destroy(gameObject);
        }

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
