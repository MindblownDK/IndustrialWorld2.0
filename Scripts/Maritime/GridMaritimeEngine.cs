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
    /// <summary>Engine size tier — drives torque, RPM, fuel type, mass and turbo slots.</summary>
    public enum EngineTier : byte
    {
        /// <summary>1×2×1 — burns wood/coal (solid). 1 small turbo slot.</summary>
        Small = 0,
        /// <summary>2×3×2 — burns Heavy Fuel Oil. 2 turbo slots (small or large).</summary>
        Medium = 1,
        /// <summary>4×5×6 — burns Marine Gas Oil. 4 turbo slots. Massive.</summary>
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

        [Header("Exhaust Gas")]
        [Tooltip("Maximum exhaust gas backlog before the engine chokes and stops.")]
        public float exhaustGasCapacity = 100f;
        [Tooltip("Exhaust gas produced per second at full throttle.")]
        public float exhaustGasRate = 8f;
        [Tooltip("Exhaust gas vented per second through an adjacent Exhaust Pipe.")]
        public float exhaustVentRate = 12f;
        [Tooltip("At this fill ratio (0..1) the engine starts losing power from back-pressure.")]
        [Range(0.5f, 0.99f)] public float exhaustChokeThreshold = 0.8f;

        [Header("State (read-only)")]
        /// <summary>Current fuel buffer level (0..capacity).</summary>
        public float FuelBuffer { get; private set; }

        /// <summary>0..1 fill ratio of the internal fuel buffer.</summary>
        public float FuelFill01 => fuelBufferCapacity > 0f ? Mathf.Clamp01(FuelBuffer / fuelBufferCapacity) : 0f;

        /// <summary>Current exhaust gas backlog (0..capacity).</summary>
        public float ExhaustGas { get; private set; }

        /// <summary>0..1 fill ratio of the exhaust gas backlog.</summary>
        public float ExhaustFill01 => exhaustGasCapacity > 0f ? Mathf.Clamp01(ExhaustGas / exhaustGasCapacity) : 0f;

        /// <summary>True while the engine is actively producing torque.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>True if exhaust gas backlog is critically high (engine losing power).</summary>
        public bool IsChoked => ExhaustFill01 >= exhaustChokeThreshold;

        /// <summary>True if an exhaust pipe is adjacent (otherwise the engine chokes).</summary>
        public bool HasExhaust { get; private set; }

        /// <summary>Current RPM (written back by ApplyResults).</summary>
        public float CurrentRPM { get; private set; }

        /// <summary>Current litres/s fuel consumption (for UI).</summary>
        public float CurrentUsage { get; private set; }

        /// <summary>Current torque output (for UI).</summary>
        public float CurrentTorque { get; private set; }

        /// <summary>0..1 stress level (torque vs max, with exhaust penalty).</summary>
        public float Stress01 { get; private set; }

        /// <summary>True when the engine is overstressed (torque demand exceeds safe limits).</summary>
        public bool IsOverstressed => Stress01 > 0.95f;

        /// <summary>Number of turbochargers connected to this engine (for UI).</summary>
        public int ConnectedTurboCount { get; private set; }
        /// <summary>Total turbo boost multiplier (1.0 = none, 1.4 = one small, etc.).</summary>
        public float TurboBoostTotal { get; private set; }
        /// <summary>Max turbo slots this engine supports.</summary>
        public int MaxTurboSlots => tier switch
        {
            EngineTier.Small  => 1,
            EngineTier.Medium => 2,
            EngineTier.Giant  => 4,
            _ => 0,
        };

        public override float ContentMass
        {
            get
            {
                float m = 0f;
                if (fuelKind == MaritimeFuelKind.Liquid)
                    m += FuelBuffer * liquidFuel.DensityKgPerL();
                // Exhaust gas adds mass too (compressed gas is heavy).
                m += ExhaustGas * 0.01f;
                return m;
            }
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            // Auto-configure based on tier.
            switch (tier)
            {
                case EngineTier.Small:
                    blockName = "Crude Engine";
                    fuelKind = MaritimeFuelKind.Solid;
                    fuelBufferCapacity = 60f;
                    fuelConsumptionRate = 1f;
                    break;
                case EngineTier.Medium:
                    blockName = "Heavy Fuel Oil Engine";
                    fuelKind = MaritimeFuelKind.Liquid;
                    liquidFuel = LiquidType.HeavyFuelOil;
                    fuelBufferCapacity = 80f;
                    fuelConsumptionRate = 2f;
                    liquidRefillRate = 8f;
                    break;
                case EngineTier.Giant:
                    blockName = "MGO Engine";
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
                node.SetFlag(MechanicalFlags.GiantDiesel);
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            float dt = Time.fixedDeltaTime;

            // Exhaust check — need an exhaust pipe adjacent to vent gas.
            HasExhaust = HasAdjacentExhaust();

            // ── Exhaust gas accumulation ────────────────────────────────
            // Gas builds up while running; vents through an adjacent exhaust pipe.
            if (IsRunning)
            {
                ExhaustGas = Mathf.Min(exhaustGasCapacity, ExhaustGas + exhaustGasRate * throttle * dt);
            }
            if (HasExhaust)
            {
                ExhaustGas = Mathf.Max(0f, ExhaustGas - exhaustVentRate * dt);
            }

            // ── Engine running conditions ───────────────────────────────
            bool exhaustChoked = ExhaustFill01 >= 0.99f;

            if (!Enabled || !HasExhaust || exhaustChoked)
            {
                node.FuelAvailable01 = 0f;
                IsRunning = false;
                CurrentUsage = 0f;
                node.SetFlag(MechanicalFlags.Broken);
                return;
            }
            node.ClearFlag(MechanicalFlags.Broken);

            // Consume fuel from the internal buffer.
            float consumption = fuelConsumptionRate * throttle * dt;
            FuelBuffer = Mathf.Max(0f, FuelBuffer - consumption);
            CurrentUsage = fuelConsumptionRate * throttle;

            // Refill from grid storage.
            RefillBuffer(dt);

            // Exhaust back-pressure reduces power.
            float exhaustPenalty = 1f;
            if (IsChoked)
            {
                float overChoke = (ExhaustFill01 - exhaustChokeThreshold) / (1f - exhaustChokeThreshold);
                exhaustPenalty = 1f - overChoke * 0.7f; // lose up to 70% power near full
            }

            IsRunning = FuelBuffer > 0.01f && throttle > 0.01f;
            float effectiveFuel = IsRunning ? FuelFill01 * throttle * exhaustPenalty : 0f;

            // Count connected turbos and apply stacked boost to the torque.
            CountTurbos();
            node.MaxTorque = maxTorque * TurboBoostTotal;

            node.FuelAvailable01 = effectiveFuel;

            // Stress = how hard we're pushing relative to max.
            CurrentTorque = node.MaxTorque * effectiveFuel;
            Stress01 = Mathf.Clamp01(effectiveFuel * (1f + ExhaustFill01 * 0.3f));
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            CurrentRPM = node.CurrentRPM;
        }

        /// <summary>Scan 6 neighbours for turbochargers; compute stacked boost.</summary>
        private void CountTurbos()
        {
            ConnectedTurboCount = 0;
            float boost = 1f;

            if (Grid != null)
            {
                var faces = new[]
                {
                    new Vector3Int( 1,0,0), new(-1,0,0),
                    new( 0,1,0), new( 0,-1,0),
                    new( 0,0,1), new( 0,0,-1),
                };
                foreach (var off in faces)
                {
                    if (Grid.GetBlock(GridPos + off) is GridTurbocharger tc)
                    {
                        ConnectedTurboCount++;
                        boost += tc.tier == TurboTier.Large ? 0.25f : 0.15f;
                    }
                }
                ConnectedTurboCount = Mathf.Min(ConnectedTurboCount, MaxTurboSlots);
            }
            TurboBoostTotal = boost;
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
