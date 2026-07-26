// Assets/Scripts/VoxelEngine/Fluids/FluidNetwork.cs
using System.Collections.Generic;

namespace VoxelEngine.Fluids
{
    /// <summary>
    /// One connected component of fluid nodes. Bottleneck = lowest pipe maxFlowLps.
    /// </summary>
    public class FluidNetwork
    {
        public readonly List<FluidNode> nodes = new();
        public float bottleneckLps;

        public void Recompute()
        {
            bottleneckLps = float.PositiveInfinity;
            foreach (var n in nodes)
                if (n is WaterPipe p && p.maxFlowLps < bottleneckLps)
                    bottleneckLps = p.maxFlowLps;
            if (float.IsInfinity(bottleneckLps)) bottleneckLps = 0f;
        }
    }
}
