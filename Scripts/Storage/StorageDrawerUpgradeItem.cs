// Assets/Scripts/VoxelEngine/Storage/StorageDrawerUpgradeItem.cs
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    public enum StorageDrawerUpgradeKind { Void, StackLimit }

    [CreateAssetMenu(menuName = "Voxel Engine/Storage/Drawer Upgrade", fileName = "DrawerUpgrade_New")]
    public class StorageDrawerUpgradeItem : ItemDefinition
    {
        public StorageDrawerUpgradeKind upgradeKind = StorageDrawerUpgradeKind.StackLimit;
        [Min(1)] public int stackMultiplier = 1;
    }
}
