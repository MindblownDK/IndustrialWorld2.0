// Assets/Scripts/VoxelEngine/Power/PowerRelay.cs
//
// Compact wall/foundation relay for power networks. It behaves like a cable node
// for topology purposes, but exists as a small mountable device so players do not
// need full poles inside tidy bases.

using UnityEngine;

namespace VoxelEngine.Power
{
    public class PowerRelay : PowerNode
    {
        [Tooltip("Maximum automatic power links this relay may hold.")]
        public int maxConnections = 8;

        public override PowerNodeKind Kind => PowerNodeKind.Cable;
        public override int MaxAutoConnections => Mathf.Max(1, maxConnections);

        protected override void OnEnable()
        {
            connectRadius = Mathf.Max(connectRadius, 3.0f);
            requireGridAlignedNeighbours = false;
            base.OnEnable();
        }
    }

    public class LVWireConnector : PowerNode
    {
        public override PowerNodeKind Kind => PowerNodeKind.Cable;
        public override int MaxAutoConnections => 1;

        protected override void OnEnable()
        {
            connectRadius = Mathf.Max(connectRadius, 2.0f);
            requireGridAlignedNeighbours = false;
            base.OnEnable();
        }
    }

    public class HVWireConnector : PowerNode
    {
        public override PowerNodeKind Kind => PowerNodeKind.Cable;
        public override int MaxAutoConnections => 1;

        protected override void OnEnable()
        {
            connectRadius = Mathf.Max(connectRadius, 4.0f);
            requireGridAlignedNeighbours = false;
            base.OnEnable();
        }
    }
}
