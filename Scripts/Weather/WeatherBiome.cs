// Assets/Scripts/VoxelEngine/Weather/WeatherBiome.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║      THE AIR OVER A BIOME — CLIMATE RESPONSE (17.34.0-dev)       ║
// ║                                                                  ║
// ║  17.33.0-dev made the ground vote: a band crossing a ridge        ║
// ║  lands harder on the windward face and shadows the lee. That      ║
// ║  answer was about SHAPE. It said nothing about what the ground    ║
// ║  is - a band over a rainforest and a band over a desert dropped   ║
// ║  exactly the same amount.                                        ║
// ║                                                                  ║
// ║  This file is the climate's answer. The humidity the planet's own ║
// ║  climate field reports at a place decides how much of a band      ║
// ║  reaches the ground there: moist air keeps its rain, dry air      ║
// ║  eats it on the way down (virga - the reason a desert can sit     ║
// ║  under a dark cloud and stay dry), and wet ground recycles its    ║
// ║  water back into the next band. The result is that the planet's   ║
// ║  bands finally agree with its biomes: the rainforest is wet       ║
// ║  because it rains there, and it rains there because it is wet.    ║
// ║                                                                  ║
// ║  The climate is read from SphereDensity.SampleClimate - the SAME  ║
// ║  call the scatter system uses to decide which biome grows at a    ║
// ║  place - so "the biome here" and "the air here" are one answer,   ║
// ║  not two systems that can drift apart.                            ║
// ║                                                                  ║
// ║  Like the terrain response, this is a MULTIPLIER on coverage:     ║
// ║  a clear spell stays clear over a desert, and the state machine   ║
// ║  is not consulted. It is symmetric around the climate field's     ║
// ║  own centre, so no planet gets wetter or drier on average.        ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// How the air over a biome changes how much of a band reaches the ground there.
    /// One pure function of the body's own climate field, memoised for the session.
    /// </summary>
    public static class WeatherBiome
    {
        // ── The response curve ───────────────────────────────────────────────────
        /// <summary>
        /// How far a fully saturated or fully arid climate moves the multiplier. The humidity
        /// field runs 0..1 and the response is centred on its own midpoint, so the multiplier
        /// is 1 at ordinary humidity, up to 1.20 in the wettest air and down to 0.80 in the
        /// driest - deliberately half of what terrain can do, because climate should tint the
        /// sky rather than overrule the land.
        /// </summary>
        public const float ClimateGain = 0.20f;

        /// <summary>Hard limits, at the extremes of the humidity field itself. Unlike the
        /// terrain clamps these are the mathematical envelope of the curve below, so they bind
        /// only at exactly 0 or 1 humidity and can never quietly flatten a region.</summary>
        public const float MinMultiplier = 1f - ClimateGain;
        public const float MaxMultiplier = 1f + ClimateGain;

        /// <summary>The humidity the curve is centred on: the climate field's own midpoint.
        /// `SampleClimate` returns `noise * 0.5 + 0.5` blended 38.5% with a latitude term whose
        /// area-weighted mean over a sphere is 0.5128 (`0.28 * sin(9) / 9 + 0.5`), so the
        /// field's mean humidity is 0.505 and a whole body's average precipitation moves by
        /// about +0.2% - the width of a rounding step. That is the property that lets this ship
        /// without a balance pass.</summary>
        public const float NeutralHumidity = 0.5f;

        /// <summary>Memo bucket, in metres of ground - the same granularity the terrain
        /// response uses, and far finer than the climate field (features thousands of metres
        /// across) needs.</summary>
        public const float CacheCellMetres = 150f;
        private const int CacheCapacity = 4096;

        // ── Memo (derived state, safe to discard) ────────────────────────────────
        private struct CacheKey : System.IEquatable<CacheKey>
        {
            public int seed;
            public int x;
            public int y;
            public int z;

            public bool Equals(CacheKey other)
                => seed == other.seed && x == other.x && y == other.y && z == other.z;

            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = seed;
                    h = (h * 397) ^ x;
                    h = (h * 397) ^ y;
                    h = (h * 397) ^ z;
                    return h;
                }
            }
        }

        private static readonly Dictionary<CacheKey, float> s_cache = new(CacheCapacity);

        /// <summary>
        /// The climate multiplier for a place on a body, 1 = unchanged.
        /// <paramref name="up"/> is the surface direction - the same vector the front field and
        /// the terrain response are asked about, so all three can never disagree about which
        /// square metre they are describing.
        /// </summary>
        public static float Multiplier(int seed, Vector3 up, float surfaceRadius)
        {
            if (up.sqrMagnitude < 1e-6f) return 1f;
            up.Normalize();

            CacheKey key = KeyFor(seed, up, surfaceRadius);
            if (s_cache.TryGetValue(key, out float memoised)) return memoised;

            float value = Evaluate(seed, up);

            if (s_cache.Count >= CacheCapacity) s_cache.Clear();
            s_cache[key] = value;
            return value;
        }

        /// <summary>Drops the memo. Only needed when the world underneath has genuinely
        /// changed; everything else is keyed by seed.</summary>
        public static void ClearCache() => s_cache.Clear();

        /// <summary>
        /// The humidity the climate field reports at a place, 0..1. Exposed because a readout
        /// may want to say "this is a wet place" in its own words; gameplay uses
        /// <see cref="Multiplier"/> instead, which already encodes the curve.
        /// </summary>
        public static float HumidityAt(int seed, Vector3 up)
        {
            up.Normalize();
            return SphereDensity.SampleClimate(seed, new float3(up.x, up.y, up.z)).y;
        }

        /// <summary>
        /// The three names the air can earn, in the same spirit as the band and terrain words.
        /// Thresholds sit where a player could actually notice the difference.
        /// </summary>
        public static string Word(float multiplier)
        {
            if (multiplier >= 1.05f) return "HUMID";
            if (multiplier <= 0.95f) return "DRY";
            return "TEMPERATE";
        }

        // ── The physics ──────────────────────────────────────────────────────────
        private static float Evaluate(int seed, Vector3 up)
        {
            float humidity = SphereDensity.SampleClimate(seed, new float3(up.x, up.y, up.z)).y;

            // Symmetric around the field's own midpoint: -1 in dead air, 0 at ordinary humidity,
            // +1 in saturated air. Scaling by 2 maps the whole 0..1 field onto -1..+1 so the
            // curve spans exactly ClimateGain either way.
            float moisture = Mathf.Clamp((humidity - NeutralHumidity) * 2f, -1f, 1f);

            return Mathf.Clamp(1f + ClimateGain * moisture, MinMultiplier, MaxMultiplier);
        }

        private static CacheKey KeyFor(int seed, Vector3 up, float surfaceRadius)
        {
            float radius = Mathf.Max(50f, surfaceRadius);
            float scale = radius / CacheCellMetres;
            return new CacheKey
            {
                seed = seed,
                x = Mathf.RoundToInt(up.x * scale),
                y = Mathf.RoundToInt(up.y * scale),
                z = Mathf.RoundToInt(up.z * scale),
            };
        }
    }
}
