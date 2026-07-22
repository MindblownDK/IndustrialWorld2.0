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
                    if (!endpoint && !connectedPipe) return;
                    if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(gridBlock.gameObject, block.gameObject)) return;
                    _neighbourPosBuf.Add(connectedPipe
                        ? Vector3.Lerp(transform.position, block.transform.position, 0.5f)
                        : block.transform.position);
                }

                foreach (var block in VoxelEngine.GridSystem.UnifiedGridTopology.AdjacentBlocks(grid, gridBlock))
                    AddArm(block);

                // Proximity arms: draw toward liquid endpoints whose body or named
                // liquid ports the pipe touches even when lattice cells don't face-touch.
                var myGridBlock = gridBlock;
                int hitCount = Physics.OverlapSphereNonAlloc(transform.position,
                    Mathf.Max(myGridBlock != null ? myGridBlock.EffectiveCellSize : cs,
                        VoxelEngine.GridSystem.GridSizeExt.CellSize(VoxelEngine.GridSystem.GridSize.Small)) * 1.35f,
                    s_armProbe, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hitCount; i++)
                {
                    var col = s_armProbe[i];
                    if (col == null) continue;
                    var block = col.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
                    if (block == null || block.Grid != grid) continue;
                    AddArm(block);
                }
            }
            return _neighbourPosBuf;
        }

        private static readonly Collider[] s_armProbe = new Collider[12];
    }
}
