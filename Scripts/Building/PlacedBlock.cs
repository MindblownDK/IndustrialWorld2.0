// Assets/Scripts/VoxelEngine/Building/PlacedBlock.cs
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Building
{
    /// <summary>
    /// Marker + state for a player-placed block in the world. The owning prefab is the
    /// BlockItem.placedPrefab. Damage is reduced by mining hits; on death the block is
    /// destroyed and (optionally) drops itself back to the player.
    /// </summary>
    public class PlacedBlock : MonoBehaviour
    {
        public BlockItem Item;            // assigned at placement time
        public int       Hp = 100;
        public bool      onGrid = true;

        public void Damage(int amount, Inventory recipient)
        {
            Hp -= amount;
            if (Hp <= 0)
            {
                if (recipient != null && Item != null)
                {
                    var customDrop = GetComponentInChildren<ICustomBlockDrop>();
                    var stack = customDrop != null ? customDrop.CreateBlockDrop(Item) : new ItemStack(Item, 1);
                    if (stack != null && !stack.IsEmpty)
                    {
                        var leftover = recipient.container != null ? recipient.container.Insert(stack) : stack;
                        if (leftover != null && leftover.count > 0)
                            DroppedItem.Spawn(leftover, transform.position + Vector3.up * 0.6f, Vector3.up);
                    }
                }
                Destroy(gameObject);
            }
        }
    }
}
