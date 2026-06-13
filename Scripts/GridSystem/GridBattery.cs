// Assets/Scripts/VoxelEngine/GridSystem/GridBattery.cs
//
// Grid battery — stores and releases power for the ship grid.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public enum GridBatteryMode
    {
        Auto,
        Recharge,
        Discharge
    }

    public class GridBattery : GridBlock
    {
        [Header("Battery")]
        public float capacityWh = 1000f;
        public float storedWh;
        public float maxChargeRate = 500f; // W
        public float maxDischargeRate = 500f; // W
        public GridBatteryMode mode = GridBatteryMode.Auto;

        public float Fill01 => capacityWh > 0 ? Mathf.Clamp01(storedWh / capacityWh) : 0;

        /// <summary>Max watts this battery could supply RIGHT NOW (0 when empty/disabled/recharging).
        /// GridEntity.UpdatePower() sums these and uses them to cover any generation deficit,
        /// so the battery never lags a frame behind a new load.</summary>
        public float AvailableDischargeWatts =>
            (Enabled && mode != GridBatteryMode.Recharge && storedWh > 0f) ? maxDischargeRate : 0f;

        // Battery contribution is folded into the grid's generation by GridEntity (so it can be
        // capped to the actual deficit). Returning 0 here prevents double-counting.
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
                // Drain only while the grid actually has load. This prevents idle ships from
                // slowly emptying batteries just because generated/consumed power are both zero.
                float discharge = maxDischargeRate * Time.deltaTime / 3600f;
                storedWh = Mathf.Max(0, storedWh - discharge);
            }
        }
    }
}
