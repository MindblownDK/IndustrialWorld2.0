// Assets/Scripts/VoxelEngine/Maritime/GridHullBlock.cs
//
// Hull material base class + four material variants.
//
//   Untreated Wood  — Medium mass, high buoyancy, SOAKS UP WATER over time
//                     (increases mass → sinks lower → more soaking → feedback loop).
//                     Early-game hull that forces progression.
//
//   Tar-Coated Plank — Low mass, high buoyancy, 100% WATERPROOF (never waterlogs).
//                      Mid-game hull for reliable ocean voyages.
//
//   Iron Hull       — Very high mass, ZERO buoyancy, insane HP.
//                     Requires massive internal air pockets to float.
//                     Late-game armored warships.
//
//   Balsa Wood/Cork — Ultra-low mass, MAXIMUM buoyancy, fragile (low HP).
//                      Used for lifeboats, buoys, outriggers, stabilizers.
//
// Waterlogging:
//   Non-waterproof hulls absorb water while submerged. Each unit of waterlogging
//   adds mass (dragging the ship down). The MaritimePropulsionSystem batches the
//   waterlogging tick after the buoyancy job — no per-block MonoBehaviour loop.
//
// Bilge Pump blocks reverse waterlogging on nearby hulls by consuming power.

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    /// <summary>
    /// Base class for all hull-material blocks. Provides buoyancy factor,
    /// waterproofing, waterlogging state and a health multiplier. Read by the
    /// MaritimePropulsionSystem for buoyancy computation + waterlogging tick.
    /// </summary>
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

    // ══════════════════════════════════════════════════════════════════
    //  MATERIAL VARIANTS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Untreated Wood — the starter hull. Buoyant but soaks up water over time,
    /// gradually increasing mass and dragging the ship down. Forces the player
    /// to research waterproof hulls (Tar-Coated Plank) for long voyages.
    /// </summary>
    public class GridUntreatedWood : GridHullBlock
    {
        public override void OnPlaced()
        {
            buoyancyFactor = 0.85f;
            waterproof = false;
            maxWaterlogging = 40f;       // kg of water it can soak
            soakRate = 1.5f;             // kg/s while submerged
            healthMultiplier = 1f;
            BlockMass = 80f;
            base.OnPlaced();
            blockName = "Untreated Wood Hull";
        }
    }

    /// <summary>
    /// Tar-Coated Plank — the reliable mid-game hull. Same buoyancy as untreated
    /// wood but 100% waterproof: never waterlogs, never gains mass from soaking.
    /// </summary>
    public class GridTarCoatedPlank : GridHullBlock
    {
        public override void OnPlaced()
        {
            buoyancyFactor = 0.9f;
            waterproof = true;
            maxWaterlogging = 0f;
            soakRate = 0f;
            healthMultiplier = 1.3f;
            BlockMass = 60f;
            base.OnPlaced();
            blockName = "Tar-Coated Plank";
        }
    }

    /// <summary>
    /// Iron Hull — late-game armored hull. Zero natural buoyancy (sinks!) and
    /// extremely heavy, but massive HP. Requires internal air-pocket chambers
    /// (empty hull blocks below the waterline) to displace enough water to float.
    /// </summary>
    public class GridIronHull : GridHullBlock
    {
        public override void OnPlaced()
        {
            buoyancyFactor = 0.0f;       // iron sinks — needs air pockets to float
            waterproof = true;
            maxWaterlogging = 0f;
            soakRate = 0f;
            healthMultiplier = 5f;       // insane health
            BlockMass = 400f;
            base.OnPlaced();
            blockName = "Iron Hull";
        }
    }

    /// <summary>
    /// Balsa Wood / Cork — ultra-light, maximally buoyant, but fragile.
    /// Perfect for lifeboats, buoys, outrigger stabilizers. Breaks easily.
    /// </summary>
    public class GridBalsaWood : GridHullBlock
    {
        public override void OnPlaced()
        {
            buoyancyFactor = 1.0f;
            waterproof = true;
            maxWaterlogging = 0f;
            soakRate = 0f;
            healthMultiplier = 0.4f;     // breaks easily
            BlockMass = 25f;
            base.OnPlaced();
            blockName = "Balsa Wood";
        }
    }
}
