// Assets/Scripts/VoxelEngine/Items/LiquidCanister.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║             LIQUID CANISTER (9.16.0) — replaces the Water Bucket      ║
// ║                                                                      ║
// ║  A portable 10 L canister for the 7 liquids. ItemStack.durability    ║
// ║  stores millilitres (0 = empty, 10000 = full); payload holds the     ║
// ║  carried LiquidType (null = empty). Rules:                           ║
// ║                                                                      ║
// ║   • RMB on a liquid pool  → scoops 500 ml per click (same liquid     ║
// ║     only once filled; click until full)                              ║
// ║   • RMB on a liquid tank  → pours 500 ml in (or draws out when       ║
// ║     the canister is empty)                                           ║
// ║   • RMB on a water/marine pump → pours 500 ml into the buffer        ║
// ║   • RMB on an infinity pump jack → fills 500 ml of crude oil from    ║
// ║     the jack pump's infinite reservoir node                          ║
// ║   • LMB on the world      → pours 500 ml onto the hit cell           ║
// ║                                                                      ║
// ║  Save-stable: the itemId stays "water_bucket" so existing worlds     ║
// ║  resolve the same asset; legacy full buckets (durability 1) are      ║
// ║  upgraded to full canisters on first use.                            ║
// ╚══════════════════════════════════════════════════════════════════════╝
using UnityEngine;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Liquid Canister", fileName = "Canister_New")]
    public class LiquidCanister : ToolItem
    {
        /// <summary>Capacity in millilitres (10 L).</summary>
        public const int CapacityMl = 10000;
        /// <summary>Liquid moved per interaction click (500 ml).</summary>
        public const int PerClickMl = 500;
        /// <summary>One click in the tank/pump litre economy.</summary>
        public const float LitresPerClick = PerClickMl / 1000f;
        /// <summary>World mapping: one FULL fluid voxel cell equals one canister (10 L).</summary>
        public const float MlPerFullCell = 10000f;
        /// <summary>Millilitres per fluid-level byte (255 levels per full cell).</summary>
        public const float MlPerCellLevel = MlPerFullCell / 255f;

        public LiquidCanister() { toolType = ToolType.Other; maxDurability = CapacityMl; maxStack = 1; }

        /// <summary>Legacy migration: a pre-9.16.0 "full bucket" (durability 1) reads as a
        /// full canister after the first touch. Buckets from before the liquid payload
        /// existed carried water only, so a missing payload defaults to water.</summary>
        public static void NormalizeLegacy(ItemStack stack)
        {
            if (stack == null || stack.durability <= 0 || stack.durability > 1) return;
            if (!(stack.payload is LiquidType)) stack.payload = LiquidType.Water;
            stack.durability = CapacityMl;
        }

        /// <summary>True when the canister carries nothing.</summary>
        public static bool IsEmpty(ItemStack stack)
        {
            NormalizeLegacy(stack);
            return stack == null || stack.durability <= 0 || !(stack.payload is LiquidType);
        }

        /// <summary>True when the canister is completely full.</summary>
        public static bool IsFull(ItemStack stack)
        {
            NormalizeLegacy(stack);
            return stack != null && stack.durability >= CapacityMl && stack.payload is LiquidType;
        }

        /// <summary>The carried liquid, or null when empty.</summary>
        public static LiquidType? CarriedLiquid(ItemStack stack)
        {
            NormalizeLegacy(stack);
            if (stack == null || stack.durability <= 0 || !(stack.payload is LiquidType t)) return null;
            return t;
        }

        /// <summary>
        /// Add liquid to the canister. Only the SAME liquid may join a partially filled
        /// canister; the first fill sets the type. Returns false when the canister is full
        /// or the liquids don't match.
        /// </summary>
        public static bool AddMl(ItemStack stack, LiquidType liquid, int millilitres)
        {
            NormalizeLegacy(stack);
            if (stack == null || millilitres <= 0) return false;
            var carried = CarriedLiquid(stack);
            if (carried != null && carried.Value != liquid) return false;
            if (stack.durability >= CapacityMl) return false;

            int space = CapacityMl - Mathf.Max(0, stack.durability);
            int add = Mathf.Min(space, millilitres);
            if (add <= 0) return false;
            stack.payload = liquid;
            stack.durability += add;
            return true;
        }

        /// <summary>Remove liquid from the canister. At zero the canister empties (payload cleared).</summary>
        public static bool RemoveMl(ItemStack stack, int millilitres)
        {
            NormalizeLegacy(stack);
            if (stack == null || millilitres <= 0 || stack.durability <= 0 || !(stack.payload is LiquidType)) return false;
            stack.durability = Mathf.Max(0, stack.durability - millilitres);
            if (stack.durability <= 0)
            {
                stack.durability = 0;
                stack.payload = null;
            }
            return true;
        }

        /// <summary>Fluid levels a 500 ml click moves in the voxel world (13 of 255).</summary>
        public static int LevelsPerClick => Mathf.Max(1, Mathf.RoundToInt(PerClickMl / MlPerCellLevel));
    }
}
