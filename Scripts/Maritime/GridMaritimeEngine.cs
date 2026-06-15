// Assets/Scripts/VoxelEngine/Maritime/GridMaritimeEngine.cs
//
// Maritime engine block. Three tiers share one class:
//
//   Small  (1×2×1)  — burns Wood/Coal items  → low torque, high fuel efficiency
//   Medium (2×3×2)  — burns Heavy Fuel Oil   → medium torque, steady RPM
//   Giant  (4×5×6)  — burns Marine Gas Oil   → colossal torque, heavy
//
// Fuel is drawn from grid storage (cargo for solids, liquid tanks for liquids)
// into an internal buffer. FuelAvailable01 = buffer fill × throttle.
//
// REQUIRES an adjacent Exhaust Pipe — without one the engine chokes and
// produces zero torque. A Giant Diesel adjacent to a Turbocharger gets ×1.40.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Maritime
{
    /// <summary>Engine size tier — drives torque, RPM, fuel type and mass.</summary>
    public enum EngineTier : byte
    {
        Small = 0,
        Medium = 1,
        Giant = 2,
    }

    public class GridMaritimeEngine : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Engine;

        [Header("Engine Tier")]
        public EngineTier tier = EngineTier.Small;

        [Header("Performance")]
        [Tooltip("Maximum torque output in N·m (before turbo boost).")]
        public float maxTorque = 8000f;
        [Tooltip("Maximum rotational speed in rev/min.")]
        public float maxRPM = 1500f;

        [Header("Fuel")]
        public MaritimeFuelKind fuelKind = MaritimeFuelKind.Solid;
        [Tooltip("Liquid fuel type consumed (when fuelKind = Liquid).")]
        public LiquidType liquidFuel = LiquidType.LiquidFuel;
        [Tooltip("Internal fuel buffer capacity. Solid = burn-seconds, Liquid = litres.")]
        public float fuelBufferCapacity = 60f;
        [Tooltip("Fuel consumed per second at full throttle. Solid = burn-sec/sec, Liquid = litres/sec.")]
        public float fuelConsumptionRate = 1f;
        [Tooltip("Litres pulled from grid tanks per second when refilling.")]
        public float liquidRefillRate = 10f;

        [Header("State (read-only)")]
        /// <summary>Current fuel buffer level (0..capacity).</summary>
        public float FuelBuffer { get; private set; }

        /// <summary>0..1 fill ratio of the internal fuel buffer.</summary>
        public float FuelFill01 => fuelBufferCapacity > 0f ? Mathf.Clamp01(FuelBuffer / fuelBufferCapacity) : 0f;

        /// <summary>True while the engine is actively producing torque.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Current RPM (written back by ApplyResults).</summary>
        public float CurrentRPM { get; private set; }

        /// <summary>True if an exhaust pipe is adjacent (otherwise the engine chokes).</summary>
        public bool HasExhaust { get; private set; }

        public override float ContentMass
        {
            get
            {
                if (fuelKind == MaritimeFuelKind.Liquid)
                    return FuelBuffer * liquidFuel.DensityKgPerL();
                return 0f; // solid fuel mass already counted in cargo
            }
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            // Auto-configure based on tier.
            switch (tier)
            {
                case EngineTier.Small:
                    blockName = "Small Engine";
                    fuelKind = MaritimeFuelKind.Solid;
                    fuelBufferCapacity = 60f;
                    fuelConsumptionRate = 1f;
                    break;
                case EngineTier.Medium:
                    blockName = "Medium Engine";
                    fuelKind = MaritimeFuelKind.Liquid;
                    liquidFuel = LiquidType.HeavyFuelOil;
                    fuelBufferCapacity = 80f;
                    fuelConsumptionRate = 2f;
                    liquidRefillRate = 8f;
                    break;
                case EngineTier.Giant:
                    blockName = "Giant Diesel Engine";
                    fuelKind = MaritimeFuelKind.Liquid;
                    liquidFuel = LiquidType.MarineGasOil;
                    fuelBufferCapacity = 300f;
                    fuelConsumptionRate = 6f;
                    liquidRefillRate = 25f;
                    break;
            }
            FuelBuffer = Mathf.Min(FuelBuffer, fuelBufferCapacity);
        }

        // ══════════════════════════════════════════════════════════════
        //  IMechanicalBlock
        // ══════════════════════════════════════════════════════════════
        public override void PopulateMaritimeNode(ref MechanicalNode node)
        {
            node.MaxTorque = maxTorque;
            node.MaxRPM = maxRPM;
            node.GearRatio = 1f;
            node.PropellerSize = 1f;

            if (tier == EngineTier.Giant)
                node.Flags |= MechanicalFlags.GiantDiesel;
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            // Exhaust check — choke the engine if no exhaust pipe is adjacent.
            HasExhaust = HasAdjacentExhaust();

            if (!Enabled || !HasExhaust)
            {
                node.FuelAvailable01 = 0f;
                IsRunning = false;
                node.Flags |= MechanicalFlags.Broken;
                return;
            }
            node.Flags &= (byte)~MechanicalFlags.Broken;

            // Consume fuel from the internal buffer.
            float dt = Time.fixedDeltaTime;
            float consumption = fuelConsumptionRate * throttle * dt;
            FuelBuffer = Mathf.Max(0f, FuelBuffer - consumption);

            // Refill from grid storage.
            RefillBuffer(dt);

            IsRunning = FuelBuffer > 0.01f && throttle > 0.01f;
            node.FuelAvailable01 = IsRunning ? FuelFill01 * throttle : 0f;
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            CurrentRPM = node.CurrentRPM;
        }

        // ══════════════════════════════════════════════════════════════
        //  FUEL MANAGEMENT
        // ══════════════════════════════════════════════════════════════
        private void RefillBuffer(float dt)
        {
            float space = fuelBufferCapacity - FuelBuffer;
            if (space < 0.01f) return;

            if (fuelKind == MaritimeFuelKind.Solid)
            {
                // Only pull a new fuel item when the buffer is getting low
                // (avoids draining cargo one item per frame).
                if (FuelBuffer < fuelBufferCapacity * 0.25f)
                {
                    float burnSec = DrawSolidFuel();
                    if (burnSec > 0f)
                        FuelBuffer = Mathf.Min(fuelBufferCapacity, FuelBuffer + burnSec);
                }
            }
            else
            {
                float want = Mathf.Min(space, liquidRefillRate * dt);
                float drawn = DrawLiquidFuel(liquidFuel, want);
                FuelBuffer += drawn;
            }
        }
    }
}
