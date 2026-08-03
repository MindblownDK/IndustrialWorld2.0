// Assets/Scripts/VoxelEngine/GridSystem/GridBattery.cs
//
// Grid battery — stores and releases power for the ship grid.
// v6.9.0-dev — transfer is now coordinated centrally by GridEntity so batteries
// can charge/discharge each other reliably and explicit modes behave deterministically.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public enum GridBatteryMode
    {
        Auto,
        Recharge,
        Discharge
    }

    public class GridBattery : GridBlock, IGridDataProvider
    {
        [Header("Battery")]
        public float capacityWh = 1000f;
        public float storedWh;
        public float maxChargeRate = 500f; // W
        public float maxDischargeRate = 500f; // W
        public GridBatteryMode mode = GridBatteryMode.Auto;

        [Header("Device Charger")]
        [Tooltip("Watts moved from this grid battery into one docked rechargeable player item.")]
        public float itemChargeRateWatts = 500f;

        /// <summary>One-item dock for a Portable Battery or power-fed jetpack.</summary>
        public ItemContainer ChargeSlot { get; private set; }
        public bool IsChargingItem { get; private set; }
        public float CurrentDeviceChargeWatts { get; private set; }

        public float Fill01 => capacityWh > 0 ? Mathf.Clamp01(storedWh / capacityWh) : 0f;

        public float CurrentChargeWatts { get; private set; }
        public float CurrentDischargeWatts { get; private set; }
        public float NetTransferWatts => CurrentDischargeWatts - CurrentChargeWatts;
        public bool IsCharging => CurrentChargeWatts > 0.01f;
        public bool IsDischarging => CurrentDischargeWatts > 0.01f;
        public string TransferState => IsDischarging ? "DISCHARGING" : IsCharging ? "CHARGING" : "IDLE";

        public bool CanCharge => Enabled && mode != GridBatteryMode.Discharge && storedWh < capacityWh - 0.001f;
        public bool CanDischarge => Enabled && mode != GridBatteryMode.Recharge && storedWh > 0.001f;

        public override float PowerOutput => 0f;

        private void Awake() => EnsureContainers();

        private void Update()
        {
            TickDeviceCharger(Time.deltaTime);
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            EnsureContainers();
        }

        /// <summary>Creates/repairs the runtime charger dock without modifying the authored prefab.</summary>
        public void EnsureContainers()
        {
            if (ChargeSlot == null) ChargeSlot = new ItemContainer("Grid Battery Charger", 1);
            else ChargeSlot.Resize(1);

            ChargeSlot.AcceptFilter = (item, wanted) => IsRechargeablePlayerItem(item)
                ? Mathf.Min(1, wanted)
                : 0;
        }

        public static bool IsRechargeablePlayerItem(ItemDefinition item)
        {
            if (PortableBatteryItem.IsPortableBattery(item)) return true;
            return item is JetpackItem jetpack && jetpack.UsesPowerEffective;
        }

        /// <summary>
        /// Charges the supplied held item directly from stored grid energy. Shift callers
        /// pass <paramref name="fillFully"/> to fill all available capacity in one action.
        /// </summary>
        public bool TryChargeHandheld(ItemStack stack, bool fillFully, out int addedWh, out int storedWh, out int capacityWh)
        {
            addedWh = 0;
            storedWh = 0;
            capacityWh = 0;
            if (!Enabled || stack == null || stack.IsEmpty || stack.item == null || this.storedWh < 0f) return false;
            if (!TryGetRechargeState(stack, out int stored, out int capacity, out int space)) return false;

            int request = fillFully
                ? space
                : Mathf.Min(space, Mathf.Max(1, Mathf.RoundToInt(DefaultHandheldChargeRate(stack.item))));
            int available = Mathf.FloorToInt(Mathf.Max(0f, this.storedWh));
            int moved = Mathf.Min(request, available);
            if (moved <= 0)
            {
                storedWh = stored;
                capacityWh = capacity;
                return false;
            }

            if (PortableBatteryItem.IsPortableBattery(stack.item))
                addedWh = PortableBatteryItem.TryAddMl(stack, moved);
            else if (stack.item is JetpackItem jetpack && jetpack.UsesPowerEffective)
            {
                JetpackItem.AddPower(stack, moved);
                addedWh = moved;
            }

            if (addedWh <= 0)
            {
                storedWh = stored;
                capacityWh = capacity;
                return false;
            }

            this.storedWh = Mathf.Max(0f, this.storedWh - addedWh);
            CurrentDeviceChargeWatts = addedWh / Mathf.Max(Time.deltaTime, 0.0001f);
            IsChargingItem = true;
            TryGetRechargeState(stack, out storedWh, out capacityWh, out _);
            return true;
        }

        /// <summary>Pushes stored grid energy into the docked rechargeable item.</summary>
        public float TickDeviceCharger(float dt)
        {
            IsChargingItem = false;
            CurrentDeviceChargeWatts = 0f;
            EnsureContainers();
            if (!Enabled || dt <= 0f || storedWh < 1f) return 0f;

            var stack = ChargeSlot.GetSlot(0);
            if (stack == null || stack.IsEmpty || stack.item == null) return 0f;
            if (!TryGetRechargeState(stack, out _, out _, out int space) || space <= 0) return 0f;

            int wanted = Mathf.Min(space, Mathf.FloorToInt(Mathf.Max(1f, itemChargeRateWatts) * dt));
            int available = Mathf.FloorToInt(storedWh);
            int moved = Mathf.Min(wanted, available);
            if (moved <= 0) return 0f;

            int added = PortableBatteryItem.IsPortableBattery(stack.item)
                ? PortableBatteryItem.TryAddMl(stack, moved)
                : stack.item is JetpackItem jetpack && jetpack.UsesPowerEffective
                    ? AddJetpackPower(stack, moved)
                    : 0;
            if (added <= 0) return 0f;

            storedWh = Mathf.Max(0f, storedWh - added);
            CurrentDeviceChargeWatts = added / dt;
            IsChargingItem = true;
            return added;
        }

        public float DockedItemFill01
        {
            get
            {
                EnsureContainers();
                var stack = ChargeSlot.GetSlot(0);
                return TryGetRechargeState(stack, out int stored, out int capacity, out _)
                    ? Mathf.Clamp01(stored / (float)Mathf.Max(1, capacity))
                    : -1f;
            }
        }

        public void GetDockedItemCharge(out int storedWh, out int capacityWh)
        {
            EnsureContainers();
            var stack = ChargeSlot.GetSlot(0);
            if (!TryGetRechargeState(stack, out storedWh, out capacityWh, out _))
            {
                storedWh = 0;
                capacityWh = 0;
            }
        }

        private static int AddJetpackPower(ItemStack stack, int amount)
        {
            if (stack?.item is not JetpackItem jetpack || !jetpack.UsesPowerEffective || amount <= 0) return 0;
            int before = JetpackItem.GetPowerMl(stack);
            JetpackItem.AddPower(stack, amount);
            return Mathf.Max(0, JetpackItem.GetPowerMl(stack) - before);
        }

        private static float DefaultHandheldChargeRate(ItemDefinition item)
        {
            if (PortableBatteryItem.IsPortableBattery(item)) return PortableBatteryItem.DefaultFillRateMl(item);
            return 400f;
        }

        private static bool TryGetRechargeState(ItemStack stack, out int stored, out int capacity, out int space)
        {
            stored = 0;
            capacity = 0;
            space = 0;
            if (stack == null || stack.IsEmpty || stack.item == null) return false;
            if (PortableBatteryItem.IsPortableBattery(stack.item))
            {
                stored = PortableBatteryItem.GetStoredMl(stack);
                capacity = PortableBatteryItem.GetCapacityMl(stack);
            }
            else if (stack.item is JetpackItem jetpack && jetpack.UsesPowerEffective)
            {
                stored = JetpackItem.GetPowerMl(stack);
                capacity = jetpack.PowerCapacityMl;
            }
            else return false;

            space = Mathf.Max(0, capacity - stored);
            return capacity > 0;
        }

        public float AvailableChargeWatts(float dt)
        {
            if (!CanCharge) return 0f;
            if (dt <= 0.0001f) return 0f;
            float spaceWh = Mathf.Max(0f, capacityWh - storedWh);
            return Mathf.Min(maxChargeRate, spaceWh * 3600f / dt);
        }

        public float AvailableDischargeWatts(float dt)
        {
            if (!CanDischarge) return 0f;
            if (dt <= 0.0001f) return 0f;
            return Mathf.Min(maxDischargeRate, storedWh * 3600f / dt);
        }

        public void BeginPowerTick()
        {
            CurrentChargeWatts = 0f;
            CurrentDischargeWatts = 0f;
        }

        public float ChargeFromBus(float requestedWatts, float dt)
        {
            float acceptedWatts = Mathf.Min(Mathf.Max(0f, requestedWatts), AvailableChargeWatts(dt));
            if (acceptedWatts <= 0.01f) return 0f;

            storedWh = Mathf.Min(capacityWh, storedWh + acceptedWatts * dt / 3600f);
            CurrentChargeWatts += acceptedWatts;
            return acceptedWatts;
        }

        public float DischargeToBus(float requestedWatts, float dt)
        {
            float deliveredWatts = Mathf.Min(Mathf.Max(0f, requestedWatts), AvailableDischargeWatts(dt));
            if (deliveredWatts <= 0.01f) return 0f;

            storedWh = Mathf.Max(0f, storedWh - deliveredWatts * dt / 3600f);
            CurrentDischargeWatts += deliveredWatts;
            return deliveredWatts;
        }

        // -- IGridDataProvider -----------------------------------------
        public string SourceName => blockName;
        public string DataCategory => "Power";
        public string GetDisplayData()
        {
            return $"POWER\n{Fill01 * 100f:0}%\n{storedWh / 1000f:0.00}/{capacityWh / 1000f:0.00} kWh\n{mode}\n{TransferState}\nNet: {NetTransferWatts / 1000f:+0.0;-0.0;0.0} kW";
        }
    }
}
