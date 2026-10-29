// Assets/Scripts/VoxelEngine/GridSystem/GridVerticalFarm.cs
//
// 18.4.0-dev / 18.8.0-dev — Vertical Farm Plot for a grid.
//
// Three stacked hydroponic shelves with nine crop pods in a single upright rack.
// Fits a ship corridor or a station wall where floor space is scarce; takes piped
// water from GridLiquidNetwork and breathable air from the room it stands in.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridVerticalFarm : GridFarmPlotBase
    {
        public override int CellCount => 9;
        public override string PlotKind => "VERTICAL";
        protected override float PodVisualScale => 0.56f;

        private void Reset()
        {
            blockName = "Vertical Farm Plot";
            BlockMass = 360f;
            maxHP = 450f;
            allowsTrees = false;
            powerDraw = 55f;
            idlePowerDraw = 6f;
            waterCapacity = 160f;
            waterStored = 0f;
            litresPerCellSecond = 0.018f;
            refillLitresPerSecond = 2.5f;
            growthSpeedMultiplier = 0.6f;
        }

        protected override Vector3 PodPosition(int index, int side)
        {
            int shelf = Mathf.Clamp(index / 3, 0, 2);
            int col   = Mathf.Clamp(index % 3, 0, 2);
            float h = 2.5f * 0.5f;
            float trayY = Mathf.Lerp(-h * 0.52f, h * 0.36f, shelf / 2f) + 0.04f;
            float x = (col - 1) * (2.5f * 0.22f);
            return new Vector3(x, trayY, 0.04f);
        }
    }
}
