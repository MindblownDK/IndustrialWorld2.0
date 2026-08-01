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
        [Tooltip("Internal hydrogen fuel tank capacity in millilitres (ml).")]
        public int fuelCapacityMl = 1000;
        [Tooltip("Internal power cell capacity in watt-hours (Wh) for packs that run on " +
                 "power (Atmospheric / Hybrid). The Hybrid flies and boosts on H₂, but can " +
                 "also cruise on this cell alone — shift boost stays H₂-only.")]
        public int powerCapacityMl = 0;
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

        public int FuelCapacityMl
        {
            get
            {
                // Migrate tiny pre-ml capacities (e.g. 80) into millilitres.
                int v = fuelCapacityMl;
                if (v > 0 && v < 50) v *= 10;
                if (v <= 0) v = 1000;
                return v;
            }
        }
        public int FuelCapacity => FuelCapacityMl;
        public float RechargeThreshold
        {
            get
            {
                float t = autoRechargeThreshold;
                if (t <= 0.001f) t = 0.10f; // old assets
                return Mathf.Clamp(t, 0.01f, 0.5f);
            }
        }

        public static bool IsPowerFuelItem(ItemDefinition item)
            => item != null && (item.itemId == "item_charged_cell" || item.itemId == "item_energy_cell");

        public bool NeedsHydrogen => usesHydrogen;
        public bool NeedsPower => usesPower;

        // ════════════════════════════════════════════════════════════
        //   DUAL-FUEL POOLS (11.3+) — H₂ tank + power cell per pack
        // ════════════════════════════════════════════════════════════
        // Capability flags are authoritative, but legacy assets may not have
        // them set — fall back to the family so old packs keep working.
        public bool UsesHydrogenEffective =>
            usesHydrogen || family == JetpackFamily.HydrogenBoost || family == JetpackFamily.Hybrid;
        public bool UsesPowerEffective =>
            usesPower || family == JetpackFamily.Atmospheric || family == JetpackFamily.Hybrid;

        /// <summary>Hydrogen tank capacity (ml) for this pack — 0 when it burns no H₂.</summary>
        public int HydrogenCapacityMl => UsesHydrogenEffective ? FuelCapacityMl : 0;

        /// <summary>Power cell capacity (Wh) for this pack — 0 when it draws no power.</summary>
        public int PowerCapacityMl
        {
            get
            {
                if (!UsesPowerEffective) return 0;
                if (powerCapacityMl > 0) return powerCapacityMl;
                // Legacy assets predate the field: pure-power packs stored charge in the
                // old single-pool capacity, hybrids get a modest emergency cell.
                if (UsesHydrogenEffective) return 600;
                return Mathf.Max(1, FuelCapacityMl);
            }
        }

        // ── Per-stack pool access ──────────────────────────────────
        // Storage layout (save-compatible):
        //   • H₂ pool        → ItemStack.durability (packs that use H₂)
        //   • power pool     → ItemStack.charge on hybrids, ItemStack.durability
        //                      on pure-power packs (legacy stacks stay valid).
        public static int GetH2Ml(ItemStack s)
            => s == null || s.IsEmpty || s.item is not JetpackItem p || !p.UsesHydrogenEffective
                ? 0 : Mathf.Max(0, s.durability);

        public static int GetPowerMl(ItemStack s)
        {
            if (s == null || s.IsEmpty || s.item is not JetpackItem p || !p.UsesPowerEffective) return 0;
            return Mathf.Max(0, p.UsesHydrogenEffective ? s.charge : s.durability);
        }

        public static void SetH2Ml(ItemStack s, int ml)
        {
            if (s == null || s.IsEmpty || s.item is not JetpackItem p || !p.UsesHydrogenEffective) return;
            s.durability = Mathf.Max(0, ml);
        }

        public static void SetPowerMl(ItemStack s, int ml)
        {
            if (s == null || s.IsEmpty || s.item is not JetpackItem p || !p.UsesPowerEffective) return;
            if (p.UsesHydrogenEffective) s.charge = Mathf.Max(0, ml);
            else s.durability = Mathf.Max(0, ml);
        }

        /// <summary>Adds H₂ up to tank capacity. Returns ml actually stored.</summary>
        public static int AddH2(ItemStack s, int ml)
        {
            if (ml <= 0 || s == null || s.IsEmpty || s.item is not JetpackItem p) return 0;
            int space = Mathf.Max(0, p.HydrogenCapacityMl - GetH2Ml(s));
            int add = Mathf.Min(space, ml);
            if (add > 0) SetH2Ml(s, GetH2Ml(s) + add);
            return add;
        }

        /// <summary>Adds power up to cell capacity. Returns Wh actually stored.</summary>
        public static int AddPower(ItemStack s, int ml)
        {
            if (ml <= 0 || s == null || s.IsEmpty || s.item is not JetpackItem p) return 0;
            int space = Mathf.Max(0, p.PowerCapacityMl - GetPowerMl(s));
            int add = Mathf.Min(space, ml);
            if (add > 0) SetPowerMl(s, GetPowerMl(s) + add);
            return add;
        }

        /// <summary>Removes up to <paramref name="ml"/> of H₂. Returns ml taken.</summary>
        public static int TakeH2(ItemStack s, int ml)
        {
            if (ml <= 0) return 0;
            int have = GetH2Ml(s);
            int take = Mathf.Min(have, ml);
            if (take > 0) SetH2Ml(s, have - take);
            return take;
        }

        /// <summary>Removes up to <paramref name="ml"/> of power. Returns Wh taken.</summary>
        public static int TakePower(ItemStack s, int ml)
        {
            if (ml <= 0) return 0;
            int have = GetPowerMl(s);
            int take = Mathf.Min(have, ml);
            if (take > 0) SetPowerMl(s, have - take);
            return take;
        }
    }
}
