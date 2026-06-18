// Assets/Scripts/VoxelEngine/Maritime/GridEncasedChainDrive.cs
//
// Encased Chain Drive — a protected shaft segment with visible chain casing and
// propeller mounting points. Functionally it is a shaft in the mechanical graph,
// so it carries RPM/torque between engines, transfer blocks, and shaft propellers.

using UnityEngine;

namespace VoxelEngine.Maritime
{
    public class GridEncasedChainDrive : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Shaft;

        [Header("Encased Chain Drive")]
        [Tooltip("Maximum RPM this chain drive can safely carry.")]
        public float maxSafeRPM = 2600f;

        public float CurrentRPM { get; private set; }

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Encased Chain Drive";
        }

        public override void PopulateMaritimeNode(ref MechanicalNode node)
        {
            node.MaxRPM = maxSafeRPM;
            node.GearRatio = 1f;
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
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
