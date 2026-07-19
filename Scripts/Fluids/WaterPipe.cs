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
                    if (n != null) _neighbourPosBuf.Add(n.transform.position);
            }

            // If this normal liquid pipe is attached to a grid, also draw arms to
            // adjacent liquid-capable grid blocks. WrenchBlacklist can disable each
            // pipe ↔ endpoint link.
            var gridBlock = GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            var grid = gridBlock != null ? gridBlock.Grid : null;
            if (grid != null)
            {
                foreach (var block in VoxelEngine.GridSystem.UnifiedGridTopology.AdjacentBlocks(grid, gridBlock))
                {
                    if (block == null || block == gridBlock) continue;
                    bool endpoint = block is VoxelEngine.GridSystem.GridLiquidTank
                                 || block is VoxelEngine.GridSystem.GridH2O2Generator
                                 || block is VoxelEngine.GridSystem.GridRefinery
                                 || block is VoxelEngine.GridSystem.GridChemicalPlant;
                    if (!endpoint && block.GetComponentInChildren<WaterPipe>(true) == null) continue;
                    if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(gridBlock.gameObject, block.gameObject)) continue;
                    _neighbourPosBuf.Add(block.transform.position);
                }
            }
            return _neighbourPosBuf;
        }
    }
}
