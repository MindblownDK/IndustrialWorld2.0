using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    public sealed class CompactPowerNode : PowerNode
    {
        public int maxAutoConnections = 8;
        public override PowerNodeKind Kind => PowerNodeKind.Cable;
        public override int MaxAutoConnections => Mathf.Max(1, maxAutoConnections);
    }
}
