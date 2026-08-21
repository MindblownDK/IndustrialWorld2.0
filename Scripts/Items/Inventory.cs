// Assets/Scripts/VoxelEngine/Items/Inventory.cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// The player's inventory. Slim 10-slot hotbar + 30-slot main pouch (40 total).
    /// Backwards-compatible shim: VoxelEditor still calls inv.Add(item, count).
    /// </summary>
    public class Inventory : MonoBehaviour
    {
        public const int HOTBAR_SIZE   = 10;
        public const int BACKPACK_SIZE = 30;
        public const int TOTAL_SIZE    = HOTBAR_SIZE + BACKPACK_SIZE;

        [Tooltip("Index 0..9 = hotbar, 10..39 = backpack.")]
        public ItemContainer container;

        public int activeHotbarIndex = 0;
        public event Action OnActiveSlotChanged;

        public ItemStack ActiveStack
        {
            get
            {
                EnsureContainer();
                if (activeHotbarIndex < 0 || activeHotbarIndex >= HOTBAR_SIZE) return new ItemStack();
                if (activeHotbarIndex >= container.Slots.Count) return new ItemStack();
                return container.GetSlot(activeHotbarIndex);
            }
        }

        private void Awake() => EnsureContainer();

        // Always-callable; safe regardless of script execution order.
        private void EnsureContainer()
        {
            if (container == null) container = new ItemContainer("Inventory", TOTAL_SIZE);
            else container.Resize(TOTAL_SIZE);
            container.UsePlayerWeightProfile = true;
            // Raw exotic matter (antimatter/dark matter) is never carried by hand —
            // it only travels in pressurized canisters.
            container.allowPlayerCarry = false;
        }

        public float CurrentWeightKg
        {
            get { EnsureContainer(); return container.CurrentWeightKg; }
        }

        public float MaxWeightKg
        {
            get { EnsureContainer(); return container.MaxWeightKg; }
        }

        // Backwards-compat helper for VoxelEditor / old code.
        public void Add(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return;
            var leftover = container.Insert(new ItemStack(item, count));
            if (leftover != null && leftover.count > 0)
            {
                Debug.Log($"[Inventory] Inventory overweight/full — dropped {leftover.count} x {item.displayName}");
                DroppedItem.Spawn(leftover, transform.position + Vector3.up * 0.6f, Vector3.up);
            }
        }

        public int CountOf(ItemDefinition item) => container.CountOf(item);

        public void SetActiveHotbar(int idx)
        {
            int clamped = Mathf.Clamp(idx, 0, HOTBAR_SIZE - 1);
            if (clamped == activeHotbarIndex) return;
            activeHotbarIndex = clamped;
            OnActiveSlotChanged?.Invoke();
        }
    }
}
