using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    public class StepUpTransformer : VoltageStationBase
    {
        [Header("Transformer Configuration")]
        public float maxThroughputWatts = 200_000_000f;
        public float conversionLoss = 0.02f;

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
            
            // Internal nodes
            var hvGo = new GameObject("HV_Node");
            hvGo.transform.SetParent(transform, false);
            _hvNode = hvGo.AddComponent<PowerCable>();
            
            var lvGo = new GameObject("LV_Node");
            lvGo.transform.SetParent(transform, false);
            _lvNode = lvGo.AddComponent<PowerCable>();
        }
    }
}
