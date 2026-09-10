// Assets/Scripts/VoxelEngine/Items/RoadPaverTool.cs
//
// THE ROAD PAVER — the drag-to-pave hand tool.
//
// A player holding the material should be able to pave without having earned a traffic system,
// so the paver ships with the surface: hold LMB and walk a line, and the strip lays itself under
// the aim, cell by cell, interpolating the cells a fast drag skips so the run is continuous
// rather than dotted. RMB lifts a cell back.
//
// This is the ITEM (data + authored tuning). The gesture lives in `Building/RoadPaver.cs`, and
// block-by-block placement of the road block stays available as the fallback for one culvert or a
// patched bend — the tool is a convenience, not a gate.

using UnityEngine;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Road Paver Tool", fileName = "Tool_RoadPaver")]
    public class RoadPaverTool : ToolItem
    {
        [Header("Paving")]
        [Tooltip("Material the paver burns, one unit per laid cell and per unit of repair. This is " +
                 "the same hot mix the road block is crafted from, so repairing a road and laying " +
                 "one cost the same thing — 'a can of the same material'.")]
        public ItemDefinition pavingMaterial;

        [Tooltip("Road block laid by this tool. Authored by the setup step so the paver and the " +
                 "hand-placed block are always the same block.")]
        public BlockItem roadBlock;

        [Tooltip("Units of material one cell costs on smooth ground. Rough ground doubles it; " +
                 "that is the grading rule the design asks for.")]
        public int materialPerCell = 1;

        [Tooltip("Units refunded when a cell is lifted. Only a strip in good condition gives its " +
                 "material back — worn-out pavement is rubble, not stock.")]
        public int refundPerCell = 1;

        [Tooltip("Wear below which a lifted cell still refunds its material.")]
        [Range(0f, 1f)] public float refundWearLimit = 0.35f;

        [Tooltip("Longest gap the paver will interpolate across in one drag step, in cells. Stops a " +
                 "wild mouse flick from laying a kilometre of road in a single frame.")]
        public int maxCellsPerStep = 12;
        [Tooltip("Hot mix spent per SQUARE METRE of run per unit of wear, so a full repair of a " +
                 "worn-out run costs about a third of what laying it did. Priced by area rather " +
                 "than by cell count: a 4 m carriageway slab is sixteen times the pavement of a " +
                 "1 m patch cell, and billing both per cell would make the wide road sixteen " +
                 "times cheaper to keep.")]
        public float repairMaterialPerSquareMetre = 0.19f;

        public RoadPaverTool()
        {
            toolType = ToolType.Other;
            fireRate = 12f;
        }
    }
}
