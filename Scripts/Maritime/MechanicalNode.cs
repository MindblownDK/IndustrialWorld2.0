// Assets/Scripts/VoxelEngine/Maritime/MechanicalNode.cs
//
// Blittable, Burst-compatible snapshot of one block in the mechanical
// network. The MaritimePropulsionSystem fills a NativeArray of these from
// the live GridBlock components once (on graph rebuild) and then the jobs
// mutate the array every FixedUpdate — no managed objects touch the hot path.

using System;
using Unity.Mathematics;

namespace VoxelEngine.Maritime
{
    /// <summary>Flag bits packed into <see cref="MechanicalNode.Flags"/>.</summary>
    public static class MechanicalFlags
    {
        /// <summary>Engine is boosted by a turbocharger (×1.40 torque).</summary>
        public const byte TurboBoosted = 0x01;
        /// <summary>Shaft is severed / block disabled — torque cannot cross it.</summary>
        public const byte Broken = 0x02;
        /// <summary>This engine is a Giant Diesel (turbocharger target).</summary>
        public const byte GiantDiesel = 0x04;
        /// <summary>Placed stationary on land (not riding a moving grid).</summary>
        public const byte Stationary = 0x08;
    }

    /// <summary>
    /// A single connection in the cached propulsion chain: torque flows
    /// <c>From</c> (source side) → <c>To</c> (load side). Kept for diagnostics
    /// and future per-edge gearbox modelling.
    /// </summary>
    [Serializable]
    public struct MechanicalEdge
    {
        public int From;
        public int To;
        public int ChainIndex;
    }

    /// <summary>
    /// Complete per-block record for the maritime simulation. Everything the
    /// propagation + buoyancy jobs need is here; nothing is read from
    /// Transform / MonoBehaviour during the job.
    /// </summary>
    [Serializable]
    public struct MechanicalNode
    {
        // ── Identity & topology ──────────────────────────────────────
        /// <summary>Stable index into the flat node array (== array slot).</summary>
        public int Id;
        public MechanicalNodeType Type;
        /// <summary>Connected-component id (0 = standalone). Nodes of one chain share this.</summary>
        public int ChainIndex;
        /// <summary>BFS parent within the chain (-1 = chain source / no parent). Lets the
        /// propagation job evaluate each node from its actual upstream neighbour so branched
        /// drivetrains route torque/RPM correctly and a gearbox works from ANY input side.</summary>
        public int ParentIndex;
        public byte Flags;

        // ── Geometry (world space, captured at rebuild) ──────────────
        /// <summary>World-space block centre — buoyancy + thrust application point.</summary>
        public float3 WorldPosition;
        /// <summary>Vertical extent of the block (m) — used for submergence sampling.</summary>
        public float BlockHeight;
        /// <summary>World-space, normalised direction this propeller pushes the ship.</summary>
        public float3 WorldThrustAxis;
        /// <summary>World-space block up — used for waterwheel paddle orientation.</summary>
        public float3 UpAxis;

        // ── Buoyancy / mass ──────────────────────────────────────────
        /// <summary>Block structural mass (kg).</summary>
        public float Mass;
        /// <summary>Displaced water volume when fully submerged (m³).</summary>
        public float Volume;
        /// <summary>0..1 buoyancy effectiveness. 0 = iron hull (needs air pockets),
        /// 1 = fully buoyant wood. Applied on top of Archimedes displacement.</summary>
        public float BuoyancyFactor;

        // ── Engine / wheel (torque sources) ──────────────────────────
        /// <summary>Maximum torque output (N·m) before turbo boost.</summary>
        public float MaxTorque;
        /// <summary>Maximum rotational speed (rev/min).</summary>
        public float MaxRPM;
        /// <summary>Current RPM — written by the propagation job, read by buoyancy.</summary>
        public float CurrentRPM;
        /// <summary>0..1 actual fuel/power authority delivered this tick. &lt;=0 ⇒ engine off / shaft dead.</summary>
        public float FuelAvailable01;
        /// <summary>0..1 requested electrical authority. Electrical propellers retain this separately so the grid can bill commanded demand while thrust uses delivered power.</summary>
        public float PowerCommand01;

        // ── Gearbox ──────────────────────────────────────────────────
        /// <summary>Player-selected speed multiplier (≥1 = faster, less torque). 1 = direct shaft.</summary>
        public float GearRatio;
        /// <summary>Actual ratio after the output RPM governor/clamp is applied.</summary>
        public float AppliedGearRatio;
        /// <summary>Hard RPM clamp for this gearbox (stops runaway gearing).</summary>
        public float MaxGearSpeed;
        /// <summary>Per-node shaft torque arriving at this node after upstream transforms
        /// (computed by the propagation job; N·m on the shared bus at this point).</summary>
        public float ShaftTorque;
        /// <summary>Per-node shaft RPM arriving at this node after upstream transforms.</summary>
        public float ShaftRpm;
        /// <summary>Extra output multiplier for consumers (upgrade modules on generators).</summary>
        public float OutputMultiplier;
        /// <summary>Generator rated electrical output after live module configuration (W).</summary>
        public float RatedElectricalOutputWatts;
        /// <summary>Electrical output requested by the generator at its current shaft speed (W).</summary>
        public float RequestedElectricalWatts;
        /// <summary>Downstream mechanical torque demand accumulated at this node (N·m).</summary>
        public float MechanicalLoadTorque;
        /// <summary>Demand divided by locally available torque. Values above one mean overload.</summary>
        public float MechanicalLoadRatio;
        /// <summary>0..1 drivetrain service after all connected mechanical loads are resolved.</summary>
        public float DriveService01;

        // ── Propeller / wheel consumers ──────────────────────────────
        /// <summary>Size multiplier (1× small, 3× large, etc.).</summary>
        public float PropellerSize;
        /// <summary>Tunable N per (RPM·submergence·size) — keeps thrust in a sane range.</summary>
        public float ThrustCoefficient;

        // ── Water environment (sampled per tick) ─────────────────────
        /// <summary>World-space water flow velocity at this block (m/s). Waterwheel input.</summary>
        public float3 WaterFlowVelocity;

        // ── Computed outputs (written by jobs) ───────────────────────
        /// <summary>0..1 fraction of the block currently below the water surface.</summary>
        public float Submergence;
        /// <summary>Resultant world-space force (N) contributed by this block.</summary>
        public float3 ComputedForce;
        /// <summary>Resultant world-space torque (N·m) about the grid centre.</summary>
        public float3 ComputedTorque;
        /// <summary>Electricity produced (W) — generator output / electric-propeller demand.</summary>
        public float ElectricityOutput;
        public float ElectricityDemand;

        // ── Convenience flag accessors (Burst-inlined) ───────────────
        public bool IsTurboBoosted  => (Flags & MechanicalFlags.TurboBoosted) != 0;
        public bool IsBroken        => (Flags & MechanicalFlags.Broken) != 0;
        public bool IsGiantDiesel   => (Flags & MechanicalFlags.GiantDiesel) != 0;
        public bool IsStationary    => (Flags & MechanicalFlags.Stationary) != 0;

        /// <summary>Set one or more flag bits. Safe for byte (avoids the
        /// constant-overflow compile error of inline ~(flag)).</summary>
        public void SetFlag(byte flag) => Flags |= flag;

        /// <summary>Clear one or more flag bits.</summary>
        public void ClearFlag(byte flag) => Flags &= (byte)(~flag);

        /// <summary>True for blocks that act as a torque source.</summary>
        public bool IsProducer =>
            Type == MechanicalNodeType.Engine ||
            Type == MechanicalNodeType.Waterwheel;

        /// <summary>True for blocks that consume torque to make motion/power.</summary>
        public bool IsConsumer =>
            Type == MechanicalNodeType.Propeller ||
            Type == MechanicalNodeType.ElectricalPropeller ||
            Type == MechanicalNodeType.Generator ||
            Type == MechanicalNodeType.Waterwheel;

        /// <summary>True for blocks that merely conduct torque.</summary>
        public bool IsConduit =>
            Type == MechanicalNodeType.Shaft ||
            Type == MechanicalNodeType.Gearbox;

        /// <summary>Turbocharger boost applied to a Giant Diesel's torque (×1.40).</summary>
        public const float TurboBoost = 1.40f;
    }
}
