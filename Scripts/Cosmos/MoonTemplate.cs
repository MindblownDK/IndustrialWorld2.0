// Assets/Scripts/VoxelEngine/Cosmos/MoonTemplate.cs
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Authoring asset for a moon. Right-click in Project ▸
    /// Create ▸ Voxel Engine ▸ Planets ▸ Moon.
    ///
    /// A moon is a body that orbits a <see cref="PlanetTemplate"/>. Multiple moons may share
    /// a planet; the runtime generator assigns each a strictly-increasing orbit radius and an
    /// evenly-spread phase so their circular orbits can never intersect.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Planets/Moon", fileName = "Moon_")]
    public class MoonTemplate : ScriptableObject
    {
        [Header("Body")]
        [Tooltip("Defaults to an airless, low-gravity, grassless body.")]
        public BodySettings body = new BodySettings
        {
            bodyName      = "Moon",
            gravity       = 0.16f,
            oxygenLevel   = 0f,
            temperature   = -20f,
            enableGrass   = false,
            windStrength  = 0f,
            radiusKm      = 1.5f,
            waterLevel    = 0,
        };

        [Header("Orbit")]
        [Tooltip("Planet this moon orbits.")]
        public PlanetTemplate orbitsPlanet;

        [Tooltip("Min/max orbital radius (km) around its planet.")]
        public Vector2 orbitRadiusKm = new Vector2(80f, 400f);

        [Range(0f, 5f)]
        [Tooltip("Orbital speed multiplier.")]
        public float orbitSpeed = 1f;

        [Range(0f, 360f)]
        [Tooltip("Starting orbital phase (deg). Auto-spread among sibling moons if left at 0.")]
        public float orbitPhaseDegrees = 0f;
    }
}
