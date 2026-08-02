// Assets/Scripts/VoxelEngine/Maritime/GridElectricalPropeller.cs
//
// Electrical Propeller (torpedo pod) — driven by the grid power bus rather than
// shaft torque. It declares its commanded draw every tick, then uses the latest
// resolved grid-service fraction for real thrust on the following maritime tick.
// This keeps an undersupplied grid stable instead of flickering between zero and
// full demand.

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    public class GridElectricalPropeller : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.ElectricalPropeller;

        [Header("Electric Propeller")]
        public float propellerSize = 2f;
        public float maxRPM = 3000f;
        public float powerDrawWatts = 2000f;

        public float CurrentRPM { get; private set; }
        public float CurrentThrustN { get; private set; }
        /// <summary>0..1 pilot command after enabled/throttle checks, before power rationing.</summary>
        public float CommandedFraction => _commandedFraction;
        /// <summary>0..1 fraction of commanded draw delivered by the grid on the latest resolved tick.</summary>
        public float PowerAvailability01 => _gridPowerAvailability01;
        /// <summary>Actual fraction used for thrust this maritime tick.</summary>
        public float ActiveFraction => _activeFraction;
        public float CommandedPowerWatts => Mathf.Max(0f, powerDrawWatts) * _commandedFraction;
        public float DeliveredPowerWatts => Mathf.Max(0f, powerDrawWatts) * _activeFraction;

        // The grid ledger must always see the requested demand. It is intentionally
        // independent from _activeFraction, which is the previous resolved service
        // fraction used only for this tick's thrust.
        public override float PowerDraw => Enabled ? CommandedPowerWatts : 0f;

        private float _commandedFraction;
        private float _activeFraction;
        private float _gridPowerAvailability01 = 1f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Electrical Propeller";
        }

        /// <summary>Called by GridEntity after its authoritative power-bus resolution.</summary>
        public void SetGridPowerAvailability(float availability01)
        {
            _gridPowerAvailability01 = Mathf.Clamp01(availability01);
        }

        public override void PopulateMaritimeNode(ref MechanicalNode node)
        {
            node.MaxRPM = maxRPM;
            node.PropellerSize = propellerSize;
            // Reused as the rated electrical demand in the Burst node.
            node.MaxTorque = Mathf.Max(0f, powerDrawWatts);
            node.GearRatio = 1f;
            node.PowerCommand01 = 0f;
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            _commandedFraction = Enabled ? Mathf.Clamp01(throttle) : 0f;
            float availability = Grid != null ? Grid.PowerAvailability01 : 0f;
            _gridPowerAvailability01 = Mathf.Clamp01(availability);
            _activeFraction = _commandedFraction * _gridPowerAvailability01;

            node.PowerCommand01 = _commandedFraction;
            node.FuelAvailable01 = _activeFraction;
            node.MaxRPM = maxRPM;
            node.MaxTorque = Mathf.Max(0f, powerDrawWatts);

            if (!Enabled)
                node.SetFlag(MechanicalFlags.Broken);
            else
                node.ClearFlag(MechanicalFlags.Broken);
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            CurrentRPM = node.CurrentRPM;
            CurrentThrustN = Unity.Mathematics.math.length(node.ComputedForce);
        }
    }
}
