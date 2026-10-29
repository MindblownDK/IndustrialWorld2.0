// Assets/Scripts/VoxelEngine/GridSystem/GridVerticalFarm.cs
//
// 18.4.0-dev — Vertical Farm Plot.
//
// Three stacked trays on a 2.5 m grid cell: a tall, narrow block for a hull where floor
// area is the scarce thing. Nine cells, all crops, no trees - a tree needs depth for its
// roots and headroom for its canopy, and a stacked tray gives it neither. That rule is
// enforced here rather than in the UI so a panel, a script or a future drone all get the
// same answer.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridVerticalFarm : GridFarmPlotBase
    {
        public override int CellCount => 9;

        public override string PlotKind => "VERTICAL";

        /// <summary>
        /// The vertical plot's nine cells live on three trays, so a crop grows out of the
        /// tray it was planted in rather than out of the middle of the block.
        /// </summary>
        protected override Vector3 PodPosition(int index, int side)
        {
            int tray = Mathf.Clamp(index / 3, 0, 2);
            int column = index % 3;
            float trayY = tray == 0 ? -0.72f : tray == 1 ? 0.06f : 0.84f;
            return new Vector3((column - 1) * 0.60f, trayY + 0.10f, 0.10f);
        }

        private void Reset()
        {
            blockName = "Vertical Farm Plot";
            BlockMass = 620f;
            maxHP = 560f;
            allowsTrees = false;
            waterCapacity = 180f;
            waterStored = 110f;
            powerDraw = 60f;
            idlePowerDraw = 6f;
        }
    }
}
