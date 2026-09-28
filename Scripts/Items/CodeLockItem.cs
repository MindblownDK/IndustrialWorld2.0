// Assets/Scripts/VoxelEngine/Items/CodeLockItem.cs
//
// 13.17.0-dev: craftable keypad lock. Right-click with it on a door, gate,
// garage door or floor hatch to fit the lock; the CodeLock component and
// CodeLockHud handle everything after that.

using UnityEngine;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Code Lock", fileName = "Item_CodeLock")]
    public class CodeLockItem : ItemDefinition
    {
    }
}
