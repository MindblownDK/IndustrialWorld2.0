// Assets/Scripts/VoxelEngine/Cosmos/PlanetTemplate.cs
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Authoring asset for a planet. Right-click in Project ▸
    /// Create ▸ Voxel Engine ▸ Planets ▸ Planet.
    ///
    /// A planet is a body that orbits a star (never another planet). It is assigned to a
    /// <see cref="SolarSystemTemplate"/>; the runtime generator scatters its planets
    /// randomly so every pair sits 500–10000 km apart.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Planets/Planet", fileName = "Planet_")]
    public class PlanetTemplate : ScriptableObject
    {
        [Header("Body")]
        public BodySettings body = BodySettings.CreateEarthlike();

        [Header("Orbit")]
        [Tooltip("Solar system this planet belongs to.")]
        public SolarSystemTemplate solarSystem;

        [Tooltip("Fixed distance (km) from the sun. If 0, uses the orbitalDistanceKm range.")]
        public float distanceFromSun = 0f;

        [Tooltip("Min/max distance (km) the planet may sit from its star. Final value is seeded per world.")]
        public Vector2 orbitalDistanceKm = new Vector2(1500f, 6000f);

        [Range(0f, 5f)]
        [Tooltip("Orbital speed multiplier.")]
        public float orbitSpeed = 1f;

        [Range(0f, 360f)]
        [Tooltip("Starting orbital phase (deg). Ignored if the generator auto-distributes phases.")]
        public float orbitPhaseDegrees = 0f;

        [Header("Moons")]
        [Tooltip("Moons that orbit this planet. The generator guarantees sibling moons never collide " +
                 "(distinct orbit radii, evenly spread phases).")]
        public MoonTemplate[] moons;
    }
}
