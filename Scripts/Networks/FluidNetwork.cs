// Assets/Scripts/VoxelEngine/Networks/FluidNetwork.cs
//
// Fluid/Gas network. Balances volume across all connected pipe anchors.
// Viscosity affects flow speed. Gases spread faster than liquids.

using UnityEngine;

namespace VoxelEngine.Networks
{
    public class FluidNetworkNew : ResourceNetwork<float>
    {
        public FluidType fluidType; // null = empty network, set when first fluid enters
        public float totalVolume;
        public float totalCapacity;
        public float flowRate; // litres/sec moving through the network this tick
        public float averageFill; // 0..1

        private readonly NetworkType _netType;

        public FluidNetworkNew(NetworkType netType) : base(netType)
        {
            _netType = netType;
        }

        public override bool CanAccept(ConnectionAnchor a)
        {
            return a.networkType == _netType;
        }

        public override void Tick(float dt)
        {
            // Gather totals.
            totalVolume = 0; totalCapacity = 0;
            foreach (var a in anchors)
            {
                if (a == null) continue;
                totalVolume += a.fluidVolume;
                totalCapacity += a.fluidCapacity;
            }

            if (totalCapacity <= 0) { averageFill = 0; flowRate = 0; return; }
            averageFill = totalVolume / totalCapacity;

            // Balance: equalize volume across all anchors proportional to capacity.
            // Flow speed scales inversely with viscosity.
            float visc = (fluidType != null) ? fluidType.viscosity : 1f;
            float speed = dt * (10f / visc); // higher viscosity = slower balancing
            speed = Mathf.Clamp01(speed);

            float moved = 0;
            foreach (var a in anchors)
            {
                if (a == null || a.fluidCapacity <= 0) continue;
                float target = averageFill * a.fluidCapacity;
                float diff = target - a.fluidVolume;
                float transfer = diff * speed;
                a.fluidVolume += transfer;
                a.fluidVolume = Mathf.Clamp(a.fluidVolume, 0, a.fluidCapacity);
                moved += Mathf.Abs(transfer);
            }
            flowRate = moved / Mathf.Max(0.001f, dt);
        }
    }
}
