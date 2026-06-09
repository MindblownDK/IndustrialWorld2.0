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

        public override float PowerOutput
        {
            get
            {
                if (Grid == null || storedWh <= 0) return 0;
                // Discharge when grid needs power.
                if (Grid.PowerBalance < 0)
                    return Mathf.Min(maxDischargeRate, -Grid.PowerBalance);
                return 0;
            }
        }

        private void Update()
        {
            if (Grid == null) return;

            // Charge when excess power.
            if (Grid.PowerBalance > 0 && storedWh < capacityWh)
            {
                float charge = Mathf.Min(Grid.PowerBalance, maxChargeRate) * Time.deltaTime / 3600f;
                storedWh = Mathf.Min(capacityWh, storedWh + charge);
            }

            // Discharge when deficit.
            if (Grid.PowerBalance < 0 && storedWh > 0)
            {
                float discharge = Mathf.Min(-Grid.PowerBalance, maxDischargeRate) * Time.deltaTime / 3600f;
                storedWh = Mathf.Max(0, storedWh - discharge);
            }
        }
    }
}
