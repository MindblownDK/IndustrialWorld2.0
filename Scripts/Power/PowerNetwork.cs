// Assets/Scripts/VoxelEngine/Power/PowerNetwork.cs
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Power
{
    public class PowerNetwork
    {
        public readonly List<PowerNode> nodes = new();
        public float bottleneckWatts;
        public float producedThisTick;
        public float consumedThisTick;
        public float storedThisTick;

        public void Recompute()
        {
            bottleneckWatts = float.PositiveInfinity;
            
            // Check node-based capacity (Electrical Pipes)
            foreach (var n in nodes)
            {
                if (n is PowerCable c && c.wire != null)
                {
                    // Superconductor / infinite capacity check
                    if (c.wire.capacityWatts < 0 || c.wire.capacityWatts >= 1000000000f)
                        continue;

                    if (c.wire.capacityWatts < bottleneckWatts)
                        bottleneckWatts = c.wire.capacityWatts;
                }
            }

            // Check manual link-based capacity (LV Wires)
            foreach (var n in nodes)
            {
                foreach (var nb in n.neighbours)
                {
                    if (n.manualLinkCapacities.TryGetValue(nb, out float cap))
                    {
                        if (cap > 0 && cap < bottleneckWatts)
                            bottleneckWatts = cap;
                    }
                }
            }

            if (float.IsInfinity(bottleneckWatts)) bottleneckWatts = float.MaxValue;
        }
    }
}
