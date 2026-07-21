// Assets/Scripts/VoxelEngine/Maritime/GridMarineWaterPump.cs
//
// Marine Water Pump — a grid block that sucks up water from the ocean (below
// the ship) and feeds it into connected liquid tanks. Used to supply engine
// coolant, bilge pump priming, and general onboard water needs.
//
//   • Requires the pump to be submerged (below waterline) to operate.
//   • Draws power from the grid.
//   • Pushes water into GridLiquidTank blocks set to Water.
//   • Internal buffer so it can prime before tanks accept.

using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Maritime
{
    public class GridMarineWaterPump : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Hull; // passive — no torque role

        [Header("Marine Water Pump")]
        [Tooltip("Litres pumped per second when submerged.")]
        public float pumpRate = 50f;
        [Tooltip("Internal buffer size (litres).")]
        public float bufferCapacity = 100f;
        [Tooltip("Power consumed while pumping (W).")]
        public float powerDrawWatts = 200f;
        [Tooltip("How far below the pump to look for water (metres).")]
        public float suctionDepth = 3f;

        public float Buffer { get; private set; }
        public bool IsSubmerged { get; private set; }
        public bool IsPumping { get; private set; }

        public override float PowerDraw => (Enabled && IsPumping) ? powerDrawWatts : 0f;
        public override float ContentMass => Buffer * LiquidType.Water.DensityKgPerL();
        public float Fill01 => bufferCapacity > 0f ? Mathf.Clamp01(Buffer / bufferCapacity) : 0f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            BlockMass = 150f;
            maxHP = 400f;
            currentHP = maxHP;
            blockName = "Marine Water Pump";
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            UpdateSubmerged();
        }

        /// <summary>Check if the pump is touching water (bottom of block at/below water surface).</summary>
        private void UpdateSubmerged()
        {
            IsSubmerged = WaterProbeSystem.GetSubmergence(transform.position - transform.up * 0.5f, suctionDepth) > 0.08f;
        }

        private void FixedUpdate()
        {
            if (!Enabled || Grid == null || !Grid.HasPower)
            {
                IsPumping = false;
                return;
            }

            float dt = Time.fixedDeltaTime;

            // Re-check submergence each tick (water level can change).
            UpdateSubmerged();

            // Suck water from the ocean — pump must be touching the water.
            if (IsSubmerged && Buffer < bufferCapacity - 0.1f)
            {
                Buffer = Mathf.Min(bufferCapacity, Buffer + pumpRate * dt);
                IsPumping = true;
            }
            else
            {
                IsPumping = false;
            }

            // Push water into grid tanks set to Water.
            if (Buffer > 0.1f)
            {
                float pushed = PushToTanks(Buffer, dt);
                Buffer = Mathf.Max(0f, Buffer - pushed);
            }
        }

        private float PushToTanks(float available, float dt)
        {
            if (Grid == null || available <= 0f) return 0f;
            float maxPush = Mathf.Min(available, pumpRate * dt);

            if (GridLiquidNetwork.Instance != null && GridLiquidNetwork.Instance.HasPipes(Grid))
                return GridLiquidNetwork.Instance.FillLiquidFrom(this, LiquidType.Water, maxPush);

            float remaining = maxPush;
            foreach (var kv in Grid.Blocks)
            {
                if (remaining <= 0.01f) break;
                if (kv.Value is not GridLiquidTank tank) continue;
                if (tank.mode != GridTankMode.Auto) continue;
                if (tank.liquidType != LiquidType.Water) continue;

                float push = Mathf.Min(tank.capacity - tank.stored, remaining);
                if (push <= 0f) continue;
                tank.stored += push;
                remaining -= push;
            }

            return maxPush - remaining;
        }
    }
}
