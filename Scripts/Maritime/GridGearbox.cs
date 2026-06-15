// Assets/Scripts/VoxelEngine/Maritime/GridGearbox.cs
//
// Gearbox — transmits rotational power in ALL directions while trading
// torque for RPM. The higher the gear ratio, the faster the output spins
// but the less torque it delivers (power ≈ conserved). Speed is hard-clamped
// to prevent runaway gearing.
//
//   output_rpm  = input_rpm  × GearRatio   (clamped to MaxOutputSpeed)
//   output_torque = input_torque ÷ GearRatio
//
// The actual math happens in MechanicalPropagationJob; this block just
// feeds GearRatio + MaxGearSpeed into the node at rebuild time.

using UnityEngine;

namespace VoxelEngine.Maritime
{
    public class GridGearbox : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Gearbox;

        [Header("Gearbox")]
        [Tooltip("Speed multiplier. >1 = faster but less torque. <1 = more torque but slower.")]
        [Range(0.1f, 10f)]
        public float gearRatio = 2f;

        [Tooltip("Absolute RPM clamp on the output. The more speed, the less torque.")]
        public float maxOutputSpeed = 2000f;

        [Tooltip("Current selected gear (1-based). Adjusts gearRatio in steps.")]
        [Range(1, 6)]
        public int selectedGear = 2;

        /// <summary>Current RPM passing through (written back by ApplyResults).</summary>
        public float CurrentRPM { get; private set; }

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Gearbox";
            ApplyGearSelection();
        }

        /// <summary>Recalculate the effective gear ratio from the selected gear step.</summary>
        private void ApplyGearSelection()
        {
            // 6 gears: 0.5×, 0.75×, 1×, 1.5×, 2.5×, 4×
            float[] ratios = { 0.5f, 0.75f, 1f, 1.5f, 2.5f, 4f };
            int idx = Mathf.Clamp(selectedGear - 1, 0, ratios.Length - 1);
            gearRatio = ratios[idx];
        }

        public override void PopulateMaritimeNode(ref MechanicalNode node)
        {
            ApplyGearSelection();
            node.GearRatio = gearRatio;
            node.MaxGearSpeed = maxOutputSpeed;
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            if (!Enabled)
                node.SetFlag(MechanicalFlags.Broken);
            else
                node.ClearFlag(MechanicalFlags.Broken);

            // Keep the node in sync if the designer changed the ratio at runtime.
            node.GearRatio = gearRatio;
            node.MaxGearSpeed = maxOutputSpeed;
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            CurrentRPM = node.CurrentRPM;
        }
    }
}
