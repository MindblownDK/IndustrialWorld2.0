// Assets/Scripts/VoxelEngine/Storage/ServerComponent.cs
//
// Server hardware items: RAM, CPU, PSU. Inserted into ServerRack slots.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    public enum ComponentType { RAM, CPU, PSU }

    [CreateAssetMenu(menuName = "Voxel Engine/Storage/Server Component", fileName = "Comp_New")]
    public class ServerComponent : ItemDefinition
    {
        [Header("Component")]
        public ComponentType componentType = ComponentType.RAM;

        [Tooltip("RAM: pattern slots per module. CPU: speed multiplier. PSU: max watts.")]
        public float value = 4f;

        public ServerComponent() { maxStack = 1; category = "Storage"; }
    }
}
