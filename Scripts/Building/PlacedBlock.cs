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
                if (recipient != null && Item != null) recipient.Add(Item, 1);
                Destroy(gameObject);
            }
        }
    }
}
