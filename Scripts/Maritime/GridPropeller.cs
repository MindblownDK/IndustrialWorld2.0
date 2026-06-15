// Assets/Scripts/VoxelEngine/Maritime/GridPropeller.cs
//
// Propeller — consumes shaft torque to generate thrust.
//   Small (1×1×1):  low thrust, highly maneuverable.
//   Large  (3×3×1): extreme thrust, slow spin-up.
//
// Thrust = RPM × Submergence × PropellerSize × ThrustCoeff
// (computed in BuoyancyJob). This block just feeds its parameters into the
// node and reports the computed values back for UI/audio.
//
// Also contains the ElectricalPropeller variant — driven by electricity
// instead of shaft torque, with fast spin-up.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Maritime
{
    /// <summary>Propeller size tier.</summary>
    public enum PropellerTier : byte
    {
        Small = 0,  // 1×1×1
        Large = 1,  // 3×3×1
    }

    // ────────────────────────────────────────────────────────────────────
    //  SHAFT-DRIVEN PROPELLER
    // ────────────────────────────────────────────────────────────────────
    public class GridPropeller : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Propeller;

        [Header("Propeller")]
        public PropellerTier tier = PropellerTier.Small;
        [Tooltip("Size multiplier — 1 for small, 3 for large.")]
        public float propellerSize = 1f;
        [Tooltip("Max RPM at full shaft power.")]
        public float maxRPM = 2000f;

        /// <summary>Current RPM (written back by ApplyResults).</summary>
        public float CurrentRPM { get; private set; }
        /// <summary>Current submergence 0..1 (written back by ApplyResults).</summary>
        public float Submergence { get; private set; }
        /// <summary>Current thrust in Newtons (written back by ApplyResults).</summary>
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
            node.MaxTorque = 0f; // propellers don't produce torque
            node.GearRatio = 1f;
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            // Propellers are passive consumers — they don't gate on fuel/throttle.
            node.FuelAvailable01 = Enabled ? 1f : 0f;
            if (!Enabled)
                node.Flags |= MechanicalFlags.Broken;
            else
                node.Flags &= (byte)~MechanicalFlags.Broken;
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            CurrentRPM = node.CurrentRPM;
            Submergence = node.Submergence;
            CurrentThrustN = Unity.Mathematics.math.length(node.ComputedForce);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  ELECTRICAL PROPELLER (torpedo pod)
    // ────────────────────────────────────────────────────────────────────
    public class GridElectricalPropeller : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.ElectricalPropeller;

        [Header("Electric Propeller")]
        [Tooltip("Size multiplier.")]
        public float propellerSize = 2f;
        [Tooltip("Max RPM at full power.")]
        public float maxRPM = 3000f;
        [Tooltip("Power consumed at full thrust (W).")]
        public float powerDrawWatts = 2000f;

        /// <summary>Current RPM (written back by ApplyResults).</summary>
        public float CurrentRPM { get; private set; }
        /// <summary>Current thrust in Newtons (written back by ApplyResults).</summary>
        public float CurrentThrustN { get; private set; }

        public override float PowerDraw => Enabled ? powerDrawWatts * _activeFraction : 0f;

        private float _activeFraction;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Electrical Propeller";
        }

        public override void PopulateMaritimeNode(ref MechanicalNode node)
        {
            node.MaxRPM = maxRPM;
            node.PropellerSize = propellerSize;
            node.MaxTorque = powerDrawWatts; // reused as watt-demand proxy in the job
            node.GearRatio = 1f;
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            // E-propellers draw from the grid's electrical pool.
            bool hasPower = Grid != null && Grid.HasPower;
            _activeFraction = (Enabled && hasPower) ? throttle : 0f;
            node.FuelAvailable01 = _activeFraction;

            if (!Enabled)
                node.Flags |= MechanicalFlags.Broken;
            else
                node.Flags &= (byte)~MechanicalFlags.Broken;
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            CurrentRPM = node.CurrentRPM;
            CurrentThrustN = Unity.Mathematics.math.length(node.ComputedForce);
        }
    }
}
