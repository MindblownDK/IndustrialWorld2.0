// Assets/Scripts/VoxelEngine/Building/Tiered/Hammer.cs
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>
    /// Marker subclass — the hammer is a regular ToolItem that PlayerInteractionTool
    /// recognises by type to route LMB to "upgrade" instead of "mine".
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Hammer", fileName = "Hammer_New")]
    public class Hammer : ToolItem
    {
        public Hammer() { toolType = ToolType.Other; }
    }
}
