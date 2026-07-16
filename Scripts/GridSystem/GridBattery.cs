// Assets/Scripts/VoxelEngine/GridSystem/GridBattery.cs
//
// Grid battery — stores and releases power for the ship grid.
// v5.43.0-dev — Implements IGridDataProvider for screen display.

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

        public float Fill01 => capacityWh > 0 ? Mathf.Clamp01(storedWh / capacityWh) : 0;

        public float AvailableDischargeWatts =>
            (Enabled && mode != GridBatteryMode.Recharge && storedWh > 0f) ? maxDischargeRate : 0f;

        public override float PowerOutput => 0f;

        private void Update()
        {
            if (Grid == null || !Enabled) return;

            float surplus = Grid.PowerGenerated - Grid.PowerConsumed;
            bool canCharge = mode == GridBatteryMode.Auto || mode == GridBatteryMode.Recharge;
            bool canDischarge = mode == GridBatteryMode.Auto || mode == GridBatteryMode.Discharge;
            bool gridHasLoad = Grid.PowerConsumed > 0.01f;

            if (canCharge && surplus > 0.01f && storedWh < capacityWh)
            {
                float charge = Mathf.Min(surplus, maxChargeRate) * Time.deltaTime / 3600f;
                storedWh = Mathf.Min(capacityWh, storedWh + charge);
            }
            else if (canDischarge && gridHasLoad && surplus <= 0.01f && storedWh > 0f)
            {
                float discharge = maxDischargeRate * Time.deltaTime / 3600f;
                storedWh = Mathf.Max(0, storedWh - discharge);
            }
        }

        // -- IGridDataProvider -----------------------------------------
        public string SourceName => blockName;
        public string DataCategory => "Power";
        public string GetDisplayData()
        {
            return $"POWER\n{Fill01 * 100f:0}%\n{storedWh / 1000f:0.00}/{capacityWh / 1000f:0.00} kWh\n{mode}\nRate: {maxDischargeRate / 1000f:0.0} kW";
        }
    }
}
