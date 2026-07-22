// Assets/Scripts/VoxelEngine/Maritime/GridGearbox.cs
//
// Gearbox — transmits rotational power in ALL directions while trading
// torque for RPM. The higher the gear ratio, the faster the output spins
// but the less torque it delivers (power ≈ conserved). Speed is hard-clamped
// to prevent runaway gearing.
//
//   output_rpm    = input_rpm × GearRatio   (clamped to MaxOutputSpeed)
//   output_torque = input_torque ÷ GearRatio
//
// The actual math happens per-node in MechanicalPropagationJob using the BFS
// parent map built at graph rebuild — power always flows engine → load, so
// EITHER face can be the physical input: the far side automatically becomes
// the output. No orientation wiring needed.
//
// v6.10.0-dev —
//   • 20 selectable gears (0.25× … 6.00×), applied LIVE from the UI.
//   • Gear changes no longer wait for a graph rebuild: RefreshMaritimeNode
//     re-derives the ratio from the selected gear every tick.
//   • Fixed Input RPM readout (was inverted — multiplied instead of divided).
//   • Legacy placed gearboxes (2000 RPM cap) auto-migrate to the 10000 RPM cap.

using UnityEngine;

namespace VoxelEngine.Maritime
{
    public class GridGearbox : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Gearbox;

        /// <summary>Number of selectable gears.</summary>
        public const int GearCount = 20;

        /// <summary>The 20 standard gear ratios (0.25× torque gears … 6.00× speed gears).
        /// Evenly blended so low gears favour heavy props and high gears feed generators.</summary>
        public static readonly float[] GearRatios =
        {
            0.25f, 0.33f, 0.40f, 0.50f, 0.60f, 0.70f, 0.80f, 0.90f, 1.00f, 1.10f,
            1.25f, 1.50f, 1.75f, 2.00f, 2.50f, 3.00f, 3.50f, 4.00f, 5.00f, 6.00f,
        };

        [Header("Gearbox")]
        [Tooltip("Speed multiplier. >1 = faster but less torque. <1 = more torque but slower.")]
        [Range(0.1f, 10f)]
        public float gearRatio = 2f;

        [Tooltip("Absolute RPM clamp on the output. The more speed, the less torque.")]
        public float maxOutputSpeed = 10000f;

        [Tooltip("Current selected gear (1-based). Adjusts gearRatio in steps.")]
        [Range(1, GearCount)]
        public int selectedGear = 2;

        /// <summary>Current RPM passing through (written back by ApplyResults).</summary>
        public float CurrentRPM { get; private set; }
        /// <summary>Input RPM (before gear ratio).</summary>
        public float InputRPM { get; private set; }
        /// <summary>Output RPM (after gear ratio, clamped).</summary>
        public float OutputRPM => CurrentRPM;
        /// <summary>True when output torque demand exceeds safe limits.</summary>
        public bool IsOverstressed { get; private set; }
        /// <summary>0..1 stress level for UI.</summary>
        public float Stress01 { get; private set; }

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Gearbox";
            // Legacy migration: pre-6.10 blocks were capped at 2000 RPM, which
            // silently killed gears above ~1.3× on stock engines.
            if (maxOutputSpeed <= 2000f) maxOutputSpeed = 10000f;
            ApplyGearSelection();
        }

        /// <summary>Select a gear (1..GearCount) and apply it immediately — no rebuild needed.</summary>
        public void SetGear(int gear)
        {
            selectedGear = Mathf.Clamp(gear, 1, GearCount);
            ApplyGearSelection();
        }

        /// <summary>Recalculate the effective gear ratio from the selected gear step.</summary>
        private void ApplyGearSelection()
        {
            int idx = Mathf.Clamp(selectedGear - 1, 0, GearRatios.Length - 1);
            gearRatio = GearRatios[idx];
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

            // Stay in sync with the UI every tick so gear changes apply LIVE.
            ApplyGearSelection();
            node.GearRatio = gearRatio;
            node.MaxGearSpeed = maxOutputSpeed;
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            CurrentRPM = node.CurrentRPM;
            // Input speed = output ÷ ratio (the job already applied the ratio upstream).
            InputRPM = CurrentRPM / Mathf.Max(0.01f, gearRatio);
            // Stress: if we're near the speed cap, the gearbox is stressed.
            float speedRatio = maxOutputSpeed > 0f ? CurrentRPM / maxOutputSpeed : 0f;
            Stress01 = Mathf.Clamp01(speedRatio);
            IsOverstressed = Stress01 > 0.95f;
        }
    }
}
