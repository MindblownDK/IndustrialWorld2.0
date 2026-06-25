// Assets/Scripts/VoxelEngine/Cosmos/SphereGenParams.cs
using System;
using Unity.Mathematics;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Blittable POD bundle of every parameter the radial density generator needs. Passed by
    /// value into Burst jobs. Derived from <see cref="BodySettings"/> in Phase 2; for Phase 1
    /// it can be authored directly for isolated testing.
    /// </summary>
    [Serializable]
    public struct SphereGenParams
    {
        /// <summary>Per-body seed (randomised once at world creation, persisted thereafter).</summary>
        public int seed;

        /// <summary>Planet radius in world metres — the generator is radius-agnostic.</summary>
        public float radiusWorld;

        /// <summary>Mean terrain radius offset added to <see cref="radiusWorld"/> (metres).</summary>
        public float baseHeight;

        /// <summary>Absolute sea-level radius (metres). Below this + above terrain = water.</summary>
        public float seaRadius;

        /// <summary>Continent frequency in DIRECTION space (≈ radiusWorld / desiredWavelengthM).</summary>
        public float continentScaleDir;

        /// <summary>Mountain amplitude multiplier (scales every biome's heightAmplitude).</summary>
        public float mountainScale;

        /// <summary>Convenience: mean surface radius (no terrain noise).</summary>
        public float MeanSurfaceRadius => radiusWorld + baseHeight;
    }
}
