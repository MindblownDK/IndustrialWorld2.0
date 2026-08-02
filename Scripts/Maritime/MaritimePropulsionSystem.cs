// Assets/Scripts/VoxelEngine/Maritime/MaritimePropulsionSystem.cs
//
//  ╔══════════════════════════════════════════════════════════════════╗
//  ║   MARITIME PROPULSION SYSTEM — the orchestrator (Part 1 core)    ║
//  ╚══════════════════════════════════════════════════════════════════╝
//
//  One MonoBehaviour per ship (sits beside GridEntity). It is the ONLY
//  MonoBehaviour in the maritime stack — every per-block update loop is gone:
//
//     [REBUILD]  (only when the ship is built/modified)
//        scan grid blocks → build flat NativeArray<MechanicalNode>
//        + cached PropulsionChain[] (engine→shaft→gearbox→propeller)
//        + turbocharger adjacency flags
//
//     [FixedUpdate]  (every physics step)
//        1. refresh dynamic fields (fuel/throttle/broken) on live blocks
//        2. WaterProbeSystem.GetWavesHeights(...)        ← main thread, cached
//        3. MechanicalPropagationJob  (Burst, per-chain)  → writes RPM
//        4. BuoyancyJob               (Burst, per-node)   → writes force/torque
//        5. sum → AddForce + AddTorque on the Rigidbody
//        6. expose electricity totals (generator/E-propeller)
//
//  Integrates with the existing grid: buoyancy counters GridEntity.ApplyGravity,
//  and rudder torque gives Helm steering. Flying-ship thrusters are untouched.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(-20)] // must run BEFORE GridEntity so generator PowerOutput values are fresh
    public class MaritimePropulsionSystem : MonoBehaviour
    {
        // ── Tuning ────────────────────────────────────────────────────
        [Tooltip("Maritime balance asset. Falls back to runtime defaults if unassigned.")]
        public MaritimeSettings settings;

        // ── Helm / cockpit input ──────────────────────────────────────
        /// <summary>Pilot throttle 0..1 (forward power). Set by the Helm/Cockpit.</summary>
        public float Throttle { get; set; }
        /// <summary>Steer input -1..1 (left/right). Set by the Helm/Cockpit.</summary>
        public float Steer { get; set; }
        /// <summary>True while the helm is actively crewed (enables rudder authority).</summary>
        public bool HelmActive { get; set; }

        // ── Electricity totals (read by Generator/E-Propeller blocks) ─
        public float ElectricityGenerated { get; private set; }
        public float ElectricityDemand { get; private set; }

        // ── References ────────────────────────────────────────────────
        private Rigidbody _rb;
        private GridEntity _grid;
        private float _cellSize = 1f;

        // ── Job data (persistent, rebuilt only on change) ─────────────
        private NativeArray<MechanicalNode> _nodes;
        private NativeArray<float> _waterHeights;
        private NativeArray<float> _waterDensities;
        private NativeArray<PropulsionChain> _chains;
        private bool _allocated;

        // node array index → live block (for per-frame refresh of dynamic fields)
        private readonly List<(int index, IMechanicalBlock block)> _liveMech = new(64);

        // Hull material blocks that can waterlog (tracked for the batched tick).
        private readonly List<(int index, GridHullBlock hull)> _waterlogHulls = new(128);

        // Bilge pumps (drain waterlogged hulls, batched tick).
        private readonly List<GridBilgePump> _bilgePumps = new(8);

        // ── Rebuild trigger ───────────────────────────────────────────
        private int _lastBlockCount = -1;
        private bool _forceRebuild = true;

        // Filled during rebuild (managed, not in jobs) for adjacency math.
        private readonly List<Vector3Int> _nodePositions = new(256);

        private static readonly Vector3Int[] Neighbours6 =
        {
            new( 1, 0, 0), new(-1, 0, 0),
            new( 0, 1, 0), new( 0,-1, 0),
            new( 0, 0, 1), new( 0, 0,-1),
        };

        // ══════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ══════════════════════════════════════════════════════════════
        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _grid = GetComponent<GridEntity>();
            if (settings == null) settings = MaritimeSettings.Default;
            if (_grid != null) _cellSize = _grid.gridSize.CellSize();
            else _cellSize = VoxelEngine.Core.VoxelConstants.VOXEL_SIZE;
        }

        private void OnEnable() => _forceRebuild = true;

        private void OnDestroy()
        {
            DisposeJobData();
        }

        /// <summary>Force a propulsion-graph rebuild next FixedUpdate (call after editing the grid).</summary>
        public void MarkDirty() => _forceRebuild = true;

        // ══════════════════════════════════════════════════════════════
        //  FIXED UPDATE — the whole simulation tick
        // ══════════════════════════════════════════════════════════════
        private void FixedUpdate()
        {
            if (_grid == null || _rb == null) return;

            int count = _grid.BlockCount;
            if (_forceRebuild || count != _lastBlockCount)
            {
                RebuildGraph();
                _lastBlockCount = count;
                _forceRebuild = false;
            }

            if (!_allocated || _nodes.Length == 0) return;

            // 1. Refresh dynamic fields (fuel/throttle/breakage) from live blocks.
            float throttle = Mathf.Clamp01(Throttle);
            for (int i = 0; i < _liveMech.Count; i++)
            {
                var (idx, block) = _liveMech[i];
                if (block == null) { _forceRebuild = true; continue; }
                var n = _nodes[idx];
                block.RefreshMaritimeNode(ref n, throttle);
                _nodes[idx] = n;
            }

            var s = settings;

            // 2. Sample wave heights (main thread, per-column cached).
            for (int i = 0; i < _nodes.Length; i++)
                _nodePosArray[i] = _nodes[i].WorldPosition;
            WaterProbeSystem.GetWavesHeights(_nodePosArray, _waterHeights);
            for (int i = 0; i < _nodes.Length; i++)
            {
                float3 p = _nodePosArray[i];
                _waterDensities[i] = WaterProbeSystem.GetSubmergence(new Vector3(p.x, p.y, p.z), _nodes[i].BlockHeight * 0.5f);
            }

            // 3. Propagation job (per-chain) → writes CurrentRPM.
            var propJob = new MechanicalPropagationJob
            {
                Nodes = _nodes,
                Chains = _chains,
                RpmResponse = s.rpmResponse,
                GeneratorEfficiency = s.generatorEfficiency,
                GlobalGearSpeedCap = s.globalGearSpeedCap,
                WheelFlowTorque = s.wheelFlowTorque,
                GeneratorSpeedBonus = s.generatorSpeedBonus,
            };
            JobHandle propHandle = propJob.Schedule(_chains.Length, 1);

            // 4. Buoyancy job (per-node, depends on propagation) → writes force/torque.
            float gravity = CurrentGravityMagnitude();
            var buoyJob = new BuoyancyJob
            {
                Nodes = _nodes,
                WaterHeights = _waterHeights,
                WaterDensities = _waterDensities,
                GridCenter = _rb.worldCenterOfMass,
                GridLinearVelocity = _rb.linearVelocity,
                GridAngularVelocity = _rb.angularVelocity,
                WorldUp = VoxelEngine.WaterSim.PlanetWaterUtility.ToFloat3(VoxelEngine.WaterSim.PlanetWaterUtility.WorldUp(_rb.worldCenterOfMass)),
                Gravity = gravity,
                WaterDensity = s.waterDensity,
                BuoyancyGain = s.buoyancyGain,
                WaterDrag = s.waterDrag,
                ThrustCoefficient = s.thrustCoefficient,
                CavitationLoss = s.cavitationLoss,
                WheelPaddleThrust = s.wheelPaddleThrust,
            };
            JobHandle buoyHandle = buoyJob.Schedule(_nodes.Length, 32, propHandle);
            buoyHandle.Complete();

            // 4b. Write back computed results (RPM, electricity, submergence) to live blocks
            //     so generators / UI / audio can read them this frame.
            for (int i = 0; i < _liveMech.Count; i++)
            {
                var (idx, block) = _liveMech[i];
                if (block == null) continue;
                block.ApplyResults(_nodes[idx]);
            }

            // 4c. Waterlogging tick — hull blocks absorb water while submerged,
            //     bilge pumps drain it. Batched (not per-block MonoBehaviour).
            TickWaterlogging();

            // 5. Sum forces + torques and apply to the Rigidbody.
            float3 totalForce = float3.zero;
            float3 totalTorque = float3.zero;
            float elecGen = 0f, elecDemand = 0f;
            for (int i = 0; i < _nodes.Length; i++)
            {
                var n = _nodes[i];
                totalForce += n.ComputedForce;
                totalTorque += n.ComputedTorque;
                elecGen += n.ElectricityOutput;
                elecDemand += n.ElectricityDemand;
            }
            ElectricityGenerated = elecGen;
            ElectricityDemand = elecDemand;

            // Rudder steering torque (only when the helm is crewed and moving).
            if (HelmActive)
            {
                Vector3 forward = transform.forward;
                float fwdSpeed = Vector3.Dot(_rb.linearVelocity, forward);
                float steerAuthority = Mathf.Clamp01((Mathf.Abs(fwdSpeed) - s.rudderMinSpeed) / 3f);
                if (steerAuthority > 0f)
                {
                    Vector3 rudderTorque = transform.up * (Steer * s.rudderTorque * steerAuthority * Mathf.Sign(fwdSpeed));
                    totalTorque += new float3(rudderTorque.x, rudderTorque.y, rudderTorque.z);
                }
            }

            if (math.lengthsq(totalForce) > 1e-6f)
                _rb.AddForce((Vector3)totalForce * s.forceGain, ForceMode.Force);
            if (math.lengthsq(totalTorque) > 1e-6f)
                _rb.AddTorque((Vector3)totalTorque * s.torqueGain, ForceMode.Force);

            WaterProbeSystem.RegisterShipWake(_rb.worldCenterOfMass, _rb.linearVelocity, _grid.BlockCount);
        }

        // Pre-allocated scratch so the per-tick position copy stays GC-free.
        private NativeArray<float3> _nodePosArray;
        private bool _posAllocated;

        // ══════════════════════════════════════════════════════════════
        //  GRAPH REBUILD — runs only when the ship changes
        // ══════════════════════════════════════════════════════════════
        private void RebuildGraph()
        {
            DisposeJobData();
            _liveMech.Clear();
            _waterlogHulls.Clear();
            _bilgePumps.Clear();
            _nodePositions.Clear();

            if (_grid == null || _grid.BlockCount == 0) return;

            var blocks = _grid.Blocks;
            int total = blocks.Count;

            // Temporary managed buffers (we promote to NativeArrays at the end).
            var mechNodes = new List<MechanicalNode>(total);
            var mechPositions = new List<Vector3Int>(total);
            var mechBlocks = new List<GridBlock>(total);
            var hullNodes = new List<MechanicalNode>(total);
            var hullBlockSources = new List<GridHullBlock>(total);
            var posToMechIndex = new Dictionary<Vector3Int, int>(total);
            // Map from a mechanical node's index (in mechNodes) to the live block.
            var mechBlockByIndex = new List<IMechanicalBlock>(total);

            // World-flow sampled once at rebuild for waterwheels; refreshed lightly per tick.
            int nodeId = 0;

            foreach (var kv in blocks)
            {
                Vector3Int gpos = kv.Key;
                var block = kv.Value;
                if (block == null) continue;

                Vector3 worldPos = _grid.GridToWorld(gpos);
                float3 wp = new(worldPos.x, worldPos.y, worldPos.z);

                // Track bilge pumps wherever they are.
                if (block is GridBilgePump pump)
                    _bilgePumps.Add(pump);

                if (block is IMechanicalBlock mech)
                {
                    var node = BaseNode(nodeId, wp, block, mech);
                    mech.PopulateMaritimeNode(ref node);
                    // initial dynamic refresh
                    mech.RefreshMaritimeNode(ref node, Throttle);
                    // Sample water flow for waterwheels (refreshed per-tick below).
                    node.WaterFlowVelocity = WaterProbeSystem.GetWaterFlow(wp);

                    mechNodes.Add(node);
                    mechPositions.Add(gpos);
                    mechBlocks.Add(block);
                    posToMechIndex[gpos] = mechNodes.Count - 1;
                    mechBlockByIndex.Add(mech);
                }
                else
                {
                    // Non-mechanical block → pure buoyancy hull node.
                    hullNodes.Add(HullNode(nodeId, wp, block));
                    hullBlockSources.Add(block as GridHullBlock);
                }
                nodeId++;
            }

            // ── Build propulsion chains via BFS over mechanical 6-neighbours ──
            var chains = BuildChains(mechNodes, mechPositions, mechBlocks, posToMechIndex);

            // Note: turbocharger boost is now computed per-engine in RefreshMaritimeNode
            // (CountTurbos scans for adjacent turbochargers each tick).

            // ── Final ordering: chains first (contiguous), then hull nodes ──
            int finalCount = mechNodes.Count + hullNodes.Count;
            if (finalCount == 0) return;

            var finalNodes = new NativeArray<MechanicalNode>(finalCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            var finalPositions = new Vector3Int[finalCount];
            int write = 0;

            // Write chain nodes in chain order so each chain is a contiguous slice.
            var liveMap = new Dictionary<int, IMechanicalBlock>(); // oldIndex → block
            for (int i = 0; i < mechBlockByIndex.Count; i++) liveMap[i] = mechBlockByIndex[i];
            var newIndexByOld = new Dictionary<int, int>(mechNodes.Count);

            foreach (var chain in chains.chainNodeOrder)
            {
                var n = mechNodes[chain.oldIndex];
                n.Id = write;
                n.ChainIndex = chain.chainId;
                finalNodes[write] = n;
                finalPositions[write] = mechPositions[chain.oldIndex];
                newIndexByOld[chain.oldIndex] = write;
                // Record live block for per-tick refresh at the NEW index.
                if (liveMap.TryGetValue(chain.oldIndex, out var blk) && blk != null)
                    _liveMech.Add((write, blk));
                write++;
            }

            // Second pass: convert BFS-parent OLD indices into the final NEW indices.
            foreach (var chain in chains.chainNodeOrder)
            {
                int wi = newIndexByOld[chain.oldIndex];
                var n = finalNodes[wi];
                n.ParentIndex = chain.parentOldIndex >= 0 && newIndexByOld.TryGetValue(chain.parentOldIndex, out int p)
                    ? p
                    : -1;
                finalNodes[wi] = n;
            }

            // Then hull nodes.
            for (int i = 0; i < hullNodes.Count; i++)
            {
                var n = hullNodes[i];
                n.Id = write;
                n.ChainIndex = -1;

                // Track hull material blocks for waterlogging + read their actual buoyancy.
                var hullBlock = hullBlockSources[i];
                if (hullBlock != null)
                {
                    n.BuoyancyFactor = hullBlock.buoyancyFactor;
                    if (hullBlock.maxWaterlogging > 0f)
                        _waterlogHulls.Add((write, hullBlock));
                }

                finalNodes[write] = n;
                write++;
            }

            // Refresh waterwheel flow velocity stored on nodes is fine; per-tick we
            // don't re-sample flow (it's slow-varying). Re-sample if a rebuild happens.

            _nodes = finalNodes;
            _nodePositions.AddRange(finalPositions);
            _waterHeights = new NativeArray<float>(finalCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _waterDensities = new NativeArray<float>(finalCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _nodePosArray = new NativeArray<float3>(finalCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _posAllocated = true;

            _chains = new NativeArray<PropulsionChain>(chains.chains.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < chains.chains.Count; i++) _chains[i] = chains.chains[i];

            _allocated = true;
        }

        private MechanicalNode BaseNode(int id, float3 worldPos, GridBlock block, IMechanicalBlock mech)
        {
            Vector3 fwd = block.transform.forward;
            Vector3 up = block.transform.up;
            return new MechanicalNode
            {
                Id = id,
                Type = mech.NodeType,
                ParentIndex = -1,
                WorldPosition = worldPos,
                WorldThrustAxis = math.normalizesafe(new float3(fwd.x, fwd.y, fwd.z), new float3(0, 0, 1)),
                UpAxis = math.normalizesafe(new float3(up.x, up.y, up.z), new float3(0, 1, 0)),
                Mass = Mathf.Max(1f, block.BlockMass),
                Volume = BlockVolume(block),
                BlockHeight = _cellSize,
                BuoyancyFactor = DefaultBuoyancyFactor(block),
                GearRatio = 1f,
                MaxGearSpeed = settings.globalGearSpeedCap,
                PropellerSize = 1f,
                ThrustCoefficient = settings.thrustCoefficient,
                OutputMultiplier = 1f,
            };
        }

        private MechanicalNode HullNode(int id, float3 worldPos, GridBlock block)
        {
            return new MechanicalNode
            {
                Id = id,
                Type = MechanicalNodeType.Hull,
                ParentIndex = -1,
                WorldPosition = worldPos,
                WorldThrustAxis = new float3(0, 0, 1),
                UpAxis = new float3(0, 1, 0),
                Mass = Mathf.Max(1f, block.BlockMass),
                Volume = BlockVolume(block),
                BlockHeight = _cellSize,
                BuoyancyFactor = DefaultBuoyancyFactor(block),
                OutputMultiplier = 1f,
            };
        }

        private float BlockVolume(GridBlock block)
        {
            // Volume is tuned from runtime mass, then multiplied by a displacement reserve.
            // Without reserve, buoyancyFactor=1 only becomes weight-neutral when the
            // block is fully submerged. Real ship blocks need spare enclosed volume, so
            // buoyant hulls should float visibly higher in the water.
            float runtimeMass = _grid != null && _rb != null
                ? Mathf.Max(1f, _rb.mass / Mathf.Max(1, _grid.BlockCount)) // per-block mass
                : Mathf.Max(1f, block.TotalMass);
            float waterDensity = settings != null ? settings.waterDensity : 1025f;
            float reserve = settings != null ? Mathf.Max(1f, settings.buoyancyReserve) : 2.0f;
            return runtimeMass / waterDensity * reserve;
        }

        private float DefaultBuoyancyFactor(GridBlock block)
        {
            // Proper hull material component — use its authored buoyancy factor.
            if (block is GridHullBlock hull)
                return hull.buoyancyFactor;

            // Fallback: name-based heuristic for unconfigured blocks.
            string n = (block.blockName ?? string.Empty).ToLowerInvariant();
            if (n.Contains("iron")) return 0.0f;
            if (n.Contains("balsa") || n.Contains("cork")) return 1.0f;
            if (n.Contains("wood") || n.Contains("plank")) return 0.9f;
            return 0.6f;
        }

        // ── Chain (connected-component) builder ───────────────────────
        private struct ChainBuildResult
        {
            public List<PropulsionChain> chains;
            public List<(int oldIndex, int chainId, int parentOldIndex)> chainNodeOrder;
        }

        private ChainBuildResult BuildChains(List<MechanicalNode> mechNodes,
            List<Vector3Int> mechPositions, List<GridBlock> mechBlocks,
            Dictionary<Vector3Int, int> posToIndex)
        {
            int n = mechNodes.Count;
            var visited = new bool[n];
            var chains = new List<PropulsionChain>(8);
            var order = new List<(int oldIndex, int chainId, int parentOldIndex)>(n);

            int chainId = 0;
            for (int start = 0; start < n; start++)
            {
                if (visited[start]) continue;

                // BFS this connected component.
                var comp = new List<int>(16);
                var queue = new Queue<int>(16);
                queue.Enqueue(start);
                visited[start] = true;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    comp.Add(cur);
                    Vector3Int p = mechPositions[cur];
                    foreach (var off in Neighbours6)
                    {
                        if (!posToIndex.TryGetValue(p + off, out int nb) || visited[nb]) continue;
                        if (!MaritimeMechanicalPorts.CanConnect(mechBlocks[cur], mechBlocks[nb], _cellSize)) continue;
                        visited[nb] = true;
                        queue.Enqueue(nb);
                    }
                }

                // Choose the source: prefer an engine, then a waterwheel.
                int source = -1;
                for (int i = 0; i < comp.Count; i++)
                {
                    var t = mechNodes[comp[i]].Type;
                    if (t == MechanicalNodeType.Engine) { source = comp[i]; break; }
                }
                if (source < 0)
                {
                    for (int i = 0; i < comp.Count; i++)
                        if (mechNodes[comp[i]].Type == MechanicalNodeType.Waterwheel) { source = comp[i]; break; }
                }

                // Order the component source-first (BFS from the source if we have one).
                // Each node also records its BFS parent so the propagation job can
                // evaluate the drivetrain as a tree (fixes branch splits + makes a
                // gearbox work from ANY input side).
                List<(int idx, int parent)> ordered;
                if (source >= 0)
                {
                    ordered = BfsOrdered(source, mechNodes, mechPositions, mechBlocks, posToIndex, n);
                }
                else
                {
                    ordered = new List<(int, int)>(comp.Count);
                    foreach (int compIdx in comp) ordered.Add((compIdx, -1));
                }

                int sliceStart = order.Count;
                foreach (var entry in ordered)
                    order.Add((entry.idx, chainId, entry.parent));

                // SourceIndex is the ABSOLUTE index into the final node array
                // (slice start + the source's position within the ordered slice).
                int sourceOffset = -1;
                if (source >= 0)
                    for (int oi = 0; oi < ordered.Count; oi++)
                        if (ordered[oi].idx == source) { sourceOffset = oi; break; }
                chains.Add(new PropulsionChain
                {
                    StartIndex = sliceStart,
                    Length = ordered.Count,
                    SourceIndex = source >= 0 ? sliceStart + sourceOffset : -1
                });
                chainId++;
            }

            return new ChainBuildResult { chains = chains, chainNodeOrder = order };
        }

        private List<(int idx, int parent)> BfsOrdered(int source, List<MechanicalNode> nodes,
            List<Vector3Int> positions, List<GridBlock> blocks,
            Dictionary<Vector3Int, int> posToIndex, int total)
        {
            var visited = new bool[total];
            var result = new List<(int, int)>(16);
            var queue = new Queue<(int idx, int parent)>(16);
            queue.Enqueue((source, -1));
            visited[source] = true;
            while (queue.Count > 0)
            {
                var (cur, parent) = queue.Dequeue();
                result.Add((cur, parent));
                Vector3Int p = positions[cur];
                foreach (var off in Neighbours6)
                {
                    if (!posToIndex.TryGetValue(p + off, out int nb) || visited[nb]) continue;
                    if (!CanTraverseMechanicalEdge(cur, nb, nodes, blocks)) continue;
                    visited[nb] = true;
                    queue.Enqueue((nb, cur));
                }
            }
            return result;
        }

        private bool CanTraverseMechanicalEdge(int fromIndex, int toIndex,
            List<MechanicalNode> nodes, List<GridBlock> blocks)
        {
            if (fromIndex < 0 || toIndex < 0 || fromIndex >= blocks.Count || toIndex >= blocks.Count)
                return false;

            // A second engine/waterwheel may join an already sourced shaft bus from
            // its own output side. The propagation job combines all producer torque,
            // so include it after validating the physical port mate in either direction.
            var toType = nodes[toIndex].Type;
            if (toType == MechanicalNodeType.Engine || toType == MechanicalNodeType.Waterwheel)
                return MaritimeMechanicalPorts.CanConnect(blocks[fromIndex], blocks[toIndex], _cellSize);

            return MaritimeMechanicalPorts.CanTransfer(blocks[fromIndex], blocks[toIndex], _cellSize);
        }

        // ── Turbocharger: flag adjacent Giant Diesel engines (+40% torque) ──
        private void ApplyTurbochargers(List<MechanicalNode> mechNodes,
            List<Vector3Int> mechPositions, Dictionary<Vector3Int, int> posToIndex)
        {
            for (int i = 0; i < mechNodes.Count; i++)
            {
                if (mechNodes[i].Type != MechanicalNodeType.Turbocharger) continue;
                Vector3Int p = mechPositions[i];
                foreach (var off in Neighbours6)
                {
                    if (posToIndex.TryGetValue(p + off, out int nb))
                    {
                        var node = mechNodes[nb];
                        if (node.IsGiantDiesel)
                            node.SetFlag(MechanicalFlags.TurboBoosted);
                        mechNodes[nb] = node;
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  WATERLOGGING TICK
        // ══════════════════════════════════════════════════════════════
        private void TickWaterlogging()
        {
            float dt = Time.fixedDeltaTime;

            // Bilge pumps drain first (so soaked hulls that are also being pumped
            // get net-zero or net-negative waterlogging this tick).
            for (int i = 0; i < _bilgePumps.Count; i++)
            {
                if (_bilgePumps[i] != null) _bilgePumps[i].TickDrain();
            }

            // Hull blocks absorb water while submerged (if not waterproof).
            for (int i = 0; i < _waterlogHulls.Count; i++)
            {
                var (idx, hull) = _waterlogHulls[i];
                if (hull == null) continue;

                float submergence = _nodes[idx].Submergence;
                if (submergence > 0f && hull.maxWaterlogging > 0f)
                {
                    float soak = hull.soakRate * submergence * dt;
                    hull.WaterloggedMass = Mathf.Min(hull.maxWaterlogging, hull.WaterloggedMass + soak);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════
        private float CurrentGravityMagnitude()
        {
            float scale = _grid != null ? _grid.gravityScale : 1f;
            float mult = AtmosphereManager.GetGravityMultiplier(transform.position);
            float g = VoxelEngine.Cosmos.GravityProvider.GetGravity(transform.position).magnitude;
            if (g <= 0.001f) g = Physics.gravity.magnitude;
            return g * Mathf.Max(0f, scale) * Mathf.Max(0f, mult);
        }

        private void DisposeJobData()
        {
            if (_allocated)
            {
                if (_nodes.IsCreated) _nodes.Dispose();
                if (_waterHeights.IsCreated) _waterHeights.Dispose();
                if (_waterDensities.IsCreated) _waterDensities.Dispose();
                if (_chains.IsCreated) _chains.Dispose();
            }
            if (_posAllocated && _nodePosArray.IsCreated) _nodePosArray.Dispose();
            _allocated = false;
            _posAllocated = false;
        }
    }
}
