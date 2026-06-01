// Assets/Scripts/VoxelEngine/Networks/PowerNetwork.cs
//
// Power network. Distributes generated watts to consumers.
// Short-circuits if a high-tier cable connects to a low-tier cable directly.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Networks
{
    public class PowerNetworkNew : ResourceNetwork<float>
    {
        public float totalGenerated;
        public float totalConsumed;
        public float bottleneckWatts = float.MaxValue;
        public bool isShortCircuited;

        public PowerNetworkNew() : base(NetworkType.Power) { }

        public override bool CanAccept(ConnectionAnchor a)
        {
            return a.networkType == NetworkType.Power;
        }

        public override void Tick(float dt)
        {
            // Detect short circuit: any two connected anchors with tier difference > 1.
            isShortCircuited = false;
            PowerTier? minTier = null, maxTier = null;
            float gen = 0, con = 0;
            bottleneckWatts = float.MaxValue;

            foreach (var a in anchors)
            {
                if (a == null) continue;
                if (minTier == null || a.powerTier < minTier) minTier = a.powerTier;
                if (maxTier == null || a.powerTier > maxTier) maxTier = a.powerTier;
                bottleneckWatts = Mathf.Min(bottleneckWatts, a.powerTier.MaxWatts());

                // Gather power from generators/consumers via anchor callbacks.
                gen += a.powerOutput;
                con += a.powerDraw;
            }

            // Short circuit: high tier directly connected to low tier without transformer.
            if (minTier.HasValue && maxTier.HasValue && (int)maxTier.Value - (int)minTier.Value > 0)
                isShortCircuited = true;

            if (isShortCircuited)
            {
                totalGenerated = 0; totalConsumed = con;
                foreach (var a in anchors) a.isPowered = false;
                return;
            }

            float supply = Mathf.Min(gen, bottleneckWatts);
            totalGenerated = supply;
            totalConsumed = con;

            bool allPowered = supply >= con - 0.01f;
            foreach (var a in anchors)
                if (a != null) a.isPowered = allPowered;
        }
    }
}
