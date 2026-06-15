// Assets/Scripts/VoxelEngine/Maritime/GridWaterwheel.cs
//
// Waterwheel (3×3×1) — dual-mode mechanical block:
//
//   STATIONARY (in moving water):
//     Generates torque from the water current flow → acts as a torque SOURCE.
//     The MaritimePropulsionJob derives torque from WaterFlowVelocity.
//
//   ON A SHIP (powered by a shaft):
//     If connected to a shaft delivering RPM, the wheel acts as a paddle —
//     producing thrust from RPM × submergence (handled in BuoyancyJob).
//
// The node's role is determined at graph-rebuild time: if the wheel is the
// only source in its chain (no engine upstream), it becomes the source and
// extracts torque from flow. If an engine feeds it, it becomes a consumer
// and generates paddle thrust.

using UnityEngine;

namespace VoxelEngine.Maritime
{
    public class GridWaterwheel : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Waterwheel;

        [Header("Waterwheel")]
        [Tooltip("Size multiplier — a 3×3×1 wheel has size 3.")]
        public float wheelSize = 3f;
        [Tooltip("Max RPM when spun by a strong current.")]
        public float maxRPM = 120f;
        [Tooltip("Torque per m/s of water flow (set on the node, used by the propagation job).")]
        public float flowTorqueCoefficient = 12000f;

        /// <summary>Current RPM (written back by ApplyResults).</summary>
        public float CurrentRPM { get; private set; }
        /// <summary>Current submergence 0..1 (written back by ApplyResults).</summary>
        public float Submergence { get; private set; }

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Waterwheel";
        }

        public override void PopulateMaritimeNode(ref MechanicalNode node)
        {
            node.MaxRPM = maxRPM;
            node.MaxTorque = flowTorqueCoefficient * 3f; // peak torque at ~3 m/s flow
            node.PropellerSize = wheelSize;
            node.GearRatio = 1f;
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            // A waterwheel-as-source is always "fuelled" by the water flow itself.
            // A waterwheel-as-consumer needs shaft RPM (throttle gating is irrelevant).
            // In both cases FuelAvailable01 = 1 so the propagation job runs it.
            node.FuelAvailable01 = Enabled ? 1f : 0f;

            if (!Enabled)
                node.SetFlag(MechanicalFlags.Broken);
            else
                node.ClearFlag(MechanicalFlags.Broken);
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            CurrentRPM = node.CurrentRPM;
            Submergence = node.Submergence;
        }
    }
}
