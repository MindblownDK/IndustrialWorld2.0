// Assets/Scripts/VoxelEngine/Maritime/GridPropeller.cs
//
// Shaft-driven propeller. Consumes shaft torque to generate thrust.
// Small (1×1×1): low thrust, highly maneuverable.
// Large  (3×3×1): extreme thrust, slow spin-up.

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    /// <summary>Propeller size tier.</summary>
    public enum PropellerTier : byte
    {
        Small = 0,
        Large = 1,
    }

    public class GridPropeller : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Propeller;

        [Header("Propeller")]
        public PropellerTier tier = PropellerTier.Small;
        [Tooltip("Size multiplier — 1 for small, 3 for large.")]
        public float propellerSize = 1f;
        [Tooltip("Max RPM at full shaft power.")]
        public float maxRPM = 2000f;

        public float CurrentRPM { get; private set; }
        public float Submergence { get; private set; }
        public float CurrentThrustN { get; private set; }

        public override void OnPlaced()
        {
            base.OnPlaced();
            propellerSize = tier == PropellerTier.Large ? 3f : 1f;
            blockName = tier == PropellerTier.Large ? "Large Propeller" : "Small Propeller";
        }

        public override void PopulateMaritimeNode(ref MechanicalNode node)
        {
            node.MaxRPM = maxRPM;
            node.PropellerSize = propellerSize;
            node.MaxTorque = 0f;
            node.GearRatio = 1f;
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            node.FuelAvailable01 = Enabled ? 1f : 0f;
            if (!Enabled)
                node.SetFlag(MechanicalFlags.Broken);
            else
                node.ClearFlag(MechanicalFlags.Broken);
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            CurrentRPM = node.CurrentRPM;
            Submergence = node.Submergence;
            CurrentThrustN = Unity.Mathematics.math.length(node.ComputedForce);
        }
    }
}
