// Assets/Scripts/VoxelEngine/GridSystem/GridFarmBox.cs
//
// 18.4.0-dev — Large Farm Plot Box.
//
// The floor-standing sibling of the Vertical Farm Plot: a wide, low glass box with a
// 3x3 bed, and the only grid plot that accepts a tree sapling. The trade the player is
// making is honest - the box eats a full hull cell of floor and gives back nine beds plus
// room for one tree, where the vertical plot gives nine beds for half the footprint and
// no tree.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridFarmBox : GridFarmPlotBase
    {
        public override int CellCount => 9;

        public override string PlotKind => "FLOOR";

        private void Reset()
        {
            blockName = "Large Farm Plot Box";
            BlockMass = 780f;
            maxHP = 620f;
            allowsTrees = true;
            waterCapacity = 260f;
            waterStored = 160f;
            powerDraw = 75f;
            idlePowerDraw = 7f;
            treeGrowthSeconds = 100f;
        }
    }
}
