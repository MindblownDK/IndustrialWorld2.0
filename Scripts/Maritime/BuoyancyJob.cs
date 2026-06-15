// Assets/Scripts/VoxelEngine/Maritime/BuoyancyJob.cs
//
// Burst-compiled per-block buoyancy + thrust computation.
//
//   • Runs as IJobParallelFor over EVERY node (embarrassingly parallel).
//   • Reads pre-sampled wave heights (WaterProbeSystem) and the RPM delivered
//     by MechanicalPropagationJob, then writes:
//       – Submergence (0..1)
//       – ComputedForce  (world-space N at this block)
//       – ComputedTorque (world-space N·m about the grid centre of mass)
//   • Buoyancy uses Archimedes:  F = ρ_water · g · V_submerged · buoyancyFactor.
//   • Propeller thrust:  Thrust = RPM · Submergence · Size · ThrustCoeff.
//   • Hull drag opposes the block's local water-relative velocity.
//   • Righting moment emerges naturally from cross(r, F_buoy) — no fake stabiliser.
//
// The orchestrator sums all ComputedForce / ComputedTorque into one resultant
// Vector3 pair and applies them to the ship's Rigidbody in FixedUpdate.

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngine.Maritime
{
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public struct BuoyancyJob : IJobParallelFor
    {
        public NativeArray<MechanicalNode> Nodes;

        [ReadOnly] public NativeArray<float> WaterHeights;

        // ── Grid transform / motion ───────────────────────────────────
        public float3 GridCenter;
        public float3 GridLinearVelocity;
        public float3 GridAngularVelocity;
        public float3 WorldUp;

        // ── Tunables (copied from MaritimeSettings) ───────────────────
        public float Gravity;
        public float WaterDensity;
        public float BuoyancyGain;
        public float WaterDrag;
        public float ThrustCoefficient;
        public float CavitationLoss;
        public float WheelPaddleThrust;

        public void Execute(int i)
        {
            var node = Nodes[i];

            // ── Submergence ────────────────────────────────────────────
            // BlockHeight is precomputed by the orchestrator (avoids cube-root in the job).
            float blockHeight = node.BlockHeight;
            float topY    = node.WorldPosition.y + blockHeight * 0.5f;
            float bottomY = node.WorldPosition.y - blockHeight * 0.5f;
            float wh = WaterHeights[i];

            float submergence = 0f;
            if (wh > bottomY)
                submergence = math.saturate((wh - bottomY) / math.max(blockHeight, 1e-6f));
            node.Submergence = submergence;

            float3 force = float3.zero;
            float3 r = node.WorldPosition - GridCenter; // lever arm about CoM

            // ── Buoyancy (Archimedes) ──────────────────────────────────
            if (submergence > 0f && node.Volume > 0f)
            {
                float vSub = node.Volume * submergence;
                float fBuoy = WaterDensity * Gravity * vSub * node.BuoyancyFactor * BuoyancyGain;
                force += WorldUp * fBuoy;
            }

            // ── Propeller thrust ───────────────────────────────────────
            if (node.Type == MechanicalNodeType.Propeller && submergence > 0f && node.CurrentRPM > 0f)
            {
                // Cavitation: large fast props in shallow water lose efficiency.
                float cavitation = 1f - CavitationLoss * math.saturate(node.PropellerSize - 1f) * (1f - submergence);
                float thrust = node.CurrentRPM * submergence * node.PropellerSize * ThrustCoefficient * cavitation;
                force += node.WorldThrustAxis * thrust;
            }

            // ── Waterwheel paddle thrust (shaft-driven) ────────────────
            if (node.Type == MechanicalNodeType.Waterwheel && node.CurrentRPM > 0f && submergence > 0f)
            {
                float thrust = node.CurrentRPM * submergence * node.PropellerSize * WheelPaddleThrust;
                force += node.WorldThrustAxis * thrust;
            }

            // ── Electrical propeller (electricity-driven, fast spin-up) ─
            if (node.Type == MechanicalNodeType.ElectricalPropeller && submergence > 0f)
            {
                // Spin-up modelled as fast response to fuel-authority (electric supply).
                float thrust = node.MaxRPM * node.FuelAvailable01 * submergence *
                               node.PropellerSize * ThrustCoefficient;
                force += node.WorldThrustAxis * thrust;
            }

            // ── Hull drag (opposes local water-relative velocity) ──────
            if (submergence > 0f)
            {
                float3 localVel = GridLinearVelocity + math.cross(GridAngularVelocity, r);
                float speed = math.length(localVel);
                if (speed > 1e-4f)
                {
                    // Drag scales with submerged volume (bigger hull = more resistance).
                    float dragMag = WaterDrag * submergence * node.Volume * speed;
                    force += (-localVel / speed) * dragMag;
                }
            }

            // ── Accumulate torque about CoM (cross product of lever & force) ──
            float3 torque = math.cross(r, force);

            node.ComputedForce = force;
            node.ComputedTorque = torque;
            Nodes[i] = node;
        }
    }
}
