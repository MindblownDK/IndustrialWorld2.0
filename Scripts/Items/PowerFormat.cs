// Assets/Scripts/VoxelEngine/Items/PowerFormat.cs
//
// Formats power (watts) into the most readable unit, mirroring MassFormat:
//   < 1000 W       → W
//   1 kW .. 999 kW → kW    (1 kW = 1 000 W)
//   1 MW .. 999 MW → MW    (1 MW = 1 000 000 W)
//   >= 1 GW        → GW
//
// Also handles energy (watt-hours): Wh / kWh / MWh / GWh.

using System.Globalization;

namespace VoxelEngine.Items
{
    public static class PowerFormat
    {
        public static string Watts(float w) => Scale(w, "W");
        public static string WattHours(float wh) => Scale(wh, "Wh");
        /// <summary>Force in newtons: N / kN / MN.</summary>
        public static string Newtons(float n) => Scale(n, "N");

        private static string Scale(float v, string unit)
        {
            float abs = v < 0 ? -v : v;
            if (abs < 1_000f)             return Trim(v)             + " "  + unit;
            if (abs < 1_000_000f)         return Trim(v / 1_000f)    + " k" + unit;
            if (abs < 1_000_000_000f)     return Trim(v / 1_000_000f)+ " M" + unit;
            return Trim(v / 1_000_000_000f) + " G" + unit;
        }

        private static string Trim(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
