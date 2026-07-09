using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    public class StepDownTransformer : VoltageStationBase
    {
        public new float maxThroughputWatts = 200000000f;
        public new float conversionLoss = 0.02f;

        public override float TotalProduced => hvNetwork != null ? hvNetwork.producedThisTick : 0f;
        public override float TotalConsumed => hvNetwork != null ? hvNetwork.consumedThisTick : 0f;
        public override float MaxCapacity => maxThroughputWatts;

        private PowerNetwork hvNetwork => _hvNode != null ? _hvNode.network : null;
        private PowerNode _hvNode;
        private PowerNode _lvNode;

        protected override void Awake()
        {
            base.Awake();
            isHighVoltage = true;
            connectionPointOffset = new Vector3(0, 3f, 0);
            var hvGo = new GameObject("HV_Node");
            hvGo.transform.SetParent(transform, false);
            _hvNode = hvGo.AddComponent<PowerCable>();
            var lvGo = new GameObject("LV_Node");
            lvGo.transform.SetParent(transform, false);
            _lvNode = lvGo.AddComponent<PowerCable>();
        }
    }
}
