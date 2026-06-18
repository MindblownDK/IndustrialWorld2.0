// Assets/Scripts/VoxelEngine/Maritime/MechanicalPropagationJob.cs
//
// Burst-compiled torque propagation across the cached propulsion chains.
//
//   • Runs as IJobParallelFor over CHAINS (each chain is independent).
//   • Within one chain it sweeps serially:  source → shafts → gearbox → consumer,
//     applying turbo boost, gear ratios, broken-shaft cutoff and generator load.
//   • Writes CurrentRPM + ElectricityOutput back into the node array so the
//     subsequent BuoyancyJob can convert RPM into thrust.
//
// Zero GC, zero managed calls, Burst-friendly. The "Component-Driven Network
// Graph" the design specifies: the heavy graph walk happens once (at rebuild)
// and this job only ever touches a flat struct array.

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngine.Maritime
{
    /// <summary>A contiguous slice of the node array forming one propulsion chain.</summary>
    public struct PropulsionChain
    {
        /// <summary>First node index (inclusive).</summary>
        public int StartIndex;
        /// <summary>Number of nodes in this chain.</summary>
        public int Length;
        /// <summary>Index of the torque source, or -1 if the chain has no live source.</summary>
        public int SourceIndex;
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public struct MechanicalPropagationJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<MechanicalNode> Nodes;

        [ReadOnly] public NativeArray<PropulsionChain> Chains;

        // Tunables copied from MaritimeSettings (blittable — no SO in the job).
        public float RpmResponse;
        public float GeneratorEfficiency;
        public float GlobalGearSpeedCap;
        public float WheelFlowTorque;

        // ω = rpm × 2π/60. Hardcoded literal because math.PI2 is static-readonly
        // (not a compile-time const) and cannot be used in a const expression.
        private const float RPM_TO_RAD_PER_SEC = 0.10471975512f;

        public void Execute(int chainIndex)
        {
            var chain = Chains[chainIndex];
            if (chain.Length <= 0) return;

            int end = chain.StartIndex + chain.Length;

            // ── Determine / compute all live torque sources ─────────────
            // Any engine/waterwheel in the connected mechanical component can feed
            // the shared shaft bus. This lets players join multiple engines into one
            // Rotation Transfer / Encased Chain Drive and have their torque combine,
            // regardless of which side they used as the physical input.
            float torque = 0f;
            float rpmWeighted = 0f;
            float rpmMax = 0f;
            bool haveSource = false;

            for (int i = chain.StartIndex; i < end; i++)
            {
                var node = Nodes[i];
                node.ElectricityOutput = 0f;
                node.ElectricityDemand = 0f;

                bool producer = node.Type == MechanicalNodeType.Engine || node.Type == MechanicalNodeType.Waterwheel;
                if (!producer || node.IsBroken || node.FuelAvailable01 <= 0.0001f)
                {
                    Nodes[i] = node;
                    continue;
                }

                float sourceTorque = node.MaxTorque * node.FuelAvailable01;
                float sourceRpm = node.MaxRPM * node.FuelAvailable01 * RpmResponse;

                if (node.Type == MechanicalNodeType.Waterwheel)
                {
                    float flowSpeed = math.length(node.WaterFlowVelocity);
                    sourceTorque = WheelFlowTorque * flowSpeed * node.FuelAvailable01;
                    sourceRpm = node.MaxRPM * math.saturate(flowSpeed * 0.5f);
                }

                node.CurrentRPM = sourceRpm;
                Nodes[i] = node;

                torque += sourceTorque;
                rpmWeighted += sourceRpm * math.max(0.0001f, sourceTorque);
                rpmMax = math.max(rpmMax, sourceRpm);
                haveSource = true;
            }

            float rpm = torque > 0.0001f ? rpmWeighted / torque : rpmMax;

            // If there's no live source the whole chain is idle — zero every node's RPM.
            if (!haveSource)
            {
                for (int i = chain.StartIndex; i < end; i++)
                {
                    var n = Nodes[i];
                    n.CurrentRPM = 0f;
                    n.ElectricityOutput = 0f;
                    n.ElectricityDemand = 0f;
                    Nodes[i] = n;
                }
                return;
            }

            // ── Propagate torque + RPM through the rest of the chain ──────
            for (int i = chain.StartIndex; i < end; i++)
            {
                var node = Nodes[i];
                if (node.Type == MechanicalNodeType.Engine) continue;

                switch (node.Type)
                {
                    case MechanicalNodeType.Shaft:
                        if (node.IsBroken) { torque = 0f; rpm = 0f; } // severed → chain dies downstream
                        node.CurrentRPM = rpm;
                        break;

                    case MechanicalNodeType.Gearbox:
                    {
                        // Speed up (or down): rpm scales, torque trades inversely (power ≈ conserved).
                        float gr = math.max(0.01f, node.GearRatio);
                        rpm = rpm * gr;
                        rpm = math.min(rpm, math.min(node.MaxGearSpeed, GlobalGearSpeedCap));
                        torque = torque / gr;
                        node.CurrentRPM = rpm;
                        break;
                    }

                    case MechanicalNodeType.Propeller:
                    case MechanicalNodeType.Waterwheel:
                        // Torque consumers that turn RPM into thrust (computed in BuoyancyJob).
                        node.CurrentRPM = rpm;
                        break;

                    case MechanicalNodeType.Generator:
                    {
                        // Mechanical → electrical. P = τ·ω, scaled by efficiency.
                        float omega = rpm * RPM_TO_RAD_PER_SEC;
                        node.ElectricityOutput = torque * omega * GeneratorEfficiency;
                        node.CurrentRPM = rpm;
                        // A generator is a load sink: it consumes the remaining shaft torque.
                        torque = 0f;
                        break;
                    }

                    case MechanicalNodeType.ElectricalPropeller:
                        // Driven by electricity, not by shaft torque — handled in BuoyancyJob.
                        node.CurrentRPM = 0f;
                        node.ElectricityDemand = node.MaxTorque; // used as a watt demand proxy
                        break;

                    default:
                        node.CurrentRPM = 0f;
                        break;
                }

                Nodes[i] = node;
            }
        }
    }
}
