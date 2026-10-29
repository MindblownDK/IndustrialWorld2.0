// Assets/Scripts/VoxelEngine/GridSystem/GridSeedPlanter.cs
//
// 18.8.0-dev — Grid Seed Planter.
//
// A compact single-crop hydroponic tray for ships and stations. Shares the full
// GridFarmPlotBase simulation (piped water from GridLiquidNetwork, breathable air
// from FarmEnvironment.HasBreathableAir, automatic seed intake, self-replanting,
// and automatic push to connected GridCargoContainers) in a low-power single-bed
// footprint.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridSeedPlanter : GridFarmPlotBase
    {
        public override int CellCount => 1;
        public override string PlotKind => "PLANTER";
        protected override float PodVisualScale => 1.15f;

        private void Reset()
        {
            blockName = "Grid Seed Planter";
            BlockMass = 240f;
            maxHP = 320f;
            allowsTrees = false;
            powerDraw = 20f;
            idlePowerDraw = 2f;
            waterCapacity = 60f;
            waterStored = 0f;
            litresPerCellSecond = 0.02f;
            refillLitresPerSecond = 1.5f;
            growthSpeedMultiplier = 0.55f;
            seedSlots = 2;
            harvestSlots = 4;
        }

        protected override Vector3 PodPosition(int index, int side)
        {
            return new Vector3(0f, -2.5f * 0.21f, 0f);
        }
    }
}
