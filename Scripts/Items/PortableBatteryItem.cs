// Assets/Scripts/VoxelEngine/Items/PortableBatteryItem.cs
//
// Portable Battery — rechargeable power bank for Atmospheric / Hybrid jetpacks.
// Charge stored on ItemStack.durability (ml / Wh). Filled from world Battery blocks.
// Jetpacks draw from inventory batteries the same way they draw H₂ from tanks.

using UnityEngine;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Portable Battery", fileName = "Item_PortableBattery")]
    public class PortableBatteryItem : ItemDefinition
    {
        public const string ItemId = "item_portable_battery";
        public const string LegacyBatteryId = "item_portable_battery_legacy";

        [Header("Battery")]
        [Tooltip("Maximum charge in millilitres (ml) — treated as Wh for display.")]
        public int capacityMl = 3000;

        [Tooltip("Charge transferred per interaction tick from a world Battery block.")]
        public float fillRateMlPerUse = 300f;

        public override bool IsStackable => false;

        public int CapacityMl => Mathf.Max(1, capacityMl);

        public static bool IsPortableBattery(ItemDefinition item)
        {
            if (item == null) return false;
            if (item is PortableBatteryItem) return true;
            if (string.IsNullOrEmpty(item.itemId)) return false;
            return item.itemId == ItemId || item.itemId == LegacyBatteryId;
        }

        public static int GetStoredMl(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty || !IsPortableBattery(stack.item)) return 0;
            return Mathf.Max(0, stack.durability);
        }

        public static int GetCapacityMl(ItemStack stack)
        {
            if (stack?.item is PortableBatteryItem b) return b.CapacityMl;
            if (stack?.item != null && stack.item.itemId == LegacyBatteryId) return 3000;
            return 3000;
        }

        public static float Fill01(ItemStack stack)
        {
            int cap = GetCapacityMl(stack);
            return cap > 0 ? Mathf.Clamp01(GetStoredMl(stack) / (float)cap) : 0f;
        }

        public static int TryAddMl(ItemStack stack, int amountMl)
        {
            if (stack == null || stack.IsEmpty || amountMl <= 0 || !IsPortableBattery(stack.item)) return 0;
            int cap = GetCapacityMl(stack);
            int space = cap - Mathf.Max(0, stack.durability);
            int add = Mathf.Min(space, amountMl);
            stack.durability = Mathf.Max(0, stack.durability) + add;
            return add;
        }

        public static int TryTakeMl(ItemStack stack, int amountMl)
        {
            if (stack == null || stack.IsEmpty || amountMl <= 0 || !IsPortableBattery(stack.item)) return 0;
            int have = Mathf.Max(0, stack.durability);
            int take = Mathf.Min(have, amountMl);
            stack.durability = have - take;
            return take;
        }

        public static float DefaultFillRateMl(ItemDefinition item)
        {
            if (item is PortableBatteryItem b) return Mathf.Max(1f, b.fillRateMlPerUse);
            return 300f;
        }
    }
}
