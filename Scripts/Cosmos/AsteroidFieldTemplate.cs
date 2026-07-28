// Assets/Scripts/VoxelEngine/Cosmos/AsteroidFieldTemplate.cs
using System;
using UnityEngine;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Deep-space asteroid field parameters. Resources are capped at 0–5 per the design
    /// brief; any asteroid that rolls no resource defaults to plain stone.
    /// </summary>
    [Serializable]
    public class AsteroidFieldSettings
    {
        [Range(0, 5)]
        [Tooltip("How many distinct resource types may spawn in this field. 0 = pure stone.")]
        public int resourceCount = 2;

        [Tooltip("Pool of materials asteroids can be made of. A random subset (resourceCount) is picked per system seed.")]
        public MaterialId[] possibleResources =
        {
            MaterialId.Iron, MaterialId.Nickel, MaterialId.Cobalt,
            MaterialId.Silicon, MaterialId.Platinum, MaterialId.Ice,
        };

        [Range(0f, 5f)]
        [Tooltip("Spatial density multiplier — higher = more asteroids per unit volume.")]
        public float density = 1f;

        [Tooltip("Min/max asteroid diameter in km.")]
        public Vector2 sizeRangeKm = new Vector2(0.05f, 1.2f);

        [Tooltip("Inner/outer radius of the field's shell around the system star (km).")]
        public Vector2 shellRadiusKm = new Vector2(8000f, 12000f);
    }

    /// <summary>
    /// ScriptableObject authoring asset for an asteroid field.
    /// Right-click in Project ▸ Create ▸ Voxel Engine ▸ Planets ▸ Asteroid Field.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Planets/Asteroid Field", fileName = "Asteroids_")]
    public class AsteroidFieldTemplate : ScriptableObject
    {
        [Header("Field")]
        public AsteroidFieldSettings settings = new AsteroidFieldSettings();

        [Header("Ownership")]
        [Tooltip("Solar system this field drifts in. Leave null for a free-floating deep-space field.")]
        public SolarSystemTemplate solarSystem;
    }
}
