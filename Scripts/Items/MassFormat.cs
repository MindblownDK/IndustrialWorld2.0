// Assets/Scripts/VoxelEngine/Items/MassFormat.cs
//
// Single source of truth for displaying masses. All masses in the game are
// stored in KILOGRAMS (kg). This helper auto-scales to the most readable unit:
//
//   < 1 kg            → grams   (g)
//   1 kg .. 999 kg    → kilos   (kg)
//   1 t .. 999 t      → tonnes  (t)      (1 t   = 1 000 kg)
//   >= 1 000 t        → kilotonnes (kt)  (1 kt  = 1 000 t = 1 000 000 kg)
//
// Use MassFormat.Format(kg) everywhere a mass is shown so units stay consistent.

using System.Globalization;

namespace VoxelEngine.Items
{
    public static class MassFormat
    {
        /// <summary>Format a kilogram value into the most human-readable unit string.</summary>
        public static string Format(float kg)
        {
            float abs = kg < 0 ? -kg : kg;

            if (abs < 1f)                 return Trim(kg * 1000f)   + " g";
            if (abs < 1_000f)             return Trim(kg)          + " kg";
            if (abs < 1_000_000f)         return Trim(kg / 1_000f) + " t";
            return Trim(kg / 1_000_000f) + " kt";
        }

        /// <summary>Format as "current / max" using a single shared unit chosen from the max.</summary>
        public static string FormatRatio(float currentKg, float maxKg)
        {
            // Pick the unit from the capacity so the pair reads cleanly (e.g. "12 / 100 t").
            (float div, string unit) = UnitFor(maxKg);
            return $"{Trim(currentKg / div)} / {Trim(maxKg / div)} {unit}";
        }

        private static (float div, string unit) UnitFor(float kg)
        {
            float abs = kg < 0 ? -kg : kg;
            if (abs < 1f)         return (0.001f,    "g");
            if (abs < 1_000f)     return (1f,        "kg");
            if (abs < 1_000_000f) return (1_000f,    "t");
            return (1_000_000f, "kt");
        }

        // Up to 2 decimals, no trailing zeros (e.g. 12, 12.5, 12.25).
        private static string Trim(float v)
            => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
