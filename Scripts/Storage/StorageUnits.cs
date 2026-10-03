// Assets/Scripts/VoxelEngine/Storage/StorageUnits.cs
//
// 14.40.0 - One formatter for matter-data sizes. The base unit of the
// storage system is the GIGABYTE (1 GB = 1 kg of matter data), and every
// readout picks the right prefix instead of printing "540.000 GB":
//   0.25  -> "250 MB"      512    -> "512 GB"
//   1500  -> "1.50 TB"     2.3e6  -> "2.30 PB"

using UnityEngine;

namespace VoxelEngine.Storage
{
    public static class StorageUnits
    {
        /// <summary>Format a size given in GB with the best-fitting prefix.</summary>
        public static string Format(float gigabytes)
        {
            float abs = Mathf.Abs(gigabytes);
            if (abs < 0.0005f) return "0 GB";
            if (abs < 1f) return $"{gigabytes * 1000f:0} MB";
            if (abs < 1000f)
                return gigabytes < 10f ? $"{gigabytes:0.##} GB" : $"{gigabytes:0.#} GB";
            if (abs < 1_000_000f) return $"{gigabytes / 1000f:0.00} TB";
            return $"{gigabytes / 1_000_000f:0.00} PB";
        }

        /// <summary>"used / total" with independent prefixes.</summary>
        public static string FormatPair(float usedGb, float totalGb)
            => $"{Format(usedGb)} / {Format(totalGb)}";
    }
}
