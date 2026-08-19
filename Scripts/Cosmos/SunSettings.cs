// Assets/Scripts/VoxelEngine/Cosmos/SunSettings.cs
using System;
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Properties of the central star(s) of a solar system. Per the design brief:
    /// sun strength, sun count, the glow colour, the water tint each sun casts,
    /// and the system name — all live here.
    /// </summary>
    [Serializable]
    public class SunSettings
    {
        [Tooltip("Display name shown when the system is selected / referenced.")]
        public string displayName = "Sol";

        [Range(1, 4)]
        [Tooltip("Number of stars in this system. Each contributes light + a glow.")]
        public int sunCount = 1;

        [Range(0f, 5f)]
        [Tooltip("Combined intensity/strength multiplier for all stars in the system.")]
        public float intensity = 1f;

        [Tooltip("Core glow / bloom tint rendered for the star(s).")]
        public Color glowColor = new Color(1f, 0.95f, 0.78f, 1f);

        [Tooltip("Tint applied to water volumes lit by this sun.")]
        public Color waterColor = new Color(0.09f, 0.34f, 0.55f, 0.85f);

        [Tooltip("Gravitational parameter μ of this star in km³/s². Drives the real Keplerian " +
                 "orbits of every planet (T = 2π√(a³/μ) and v = √(μ(2/r − 1/a))). 0 = safe default 180. " +
                 "Higher μ = faster orbits and stronger deep-space pull.")]
        public double gravitationalParameterKm3S2 = 180d;
    }
}
