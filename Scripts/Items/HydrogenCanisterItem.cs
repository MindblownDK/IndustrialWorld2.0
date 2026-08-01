// Assets/Scripts/VoxelEngine/Items/HydrogenCanisterItem.cs
//
// Portable Hydrogen Tank — refillable H₂ bottle for jetpacks.
// Fill (ml) is stored on ItemStack.durability (0..capacityMl).
// Fill from a world GasTank holding Hydrogen (RMB tank while holding this).
// H₂ / Hybrid jetpacks auto-siphon inventory tanks when pack fuel ≤ 10%.

using UnityEngine;
using VoxelEngine.Gas;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Portable Hydrogen Tank", fileName = "Item_PortableHydrogenTank")]
    public class HydrogenCanisterItem : ItemDefinition
    {
        public const string ItemId = "item_portable_hydrogen_tank";
        public const string LegacyCanisterId = "item_hydrogen_canister";
        public const string LegacyCellId = "item_hydrogen_cell";

        [Header("Tank")]
        [Tooltip("Maximum hydrogen volume in millilitres (ml).")]
        public int capacityMl = 2000;

        [Tooltip("Millilitres transferred per RMB fill tick from a world Hydrogen Gas Tank.")]
        public float fillRateMlPerUse = 250f;

        public override bool IsStackable => false;

        public int CapacityMl => Mathf.Max(1, capacityMl);

        public static bool IsPortableHydrogenTank(ItemDefinition item)
        {
            if (item == null) return false;
            if (item is HydrogenCanisterItem) return true;
            if (string.IsNullOrEmpty(item.itemId)) return false;
            return item.itemId == ItemId
                || item.itemId == LegacyCanisterId
                || item.itemId == LegacyCellId;
        }

        // Back-compat alias used by older call sites.
        public static bool IsCanister(ItemDefinition item) => IsPortableHydrogenTank(item);

        public static int GetStoredMl(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty || !IsPortableHydrogenTank(stack.item)) return 0;
            return Mathf.Max(0, stack.durability);
        }

        public static int GetCapacityMl(ItemStack stack)
        {
            if (stack?.item is HydrogenCanisterItem c) return c.CapacityMl;
            // Legacy cell/canister assets without the typed script.
            if (stack?.item != null && stack.item.itemId == LegacyCellId) return 400;
            return 2000;
        }

        public static float Fill01(ItemStack stack)
        {
            int cap = GetCapacityMl(stack);
            return cap > 0 ? Mathf.Clamp01(GetStoredMl(stack) / (float)cap) : 0f;
        }

        public static int TryAddMl(ItemStack stack, int amountMl)
        {
            if (stack == null || stack.IsEmpty || amountMl <= 0 || !IsPortableHydrogenTank(stack.item)) return 0;
            int cap = GetCapacityMl(stack);
            int space = cap - Mathf.Max(0, stack.durability);
            int add = Mathf.Min(space, amountMl);
            stack.durability = Mathf.Max(0, stack.durability) + add;
            return add;
        }

        public static int TryTakeMl(ItemStack stack, int amountMl)
        {
            if (stack == null || stack.IsEmpty || amountMl <= 0 || !IsPortableHydrogenTank(stack.item)) return 0;
            int have = Mathf.Max(0, stack.durability);
            int take = Mathf.Min(have, amountMl);
            stack.durability = have - take;
            return take;
        }

        // Legacy names used by 6.73 call sites.
        public static int GetStored(ItemStack stack) => GetStoredMl(stack);
        public static int GetCapacity(ItemStack stack) => GetCapacityMl(stack);
        public static int TryAdd(ItemStack stack, int amount) => TryAddMl(stack, amount);
        public static int TryTake(ItemStack stack, int amount) => TryTakeMl(stack, amount);

        /// <summary>Pull hydrogen from a world GasTank into this portable tank (ml).</summary>
        public static float FillFromGasTank(ItemStack stack, GasTank tank, float maxTransferMl)
        {
            if (stack == null || tank == null || maxTransferMl <= 0f) return 0f;
            if (!IsPortableHydrogenTank(stack.item)) return 0f;
            int space = GetCapacityMl(stack) - GetStoredMl(stack);
            if (space <= 0) return 0f;

            float want = Mathf.Min(maxTransferMl, space);
            // GasTank units map 1:1 to ml for player equipment.
            float taken = tank.TryTake(GasType.Hydrogen, want);
            if (taken <= 0f) return 0f;
            TryAddMl(stack, Mathf.RoundToInt(taken));
            return taken;
        }

        public static float DefaultFillRateMl(ItemDefinition item)
        {
            if (item is HydrogenCanisterItem c) return Mathf.Max(1f, c.fillRateMlPerUse);
            return 250f;
        }
    }
}
