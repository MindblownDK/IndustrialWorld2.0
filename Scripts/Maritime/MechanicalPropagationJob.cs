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
// v9.56.0-dev —
//   • Giant Diesel (950k Nm, 1200 RPM) to generator calculations fixed: generator
//     wanted watts now scales as rated * speed01 * (1+bonus*speed01) giving
//     +50% at rated speed, ~62% at half speed, so direct 1200->2400 still yields
//     useful power and a 2:1 gearbox yields full rated+bonus.
//   • Large maritime engine tier now correctly handled: bus torque includes
//     Giant torque curve, generator load uses same conversion, service01 shared.

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngine.Maritime
{
    public struct PropulsionChain
    {
        public int StartIndex;
        public int Length;
        public int SourceIndex;
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public struct MechanicalPropagationJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<MechanicalNode> Nodes;

        [ReadOnly] public NativeArray<PropulsionChain> Chains;

        public float RpmResponse;
        public float GeneratorEfficiency;
        public float GlobalGearSpeedCap;
        public float WheelFlowTorque;
        public float GeneratorSpeedBonus;

        private const float RPM_TO_RAD_PER_SEC = 0.10471975512f;

        public void Execute(int chainIndex)
        {
            var chain = Chains[chainIndex];
            if (chain.Length <= 0) return;

            int end = chain.StartIndex + chain.Length;

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

        private void ResolveMechanicalLoads(PropulsionChain chain, int end, float busTorque)
        {
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
                    // v9.56: Giant Diesel fix — previously speedCurve = speed01*(1+bonus*speed01)/(1+bonus)
                    // gave only 41% at half speed (1200 RPM engine -> 2400 RPM gen). New curve:
                    // wanted = rated * speed01 * (1+bonus*speed01) gives 62.5% at half speed and
                    // +50% at rated, so large engine direct-drive still useful and 2:1 gearbox yields full bonus.
                    float speedCurve = speed01 * (1f + GeneratorSpeedBonus * speed01);
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

            float rootDemandTorque = 0f;
            for (int i = end - 1; i >= chain.StartIndex; i--)
            {
                var node = Nodes[i];
                float available = node.ShaftTorque;
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
                    node.ShaftTorque = 0f;
                }

                if (node.Type == MechanicalNodeType.Engine || node.Type == MechanicalNodeType.Waterwheel)
                    node.MechanicalLoadRatio = rawSourceLoad;

                Nodes[i] = node;
            }
        }
    }
}
