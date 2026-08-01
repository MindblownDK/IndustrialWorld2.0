// Assets/Scripts/VoxelEngine/Items/JetpackItem.cs
//
// Player equipment item for Jetpack Families. Runtime fuel is stored on
// ItemStack.durability as millilitres (0..fuelCapacityMl).
//
// Hydrogen / Hybrid packs recharge from Portable Hydrogen Tanks in inventory
// once fuel drops to the recharge threshold (default 10%).
// Atmospheric / Hybrid packs siphon Charged Cells for the power side.

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

        [Header("Fuel (metric — millilitres)")]
        [Tooltip("Internal fuel tank capacity in millilitres (ml).")]
        public int fuelCapacityMl = 1000;
        [Tooltip("Millilitres drained per second while flying (not boosting).")]
        public float drainMlPerSecond = 25f;
        [Tooltip("Extra ml/s drained while boosting (Sprint).")]
        public float boostDrainMlPerSecond = 40f;
        [Tooltip("When remaining fuel fraction drops to this value (or below), auto-recharge from inventory tanks/cells.")]
        [Range(0.01f, 0.5f)]
        public float autoRechargeThreshold = 0.10f;
        [Tooltip("Millilitres restored by one Charged Cell (power packs).")]
        public int chargedCellRefuelMl = 350;

        // ── Back-compat aliases (older code / wizard) ─────────────────
        public int fuelCapacity
        {
            get => fuelCapacityMl;
            set => fuelCapacityMl = value;
        }
        public float drainPerSecond
        {
            get => drainMlPerSecond;
            set => drainMlPerSecond = value;
        }
        public float boostDrainPerSecond
        {
            get => boostDrainMlPerSecond;
            set => boostDrainMlPerSecond = value;
        }
        public int chargedCellRefuel
        {
            get => chargedCellRefuelMl;
            set => chargedCellRefuelMl = value;
        }

        public override bool IsStackable => false;

        public int FuelCapacityMl => Mathf.Max(1, fuelCapacityMl);
        public int FuelCapacity => FuelCapacityMl;
        public float RechargeThreshold => Mathf.Clamp(autoRechargeThreshold, 0.01f, 0.5f);

        public static bool IsPowerFuelItem(ItemDefinition item)
            => item != null && (item.itemId == "item_charged_cell" || item.itemId == "item_energy_cell");

        public bool NeedsHydrogen => usesHydrogen;
        public bool NeedsPower => usesPower;
    }
}
