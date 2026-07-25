// Assets/Scripts/VoxelEngine/Fluids/WaterPipe.cs
//
// Fluid-network pipe segment. The actual flow logic lives in
// FluidNetwork / FluidNetworkManager — this script is mostly an identity
// + a visual driver. Glass variants reveal an inner water-tinted core
// through the translucent shell.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Networks;

namespace VoxelEngine.Fluids
{
    public class WaterPipe : FluidNode
    {
        public override FluidNodeKind Kind => FluidNodeKind.Pipe;
        public float maxFlowLps = 50f;
        public bool isGlass = false;

        private PipeVisualBuilder _visuals;
        private readonly List<Vector3> _neighbourPosBuf = new(6);

        private void Awake()
        {
            _visuals = GetComponent<PipeVisualBuilder>();
            if (_visuals == null) _visuals = gameObject.AddComponent<PipeVisualBuilder>();
            _visuals.neighbourPositionsProvider = GetNeighbourPositions;
            _visuals.isGlass = isGlass;
            // Water/fluid pipes use the FATTER copper profile so they're
            // visually distinct from the slim brass gas pipes.
            _visuals.style = VoxelEngine.Networks.PipeStyle.Copper;
            // Unused end-caps looked like fake connections. Liquid pipes now only
            // render arms/collars for actual connected neighbours or endpoints.
            _visuals.showUnusedFaceCaps = false;
        }

        private List<Vector3> GetNeighbourPositions()
        {
            _neighbourPosBuf.Clear();
            if (neighbours != null)
            {
                foreach (var n in neighbours)
                    if (n != null) _neighbourPosBuf.Add(Vector3.Lerp(transform.position, n.transform.position, 0.5f));
            }

            // If this normal liquid pipe is attached to a grid, also draw arms to
            // adjacent liquid-capable grid blocks. WrenchBlacklist can disable each
            // pipe ↔ endpoint link. Arms reach BOTH the classic face-touch neighbours
            // and world-space-proximate endpoints (big machine models overhang their
            // lattice cell, and pipes snap to their named liquid ports).
            var gridBlock = GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            var grid = gridBlock != null ? gridBlock.Grid : null;
            if (grid != null)
            {
                float cs = VoxelEngine.GridSystem.GridSizeExt.CellSize(grid.gridSize);

                void AddArm(VoxelEngine.GridSystem.GridBlock block)
                {
                    if (block == null || block == gridBlock) return;
                    bool endpoint = block is VoxelEngine.GridSystem.GridLiquidTank
                                 || block is VoxelEngine.GridSystem.GridH2O2Generator
                                 || block is VoxelEngine.GridSystem.GridRefinery
                                 || block is VoxelEngine.GridSystem.GridChemicalPlant
                                 || block is VoxelEngine.Maritime.GridMaritimeEngine
                                 || block is VoxelEngine.Maritime.GridMarineWaterPump;
                    bool connectedPipe = block.GetComponentInChildren<WaterPipe>(true) != null;
                    // Also treat world-side fluid nodes (WaterTank/WaterPump) as
                    // endpoints when they sit next to a grid pipe.
                    bool worldEndpoint = !endpoint && !connectedPipe &&
                        (block.GetComponentInChildren<VoxelEngine.Fluids.WaterTank>(true) != null
                         || block.GetComponentInChildren<VoxelEngine.Fluids.WaterPump>(true) != null);
                    if (endpoint) { /* keep */ }
                    else if (worldEndpoint) endpoint = true;
                    if (!endpoint && !connectedPipe) return;
                    // Range-gate proximity finds: 0.5 m face-touch for endpoints,
                    // one detail cell for other detail pipes. Prevents the arm
                    // from growing toward endpoints that are nowhere near.
                    float dstSqr = (block.transform.position - transform.position).sqrMagnitude;
                    float reach = connectedPipe ? cs * 1.2f : cs * 1.35f;
                    if (dstSqr > reach * reach)
                    {
                        // Tight port-range check: endpoint has a named liquid port
                        // within 0.5 m of this pipe — still draw the arm.
                        bool portReachable = false;
                        if (endpoint)
                        {
                            var port = VoxelEngine.Maritime.MaritimePorts.FindNearest(
                                block.transform, VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes,
                                transform.position, cs * 1.25f);
                            if (port != null) portReachable = true;
                        }
                        if (!portReachable) return;
                    }
                    if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(gridBlock.gameObject, block.gameObject)) return;
                    _neighbourPosBuf.Add(connectedPipe
                        ? Vector3.Lerp(transform.position, block.transform.position, 0.5f)
                        // Aim endpoint arms at the machine's ACTUAL liquid port (fuel
                        // intake, coolant intake, tank Port_LiquidIO …) instead of its
                        // lattice centre — arms used to skew inline through the body.
                        : VoxelEngine.Maritime.MaritimePorts.PortPositionOrCenter(
                            block, VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes, transform.position));
                }

                foreach (var block in VoxelEngine.GridSystem.UnifiedGridTopology.AdjacentBlocks(grid, gridBlock))
                    AddArm(block);

                // Proximity arms: draw toward liquid endpoints whose body or named
                // liquid ports the pipe touches even when lattice cells don't face-touch.
                // Probe radius is tight (≈0.7 m for detail pipes) so pipes don't spam
                // arms toward blocks sitting across the room.
                var myGridBlock = gridBlock;
                float probeRadius = Mathf.Min(
                    Mathf.Max(myGridBlock != null ? myGridBlock.EffectiveCellSize : cs,
                        VoxelEngine.GridSystem.GridSizeExt.CellSize(VoxelEngine.GridSystem.GridSize.Small)) * 1.35f,
                    0.85f);
                int hitCount = Physics.OverlapSphereNonAlloc(transform.position, probeRadius,
                    s_armProbe, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hitCount; i++)
                {
                    var col = s_armProbe[i];
                    if (col == null) continue;
                    var block = col.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
                    if (block != null && block.Grid == grid)
                    {
                        AddArm(block);
                        continue;
                    }
                    // World-side (non-grid) fluid neighbours: tanks/pumps within
                    // touch range also get a visual arm, so pipes next to a
                    // world-placed tank visibly connect.
                    var worldTank = col.GetComponentInParent<VoxelEngine.Fluids.WaterTank>();
                    var worldPump = col.GetComponentInParent<VoxelEngine.Fluids.WaterPump>();
                    var worldPipe = col.GetComponentInParent<WaterPipe>();
                    if (worldPipe != null && worldPipe != this)
                    {
                        Vector3 d = worldPipe.transform.position - transform.position;
                        if (VoxelEngine.Networks.PipeAdjacency.IsCardinalNeighbour(
                                transform.position, worldPipe.transform.position,
                                VoxelEngine.Networks.PipeAdjacency.DefaultGridSize))
                        {
                            _neighbourPosBuf.Add(Vector3.Lerp(transform.position, worldPipe.transform.position, 0.5f));
                        }
                    }
                    else if (worldTank != null || worldPump != null)
                    {
                        Vector3 anchor = worldTank != null
                            ? worldTank.transform.position
                            : worldPump.transform.position;
                        if ((anchor - transform.position).sqrMagnitude <= probeRadius * probeRadius)
                        {
                            _neighbourPosBuf.Add(Vector3.Lerp(transform.position, anchor, 0.5f));
                        }
                    }
                }

                // Detail-lattice tank reach: grid tanks connect visually at the same
                // 5-small-cell cardinal range that pipe↔pipe links use. The target is
                // the tank's nearest named liquid port, not the tank body centre.
                float detailStep = VoxelEngine.GridSystem.GridSizeExt.CellSize(VoxelEngine.GridSystem.GridSize.Small);
                foreach (var block in grid.AllBlocks)
                {
                    if (block is not VoxelEngine.GridSystem.GridLiquidTank tank) continue;
                    if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(gridBlock.gameObject, tank.gameObject)) continue;
                    var port = VoxelEngine.Maritime.MaritimePorts.FindNearest(
                        tank.transform, VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes, transform.position);
                    Vector3 target = port != null ? port.position : tank.transform.position;
                    Vector3 localDelta = grid.transform.InverseTransformVector(target - transform.position);
                    if (!VoxelEngine.Networks.PipeAdjacency.IsCardinalLinkDelta(localDelta, detailStep, 5f, detailStep * 0.55f)) continue;
                    if (!_neighbourPosBuf.Contains(target)) _neighbourPosBuf.Add(target);
                }
            }
            else
            {
                // Free-placed (non-grid) pipe: draw arms to adjacent world
                // tanks/pumps/pipes within the tight 0.5–1m face-touch range.
                float reach = VoxelEngine.Networks.PipeAdjacency.DefaultGridSize * 1.35f;
                int hitCount = Physics.OverlapSphereNonAlloc(transform.position, reach,
                    s_armProbe, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hitCount; i++)
                {
                    var col = s_armProbe[i];
                    if (col == null) continue;
                    var otherPipe = col.GetComponentInParent<WaterPipe>();
                    if (otherPipe != null && otherPipe != this)
                    {
                        if (VoxelEngine.Networks.PipeAdjacency.IsCardinalNeighbour(
                                transform.position, otherPipe.transform.position))
                            _neighbourPosBuf.Add(Vector3.Lerp(transform.position, otherPipe.transform.position, 0.5f));
                        continue;
                    }
                    var tank = col.GetComponentInParent<VoxelEngine.Fluids.WaterTank>();
                    var pump = col.GetComponentInParent<VoxelEngine.Fluids.WaterPump>();
                    if (tank != null || pump != null)
                    {
                        Vector3 anchor = tank != null ? tank.transform.position : pump.transform.position;
                        Vector3 delta = anchor - transform.position;
                        if (VoxelEngine.Networks.PipeAdjacency.IsAxisAlignedWithinDelta(
                                delta, VoxelEngine.Networks.PipeAdjacency.DefaultGridSize, 1.2f, 0.45f))
                            _neighbourPosBuf.Add(Vector3.Lerp(transform.position, anchor, 0.5f));
                    }
                }
            }
            return _neighbourPosBuf;
        }

        private static readonly Collider[] s_armProbe = new Collider[24];
    }
}
