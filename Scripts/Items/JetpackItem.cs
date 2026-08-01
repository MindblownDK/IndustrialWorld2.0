// Assets/Scripts/VoxelEngine/Items/JetpackItem.cs
//
// Player equipment item for Jetpack Families. Runtime fuel/charge is stored on the
// ItemStack.durability field (0..fuelCapacity).
//
// Hydrogen / Hybrid packs recharge from refillable Hydrogen Canisters in inventory
// once fuel drops to the recharge threshold (default 10%).
// Atmospheric / Hybrid packs still siphon Charged Cells for the power side.

using UnityEngine;

namespace VoxelEngine.Items
{
    public enum JetpackFamily
    {
        HydrogenBoost = 0,
        Atmospheric = 1,
        Hybrid = 2,
    }

    [CreateAssetMenu(menuName = "Voxel Engine/Items/Jetpack Item", fileName = "Jetpack_New")]
    public class JetpackItem : ItemDefinition
    {
        [Header("Jetpack")]
        public JetpackFamily family = JetpackFamily.HydrogenBoost;
        [Tooltip("Base 6DOF movement speed multiplier while this pack enables flight.")]
        public float flightSpeedMultiplier = 1f;
        [Tooltip("Extra sprint/boost multiplier while holding Sprint in fly mode.")]
        public float boostMultiplier = 1f;
        [Tooltip("True when this pack can operate in atmosphere.")]
        public bool supportsAtmosphere = true;
        [Tooltip("True when this pack can operate without atmosphere.")]
        public bool supportsVacuum = false;
        [Tooltip("True for hydrogen-fuelled packs (Hydrogen Boost / Hybrid).")]
        public bool usesHydrogen = false;
        [Tooltip("True for power/ion packs (Atmospheric / Hybrid).")]
        public bool usesPower = false;

        [Header("Fuel / Charge")]
        [Tooltip("Maximum fuel/charge units stored on the equipped stack (durability).")]
        public int fuelCapacity = 100;
        [Tooltip("Fuel units drained per second while flying (not boosting).")]
        public float drainPerSecond = 2.5f;
        [Tooltip("Extra fuel units drained per second while boosting (Sprint).")]
        public float boostDrainPerSecond = 4f;
        [Tooltip("When remaining fuel fraction drops to this value (or below), auto-recharge from inventory canisters/cells.")]
        [Range(0.01f, 0.5f)]
        public float autoRechargeThreshold = 0.10f;
        [Tooltip("Fuel restored by one Charged Cell (power packs).")]
        public int chargedCellRefuel = 35;

        public override bool IsStackable => false;

        public int FuelCapacity => Mathf.Max(1, fuelCapacity);
        public float RechargeThreshold => Mathf.Clamp(autoRechargeThreshold, 0.01f, 0.5f);

        public static bool IsPowerFuelItem(ItemDefinition item)
            => item != null && (item.itemId == "item_charged_cell" || item.itemId == "item_energy_cell");

        public bool NeedsHydrogen => usesHydrogen;
        public bool NeedsPower => usesPower;
    }
}
