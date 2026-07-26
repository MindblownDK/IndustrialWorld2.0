// Assets/Scripts/VoxelEngine/Gas/GasPipe.cs
//
// Universal gas transport pipe. Carries steam, hydrogen, oxygen between
// machines. Auto-connects to neighbours within connectRadius.
//
// VISUAL: hands its live neighbour list to a PipeVisualBuilder so the pipe
// renders the same chunky core+arms style used by Power / Data cables.
// Glass variant exposes an inner medium-tinted core that previews the gas
// flowing through the network.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Networks;

namespace VoxelEngine.Gas
{
    [RequireComponent(typeof(PlacedBlock))]
    public class GasPipe : MonoBehaviour
    {
        [Tooltip("Max pressure this pipe can handle (arbitrary units).")]
        public float maxPressure = 100f;
        [Tooltip("World-space search radius for other pipes. Capped at one detail " +
                 "cell (≈0.5 m) by the topology manager so pipes can't reach across gaps.")]
        public float connectRadius = 1.5f;

        [Header("Visual")]
        [Tooltip("Render as a translucent glass pipe with the carried gas visible inside.")]
        public bool isGlass = false;

        [System.NonSerialized] public List<GasPipe> neighbours = new();

        // ── Visual integration ─────────────────────────────────
        private PipeVisualBuilder _visuals;
        private readonly List<Vector3> _neighbourPosBuf = new(6);
        private static readonly Collider[] s_armProbe = new Collider[12];

        private void Awake()
        {
            _visuals = GetComponent<PipeVisualBuilder>();
            if (_visuals == null) _visuals = gameObject.AddComponent<PipeVisualBuilder>();
            _visuals.neighbourPositionsProvider = GetNeighbourPositions;
            // Sync the glass flag onto the visual builder so prefab authors only have
            // to flip a single bool on the GasPipe component.
            _visuals.isGlass = isGlass;
            // Gas pipes are SLIM polished brass — distinct silhouette from the
            // fatter copper water pipes so the player can tell them apart at a glance.
            _visuals.style = VoxelEngine.Networks.PipeStyle.Brass;
            // Unused end-caps looked like the pipe was trying to connect in every
            // direction. Gas pipes now only show arms where a real network link exists.
            _visuals.showUnusedFaceCaps = false;
        }

        private void OnEnable()
        {
            if (VoxelEngine.Building.BuildSystem.IsCreatingGhost) return;
            GasNetwork.EnsureInstance();
            GasNetwork.Instance?.Register(this);
        }
        private void OnDisable() => GasNetwork.Instance?.Unregister(this);

        /// <summary>
        /// Supplier called by <see cref="PipeVisualBuilder"/> every rebuild interval.
        /// Returns live world positions of every connected neighbour pipe.
        /// </summary>
        private List<Vector3> GetNeighbourPositions()
        {
            _neighbourPosBuf.Clear();
            foreach (var n in neighbours)
                if (n != null) _neighbourPosBuf.Add(Vector3.Lerp(transform.position, n.transform.position, 0.5f));

            // If this normal gas pipe is attached to a grid, also draw arms to
            // adjacent gas-capable grid blocks. WrenchBlacklist can disable each
            // pipe ↔ endpoint link. Endpoint arms aim at the block's real GAS port
            // (engine oxygen intake, exhaust-pipe gas tap) when it has one.
            var gridBlock = GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            var grid = gridBlock != null ? gridBlock.Grid : null;
            if (grid != null)
            {
                // Gather all gas-capable candidates once (adjacent + proximity),
                // resolving each block's nearest gas port — so exhaust-tap arms can
                // be suppressed whenever a cleaner (non-tap) port is closer.
                _armCandidates.Clear();
                void Consider(VoxelEngine.GridSystem.GridBlock block)
                {
                    if (block == null || block == gridBlock || block.Grid != grid) return;
                    bool endpoint = block is VoxelEngine.GridSystem.GridGasTank
                                 || block is VoxelEngine.GridSystem.GridH2O2Generator
                                 || block is VoxelEngine.GridSystem.GridHydrogenEngine
                                 || block is VoxelEngine.GridSystem.GridThruster
                                 || block is VoxelEngine.Maritime.GridExhaustPipe
                                 || block is VoxelEngine.Maritime.GridMaritimeEngine;
                    bool connectedPipe = block.GetComponentInChildren<GasPipe>(true) != null;
                    // World-side gas tanks/electrolysers placed adjacent to a grid
                    // pipe should also draw an arm — same bridging rule as liquid pipes.
                    bool worldEndpoint = !endpoint && !connectedPipe &&
                        (block.GetComponentInChildren<VoxelEngine.Gas.GasTank>(true) != null);
                    if (endpoint) { /* keep */ }
                    else if (worldEndpoint) endpoint = true;
                    if (!endpoint && !connectedPipe) return;
                    if (_armCandidates.Contains(block)) return;
                    _armCandidates.Add(block);
                }

                foreach (var block in VoxelEngine.GridSystem.UnifiedGridTopology.AdjacentBlocks(grid, gridBlock))
                    Consider(block);

                // Proximity arms: gas ports overhang the lattice on the big machine
                // models (engine O2 intakes, the exhaust gas tap), so face-touch alone
                // misses them — reach any gas-capable block in touch range.
                // Radius is TIGHT (~0.7 m for detail pipes) so pipes don't grow arms
                // toward machines that sit across the room (the "tries to connect to
                // pipes nowhere near it" spam reported by players).
                float probeRadius = Mathf.Min(
                    Mathf.Max(gridBlock != null ? gridBlock.EffectiveCellSize : 0.5f,
                        VoxelEngine.GridSystem.GridSizeExt.CellSize(VoxelEngine.GridSystem.GridSize.Small)) * 1.35f,
                    0.85f);
                int hitCount = Physics.OverlapSphereNonAlloc(transform.position, probeRadius,
                    s_armProbe, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hitCount; i++)
                {
                    var col = s_armProbe[i];
                    if (col == null) continue;
                    Consider(col.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>());
                }

                // Detail-lattice tank reach: grid gas tanks connect visually at the
                // same 5-small-cell cardinal range that pipe↔pipe links use.
                // The actual drawn arm targets the nearest named gas port.
                float detailStep = VoxelEngine.GridSystem.GridSizeExt.CellSize(VoxelEngine.GridSystem.GridSize.Small);
                foreach (var block in grid.AllBlocks)
                {
                    if (block is not VoxelEngine.GridSystem.GridGasTank tank) continue;
                    var port = VoxelEngine.Maritime.MaritimePorts.FindNearest(
                        tank.transform, VoxelEngine.Maritime.MaritimePorts.GasPrefixes, transform.position);
                    Vector3 target = port != null ? port.position : tank.transform.position;
                    Vector3 localDelta = grid.transform.InverseTransformVector(target - transform.position);
                    if (!VoxelEngine.Networks.PipeAdjacency.IsCardinalLinkDelta(localDelta, detailStep, 5f, detailStep * 0.55f)) continue;
                    Consider(tank);
                }

                // Closest CLEAN (non-exhaust-tap) gas port to THIS pipe — an O₂
                // delivery pipe plugs into the intake, not into the exhaust tap
                // standing next to it.
                float bestCleanDistSqr = float.MaxValue;
                for (int i = 0; i < _armCandidates.Count; i++)
                {
                    var block = _armCandidates[i];
                    var port = VoxelEngine.Maritime.MaritimePorts.FindNearest(
                        block.transform, VoxelEngine.Maritime.MaritimePorts.GasPrefixes, transform.position);
                    if (port == null || IsExhaustTapPort(port)) continue;
                    float d = (port.position - transform.position).sqrMagnitude;
                    if (d < bestCleanDistSqr) bestCleanDistSqr = d;
                }

                for (int i = 0; i < _armCandidates.Count; i++)
                {
                    var block = _armCandidates[i];
                    if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(gridBlock.gameObject, block.gameObject)) continue;
                    bool connectedPipe = block.GetComponentInChildren<GasPipe>(true) != null;
                    if (connectedPipe)
                    {
                        float detail = VoxelEngine.GridSystem.GridSizeExt.CellSize(VoxelEngine.GridSystem.GridSize.Small);
                        Vector3 localDelta = grid.transform.InverseTransformVector(block.transform.position - transform.position);
                        if (!VoxelEngine.Networks.PipeAdjacency.IsCardinalLinkDelta(localDelta, detail, 5f, detail * 0.12f))
                            continue;
                        _neighbourPosBuf.Add(Vector3.Lerp(transform.position, block.transform.position, 0.5f));
                        continue;
                    }
                    var port = VoxelEngine.Maritime.MaritimePorts.FindNearest(
                        block.transform, VoxelEngine.Maritime.MaritimePorts.GasPrefixes, transform.position);
                    if (port != null && IsExhaustTapPort(port)
                        && (port.position - transform.position).sqrMagnitude > bestCleanDistSqr)
                        continue; // a cleaner port is nearer — this pipe isn't the capture run
                    _neighbourPosBuf.Add(port != null ? port.position : block.transform.position);
                }

                // World-side gas tanks/electrolysers placed next to a grid pipe
                // (bridging between world-placed tanks and grid-mounted pipes).
                int worldHit = Physics.OverlapSphereNonAlloc(transform.position, probeRadius,
                    s_worldProbe, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < worldHit; i++)
                {
                    var col = s_worldProbe[i]; s_worldProbe[i] = null;
                    if (col == null) continue;
                    var gb = col.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
                    if (gb != null) continue; // already handled above
                    var wPipe = col.GetComponentInParent<GasPipe>();
                    if (wPipe != null && wPipe != this)
                    {
                        if (VoxelEngine.Networks.PipeAdjacency.IsCardinalNeighbour(
                                transform.position, wPipe.transform.position,
                                VoxelEngine.Networks.PipeAdjacency.DefaultGridSize))
                            _neighbourPosBuf.Add(Vector3.Lerp(transform.position, wPipe.transform.position, 0.5f));
                        continue;
                    }
                    var wTank = col.GetComponentInParent<GasTank>();
                    if (wTank != null)
                    {
                        Vector3 anchor = wTank.transform.position;
                        if ((anchor - transform.position).sqrMagnitude <= probeRadius * probeRadius)
                            _neighbourPosBuf.Add(Vector3.Lerp(transform.position, anchor, 0.5f));
                    }
                }
            }
            else
            {
                // Free-placed (non-grid) gas pipe — draw arms to nearby world tanks/pipes.
                float reach = VoxelEngine.Networks.PipeAdjacency.DefaultGridSize * 1.35f;
                int hitCount = Physics.OverlapSphereNonAlloc(transform.position, reach,
                    s_worldProbe, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hitCount; i++)
                {
                    var col = s_worldProbe[i]; s_worldProbe[i] = null;
                    if (col == null) continue;
                    var other = col.GetComponentInParent<GasPipe>();
                    if (other != null && other != this)
                    {
                        if (VoxelEngine.Networks.PipeAdjacency.IsCardinalNeighbour(
                                transform.position, other.transform.position))
                            _neighbourPosBuf.Add(Vector3.Lerp(transform.position, other.transform.position, 0.5f));
                        continue;
                    }
                    var tank = col.GetComponentInParent<GasTank>();
                    if (tank != null)
                    {
                        Vector3 delta = tank.transform.position - transform.position;
                        if (VoxelEngine.Networks.PipeAdjacency.IsAxisAlignedWithinDelta(
                                delta, VoxelEngine.Networks.PipeAdjacency.DefaultGridSize, 1.2f, 0.45f))
                            _neighbourPosBuf.Add(Vector3.Lerp(transform.position, tank.transform.position, 0.5f));
                    }
                }
            }
            return _neighbourPosBuf;
        }

        private readonly System.Collections.Generic.List<VoxelEngine.GridSystem.GridBlock> _armCandidates = new(8);
        private static readonly Collider[] s_worldProbe = new Collider[16];

        private static bool IsExhaustTapPort(Transform port)
            => port != null && port.name.StartsWith("Port_ExhaustGasIO", System.StringComparison.Ordinal);
    }
}
