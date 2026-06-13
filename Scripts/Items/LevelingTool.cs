// Assets/Scripts/VoxelEngine/Items/LevelingTool.cs
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// A tool that flattens terrain to a target Y level within a brush radius.
    /// First left-click sets the target Y (the surface you're looking at).
    /// Subsequent left-clicks fill voxels under that Y to stone, and remove voxels above it.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Leveling Tool", fileName = "LevelingTool_New")]
    public class LevelingTool : ToolItem
    {
        public LevelingTool() { toolType = ToolType.Other; }
    }
}
