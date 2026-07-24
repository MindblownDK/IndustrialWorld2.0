// Assets/Scripts/VoxelEngine/Building/PlacedBlock.cs
using System.Collections.Generic;
using System.Reflection;
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
                DrainInventoriesToPlayerThenWorld(recipient);
                if (Item != null)
                {
                    var customDrop = GetComponentInChildren<ICustomBlockDrop>();
                    var stack = customDrop != null ? customDrop.CreateBlockDrop(Item) : new ItemStack(Item, 1);
                    GiveToPlayerThenDrop(stack, recipient, transform.position + Vector3.up * 0.6f);
                }
                var gridBlock = GetComponent<VoxelEngine.GridSystem.GridBlock>();
                if (gridBlock != null && gridBlock.IsPrecisionAttachment && gridBlock.Grid != null)
                    gridBlock.Grid.GetComponent<VoxelEngine.GridSystem.GridPrecisionAttachmentLayer>()?.RemoveBlock(gridBlock.PrecisionGridPos);
                else
                    Destroy(gameObject);
            }
        }

        private void DrainInventoriesToPlayerThenWorld(Inventory recipient)
        {
            var seen = new HashSet<ItemContainer>();
            foreach (var component in GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var type = component.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var field in type.GetFields(flags))
                {
                    if (!typeof(ItemContainer).IsAssignableFrom(field.FieldType)) continue;
                    try
                    {
                        if (field.GetValue(component) is ItemContainer container && seen.Add(container))
                            DrainContainer(container, recipient);
                    }
                    catch { }
                }
                foreach (var property in type.GetProperties(flags))
                {
                    if (!typeof(ItemContainer).IsAssignableFrom(property.PropertyType)) continue;
                    if (property.GetIndexParameters().Length != 0 || !property.CanRead) continue;
                    try
                    {
                        if (property.GetValue(component) is ItemContainer container && seen.Add(container))
                            DrainContainer(container, recipient);
                    }
                    catch { }
                }
            }
        }

        private void DrainContainer(ItemContainer container, Inventory recipient)
        {
            if (container == null) return;
            for (int i = 0; i < container.Size; i++)
            {
                var stack = container.GetSlot(i);
                if (stack == null || stack.IsEmpty) continue;
                GiveToPlayerThenDrop(stack.Clone(), recipient,
                    transform.position + Vector3.up * 0.75f + Random.insideUnitSphere * 0.35f);
                container.SetSlot(i, new ItemStack());
            }
        }

        private static void GiveToPlayerThenDrop(ItemStack stack, Inventory recipient, Vector3 dropPos)
        {
            if (stack == null || stack.IsEmpty) return;
            var moving = stack.Clone();
            ItemStack leftover = moving;
            if (recipient != null && recipient.container != null)
                leftover = recipient.container.Insert(moving);
            if (leftover != null && leftover.count > 0)
                DroppedItem.Spawn(leftover, dropPos, Vector3.up);
        }
    }
}
