// Assets/Scripts/VoxelEngine/GridSystem/GrinderTool.cs
//
// Hand-held grinder tool. Held in hotbar, LMB on a grid block to grind it down
// and recover it as an item. Takes 2 seconds base (upgradable to 0.5s via research).

using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Player;

namespace VoxelEngine.GridSystem
{
    /// <summary>
    /// Grinder tool item. When held and LMB on a GridBlock, grinds it down
    /// over time and returns the block as an inventory item.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Grid/Grinder Tool", fileName = "Tool_Grinder")]
    public class GrinderTool : ToolItem
    {
        [Header("Grinder")]
        [Tooltip("Base grind time in seconds.")]
        public float baseGrindTime = 2.0f;
        [Tooltip("Minimum grind time after upgrades.")]
        public float minGrindTime = 0.5f;

        public GrinderTool()
        {
            toolType = ToolType.Other;
            maxDurability = 500;
            maxStack = 1;
        }
    }
}
