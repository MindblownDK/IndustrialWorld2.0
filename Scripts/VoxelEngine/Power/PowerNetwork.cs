// Assets/Scripts/VoxelEngine/Power/PowerNetwork.cs
using System.Collections.Generic;

namespace VoxelEngine.Power
{
    /// <summary>
    /// One connected component of power nodes (cables + machines). Recomputed lazily
    /// when nodes change. Holds the network-wide bottleneck = minimum cable capacity
    /// among all cables in the network. Generators contribute up to that bottleneck.
    /// </summary>
    public class PowerNetwork
    {
        public readonly List<PowerNode> nodes = new();
        public float bottleneckWatts;          // minimum cable capacity in this network (W/s)
        public float producedThisTick;
        public float consumedThisTick;
        public float storedThisTick;           // delta into batteries

        public void Recompute()
        {
            bottleneckWatts = float.PositiveInfinity;
            foreach (var n in nodes)
            {
                if (n is PowerCable c && c.wire != null)
                {
                    if (c.wire.capacityWatts < bottleneckWatts)
                        bottleneckWatts = c.wire.capacityWatts;
                }
            }
            // No cables in the network → power flows freely (direct connection).
            if (float.IsInfinity(bottleneckWatts)) bottleneckWatts = float.MaxValue;
        }
    }
}
