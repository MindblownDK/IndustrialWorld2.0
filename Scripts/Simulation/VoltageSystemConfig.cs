// Assets/Scripts/VoxelEngine/Simulation/VoltageSystemConfig.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — VOLTAGE SYSTEM CONFIGURATION                ║
// ║  Central ScriptableObject defining the LV / HV power threshold  ║
// ║  and conversion parameters. Referenced by all voltage-related   ║
// ║  blocks to keep balance values in one place.                    ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Simulation
{
    [CreateAssetMenu(menuName = "Voxel Engine/Simulation/Voltage System Config", fileName = "VoltageSystemConfig")]
    public class VoltageSystemConfig : ScriptableObject
    {
        [Header("Low Voltage / High Voltage Threshold")]
        [Tooltip("When total network wattage exceeds this value, the player must use a Step-Up Transformer and HV transmission lines. In watts.")]
        public float lvThresholdWatts = 25_000_000f; // 25 MW

        [Header("Conversion")]
        [Tooltip("Default conversion loss for step-up (LV → HV) transformers.")]
        [Range(0f, 0.1f)]
        public float defaultStepUpLoss = 0.02f;

        [Tooltip("Default conversion loss for step-down (HV → LV) transformers.")]
        [Range(0f, 0.1f)]
        public float defaultStepDownLoss = 0.02f;

        [Header("High Voltage")]
        [Tooltip("Maximum distance (m) an HV transmission line can span between towers.")]
        public float hvLineReach = 200f;

        [Tooltip("HV lines have unlimited power throughput.")]
        public float hvMaxThroughput = float.MaxValue;

        [Header("Low Voltage")]
        [Tooltip("Maximum distance (m) a standard LV wire can span between poles.")]
        public float lvWireReach = 15f;

        [Tooltip("Maximum connections per standard power pole.")]
        public int lvPoleMaxConnections = 6;

        [Header("UI")]
        [Tooltip("Colour used for LV indicators and UI elements.")]
        public Color lvColor = new(0.22f, 0.78f, 0.42f); // green

        [Tooltip("Colour used for HV indicators and UI elements.")]
        public Color hvColor = new(0.92f, 0.45f, 0.12f); // amber/orange

        [Tooltip("Colour used for Step-Up station accent.")]
        public Color stepUpAccent = new(0.15f, 0.45f, 0.85f); // blue

        [Tooltip("Colour used for Step-Down station accent.")]
        public Color stepDownAccent = new(0.92f, 0.60f, 0.12f); // amber

        // ── Singleton Access ──────────────────────────────────────────

        private static VoltageSystemConfig _instance;

        /// <summary>
        /// Returns the active config. Loads from Resources if not set.
        /// Falls back to a default instance if none exists.
        /// </summary>
        public static VoltageSystemConfig Active
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<VoltageSystemConfig>("VoltageSystemConfig");
                    if (_instance == null)
                    {
                        _instance = CreateInstance<VoltageSystemConfig>();
                        Debug.LogWarning("[VoltageSystem] No VoltageSystemConfig found in Resources. Using defaults.");
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Check if a given wattage requires HV transmission.
        /// </summary>
        public bool RequiresHighVoltage(float watts) => watts > lvThresholdWatts;

        /// <summary>
        /// Format wattage for display with appropriate suffix.
        /// </summary>
        public static string FormatWatts(float watts)
        {
            if (watts >= 1_000_000_000f) return $"{watts / 1_000_000_000f:F1} GW";
            if (watts >= 1_000_000f) return $"{watts / 1_000_000f:F1} MW";
            if (watts >= 1_000f) return $"{watts / 1_000f:F1} kW";
            return $"{watts:F0} W";
        }
    }
}
