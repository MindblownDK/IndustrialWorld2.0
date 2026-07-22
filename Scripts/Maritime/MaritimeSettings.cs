// Assets/Scripts/VoxelEngine/Maritime/MaritimeSettings.cs
//
// Central tuning ScriptableObject for the maritime propulsion simulation.
// One shared asset (created by the setup wizard) so designers can balance the
// whole watercraft feel in one inspector without touching code.

using UnityEngine;

namespace VoxelEngine.Maritime
{
    [CreateAssetMenu(menuName = "Voxel Engine/Maritime/Maritime Settings", fileName = "MaritimeSettings")]
    public class MaritimeSettings : ScriptableObject
    {
        [Header("Buoyancy")]
        [Tooltip("Water density (kg/m³). Drives Archimedes displacement force. Sea water ≈ 1025.")]
        public float waterDensity = 1025f;
        [Tooltip("Global multiplier on buoyancy force so ships feel responsive at game scale.")]
        public float buoyancyGain = 1.0f;
        [Tooltip("Extra displacement reserve per block. 1.6 means a buoyancyFactor=1 block floats around 62% submerged instead of only at full submergence.")]
        public float buoyancyReserve = 1.6f;
        [Tooltip("Downward drag applied to blocks moving through water (hull resistance).")]
        public float waterDrag = 0.6f;
        [Tooltip("Upward stabilisation gain — gently pushes submerged hulls toward equilibrium.")]
        public float stabiliserGain = 1.5f;

        [Header("Propulsion — Propellers")]
        [Tooltip("Thrust = RPM × Submergence × PropellerSize × ThrustCoefficient.")]
        public float thrustCoefficient = 0.18f;
        [Tooltip("Cavitation: thrust is reduced when a large propeller turns fast in shallow water.")]
        [Range(0f, 1f)] public float cavitationLoss = 0.35f;

        [Header("Propulsion — Waterwheel")]
        [Tooltip("Torque a stationary waterwheel extracts from flow (per m/s of current).")]
        public float wheelFlowTorque = 12000f;
        [Tooltip("Thrust a shaft-driven waterwheel produces as paddle thrust.")]
        public float wheelPaddleThrust = 0.10f;

        [Header("Propulsion — Engine RPM")]
        [Tooltip("Fraction of MaxRPM an engine actually delivers per unit fuel authority (smoothing).")]
        [Range(0.1f, 2f)] public float rpmResponse = 1.0f;
        [Tooltip("Generator efficiency converting shaft power (torque·ω) into electricity (W).")]
        [Range(0f, 1f)] public float generatorEfficiency = 0.85f;
        [Tooltip("Bonus electrical output a generator makes at its rated RPM. 0.5 = up to +50% more power at full rated speed.")]
        [Range(0f, 1f)] public float generatorSpeedBonus = 0.5f;

        [Header("Gearbox")]
        [Tooltip("Absolute RPM clamp applied to every gearbox output (safety).")]
        public float globalGearSpeedCap = 4000f;

        [Header("Physics Integration")]
        [Tooltip("How strongly the resultant force is applied (kept at 1 for real Newtons).")]
        public float forceGain = 1.0f;
        [Tooltip("How strongly the resultant torque is applied.")]
        public float torqueGain = 1.0f;

        [Header("Steering (Helm)")]
        [Tooltip("Rudder effectiveness — yaw torque per (forward speed · steer input).")]
        public float rudderTorque = 8000f;
        [Tooltip("Minimum forward speed (m/s) before the rudder has any authority.")]
        public float rudderMinSpeed = 0.5f;

        /// <summary>Fallback instance used when no asset is assigned (keeps the system running).</summary>
        private static MaritimeSettings _default;
        public static MaritimeSettings Default
        {
            get
            {
                if (_default == null)
                {
                    _default = CreateInstance<MaritimeSettings>();
                    _default.name = "MaritimeSettings (Runtime Default)";
                }
                return _default;
            }
        }
    }
}
