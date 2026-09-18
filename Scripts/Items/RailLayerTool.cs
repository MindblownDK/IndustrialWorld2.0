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

        [Tooltip("Material consumed per laid cell. A wide gauge over a long run should cost " +
                 "what the same track would cost laid by hand.")]
        public ItemDefinition railMaterial;

        [Tooltip("Units of material per cell.")]
        public int materialPerCell = 1;

        [Header("Gauge")]
        [Tooltip("How many parallel tracks this tool lays. 1 is a single line, 2 a double-track " +
                 "mainline, 3 a yard throat.")]
        [Range(1, 3)] public int defaultGauge = 1;

        public RailLayerTool() { toolType = ToolType.Other; }
    }
}
