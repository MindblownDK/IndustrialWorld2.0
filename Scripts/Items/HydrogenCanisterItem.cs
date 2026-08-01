// Assets/Scripts/VoxelEngine/Items/HydrogenCanisterItem.cs
//
// Refillable portable hydrogen tank for jetpacks. Stored amount lives on
// ItemStack.durability (0..capacity). Fill from a world GasTank holding Hydrogen
// (RMB the tank while holding the canister). H₂ / Hybrid jetpacks auto-siphon
// canisters from inventory when pack fuel drops to the recharge threshold.

using UnityEngine;
using VoxelEngine.Gas;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Hydrogen Canister", fileName = "Item_HydrogenCanister")]
    public class HydrogenCanisterItem : ItemDefinition
    {
        public const string ItemId = "item_hydrogen_canister";

        [Header("Canister")]
        [Tooltip("Maximum hydrogen units this canister can hold (maps 1:1 to stack durability).")]
        public int capacity = 200;

        [Tooltip("How many gas-tank units transfer per fill tick when RMB-filling from a GasTank.")]
        public float fillRatePerUse = 40f;

        public override bool IsStackable => false;

        public int Capacity => Mathf.Max(1, capacity);

        public static bool IsCanister(ItemDefinition item)
            => item is HydrogenCanisterItem
               || (item != null && (item.itemId == ItemId || item.itemId == "item_hydrogen_cell"));

        public static int GetStored(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty || !IsCanister(stack.item)) return 0;
            return Mathf.Max(0, stack.durability);
        }

        public static int GetCapacity(ItemStack stack)
        {
            if (stack?.item is HydrogenCanisterItem c) return c.Capacity;
            // Legacy hydrogen cell assets treated as small disposable-capacity canisters.
            return 40;
        }

        public static float Fill01(ItemStack stack)
        {
            int cap = GetCapacity(stack);
            return cap > 0 ? Mathf.Clamp01(GetStored(stack) / (float)cap) : 0f;
        }

        /// <summary>Add up to <paramref name="amount"/> units. Returns units actually added.</summary>
        public static int TryAdd(ItemStack stack, int amount)
        {
            if (stack == null || stack.IsEmpty || amount <= 0 || !IsCanister(stack.item)) return 0;
            int cap = GetCapacity(stack);
            int space = cap - Mathf.Max(0, stack.durability);
            int add = Mathf.Min(space, amount);
            stack.durability = Mathf.Max(0, stack.durability) + add;
            return add;
        }

        /// <summary>Take up to <paramref name="amount"/> units. Returns units actually taken.</summary>
        public static int TryTake(ItemStack stack, int amount)
        {
            if (stack == null || stack.IsEmpty || amount <= 0 || !IsCanister(stack.item)) return 0;
            int have = Mathf.Max(0, stack.durability);
            int take = Mathf.Min(have, amount);
            stack.durability = have - take;
            return take;
        }

        /// <summary>Pull hydrogen from a world GasTank into this canister stack.</summary>
        public static float FillFromGasTank(ItemStack stack, GasTank tank, float maxTransfer)
        {
            if (stack == null || tank == null || maxTransfer <= 0f) return 0f;
            if (!IsCanister(stack.item)) return 0f;
            int space = GetCapacity(stack) - GetStored(stack);
            if (space <= 0) return 0f;

            float want = Mathf.Min(maxTransfer, space);
            float taken = tank.TryTake(GasType.Hydrogen, want);
            if (taken <= 0f) return 0f;
            TryAdd(stack, Mathf.RoundToInt(taken));
            return taken;
        }
    }
}
