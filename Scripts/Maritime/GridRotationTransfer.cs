// Assets/Scripts/VoxelEngine/Maritime/GridRotationTransfer.cs
//
// Compact rotation-transfer block for maritime mechanical networks. It behaves as a
// shaft in the Burst propulsion graph, while its prefab/rotation makes it clear how
// the player wants the mechanical line to turn: straight, up, or down. Rotating the
// block in build mode naturally turns those routes left/right as well.

using UnityEngine;

namespace VoxelEngine.Maritime
{
    public enum RotationTransferRoute : byte
    {
        Straight = 0,
        Up = 1,
        Down = 2,
    }

    public class GridRotationTransfer : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Shaft;

        [Header("Rotation Transfer")]
        [Tooltip("Visual/mechanical route. The graph accepts any adjacent mechanical neighbour; this describes the intended casing route.")]
        public RotationTransferRoute route = RotationTransferRoute.Straight;

        [Tooltip("Maximum RPM this transfer casing can safely carry.")]
        public float maxSafeRPM = 3200f;

        public float CurrentRPM { get; private set; }

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Rotation Transfer";
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
