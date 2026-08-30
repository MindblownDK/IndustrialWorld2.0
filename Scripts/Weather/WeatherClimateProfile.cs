// Assets/Scripts/VoxelEngine/Weather/WeatherClimateProfile.cs
//
// Per-celestial-body weather personality. Lives on BodySettings so every planet
// and moon can describe its own climate: whether it has weather at all, what falls
// from its sky, how often storms roll in, and how violently they hit.
//
// This is additive data — existing planet/moon assets deserialize with the safe
// Earth-like default, and an atmosphere check at runtime auto-disables weather on
// airless bodies regardless of this toggle. The setup step (Step 58) only ever
// authors non-default values when profileVersion == 0, so hand-tuned worlds are
// never overwritten.

using System;
using UnityEngine;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// Serializable climate profile embedded in <see cref="VoxelEngine.Cosmos.BodySettings"/>.
    /// Drives the <see cref="WeatherManager"/> state machine and visual intensity per body.
    /// </summary>
    [Serializable]
    public class WeatherClimateProfile
    {
        public enum Precipitation
        {
            /// <summary>Driven by the local biome temperature (rain in warm biomes, snow in cold).</summary>
            Auto = 0,
            /// <summary>Always rain on this body (temperate / ocean worlds).</summary>
            Rain = 1,
            /// <summary>Always snow on this body (tundra / ice worlds).</summary>
            Snow = 2,
            /// <summary>No precipitation — wind & overcast only (desert / ash worlds).</summary>
            None = 3
        }

        [Tooltip("Master weather toggle. Automatically forced off at runtime when the body has no atmosphere.")]
        public bool weatherEnabled = true;

        [Tooltip("What falls from the sky. Auto = driven by local biome temperature.")]
        public Precipitation precipitation = Precipitation.Auto;

        [Range(0f, 1f)]
        [Tooltip("Baseline cloudiness / overcast tendency. Higher = gloomier average weather.")]
        public float overcastBias = 0.45f;

        [Range(0f, 1f)]
        [Tooltip("Chance a weather cycle escalates into a storm (heavy rain / blizzard).")]
        public float stormChance = 0.35f;

        [Range(0f, 0.95f)]
        [Tooltip("How aggressively a storm darkens the sun. 0 = no darkening, 0.8 = near-twilight.")]
        public float stormDarkening = 0.6f;

        [Range(0f, 2f)]
        [Tooltip("Multiplier applied to the body wind strength during storms.")]
        public float stormWindMultiplier = 1.6f;

        [Range(0f, 2f)]
        [Tooltip("Multiplier on weather-driven fog density (rain haze, blizzard whiteout).")]
        public float stormFogScale = 1.0f;

        [Range(0.1f, 1f)]
        [Tooltip("Sun-intensity floor during the worst weather — keeps storms readable, not pitch black.")]
        public float stormLightFloor = 0.18f;

        [Range(0f, 1f)]
        [Tooltip("How often thunder strikes during a storm. 0 = silent storms, 1 = frequent thunder.")]
        public float thunderFrequency = 0.6f;

        /// <summary>Authored by Step 58. Zero identifies assets that predate this profile; their safe defaults are kept.</summary>
        [HideInInspector] public int profileVersion = 0;

        // ── Curated presets (used by Step 58 to author themed worlds non-destructively) ──

        /// <summary>Default Earth-like temperate profile (also the deserialization default).</summary>
        public static WeatherClimateProfile Default() => new WeatherClimateProfile
        {
            weatherEnabled     = true,
            precipitation      = Precipitation.Auto,
            overcastBias       = 0.45f,
            stormChance        = 0.35f,
            stormDarkening     = 0.6f,
            stormWindMultiplier = 1.6f,
            stormFogScale      = 1.0f,
            stormLightFloor    = 0.18f,
            thunderFrequency   = 0.6f,
            profileVersion     = 0,
        };

        /// <summary>Hot desert world — wind & dust, no rain, rare sandstorm-grade winds.</summary>
        public static WeatherClimateProfile Desert() => new WeatherClimateProfile
        {
            weatherEnabled     = true,
            precipitation      = Precipitation.None,
            overcastBias       = 0.1f,
            stormChance        = 0.2f,
            stormDarkening     = 0.35f,
            stormWindMultiplier = 2.2f,
            stormFogScale      = 0.7f,
            stormLightFloor    = 0.4f,
            thunderFrequency   = 0.0f,
            profileVersion     = 0,
        };

        /// <summary>Frozen tundra / ice world — snow and blizzards.</summary>
        public static WeatherClimateProfile Tundra() => new WeatherClimateProfile
        {
            weatherEnabled     = true,
            precipitation      = Precipitation.Snow,
            overcastBias       = 0.5f,
            stormChance        = 0.45f,
            stormDarkening     = 0.4f,
            stormWindMultiplier = 1.8f,
            stormFogScale      = 1.3f,
            stormLightFloor    = 0.25f,
            thunderFrequency   = 0.1f,
            profileVersion     = 0,
        };

        /// <summary>Stormy ocean world — frequent heavy rain and thunder.</summary>
        public static WeatherClimateProfile Ocean() => new WeatherClimateProfile
        {
            weatherEnabled     = true,
            precipitation      = Precipitation.Rain,
            overcastBias       = 0.65f,
            stormChance        = 0.55f,
            stormDarkening     = 0.7f,
            stormWindMultiplier = 1.7f,
            stormFogScale      = 1.2f,
            stormLightFloor    = 0.16f,
            thunderFrequency   = 0.85f,
            profileVersion     = 0,
        };

        /// <summary>Airless / vacuum body — no weather at all.</summary>
        public static WeatherClimateProfile Airless() => new WeatherClimateProfile
        {
            weatherEnabled     = false,
            precipitation      = Precipitation.None,
            overcastBias       = 0f,
            stormChance        = 0f,
            stormDarkening     = 0f,
            stormWindMultiplier = 1f,
            stormFogScale      = 0f,
            stormLightFloor    = 1f,
            thunderFrequency   = 0f,
            profileVersion     = 0,
        };
    }
}
