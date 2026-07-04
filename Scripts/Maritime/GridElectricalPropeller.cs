// Assets/Scripts/VoxelEngine/Maritime/GridElectricalPropeller.cs
//
// Electrical Propeller (torpedo pod) — driven by grid electricity,
// not shaft torque. Medium thrust with fast spin-up.

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
            node.MaxTorque = powerDrawWatts;
            node.GearRatio = 1f;
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            bool hasPower = Grid != null && Grid.HasPower;
            _activeFraction = (Enabled && hasPower) ? throttle : 0f;
            node.FuelAvailable01 = _activeFraction;

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
