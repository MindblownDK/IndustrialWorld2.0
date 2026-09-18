// Assets/Scripts/VoxelEngine/Items/RailLayerTool.cs
//
// The carried tool that lays rail by dragging, at a chosen gauge.
//
// Modelled on `RoadPaverTool` deliberately: the player already knows how the paver works
// (aim, click a start, drag, click to commit), so rail should not invent a second
// interaction for the same verb. The differences are only the ones rail genuinely has -
// a gauge instead of a lane count, and a gradient limit a road does not care about.

using UnityEngine;

namespace VoxelEngine.Items
{
    public class RailLayerTool : ToolItem
    {
        [Header("Rail")]
        [Tooltip("Track block laid by this tool. Authored by the setup step so the tool and " +
                 "the hand-placed block are always the same block.")]
        public BlockItem trackBlock;

        [Tooltip("Ballast block laid under every rail cell - the raised stone bed real track " +
                 "sits on. Authored by the setup step.")]
        public BlockItem ballastBlock;

        [Tooltip("Rail track ITEM consumed per cell. This is the same Rail Track the player " +
                 "crafts and places by hand, so laying a run costs exactly what laying it by " +
                 "hand would - the tool saves effort, not materials.")]
        public ItemDefinition trackItem;

        [Tooltip("Rail track items consumed per cell.")]
        public int trackPerCell = 1;

        [Tooltip("Stone consumed per cell for the ballast bed.")]
        public ItemDefinition ballastMaterial;

        [Tooltip("Stone per cell. Ballast is cheap but not free - a long main line is a real " +
                 "quarrying commitment, which is what makes a branch line a decision.")]
        public int ballastPerCell = 2;

        [Header("Gauge")]
        [Tooltip("How many parallel tracks this tool lays. 1 is a single line, 2 a double-track " +
                 "mainline, 3 a yard throat.")]
        [Range(1, 3)] public int defaultGauge = 1;

        public RailLayerTool() { toolType = ToolType.Other; }
    }
}
