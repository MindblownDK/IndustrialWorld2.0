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
            // Every engine/waterwheel on the connected bus contributes its
            // available torque. Mechanical loads are resolved afterwards and then
            // shared back across those sources as one real drivetrain service level.
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
                node.RequestedElectricalWatts = 0f;
                node.MechanicalLoadTorque = 0f;
                if (node.Type == MechanicalNodeType.Gearbox)
                    node.AppliedGearRatio = math.max(0.01f, node.GearRatio);
                node.MechanicalLoadRatio = 0f;
                node.DriveService01 = 1f;

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
                    n.RequestedElectricalWatts = 0f;
                    n.MechanicalLoadTorque = 0f;
                    n.MechanicalLoadRatio = 0f;
                    n.DriveService01 = 0f;
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

            // ── Forward propagation: rated torque and speed at every node ──
            // Nodes are stored in BFS order from the source, so a parent is always
            // evaluated before its children in this pass.
            for (int i = chain.StartIndex; i < end; i++)
            {
                var node = Nodes[i];

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
                        node.ShaftTorque = inTorque;
                        node.ShaftRpm = inRpm;
                        node.CurrentRPM = inRpm;
                        break;

                    case MechanicalNodeType.Shaft:
                        if (node.IsBroken) { inTorque = 0f; inRpm = 0f; }
                        node.ShaftTorque = inTorque;
                        node.ShaftRpm = inRpm;
                        node.CurrentRPM = inRpm;
                        break;

                    case MechanicalNodeType.Gearbox:
                    {
                        float selectedRatio = math.max(0.01f, node.GearRatio);
                        float outRpm = math.min(inRpm * selectedRatio, math.min(node.MaxGearSpeed, GlobalGearSpeedCap));
                        // If the RPM governor clamps a high selected gear, torque must
                        // use the *actual* speed ratio or the drivetrain would destroy
                        // power mathematically and report false overloads.
                        float actualRatio = inRpm > 0.01f ? math.max(0.01f, outRpm / inRpm) : selectedRatio;
                        node.AppliedGearRatio = actualRatio;
                        node.ShaftRpm = outRpm;
                        node.ShaftTorque = inTorque / actualRatio;
                        node.CurrentRPM = outRpm;
                        break;
                    }

                    case MechanicalNodeType.Propeller:
                    case MechanicalNodeType.Waterwheel:
                        if (node.Type == MechanicalNodeType.Waterwheel && node.ParentIndex < 0)
                        {
                            node.ShaftTorque = inTorque;
                            node.ShaftRpm = inRpm;
                            break;
                        }
                        node.ShaftTorque = inTorque;
                        node.ShaftRpm = inRpm;
                        node.CurrentRPM = inRpm;
                        break;

                    case MechanicalNodeType.Generator:
                        // Generators are evaluated in the backward mechanical-load
                        // pass: their rated electrical target becomes a real torque
                        // request instead of free power from every shaft branch.
                        node.CurrentRPM = inRpm;
                        node.ShaftRpm = inRpm;
                        node.ShaftTorque = 0f;
                        break;

                    case MechanicalNodeType.ElectricalPropeller:
                    {
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

            ResolveMechanicalLoads(chain, end, busTorque);
        }

        /// <summary>
        /// Resolves generator and propeller resistance back toward the torque source.
        /// The prior model calculated generation from whatever torque happened to be
        /// present on a branch, which made every additional generator look free and
        /// left engine stress almost unchanged. This backward pass makes all loads
        /// share one finite mechanical power budget.
        /// </summary>
        private void ResolveMechanicalLoads(PropulsionChain chain, int end, float busTorque)
        {
            // Direct loads at their local shaft. Generator output is speed-limited
            // and then converted through P = torque × omega / efficiency.
            for (int i = chain.StartIndex; i < end; i++)
            {
                var node = Nodes[i];
                node.MechanicalLoadTorque = 0f;
                node.MechanicalLoadRatio = 0f;
                node.RequestedElectricalWatts = 0f;
                node.DriveService01 = 1f;

                if (node.IsBroken)
                {
                    Nodes[i] = node;
                    continue;
                }

                if (node.Type == MechanicalNodeType.Generator)
                {
                    float omega = node.ShaftRpm * RPM_TO_RAD_PER_SEC;
                    float rated = math.max(0f, node.RatedElectricalOutputWatts);
                    float speed01 = math.saturate(node.ShaftRpm / math.max(1f, node.MaxRPM));
                    // A generator has a regulator curve: it cannot make rated watts
                    // at crawl speed, but it does not get an arbitrary speed bonus
                    // above its authored electrical rating either.
                    float speedCurve = speed01 * (1f + GeneratorSpeedBonus * speed01)
                                     / math.max(1f, 1f + GeneratorSpeedBonus);
                    float wantedWatts = rated * speedCurve;
                    float conversion = math.max(0.05f, GeneratorEfficiency);
                    if (omega > 0.01f && wantedWatts > 0.01f)
                    {
                        node.RequestedElectricalWatts = wantedWatts;
                        node.MechanicalLoadTorque = wantedWatts / (omega * conversion);
                    }
                }
                else if (node.Type == MechanicalNodeType.Propeller)
                {
                    float rpm01 = math.saturate(node.ShaftRpm / math.max(1f, node.MaxRPM));
                    float waterAuthority = math.max(0.20f, node.Submergence);
                    node.MechanicalLoadTorque = 850f * math.pow(math.max(1f, node.PropellerSize), 3f)
                        * rpm01 * rpm01 * waterAuthority;
                }
                else if (node.Type == MechanicalNodeType.Waterwheel && node.ParentIndex >= 0)
                {
                    float rpm01 = math.saturate(node.ShaftRpm / math.max(1f, node.MaxRPM));
                    node.MechanicalLoadTorque = 420f * math.pow(math.max(1f, node.PropellerSize), 3f)
                        * rpm01 * rpm01 * math.max(0.20f, node.Submergence);
                }

                Nodes[i] = node;
            }

            // Walk leaves → source. A gearbox transforms output-side demand back
            // through its ratio, exactly like real torque multiplication/reduction.
            float rootDemandTorque = 0f;
            for (int i = end - 1; i >= chain.StartIndex; i--)
            {
                var node = Nodes[i];
                float available = node.ShaftTorque;
                // A generator deliberately stores zero downstream shaft torque because
                // it is a sink, so its load compares against the parent shaft. Gearboxes
                // instead compare against their transformed output torque.
                if (node.Type == MechanicalNodeType.Generator && node.ParentIndex >= chain.StartIndex)
                    available = Nodes[node.ParentIndex].ShaftTorque;
                else if (node.ParentIndex < 0)
                    available = busTorque;

                node.MechanicalLoadRatio = node.MechanicalLoadTorque / math.max(1f, available);
                float demandUpstream = node.MechanicalLoadTorque;
                if (node.Type == MechanicalNodeType.Gearbox)
                    demandUpstream *= math.max(0.01f, node.AppliedGearRatio);

                if (node.ParentIndex >= chain.StartIndex)
                {
                    var parent = Nodes[node.ParentIndex];
                    parent.MechanicalLoadTorque += demandUpstream;
                    Nodes[node.ParentIndex] = parent;
                }
                else
                {
                    rootDemandTorque += demandUpstream;
                }

                Nodes[i] = node;
            }

            float rawSourceLoad = rootDemandTorque / math.max(1f, busTorque);
            float service01 = rootDemandTorque > 0.0001f
                ? math.saturate(busTorque / rootDemandTorque)
                : 1f;
            // Once requested torque exceeds supply the engine bogs rather than
            // pretending to hold perfect RPM under an impossible generator bank.
            float overload01 = math.saturate(rawSourceLoad - 1f);
            float rpmService = math.lerp(1f, 0.58f, overload01);

            for (int i = chain.StartIndex; i < end; i++)
            {
                var node = Nodes[i];
                node.DriveService01 = service01;
                if (node.Type != MechanicalNodeType.ElectricalPropeller)
                {
                    node.ShaftTorque *= service01;
                    node.ShaftRpm *= rpmService;
                    node.CurrentRPM *= rpmService;
                }

                if (node.Type == MechanicalNodeType.Generator)
                {
                    node.ElectricityOutput = node.RequestedElectricalWatts * service01 * rpmService;
                    node.ShaftTorque = 0f; // load sink: no shaft torque beyond it
                }

                // All coupled torque sources share the total bus load in proportion
                // to their available torque, so adding generators raises every engine's
                // reported stress instead of only affecting one arbitrary BFS root.
                if (node.Type == MechanicalNodeType.Engine || node.Type == MechanicalNodeType.Waterwheel)
                    node.MechanicalLoadRatio = rawSourceLoad;

                Nodes[i] = node;
            }
        }

    }
}
