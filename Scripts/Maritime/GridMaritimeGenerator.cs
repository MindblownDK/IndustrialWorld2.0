// Assets/Scripts/VoxelEngine/Maritime/GridMaritimeGenerator.cs
//
// Maritime Generator (2×2×2) — converts shaft torque into electricity.
// Attached to the END of a propulsion chain (after a gearbox for best
// efficiency: more speed = more torque at the generator).
//
// The MaritimePropagationJob computes:
//   ElectricityOutput = shaftTorque × shaftRPM × (2π/60) × generatorEfficiency
//
// This block reports that value via PowerOutput so GridEntity.UpdatePower()
// adds it to the grid-wide power pool.

using UnityEngine;

namespace VoxelEngine.Maritime
{
    public class GridMaritimeGenerator : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Generator;

        [Header("Generator")]
        [Tooltip("Max RPM this generator can accept.")]
        public float maxRPM = 1800f;
        [Tooltip("Max electrical output (W). Excess shaft power is clipped.")]
        public float maxWattOutput = 50000f;

        /// <summary>Live electricity output (W) — set by ApplyResults.</summary>
        public float GeneratedWatts { get; private set; }
        /// <summary>Current shaft RPM.</summary>
        public float CurrentRPM { get; private set; }

        public override float PowerOutput
        {
            get
            {
                if (!Enabled) return 0f;
                return Mathf.Min(GeneratedWatts, maxWattOutput);
            }
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Maritime Generator";
        }

        public override void PopulateMaritimeNode(ref MechanicalNode node)
        {
            node.MaxRPM = maxRPM;
            node.MaxTorque = 0f; // generator is a pure load sink
            node.GearRatio = 1f;
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            node.FuelAvailable01 = Enabled ? 1f : 0f;
            if (!Enabled)
                node.SetFlag(MechanicalFlags.Broken);
            else
                node.ClearFlag(MechanicalFlags.Broken);
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            GeneratedWatts = node.ElectricityOutput;
            CurrentRPM = node.CurrentRPM;
        }
    }
}
