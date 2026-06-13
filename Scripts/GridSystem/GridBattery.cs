// Assets/Scripts/VoxelEngine/GridSystem/GridBattery.cs
//
// Grid battery — stores and releases power for the ship grid.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridBattery : GridBlock
    {
        [Header("Battery")]
        public float capacityWh = 1000f;
        public float storedWh;
        public float maxChargeRate = 500f; // W
        public float maxDischargeRate = 500f; // W

        public float Fill01 => capacityWh > 0 ? Mathf.Clamp01(storedWh / capacityWh) : 0;

        /// <summary>Max watts this battery could supply RIGHT NOW (0 when empty/disabled).
        /// GridEntity.UpdatePower() sums these and uses them to cover any generation deficit,
        /// so the battery never lags a frame behind a new load.</summary>
        public float AvailableDischargeWatts => (Enabled && storedWh > 0f) ? maxDischargeRate : 0f;

        // Battery contribution is folded into the grid's generation by GridEntity (so it can be
        // capped to the actual deficit). Returning 0 here prevents double-counting.
        public override float PowerOutput => 0f;

        private void Update()
        {
            if (Grid == null || !Enabled) return;

            // GridEntity already folded battery discharge into PowerGenerated to meet demand.
            // So: if generation EXCEEDS consumption there's a true surplus → charge; otherwise
            // the batteries are carrying the deficit → drain by our share of it.
            float surplus = Grid.PowerGenerated - Grid.PowerConsumed;
            if (surplus > 0.01f && storedWh < capacityWh)
            {
                float charge = Mathf.Min(surplus, maxChargeRate) * Time.deltaTime / 3600f;
                storedWh = Mathf.Min(capacityWh, storedWh + charge);
            }
            else if (surplus <= 0.01f && storedWh > 0f)
            {
                // We're supplying up to our max discharge to keep the grid alive.
                float discharge = maxDischargeRate * Time.deltaTime / 3600f;
                storedWh = Mathf.Max(0, storedWh - discharge);
            }
        }
    }
}
