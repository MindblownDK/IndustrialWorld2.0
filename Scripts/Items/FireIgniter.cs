// Assets/Scripts/VoxelEngine/Items/FireIgniter.cs
//
// 9.16.0 fire system (Liquids Overhaul, Part 2) — a flint-and-steel style igniter.
// RMB a flammable liquid cell (liquid fuel, refined oil, MGO, crude oil, heavy fuel
// oil) to set the pool alight. Durability = sparks (64 uses), broken sparks remove
// the tool from the slot like any other tool.
using UnityEngine;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Fire Igniter", fileName = "Igniter_New")]
    public class FireIgniter : ToolItem
    {
        public FireIgniter() { toolType = ToolType.Other; maxDurability = 64; maxStack = 1; }
    }
}
