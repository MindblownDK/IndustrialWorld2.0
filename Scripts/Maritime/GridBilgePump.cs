// Assets/Scripts/VoxelEngine/Maritime/GridBilgePump.cs
//
// Bilge Pump Block — heavy, no buoyancy, consumes electricity to slowly remove
// waterlogged mass from nearby hull blocks. Essential for surviving mega-storms
// or hull breaches that flood untreated-wood ships.
//
//   • Draws power from the grid-wide pool (GridEntity.HasPower).
//   • Scans a radius for GridHullBlock instances and reduces their WaterloggedMass.
//   • More pumps = faster draining (they stack additively).
//
// The actual draining happens in the MaritimePropulsionSystem batched tick
// (not per-block Update) so hundreds of hull blocks + pumps stay cheap.

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    public class GridBilgePump : GridBlock
    {
        [Header("Bilge Pump")]
        [Tooltip("Water removed per second per affected hull block (kg/s).")]
        public float drainRate = 5f;

        [Tooltip("Radius (in grid cells) of hull blocks this pump can drain.")]
        public float drainRadiusCells = 4f;

        [Tooltip("Power consumed while actively draining (W).")]
        public float powerDrawWatts = 500f;

        /// <summary>True while powered and at least one nearby hull is waterlogged.</summary>
        public bool IsActive { get; private set; }

        public override float PowerDraw => (Enabled && IsActive) ? powerDrawWatts : 0f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            BlockMass = 200f;
            maxHP = 400f;
            currentHP = maxHP;
            blockName = "Bilge Pump";
        }

        /// <summary>
        /// Called by MaritimePropulsionSystem during the batched waterlogging tick.
        /// Drains water from nearby hull blocks if the grid has power.
        /// </summary>
        public void TickDrain()
        {
            if (!Enabled || Grid == null || !Grid.HasPower) { IsActive = false; return; }

            float cs = Grid.gridSize.CellSize();
            float radiusWorld = drainRadiusCells * cs;
            float radiusSq = radiusWorld * radiusWorld;
            bool drainedAny = false;

            foreach (var kv in Grid.Blocks)
            {
                if (kv.Value is not GridHullBlock hull) continue;
                if (hull.WaterloggedMass <= 0.01f) continue;

                // Distance check in grid space.
                float distSq = (kv.Key - GridPos).sqrMagnitude;
                if (distSq > drainRadiusCells * drainRadiusCells) continue;

                float remove = drainRate * Time.fixedDeltaTime;
                hull.WaterloggedMass = Mathf.Max(0f, hull.WaterloggedMass - remove);
                drainedAny = true;
            }

            IsActive = drainedAny;
        }
    }
}
