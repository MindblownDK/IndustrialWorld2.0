// Assets/Scripts/VoxelEngine/Maritime/MechanicalPropagationJob.cs
//
// Burst-compiled torque propagation across the cached propulsion chains.
//
//   • Runs as IJobParallelFor over CHAINS (each chain is independent).
//   • Within one chain it evaluates every node from its BFS PARENT (set at
//     rebuild), so branched drivetrains route torque/RPM correctly and a
//     gearbox trades torque for speed no matter which face is the input.
//   • Writes CurrentRPM + ElectricityOutput back into the node array so the
//     subsequent BuoyancyJob can convert RPM into thrust.
//
// v6.10.0-dev —
//   • Tree-aware per-node shaft values (ShaftTorque/ShaftRpm via ParentIndex)
//     replacing the single rolling accumulator: branch splits no longer leak
//     a gearbox ratio into sibling branches, and generators only sink the
//     torque on their own branch.
//   • Generator Speed Bonus: spinning faster toward rated RPM yields up to
//     +50% more electrical output (maxSpeedBonus, read from node.MaxRPM scale).
//   • Generator OutputMultiplier: upgrade-module output bonus, fed from the
//     live block each tick.

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
        /// <summary>Extra generator output at rated RPM (0.5 = +50%).</summary>
        public float GeneratorSpeedBonus;

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
            // Rotation Transfers and direct shaft carriers have their torque combine,
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
                node.ShaftTorque = 0f;
                node.ShaftRpm = 0f;

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

            float busRpm = torque > 0.0001f ? rpmWeighted / torque : rpmMax;
            float busTorque = torque;

            // A shaft chain without an engine/waterwheel is idle, except electrical
            // propellers: they are grid-powered pods and must still report their
            // commanded demand/RPM without a mechanical source on the construct.
            if (!haveSource)
            {
                for (int i = chain.StartIndex; i < end; i++)
                {
                    var n = Nodes[i];
                    n.ElectricityOutput = 0f;
                    n.ShaftTorque = 0f;
                    n.ShaftRpm = 0f;
                    if (n.Type == MechanicalNodeType.ElectricalPropeller)
                    {
                        float command01 = math.saturate(n.PowerCommand01);
                        float delivered01 = math.saturate(n.FuelAvailable01);
                        n.CurrentRPM = n.MaxRPM * delivered01;
                        n.ElectricityDemand = n.MaxTorque * command01;
                    }
                    else
                    {
                        n.CurrentRPM = 0f;
                        n.ElectricityDemand = 0f;
                    }
                    Nodes[i] = n;
                }
                return;
            }

            // ── Propagate the bus through the chain, parent by parent ──
            // Nodes are stored in BFS order from the source, so a parent is always
            // evaluated before its children in this pass.
            for (int i = chain.StartIndex; i < end; i++)
            {
                var node = Nodes[i];

                // Upstream shaft state: sources own the shared bus; everyone else
                // inherits from their BFS parent (their actual physical input side).
                float inTorque;
                float inRpm;
                if (node.ParentIndex < 0)
                {
                    inTorque = busTorque;
                    inRpm = busRpm;
                }
                else
                {
                    var parent = Nodes[node.ParentIndex];
                    inTorque = parent.ShaftTorque;
                    inRpm = parent.ShaftRpm;
                }

                switch (node.Type)
                {
                    case MechanicalNodeType.Engine:
                        // Torque source — feeds the bus, shows its own generated RPM.
                        node.ShaftTorque = inTorque;
                        node.ShaftRpm = inRpm;
                        break;

                    case MechanicalNodeType.Shaft:
                        if (node.IsBroken) { inTorque = 0f; inRpm = 0f; } // severed → branch dies
                        node.ShaftTorque = inTorque;
                        node.ShaftRpm = inRpm;
                        node.CurrentRPM = inRpm;
                        break;

                    case MechanicalNodeType.Gearbox:
                    {
                        // Speed up (or down): rpm scales, torque trades inversely
                        // (power ≈ conserved). Symmetric — whichever face carries the
                        // input, the opposite side carries the transformed output.
                        float gr = math.max(0.01f, node.GearRatio);
                        float outRpm = math.min(inRpm * gr, math.min(node.MaxGearSpeed, GlobalGearSpeedCap));
                        node.ShaftRpm = outRpm;
                        node.ShaftTorque = inTorque / gr;
                        node.CurrentRPM = outRpm;
                        break;
                    }

                    case MechanicalNodeType.Propeller:
                    case MechanicalNodeType.Waterwheel:
                        // Torque consumers that turn RPM into thrust (computed in BuoyancyJob).
                        if (node.Type == MechanicalNodeType.Waterwheel && node.ParentIndex < 0)
                        {
                            node.ShaftTorque = inTorque;
                            node.ShaftRpm = inRpm;
                            break; // source-mode waterwheel — own RPM already set above
                        }
                        node.ShaftTorque = inTorque;
                        node.ShaftRpm = inRpm;
                        node.CurrentRPM = inRpm;
                        break;

                    case MechanicalNodeType.Generator:
                    {
                        // Mechanical → electrical. P = τ·ω, scaled by efficiency.
                        // Speed Bonus: the closer the shaft runs to the generator's
                        // rated RPM, the more power it makes (up to +50%).
                        float omega = inRpm * RPM_TO_RAD_PER_SEC;
                        float speedBonus = 1f + GeneratorSpeedBonus * math.saturate(inRpm / math.max(1f, node.MaxRPM));
                        node.ElectricityOutput = inTorque * omega * GeneratorEfficiency * speedBonus * math.max(0.01f, node.OutputMultiplier);
                        node.CurrentRPM = inRpm;
                        node.ShaftRpm = inRpm;
                        // A generator is a load sink: downstream of it gets no torque.
                        node.ShaftTorque = 0f;
                        break;
                    }

                    case MechanicalNodeType.ElectricalPropeller:
                    {
                        // Driven by the grid, not shaft torque. Demand tracks the pilot's
                        // command while RPM/thrust use the actual resolved grid service
                        // fraction supplied on the prior GridEntity power tick.
                        float command01 = math.saturate(node.PowerCommand01);
                        float delivered01 = math.saturate(node.FuelAvailable01);
                        node.CurrentRPM = node.MaxRPM * delivered01;
                        node.ShaftTorque = inTorque;
                        node.ShaftRpm = inRpm;
                        node.ElectricityDemand = node.MaxTorque * command01;
                        break;
                    }

                    default:
                        node.CurrentRPM = 0f;
                        node.ShaftTorque = inTorque;
                        node.ShaftRpm = inRpm;
                        break;
                }

                Nodes[i] = node;
            }
        }
    }
}
