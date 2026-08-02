// Assets/Scripts/VoxelEngine/Maritime/GridShaftHousing.cs
//
// Watertight Shaft Housing — a sealed hull module with a through-shaft. The
// hull stays closed to the sea while the mechanical line continues through it.

using UnityEngine;

namespace VoxelEngine.Maritime
{
    /// <summary>
    /// A waterproof hull penetration with a bidirectional mechanical shaft inside.
    /// It deliberately derives from <see cref="GridHullBlock"/> so it contributes
    /// sealed hull buoyancy rather than behaving like an exposed, non-hull shaft.
    /// </summary>
    public sealed class GridShaftHousing : GridHullBlock, IMechanicalBlock
    {
        [Header("Watertight Shaft Housing")]
        [Tooltip("Maximum rotational speed the sealed bearing assembly can safely carry.")]
        [Min(1f)] public float maxSafeRPM = 3200f;

        /// <summary>Current shaft speed, written back by the maritime propagation job.</summary>
        public float CurrentRPM { get; private set; }

        public MechanicalNodeType NodeType => MechanicalNodeType.Shaft;

        public override void OnPlaced()
        {
            // These are functional sealing guarantees, not balance defaults: a shaft
            // housing must never soak or open a wet hull route.
            waterproof = true;
            maxWaterlogging = 0f;
            soakRate = 0f;

            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Watertight Shaft Housing";
        }

        public void PopulateMaritimeNode(ref MechanicalNode node)
        {
            node.MaxRPM = Mathf.Max(1f, maxSafeRPM);
            node.GearRatio = 1f;
        }

        public void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            if (!Enabled)
                node.SetFlag(MechanicalFlags.Broken);
            else
                node.ClearFlag(MechanicalFlags.Broken);
        }

        public void ApplyResults(in MechanicalNode node)
        {
            CurrentRPM = node.CurrentRPM;
        }
    }
}
