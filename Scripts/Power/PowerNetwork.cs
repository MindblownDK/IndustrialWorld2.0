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
            
            // Energy Pipes are unlimited transport segments. Their definition still
            // controls visual/tier identity but never throttles power transfer.

            // Check manual wire-link capacity (LV/HV Wires)
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
