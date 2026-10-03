// Assets/Scripts/VoxelEngine/Storage/WirelessTerminalItem.cs
//
// 14.40.0 - The handheld Wireless Terminal. Carrying one in the inventory
// unlocks remote storage access: the STORAGE LINK command in the inventory
// screen, network-backed crafting, and network-paid building-wheel costs -
// all while inside the range of an online Wireless Transmitter whose
// network's Security Block permits wireless use.

using UnityEngine;

namespace VoxelEngine.Storage
{
    [CreateAssetMenu(menuName = "Voxel Engine/Storage/Wireless Terminal", fileName = "Item_WirelessTerminal")]
    public class WirelessTerminalItem : Items.ItemDefinition
    {
        public WirelessTerminalItem()
        {
            maxStack = 1;
            category = "Storage";
        }
    }
}
