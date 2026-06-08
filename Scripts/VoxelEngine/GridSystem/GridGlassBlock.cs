// Assets/Scripts/VoxelEngine/GridSystem/GridGlassBlock.cs
//
// Transparent glass block for grids. Lower mass, structural, allows visibility.
// Uses transparent material in CreateBlock or prefab.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridGlassBlock : GridBlock
    {
        public override void OnPlaced()
        {
            base.OnPlaced();
            BlockMass = 40f;
            maxHP = 120f;
            currentHP = maxHP;
            blockName = "Glass Block";
        }
    }
}