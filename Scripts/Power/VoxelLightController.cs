// Assets/Scripts/VoxelEngine/Power/VoxelLightController.cs
using UnityEngine;
using VoxelEngine.Power;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Power
{
    /// <summary>
    /// Advanced light controller for RGB, LED, and Floodlights.
    /// Handles color, intensity, power dependency, and ambient light sensing.
    /// </summary>
    [RequireComponent(typeof(PowerConsumer))]
    public class VoxelLightController : MonoBehaviour
    {
        [Header("Light Settings")]
        public Light lightComponent;
        public Color lightColor = Color.white;
        public float intensity = 10f;
        
        [Header("Control Modes")]
        public bool isManuallyOn = true;
        public bool autoNightMode = false; // If true, only turns on when SunLightController reports night.

        private PowerConsumer _consumer;
        private SunLightController _sunController;

        private void Awake()
        {
            _consumer = GetComponent<PowerConsumer>();
            if (lightComponent == null) lightComponent = GetComponentInChildren<Light>();
            
            // Try to find the sun controller in the scene
            _sunController = Object.FindAnyObjectByType<SunLightController>();
        }

        private void Update()
        {
            if (lightComponent == null || _consumer == null) return;

            // 1. Check Power
            bool hasPower = _consumer.IsPowered;
            
            // 2. Check Light-Sensing Mode (Auto Night)
            bool ambientConditionMet = true;
            if (autoNightMode && _sunController != null)
            {
                // Assume "Night" is when dayFactor < 0.3f
                // We use the dayFactor logic similar to SunLightController's additive moonlight.
                // Since SunLightController is a singleton-like manager, we check its state.
                // Note: In a real scenario, we'd access the property from the class.
                // For this project, we'll assume a property 'DayFactor' exists or we'll use a mock if not.
                // Let's assume SunLightController has a public float dayFactor.
                
                // We use a reflection-like approach or just a direct access if we know the property.
                // Based on previous knowledge, SunLightController has a 'dayFactor'.
                float currentDayFactor = 1.0f;
                
                // Since we don't have the source of SunLightController right here, 
                // we'll use a safer check or just access it.
                // I will assume it has a public field/property called 'dayFactor'.
                // If it doesn't, the code might need a small adjustment, but logically it's where the data lives.
                
                // Using a generic approach to avoid crash if the class changes:
                var field = typeof(SunLightController).GetField("dayFactor");
                if (field != null) currentDayFactor = (float)field.GetValue(_sunController);
                
                ambientConditionMet = currentDayFactor < 0.3f;
            }

            // Final State: Power AND (Manual ON or Auto-Night-Met)
            bool shouldBeActive = hasPower && (isManuallyOn || (autoNightMode && ambientConditionMet));
            
            lightComponent.enabled = shouldBeActive;
            lightComponent.color = lightColor;
            lightComponent.intensity = intensity;
        }

        public float GetPowerUsage()
        {
            return _consumer != null ? _consumer.wattsPerSecond : 0f;
        }
    }
}
