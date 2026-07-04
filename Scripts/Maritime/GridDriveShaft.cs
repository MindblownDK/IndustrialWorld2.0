// Assets/Scripts/VoxelEngine/Maritime/GridDriveShaft.cs
//
// Drive Shaft — the torque conduit. Passes torque linearly from an engine
// toward a propeller, gearbox or generator. If the shaft is disabled or
// destroyed, torque stops flowing downstream (handled by the Broken flag
// in MechanicalPropagationJob).

using UnityEngine;

namespace VoxelEngine.Maritime
{
    public class GridDriveShaft : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Shaft;

        [Header("Shaft")]
        [Tooltip("Max RPM this shaft can handle before shearing (visual only for now).")]
        public float maxSafeRPM = 3000f;

        /// <summary>Current RPM passing through (written back by ApplyResults).</summary>
        public float CurrentRPM { get; private set; }

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Drive Shaft";
        }

        public override void PopulateMaritimeNode(ref MechanicalNode node)
        {
            node.MaxRPM = maxSafeRPM;
            node.GearRatio = 1f; // direct pass-through
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            // A disabled shaft severs the chain.
            if (!Enabled)
                node.SetFlag(MechanicalFlags.Broken);
            else
                node.ClearFlag(MechanicalFlags.Broken);
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            CurrentRPM = node.CurrentRPM;
        }
    }
}
