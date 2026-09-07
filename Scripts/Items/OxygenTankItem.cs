// Assets/Scripts/VoxelEngine/Items/OxygenTankItem.cs
//
// Equipment item for player life support. The tank is a REAL refillable reserve:
// its remaining oxygen is per-instance state stored on the ItemStack, so a tank
// you drained on a spacewalk stays drained until you refill it at a vent, an
// oxygen tank dock, or a pressurised room.
//
// Storage layout (additive, save-compatible):
//   • oxygen litres → ItemStack.durability
// Legacy stacks written before 9.28.0 have durability 0. Rather than punishing
// them with a dead tank, TankLitres() treats an untouched legacy stack as full
// (see IsUninitialised) so existing saves keep working exactly as before.

using UnityEngine;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Oxygen Tank Item", fileName = "OxygenTank_New")]
    public class OxygenTankItem : ItemDefinition
    {
        [Header("Life Support")]
        [Tooltip("Extra oxygen reserve added to the player's oxygen bar when equipped with a sealed helmet.")]
        public float bonusOxygen = 180f;

        [Tooltip("Multiplier applied to underwater/vacuum oxygen drain. Lower is better.")]
        [Range(0.1f, 1f)] public float drainMultiplier = 0.55f;

        [Header("Refillable Reserve")]
        [Tooltip("Litres of oxygen this tank holds when completely full.")]
        public float capacityLitres = 600f;

        [Tooltip("Litres consumed per second of life-support use while sealed in a hostile atmosphere.")]
        public float litresPerSecond = 1.2f;

        public override bool IsStackable => false;

        // ── Per-instance reserve helpers ───────────────────────────────────────

        /// <summary>Sentinel meaning "this stack predates the refillable tank system".</summary>
        private const int UninitialisedMarker = 0;

        public static bool IsOxygenTank(ItemStack stack)
            => stack != null && !stack.IsEmpty && stack.item is OxygenTankItem;

        /// <summary>Full capacity of the tank in this stack (0 when not a tank).</summary>
        public static float CapacityLitres(ItemStack stack)
            => IsOxygenTank(stack) ? Mathf.Max(1f, ((OxygenTankItem)stack.item).capacityLitres) : 0f;

        /// <summary>
        /// Oxygen litres currently in the tank. A legacy stack that has never been
        /// written reads as FULL so old saves are not silently emptied.
        /// </summary>
        public static float StoredLitres(ItemStack stack)
        {
            if (!IsOxygenTank(stack)) return 0f;
            if (stack.durability == UninitialisedMarker) return CapacityLitres(stack);
            // Stored as (litres + 1) so a genuinely empty tank is distinguishable
            // from a legacy uninitialised one.
            return Mathf.Clamp(stack.durability - 1, 0f, CapacityLitres(stack));
        }

        /// <summary>Writes the tank's remaining litres back onto the stack.</summary>
        public static void SetStoredLitres(ItemStack stack, float litres)
        {
            if (!IsOxygenTank(stack)) return;
            float clamped = Mathf.Clamp(litres, 0f, CapacityLitres(stack));
            stack.durability = Mathf.RoundToInt(clamped) + 1;
        }

        /// <summary>Fill fraction 0..1, for gauges.</summary>
        public static float Fill01(ItemStack stack)
        {
            float cap = CapacityLitres(stack);
            return cap > 0f ? Mathf.Clamp01(StoredLitres(stack) / cap) : 0f;
        }

        /// <summary>Adds oxygen, returns the litres actually accepted.</summary>
        public static float AddLitres(ItemStack stack, float litres)
        {
            if (!IsOxygenTank(stack) || litres <= 0f) return 0f;
            float have = StoredLitres(stack);
            float space = Mathf.Max(0f, CapacityLitres(stack) - have);
            float added = Mathf.Min(space, litres);
            if (added > 0f) SetStoredLitres(stack, have + added);
            return added;
        }

        /// <summary>Removes oxygen, returns the litres actually drawn.</summary>
        public static float TakeLitres(ItemStack stack, float litres)
        {
            if (!IsOxygenTank(stack) || litres <= 0f) return 0f;
            float have = StoredLitres(stack);
            float taken = Mathf.Min(have, litres);
            if (taken > 0f) SetStoredLitres(stack, have - taken);
            return taken;
        }
    }
}
