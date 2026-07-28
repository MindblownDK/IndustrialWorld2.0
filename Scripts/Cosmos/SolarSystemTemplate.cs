// Assets/Scripts/VoxelEngine/Cosmos/SolarSystemTemplate.cs
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Authoring asset for a whole solar system. Right-click in Project ▸
    /// Create ▸ Voxel Engine ▸ Planets ▸ Solar System.
    ///
    /// Holds the system name, its sun(s), the planet-separation range (500–10000 km),
    /// the planets that belong to it, any asteroid fields, and the background quasar.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Planets/Solar System", fileName = "System_")]
    public class SolarSystemTemplate : ScriptableObject
    {
        [Header("Identity")]
        public string systemName = "Sol System";

        [Header("Star(s)")]
        public SunSettings sun = new SunSettings();

        [Header("Planet Layout")]
        [Tooltip("Minimum distance (km) between any two planets in this system.")]
        public float minPlanetSeparationKm = 500f;

        [Tooltip("Maximum distance (km) between any two planets in this system.")]
        public float maxPlanetSeparationKm = 10000f;

        [Header("Bodies")]
        [Tooltip("Planets in this system. Order is orbital order (innermost first).")]
        public PlanetTemplate[] planets;

        [Header("Deep Space")]
        [Tooltip("Asteroid fields that drift in this system's space.")]
        public AsteroidFieldTemplate[] asteroidFields;

        [Header("Aesthetics")]
        [Tooltip("Background quasar pinned to the system's deep-space skybox.")]
        public QuasarSettings quasar = new QuasarSettings();
    }
}
