// Assets/Scripts/VoxelEngine/Maritime/GridHullBlock.cs
//
// Base class for all hull-material blocks. Provides buoyancy factor,
// waterproofing, waterlogging state and a health multiplier.
//
// NOTE: Each material variant (UntreatedWood, TarPlank, IronHull, BalsaWood)
// has its OWN file — Unity requires MonoBehaviour subclasses to be in a file
// matching their class name or prefab script references break.

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    public class GridHullBlock : GridBlock
    {
        [Header("Hull Material")]
        [Tooltip("0 = sinks (iron). 1 = maximally buoyant (balsa/cork).")]
        [Range(0f, 1f)]
        public float buoyancyFactor = 0.7f;

        [Tooltip("If true, this block never absorbs water (tar-coated, sealed).")]
        public bool waterproof = false;

        [Tooltip("Maximum water this block can absorb (kg). 0 = waterproof.")]
        public float maxWaterlogging = 0f;

        [Tooltip("Water absorbed per second while fully submerged (kg/s).")]
        public float soakRate = 0f;

        [Tooltip("Extra HP multiplier applied on top of maxHP at placement.")]
        public float healthMultiplier = 1f;

        /// <summary>Current absorbed water mass (kg). Drives ContentMass.</summary>
        public float WaterloggedMass { get; set; }

        /// <summary>0..1 waterlogging fill.</summary>
        public float WaterlogFill01 => maxWaterlogging > 0f ? Mathf.Clamp01(WaterloggedMass / maxWaterlogging) : 0f;

        /// <summary>Waterlogged mass adds to the block's total mass (sinks the ship).</summary>
        public override float ContentMass => WaterloggedMass;

        public override void OnPlaced()
        {
            base.OnPlaced();
            maxHP *= healthMultiplier;
            currentHP = maxHP;
        }
    }
}
