// Assets/Scripts/VoxelEngine/GridSystem/GrinderTool.cs
//
// Grinder tool item for grid blocks.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    [CreateAssetMenu(menuName = "Voxel Engine/Grid/Grinder Tool")]
    public class GrinderTool : ToolItem
    {
        public float baseGrindTime = 4f;
        public float minGrindTime = 1.5f;
    }
}