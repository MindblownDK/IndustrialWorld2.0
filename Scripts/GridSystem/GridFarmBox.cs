// Assets/Scripts/VoxelEngine/GridSystem/GridFarmBox.cs
//
// 18.4.0-dev / 18.8.0-dev — Large Farm Plot Box for a grid.
//
// A three-by-three floor planter with a wide soil bed and an overhead grow-lamp
// gantry. Takes both food crops and tree saplings; planting a sapling claims the
// whole bed (treeBed = true), locking the other eight cells until the timber is
// felled. Pulls water from GridLiquidNetwork and breathable air from the room.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridFarmBox : GridFarmPlotBase
    {
        public override int CellCount => 9;
        public override string PlotKind => "FLOOR";
        protected override float PodVisualScale => 0.85f;

        private void Reset()
        {
            blockName = "Large Farm Plot Box";
            BlockMass = 520f;
            maxHP = 650f;
            allowsTrees = true;
            powerDraw = 65f;
            idlePowerDraw = 6f;
            waterCapacity = 240f;
            waterStored = 0f;
            litresPerCellSecond = 0.02f;
            refillLitresPerSecond = 3f;
            growthSpeedMultiplier = 0.55f;
            treeGrowthSeconds = 110f;
            treeLogsPerHarvest = 6;
            treeSaplingsPerHarvest = 2;
            treeBiomassPerHarvest = 2;
        }

        protected override Vector3 PodPosition(int index, int side)
        {
            int n = Mathf.Max(1, side);
            float step = 2.5f * 0.24f;
            int x = index % n;
            int z = index / n;
            float offset = (n - 1) * 0.5f;
            return new Vector3((x - offset) * step, -2.5f * 0.21f, (z - offset) * step);
        }
    }
}
