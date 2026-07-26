// Assets/Scripts/VoxelEngine/GridSystem/GridBattery.cs
//
// Grid battery — stores and releases power for the ship grid.
// v6.9.0-dev — transfer is now coordinated centrally by GridEntity so batteries
// can charge/discharge each other reliably and explicit modes behave deterministically.

using UnityEngine;

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
