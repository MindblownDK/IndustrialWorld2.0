// Assets/Scripts/VoxelEngine/GridSystem/GridGlassBlock.cs
//
// Transparent glass block.

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