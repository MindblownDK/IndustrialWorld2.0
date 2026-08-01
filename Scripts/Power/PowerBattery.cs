// Assets/Scripts/VoxelEngine/Power/PowerBattery.cs
//
// World battery block. Stores network energy (Wh) and doubles as a device
// charger: the dock slot accepts Portable Batteries and power-fed jetpacks
// (Atmospheric / Hybrid) and trickles block charge into the item's cell.
//
// Units: 1 item charge unit = 1 Wh — items move energy 1:1 with the block.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Power
{
    public class PowerBattery : PowerNode
    {
        public override PowerNodeKind Kind => PowerNodeKind.Battery;

        public float capacityWattHours = 1000f;
        public float charge;
        [Tooltip("Max watts/sec the battery can charge or discharge.")]
        public float ioRate = 200f;

        [Header("Device Charger")]
        [Tooltip("Watts/sec trickled into the docked item (Portable Battery or power jetpack).")]
        public float itemChargeRateWatts = 500f;

        // ── Live flow telemetry (written every PowerNetworkManager tick) ──
        // Non-serialized: derived values for the UI, recomputed on every network tick.
        [System.NonSerialized] public float lastChargeInW;
        [System.NonSerialized] public float lastDischargeOutW;

        /// <summary>Dock for chargeable electrical items. Created lazily (runtime),
        /// so existing prefabs need no re-serialization.</summary>
        public ItemContainer ChargeSlot { get; private set; }

        public float Fill01 => capacityWattHours > 0f ? Mathf.Clamp01(charge / capacityWattHours) : 0f;

        /// <summary>True while the dock is actively pushing energy into an item.</summary>
        public bool IsChargingItem { get; private set; }

        private void Awake() => EnsureContainers();

        private void Update()
        {
            TickDeviceCharger(Time.deltaTime);
        }

        public void EnsureContainers()
        {
            if (ChargeSlot == null)
            {
                ChargeSlot = new ItemContainer("Device Charger", 1);
            }
            else ChargeSlot.Resize(1);
            ChargeSlot.AcceptFilter = (item, wanted) =>
            {
                if (PortableBatteryItem.IsPortableBattery(item)) return Mathf.Min(1, wanted);
                if (item is JetpackItem jp && jp.UsesPowerEffective) return Mathf.Min(1, wanted);
                return 0;
            };
        }

        /// <summary>Push block charge into the docked item. Returns Wh moved this tick.</summary>
        public float TickDeviceCharger(float dt)
        {
            IsChargingItem = false;
            EnsureContainers();
            if (dt <= 0f || charge <= 0f) return 0f;

            var stack = ChargeSlot.GetSlot(0);
            if (stack == null || stack.IsEmpty || stack.item == null) return 0f;

            float want = Mathf.Max(1f, itemChargeRateWatts) * dt;
            float moved = 0f;

            if (PortableBatteryItem.IsPortableBattery(stack.item))
            {
                int space = PortableBatteryItem.GetCapacityMl(stack) - PortableBatteryItem.GetStoredMl(stack);
                if (space <= 0) return 0f;
                int add = Mathf.Min(space, Mathf.FloorToInt(Mathf.Min(want, charge)));
                if (add <= 0) return 0f;
                PortableBatteryItem.TryAddMl(stack, add);
                moved = add;
            }
            else if (stack.item is JetpackItem jp && jp.UsesPowerEffective)
            {
                int space = jp.PowerCapacityMl - JetpackItem.GetPowerMl(stack);
                if (space <= 0) return 0f;
                int add = Mathf.Min(space, Mathf.FloorToInt(Mathf.Min(want, charge)));
                if (add <= 0) return 0f;
                JetpackItem.AddPower(stack, add);
                moved = add;
            }
            else return 0f;

            charge = Mathf.Max(0f, charge - moved);
            ChargeSlot.SetSlot(0, stack);
            IsChargingItem = true;
            return moved;
        }

        /// <summary>Charge state of the docked item (for the UI), or -1 when empty.</summary>
        public float DockedItemFill01
        {
            get
            {
                EnsureContainers();
                var stack = ChargeSlot.GetSlot(0);
                if (stack == null || stack.IsEmpty || stack.item == null) return -1f;
                if (PortableBatteryItem.IsPortableBattery(stack.item)) return PortableBatteryItem.Fill01(stack);
                if (stack.item is JetpackItem jp && jp.UsesPowerEffective)
                    return Mathf.Clamp01(JetpackItem.GetPowerMl(stack) / (float)Mathf.Max(1, jp.PowerCapacityMl));
                return -1f;
            }
        }

        /// <summary>Stored/capacity of the docked item (for the UI).</summary>
        public void GetDockedItemCharge(out int stored, out int capacity)
        {
            stored = 0; capacity = 0;
            EnsureContainers();
            var stack = ChargeSlot.GetSlot(0);
            if (stack == null || stack.IsEmpty || stack.item == null) return;
            if (PortableBatteryItem.IsPortableBattery(stack.item))
            {
                stored = PortableBatteryItem.GetStoredMl(stack);
                capacity = PortableBatteryItem.GetCapacityMl(stack);
            }
            else if (stack.item is JetpackItem jp && jp.UsesPowerEffective)
            {
                stored = JetpackItem.GetPowerMl(stack);
                capacity = jp.PowerCapacityMl;
            }
        }
    }
}
