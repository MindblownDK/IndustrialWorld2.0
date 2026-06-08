// Assets/Scripts/VoxelEngine/GridSystem/GridArmorBlock.cs
//
// High-durability armor block for grids. Better HP and mass than basic blocks.
// Can be placed on Small or Large grids.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridArmorBlock : GridBlock
    {
        public override void OnPlaced()
        {
            base.OnPlaced();
            BlockMass = 250f;
            maxHP = 800f;
            currentHP = maxHP;
            blockName = "Armor Block";
        }
    }
}