// Assets/Scripts/VoxelEngine/Weather/StaticSeasonMonitor.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                 STATIC PLANETARY SEASON MONITOR                      ║
// ║                                                                      ║
// ║  Grand ground-placed planetary observatory & climate tracking        ║
// ║  station. Features an animated rotating Doppler meteorological dish, ║
// ║  emissive telemetry glass terminal, and full seasonal climate        ║
// ║  telemetry for the local world or remote celestial targets.          ║
// ║  Requires 400 W of base electrical power to operate.                ║
// ╚══════════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Power;
using VoxelEngine.Simulation;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// Ground-placed planetary observatory and seasonal climate telemetry station.
    /// Right-clicking opens the high-tech observatory season tracking panel.
    /// Requires 400 W of base electrical power.
    /// </summary>
    [RequireComponent(typeof(PowerConsumer))]
    public class StaticSeasonMonitor : MonoBehaviour, IPowerConsumer
    {
        public enum MonitorMode
        {
            AutoCurrentPlanet = 0,
            SpecificPlanet = 1
        }

        [Header("Observatory Configuration")]
        [Tooltip("Operating power draw in Watts (nominal: 400 W).")]
        public float powerDrawWatts = 400f;

        [Tooltip("Target selection mode: automatically track local world or lock onto a specific celestial body.")]
        public MonitorMode mode = MonitorMode.AutoCurrentPlanet;

        [Tooltip("Specific body name to track when in SpecificPlanet mode.")]
        public string specificBodyName = "Home Planet";

        [Tooltip("Rotation speed of the top meteorological dish in deg/s.")]
        public float dishRotationSpeed = 30f;

        [Header("Telemetry Asset")]
        [Tooltip("Optional ScriptableObject screen data object binding.")]
        public PlanetSeasonData screenDataObject;

        // Visual animation & power references
        private Transform _dishTransform;
        private Light _statusIndicatorLight;
        private PowerConsumer _powerConsumer;

        // ── IPowerConsumer Implementation ────────────────────────────
        public bool IsPowered => _powerConsumer != null && _powerConsumer.IsPowered;

        public float WattsPerSecond
        {
            get => powerDrawWatts;
            set
            {
                powerDrawWatts = value;
                if (_powerConsumer != null) _powerConsumer.wattsPerSecond = value;
            }
        }

        public bool IsOnline => enabled && gameObject.activeInHierarchy && IsPowered;

        public string TargetName => mode switch
        {
            MonitorMode.SpecificPlanet when !string.IsNullOrEmpty(specificBodyName) => specificBodyName,
            _ => (GravityProvider.ActiveBody != null && !string.IsNullOrEmpty(GravityProvider.ActiveBody.DisplayName))
                    ? GravityProvider.ActiveBody.DisplayName
                    : "Local Planet"
        };

        private void Awake()
        {
            EnsurePowerComponent();
            FindVisualReferences();
        }

        private void Start()
        {
            EnsurePowerComponent();
        }

        private void Update()
        {
            bool powered = IsPowered;

            // Animate meteorological Doppler dish only when actively powered
            if (_dishTransform != null)
            {
                if (powered && dishRotationSpeed > 0f)
                {
                    _dishTransform.Rotate(Vector3.up, dishRotationSpeed * Time.deltaTime, Space.Self);
                }
            }

            // Beacon indicator light shines when powered
            if (_statusIndicatorLight != null)
            {
                if (_statusIndicatorLight.enabled != powered)
                    _statusIndicatorLight.enabled = powered;
            }
        }

        private void EnsurePowerComponent()
        {
            if (_powerConsumer == null)
            {
                _powerConsumer = GetComponent<PowerConsumer>();
                if (_powerConsumer == null)
                    _powerConsumer = gameObject.AddComponent<PowerConsumer>();
            }

            if (_powerConsumer != null)
            {
                _powerConsumer.wattsPerSecond = powerDrawWatts;
                _powerConsumer.connectRadius = 4.0f;
            }
        }

        private void FindVisualReferences()
        {
            _dishTransform = transform.Find("MeteorologicalDish")
                          ?? transform.Find("Dish")
                          ?? transform.Find("Generated_Visuals/MeteorologicalDish");
            _statusIndicatorLight = GetComponentInChildren<Light>();
        }

        /// <summary>
        /// Query the current seasonal telemetry snapshot for the active target.
        /// </summary>
        public PlanetSeasonInfo GetSeasonInfo()
        {
            return mode switch
            {
                MonitorMode.SpecificPlanet when !string.IsNullOrEmpty(specificBodyName) => PlanetarySeasons.GetSeasonInfo(specificBodyName),
                _ => PlanetarySeasons.GetCurrentSeasonInfo()
            };
        }

        /// <summary>
        /// Cycle through available celestial bodies in the cosmic registry.
        /// </summary>
        public void CycleTarget(int direction)
        {
            var names = PlanetarySeasons.GetAllMonitoredBodyNames();
            if (names == null || names.Count == 0) return;

            int currentIdx = -1;
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], specificBodyName, StringComparison.OrdinalIgnoreCase))
                {
                    currentIdx = i;
                    break;
                }
            }

            if (currentIdx < 0) currentIdx = 0;
            int nextIdx = (currentIdx + direction) % names.Count;
            if (nextIdx < 0) nextIdx += names.Count;

            specificBodyName = names[nextIdx];
            mode = MonitorMode.SpecificPlanet;

            if (screenDataObject != null)
            {
                screenDataObject.targetMode = PlanetSeasonData.TargetPlanetMode.SpecifiedPlanetName;
                screenDataObject.targetBodyName = specificBodyName;
            }
        }
    }
}
